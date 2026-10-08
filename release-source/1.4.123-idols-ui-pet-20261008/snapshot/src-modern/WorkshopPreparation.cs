using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimMastery
{
    // Optional ownership preparation, deliberately outside request admission,
    // escrow and receipts. Final actions always replan and CAS current stock.
    internal static class WorkshopPreparation
    {
        private static readonly WorkshopPreparationCache Cache = new WorkshopPreparationCache();
        private static readonly Dictionary<long, float> Rate = new Dictionary<long, float>();
        private static ZNet _session;
        private static ZRpc _rpc;
        private static float _nextTick;
        private static long _character;
        private static string _buildKey;
        private static float _buildUntil;
        internal static void BuildActivity() => _buildUntil = Time.time + 30f;
        internal static void Register(ZRpc rpc) => rpc.Register<ZPackage>("VM_WS_Prepare", Receive);
        internal static void Tick()
        {
            var net = ZNet.instance;
            var rpc = net?.GetServerRPC();
            var player = Player.m_localPlayer;
            long character = player?.GetPlayerID() ?? 0;
            if (_session != net || _rpc != rpc || _character != character)
            { Cache.Clear(); Rate.Clear(); _session = net; _rpc = rpc; _character = character; _nextTick = 0f; _buildKey = null; _buildUntil = 0f; }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + .1f;
            if (!WorkshopRemoteCraft.PreparationReady || player == null || player.IsDead() || player.IsTeleporting() ||
                WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding || player.NoCostCheat() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) { Cache.Idle(); return; }
            var gui = InventoryGui.instance;
            string name; int quality, amount; ZDOID station = ZDOID.None;
            Vector3 point = player.transform.position;
            Dictionary<string, int> costs;
            // Observe vanilla's frozen craft recipe, not the subsequently selected row.
            if (gui != null && InventoryGui.IsVisible() && gui.m_craftTimer >= 0f && gui.m_craftRecipe != null)
            {
                quality = gui.m_craftUpgradeItem == null ? 1 : gui.m_craftUpgradeItem.m_quality + 1;
                amount = gui.m_multiCrafting ? gui.m_multiCraftAmount : 1;
                var recipe = gui.m_craftRecipe;
                if (player.GetCurrentCraftingStation()?.m_upgrader == true ||
                    ZoneSystem.instance?.GetGlobalKey(GlobalKeys.NoCraftCost) == true ||
                    !WorkshopRemoteCraft.TryCosts(recipe, quality, amount, out costs) ||
                    !player.RequiredCraftingStation(recipe, quality, true)) { Cache.Idle(); return; }
                name = recipe.name;
                station = player.GetCurrentCraftingStation()?.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None;
            }
            else
            {
                var piece = player.GetSelectedPiece();
                if (!WorkshopBuildBridge.ClientEligible(player, piece) ||
                    !WorkshopRemoteCraft.TryResourceCosts(piece.m_resources, 0, 1, out costs)) { Cache.Idle(); return; }
                name = "@build:" + piece.gameObject.name; quality = 0; amount = 1;
                if (player.m_placementGhost != null) point = player.m_placementGhost.transform.position;
            }
            var personal = costs.ToDictionary(c => c.Key, c => Math.Min(c.Value, player.GetInventory().CountItems(c.Key, -1, false)), StringComparer.Ordinal);
            if (costs.All(c => personal[c.Key] == c.Value)) { Cache.Idle(); return; }
            string key = name + ":" + quality + ":" + amount + ":" + station + ":" +
                Mathf.FloorToInt(point.x / 2f) + ":" + Mathf.FloorToInt(point.y / 2f) + ":" + Mathf.FloorToInt(point.z / 2f) + ":" +
                string.Join(";", costs.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + "=" + c.Value + "/" + personal[c.Key]));
            if (quality == 0)
            {
                if (_buildKey != key) { _buildKey = key; BuildActivity(); }
                if (Time.time > _buildUntil) { Cache.Idle(); return; }
            }
            if (!Cache.ShouldSend(key, Time.time)) return;
            var packet = new ZPackage(); packet.Write(Guid.NewGuid().ToString("N")); packet.Write(name);
            packet.Write(quality); packet.Write(amount); packet.Write(station);
            if (quality == 0) packet.Write(point);
            packet.Write(costs.Count);
            foreach (var cost in costs) { packet.Write(cost.Key); packet.Write(personal[cost.Key]); }
            if (net.IsServer()) Receive(null, packet);
            else { OwnerSkillAuthority.SendNow(); rpc.Invoke("VM_WS_Prepare", packet); }
        }
        private static void Receive(ZRpc rpc, ZPackage packet)
        {
            var net = ZNet.instance;
            if (net?.IsServer() != true || !WorkshopRemoteCraft.Enabled || packet == null || packet.Size() > 16384 ||
                (rpc != null && !WorkshopRemoteCraft.SupportsPreparation(rpc))) return;
            try
            {
                var actor = WorkshopActor.Resolve(rpc);
                if (actor == null || !actor.Available || actor.IsDead() || actor.IsTeleporting() || !actor.HasCrafting70 ||
                    WorkshopRemoteCraft.ServerBusy(actor.GetPlayerID()) || WorkshopChestLease.Pending(actor.GetPlayerID())) return;
                long playerId = actor.GetPlayerID();
                foreach (var entry in Rate.ToArray()) if (Time.time - entry.Value > 30f) Rate.Remove(entry.Key);
                if (Rate.TryGetValue(playerId, out float last) && Time.time - last < .5f) return;
                if (!Rate.ContainsKey(playerId) && Rate.Count >= 128) return;
                Rate[playerId] = Time.time;
                string token = packet.ReadString(), name = packet.ReadString();
                int quality = packet.ReadInt(), amount = packet.ReadInt(); ZDOID station = packet.ReadZDOID();
                if (!Guid.TryParseExact(token, "N", out _) || name.Length > 256 ||
                    WorkshopStationProof.Busy(playerId, token, out _)) return;
                Vector3 point = actor.Position; Dictionary<string, int> costs;
                bool build = name.StartsWith("@build:", StringComparison.Ordinal);
                if (build)
                {
                    point = packet.ReadVector3();
                    var piece = ZNetScene.instance?.GetPrefab(name.Substring(7))?.GetComponent<Piece>();
                    if (quality != 0 || amount != 1 || station != ZDOID.None ||
                        !WorkshopBuildBridge.ServerEligible(actor, piece, point) ||
                        !WorkshopRemoteCraft.TryResourceCosts(piece.m_resources, 0, 1, out costs)) return;
                }
                else
                {
                    Recipe recipe = ObjectDB.instance?.m_recipes.Find(r => r != null && r.name == name);
                    if (ZoneSystem.instance?.GetGlobalKey(GlobalKeys.NoCraftCost) == true ||
                        !WorkshopRemoteCraft.TryCosts(recipe, quality, amount, out costs) ||
                        !WorkshopRemoteCraft.ValidRecipeStation(actor, recipe, quality, station, false)) return;
                }
                int count = packet.ReadInt(); if (count != costs.Count) return;
                var credit = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string resource = packet.ReadString(); int personal = packet.ReadInt();
                    if (!costs.TryGetValue(resource, out int total) || personal < 0 || personal > total || credit.ContainsKey(resource)) return;
                    credit.Add(resource, total - personal);
                }
                if (!credit.Values.Any(v => v > 0)) return;
                WorkshopTiming.Start(token, true, build ? "prepare-build" : "prepare-craft", rpc == null ? "host" : "remote");
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                WorkshopChestLease.Acquire(actor, rpc, token, point, credit,
                    () => MasteryPlugin.Log.LogInfo("[Workshop70] Early preparation " + token + " ready ms=" + elapsed.ElapsedMilliseconds),
                    reason => MasteryPlugin.Log.LogInfo("[Workshop70] Optional preparation missed: " + reason),
                    true, build ? 30f : 10f);
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Workshop70] Optional preparation ignored: " + error.Message); }
        }
    }
}
