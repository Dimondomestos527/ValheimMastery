using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // The ACK is evidence of relinquishment, never a command to replace world stock.
    internal static class CrossbowTurretHandoff
    {
        private sealed class Pending
        {
            internal string Token;
            internal ZDO Record;
            internal ZRpc Rpc;
            internal long PlayerId, OwnerUid;
            internal ZDOID CharacterId;
            internal float Deadline;
            internal byte[] Expected;
            internal bool RemoveEmpty;
            internal int CustodyRevision;
            internal CrossbowTurretState CustodyState;
        }
        private sealed class Freeze { internal string Token; internal float Deadline; }
        private static readonly Dictionary<ZDOID, Pending> PendingById = new Dictionary<ZDOID, Pending>();
        private static readonly Dictionary<ZDOID, Freeze> Frozen = new Dictionary<ZDOID, Freeze>();
        private static ZNet Session;
        private static float NextTick;
        [ThreadStatic] private static Container Saving;
        internal static bool SavingForHandoff(Container chest) => ReferenceEquals(Saving, chest);
        internal static bool IsPending(ZDO record) => record != null && PendingById.ContainsKey(record.m_uid);
        private static void SaveForHandoff(Container chest)
        {
            Saving = chest;
            try { chest.Save(); } finally { Saving = null; }
        }
        internal static bool LocallyFrozen(ZDO record) => record != null && Frozen.ContainsKey(record.m_uid);
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>("VM_Crossbow70Freeze", ReceiveFreeze);
            rpc.Register<ZPackage>("VM_Crossbow70FreezeAck", ReceiveAck);
        }
        internal static bool Begin(ZRpc rpc, ZDO record, bool removeEmpty = false)
        {
            EnsureSession();
            ZNet net = ZNet.instance;
            if (net?.IsServer() != true || !CrossbowTurretCustody.Marked(record) ||
                CrossbowTurretContainerGuards.Locked(record) ||
                PendingById.ContainsKey(record.m_uid) || PendingById.Count >= 128) return false;
            Player local = rpc == null ? Player.m_localPlayer : null;
            ZNetPeer peer = rpc == null ? null : net.GetPeer(rpc);
            ZDO character = local?.m_nview?.GetZDO() ?? OwnerSkillAuthority.ResolveCharacterData(peer);
            long playerId = character?.GetLong(ZDOVars.s_playerID, 0) ?? 0;
            if (playerId == 0 || record.GetLong(CrossbowTurretCustody.OwnerKey, 0) != playerId ||
                character.GetBool(ZDOVars.s_dead, false) || (character.GetPosition() - record.GetPosition()).sqrMagnitude > 36f ||
                (!removeEmpty && (!(local != null ? OwnerSkillAuthority.Has(local, Skills.SkillType.Crossbows, 70) :
                    OwnerSkillAuthority.Has(rpc, character.m_uid, playerId, Skills.SkillType.Crossbows, 70)) ||
                    CrossbowTurretCustody.AnotherActive(playerId, record.m_uid)))) return false;
            foreach (var other in PendingById.Values) if (other.PlayerId == playerId) return false;
            long ownerUid = record.GetOwner();
            // Native supply UI may delegate only to this authenticated owner, not a third peer.
            if (ownerUid != ZNet.GetUID() && ownerUid != peer?.m_uid) return false;
            var pending = new Pending { Token = Guid.NewGuid().ToString("N"), Record = record, Rpc = rpc,
                CharacterId = character.m_uid, PlayerId = playerId, OwnerUid = ownerUid, Deadline = Time.time + 8f, RemoveEmpty = removeEmpty,
                CustodyRevision = record.GetInt(CrossbowTurretCustody.RevisionKey, -1), CustodyState = CrossbowTurretCustody.State(record) };
            PendingById.Add(record.m_uid, pending);
            if (ownerUid == ZNet.GetUID())
            {
                Container chest = ZNetScene.instance?.FindInstance(record.m_uid)?.GetComponent<Container>();
                if (chest == null || chest.m_nview?.IsOwner() != true) { PendingById.Remove(record.m_uid); return false; }
                CloseUi(chest);
                if (chest.IsInUse()) { PendingById.Remove(record.m_uid); return false; }
                SaveForHandoff(chest); pending.Expected = record.GetByteArray(ZDOVars.s_items, null);
            }
            else
            {
                var packet = new ZPackage(); packet.Write(0); packet.Write(pending.Token); packet.Write(record.m_uid);
                packet.Write(playerId); packet.Write(ZNet.GetUID()); rpc.Invoke("VM_Crossbow70Freeze", packet);
            }
            return true;
        }
        private static void CloseUi(Container chest)
        {
            if (InventoryGui.instance != null && InventoryGui.instance.m_currentContainer == chest)
                InventoryGui.instance.CloseContainer();
        }
        private static void ReceiveFreeze(ZRpc rpc, ZPackage packet)
        {
            EnsureSession();
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer() || rpc != net.GetServerRPC() || packet == null || packet.Size() > 256) return;
            try
            {
                int operation = packet.ReadInt(); string token = packet.ReadString(); ZDOID id = packet.ReadZDOID();
                if (token.Length != 32) return;
                if (operation == 1)
                {
                    if (Frozen.TryGetValue(id, out var prior) && prior.Token == token) Frozen.Remove(id);
                    return;
                }
                if (operation != 0 || Frozen.Count >= 128 || Frozen.ContainsKey(id)) return;
                long playerId = packet.ReadLong(), serverUid = packet.ReadLong();
                Player local = Player.m_localPlayer;
                Container chest = ZNetScene.instance?.FindInstance(id)?.GetComponent<Container>();
                ZDO record = chest?.m_nview?.GetZDO();
                if (serverUid != net.GetServerPeer()?.m_uid || local == null || local.GetPlayerID() != playerId ||
                    !CrossbowTurretCustody.Marked(record) || record.GetLong(CrossbowTurretCustody.OwnerKey, 0) != playerId ||
                    chest.m_nview.IsOwner() != true || CrossbowTurretRules.LocksStock(CrossbowTurretCustody.State(record))) return;
                CloseUi(chest); if (chest.IsInUse()) return;
                chest.Save();
                byte[] bytes = record.GetByteArray(ZDOVars.s_items, null);
                if (bytes == null || bytes.Length == 0 || bytes.Length > CrossbowTurretRules.MaximumSnapshotBytes) return;
                Frozen.Add(id, new Freeze { Token = token, Deadline = Time.time + 20f });
                record.SetOwner(serverUid);
                if (chest.m_nview.IsOwner()) { Frozen.Remove(id); return; }
                ZDOMan.instance.ForceSendZDO(serverUid, id);
                var reply = new ZPackage(); reply.Write(token); reply.Write(id); reply.Write(bytes);
                rpc.Invoke("VM_Crossbow70FreezeAck", reply);
            }
            catch (Exception exception) { MasteryPlugin.Log.LogWarning("[Crossbow70] Freeze rejected: " + exception.GetType().Name); }
        }
        private static void ReceiveAck(ZRpc rpc, ZPackage packet)
        {
            EnsureSession();
            if (ZNet.instance?.IsServer() != true || packet == null || packet.Size() > 263000) return;
            try
            {
                string token = packet.ReadString(); ZDOID id = packet.ReadZDOID();
                if (!PendingById.TryGetValue(id, out var pending) || pending.Rpc != rpc || pending.Token != token ||
                    Time.time >= pending.Deadline || ZNet.instance.GetPeer(rpc)?.m_uid != pending.OwnerUid) return;
                ZDO character = OwnerSkillAuthority.ResolveCharacterData(ZNet.instance.GetPeer(rpc));
                if (character?.m_uid != pending.CharacterId || character.GetLong(ZDOVars.s_playerID, 0) != pending.PlayerId) return;
                byte[] bytes = packet.ReadByteArray();
                if (bytes == null || bytes.Length == 0 || bytes.Length > CrossbowTurretRules.MaximumSnapshotBytes) return;
                if (pending.Expected != null && !pending.Expected.SequenceEqual(bytes)) return;
                pending.Expected = bytes;
            }
            catch (Exception) { /* malformed packet cannot activate or overwrite inventory */ }
        }
        private static void EnsureSession()
        {
            if (Session == ZNet.instance) return;
            PendingById.Clear(); Frozen.Clear(); Session = ZNet.instance; NextTick = 0f;
        }
        internal static void Tick()
        {
            EnsureSession();
            if (Session == null || Time.time < NextTick) return;
            NextTick = Time.time + .2f;
            foreach (var pair in new List<KeyValuePair<ZDOID, Freeze>>(Frozen))
                if (Time.time >= pair.Value.Deadline) Frozen.Remove(pair.Key);
            if (!Session.IsServer()) return;
            foreach (var pair in new List<KeyValuePair<ZDOID, Pending>>(PendingById))
            {
                Pending pending = pair.Value; ZDO record = ZDOMan.instance?.GetZDO(pair.Key);
                ZDO character = pending.Rpc == null ? Player.m_localPlayer?.m_nview?.GetZDO() :
                    OwnerSkillAuthority.ResolveCharacterData(Session.GetPeer(pending.Rpc));
                bool expired = Time.time >= pending.Deadline || character?.m_uid != pending.CharacterId ||
                    character.GetLong(ZDOVars.s_playerID, 0) != pending.PlayerId || !ReferenceEquals(record, pending.Record) ||
                    record.GetInt(CrossbowTurretCustody.RevisionKey, -1) != pending.CustodyRevision ||
                    CrossbowTurretCustody.State(record) != pending.CustodyState;
                if (!expired)
                {
                    bool eligible = pending.Rpc == null ?
                        OwnerSkillAuthority.Has(Player.m_localPlayer, Skills.SkillType.Crossbows, 70) :
                        OwnerSkillAuthority.Has(pending.Rpc, pending.CharacterId, pending.PlayerId, Skills.SkillType.Crossbows, 70);
                    expired = character.GetBool(ZDOVars.s_dead, false) ||
                        (character.GetPosition() - pending.Record.GetPosition()).sqrMagnitude > 36f || (!pending.RemoveEmpty && !eligible);
                }
                if (!expired && ReferenceEquals(record, pending.Record) && record.GetOwner() == ZNet.GetUID() &&
                    pending.Expected != null && pending.Expected.SequenceEqual(record.GetByteArray(ZDOVars.s_items, null) ?? new byte[0]))
                {
                    Container definition = ZNetScene.instance?.FindInstance(pair.Key)?.GetComponent<Container>() ??
                        ZNetScene.instance?.GetPrefab(record.GetPrefab())?.GetComponent<Container>();
                    Container loaded = ZNetScene.instance?.FindInstance(pair.Key)?.GetComponent<Container>();
                    if (loaded != null && loaded.IsInUse()) continue;
                    int revision = record.GetInt(CrossbowTurretCustody.RevisionKey, -1);
                    if (pending.RemoveEmpty)
                    {
                        if (CrossbowTurretStock.Lossless(pending.Expected, definition))
                        {
                            Complete(pair.Key, pending, "return_custody");
                            CrossbowTurretCommands.RemoveEmpty(record);
                        }
                        else Complete(pair.Key, pending, "invalid_cargo");
                        continue;
                    }
                    if (CrossbowTurretStock.TryRead(pending.Expected, definition, out _, out string reason) &&
                        CrossbowTurretCustody.TryTransition(record, revision, pending.Expected, CrossbowTurretState.Frozen, definition, out reason) &&
                        CrossbowTurretCustody.TryTransition(record, revision + 1, pending.Expected, CrossbowTurretState.Active, definition, out reason))
                    { Complete(pair.Key, pending, "active"); continue; }
                    if (CrossbowTurretCustody.State(record) == CrossbowTurretState.Frozen)
                        CrossbowTurretCustody.TryTransition(record, revision + 1, pending.Expected, CrossbowTurretState.Recovery, definition, out _);
                    Complete(pair.Key, pending, "stock_rejected:" + reason); continue;
                }
                if (expired) Complete(pair.Key, pending, "handoff_timeout");
            }
        }
        private static void Complete(ZDOID id, Pending pending, string result)
        {
            ZDO record = ZDOMan.instance?.GetZDO(id);
            if (CrossbowTurretCustody.Marked(record) && record.GetOwner() == ZNet.GetUID())
            {
                Container loaded = ZNetScene.instance?.FindInstance(id)?.GetComponent<Container>();
                Container definition = loaded ?? ZNetScene.instance?.GetPrefab(record.GetPrefab())?.GetComponent<Container>();
                byte[] bytes = record.GetByteArray(ZDOVars.s_items, null);
                CrossbowTurretStock.TryRead(bytes, definition, out _, out string reason);
                // Validate unloaded stock too: only cache refresh needs a live Container.
                if (reason == "non_lossless_snapshot" || reason == "malformed_snapshot" ||
                    reason == "invalid_snapshot" || reason == "missing_item_definition")
                {
                    // Native Load skips unknown prefabs. Keep the original bytes locked
                    // instead of exposing a lossy cache to a later native Save.
                    record.Set(CrossbowTurretCustody.QuarantineKey, true);
                    result = "custody_quarantined:" + reason;
                }
                else if (loaded != null)
                {
                    try { loaded.Load(); }
                    catch (Exception)
                    {
                        record.Set(CrossbowTurretCustody.QuarantineKey, true);
                        result = "custody_quarantined:cache_reload";
                    }
                }
            }
            // Synchronize/quarantine the cache BEFORE dropping the pending writer lock.
            PendingById.Remove(id);
            if (pending.Rpc != null && Session.GetPeer(pending.Rpc) != null)
            {
                var packet = new ZPackage(); packet.Write(1); packet.Write(pending.Token); packet.Write(id);
                pending.Rpc.Invoke("VM_Crossbow70Freeze", packet);
            }
            MasteryPlugin.Log.LogInfo("[Crossbow70] handoff=" + result + " turret=" + id + " owner=" + pending.PlayerId);
            if (pending.Rpc == null || Session.GetPeer(pending.Rpc) != null) CrossbowTurretCommands.CompleteFeedback(pending.Rpc, result);
        }
    }
    [HarmonyPatch(typeof(ZNet), "Update")]
    internal static class CrossbowTurretHandoffTickPatch
    { private static void Postfix() => CrossbowTurretHandoff.Tick(); }
}
