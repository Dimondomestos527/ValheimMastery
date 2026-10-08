using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Cooperative handoff: the current owner freezes/saves, server observes those
    // exact bytes AND the owner's handoff through ordinary ZDO replication. No
    // snapshot supplied by a non-owner is ever written into the world inventory.
    internal static class WorkshopChestLease
    {
        internal const string QuarantineKey = "vm.workshop.owner-quarantine";
        internal const string ReturnOwnerKey = "vm.workshop.return-owner";
        internal const string WarmKey = "vm.workshop.warm";
        internal const string PreparationKey = "vm.workshop.prepared";
        internal const string PreparationReturnKey = "vm.workshop.prepared-return";
        private const string PreparationAbortKey = "vm.workshop.prepare-abort";
        private const string PreparationOriginKey = "vm.workshop.prepare-origin";
        private const string PreparationExpectedKey = "vm.workshop.prepare-expected";
        private sealed class WarmLease
        {
            internal ZDO Record;
            internal WorkshopActor Actor;
            internal Vector3 Point;
            internal float Expires;
        }
        private static readonly Dictionary<ZDOID, WarmLease> Warm = new Dictionary<ZDOID, WarmLease>();
        private static Container _opening;
        private static bool _openingAlt;
        private static float _openingDeadline, _nextWarmTick, _nextOpenRequest, _nextRecoveryTick;
        private sealed class Entry
        {
            internal ZDO Record;
            internal ZDOID Id;
            internal ZRpc OwnerRpc;
            internal long Owner;
            internal byte[] Expected;
            internal bool Sent, Rejected;
        }
        private sealed class Batch
        {
            internal string Token;
            internal WorkshopActor Player;
            internal long PlayerId;
            internal ZRpc Requester;
            internal Vector3 ActionPoint;
            internal float Deadline;
            internal readonly List<Entry> Entries = new List<Entry>();
            internal Action Continue;
            internal Action<string> Reject;
            internal bool Preparation;
            internal float WarmSeconds;
            internal readonly List<ZDO> Contributors = new List<ZDO>();
            internal string WaitingToken;
            internal Action WaitingAction;
        }
        private sealed class Freeze
        {
            internal string Token;
            internal long ServerUid;
            internal float Expires;
            internal byte[] Snapshot;
            internal bool Released;
            internal bool Preparation;
            internal float NextAck;
        }
        private sealed class RetiringLease
        {
            internal Entry Entry;
            internal string Token;
            internal float Deadline;
            internal float SafeReturnUntil;
        }
        // Failed speculative handoffs can still receive native ownership/ACK
        // later. Keep their origin identity and access guard until reconciliation.
        // Capacity is bounded; never discard an ambiguous handoff to make room.
        private static readonly Dictionary<ZDOID, RetiringLease> Retiring = new Dictionary<ZDOID, RetiringLease>();
        private static readonly Dictionary<long, Batch> Batches = new Dictionary<long, Batch>();
        private static readonly Dictionary<ZDOID, string> Held = new Dictionary<ZDOID, string>();
        private static readonly Dictionary<ZDOID, Freeze> Frozen = new Dictionary<ZDOID, Freeze>();
        [ThreadStatic] private static string _accessToken;
        [ThreadStatic] private static long _accessPlayer;
        private static ZNet _session;
        private static float _nextTick;
        internal static bool Pending(long playerId) => Batches.ContainsKey(playerId);
        internal static bool SamePending(long playerId, string token) => Batches.TryGetValue(playerId, out var batch) && batch.Token == token;
        internal static bool WaitForPreparation(long playerId, string token, Action action, Action<string> reject)
        {
            if (!Batches.TryGetValue(playerId, out var batch) || !batch.Preparation) return false;
            if (batch.WaitingToken == null) { batch.WaitingToken = token; batch.WaitingAction = action; }
            else if (batch.WaitingToken != token) reject("Попередня дія вже очікує підготовки скринь.");
            return true;
        }
        internal static bool BlocksAccess(Container chest, long playerId)
        {
            ZDO zdo = chest?.m_nview?.GetZDO();
            return BlocksRecord(zdo, playerId) || (zdo != null && WorkshopAtomicDebit.IsLocked(zdo));
        }
        internal static bool BlocksRecord(ZDO zdo, long playerId)
        {
            if (zdo == null) return false;
            if (zdo.GetBool(QuarantineKey, false) || Frozen.ContainsKey(zdo.m_uid)) return true;
            return Held.TryGetValue(zdo.m_uid, out string token) &&
                (token != _accessToken || playerId != _accessPlayer);
        }
        internal static bool PinOwner(ZDO zdo, long requestedOwner)
        {
            ZNet net = ZNet.instance;
            if (!WorkshopRemoteCraft.Enabled || net == null || zdo == null) return false;
            if (Frozen.TryGetValue(zdo.m_uid, out var freeze) && requestedOwner != freeze.ServerUid && requestedOwner != zdo.GetOwner()) return true;
            return net.IsServer() && zdo.GetOwner() == ZNet.GetUID() && requestedOwner != ZNet.GetUID() &&
                (Held.ContainsKey(zdo.m_uid) || Warm.ContainsKey(zdo.m_uid) || zdo.GetBool(WarmKey, false) || zdo.GetBool(QuarantineKey, false) ||
                 zdo.GetString(WorkshopRemoteCraft.EscrowKey, "").Length != 0);
        }
        internal static void Register(ZRpc rpc)
        {
            if (!WorkshopRemoteCraft.Enabled) return;
            Tick();
            rpc.Register<ZPackage>("VM_WS_Lease", ReceiveLease);
            rpc.Register<ZPackage>("VM_WS_LeaseAck", ReceiveAck);
            rpc.Register<ZDOID>("VM_WS_WarmOpen", ReceiveWarmOpen);
        }
        internal static void KeepWarm(IEnumerable<ZDO> records, WorkshopActor actor, Vector3 point, float seconds = 8f, bool preparation = false)
        {
            if (ZNet.instance?.IsServer() != true || actor == null) return;
            foreach (ZDO record in records)
            {
                if (record.GetOwner() != ZNet.GetUID() || record.GetBool(QuarantineKey, false) ||
                    (!Warm.ContainsKey(record.m_uid) && Warm.Count >= 128)) continue;
                if (preparation && !Warm.ContainsKey(record.m_uid) &&
                    Warm.Values.Count(w => w.Actor.GetPlayerID() == actor.GetPlayerID()) >= 8)
                {
                    var oldest = Warm.Where(w => w.Value.Actor.GetPlayerID() == actor.GetPlayerID()).OrderBy(w => w.Value.Expires).First();
                    Warm.Remove(oldest.Key); oldest.Value.Record.Set(WarmKey, false); ReturnSettled(oldest.Value.Record);
                }
                float expires = Time.time + seconds;
                if (Warm.TryGetValue(record.m_uid, out var previous)) expires = Math.Max(expires, previous.Expires);
                Warm[record.m_uid] = new WarmLease { Record = record, Actor = actor, Point = point, Expires = expires };
                record.Set(WarmKey, true);
            }
        }
        // An unloaded dedicated-server chest has no native RequestOpen handler.
        // Release first, then let the native owner handle normal interaction.
        internal static bool DeferOpen(Container chest, Humanoid user, bool hold, bool alt)
        {
            var net = ZNet.instance; ZDO record = chest?.m_nview?.GetZDO();
            if (!WorkshopRemoteCraft.Enabled || net == null || net.IsServer() || hold || user != Player.m_localPlayer ||
                record == null || !record.GetBool(WarmKey, false) || record.GetOwner() != net.GetServerPeer()?.m_uid) return false;
            if (_opening != null) return true;
            if (!chest.CheckAccess(Player.m_localPlayer.GetPlayerID()) ||
                (chest.m_checkGuardStone && !PrivateArea.CheckAccess(chest.transform.position, 0f, true, false))) return false;
            _opening = chest; _openingAlt = alt; _openingDeadline = Time.time + 4f;
            _nextOpenRequest = Time.time + .25f;
            net.GetServerRPC()?.Invoke("VM_WS_WarmOpen", record.m_uid);
            return true;
        }
        private static void ReceiveWarmOpen(ZRpc rpc, ZDOID id)
        {
            if (ZNet.instance?.IsServer() != true) return;
            WorkshopActor actor = WorkshopActor.Resolve(rpc); ZDO record = ZDOMan.instance?.GetZDO(id);
            if (actor == null || !actor.Available || actor.IsDead() || record == null ||
                record.GetOwner() != ZNet.GetUID() || record.GetInt(ZDOVars.s_inUse, 0) != 0 ||
                Vector3.Distance(actor.Position, record.GetPosition()) > 6f ||
                !WorkshopWorldRecords.WardAllows(actor.GetPlayerID(), record.GetPosition())) return;
            var entry = WorkshopWorldRecords.Find(id);
            if (entry?.Chest == null || entry.Chest.m_privacy == Container.PrivacySetting.Group ||
                (entry.Chest.m_privacy == Container.PrivacySetting.Private && record.GetLong(ZDOVars.s_creator, 0) != actor.GetPlayerID()) ||
                WorkshopAtomicDebit.IsLocked(record) || Held.ContainsKey(id) || record.GetBool(QuarantineKey, false) ||
                record.GetString(WorkshopRemoteCraft.EscrowKey, "").Length != 0) return;
            Warm.Remove(id); record.Set(WarmKey, false);
            // Native ownership may return to the opener even if the previous
            // simulation owner disconnected. No inventory snapshot is supplied.
            record.Set(ReturnOwnerKey, ZNet.instance.GetPeer(rpc).m_uid);
            ReturnSettled(record);
        }
        internal static void Acquire(WorkshopActor player, ZRpc requester, string token, Vector3 actionPoint,
            Dictionary<string, int> credit, Action continuation, Action<string> reject, bool preparation = false, float warmSeconds = 8f)
        {
            if (Batches.ContainsKey(player.GetPlayerID())) { reject("Передавання власності вже очікує на завершення."); return; }
            WorkshopTiming.Mark(token, true, "Snetwork.begin");
            var candidates = WorkshopWorldRecords.Chests(player, actionPoint, false, token);
            WorkshopTiming.Mark(token, true, "T2.containersResolved");
            WorkshopTiming.Count(token, true, "eligibleChests", candidates.Count);
            var left = new Dictionary<string, int>(credit, StringComparer.Ordinal);
            var remote = new List<WorkshopWorldRecords.Entry>();
            var contributors = new List<ZDO>();
            int inspected = 0;
            foreach (var chest in candidates)
            {
                if (preparation && ++inspected > 64) break; // Optional proposal budget; actual action still scans normally.
                // Legacy owners retain the ordinary action path.
                if (preparation && chest.Data.GetOwner() != ZNet.GetUID() &&
                    !WorkshopRemoteCraft.SupportsPreparation(ZNet.instance.GetPeer(chest.Data.GetOwner())?.m_rpc)) continue;
                // This is only a lease proposal. The owner ACK and final debit
                // still verify current contents; stale previews never grant stock.
                var stock = new WorkshopRecordStore(chest.Data, chest.Chest);
                bool contributes = false;
                foreach (string name in new List<string>(left.Keys))
                {
                    int take = Math.Min(left[name], stock.Count(name));
                    if (take <= 0) continue;
                    left[name] -= take; contributes = true;
                }
                if (contributes)
                {
                    contributors.Add(chest.Data);
                    if (chest.Data.GetOwner() != ZNet.GetUID()) remote.Add(chest);
                }
                if (left.Values.All(value => value == 0)) break;
                if (preparation && contributors.Count >= 8) break;
            }
            WorkshopTiming.Mark(token, true, "T3.availabilityProposal");
            WorkshopTiming.Count(token, true, "leasedChests", remote.Count);
            if (remote.Count == 0)
            { if (preparation) KeepWarm(contributors, player, actionPoint, warmSeconds, true); continuation(); return; }
            if (remote.Count > 32) { reject("Забагато скринь з віддаленим власником для безпечного передавання (максимум 32)."); return; }
            if (preparation && Retiring.Count + Batches.Values.Where(b => b.Preparation).Sum(b => b.Entries.Count) + remote.Count > 128)
            { reject("Ліміт незавершених підготовок скринь."); return; }
            var batch = new Batch { Player = player, PlayerId = player.GetPlayerID(), Requester = requester,
                Token = token, ActionPoint = actionPoint, Deadline = Time.time + 6f, Continue = continuation, Reject = reject,
                Preparation = preparation, WarmSeconds = warmSeconds };
            batch.Contributors.AddRange(contributors);
            foreach (var chest in remote)
            {
                ZDO zdo = chest.Data; long owner = zdo.GetOwner();
                var peer = ZNet.instance.GetPeer(owner);
                if (peer?.IsReady() != true || Held.ContainsKey(zdo.m_uid))
                { reject("Власник скрині недоступний або зайнятий."); return; }
                batch.Entries.Add(new Entry { Record = zdo, Id = zdo.m_uid, Owner = owner, OwnerRpc = peer.m_rpc });
            }
            // Lock the whole set before the first callback or RPC can run.
            foreach (var entry in batch.Entries) Held.Add(entry.Id, token);
            Batches.Add(batch.PlayerId, batch);
            try
            {
                foreach (var entry in batch.Entries)
                {
                    var packet = new ZPackage(); packet.Write(preparation ? 2 : 0); packet.Write(token); packet.Write(entry.Id);
                    packet.Write(batch.PlayerId); packet.Write(ZNet.GetUID());
                    entry.Sent = true;
                    entry.OwnerRpc.Invoke("VM_WS_Lease", packet);
                }
                WorkshopTiming.Mark(token, true, "Sleases.sent");
            }
            catch (Exception error) { Finish(batch, "Ownership handoff send failed: " + error.Message); }
        }
        internal static void Tick()
        {
            if (!WorkshopRemoteCraft.Enabled) return;
            ZNet net = ZNet.instance;
            if (_session != net) { Batches.Clear(); Held.Clear(); Frozen.Clear(); Warm.Clear(); Retiring.Clear(); _opening = null; _session = net; _nextTick = _nextWarmTick = _nextRecoveryTick = 0f; }
            // Persisted uncertain prep is recoverable even after a session restart.
            if (net?.IsServer() == true && Time.time >= _nextRecoveryTick)
            { _nextRecoveryTick = Time.time + 5f; RecoverPreparationAborts(); }
            if (net == null || (Batches.Count == 0 && Frozen.Count == 0 && Warm.Count == 0 && Retiring.Count == 0 && _opening == null) || Time.time < _nextTick) return;
            _nextTick = Time.time + 0.05f;
            if (!net.IsServer())
            {
                if (_opening != null)
                {
                    var record = _opening.m_nview?.GetZDO(); var player = Player.m_localPlayer;
                    if (record == null || player == null || player.IsDead() || Time.time > _openingDeadline ||
                        Vector3.Distance(player.transform.position, _opening.transform.position) > 6f) _opening = null;
                    else if (!record.GetBool(WarmKey, false) && record.GetOwner() != net.GetServerPeer()?.m_uid)
                    {
                        Container chest = _opening; bool alt = _openingAlt; _opening = null;
                        if (chest.m_nview.IsOwner()) chest.Load();
                        chest.Interact(player, false, alt);
                    }
                    else if (Time.time >= _nextOpenRequest)
                    { _nextOpenRequest = Time.time + .25f; net.GetServerRPC()?.Invoke("VM_WS_WarmOpen", record.m_uid); }
                }
                foreach (var pair in new List<KeyValuePair<ZDOID, Freeze>>(Frozen))
                {
                    ZDO zdo = ZDOMan.instance?.GetZDO(pair.Key);
                    if (pair.Value.Preparation && Time.time >= pair.Value.NextAck &&
                        zdo?.GetOwner() == pair.Value.ServerUid && pair.Value.Snapshot != null)
                    {
                        pair.Value.NextAck = Time.time + .5f;
                        try { ZDOMan.instance.ForceSendZDO(pair.Value.ServerUid, pair.Key); Ack(net.GetServerRPC(), pair.Value.Token, pair.Key, pair.Value.Snapshot); }
                        catch { /* The next bounded update retries the same owner snapshot. */ }
                    }
                    // The local freeze cannot outlive a lost connection/session.
                    // Server stops handoffs at6s, well before this20s expiry.
                    // The return stamp binds CURRENT stock, including any later
                    // real debits. An old prep snapshot is never restored.
                    bool preparedReturn = zdo != null && pair.Value.Preparation &&
                        WorkshopPreparationReturn.Matches(zdo.GetString(PreparationReturnKey, ""),
                            pair.Value.Token, zdo.GetByteArray(ZDOVars.s_items, null));
                    if (zdo != null && pair.Value.Released && zdo.GetOwner() == ZNet.GetUID() &&
                        (preparedReturn || zdo.GetString(WorkshopRecovery.SettlementKey, "").Contains("|" + pair.Value.Token + "|")))
                    {
                        // A returned owner must first load the settled world bytes,
                        // even if intermediate server ownership was never rendered.
                        var chest = ZNetScene.instance?.FindInstance(pair.Key)?.GetComponent<Container>();
                        if (chest != null) { chest.Load(); Frozen.Remove(pair.Key); }
                        continue;
                    }
                    if (zdo == null || net.GetServerRPC() == null ||
                        (Time.time > pair.Value.Expires && zdo.GetOwner() != ZNet.GetUID()) ||
                        (pair.Value.Released && zdo.GetOwner() != ZNet.GetUID() &&
                         (!pair.Value.Preparation || zdo.GetString(PreparationKey, "") == pair.Value.Token)))
                        Frozen.Remove(pair.Key);
                }
                return;
            }
            foreach (var pair in Retiring.ToArray())
            {
                var retirement = pair.Value; var entry = retirement.Entry;
                var record = ZDOMan.instance?.GetZDO(pair.Key);
                if (!ReferenceEquals(record, entry.Record))
                { Retiring.Remove(pair.Key); Held.Remove(pair.Key); continue; }
                // Only native owner-origin bytes plus authenticated ACK permit
                // handback. Never overwrite items with any retained snapshot.
                bool originGone = net.GetPeer(entry.OwnerRpc)?.m_uid != entry.Owner && net.GetPeer(entry.Owner) == null;
                bool bytesReady = entry.Expected != null && record.GetByteArray(ZDOVars.s_items, null)?.SequenceEqual(entry.Expected) == true;
                var retirementState = WorkshopPreparationRetirementRules.Evaluate(Time.time, retirement.Deadline,
                    record.GetOwner() == ZNet.GetUID(), originGone, bytesReady,
                    WorkshopAtomicDebit.IsLocked(record) || record.GetString(WorkshopRemoteCraft.EscrowKey, "").Length != 0 || record.GetBool(QuarantineKey, false));
                if (retirementState == WorkshopPreparationRetirement.Wait) continue;
                if (retirementState == WorkshopPreparationRetirement.Quarantine)
                {
                    // Surface uncertainty durably instead of keeping an invisible
                    // in-memory access lock forever. Recovery never restores bytes.
                    record.Set(PreparationAbortKey, retirement.Token); record.Set(PreparationOriginKey, entry.Owner);
                    record.Set(PreparationExpectedKey, WorkshopPreparationReturn.Stamp(retirement.Token, entry.Expected));
                    record.Set(QuarantineKey, true);
                    Held.Remove(pair.Key); Retiring.Remove(pair.Key);
                    MasteryPlugin.Log.LogWarning("[Workshop70] Preparation quarantined chest=" + pair.Key + " origin=" + entry.Owner +
                        " token=" + retirement.Token + "; recovers after origin disconnect/native server ownership or matching acknowledged bytes.");
                    continue;
                }
                record.Set(ReturnOwnerKey, entry.Owner);
                record.Set(PreparationKey, retirement.Token);
                Held.Remove(pair.Key); Retiring.Remove(pair.Key);
                if (Time.time < retirement.SafeReturnUntil) ReturnSettled(record);
                else record.Set(ReturnOwnerKey, 0L); // Late return requires explicit WarmOpen/Load.
                // If origin disconnected, current authentic stock remains server
                // owned but normally openable after reconnect/another opener.
                if (record.GetOwner() == ZNet.GetUID())
                { record.Set(WarmKey, true); }
                try
                {
                    var release = new ZPackage(); release.Write(3); release.Write(retirement.Token); release.Write(entry.Id);
                    release.Write(record.GetOwner() == ZNet.GetUID()); entry.OwnerRpc.Invoke("VM_WS_Lease", release);
                }
                catch { /* Native record and return identity remain authoritative. */ }
            }
            if (Time.time >= _nextWarmTick)
            {
                _nextWarmTick = Time.time + 0.25f;
                foreach (var pair in new List<KeyValuePair<ZDOID, WarmLease>>(Warm))
                {
                    var lease = pair.Value;
                    if (Time.time <= lease.Expires && lease.Actor.HasCrafting70 && !lease.Actor.IsDead() &&
                        !lease.Actor.IsTeleporting() && Vector3.Distance(lease.Actor.Position, lease.Point) <= 8f &&
                        ReferenceEquals(ZDOMan.instance?.GetZDO(pair.Key), lease.Record) && lease.Record.GetOwner() == ZNet.GetUID()) continue;
                    Warm.Remove(pair.Key);
                    if (ReferenceEquals(ZDOMan.instance?.GetZDO(pair.Key), lease.Record) && lease.Record.GetOwner() == ZNet.GetUID())
                    { lease.Record.Set(WarmKey, false); ReturnSettled(lease.Record); }
                }
            }
            foreach (var batch in new List<Batch>(Batches.Values))
            {
                if (Time.time > batch.Deadline || batch.Player == null || batch.Player.IsDead() ||
                    batch.Player.IsTeleporting() || WorkshopRemoteCraft.RequestPlayer(batch.Requester)?.CharacterId != batch.Player.CharacterId ||
                    !batch.Player.HasCrafting70)
                { Finish(batch, "Час очікування передавання власності минув або гравець недоступний."); continue; }
                bool ready = true; string failure = null;
                foreach (var entry in batch.Entries)
                {
                    var zdo = ZDOMan.instance?.GetZDO(entry.Id);
                    if (!ReferenceEquals(zdo, entry.Record)) { failure = "Скриня зникла під час передавання власності."; break; }
                    var status = WorkshopLeaseRules.Evaluate(zdo.GetOwner(), entry.Owner, ZNet.GetUID(),
                        zdo.GetInt(ZDOVars.s_inUse, 0) != 0, net.GetPeer(entry.OwnerRpc)?.m_uid == entry.Owner,
                        zdo.GetByteArray(ZDOVars.s_items, null), entry.Expected);
                    if (status == WorkshopLeaseReadiness.Abort) { failure = "Власник скрині або її стан змінився під час передавання."; break; }
                    if (status != WorkshopLeaseReadiness.Ready) ready = false;
                }
                if (failure != null) { Finish(batch, failure); continue; }
                if (!ready) continue;
                try
                {
                    Access(batch, () =>
                    {
                        var stillEligible = WorkshopWorldRecords.Chests(batch.Player, batch.ActionPoint, false);
                        if (batch.Entries.Any(e => !stillEligible.Any(c => ReferenceEquals(c.Data, e.Record))))
                            throw new InvalidOperationException("Chest access/network coverage changed.");
                        foreach (var entry in batch.Entries)
                        {
                            ZDO zdo = entry.Record;
                            zdo.SetOwner(ZNet.GetUID());
                            zdo.Set(ReturnOwnerKey, entry.Owner);
                            if (zdo.GetOwner() != ZNet.GetUID()) throw new InvalidOperationException("Server did not acquire chest.");
                            if (!zdo.GetByteArray(ZDOVars.s_items, null).SequenceEqual(entry.Expected)) throw new InvalidOperationException("World stock differs from frozen owner snapshot.");
                            ZDOMan.instance.ForceSendZDO(entry.Owner, entry.Id);
                        }
                        if (batch.Preparation)
                        {
                            foreach (var entry in batch.Entries) entry.Record.Set(PreparationKey, batch.Token);
                            KeepWarm(batch.Contributors, batch.Player, batch.ActionPoint, batch.WarmSeconds, true);
                        }
                        batch.Continue(); // Preparation never spends stock; real actions replan/debit.
                    });
                    Finish(batch, null);
                }
                catch (Exception error) { Finish(batch, "Ownership handoff failed: " + error.Message); }
            }
        }
        private static void Access(Batch batch, Action action)
        {
            string oldToken = _accessToken; long oldPlayer = _accessPlayer;
            _accessToken = batch.Token; _accessPlayer = batch.PlayerId;
            try { action(); } finally { _accessToken = oldToken; _accessPlayer = oldPlayer; }
        }
        private static void Finish(Batch batch, string failure)
        {
            if (!Batches.Remove(batch.PlayerId)) return;
            foreach (var entry in batch.Entries)
            {
                if (batch.Preparation && failure != null && entry.Sent && !entry.Rejected)
                    Retiring[entry.Id] = new RetiringLease { Entry = entry, Token = batch.Token, Deadline = Time.time + 20f,
                        SafeReturnUntil = batch.Deadline + 12f };
                else Held.Remove(entry.Id);
                var zdo = ZDOMan.instance?.GetZDO(entry.Id);
                if (zdo != null && zdo.GetOwner() == ZNet.GetUID() && !(batch.Preparation && failure != null))
                {
                    if (WorkshopAtomicDebit.IsLocked(zdo) && zdo.GetString(WorkshopRemoteCraft.EscrowKey, "").Length == 0)
                        zdo.Set(QuarantineKey, true);
                    // Pending escrow stays server-owned. Only settled/free stock
                    // can return to a live simulation owner.
                    ZDOMan.instance.ForceSendZDO(entry.Owner, entry.Id);
                    ReturnSettled(zdo);
                }
                try
                {
                    var packet = new ZPackage(); packet.Write(batch.Preparation ? 3 : 1); packet.Write(batch.Token); packet.Write(entry.Id);
                    packet.Write(zdo != null && zdo.GetOwner() == ZNet.GetUID());
                    entry.OwnerRpc.Invoke("VM_WS_Lease", packet);
                }
                catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Lease release delivery deferred: " + error.Message); }
            }
            if (failure != null)
            {
                try { batch.Reject(failure); }
                catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Handoff rejection delivery failed: " + error.Message); }
            }
            // Run the original real request only after locks/batch are cleaned up.
            batch.WaitingAction?.Invoke();
        }
        internal static void ReturnSettled(ZDO record)
        {
            if (record == null || ZNet.instance?.IsServer() != true || record.GetOwner() != ZNet.GetUID() ||
                Held.ContainsKey(record.m_uid) ||
                WorkshopAtomicDebit.IsLocked(record) || record.GetBool(QuarantineKey, false) ||
                record.GetString(WorkshopRemoteCraft.EscrowKey, "").Length != 0) return;
            if (Warm.TryGetValue(record.m_uid, out var warm) && Time.time <= warm.Expires) return;
            Warm.Remove(record.m_uid); record.Set(WarmKey, false);
            long owner = record.GetLong(ReturnOwnerKey, 0);
            if (owner == 0 || owner == ZNet.GetUID() || ZNet.instance.GetPeer(owner)?.IsReady() != true) return;
            string prepared = record.GetString(PreparationKey, "");
            if (prepared.Length != 0)
                record.Set(PreparationReturnKey, WorkshopPreparationReturn.Stamp(prepared, record.GetByteArray(ZDOVars.s_items, null)));
            record.Set(ReturnOwnerKey, 0L);
            record.SetOwner(owner);
            ZDOMan.instance.ForceSendZDO(owner, record.m_uid);
        }
        private static void ReceiveLease(ZRpc rpc, ZPackage packet)
        {
            var net = ZNet.instance;
            if (!WorkshopRemoteCraft.Enabled || net == null || net.IsServer() || rpc != net.GetServerRPC() || packet == null || packet.Size() > 256) return;
            try
            {
                int operation = packet.ReadInt(); string token = packet.ReadString(); ZDOID id = packet.ReadZDOID();
                if (!Guid.TryParseExact(token, "N", out _)) return;
                if (operation == 1 || operation == 3)
                {
                    bool transferred = packet.ReadBool();
                    if (Frozen.TryGetValue(id, out var previous) && previous.Token == token)
                    {
                        // Release can arrive before the owner-change ZDO. Do not
                        // unfreeze old stock until ownership actually left us.
                        ZDO zdo = ZDOMan.instance?.GetZDO(id);
                        previous.Released = true;
                        if (zdo == null || (!previous.Preparation && (!transferred || zdo.GetOwner() != ZNet.GetUID()))) Frozen.Remove(id);
                    }
                    return;
                }
                if (operation != 0 && operation != 2) return;
                long playerId = packet.ReadLong(), serverUid = packet.ReadLong();
                if (serverUid != net.GetServerPeer()?.m_uid) return;
                var chest = ZNetScene.instance?.FindInstance(id)?.GetComponent<Container>();
                if (Frozen.TryGetValue(id, out var existing))
                { Ack(rpc, token, id, existing.Token == token ? existing.Snapshot : null); return; }
                if (chest == null || chest.m_nview?.IsOwner() != true || chest.IsInUse() ||
                    !WorkshopNetworkStorage.IsPlayerChest(chest) || !chest.CheckAccess(playerId) ||
                    !WorkshopNetworkStorage.WardAllows(playerId, chest.transform.position))
                { Ack(rpc, token, id, null); return; }
                var freeze = new Freeze { Token = token, ServerUid = serverUid, Expires = Time.time + 20f, Preparation = operation == 2 };
                Frozen.Add(id, freeze);
                try
                {
                    chest.Save();
                    freeze.Snapshot = chest.m_nview.GetZDO().GetByteArray(ZDOVars.s_items, null);
                    if (freeze.Snapshot == null || freeze.Snapshot.Length == 0 || freeze.Snapshot.Length > WorkshopLeaseRules.MaximumSnapshotBytes)
                        throw new InvalidOperationException("Invalid/oversized chest snapshot.");
                    // The old owner relinquishes authority BEFORE acknowledgment.
                    // Merely freezing CheckAccess is insufficient: destruction or
                    // automatic callbacks must not write its pre-debit inventory.
                    chest.m_nview.GetZDO().SetOwner(serverUid);
                    if (chest.m_nview.IsOwner()) throw new InvalidOperationException("Owner did not relinquish chest.");
                    ZDOMan.instance.ForceSendZDO(serverUid, id);
                    Ack(rpc, token, id, freeze.Snapshot);
                }
                catch
                {
                    if (freeze.Preparation && freeze.Snapshot != null && chest.m_nview.GetZDO()?.GetOwner() == serverUid)
                    {
                        // Relinquishment has happened: never misreport it as an
                        // ordinary refusal or expose the old in-memory inventory.
                        try { Ack(rpc, token, id, freeze.Snapshot); } catch { }
                    }
                    else { Frozen.Remove(id); Ack(rpc, token, id, null); }
                }
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid lease packet: " + error.Message); }
        }
        private static void Ack(ZRpc rpc, string token, ZDOID id, byte[] snapshot)
        {
            var reply = new ZPackage(); reply.Write(token); reply.Write(id); reply.Write(snapshot != null);
            if (snapshot != null) reply.Write(snapshot);
            rpc.Invoke("VM_WS_LeaseAck", reply);
        }
        private static void RecoverPreparationAborts()
        {
            if (ZDOMan.instance == null) return;
            foreach (ZDO record in ZDOMan.instance.m_objectsByID.Values)
            {
                string token = record.GetString(PreparationAbortKey, "");
                if (token.Length == 0 || record.GetOwner() != ZNet.GetUID() || Held.ContainsKey(record.m_uid) ||
                    WorkshopAtomicDebit.IsLocked(record) || record.GetString(WorkshopRemoteCraft.EscrowKey, "").Length != 0) continue;
                long origin = record.GetLong(PreparationOriginKey, 0);
                if (ZNet.instance.GetPeer(origin) != null &&
                    !WorkshopPreparationReturn.Matches(record.GetString(PreparationExpectedKey, ""), token, record.GetByteArray(ZDOVars.s_items, null))) continue;
                // After a long quarantine the old client freeze may have expired.
                // Keep server authority until explicit WarmOpen forces native Load.
                record.Set(ReturnOwnerKey, 0L); record.Set(PreparationKey, token);
                record.Set(PreparationAbortKey, ""); record.Set(PreparationOriginKey, 0L); record.Set(PreparationExpectedKey, "");
                record.Set(QuarantineKey, false); record.Set(WarmKey, true);
                MasteryPlugin.Log.LogInfo("[Workshop70] Preparation quarantine recovered chest=" + record.m_uid + "; current native inventory preserved.");
            }
        }
        private static void ReceiveAck(ZRpc rpc, ZPackage packet)
        {
            var net = ZNet.instance;
            if (!WorkshopRemoteCraft.Enabled || net == null || !net.IsServer() || net.GetPeer(rpc) == null ||
                packet == null || packet.Size() > 263000) return;
            try
            {
                string token = packet.ReadString(); ZDOID id = packet.ReadZDOID(); bool accepted = packet.ReadBool();
                if (Retiring.TryGetValue(id, out var retirement) && retirement.Token == token && retirement.Entry.OwnerRpc == rpc)
                {
                    if (!accepted) { Retiring.Remove(id); Held.Remove(id); return; }
                    byte[] late = packet.ReadByteArray();
                    if (late != null && late.Length > 0 && late.Length <= WorkshopLeaseRules.MaximumSnapshotBytes &&
                        (retirement.Entry.Expected == null || retirement.Entry.Expected.SequenceEqual(late))) retirement.Entry.Expected = late;
                    return;
                }
                foreach (var batch in new List<Batch>(Batches.Values))
                {
                    if (batch.Token != token) continue;
                    Entry entry = batch.Entries.Find(e => e.Id == id && e.OwnerRpc == rpc);
                    if (entry == null) continue;
                    if (!accepted) { entry.Rejected = true; Finish(batch, "Власник скрині відмовився від передавання (скриня зайнята або немає доступу)."); return; }
                    byte[] bytes = packet.ReadByteArray();
                    if (bytes == null || bytes.Length == 0 || bytes.Length > WorkshopLeaseRules.MaximumSnapshotBytes)
                    { Finish(batch, "Некоректний знімок даних власника."); return; }
                    if (entry.Expected != null && !entry.Expected.SequenceEqual(bytes)) { Finish(batch, "Власник змінив зафіксований знімок даних."); return; }
                    entry.Expected = bytes;
                    if (batch.Entries.All(e => e.Expected != null)) WorkshopTiming.Mark(token, true, "Sleases.allAcked");
                    return;
                }
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid owner acknowledgment: " + error.Message); }
        }
    }
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class WorkshopWarmOpenPatch
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
        {
            if (!WorkshopChestLease.DeferOpen(__instance, character, hold, alt)) return true;
            __result = true; return false;
        }
    }
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    internal static class WorkshopLeaseOwnershipPatch
    {
        private static bool Prefix(ZDO __instance, long __0) => !WorkshopChestLease.PinOwner(__instance, __0);
    }
}
