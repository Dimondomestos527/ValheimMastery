using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MeadXpTransport
    {
        private const string RelayRpc = "VM_MeadXpRelayV2", AwardRpc = "VM_MeadXpAwardV2",
            AckRpc = "VM_MeadXpAckV2", OwnerAckRpc = "VM_MeadXpOwnerAckV2";
        private sealed class Packet { internal long World; internal ZDOID Station; internal string Identity; internal MeadXpGrant Grant; }
        private sealed class Retry { internal float Last = -10f; internal int Cursor; }
        private static readonly ConditionalWeakTable<Fermenter, Retry> Retries = new ConditionalWeakTable<Fermenter, Retry>();

        internal static void Register(ZNetPeer peer)
        {
            peer.m_rpc.Register<ZPackage>(RelayRpc, OnRelay);
            peer.m_rpc.Register<ZPackage>(AwardRpc, OnAward);
            peer.m_rpc.Register<ZPackage>(AckRpc, OnAck);
            peer.m_rpc.Register<ZPackage>(OwnerAckRpc, OnOwnerAck);
        }
        private static ZPackage Encode(Packet packet)
        {
            var data = new ZPackage(); data.Write(2); data.Write(packet.World); data.Write(packet.Station); data.Write(packet.Identity);
            data.Write(packet.Grant.Serial); data.Write(packet.Grant.Stage); data.Write(packet.Grant.Player);
            data.Write(packet.Grant.Coefficient); data.Write(packet.Grant.Input); return data;
        }
        private static Packet Decode(ZPackage data)
        {
            if (data == null || data.Size() > 384 || data.ReadInt() != 2) throw new FormatException("Malformed mead XP v2 packet.");
            var packet = new Packet { World = data.ReadLong(), Station = data.ReadZDOID(), Identity = data.ReadString(), Grant = new MeadXpGrant
            { Serial = data.ReadLong(), Stage = data.ReadInt(), Player = data.ReadLong(), Coefficient = data.ReadSingle(), Input = data.ReadString() } };
            if (data.GetPos() != data.Size() || ZNet.instance == null || packet.World == 0 || packet.World != ZNet.instance.GetWorldUID() ||
                packet.Station == ZDOID.None || !MeadStationIdentity.ValidToken(packet.Identity) || !packet.Grant.Valid)
                throw new FormatException("Invalid mead XP world/identity/amount.");
            return packet;
        }
        private static string StationKey(Packet packet) => MeadStationIdentity.LedgerKey(packet.World, packet.Identity);
        private static ZDO Station(Packet packet)
        {
            ZDO zdo = ZDOMan.instance?.GetZDO(packet.Station);
            return MeadStationIdentity.Matches(zdo, packet.World, packet.Identity) ? zdo : null;
        }
        private static bool IsServerRpc(ZRpc rpc) => ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC();
        private static void Fault(Exception error) => MasteryPlugin.Log.LogWarning("[MeadXP] transport: " + error.Message);

        internal static bool HasDeferred(ZDO zdo) => zdo != null && (!string.IsNullOrEmpty(zdo.GetString(MeadXpOutbox.DeferredLoadKey, "")) ||
            !string.IsNullOrEmpty(zdo.GetString(MeadXpOutbox.DeferredCompletionKey, "")));
        // P2 owner reservation calls this BEFORE native/client debit, with the
        // number of pending slots held by its existing transaction lifecycle.
        internal static bool CanAdmit(Fermenter fermenter, int requiredRows, int reservedRows = 0)
        {
            ZNet net = ZNet.instance;
            if (net == null || fermenter?.m_nview?.IsOwner() != true) return false;
            ZDO zdo = fermenter.m_nview.GetZDO();
            return !HasDeferred(zdo) && MeadStationIdentity.TryEnsure(zdo, net.GetWorldUID(), out _) &&
                MeadXpOutbox.HasCapacity(zdo.GetString(MeadXpOutbox.Key, ""), requiredRows, reservedRows);
        }
        internal static bool TryGetStationKey(Fermenter fermenter, out string station)
        {
            station = null;
            if (ZNet.instance == null || fermenter?.m_nview?.IsOwner() != true ||
                !MeadStationIdentity.TryEnsure(fermenter.m_nview.GetZDO(), ZNet.instance.GetWorldUID(), out string token)) return false;
            station = MeadStationIdentity.LedgerKey(ZNet.instance.GetWorldUID(), token); return true;
        }
        internal static bool CanAcceptLoad(Fermenter fermenter, int reservedRows = 0) =>
            MeadStageExperience.NextLoadSerial(fermenter) > 0 && CanAdmit(fermenter, 2, reservedRows) && !MeadStageExperience.HasPendingOutput(fermenter);
        internal static bool CanAcceptTap(Fermenter fermenter, int reservedRows = 0) =>
            CanAdmit(fermenter, 1, reservedRows) && !MeadStageExperience.HasPendingOutput(fermenter);
        // Durable enqueue only; terminal markers must commit before dispatch.
        internal static bool TryQueue(Fermenter fermenter, MeadXpGrant grant)
        {
            if (ZNet.instance == null || fermenter?.m_nview?.IsOwner() != true || grant?.Valid != true) return false;
            ZDO zdo = fermenter.m_nview.GetZDO();
            try
            {
                if (!MeadStationIdentity.TryEnsure(zdo, ZNet.instance.GetWorldUID(), out _)) return false;
                var rows = MeadXpOutbox.Read(zdo.GetString(MeadXpOutbox.Key, ""));
                if (MeadXpOutbox.Append(rows, grant)) zdo.Set(MeadXpOutbox.Key, MeadXpOutbox.Write(rows));
                return true;
            }
            catch (Exception error) { Fault(error); return false; }
        }
        internal static bool Queue(Fermenter fermenter, MeadXpGrant grant)
        { bool stored = TryQueue(fermenter, grant); if (stored) Drain(fermenter, true); return stored; }
        internal static bool Defer(Fermenter fermenter, MeadXpGrant grant)
        {
            if (ZNet.instance == null || fermenter?.m_nview?.IsOwner() != true || grant?.Valid != true) return false;
            ZDO zdo = fermenter.m_nview.GetZDO();
            try
            {
                if (!MeadStationIdentity.TryEnsure(zdo, ZNet.instance.GetWorldUID(), out _)) return false;
                int key = MeadXpOutbox.DeferredKey(grant.Stage);
                MeadXpGrant existing = MeadXpOutbox.ReadDeferred(zdo.GetString(key, ""), grant.Stage);
                if (existing != null) return existing.Same(grant); // Never replace an earned grant.
                zdo.Set(key, MeadXpOutbox.Write(new[] { grant })); return true;
            }
            catch (Exception error) { Fault(error); return false; }
        }
        private static void RetryDeferred(Fermenter fermenter)
        {
            ZDO zdo = fermenter.m_nview.GetZDO();
            foreach (int stage in new[] { 2, 3 })
            {
                int key = MeadXpOutbox.DeferredKey(stage);
                MeadXpGrant grant = MeadXpOutbox.ReadDeferred(zdo.GetString(key, ""), stage);
                if (grant == null || !TryQueue(fermenter, grant)) continue;
                if (stage == 3) MeadStageExperience.CommitQueuedCompletion(fermenter, grant.Serial);
                zdo.Set(key, "");
            }
        }
        internal static void Drain(Fermenter fermenter, bool immediate = false)
        {
            ZNet net = ZNet.instance;
            if (net == null || !MasteryPlugin.Settings.Enabled.Value || fermenter?.m_nview?.IsOwner() != true) return;
            ZDO zdo = fermenter.m_nview.GetZDO(); string raw = zdo.GetString(MeadXpOutbox.Key, "");
            if ((string.IsNullOrEmpty(raw) || raw == "1\n") && !HasDeferred(zdo)) return;
            Retry retry = Retries.GetOrCreateValue(fermenter);
            if (!immediate && Time.time - retry.Last < 3f) return;
            retry.Last = Time.time;
            try
            {
                if (!MeadStationIdentity.TryGet(zdo, net.GetWorldUID(), out string identity)) return;
                RetryDeferred(fermenter);
                var rows = MeadXpOutbox.Read(zdo.GetString(MeadXpOutbox.Key, "")); int sent = 0;
                if (rows.Count == 0) return;
                int start = retry.Cursor % rows.Count, inspected = 0;
                for (int offset = 0; offset < Math.Min(rows.Count, 32) && sent < 8; offset++)
                {
                    inspected++;
                    MeadXpGrant grant = rows[(start + offset) % rows.Count];
                    if (!MeadXpOutbox.CanDeliver(rows, grant)) continue;
                    var packet = new Packet { World = net.GetWorldUID(), Station = zdo.m_uid, Identity = identity, Grant = grant };
                    if (net.IsServer()) Deliver(packet); else net.GetServerRPC()?.Invoke(RelayRpc, Encode(packet));
                    sent++;
                }
                retry.Cursor = start + Math.Max(1, inspected);
            }
            catch (Exception error) { Fault(error); }
        }
        private static void OnRelay(ZRpc rpc, ZPackage data)
        {
            if (ZNet.instance?.IsServer() != true) return;
            try
            {
                Packet packet = Decode(data); ZNetPeer peer = ZNet.instance.GetPeer(rpc); ZDO zdo = Station(packet);
                if (peer?.IsReady() != true || zdo?.GetOwner() != peer.m_uid ||
                    !MeadXpOutbox.CanDeliver(MeadXpOutbox.Read(zdo.GetString(MeadXpOutbox.Key, "")), packet.Grant)) return;
                Deliver(packet);
            }
            catch (Exception error) { Fault(error); }
        }
        private static void Deliver(Packet packet)
        {
            if (Station(packet) == null) return;
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == packet.Grant.Player)
            {
                if (MeadStageExperience.Apply(local, StationKey(packet), packet.Grant.Serial, packet.Grant.Stage, packet.Grant.Input, packet.Grant.Coefficient))
                    RouteAck(packet);
                return;
            }
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                if (peer.IsReady() && OwnerSkillAuthority.ResolvePlayerId(peer) == packet.Grant.Player)
                { peer.m_rpc.Invoke(AwardRpc, Encode(packet)); return; }
            // Offline recipient: retain the owner outbox and find the new peer after reconnect.
        }
        private static void OnAward(ZRpc rpc, ZPackage data)
        {
            if (!IsServerRpc(rpc)) return;
            try
            {
                Packet packet = Decode(data); Player player = Player.m_localPlayer;
                if (player == null || player.GetPlayerID() != packet.Grant.Player) return;
                if (MeadStageExperience.Apply(player, StationKey(packet), packet.Grant.Serial, packet.Grant.Stage, packet.Grant.Input, packet.Grant.Coefficient))
                    rpc.Invoke(AckRpc, Encode(packet));
            }
            catch (Exception error) { Fault(error); }
        }
        private static void OnAck(ZRpc rpc, ZPackage data)
        {
            if (ZNet.instance?.IsServer() != true) return;
            try
            {
                Packet packet = Decode(data); ZNetPeer peer = ZNet.instance.GetPeer(rpc);
                if (peer?.IsReady() != true || OwnerSkillAuthority.ResolvePlayerId(peer) != packet.Grant.Player || Station(packet) == null) return;
                RouteAck(packet);
            }
            catch (Exception error) { Fault(error); }
        }
        private static void RouteAck(Packet packet)
        {
            ZDO zdo = Station(packet);
            if (zdo == null) return;
            if (zdo.GetOwner() == ZNet.GetUID()) { Remove(zdo, packet.Grant); return; }
            ZNetPeer owner = ZNet.instance.GetPeer(zdo.GetOwner());
            if (owner?.IsReady() == true) owner.m_rpc.Invoke(OwnerAckRpc, Encode(packet));
        }
        private static void OnOwnerAck(ZRpc rpc, ZPackage data)
        {
            if (!IsServerRpc(rpc)) return;
            try
            {
                Packet packet = Decode(data); ZDO zdo = Station(packet);
                if (zdo?.GetOwner() == ZNet.GetUID()) Remove(zdo, packet.Grant);
            }
            catch (Exception error) { Fault(error); }
        }
        private static void Remove(ZDO zdo, MeadXpGrant grant)
        {
            var rows = MeadXpOutbox.Read(zdo.GetString(MeadXpOutbox.Key, ""));
            if (MeadXpOutbox.Remove(rows, grant)) zdo.Set(MeadXpOutbox.Key, MeadXpOutbox.Write(rows));
        }
    }
}
