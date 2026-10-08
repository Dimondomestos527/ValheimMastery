using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Approved trust boundary: vanilla character progression belongs to its owner.
    // This synchronizes ONLY that owner's Crafting/Spears levels. It grants neither
    // chest access nor permission to damage/move arbitrary entities.
    internal static class OwnerSkillAuthority
    {
        private sealed class Snapshot
        {
            internal long PlayerId;
            internal ZDOID CharacterId;
            internal float Crafting, Spears, Unarmed, Ride, Received;
            internal float Elemental, Blood, MagicReceived = -100f;
        }
        private static readonly Dictionary<ZRpc, Snapshot> Snapshots = new Dictionary<ZRpc, Snapshot>();
        private sealed class CrossbowSnapshot
        {
            internal long PlayerId;
            internal ZDOID CharacterId;
            internal float Level, Received;
        }
        // Independent channel: Receive(V2) replaces its Snapshot every two seconds.
        private static readonly Dictionary<ZRpc, CrossbowSnapshot> CrossbowSnapshots = new Dictionary<ZRpc, CrossbowSnapshot>();
        private sealed class PolearmsSnapshot
        {
            internal ZNet Session;
            internal ZNetPeer Peer;
            internal long World, PlayerId;
            internal ZDOID CharacterId;
            internal float Level, Received;
        }
        private static readonly Dictionary<ZRpc, PolearmsSnapshot> PolearmsSnapshots = new Dictionary<ZRpc, PolearmsSnapshot>();
        private static long _polearmsWorld;
        private static float _nextSend;
        private static float _nextPrune;
        private static ZNet _session;

        internal static void Register(ZRpc rpc)
        {
            rpc.Register<float, float, float, float>("VM_OwnerSkillsV2", Receive);
            rpc.Register<float>("VM_OwnerCrossbowsV1", ReceiveCrossbows);
            rpc.Register<long, ZDOID, float>("VM_OwnerPolearmsV1", ReceivePolearms);
#if MASTERY_MAGIC70_EXPERIMENT
            rpc.Register<float, float>("VM_OwnerMagicV1", ReceiveMagic);
#endif
        }
        // Identity fields only bind delayed delivery; authority still comes from native RPC/peer/ZDO ownership.
        private static void ReceivePolearms(ZRpc rpc, long world, ZDOID reportedCharacter, float level)
        {
            if (rpc == null) return;
            PolearmsSnapshots.Remove(rpc); // A malformed/lower report must not retain an earlier eligible level.
            ZNet net = ZNet.instance;
            if (net?.IsServer() != true || net.GetWorld() == null || world != net.GetWorldUID() || !Valid(level)) return;
            ZNetPeer peer = net.GetPeer(rpc); ZDO character = ResolveCharacterData(peer);
            long playerId = character?.GetLong(ZDOVars.s_playerID, 0) ?? 0;
            if (playerId == 0 || character.m_uid != reportedCharacter || !FiniteTime(Time.time)) return;
            PolearmsSnapshots[rpc] = new PolearmsSnapshot { Session = net, World = world, Peer = peer,
                PlayerId = playerId, CharacterId = character.m_uid, Level = level, Received = Time.time };
        }
        private static bool FiniteTime(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float PolearmsLevel(ZRpc rpc, ZDOID characterId, long playerId)
        {
            ZNet net = ZNet.instance;
            if (rpc == null || net?.IsServer() != true || net.GetWorld() == null || playerId == 0 ||
                !PolearmsSnapshots.TryGetValue(rpc, out var snapshot) || !ReferenceEquals(snapshot.Session, net) ||
                snapshot.World != net.GetWorldUID() || !ReferenceEquals(snapshot.Peer, net.GetPeer(rpc))) return 0f;
            ZDO character = ResolveCharacterData(snapshot.Peer);
            float age = Time.time - snapshot.Received;
            if (character == null || character.m_uid != characterId || snapshot.CharacterId != characterId ||
                snapshot.PlayerId != playerId || character.GetLong(ZDOVars.s_playerID, 0) != playerId ||
                !Valid(snapshot.Level) || !FiniteTime(age) || age < 0f || age > 10f) return 0f;
            return snapshot.Level;
        }
        private static float LocalPolearmsLevel(Player player)
        {
            if (player == null || player != Player.m_localPlayer || ZNet.instance?.GetWorld() == null ||
                player.m_nview?.IsOwner() != true || player.m_nview.GetZDO() == null || player.GetPlayerID() == 0 ||
                player.m_nview.GetZDO().GetLong(ZDOVars.s_playerID, 0) != player.GetPlayerID()) return 0f;
            float level = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Polearms);
            return Valid(level) ? level : 0f;
        }
        private static void ReceiveCrossbows(ZRpc rpc, float level)
        {
            if (ZNet.instance?.IsServer() != true || !Valid(level)) return;
            ZDO character = ResolveCharacterData(ZNet.instance.GetPeer(rpc));
            long playerId = character?.GetLong(ZDOVars.s_playerID, 0) ?? 0;
            if (playerId == 0) return;
            CrossbowSnapshots[rpc] = new CrossbowSnapshot {
                PlayerId = playerId, CharacterId = character.m_uid, Level = level, Received = Time.time
            };
        }
        private static float CrossbowLevel(ZRpc rpc, ZDOID characterId, long playerId)
        {
            ZDO character = ResolveCharacterData(ZNet.instance?.GetPeer(rpc));
            if (character == null || character.m_uid != characterId || playerId == 0 ||
                character.GetLong(ZDOVars.s_playerID, 0) != playerId ||
                !CrossbowSnapshots.TryGetValue(rpc, out var snapshot) ||
                snapshot.CharacterId != characterId || snapshot.PlayerId != playerId ||
                Time.time - snapshot.Received < 0f || Time.time - snapshot.Received > 10f) return 0f;
            return snapshot.Level;
        }
        private static void ReceiveMagic(ZRpc rpc, float elemental, float blood)
        {
            if (ZNet.instance?.IsServer() != true || !Valid(elemental) || !Valid(blood) ||
                !Snapshots.TryGetValue(rpc, out Snapshot snapshot)) return;
            ZDO character = ResolveCharacterData(ZNet.instance.GetPeer(rpc));
            if (character == null || character.m_uid != snapshot.CharacterId ||
                character.GetLong(ZDOVars.s_playerID, 0) != snapshot.PlayerId) return;
            snapshot.Elemental = elemental; snapshot.Blood = blood; snapshot.MagicReceived = Time.time;
        }
        // CharacterID is the native authenticated peer-to-character binding.
        // PlayerID may remain zero on dedicated servers; never treat it as readiness.
        internal static Player ResolvePlayer(ZNetPeer peer)
        {
            if (peer == null || !peer.IsReady() || ZNetScene.instance == null) return null;
            return ZNetScene.instance.FindInstance(peer.m_characterID)?.GetComponent<Player>();
        }
        internal static ZDO ResolveCharacterData(ZNetPeer peer)
        {
            if (peer?.IsReady() != true || peer.m_characterID == ZDOID.None) return null;
            ZDO zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
            if (zdo == null || zdo.GetOwner() != peer.m_uid ||
                ZNetScene.instance?.GetPrefab(zdo.GetPrefab())?.GetComponent<Player>() == null) return null;
            return zdo;
        }
        internal static long ResolvePlayerId(ZNetPeer peer) => ResolveCharacterData(peer)?.GetLong(ZDOVars.s_playerID, 0) ?? 0;
        internal static bool Has(ZRpc rpc, ZDOID characterId, long playerId, Skills.SkillType skill, int level)
        {
            if (skill == Skills.SkillType.Polearms)
                return MasteryPlugin.Settings.Enabled.Value && level > 0 && level <= 100 && PolearmsLevel(rpc, characterId, playerId) >= level;
            if (skill == Skills.SkillType.Crossbows)
                return MasteryPlugin.Settings.Enabled.Value && level > 0 && CrossbowLevel(rpc, characterId, playerId) >= level;
            ZDO zdo = ResolveCharacterData(ZNet.instance?.GetPeer(rpc));
            if (!MasteryPlugin.Settings.Enabled.Value || zdo == null || zdo.m_uid != characterId ||
                zdo.GetLong(ZDOVars.s_playerID, 0) != playerId || !Snapshots.TryGetValue(rpc, out Snapshot snapshot) ||
                snapshot.CharacterId != characterId || snapshot.PlayerId != playerId || Time.time - snapshot.Received > 10f) return false;
            if (skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic)
                return Time.time - snapshot.MagicReceived <= 10f &&
                    (skill == Skills.SkillType.ElementalMagic ? snapshot.Elemental : snapshot.Blood) >= level;
            return (skill == Skills.SkillType.Crafting ? snapshot.Crafting : skill == Skills.SkillType.Spears ? snapshot.Spears :
                skill == Skills.SkillType.Unarmed ? snapshot.Unarmed : skill == Skills.SkillType.Ride ? snapshot.Ride : 0f) >= level;
        }
        internal static void Tick()
        {
            ZNet net = ZNet.instance;
            if (_session != net) { Snapshots.Clear(); CrossbowSnapshots.Clear(); _session = net; _nextSend = _nextPrune = 0f; }
            long world = net?.GetWorld() != null ? net.GetWorldUID() : 0;
            if (_polearmsWorld != world) { PolearmsSnapshots.Clear(); _polearmsWorld = world; }
            foreach (var rpc in new List<ZRpc>(PolearmsSnapshots.Keys))
                if (!ReferenceEquals(PolearmsSnapshots[rpc].Session, net) || net?.GetPeer(rpc) == null) PolearmsSnapshots.Remove(rpc);
            if (net == null) return;
            if (net.IsServer())
            {
                if (Time.time < _nextPrune) return;
                _nextPrune = Time.time + 2f;
                Player local = Player.m_localPlayer;
                if (local?.m_nview?.IsOwner() == true)
                    local.m_nview.GetZDO()?.Set(RidingPerkAuthority.LevelKey, PerkRuntimeService.GetActualSkillLevel(local, Skills.SkillType.Ride));
                foreach (var rpc in new List<ZRpc>(Snapshots.Keys))
                    if (net.GetPeer(rpc) == null) Snapshots.Remove(rpc);
                foreach (var rpc in new List<ZRpc>(CrossbowSnapshots.Keys))
                    if (net.GetPeer(rpc) == null) CrossbowSnapshots.Remove(rpc);
                return;
            }
            Player player = Player.m_localPlayer;
            if (player == null || !NetworkSync.HasServerSettings || Time.time < _nextSend) return;
            SendNow();
        }
        internal static void SendNow()
        {
            ZNet net = ZNet.instance;
            Player player = Player.m_localPlayer;
            if (net == null || net.IsServer() || player == null || !NetworkSync.HasServerSettings) return;
            // Independent send precedes legacy validation: malformed Crafting/Ride cannot suppress Polearms.
            if (net.GetWorld() != null && player.m_nview?.IsOwner() == true && player.GetPlayerID() != 0 &&
                player.m_nview.GetZDO()?.GetLong(ZDOVars.s_playerID, 0) == player.GetPlayerID())
            {
                float polearms = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Polearms);
                if (Valid(polearms)) net.GetServerRPC()?.Invoke("VM_OwnerPolearmsV1", net.GetWorldUID(), player.m_nview.GetZDO().m_uid, polearms);
            }
            float crafting = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crafting);
            float spears = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Spears);
            float unarmed = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Unarmed);
            float ride = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Ride);
            if (player.m_nview?.IsOwner() == true)
            {
                player.m_nview.GetZDO()?.Set(RidingPerkAuthority.LevelKey, ride);
                player.m_nview.GetZDO()?.Set("vm.workshop.teleporting", player.IsTeleporting());
            }
            if (!Valid(crafting) || !Valid(spears) || !Valid(unarmed) || !Valid(ride))
            {
                MasteryPlugin.Log.LogWarning("[OwnerSkills] Invalid local levels crafting=" + crafting + " spears=" + spears);
                _nextSend = Time.time + 2f; return;
            }
            _nextSend = Time.time + 2f;
            net.GetServerRPC()?.Invoke("VM_OwnerSkillsV2", crafting, spears, unarmed, ride);
            float crossbows = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crossbows);
            if (Valid(crossbows)) net.GetServerRPC()?.Invoke("VM_OwnerCrossbowsV1", crossbows);
