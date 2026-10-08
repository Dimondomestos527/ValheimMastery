using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class NetworkSync
    {
        private static readonly HashSet<ZRpc> Registered = new HashSet<ZRpc>();
        private static readonly Dictionary<ZNetPeer, float> PendingHello = new Dictionary<ZNetPeer, float>();
        private static readonly Dictionary<ZNetPeer, float> PendingConfirmation = new Dictionary<ZNetPeer, float>();
        internal static bool HasServerSettings { get; private set; }

        internal static void Register(ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null || !Registered.Add(peer.m_rpc))
                return;
            peer.m_rpc.Register<string, string>("ValheimMastery_Hello", OnHello);
            peer.m_rpc.Register<string, string>("ValheimMastery_Ready", OnReady);
            peer.m_rpc.Register<string, float, float, float>("ValheimMastery_ProcFx", OnProcFx);
            peer.m_rpc.Register<string>("ValheimMastery_ClientAbility", OnClientAbility);
            peer.m_rpc.Register<string>("ValheimMastery_Knife70Visual", OnKnife70Visual);
            peer.m_rpc.Register<string>("ValheimMastery_Knife70Candidate", OnKnife70Candidate);
            peer.m_rpc.Register<string>("ValheimMastery_Knife70Debug", OnKnife70Debug);
            peer.m_rpc.Register<string>("ValheimMastery_Knife70DebugState", OnKnife70DebugState);
#if MASTERY_SPEAR35_EXPERIMENT
            peer.m_rpc.Register<float>("ValheimMastery_SpearSkillAudit", OnSpearSkillAudit);
            Spear70HookService.Register(peer.m_rpc);
#endif
            peer.m_rpc.Register<float, string>("ValheimMastery_StationXp", OnStationXp);
            OwnerSkillAuthority.Register(peer.m_rpc);
            CrossbowTurretHandoff.Register(peer.m_rpc);
            CrossbowTurretCommands.Register(peer.m_rpc);
            GoldCraftingService.Register(peer.m_rpc);
            Fists70HitRelay.Register(peer.m_rpc);
            Skeleton35Travel.Register(peer.m_rpc);
            SummonRosterCommands.Register(peer.m_rpc);
            Magic70Carrier.Register(peer.m_rpc);
#if MASTERY_MAGIC70_EXPERIMENT
            Magic70Surtling.Register(peer.m_rpc);
            Magic70BloodDome.Register(peer.m_rpc);
#endif
            WorkshopRemoteCraft.Register(peer.m_rpc);
            MasteryPlugin.Log.LogInfo("Mastery RPC registered for peer (server=" + (ZNet.instance != null && ZNet.instance.IsServer()) + ").");
        }

        internal static void SendStationXp(Player player, float amount, string actionKey)
        {
            if (player == null || amount <= 0f) return;
            ZNet net = ZNet.instance;
            if (player == Player.m_localPlayer)
            {
                ExperienceContext.ObserveAction(player, Skills.SkillType.Crafting, actionKey);
                player.RaiseSkill(Skills.SkillType.Crafting, amount);
                return;
            }
            if (net == null || !net.IsServer())
            {
                return;
            }
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer != null && peer.m_playerID == player.GetPlayerID())
                {
                    peer.m_rpc.Invoke("ValheimMastery_StationXp", amount, actionKey ?? "processing");
                    return;
                }
        }

        private static void OnStationXp(ZRpc rpc, float amount, string actionKey)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || Player.m_localPlayer == null || amount <= 0f) return;
            ExperienceContext.ObserveAction(Player.m_localPlayer, Skills.SkillType.Crafting, actionKey);
            Player.m_localPlayer.RaiseSkill(Skills.SkillType.Crafting, amount);
        }

