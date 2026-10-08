using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimMastery
{
    internal static class WorkshopRecovery
    {
        internal const string BeforeKey = "vm.workshop.escrow.before";
        internal const string AfterKey = "vm.workshop.escrow.after";
        internal const string SettlementKey = "vm.workshop.settlement";
        private const string HistoryKey = "vm.workshop.settled-history";
        private static float _nextRecovery;
        private static float _nextClientRecovery;
        private static ZNet _session;
        private static string Key => "vm.workshop.receipt." + ZNet.instance.GetWorldUID();
        internal static bool Outstanding => Player.m_localPlayer != null && ZNet.instance != null &&
            Player.m_localPlayer.m_customData.ContainsKey(Key);
        internal static void Record(string id, WorkshopReceiptPhase phase)
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNet.instance == null) throw new InvalidOperationException("No character for transaction receipt.");
            player.m_customData[Key] = WorkshopReceipt.Encode(id, phase);
        }
        internal static void Clear(string id)
        {
            Player player = Player.m_localPlayer;
            if (player != null && ZNet.instance != null && player.m_customData.TryGetValue(Key, out string text) &&
                WorkshopReceipt.TryRead(text, out string saved, out _) && saved == id)
            { player.m_customData.Remove(Key); WorkshopStoragePreview.RefreshUiSoon(); }
        }
        internal static void Register(ZRpc rpc)
        {
            if (!WorkshopRemoteCraft.Enabled) return;
            rpc.Register<string, int>("VM_WS_Recover", Recover);
            rpc.Register<string>("VM_WS_Settled", Settled);
        }
        internal static void Tick()
        {
            if (_session != ZNet.instance)
            { LastRecovery.Clear(); _nextRecovery = _nextClientRecovery = 0f; _session = ZNet.instance; }
            if (ZNet.instance != null && ZNet.instance.IsServer() && Time.time >= _nextRecovery)
            {
                _nextRecovery = Time.time + 5f;
                foreach (var rpc in new List<ZRpc>(LastRecovery.Keys))
                    if (ZNet.instance.GetPeer(rpc) == null) LastRecovery.Remove(rpc);
            }
            if (!WorkshopRemoteCraft.Enabled || ZNet.instance == null ||
                (!ZNet.instance.IsServer() && !NetworkSync.HasServerSettings) ||
                Time.time < _nextClientRecovery || !Outstanding || WorkshopRemoteCraft.ClientBusy) return;
            _nextClientRecovery = Time.time + 5f;
            if (!WorkshopReceipt.TryRead(Player.m_localPlayer.m_customData[Key], out string id, out var phase) ||
                !WorkshopReceipt.TryDecision(phase, out _)) return; // Never guess after an ambiguous crash.
            if (ZNet.instance.IsServer()) Recover(null, id, (int)phase);
            else ZNet.instance.GetServerRPC()?.Invoke("VM_WS_Recover", id, (int)phase);
        }
        private static void Settled(ZRpc rpc, string id)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || rpc != ZNet.instance.GetServerRPC()) return;
            WorkshopTiming.Mark(id, false, "T9.confirmed");
            WorkshopTiming.Report(id, false, "settled", true);
            Clear(id);
        }
        private static readonly Dictionary<ZRpc, float> LastRecovery = new Dictionary<ZRpc, float>();
        private static void Recover(ZRpc rpc, string id, int rawPhase)
        {
            ZNet net = ZNet.instance;
            if (net == null || !net.IsServer() || !Guid.TryParseExact(id, "N", out _) ||
                !WorkshopReceipt.TryDecision((WorkshopReceiptPhase)rawPhase, out bool commit)) return;
            ZNetPeer peer = rpc == null ? null : net.GetPeer(rpc);
            long playerId = rpc == null ? Player.m_localPlayer?.GetPlayerID() ?? 0 : OwnerSkillAuthority.ResolvePlayerId(peer);
            if (playerId == 0) return;
            if (rpc != null)
            {
                if (LastRecovery.TryGetValue(rpc, out float last) && Time.time - last < 2f) return;
                LastRecovery[rpc] = Time.time;
            }
            if (WorkshopRemoteCraft.TryRecoverPending(rpc, playerId, id, commit)) return;
            try
            {
                if (RecoverWorldEscrow(playerId, id, commit))
                { if (rpc == null) Clear(id); else rpc.Invoke("VM_WS_Settled", id); }
            }
            catch (Exception error) { MasteryPlugin.Log.LogError("[Workshop70] Recovery remains quarantined " + id + ": " + error.Message); }
        }
        internal static string Manifest(long playerId, string id, List<Container> chests)
        {
            var ids = chests.Select(c => c.m_nview.GetZDO().m_uid.ToString()).OrderBy(x => x, StringComparer.Ordinal);
            return playerId + "|" + id + "|" + string.Join(";", ids);
        }
        internal static void Mark(List<Container> chests, Dictionary<Container, byte[]> before, long playerId, string id)
        {
            string manifest = Manifest(playerId, id, chests);
            foreach (var chest in chests)
            {
                var after = new ZPackage(); chest.GetInventory().Save(after);
                ZDO zdo = chest.m_nview.GetZDO();
                zdo.Set(BeforeKey, before[chest]); zdo.Set(AfterKey, after.GetArray());
                zdo.Set(SettlementKey, ""); zdo.Set(WorkshopRemoteCraft.EscrowKey, manifest);
            }
        }
        internal static void MarkSettled(List<Container> chests, bool commit)
        {
            // Keep the last settled receipt and snapshots until the next debit,
            // so a lost acknowledgment does not cause a second removal/refund.
            foreach (var chest in chests)
            {
                if (chest == null || chest.m_nview?.IsOwner() != true) throw new InvalidOperationException("Escrow chest unavailable.");
                ZDO zdo = chest.m_nview.GetZDO(); string manifest = zdo.GetString(WorkshopRemoteCraft.EscrowKey, "");
                if (manifest.Length > 0) zdo.Set(SettlementKey, (commit ? "C|" : "R|") + manifest);
            }
            foreach (var chest in chests) chest.m_nview.GetZDO().Set(WorkshopRemoteCraft.EscrowKey, "");
            // Keep compact, per-transaction proof on the world record after all
            // participants settled. A later craft must not erase an older ACK.
            // This survives ordinary world saves, not rollback to an older save.
            foreach (var chest in chests)
            {
                ZDO zdo = chest.m_nview.GetZDO();
                string[] fields = zdo.GetString(SettlementKey, "").Split('|');
                if (fields.Length == 4 && long.TryParse(fields[1], out long playerId) && Guid.TryParseExact(fields[2], "N", out _))
                    zdo.Set(HistoryKey, WorkshopSettlementHistory.Append(zdo.GetString(HistoryKey, ""), playerId, fields[2], commit));
            }
        }
        internal static void MarkRecords(List<ZDO> records, Dictionary<ZDO, byte[]> before, long playerId, string id)
        {
            string manifest = playerId + "|" + id + "|" + string.Join(";", records.Select(r => r.m_uid.ToString()).OrderBy(x => x, StringComparer.Ordinal));
            foreach (ZDO record in records)
            {
                if (record.GetOwner() != ZNet.GetUID() || !ReferenceEquals(ZDOMan.instance.GetZDO(record.m_uid), record))
                    throw new InvalidOperationException("Escrow record unavailable.");
                record.Set(BeforeKey, before[record]); record.Set(AfterKey, record.GetByteArray(ZDOVars.s_items, null));
                record.Set(SettlementKey, ""); record.Set(WorkshopRemoteCraft.EscrowKey, manifest);
                ZDOMan.instance.ForceSendZDO(record.m_uid);
            }
        }
        internal static void SettleRecords(List<ZDO> records, bool commit)
        {
            foreach (ZDO record in records)
                if (record.GetOwner() != ZNet.GetUID() || !ReferenceEquals(ZDOMan.instance.GetZDO(record.m_uid), record))
                    throw new InvalidOperationException("Escrow record unavailable at settlement.");
            foreach (ZDO record in records)
            {
                string manifest = record.GetString(WorkshopRemoteCraft.EscrowKey, "");
                if (manifest.Length == 0) throw new InvalidOperationException("Missing escrow manifest.");
                record.Set(SettlementKey, (commit ? "C|" : "R|") + manifest);
            }
            foreach (ZDO record in records) record.Set(WorkshopRemoteCraft.EscrowKey, "");
            foreach (ZDO record in records)
            {
                string[] fields = record.GetString(SettlementKey, "").Split('|');
                if (fields.Length == 4 && long.TryParse(fields[1], out long playerId) && Guid.TryParseExact(fields[2], "N", out _))
                    record.Set(HistoryKey, WorkshopSettlementHistory.Append(record.GetString(HistoryKey, ""), playerId, fields[2], commit));
                ZDOMan.instance.ForceSendZDO(record.m_uid);
            }
        }
        private static bool RecoverWorldEscrow(long playerId, string id, bool commit)
        {
            // Reconnect after a server reload. Scan world ZDOs only on this bounded
            // recovery request, not every frame and not just currently loaded chests.
            string prefix = playerId + "|" + id + "|";
            string manifest = null;
            var records = new List<ZDO>();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                string active = zdo.GetString(WorkshopRemoteCraft.EscrowKey, "");
                string settled = zdo.GetString(SettlementKey, "");
                string match = active.StartsWith(prefix, StringComparison.Ordinal) ? active :
                    settled.Length > 2 && settled.Substring(2).StartsWith(prefix, StringComparison.Ordinal) ? settled.Substring(2) : null;
                if (match == null) continue;
                if (manifest != null && manifest != match) return false;
                manifest = match; records.Add(zdo);
            }
            if (records.Count == 0)
            {
                bool found = false;
                foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
                {
                    if (!WorkshopSettlementHistory.TryFind(zdo.GetString(HistoryKey, ""), playerId, id, out bool historicalCommit)) continue;
                    if (historicalCommit != commit) return false;
                    found = true;
                }
                return found;
            }
            if (records.Count > 128) return false;
            string actual = string.Join(";", records.Select(z => z.m_uid.ToString()).OrderBy(x => x, StringComparer.Ordinal));
            if (manifest != prefix + actual) return false; // Missing/destroyed or substituted chest.
            foreach (var zdo in records)
            {
                string active = zdo.GetString(WorkshopRemoteCraft.EscrowKey, "");
                string decision = zdo.GetString(SettlementKey, "");
                if (decision == (commit ? "C|" : "R|") + manifest && active.Length == 0) continue;
                if (zdo.GetOwner() != ZNet.GetUID() && zdo.GetOwner() != 0) return false;
                if (decision.Length > 0 && decision != (commit ? "C|" : "R|") + manifest) return false;
                byte[] current = zdo.GetByteArray(ZDOVars.s_items, null);
                byte[] before = zdo.GetByteArray(BeforeKey, null), after = zdo.GetByteArray(AfterKey, null);
                if (before == null || after == null || current == null ||
                    (!current.SequenceEqual(after) && (commit || !current.SequenceEqual(before)))) return false;
                var loaded = ZNetScene.instance?.FindInstance(zdo.m_uid)?.GetComponent<Container>();
                if (loaded != null)
                {
                    if (loaded.IsInUse()) return false;
                    var live = new ZPackage(); loaded.GetInventory().Save(live);
                    byte[] contents = live.GetArray();
                    if (!contents.SequenceEqual(after) && !contents.SequenceEqual(before)) return false;
                }
            }
            // Preflight the entire set before changing the first world record.
            foreach (var zdo in records)
            {
                if (zdo.GetString(WorkshopRemoteCraft.EscrowKey, "").Length == 0) continue;
                zdo.SetOwner(ZNet.GetUID());
                if (!commit) zdo.Set(ZDOVars.s_items, zdo.GetByteArray(BeforeKey, null));
                zdo.Set(SettlementKey, (commit ? "C|" : "R|") + manifest);
            }
            foreach (var zdo in records)
            {
                if (zdo.GetString(WorkshopRemoteCraft.EscrowKey, "").Length == 0) continue;
                GameObject instance = ZNetScene.instance?.FindInstance(zdo.m_uid);
                var loaded = instance?.GetComponent<Container>();
                if (loaded == null) continue;
                loaded.Load();
                var live = new ZPackage(); loaded.GetInventory().Save(live);
                byte[] expected = zdo.GetByteArray(commit ? AfterKey : BeforeKey, null);
                if (expected == null || !live.GetArray().SequenceEqual(expected))
                    throw new InvalidOperationException("Loaded chest did not reconcile to its world snapshot.");
            }
            foreach (var zdo in records) zdo.Set(WorkshopRemoteCraft.EscrowKey, "");
            foreach (var zdo in records)
            {
                WorkshopAtomicDebit.ReleaseRecovered(ZNetScene.instance?.FindInstance(zdo.m_uid)?.GetComponent<Container>()?.GetInventory());
                WorkshopAtomicDebit.ReleaseRecovered(zdo);
                WorkshopChestLease.ReturnSettled(zdo);
            }
            MasteryPlugin.Log.LogInfo("[Workshop70] Reconciled world escrow " + id + (commit ? " committed" : " refunded"));
            return true;
        }
    }
}
