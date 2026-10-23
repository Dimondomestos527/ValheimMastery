using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class NetworkSync
    {
        // The semantic mod version is intentionally stable across compatible releases.
        // WireIdentity changes whenever the RPC/build cohort is not safe to mix.
        internal const string WireIdentity = MasteryPlugin.Version + "+rpc-20261023-forest-guard-v2-v14";
        private static readonly HashSet<ZRpc> Registered = new HashSet<ZRpc>();
        private static readonly Dictionary<ZNetPeer, float> PendingHello = new Dictionary<ZNetPeer, float>();
        private static readonly Dictionary<ZNetPeer, float> PendingConfirmation = new Dictionary<ZNetPeer, float>();
        internal static bool HasServerSettings { get; private set; }
        // Separate confirmed cohort proof for the new spawn-position protocol.
        // Native peer readiness and absence from timeout queues are not acceptance.
        private sealed class SettingsProof { internal ZRpc Rpc; internal string Manifest; }
        private static readonly Dictionary<ZNetPeer, SettingsProof> ConfirmedPeers = new Dictionary<ZNetPeer, SettingsProof>();
        private static ZNet ConfirmationNet;
        private static long ConfirmationWorld;
        private static ZRpc ConfirmedServerRpc;
        private static string ConfirmedServerManifest;

        private static bool ConfirmationContext(ZNet net)
        {
            long world = net != null && net.GetWorld() != null ? net.GetWorldUID() : 0;
            if (ConfirmationNet != net || ConfirmationWorld != world)
            {
                ConfirmedPeers.Clear(); ConfirmedServerRpc = null; ConfirmedServerManifest = null;
                ConfirmationNet = net; ConfirmationWorld = world;
            }
            return net != null && net == ZNet.instance && world != 0;
        }

        private static void PruneConfirmedPeers(ZNet net)
        {
            var current = net.GetPeers();
            foreach (var entry in new List<KeyValuePair<ZNetPeer, SettingsProof>>(ConfirmedPeers))
                if (entry.Key == null || !current.Contains(entry.Key) || entry.Key.m_rpc != entry.Value.Rpc)
                    ConfirmedPeers.Remove(entry.Key);
        }

        internal static bool HasPeerSettings(ZNetPeer peer)
        {
            var net = ZNet.instance;
            if (!ConfirmationContext(net) || !net.IsServer() || peer == null || peer.m_rpc == null) return false;
            PruneConfirmedPeers(net);
            return peer.IsReady() && ConfirmedPeers.TryGetValue(peer, out var proof) &&
                peer.m_rpc == proof.Rpc && proof.Manifest == MasteryRuntime.ManifestHash();
        }

        internal static bool HasCurrentServerSettings(ZRpc rpc)
        {
            var net = ZNet.instance;
            return ConfirmationContext(net) && !net.IsServer() && HasServerSettings && rpc != null &&
                net.GetServerPeer()?.IsReady() == true && net.GetServerRPC() == rpc &&
                ConfirmedServerRpc == rpc && ConfirmedServerManifest == MasteryRuntime.ManifestHash();
        }

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
            MeadXpTransport.Register(peer);
            OwnerSkillAuthority.Register(peer.m_rpc);
            CrossbowTurretHandoff.Register(peer.m_rpc);
            CrossbowTurretCommands.Register(peer.m_rpc);
            GoldCraftingService.Register(peer.m_rpc);
            WorkbenchEnchantmentRuntime.RegisterPeer(peer);
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
            if (PendingHello.Count == 0 && PendingConfirmation.Count == 0)
                return;
            List<ZNetPeer> peers = new List<ZNetPeer>(net.GetPeers());
            float now = Time.time;
            foreach (ZNetPeer peer in peers)
            {
                if (PendingHello.TryGetValue(peer, out float sendAt) && now >= sendAt && peer.IsReady())
                {
                    PendingHello.Remove(peer);
                    PendingConfirmation[peer] = now + Mathf.Max(2f, MasteryPlugin.Settings.HandshakeTimeoutSeconds.Value);
                    peer.m_rpc.Invoke("ValheimMastery_Hello", WireIdentity, MasteryRuntime.Manifest());
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
            if (ConfirmationContext(net) && net.GetServerRPC() == rpc)
            { ConfirmedServerRpc = null; ConfirmedServerManifest = null; }
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
            if (serverVersion != WireIdentity)
            {
                MasteryPlugin.Log.LogError("Rejected incompatible Mastery network build from server: " + serverVersion);
                // Keep the existing RPC shape so an older server can actively reject this client.
                rpc.Invoke("ValheimMastery_Ready", WireIdentity, "protocol-mismatch");
                return;
            }
            MasteryRuntime.ApplyServerSettings(tierStep, firstMultiplier, dodgeXpCoefficient, worldScaling, earlyBonus, lateBonus, legacyCatchUpEnabled, legacyCatchUpScale, combatDefault, combatBow, combatClub, clubStaggerPerLevel);
            HasServerSettings = true;
            if (ConfirmationContext(net) && net.GetServerRPC() == rpc)
            { ConfirmedServerRpc = rpc; ConfirmedServerManifest = MasteryRuntime.ManifestHash(); }
            rpc.Invoke("ValheimMastery_Ready", WireIdentity, MasteryRuntime.ManifestHash());
        }

        private static void OnReady(ZRpc rpc, string clientVersion, string manifestHash)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer())
                return;
            ZNetPeer peer = FindPeer(net, rpc);
            if (peer == null)
                return;
            if (ConfirmationContext(net))
            { PruneConfirmedPeers(net); ConfirmedPeers.Remove(peer); }
            MasteryPlugin.Log.LogInfo("Mastery ready received from client.");
            PendingConfirmation.Remove(peer);
            if (clientVersion == WireIdentity && manifestHash == MasteryRuntime.ManifestHash())
            {
                if (ConfirmationContext(net) && peer.IsReady() && peer.m_rpc == rpc && net.GetPeers().Contains(peer))
                    ConfirmedPeers[peer] = new SettingsProof { Rpc = rpc, Manifest = manifestHash };
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