#if MASTERY_SPEAR35_EXPERIMENT
        internal static string SendSpearSkillAudit()
        {
            Player local = Player.m_localPlayer;
            ZNet net = ZNet.instance;
            if (local == null || net == null) return "Local player or network is unavailable.";
            float localLevel = PerkRuntimeService.GetActualSkillLevel(local, Skills.SkillType.Spears);
            if (net.IsServer())
                return "Host Spear level=" + localLevel.ToString("0.#") + "; owner and server are the same process.";
            net.GetServerRPC()?.Invoke("ValheimMastery_SpearSkillAudit", localLevel);
            return "Sent Spear skill audit to server. Compare [SpearSkillAudit] in server log; claim is diagnostic, not authorization.";
        }

        private static void OnSpearSkillAudit(ZRpc rpc, float claimedLevel)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer()) return;
            ZNetPeer peer = FindPeer(net, rpc);
            Player remote = OwnerSkillAuthority.ResolvePlayer(peer);
            if (remote == null) return;
            float serverLevel = PerkRuntimeService.GetActualSkillLevel(remote, Skills.SkillType.Spears);
            MasteryPlugin.Log.LogInfo("[SpearSkillAudit] player=" + remote.GetPlayerName() +
                " claimedClientLevel=" + claimedLevel.ToString("0.#") +
                " observedServerLevel=" + serverLevel.ToString("0.#") +
                " (client claim is never used for ability authorization)");
        }
