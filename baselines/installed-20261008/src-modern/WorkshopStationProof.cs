using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Ask the station's actual simulation owner, not an arbitrary requester.
    // World identity, station type/level, range and wards remain server checks.
    internal static class WorkshopStationProof
    {
        private sealed class Query
        {
            internal string Token;
            internal WorkshopActor Actor;
            internal ZDO Record;
            internal ZRpc OwnerRpc;
            internal long Owner;
            internal float Deadline;
            internal Action Continue;
            internal Action<string> Reject;
        }
        private sealed class Proof
        {
            internal ZDO Record;
            internal long Owner;
            internal float Expires;
            internal bool Prewarmed;
        }
        private sealed class WarmQuery
        {
            internal string Token;
            internal WorkshopActor Actor;
            internal ZRpc RequesterRpc, OwnerRpc;
            internal ZDO Record;
            internal long Owner;
            internal Recipe Recipe;
            internal int Quality;
            internal float Deadline;
        }
        private static readonly Dictionary<long, Query> Pending = new Dictionary<long, Query>();
        private static readonly Dictionary<ZDOID, Proof> Proofs = new Dictionary<ZDOID, Proof>();
        // Warm queries are deliberately separate from real action Pending queries.
        private static readonly Dictionary<string, WarmQuery> WarmQueries = new Dictionary<string, WarmQuery>();
        private static readonly Dictionary<ZDOID, string> WarmByStation = new Dictionary<ZDOID, string>();
        private static readonly Dictionary<ZRpc, Queue<float>> WarmRate = new Dictionary<ZRpc, Queue<float>>();
        private static readonly Dictionary<ZDOID, float> ClientWarmSent = new Dictionary<ZDOID, float>();
        private static ZNet Session;
        private static ZNet ClientSession;
        private static float _nextClientWarmPrune;
        private static float _nextWarmSweep;
        private static float _nextClientSkillPublication;
        private static bool Verbose => MasteryPlugin.Settings?.VerboseLogging?.Value == true;
        private static void WarmLog(string message)
        {
            if (Verbose) MasteryPlugin.Log.LogInfo("[Workshop70] Station prewarm " + message);
        }
        internal static bool Busy(long actor, string token, out bool same)
        {
            bool busy = Pending.TryGetValue(actor, out Query query);
            same = busy && query.Token == token; return busy;
        }
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<ZPackage>("VM_WS_StationQuery", ReceiveQuery);
            rpc.Register<ZPackage>("VM_WS_StationProof", ReceiveProof);
            rpc.Register<ZPackage>("VM_WS_StationWarmRequest", ReceiveWarmRequest);
            rpc.Register<ZPackage>("VM_WS_StationWarmQuery", ReceiveWarmQuery);
            rpc.Register<ZPackage>("VM_WS_StationWarmProof", ReceiveWarmProof);
        }
        internal static void Tick()
        {
            if (Session != ZNet.instance)
            {
                Pending.Clear(); Proofs.Clear(); WarmQueries.Clear(); WarmByStation.Clear(); WarmRate.Clear();
                Session = ZNet.instance; _nextWarmSweep = 0f;
            }
            foreach (var pair in new List<KeyValuePair<long, Query>>(Pending))
                if (Time.time > pair.Value.Deadline || !pair.Value.Actor.Available ||
                    ZNet.instance?.GetPeer(pair.Value.OwnerRpc)?.m_uid != pair.Value.Owner)
                    Finish(pair.Key, false, "Не вдалося підтвердити умови роботи станка.");
            foreach (var pair in new List<KeyValuePair<ZDOID, Proof>>(Proofs))
                if (Time.time > pair.Value.Expires) Proofs.Remove(pair.Key);
            if (Time.time < _nextWarmSweep) return;
            _nextWarmSweep = Time.time + .5f;
            if (WarmQueries.Count > 0)
            foreach (var pair in new List<KeyValuePair<string, WarmQuery>>(WarmQueries))
                if (Time.time > pair.Value.Deadline) FinishWarm(pair.Key, "expired");
                else if (!WarmActorStillValid(pair.Value)) FinishWarm(pair.Key, "actor-no-longer-valid");
                else if (ZNet.instance?.GetPeer(pair.Value.OwnerRpc)?.m_uid != pair.Value.Owner) FinishWarm(pair.Key, "owner-changed");
            if (ZNet.instance?.IsServer() == true && WarmRate.Count > 0)
                foreach (var rpc in new List<ZRpc>(WarmRate.Keys))
                    if (ZNet.instance.GetPeer(rpc) == null) WarmRate.Remove(rpc);
        }
        internal static bool Usable(ZDO record, CraftingStation definition)
        {
            if (!definition.m_craftRequireRoof && !definition.m_craftRequireFire) return true;
            CraftingStation loaded = ZNetScene.instance?.FindInstance(record.m_uid)?.GetComponent<CraftingStation>();
            if (record.GetOwner() == ZNet.GetUID() && loaded != null) return WorkshopRemoteCraft.StationUsable(loaded);
            return Proofs.TryGetValue(record.m_uid, out Proof proof) && ReferenceEquals(proof.Record, record) &&
                record.GetOwner() == proof.Owner && Time.time <= proof.Expires;
        }
        internal static void Prewarm(InventoryGui gui)
        {
            ZNet net = ZNet.instance;
            Player player = Player.m_localPlayer;
            if (!WorkshopRemoteCraft.Ready || net == null || net.IsServer() || player == null || gui == null ||
                !InventoryGui.IsVisible() || gui.m_currentContainer != null || WorkshopRemoteCraft.ClientBusy ||
                WorkshopRecovery.Outstanding || player.IsDead() || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70) ||
                net.GetServerRPC() == null) return;
            if (ClientSession != net) { ClientWarmSent.Clear(); ClientSession = net; _nextClientSkillPublication = 0f; }

            Recipe recipe = gui.m_selectedRecipe.Recipe;
            if (recipe == null) return;
            ItemDrop.ItemData upgrade = gui.m_selectedRecipe.ItemData;
            int quality = upgrade == null ? 1 : upgrade.m_quality + 1;
            CraftingStation required = recipe.GetRequiredStation(quality);
            CraftingStation current = player.GetCurrentCraftingStation();
            ZDO record = current?.GetComponent<ZNetView>()?.GetZDO();
            if (required == null || current == null || current.m_upgrader || record == null ||
                current.m_name != required.m_name || Vector3.Distance(player.transform.position, current.transform.position) >= current.m_useDistance ||
                (!current.m_craftRequireRoof && !current.m_craftRequireFire)) return;

            if (Time.time >= _nextClientWarmPrune)
            {
                _nextClientWarmPrune = Time.time + 5f;
                foreach (var pair in new List<KeyValuePair<ZDOID, float>>(ClientWarmSent))
                    if (Time.time - pair.Value >= 5f) ClientWarmSent.Remove(pair.Key);
            }
            if (ClientWarmSent.TryGetValue(record.m_uid, out float sent) && Time.time - sent < 5f) return;
            // Requirement decoding is useful once per warm request, not for
            // every crafting-panel refresh while inspecting the same station.
            if (!WorkshopRemoteCraft.TryCosts(recipe, quality, 1, out _)) return;
            if (ClientWarmSent.Count >= 256)
            {
                ZDOID oldestId = ZDOID.None; float oldest = float.MaxValue;
                foreach (var pair in ClientWarmSent) if (pair.Value < oldest) { oldest = pair.Value; oldestId = pair.Key; }
                if (oldestId != ZDOID.None) ClientWarmSent.Remove(oldestId);
            }

            string token = Guid.NewGuid().ToString("N");
            var packet = new ZPackage(); packet.Write(token); packet.Write(record.m_uid); packet.Write(recipe.name); packet.Write(quality);
            try
            {
                // A newly opened station may be the first RPC after join. Publish
                // the owner-bound skill snapshot first on this reliable RPC, but
                // never more often than the existing two-second skill cadence.
                bool publishedSkills = Time.time >= _nextClientSkillPublication;
                if (publishedSkills)
                {
                    OwnerSkillAuthority.SendNow();
                    _nextClientSkillPublication = Time.time + 2f;
                }
                net.GetServerRPC().Invoke("VM_WS_StationWarmRequest", packet);
                ClientWarmSent[record.m_uid] = Time.time;
                WarmLog("client sent token=" + token + " station=" + record.m_uid + " ownerSkills=" + (publishedSkills ? "published" : "cadence-suppressed"));
            }
            catch (Exception error)
            {
                // Warmup is optional; avoid retry/log churn if transport is down.
                ClientWarmSent[record.m_uid] = Time.time;
                WarmLog("client send failed token=" + token + " reason=" + error.GetType().Name);
            }
        }
        internal static void Acquire(WorkshopActor actor, string token, ZDOID id, Action continuation, Action<string> reject)
        {
            CancelWarmForActor(actor.GetPlayerID());
            var entry = WorkshopWorldRecords.Find(id);
            if (id == ZDOID.None || entry?.Station != null && !entry.Station.m_craftRequireRoof && !entry.Station.m_craftRequireFire)
            { continuation(); return; }
            if (entry?.Station == null) { reject("Робочий станок більше не існує."); return; }
            if (Proofs.TryGetValue(id, out Proof cached) && ReferenceEquals(cached.Record, entry.Data) &&
                cached.Owner == entry.Data.GetOwner() && Time.time <= cached.Expires &&
                ZNet.instance.GetPeer(cached.Owner)?.IsReady() == true)
            {
                if (cached.Prewarmed) WarmLog("cache hit transaction=" + token + " station=" + id);
                continuation(); return;
            }
            CraftingStation loaded = ZNetScene.instance.FindInstance(id)?.GetComponent<CraftingStation>();
            if (entry.Data.GetOwner() == ZNet.GetUID() && loaded != null)
            {
                if (WorkshopRemoteCraft.StationUsable(loaded)) continuation(); else reject("Станку потрібні дах або вогонь.");
                return;
            }
            var peer = ZNet.instance.GetPeer(entry.Data.GetOwner());
            if (peer?.IsReady() != true || Pending.ContainsKey(actor.GetPlayerID()))
            { reject("Власник робочого станка поки недоступний."); return; }
            var query = new Query { Token = token, Actor = actor, Record = entry.Data, Owner = peer.m_uid,
                OwnerRpc = peer.m_rpc, Deadline = Time.time + 4f, Continue = continuation, Reject = reject };
            Pending.Add(actor.GetPlayerID(), query);
            Proofs.Remove(id);
            var packet = new ZPackage(); packet.Write(token); packet.Write(actor.GetPlayerID()); packet.Write(id);
            try { peer.m_rpc.Invoke("VM_WS_StationQuery", packet); }
            catch { Finish(actor.GetPlayerID(), false, "Не вдалося зв’язатися з власником станка."); }
        }
        private static void ReceiveWarmRequest(ZRpc rpc, ZPackage packet)
        {
            ZNet net = ZNet.instance;
            if (!WorkshopRemoteCraft.Enabled || net?.IsServer() != true || rpc == null || net.GetPeer(rpc)?.IsReady() != true ||
                packet == null || packet.Size() > 512 || !AllowWarmRate(rpc)) return;
            try
            {
                string token = packet.ReadString(); ZDOID id = packet.ReadZDOID(); string recipeName = packet.ReadString(); int quality = packet.ReadInt();
                if (!Guid.TryParseExact(token, "N", out _) || id == ZDOID.None || string.IsNullOrEmpty(recipeName) || recipeName.Length > 256)
                { WarmLog("server rejected token=" + token + " reason=malformed"); return; }
                WorkshopActor actor = WorkshopActor.Resolve(rpc);
                if (actor == null || !actor.HasCrafting70 || actor.IsDead() || actor.IsTeleporting() ||
                    Pending.ContainsKey(actor.GetPlayerID()) || WorkshopChestLease.Pending(actor.GetPlayerID()))
                { WarmLog("server rejected token=" + token + " reason=actor-or-action-state"); return; }
                Recipe recipe = null;
                foreach (Recipe candidate in ObjectDB.instance?.m_recipes ?? new List<Recipe>())
                    if (candidate != null && candidate.name == recipeName) { recipe = candidate; break; }
                if (recipe == null || !WorkshopRemoteCraft.TryCosts(recipe, quality, 1, out _) ||
                    !WorkshopRemoteCraft.ValidRecipeStation(actor, recipe, quality, id, false))
                { WarmLog("server rejected token=" + token + " reason=recipe-or-station-validation"); return; }
                var entry = WorkshopWorldRecords.Find(id);
                if (entry?.Station == null || entry.Station.m_upgrader ||
                    (!entry.Station.m_craftRequireRoof && !entry.Station.m_craftRequireFire))
                { WarmLog("server rejected token=" + token + " reason=station-proof-not-required"); return; }
                if (Proofs.TryGetValue(id, out Proof cached) && ReferenceEquals(cached.Record, entry.Data) &&
                    cached.Owner == entry.Data.GetOwner() && Time.time <= cached.Expires && net.GetPeer(cached.Owner)?.IsReady() == true)
                { WarmLog("server skipped token=" + token + " reason=proof-already-cached"); return; }
                Proofs.Remove(id);
                if (WarmByStation.ContainsKey(id) || WarmQueries.Count >= 32)
                { WarmLog("server rejected token=" + token + " reason=" + (WarmByStation.ContainsKey(id) ? "station-already-pending" : "pending-capacity")); return; }

                long owner = entry.Data.GetOwner();
                ZNetPeer ownerPeer = net.GetPeer(owner);
                if (owner == ZNet.GetUID())
                {
                    CraftingStation loaded = ZNetScene.instance?.FindInstance(id)?.GetComponent<CraftingStation>();
                    if (loaded != null && WorkshopRemoteCraft.StationUsable(loaded))
                    {
                        Proofs[id] = new Proof { Record = entry.Data, Owner = owner, Expires = Time.time + 8f, Prewarmed = true };
                        WarmLog("server accepted token=" + token + " station=" + id + " path=local-owner");
                    }
                    else WarmLog("server rejected token=" + token + " reason=local-station-unavailable");
                    return;
                }
                if (ownerPeer?.IsReady() != true || ownerPeer.m_rpc == null)
                { WarmLog("server rejected token=" + token + " reason=owner-not-ready"); return; }
                var query = new WarmQuery { Token = token, Actor = actor, RequesterRpc = rpc, OwnerRpc = ownerPeer.m_rpc,
                    Record = entry.Data, Owner = owner, Recipe = recipe, Quality = quality, Deadline = Time.time + 2f };
                WarmQueries.Add(token, query); WarmByStation.Add(id, token);
                var ownerPacket = new ZPackage(); ownerPacket.Write(token); ownerPacket.Write(id);
                try
                {
                    ownerPeer.m_rpc.Invoke("VM_WS_StationWarmQuery", ownerPacket);
                    WarmLog("server accepted token=" + token + " station=" + id + " owner=" + owner + " path=owner-query");
                }
                catch { FinishWarm(token, "owner-query-send-failed"); }
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid station prewarm request: " + error.Message); }
        }
        private static void ReceiveWarmQuery(ZRpc rpc, ZPackage packet)
        {
            ZNet net = ZNet.instance;
            if (!WorkshopRemoteCraft.Enabled || net == null || net.IsServer() || rpc != net.GetServerRPC() || packet == null || packet.Size() > 256) return;
            try
            {
                string token = packet.ReadString(); ZDOID id = packet.ReadZDOID();
                if (!Guid.TryParseExact(token, "N", out _) || id == ZDOID.None) return;
                CraftingStation station = ZNetScene.instance?.FindInstance(id)?.GetComponent<CraftingStation>();
                ZDO record = station?.GetComponent<ZNetView>()?.GetZDO();
                bool usable = station != null && !station.m_upgrader && (station.m_craftRequireRoof || station.m_craftRequireFire) &&
                    record != null && record.m_uid == id && station.GetComponent<ZNetView>()?.IsOwner() == true &&
                    WorkshopRemoteCraft.StationUsable(station);
                var reply = new ZPackage(); reply.Write(token); reply.Write(id); reply.Write(usable);
                rpc.Invoke("VM_WS_StationWarmProof", reply);
                WarmLog("owner replied token=" + token + " station=" + id + " usable=" + usable);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid station prewarm query: " + error.Message); }
        }
        private static void ReceiveWarmProof(ZRpc rpc, ZPackage packet)
        {
            ZNet net = ZNet.instance;
            if (!WorkshopRemoteCraft.Enabled || net?.IsServer() != true || net.GetPeer(rpc) == null || packet == null || packet.Size() > 256) return;
            try
            {
                string token = packet.ReadString(); ZDOID id = packet.ReadZDOID(); bool usable = packet.ReadBool();
                if (!WarmQueries.TryGetValue(token, out WarmQuery query) || query.Record.m_uid != id || query.OwnerRpc != rpc) return;
                bool actorStillValid = WarmActorStillValid(query);
                bool accepted = usable && actorStillValid && Time.time <= query.Deadline &&
                    !Pending.ContainsKey(query.Actor.GetPlayerID()) && !WorkshopChestLease.Pending(query.Actor.GetPlayerID()) &&
                    ReferenceEquals(ZDOMan.instance?.GetZDO(id), query.Record) && query.Record.GetOwner() == query.Owner &&
                    net.GetPeer(rpc)?.m_uid == query.Owner;
                if (accepted)
                    Proofs[id] = new Proof { Record = query.Record, Owner = query.Owner, Expires = Time.time + 8f, Prewarmed = true };
                WarmLog("server proof " + (accepted ? "accepted" : "rejected") + " token=" + token + " station=" + id +
                    " ownerUsable=" + usable + " actorValid=" + actorStillValid + " beforeDeadline=" + (Time.time <= query.Deadline));
                FinishWarm(token, accepted ? "proof-accepted" : "proof-rejected");
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid station prewarm proof: " + error.Message); }
        }
        private static bool WarmActorStillValid(WarmQuery query)
        {
            if (query?.Actor == null || !query.Actor.Available || !query.Actor.HasCrafting70 ||
                query.Actor.IsDead() || query.Actor.IsTeleporting() || ZNet.instance?.GetPeer(query.RequesterRpc)?.IsReady() != true) return false;
            WorkshopActor current = WorkshopActor.Resolve(query.RequesterRpc);
            return current != null && current.CharacterId == query.Actor.CharacterId &&
                current.GetPlayerID() == query.Actor.GetPlayerID() &&
                WorkshopRemoteCraft.ValidRecipeStation(current, query.Recipe, query.Quality, query.Record.m_uid, false);
        }
        private static bool AllowWarmRate(ZRpc rpc)
        {
            if (!WarmRate.TryGetValue(rpc, out Queue<float> arrivals)) WarmRate[rpc] = arrivals = new Queue<float>();
            while (arrivals.Count > 0 && Time.time - arrivals.Peek() >= 1f) arrivals.Dequeue();
            if (arrivals.Count >= 2) return false;
            arrivals.Enqueue(Time.time); return true;
        }
        private static void FinishWarm(string token, string reason = null)
        {
            if (!WarmQueries.TryGetValue(token, out WarmQuery query)) return;
            WarmQueries.Remove(token);
            if (WarmByStation.TryGetValue(query.Record.m_uid, out string current) && current == token) WarmByStation.Remove(query.Record.m_uid);
            if (reason != null) WarmLog("finished token=" + token + " station=" + query.Record.m_uid + " reason=" + reason);
        }
        private static void CancelWarmForActor(long playerId)
        {
            foreach (var pair in new List<KeyValuePair<string, WarmQuery>>(WarmQueries))
                if (pair.Value.Actor.GetPlayerID() == playerId) FinishWarm(pair.Key, "cancelled-by-real-request");
        }
        private static void ReceiveQuery(ZRpc rpc, ZPackage packet)
        {
            if (!WorkshopRemoteCraft.Enabled || ZNet.instance == null || ZNet.instance.IsServer() ||
                rpc != ZNet.instance.GetServerRPC() || packet == null || packet.Size() > 256) return;
            try
            {
                string token = packet.ReadString(); long actor = packet.ReadLong(); ZDOID id = packet.ReadZDOID();
                if (!Guid.TryParseExact(token, "N", out _)) return;
                CraftingStation station = ZNetScene.instance.FindInstance(id)?.GetComponent<CraftingStation>();
                bool usable = station != null && station.GetComponent<ZNetView>()?.IsOwner() == true &&
                    WorkshopRemoteCraft.StationUsable(station);
                var reply = new ZPackage(); reply.Write(token); reply.Write(actor); reply.Write(id); reply.Write(usable);
                rpc.Invoke("VM_WS_StationProof", reply);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid station query: " + error.Message); }
        }
        private static void ReceiveProof(ZRpc rpc, ZPackage packet)
        {
            if (!WorkshopRemoteCraft.Enabled || ZNet.instance?.IsServer() != true || packet == null || packet.Size() > 256) return;
            try
            {
                string token = packet.ReadString(); long actor = packet.ReadLong(); ZDOID id = packet.ReadZDOID(); bool usable = packet.ReadBool();
                if (!Pending.TryGetValue(actor, out Query query) || query.Token != token || query.OwnerRpc != rpc || query.Record.m_uid != id) return;
                usable = usable && Time.time <= query.Deadline && query.Actor.Available &&
                    ReferenceEquals(ZDOMan.instance.GetZDO(id), query.Record) && query.Record.GetOwner() == query.Owner &&
                    ZNet.instance.GetPeer(rpc)?.m_uid == query.Owner;
                if (usable) Proofs[id] = new Proof { Record = query.Record, Owner = query.Owner, Expires = Time.time + 8f };
                Finish(actor, usable, "Станку потрібні дах або вогонь, або його власник змінився.");
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Invalid station proof: " + error.Message); }
        }
        private static void Finish(long actor, bool accepted, string reason)
        {
            if (!Pending.TryGetValue(actor, out Query query)) return;
            Pending.Remove(actor);
            if (accepted) query.Continue(); else query.Reject(reason);
        }
    }
}