#if MASTERY_MAGIC70_EXPERIMENT
            float elemental = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.ElementalMagic);
            float blood = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.BloodMagic);
            if (Valid(elemental) && Valid(blood)) net.GetServerRPC()?.Invoke("VM_OwnerMagicV1", elemental, blood);
#endif
        }

        internal static string Describe(ZRpc rpc, Player player)
        {
            ZNetPeer peer = ZNet.instance?.GetPeer(rpc);
            Snapshots.TryGetValue(rpc, out Snapshot snapshot);
            return "peerId=" + (peer?.m_playerID ?? 0) + " player=" + (player != null) +
                " peerCharacter=" + (peer != null ? peer.m_characterID.ToString() : "none") +
                " characterId=" + ResolvePlayerId(peer) + " ready=" + (peer?.IsReady() == true) +
                " snapshotId=" + (snapshot?.PlayerId ?? 0) + " crafting=" + (snapshot?.Crafting ?? -1f) +
                " age=" + (snapshot != null ? Time.time - snapshot.Received : -1f);
        }
        private static void Receive(ZRpc rpc, float crafting, float spears, float unarmed, float ride)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || !Valid(crafting) || !Valid(spears) || !Valid(unarmed) || !Valid(ride)) return;
            ZNetPeer peer = net.GetPeer(rpc);
            ZDO character = ResolveCharacterData(peer);
            long playerId = character?.GetLong(ZDOVars.s_playerID, 0) ?? 0;
            if (playerId == 0)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogWarning("[OwnerSkills] Snapshot rejected: peer/player identity not ready.");
                return;
            }
            bool first = !Snapshots.ContainsKey(rpc);
            Snapshots[rpc] = new Snapshot { CharacterId = character.m_uid, PlayerId = playerId, Crafting = crafting, Spears = spears, Unarmed = unarmed, Ride = ride, Received = Time.time };
            if (first) MasteryPlugin.Log.LogInfo("[OwnerSkills] Bound character ZDO=" + character.m_uid + " player=" + playerId + " crafting=" + crafting + " spears=" + spears);
        }
        internal static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 100f;
        internal static float Level(Player player, Skills.SkillType skill)
        {
            if (player == null || !MasteryPlugin.Settings.Enabled.Value) return 0f;
            if (skill == Skills.SkillType.Polearms && player == Player.m_localPlayer) return LocalPolearmsLevel(player);
            if (player == Player.m_localPlayer) return Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, skill), 0f, 100f);
            var net = ZNet.instance;
            if (net == null || !net.IsServer()) return 0f;
            if (skill == Skills.SkillType.Polearms)
            {
                foreach (var pair in PolearmsSnapshots)
                    if (ResolvePlayer(net.GetPeer(pair.Key)) == player)
                        return PolearmsLevel(pair.Key, pair.Value.CharacterId, player.GetPlayerID());
                return 0f;
            }
            if (skill == Skills.SkillType.Crossbows)
            {
                foreach (var pair in CrossbowSnapshots)
                    if (ResolvePlayer(net.GetPeer(pair.Key)) == player)
                        return CrossbowLevel(pair.Key, pair.Value.CharacterId, player.GetPlayerID());
                return 0f;
            }
            foreach (var pair in Snapshots)
            {
                var peer = net.GetPeer(pair.Key); var snapshot = pair.Value;
                if (ResolvePlayer(peer) != player || snapshot.PlayerId != player.GetPlayerID() || Time.time - snapshot.Received > 10f) continue;
                if (skill == Skills.SkillType.Spears) return snapshot.Spears;
                if (skill == Skills.SkillType.Crafting) return snapshot.Crafting;
                if (skill == Skills.SkillType.Unarmed) return snapshot.Unarmed;
                if (skill == Skills.SkillType.Ride) return snapshot.Ride;
            }
            return 0f;
        }
        internal static bool Has(Player player, Skills.SkillType skill, int level)
        {
            if (player == null || MasteryPlugin.Settings.Enabled.Value == false) return false;
            if (skill == Skills.SkillType.Polearms) return level > 0 && level <= 100 && Level(player, skill) >= level;
            if (player == Player.m_localPlayer) return PerkRuntimeService.HasPerk(player, skill, level);
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer()) return false;
            if (skill == Skills.SkillType.Crossbows) return level > 0 && Level(player, skill) >= level;
            foreach (var pair in Snapshots)
            {
                ZNetPeer peer = net.GetPeer(pair.Key);
                Snapshot snapshot = pair.Value;
                if (ResolvePlayer(peer) != player || snapshot.PlayerId != player.GetPlayerID() ||
                    Time.time - snapshot.Received > 10f) continue;
                if (skill == Skills.SkillType.Crafting) return snapshot.Crafting >= level;
                if (skill == Skills.SkillType.Spears) return snapshot.Spears >= level;
                if (skill == Skills.SkillType.Unarmed) return snapshot.Unarmed >= level;
                if (skill == Skills.SkillType.Ride) return snapshot.Ride >= level;
            }
            return false;
        }
    }
}