#endif

        internal static void Begin(ZNet net, ZRpc rpc)
        {
            if (net == null || !net.IsServer())
                return;
            ZNetPeer peer = FindPeer(net, rpc);
            if (peer != null)
                PendingHello[peer] = Time.time + 0.2f;
        }

        internal static void Tick()
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer())
                return;
            List<ZNetPeer> peers = new List<ZNetPeer>(net.GetPeers());
            float now = Time.time;
            foreach (ZNetPeer peer in peers)
            {
                if (PendingHello.TryGetValue(peer, out float sendAt) && now >= sendAt && peer.IsReady())
                {
                    PendingHello.Remove(peer);
                    PendingConfirmation[peer] = now + Mathf.Max(2f, MasteryPlugin.Settings.HandshakeTimeoutSeconds.Value);
                    peer.m_rpc.Invoke("ValheimMastery_Hello", MasteryPlugin.Version, MasteryRuntime.Manifest());
                    if (MasteryPlugin.Settings.VerboseLogging.Value)
                        MasteryPlugin.Log.LogInfo("Mastery hello sent to " + peer.m_socket.GetHostName());
                }
            }

            foreach (KeyValuePair<ZNetPeer, float> entry in new List<KeyValuePair<ZNetPeer, float>>(PendingConfirmation))
            {
                if (!peers.Contains(entry.Key)) { PendingConfirmation.Remove(entry.Key); continue; }
                if (now < entry.Value) continue;
                PendingConfirmation.Remove(entry.Key);
                if (!MasteryPlugin.Settings.EnforceMatchingMod.Value) continue;
                entry.Key.m_rpc.Invoke("RemotePrint", "ValheimMastery is required and must match the server version.");
                MasteryPlugin.Log.LogWarning("Mastery handshake timed out: " + entry.Key.m_socket.GetHostName());
                net.Kick(entry.Key.m_socket.GetHostName());
            }
        }

        private static void OnHello(ZRpc rpc, string serverVersion, string manifest)
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer())
                return;
            MasteryPlugin.Log.LogInfo("Mastery hello received from server.");
            string[] values = manifest.Split('|');
            if (values.Length != 13 || values[0] != serverVersion ||
                !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float tierStep) ||
                !float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float firstMultiplier) ||
                !float.TryParse(values[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float dodgeXpCoefficient) ||
                !bool.TryParse(values[4], out bool worldScaling) ||
                !float.TryParse(values[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float earlyBonus) ||
                !float.TryParse(values[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float lateBonus) ||
                !bool.TryParse(values[7], out bool legacyCatchUpEnabled) ||
                !float.TryParse(values[8], NumberStyles.Float, CultureInfo.InvariantCulture, out float legacyCatchUpScale) ||
                !float.TryParse(values[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float combatDefault) ||
                !float.TryParse(values[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float combatBow) ||
                !float.TryParse(values[11], NumberStyles.Float, CultureInfo.InvariantCulture, out float combatClub) ||
                !float.TryParse(values[12], NumberStyles.Float, CultureInfo.InvariantCulture, out float clubStaggerPerLevel))
            {
                MasteryPlugin.Log.LogError("Rejected malformed Mastery config received from server.");
                return;
            }
            MasteryRuntime.ApplyServerSettings(tierStep, firstMultiplier, dodgeXpCoefficient, worldScaling, earlyBonus, lateBonus, legacyCatchUpEnabled, legacyCatchUpScale, combatDefault, combatBow, combatClub, clubStaggerPerLevel);
            HasServerSettings = true;
            rpc.Invoke("ValheimMastery_Ready", MasteryPlugin.Version, MasteryRuntime.ManifestHash());
        }

        private static void OnReady(ZRpc rpc, string clientVersion, string manifestHash)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer())
                return;
            ZNetPeer peer = FindPeer(net, rpc);
            if (peer == null)
                return;
            MasteryPlugin.Log.LogInfo("Mastery ready received from client.");
            PendingConfirmation.Remove(peer);
            if (clientVersion == MasteryPlugin.Version && manifestHash == MasteryRuntime.ManifestHash())
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("Mastery handshake accepted: " + peer.m_socket.GetHostName());
                return;
            }
            peer.m_rpc.Invoke("RemotePrint", "ValheimMastery version or server configuration does not match.");
            MasteryPlugin.Log.LogWarning("Mastery handshake rejected: " + peer.m_socket.GetHostName() + " version=" + clientVersion);
            net.Kick(peer.m_socket.GetHostName());
        }

        // Gameplay remains server-authoritative; this only relays its confirmed feedback to the owning client.
        internal static void SendProcFeedback(Player player, string procId, Vector3 position)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || player == null || string.IsNullOrEmpty(procId))
                return;
            long playerId = player.GetPlayerID();
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer != null && peer.m_playerID == playerId)
                {
                    peer.m_rpc.Invoke("ValheimMastery_ProcFx", procId, position.x, position.y, position.z);
                    return;
                }
        }

        // World-space target feedback (axe gouges/execution state, etc.) must be visible
        // to every connected client, not only to the player who caused the proc.
        internal static void BroadcastProcFeedback(string procId, Vector3 position)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || string.IsNullOrEmpty(procId))
                return;
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer?.m_rpc != null)
                    peer.m_rpc.Invoke("ValheimMastery_ProcFx", procId, position.x, position.y, position.z);
        }

        internal static void SendClientAbility(string eventId)
        {
            ZNet net = ZNet.instance;
            if (net == null || string.IsNullOrEmpty(eventId)) return;
            if (net.IsServer())
            {
                if (eventId == "knife35_blink" && Player.m_localPlayer != null)
                    AssassinBlink70Service.ArmServerBlink(Player.m_localPlayer);
                if (eventId.StartsWith("fists70_", StringComparison.Ordinal) && Player.m_localPlayer != null)
                    Fists70MaulService.HandleAbility(Player.m_localPlayer, eventId);
                if (eventId.StartsWith("bow70_", StringComparison.Ordinal) && Player.m_localPlayer != null)
                    OverdrawPenetrationService.HandleAbility(Player.m_localPlayer, eventId);
                if (eventId == "fists35_start" && Player.m_localPlayer != null)
                    Fists35AttackSpeedPatch.RecordServerStart(Player.m_localPlayer);
                if (eventId == "fists35_reset" && Player.m_localPlayer != null)
                    Fists35AttackSpeedPatch.RecordServerReset(Player.m_localPlayer);
                return;
            }
            net.GetServerRPC()?.Invoke("ValheimMastery_ClientAbility", eventId);
        }

        private static void OnClientAbility(ZRpc rpc, string eventId)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || string.IsNullOrEmpty(eventId)) return;
            ZNetPeer peer = FindPeer(net, rpc);
            Player player = OwnerSkillAuthority.ResolvePlayer(peer);
            if (player == null) return;
            if (eventId == "knife35_blink") AssassinBlink70Service.ArmServerBlink(player);
            if (eventId.StartsWith("fists70_", StringComparison.Ordinal)) Fists70MaulService.HandleAbility(player, eventId);
            if (eventId.StartsWith("bow70_", StringComparison.Ordinal)) OverdrawPenetrationService.HandleAbility(player, eventId);
            if (eventId == "fists35_start") Fists35AttackSpeedPatch.RecordServerStart(player);
            if (eventId == "fists35_reset") Fists35AttackSpeedPatch.RecordServerReset(player);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[AbilityRPC] event=" + eventId + " player=" + player.GetPlayerName());
        }
        internal static void BroadcastKnife70Visual(string payload)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || string.IsNullOrEmpty(payload)) return;
            if (Player.m_localPlayer != null) Knife70ShadowVisualService.TryHandle(payload);
            foreach (ZNetPeer peer in net.GetPeers())
                peer?.m_rpc?.Invoke("ValheimMastery_Knife70Visual", payload);
        }

        internal static void SendKnife70HitCandidate(string payload)
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer() || string.IsNullOrEmpty(payload)) return;
            net.GetServerRPC()?.Invoke("ValheimMastery_Knife70Candidate", payload);
        }

        private static void OnKnife70Candidate(ZRpc rpc, string payload)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || string.IsNullOrEmpty(payload)) return;
            Knife70ShadowStrikeService.HandleHitCandidate(payload);
        }
        internal static bool SendKnife70DebugCommand(string command)
        {
            ZNet net = ZNet.instance;
            if (net == null || string.IsNullOrEmpty(command)) return false;
            if (net.IsServer()) return true;
            net.GetServerRPC()?.Invoke("ValheimMastery_Knife70Debug", command);
            return true;
        }

        internal static void SendKnife70DebugState(Player player, string state)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || player == null || string.IsNullOrEmpty(state)) return;
            if (player == Player.m_localPlayer) { Knife70DebugOverlay.Set(state, true); return; }
            long playerId = player.GetPlayerID();
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer != null && peer.m_playerID == playerId) { peer.m_rpc.Invoke("ValheimMastery_Knife70DebugState", state); return; }
        }

        private static void OnKnife70Visual(ZRpc rpc, string payload)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;
            Knife70ShadowVisualService.TryHandle(payload);
        }

        private static void OnKnife70Debug(ZRpc rpc, string command)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || !MasteryPlugin.Settings.UIDebugLogging.Value) return;
            ZNetPeer peer = FindPeer(net, rpc);
            Player player = OwnerSkillAuthority.ResolvePlayer(peer);
            string result = Knife70ShadowStrikeService.HandleDebugCommand(player, command);
            if (player != null) SendKnife70DebugState(player, result + " | " + Knife70ShadowStrikeService.DebugSummary());
            MasteryPlugin.Log.LogInfo("[Knife70Debug] " + result);
        }

        private static void OnKnife70DebugState(ZRpc rpc, string state)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;
            Knife70DebugOverlay.Set(state, true);
        }
        private static void OnProcFx(ZRpc rpc, string procId, float x, float y, float z)
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer() || Player.m_localPlayer == null)
                return;
            Vector3 position = new Vector3(x, y, z);
            if (Fists70ClientService.TryHandle(procId, position)) return;
            PerkVisualService.PlayRemoteProc(Player.m_localPlayer, procId, position);
        }
        private static ZNetPeer FindPeer(ZNet net, ZRpc rpc)
        {
            foreach (ZNetPeer peer in net.GetPeers())
                if (peer.m_rpc == rpc)
                    return peer;
            return null;
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class NetworkRegistrationPatch
    {
        private static void Postfix(ZNetPeer __0) => NetworkSync.Register(__0);
    }

    [HarmonyPatch(typeof(ZNet), "Connect", new[] { typeof(ISocket) })]
    internal static class ClientNetworkRegistrationPatch
    {
        private static void Postfix(ZNetPeer __result) => NetworkSync.Register(__result);
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class NetworkPeerInfoPatch
    {
        private static void Postfix(ZNet __instance, ZRpc __0) => NetworkSync.Begin(__instance, __0);
    }
}
