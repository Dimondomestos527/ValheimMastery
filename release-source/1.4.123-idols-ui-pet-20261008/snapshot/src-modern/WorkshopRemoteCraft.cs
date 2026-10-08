using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Craft/upgrade/build transport. Kept release-gated until live multi-peer
    // inventory/ownership tests pass. Compile with MASTERY_WORKSHOP_REMOTE_EXPERIMENT.
    internal static class WorkshopRemoteCraft
    {
        internal const string EscrowKey = "vm.workshop.escrow";
        private sealed class ServerAction
        {
            internal ZRpc Rpc;
            internal string Id;
            internal long PlayerId;
            internal WorkshopAtomicDebit Debit;
            internal List<ZDO> Chests;
            internal Dictionary<string, int> Credit;
            internal bool? Decision;
            internal bool Failed;
        }
        private sealed class ClientAction
        {
            internal string Id;
            internal InventoryGui Gui;
            internal Recipe Recipe;
            internal ZDOID StationId;
            internal Piece BuildPiece;
            internal Vector3 BuildPosition;
            internal Quaternion BuildRotation;
            internal Vector3 BuildRayOrigin, BuildRayDirection;
            internal bool BuildEntered, BuildCompleted;
            internal ItemDrop.ItemData Upgrade;
            internal int Quality, Amount, Variant;
            internal Dictionary<string, int> Costs, Credit;
            internal WorkshopInventorySnapshot Before;
            internal CraftingOutcomeSnapshot Outcome;
            internal bool Paid, Preserve, ReplayPermit;
            internal float StartedAt;
            internal ZRpc ServerRpc;
        }
        private static readonly Dictionary<long, ServerAction> Pending = new Dictionary<long, ServerAction>();
        private static readonly WorkshopRequestLedger Used = new WorkshopRequestLedger();
        private static ClientAction _client;
        private static ClientAction _executing;
        private static ClientAction _validatingBuild;
        private static readonly Queue<ZPackage> LocalReplies = new Queue<ZPackage>();
        private static readonly Queue<KeyValuePair<ZRpc, ZPackage>> RemoteReplies = new Queue<KeyValuePair<ZRpc, ZPackage>>();
        internal static bool Executing => _executing != null;
        internal static WorkshopActor RequestPlayer(ZRpc rpc) => WorkshopActor.Resolve(rpc);
        private static void SendRequest(ZPackage request)
        {
            if (ZNet.instance.IsServer()) Request(null, new ZPackage(request.GetArray()));
            else
            {
                // Publish the owner's current levels immediately before the
                // request on the SAME reliable RPC connection, rather than
                // depending solely on a periodically refreshed snapshot.
                OwnerSkillAuthority.SendNow();
                // One extra echo ONLY during opt-in profiling; never awaited by gameplay.
                WorkshopTiming.Probe(_client?.Id);
                WorkshopTiming.Mark(_client?.Id, false, "Csend.request");
                ZNet.instance.GetServerRPC().Invoke("VM_WS_CraftRequest", request);
            }
        }
        private static ZNet _session;
        private static WorkshopDurableRequests _durableRequests;
        private static ZRpc _protocolRpc;
        private static int _protocol, _helloAttempts;
        private static bool _legacyHello;
        private static readonly HashSet<ZRpc> PreparationPeers = new HashSet<ZRpc>();
        internal static bool SupportsPreparation(ZRpc rpc) => rpc != null && PreparationPeers.Contains(rpc);
        internal static bool ServerBusy(long playerId) => Pending.ContainsKey(playerId);
        internal static bool PreparationReady => Ready && (ZNet.instance.IsServer() || _protocol == 4);
        private static float _nextHello;
        internal static bool Ready => Enabled && ZNet.instance != null &&
            (ZNet.instance.IsServer() || ((_protocol == 3 || _protocol == 4) && _protocolRpc == ZNet.instance.GetServerRPC()));
        internal static bool ClientBusy => _client != null || _executing != null;
        internal static string DescribeStatus() => "Майстерня: сервер " + (Ready ? "готовий" : "не підтвердив підтримку") +
            "; операція " + (ClientBusy ? "очікується" : "не виконується") +
            "; квитанція " + (WorkshopRecovery.Outstanding ? "очікує узгодження" : "узгоджена") + ".";
        internal static void Tick()
        {
            if (!Enabled) return;
            ZNet net = ZNet.instance;
            if (_session != net)
            {
                // World records retain escrow data. Never restore old-scene
                // Inventory objects into a newly loaded world.
                foreach (var state in Pending.Values) state.Debit.Commit();
                Pending.Clear(); Used.Clear(); _client = null; _executing = null; _validatingBuild = null; _durableRequests = null; _session = net;
                LocalReplies.Clear(); _protocolRpc = null; _protocol = _helloAttempts = 0; _nextHello = 0f;
                RemoteReplies.Clear();
                PreparationPeers.Clear(); _legacyHello = false;
            }
            // Loopback is deferred: vanilla must finish unwinding its craft timer
            // before a listen-host executes the granted action.
            if (net != null && net.IsServer() && LocalReplies.Count > 0) Reply(null, LocalReplies.Dequeue());
            // Network callbacks must not execute GUI crafting while vanilla is
            // still unwinding the completed timer. Dispatch on our next update.
            if (net != null && !net.IsServer() && RemoteReplies.Count > 0 &&
                (_client?.Gui == null || _client.Gui.m_craftTimer < 0f))
            {
                var reply = RemoteReplies.Dequeue(); Reply(reply.Key, reply.Value);
            }
            WorkshopStationProof.Tick();
            WorkshopPreparation.Tick();
            WorkshopStoragePreview.Tick();
            if (net != null && !net.IsServer() && NetworkSync.HasServerSettings)
            {
                ZRpc server = net.GetServerRPC();
                if (_protocolRpc != server) { _protocolRpc = server; _protocol = _helloAttempts = 0; _nextHello = 0f; _legacyHello = false; }
                // An unready peer can legitimately ignore the initial requests.
                // Keep retrying slowly until acknowledged; explicit incompatibility
                // (-1) still fails closed and never authorizes inventory access.
                if (server != null && _protocol == 0 && Time.time >= _nextHello)
                { _helloAttempts = Math.Min(3, _helloAttempts + 1); _nextHello = Time.time + (_helloAttempts < 3 ? 3f : 15f); server.Invoke("VM_WS_Protocol", _legacyHello ? 3 : 4); }
            }
            if (_client != null && net != null && (net.GetServerRPC() != _client.ServerRpc || Time.time - _client.StartedAt > 15f))
            {
                // Only this live in-memory pending action proves no execution.
                // Never infer the same from a stale character save after a crash.
                WorkshopRecovery.Record(_client.Id, WorkshopReceiptPhase.Refunded);
                _client = null;
            }
        }
        internal static bool Enabled
        {
            get
            {
#if MASTERY_WORKSHOP_REMOTE_EXPERIMENT
                return true;
#else
                return false;
#endif
            }
        }

        internal static void Register(ZRpc rpc)
        {
            rpc.Register<int>("VM_WS_Protocol", Protocol);
            if (!Enabled) return;
            rpc.Register<ZPackage>("VM_WS_CraftRequest", Request);
            rpc.Register<ZPackage>("VM_WS_CraftReply", ReceiveReply);
            rpc.Register<string, bool>("VM_WS_CraftAck", Acknowledge);
            WorkshopRecovery.Register(rpc);
            WorkshopChestLease.Register(rpc);
            WorkshopStationProof.Register(rpc);
            WorkshopTiming.Register(rpc);
            WorkshopPreparation.Register(rpc);
            Tick();
        }
        private static void Protocol(ZRpc rpc, int version)
        {
            var net = ZNet.instance;
            if (net == null) return;
            if (net.IsServer())
            {
                if (net.GetPeer(rpc)?.IsReady() == true)
                {
                    if (Enabled && version == 4) PreparationPeers.Add(rpc); else PreparationPeers.Remove(rpc);
                    rpc.Invoke("VM_WS_Protocol", Enabled && (version == 3 || version == 4) ? version : -1);
                }
            }
            else if (rpc == net.GetServerRPC())
            {
                _protocolRpc = rpc;
                if (version == -1 && !_legacyHello) { _legacyHello = true; _protocol = 0; _nextHello = 0f; }
                else _protocol = version == 3 || version == 4 ? version : -1;
            }
        }

        internal static bool TryCosts(Recipe recipe, int quality, int amount, out Dictionary<string, int> costs)
        {
            costs = new Dictionary<string, int>(StringComparer.Ordinal);
            if (recipe == null || !recipe.m_enabled || recipe.m_requireOnlyOneIngredient ||
                recipe.m_item == null || quality < 1 || quality > recipe.m_item.m_itemData.m_shared.m_maxQuality ||
                amount < 1 || amount > 1000 || recipe.m_resources == null || recipe.m_resources.Length > 64) return false;
            return TryResourceCosts(recipe.m_resources, quality, amount, out costs);
        }

        internal static bool TryResourceCosts(Piece.Requirement[] requirements, int quality, int amount, out Dictionary<string, int> costs)
        {
            costs = new Dictionary<string, int>(StringComparer.Ordinal);
            if (requirements == null || requirements.Length > 64 || quality < 0 || amount < 1 || amount > 1000) return false;
            foreach (var requirement in requirements)
            {
                // Ordinary stations ignore potential-forge-only rows, just like
                // Player.HaveRequirementItems. Potential forge remains unsupported.
                if (requirement?.m_upgraderResource == true) continue;
                if (requirement?.m_resItem?.m_itemData?.m_shared == null ||
                    requirement.m_resItem.m_itemData.m_shared.m_maxQuality != 1) return false;
                int quantity = requirement.GetAmount(quality);
                if (quantity <= 0) continue;
                string name = requirement.m_resItem.m_itemData.m_shared.m_name;
                costs.TryGetValue(name, out int old);
                if (quantity > (int.MaxValue - old) / amount) return false;
                costs[name] = old + quantity * amount;
            }
            return costs.Count > 0;
        }

        internal static bool BeginBuild(Player player, Piece piece)
        {
            if (!WorkshopBuildBridge.ClientEligible(player, piece) || ClientBusy || WorkshopRecovery.Outstanding) return false;
            WorkshopPreparation.BuildActivity();
            player.UpdatePlacementGhost(true);
            if (player.m_placementGhost == null || player.m_placementStatus != Player.PlacementStatus.Valid ||
                !TryResourceCosts(piece.m_resources, 0, 1, out var costs)) return false;
            if (GameCamera.instance == null) return false;
            var state = new ClientAction { Id = Guid.NewGuid().ToString("N"), BuildPiece = piece,
                BuildRayOrigin = GameCamera.instance.transform.position, BuildRayDirection = GameCamera.instance.transform.forward,
                BuildPosition = player.m_placementGhost.transform.position, BuildRotation = player.m_placementGhost.transform.rotation,
                Quality = 0, Amount = 1, Costs = costs, StartedAt = Time.time, ServerRpc = ZNet.instance.GetServerRPC() };
            WorkshopTiming.Start(state.Id, false, "build", ZNet.instance.IsServer() ? "host" : "dedicated-or-remote-host");
            var request = new ZPackage(); request.Write(state.Id); request.Write("@build:" + piece.gameObject.name);
            request.Write(0); request.Write(1); request.Write(ZDOID.None); request.Write(state.BuildPosition);
            request.Write(costs.Count);
            foreach (var cost in costs)
            { request.Write(cost.Key); request.Write(Math.Min(cost.Value, player.GetInventory().CountItems(cost.Key, -1, false))); }
            _client = state; WorkshopRecovery.Record(state.Id, WorkshopReceiptPhase.Requested);
            WorkshopStoragePreview.Invalidate();
            SendRequest(request);
            return true;
        }

        internal static string BeginSelectedCraft(bool timed = false)
        {
            Player player = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            ZNet net = ZNet.instance;
            if (!Ready) return "Сервер ще не підтвердив підтримку мережевої майстерні.";
            if (net == null || player == null || gui == null || !InventoryGui.IsVisible() || gui.m_selectedRecipe.Recipe == null)
                return "Спершу виберіть рецепт на підключеному клієнті.";
            if (!timed && gui.m_craftTimer >= 0f) return "Спершу завершіть або скасуйте поточне звичайне виготовлення.";
            if (_client != null || _executing != null) return "Транзакція майстерні вже очікує на завершення.";
            if (WorkshopRecovery.Outstanding) return "Очікується звірка попередньої операції майстерні.";
            if (!PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70) || player.IsDead() || player.IsTeleporting() ||
                player.GetCurrentCraftingStation()?.m_upgrader == true || player.NoCostCheat() ||
                ZoneSystem.instance?.GetGlobalKey(GlobalKeys.NoCraftCost) == true) return "Майстерня 70-го рівня недоступна.";
            Recipe recipe = timed ? gui.m_craftRecipe : gui.m_selectedRecipe.Recipe;
            var upgrade = timed ? gui.m_craftUpgradeItem : gui.m_selectedRecipe.ItemData;
            int quality = upgrade == null ? 1 : upgrade.m_quality + 1;
            int amount = timed && gui.m_multiCrafting ? gui.m_multiCraftAmount : 1;
            if (!TryCosts(recipe, quality, amount, out var costs) || !player.RequiredCraftingStation(recipe, quality, true))
                return "Ця транзакція не підтримує рецепт або робочу станцію.";
            var state = new ClientAction { Id = Guid.NewGuid().ToString("N"), Gui = gui, Recipe = recipe,
                StationId = player.GetCurrentCraftingStation()?.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None,
                Upgrade = upgrade, Quality = quality, Amount = amount, Variant = timed ? gui.m_craftVariant : gui.m_selectedVariant, Costs = costs,
                StartedAt = Time.time, ServerRpc = net.GetServerRPC() };
            WorkshopTiming.Start(state.Id, false, upgrade == null ? "craft" : "upgrade", net.IsServer() ? "host" : "dedicated-or-remote-host");
            var request = new ZPackage();
            request.Write(state.Id); request.Write(state.Recipe.name); request.Write(quality); request.Write(amount);
            request.Write(state.StationId);
            request.Write(costs.Count);
            foreach (var cost in costs)
            {
                request.Write(cost.Key);
                // This is a shortfall proposal, NOT a server-authoritative inventory.
                // The client adapter must still pay this exact personal portion.
                request.Write(Math.Min(cost.Value, player.GetInventory().CountItems(cost.Key, -1, false)));
            }
            _client = state;
            WorkshopStoragePreview.Invalidate();
            WorkshopRecovery.Record(state.Id, WorkshopReceiptPhase.Requested);
            SendRequest(request);
            return "Запит до майстерні надіслано; предмети ще не виготовлено, особисті ресурси не списано.";
        }

        internal static bool ValidRecipeStation(WorkshopActor player, Recipe recipe, int quality, ZDOID stationId, bool requireEnvironment = true)
        {
            if (player == null || recipe == null) return false;
            CraftingStation required = recipe.GetRequiredStation(quality);
            if (stationId == ZDOID.None) return required == null;
            var entry = WorkshopWorldRecords.Find(stationId);
            CraftingStation station = entry?.Station;
            if (station == null || station.m_upgrader || Vector3.Distance(entry.Data.GetPosition(), player.Position) >= station.m_useDistance ||
                !WorkshopWorldRecords.WardAllows(player.GetPlayerID(), entry.Data.GetPosition()) ||
                (requireEnvironment && !WorkshopStationProof.Usable(entry.Data, station))) return false;
            // m_currentStation exists only on the character owner. Resolve the
            // exact claimed station here, never a different station in the network.
            return required == null ? station.m_showBasicRecipies :
                string.Equals(station.m_name, required.m_name, StringComparison.Ordinal) &&
                entry.Level >= recipe.GetRequiredStationLevel(quality);
        }
        internal static bool StationUsable(CraftingStation station)
        {
            if (station.m_craftRequireRoof)
            {
                Cover.GetCoverForPoint(station.m_roofCheckPoint.position, out float cover, out bool roof, .5f);
                if (!roof || cover < .7f) return false;
            }
            return !station.m_craftRequireFire || station.m_haveFire;
        }
        private static void LogWorldContext(WorkshopActor actor, ZDOID stationId, Vector3 actionPoint)
        {
            ZDO record = ZDOMan.instance?.GetZDO(stationId);
            GameObject instance = ZNetScene.instance?.FindInstance(stationId);
            MasteryPlugin.Log.LogWarning("[Workshop70] WorldContext actor=" + actor.GetPlayerID() +
                " actorPos=" + actor.Position + " action=" + actionPoint + " station=" + stationId +
                " stationZDO=" + (record != null) + " stationLoaded=" + (instance != null) +
                " stationPrefab=" + (record != null ? record.GetPrefab().ToString() : "none") +
                " stationPos=" + (record != null ? record.GetPosition().ToString() : "none") +
                " loadedStations=" + CraftingStation.m_allStations.Count +
                " loadedContainers=" + UnityEngine.Object.FindObjectsOfType<Container>().Length +
                " serverReference=" + ZNet.instance.GetReferencePosition());
        }

        private static void Request(ZRpc rpc, ZPackage package)
        {
            ZNet net = ZNet.instance;
            if (!Enabled || net == null || !net.IsServer() || package == null || package.Size() > 16384) return;
            string id = "";
            byte[] original = package.GetArray();
            try
            {
                id = package.ReadString();
                string recipeName = package.ReadString();
                MasteryPlugin.Log.LogInfo("[Workshop70] Server received " + id + " recipe=" + recipeName);
                int quality = package.ReadInt(), amount = package.ReadInt();
                ZDOID stationId = package.ReadZDOID();
                if (!Guid.TryParseExact(id, "N", out _) || recipeName.Length > 256) return;
                WorkshopTiming.Start(id, true, recipeName.StartsWith("@build:", StringComparison.Ordinal) ? "build" : quality > 1 ? "upgrade" : "craft", rpc == null ? "host" : "remote");
                ZNetPeer peer = rpc == null ? null : net.GetPeer(rpc);
                WorkshopActor player = RequestPlayer(rpc);
                if (player == null || player.IsDead() || player.IsTeleporting() ||
                    !player.HasCrafting70)
                {
                    if (rpc != null) MasteryPlugin.Log.LogWarning("[Workshop70] Authorization: " + OwnerSkillAuthority.Describe(rpc, OwnerSkillAuthority.ResolvePlayer(peer)));
                    Deny(rpc, id, player == null ? "Мережеві дані персонажа ще не підтверджені." :
                        player.IsDead() || player.IsTeleporting() ? "Персонаж ще не готовий до виготовлення." :
                        "Сервер не підтвердив 70-й рівень ремісництва.");
                    return;
                }
                long playerId = player.GetPlayerID();
                // Wait only for optional preparation, before admission/debit.
                // Duplicates share one waiter; callback revalidates this RPC/actor.
                if (WorkshopChestLease.WaitForPreparation(playerId, id,
                    () => {
                        var current = RequestPlayer(rpc);
                        if (current?.CharacterId != player.CharacterId || current?.GetPlayerID() != playerId)
                            Deny(rpc, id, "Персонаж змінився під час підготовки скринь.");
                        else Request(rpc, new ZPackage(original));
                    }, reason => Deny(rpc, id, reason))) return;
                if (WorkshopStationProof.Busy(playerId, id, out bool sameProof))
                {
                    if (!sameProof) Deny(rpc, id, "Перевірка робочого станка ще триває.");
                    return;
                }
                if (WorkshopChestLease.Pending(playerId))
                {
                    // A duplicate packet must not cancel the original client's
                    // pending state while its ownership handoff continues.
                    if (!WorkshopChestLease.SamePending(playerId, id)) Deny(rpc, id, "Передавання власності вже очікує на завершення.");
                    return;
                }
                if (Pending.TryGetValue(playerId, out var pending))
                {
                    if (pending.Id == id && !pending.Failed && !pending.Decision.HasValue)
                    {
                        if (pending.Rpc != rpc && net.GetPeer(pending.Rpc) != null) return;
                        pending.Rpc = rpc;
                        SendGrant(rpc, pending.Id, pending.Credit);
                    }
                    else Deny(rpc, id, "Попередня транзакція ще очікує на завершення.");
                    return;
                }
                // Reconnecting must not reset the replay ledger or permit a
                // second debit while the character's first operation is pending.
                if (Used.Remember(playerId, id) != WorkshopRequestAdmission.Accepted)
                { Deny(rpc, id, "Повторний запит або досягнуто ліміту тестового сеансу світу."); return; }
                if (_durableRequests == null)
                    _durableRequests = new WorkshopDurableRequests(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,
                        "ValheimMastery", "workshop-" + net.GetWorldUID() + ".requests"));
                if (!_durableRequests.Admit(playerId, id))
                { Deny(rpc, id, "Транзакцію вже прийнято в цьому світі."); return; }
                WorkshopTiming.Mark(id, true, "Sjournal.admitted");
                Recipe recipe = null;
                Piece buildPiece = null;
                Vector3 actionPoint = player.Position;
                Dictionary<string, int> costs;
                if (recipeName.StartsWith("@build:", StringComparison.Ordinal))
                {
                    buildPiece = ZNetScene.instance.GetPrefab(recipeName.Substring(7))?.GetComponent<Piece>();
                    actionPoint = package.ReadVector3();
                    if (quality != 0 || amount != 1 || !WorkshopBuildBridge.ServerEligible(player, buildPiece, actionPoint) ||
                        !TryResourceCosts(buildPiece.m_resources, 0, 1, out costs))
                    { LogWorldContext(player, stationId, actionPoint); Deny(rpc, id, "Некоректний запит на будівництво."); return; }
                }
                else
                {
                    foreach (var candidate in ObjectDB.instance.m_recipes)
                        if (candidate != null && candidate.name == recipeName) { recipe = candidate; break; }
                    if (!TryCosts(recipe, quality, amount, out costs)) { Deny(rpc, id, "Некоректний рецепт або вартість."); return; }
                }
                CraftingStation required = recipe != null ? recipe.GetRequiredStation(quality) : buildPiece.m_craftingStation;
                if (recipe != null ? !ValidRecipeStation(player, recipe, quality, stationId, false) :
                    required != null && !WorkshopWorldRecords.HasCapability(required.m_name, player.Position, 1))
                { LogWorldContext(player, stationId, actionPoint); Deny(rpc, id, "Не знайдено потрібної робочої станції."); return; }
                int count = package.ReadInt();
                if (count != costs.Count) { Deny(rpc, id, "Кількість складників не збігається."); return; }
                var credit = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string name = package.ReadString(); int personal = package.ReadInt();
                    if (!costs.TryGetValue(name, out int total) || personal < 0 || personal > total || credit.ContainsKey(name))
                        throw new InvalidOperationException("Invalid personal-payment proposal.");
                    credit.Add(name, total - personal);
                }
                if (!System.Linq.Enumerable.Any(credit.Values, value => value > 0))
                { Deny(rpc, id, "Використайте звичайне виготовлення: ресурси зі скринь не потрібні."); return; }
                var preparation = System.Diagnostics.Stopwatch.StartNew();
                WorkshopTiming.Count(id, true, "materials", credit.Count);
                WorkshopTiming.Mark(id, true, "Sstation.begin");
                WorkshopStationProof.Acquire(player, id, recipe != null ? stationId : ZDOID.None,
                    () => { WorkshopTiming.Mark(id, true, "Sstation.ready"); WorkshopChestLease.Acquire(player, rpc, id, actionPoint, credit,
                    () => {
                        WorkshopTiming.Mark(id, true, "T4.ownershipReady");
                        MasteryPlugin.Log.LogInfo("[Workshop70] Preparation " + id + " ms=" + preparation.ElapsedMilliseconds);
                        var debitTime = System.Diagnostics.Stopwatch.StartNew();
                        CommitRequest(rpc, id, player, recipe, quality, amount, costs, credit, buildPiece, actionPoint, stationId);
                        MasteryPlugin.Log.LogInfo("[Workshop70] Debit/grant " + id + " ms=" + debitTime.ElapsedMilliseconds);
                    },
                    reason => Deny(rpc, id, reason)); }, reason => Deny(rpc, id, reason));
            }
            catch (Exception error)
            {
                MasteryPlugin.Log.LogWarning("[Workshop70] Request validation failed: " + error.Message);
                if (id.Length == 32) Deny(rpc, id, "Сервер відхилив запит.");
            }
        }

        private static void CommitRequest(ZRpc rpc, string id, WorkshopActor player, Recipe recipe, int quality, int amount,
            Dictionary<string, int> costs, Dictionary<string, int> credit, Piece buildPiece, Vector3 actionPoint, ZDOID stationId)
        {
            WorkshopAtomicDebit debit = null;
            bool ownsPending = false;
            var chests = new List<ZDO>();
            try
            {
                ZNet net = ZNet.instance;
                long playerId = player != null ? player.GetPlayerID() : 0;
                Dictionary<string, int> currentCosts = null;
                bool validCosts = buildPiece != null ? WorkshopBuildBridge.ServerEligible(player, buildPiece, actionPoint) &&
                    TryResourceCosts(buildPiece.m_resources, 0, 1, out currentCosts) : TryCosts(recipe, quality, amount, out currentCosts);
                if (net == null || !net.IsServer() || player == null || player.IsDead() || player.IsTeleporting() ||
                    ZoneSystem.instance?.GetGlobalKey(GlobalKeys.NoCraftCost) == true ||
                    RequestPlayer(rpc)?.CharacterId != player.CharacterId || Pending.ContainsKey(playerId) ||
                    !player.HasCrafting70 ||
                    WorkshopWorldRecords.Covered(player.Position) < 0 ||
                    WorkshopWorldRecords.Covered(player.Position) != WorkshopWorldRecords.Covered(actionPoint) ||
                    !validCosts || currentCosts.Count != costs.Count)
                { Deny(rpc, id, "Дозвіл на виготовлення змінився під час передавання власності."); return; }
                foreach (var cost in costs)
                    if (!currentCosts.TryGetValue(cost.Key, out int value) || value != cost.Value)
                    { Deny(rpc, id, "Вартість рецепта змінилася під час передавання власності."); return; }
                CraftingStation required = recipe != null ? recipe.GetRequiredStation(quality) : buildPiece.m_craftingStation;
                if (recipe != null ? !ValidRecipeStation(player, recipe, quality, stationId) :
                    required != null && !WorkshopWorldRecords.HasCapability(required.m_name, player.Position, 1))
                { Deny(rpc, id, "Доступність робочої станції змінилася під час передавання власності."); return; }
                var eligible = WorkshopWorldRecords.Chests(player, actionPoint, true);
                var stores = new Dictionary<ZDO, WorkshopRecordStore>();
                var lines = new List<WorkshopDebitLine>();
                foreach (var need in credit)
                {
                    int left = need.Value;
                    foreach (var chest in eligible)
                    {
                        if (left == 0) break;
                        if (!stores.TryGetValue(chest.Data, out var store))
                            stores.Add(chest.Data, store = new WorkshopRecordStore(chest.Data, chest.Chest));
                        int take = Math.Min(left, store.Count(need.Key));
                        if (take <= 0) continue;
                        if (!chests.Contains(chest.Data)) chests.Add(chest.Data);
                        lines.Add(new WorkshopDebitLine { Store = store, Item = need.Key, Amount = take });
                        left -= take;
                    }
                    if (left != 0) { Deny(rpc, id, "У доступних скринях сервера недостатньо ресурсів."); return; }
                }
                if (lines.Count == 0) { Deny(rpc, id, "Використайте звичайне виготовлення: ресурси зі скринь не потрібні."); return; }
                WorkshopTiming.Mark(id, true, "T5.finalPlan");
                WorkshopTiming.Count(id, true, "debitedChests", chests.Count);
                foreach (ZDO chest in chests)
                    if (chest.GetLong(WorkshopChestLease.ReturnOwnerKey, 0) == 0 && rpc != null)
                        chest.Set(WorkshopChestLease.ReturnOwnerKey, net.GetPeer(rpc).m_uid);
                var before = new Dictionary<ZDO, byte[]>();
                foreach (var chest in chests)
                    before.Add(chest, (byte[])chest.GetByteArray(ZDOVars.s_items, null).Clone());
                WorkshopTiming.Mark(id, true, "T6.removalStart");
                debit = WorkshopAtomicDebit.Begin(lines, () => player.HasCrafting70 && !player.IsDead() && !player.IsTeleporting());
                if (debit == null) { Deny(rpc, id, "Запаси скрині змінилися або скриню заблоковано."); return; }
                WorkshopTiming.Mark(id, true, "T7.removalPersisted");
                WorkshopRecovery.MarkRecords(chests, before, player.GetPlayerID(), id);
                WorkshopTiming.Mark(id, true, "Sescrow.persisted");
                WorkshopChestLease.KeepWarm(chests, player, actionPoint);
                Pending.Add(playerId, new ServerAction { Rpc = rpc, Id = id, PlayerId = playerId, Debit = debit, Chests = chests, Credit = credit });
                ownsPending = true;
                debit = null; // The pending server action owns the rollback from here.
                SendGrant(rpc, id, credit);
            }
            catch (Exception error)
            {
                if (debit != null)
                {
                    try { debit.Dispose(); ClearMarkers(chests); }
                    catch (Exception rollback) { MasteryPlugin.Log.LogError("[Workshop70] QUARANTINED: " + rollback); }
                }
                MasteryPlugin.Log.LogWarning("[Workshop70] Debit failed: " + error.Message);
                if (id.Length == 32 && !ownsPending) Deny(rpc, id, "Сервер відхилив транзакцію.");
            }
        }

        private static void Deny(ZRpc rpc, string id, string reason)
        { WorkshopTiming.Report(id, true, "denied", true); MasteryPlugin.Log.LogWarning("[Workshop70] Denied " + id + ": " + reason); var reply = new ZPackage(); reply.Write(id); reply.Write(false); reply.Write(reason); SendReply(rpc, reply); }
        private static void SendReply(ZRpc rpc, ZPackage reply)
        {
            if (rpc == null) LocalReplies.Enqueue(new ZPackage(reply.GetArray()));
            else rpc.Invoke("VM_WS_CraftReply", reply);
        }
        private static void SendGrant(ZRpc rpc, string id, Dictionary<string, int> credit)
        {
            WorkshopTiming.Mark(id, true, "Sgrant.sent");
            WorkshopTiming.Report(id, true, "grant");
            var reply = new ZPackage(); reply.Write(id); reply.Write(true); reply.Write(credit.Count);
            foreach (var item in credit) { reply.Write(item.Key); reply.Write(item.Value); }
            SendReply(rpc, reply);
        }

        private static void ReceiveReply(ZRpc rpc, ZPackage package)
        {
            ZNet net = ZNet.instance;
            if (!Enabled || net == null || net.IsServer() || rpc != net.GetServerRPC() ||
                package == null || package.Size() > 16384 || _client == null || RemoteReplies.Count >= 4) return;
            WorkshopTiming.Mark(_client.Id, false, "Cgrant.received");
            RemoteReplies.Enqueue(new KeyValuePair<ZRpc, ZPackage>(rpc, new ZPackage(package.GetArray())));
        }
        internal static bool BuildRay(Player player, out Vector3 origin, out Vector3 direction)
        {
            var state = _validatingBuild ?? _executing ?? _client;
            origin = state?.BuildRayOrigin ?? Vector3.zero; direction = state?.BuildRayDirection ?? Vector3.zero;
            return Enabled && state?.BuildPiece != null && player == Player.m_localPlayer &&
                player.GetSelectedPiece() == state.BuildPiece && direction.sqrMagnitude > .9f;
        }
        internal static void CancelPendingCraft()
        {
            if (_client?.Recipe == null) return;
            WorkshopRecovery.Record(_client.Id, WorkshopReceiptPhase.Refunded);
            MasteryPlugin.Log.LogInfo("[Workshop70] Pending craft cancelled by player " + _client.Id);
            _client = null;
        }
        private static string CraftCancellation(ClientAction state, Player player)
        {
            if (player == null || player.IsDead() || player.IsTeleporting()) return "character-unavailable";
            if (state.Gui == null || !InventoryGui.IsVisible()) return "craft-window-closed";
            if (state.Gui.m_craftTimer >= 0f) return "another-native-craft-active";
            if ((player.GetCurrentCraftingStation()?.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? ZDOID.None) != state.StationId)
                return "exact-station-changed";
            if (!player.RequiredCraftingStation(state.Recipe, state.Quality, true)) return "native-station-requirements";
            if (state.Upgrade != null && (state.Upgrade.m_quality + 1 != state.Quality || !player.GetInventory().ContainsItem(state.Upgrade)))
                return "upgrade-item-changed";
            if (player.NoCostCheat() || ZoneSystem.instance?.GetGlobalKey(GlobalKeys.NoCraftCost) == true) return "cost-mode-changed";
            if (!PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) return "crafting70-unavailable";
            return null;
        }
        private static void Reply(ZRpc rpc, ZPackage package)
        {
            ZNet net = ZNet.instance;
            if (!Enabled || net == null || (net.IsServer() ? rpc != null : rpc != net.GetServerRPC()) || package == null || package.Size() > 16384) return;
            ClientAction state = _client;
            string id = package.ReadString(); bool granted = package.ReadBool();
            if (state == null || id != state.Id) return; // Never replay an already handled grant.
            WorkshopTiming.Mark(id, false, "Cgrant.dispatched");
            MasteryPlugin.Log.LogInfo("[Workshop70] Reply " + id + " granted=" + granted + " waitMs=" + ((Time.time - state.StartedAt) * 1000f).ToString("F0"));
            _client = null;
            Player player = Player.m_localPlayer;
            if (!granted) { WorkshopTiming.Report(id, false, "denied", true); WorkshopRecovery.Clear(id); player?.Message(MessageHud.MessageType.TopLeft, "Майстерня: " + package.ReadString()); return; }
            bool commit = false;
            bool settled = true;
            try
            {
                state.Credit = new Dictionary<string, int>(StringComparer.Ordinal);
                int count = package.ReadInt();
                if (count != state.Costs.Count) throw new InvalidOperationException("Malformed grant.");
                for (int i = 0; i < count; i++)
                {
                    string name = package.ReadString(); int paid = package.ReadInt();
                    if (!state.Costs.TryGetValue(name, out int total) || paid < 0 || paid > total || state.Credit.ContainsKey(name))
                        throw new InvalidOperationException("Malformed credit.");
                    state.Credit.Add(name, paid);
                }
                if (state.BuildPiece != null)
                {
                    _validatingBuild = state;
                    if (!WorkshopBuildBridge.ClientEligible(player, state.BuildPiece) ||
                        player.GetSelectedPiece() != state.BuildPiece || !player.HaveStamina(player.GetBuildStamina()))
                    { MasteryPlugin.Log.LogWarning("[Workshop70] Build cancelled: tool/piece/stamina changed " + id); return; }
                    player.UpdatePlacementGhost(true);
                    if (player.m_placementGhost == null || player.m_placementStatus != Player.PlacementStatus.Valid ||
                        Vector3.Distance(player.m_placementGhost.transform.position, state.BuildPosition) > 0.05f ||
                        Quaternion.Angle(player.m_placementGhost.transform.rotation, state.BuildRotation) > 1f)
                    {
                        MasteryPlugin.Log.LogWarning("[Workshop70] Build cancelled: placement=" + player.m_placementStatus +
                            " drift=" + (player.m_placementGhost != null ? Vector3.Distance(player.m_placementGhost.transform.position, state.BuildPosition).ToString("F3") : "no-ghost") + " " + id);
                        return;
                    }
                }
                else
                {
                    string cancellation = CraftCancellation(state, player);
                    if (cancellation != null)
                    { MasteryPlugin.Log.LogWarning("[Workshop70] Craft cancelled: " + cancellation + " " + id); return; }
                }
                foreach (var cost in state.Costs)
                    if (player.GetInventory().CountItems(cost.Key, -1, false) < cost.Value - state.Credit[cost.Key]) return;
                state.Before = new WorkshopInventorySnapshot(player.GetInventory());
                state.Outcome = state.BuildPiece != null ? null : CraftingOutcomeSnapshot.Capture(player, state.Recipe, state.Quality);
                WorkshopRecovery.Record(id, WorkshopReceiptPhase.Uncertain);
                settled = false;
                _executing = state; state.ReplayPermit = true;
                try
                {
                    if (state.BuildPiece != null) ExecuteBuild(state, player);
                    else ExecuteSelected(state, player);
                }
                finally
                {
                    try
                    {
                        bool success = state.Paid && (state.BuildPiece != null ? state.BuildCompleted : state.Outcome.HasSuccessfulOutput());
                        WorkshopTiming.Mark(id, false, "T8.actionObserved");
                        WorkshopTiming.Count(id, false, "successfulOutput", success ? 1 : 0);
                        MasteryPlugin.Log.LogInfo("[Workshop70] Client outcome " + id + " paid=" + state.Paid +
                            " output=" + success + " buildEntered=" + state.BuildEntered + " preserve=" + state.Preserve);
                        // Once PlacePiece started, an exception may have left a
                        // real world object. Never refund this ambiguous output.
                        bool uncertainBuild = state.BuildEntered && !state.BuildCompleted;
                        if (!success && !uncertainBuild) state.Before.Restore();
                        commit = success && !state.Preserve;
                        settled = !uncertainBuild;
                    }
                    finally { _executing = null; }
                }
            }
            catch (Exception error) { MasteryPlugin.Log.LogError("[Workshop70] Client action failed: " + error); }
            finally
            {
                _validatingBuild = null;
                WorkshopStoragePreview.RefreshUiSoon();
                if (settled)
                {
                    WorkshopRecovery.Record(id, commit ? WorkshopReceiptPhase.Committed : WorkshopReceiptPhase.Refunded);
                    WorkshopTiming.Mark(id, false, "Cack.sent");
                    WorkshopTiming.Report(id, false, commit ? "output" : "refund");
                    if (net.IsServer()) Acknowledge(null, id, commit);
                    else rpc.Invoke("VM_WS_CraftAck", id, commit);
                }
                else MasteryPlugin.Log.LogError("[Workshop70] Uncertain client output; no refund acknowledgment sent. Escrow " + id + " requires reconciliation.");
            }
        }

        private static void ExecuteBuild(ClientAction state, Player player)
        {
            PayPersonal(state, player);
            state.BuildCompleted = player.TryPlacePiece(state.BuildPiece);
            if (!state.BuildCompleted) return;
            player.m_lastToolUseTime = Time.time;
            player.UseStamina(player.GetBuildStamina());
            Hud.instance?.m_buildUi?.AddRecentPiece(state.BuildPiece);
        }
        internal static void GuardBuildPlacement(Player player, Piece piece, Vector3 pos, Quaternion rot)
        {
            var state = _executing;
            if (state?.BuildPiece == null || player != Player.m_localPlayer) return;
            if (state.BuildEntered || piece != state.BuildPiece || Vector3.Distance(pos, state.BuildPosition) > 0.05f ||
                Quaternion.Angle(rot, state.BuildRotation) > 1f || !state.Paid)
                throw new InvalidOperationException("Building placement changed after authorization.");
            state.BuildEntered = true;
        }

        private static void ExecuteSelected(ClientAction state, Player player)
        {
            // m_craftRecipe exists only DURING vanilla's timed action, not while
            // browsing. Supply a scoped execution context for the granted recipe
            // and restore it even if another patch throws. No availability spoof.
            var gui = state.Gui;
            Recipe oldRecipe = gui.m_craftRecipe;
            var oldUpgrade = gui.m_craftUpgradeItem;
            int oldVariant = gui.m_craftVariant, oldAmount = gui.m_multiCraftAmount;
            bool oldMulti = gui.m_multiCrafting;
            gui.m_craftRecipe = state.Recipe; gui.m_craftUpgradeItem = state.Upgrade;
            gui.m_craftVariant = state.Variant; gui.m_multiCraftAmount = state.Amount; gui.m_multiCrafting = state.Amount > 1;
            try { gui.DoCrafting(player); }
            finally
            {
                if (gui != null)
                {
                    gui.m_craftRecipe = oldRecipe; gui.m_craftUpgradeItem = oldUpgrade;
                    gui.m_craftVariant = oldVariant; gui.m_multiCraftAmount = oldAmount; gui.m_multiCrafting = oldMulti;
                }
            }
        }

        private static void Acknowledge(ZRpc rpc, string id, bool committed)
        {
            if (!Enabled || ZNet.instance == null || !ZNet.instance.IsServer() ||
                !Pending.TryGetValue(RequestPlayer(rpc)?.GetPlayerID() ?? 0, out var state) || state.Id != id || state.Rpc != rpc ||
                RequestPlayer(rpc)?.GetPlayerID() != state.PlayerId) return;
            if (state.Failed || (state.Decision.HasValue && state.Decision.Value != committed)) return;
            state.Decision = committed;
            try
            {
                if (committed) state.Debit.Commit(); else state.Debit.Dispose();
                WorkshopRecovery.SettleRecords(state.Chests, committed); Pending.Remove(state.PlayerId);
                WorkshopTiming.Mark(id, true, "Ssettled");
                WorkshopTiming.Report(id, true, committed ? "committed" : "refund", true);
                foreach (ZDO chest in state.Chests) WorkshopChestLease.ReturnSettled(chest);
                if (rpc == null) { WorkshopTiming.Mark(id, false, "T9.confirmed"); WorkshopTiming.Report(id, false, "settled", true); WorkshopRecovery.Clear(id); } else rpc.Invoke("VM_WS_Settled", id);
                MasteryPlugin.Log.LogInfo("[Workshop70] " + id + (committed ? " committed" : " rolled back"));
            }
            catch (Exception error)
            {
                state.Failed = true; // Dispose after a failed rollback is closed, not proof of restoration.
                MasteryPlugin.Log.LogError("[Workshop70] QUARANTINED pending action " + id + ": " + error);
            }
        }
        internal static bool TryRecoverPending(ZRpc rpc, long playerId, string id, bool commit)
        {
            if (!Pending.TryGetValue(playerId, out var state) || state.Id != id) return false;
            if (state.Decision.HasValue && state.Decision.Value != commit) return true;
            if (state.Failed) { Pending.Remove(playerId); return false; }
            if (state.Rpc != rpc && ZNet.instance.GetPeer(state.Rpc) != null) return true;
            state.Rpc = rpc;
            Acknowledge(rpc, id, commit);
            return true;
        }
        private static void ClearMarkers(List<ZDO> chests)
        {
            foreach (var chest in chests)
            {
                if (chest == null || chest.GetOwner() != ZNet.GetUID()) throw new InvalidOperationException("Escrow chest unavailable.");
                chest.Set(EscrowKey, ""); chest.Set(WorkshopRecovery.BeforeKey, Array.Empty<byte>());
            }
        }
        internal static bool HasCredit(Player player, Recipe recipe, int quality, int amount) =>
            Enabled && _executing != null && player == Player.m_localPlayer && _executing.Recipe == recipe &&
            _executing.Quality == quality && _executing.Amount == amount;
        internal static bool HasBuildCredit(Player player, Piece piece) => Enabled && _executing?.BuildPiece != null &&
            _executing.BuildPiece == piece && _executing.Paid && player == Player.m_localPlayer && !_executing.BuildEntered;
        internal static bool Preview(Player player, Recipe recipe, int quality, int amount)
        {
            if (!Ready || ZNet.instance == null || player != Player.m_localPlayer ||
                player == null || player.IsDead() || player.IsTeleporting() ||
                player.GetCurrentCraftingStation()?.m_upgrader == true || !TryCosts(recipe, quality, amount, out _)) return false;
            return WorkshopStoragePreview.CanPay(player, recipe.m_resources, quality, amount);
        }
        internal static bool AllowCraft(InventoryGui gui, Player player)
        {
            if (!Enabled) return true;
            if (player?.GetCurrentCraftingStation()?.m_upgrader == true || player?.NoCostCheat() == true ||
                ZoneSystem.instance?.GetGlobalKey(GlobalKeys.NoCraftCost) == true) return true;
            if (_executing == null)
            {
                if (ZNet.instance == null || player != Player.m_localPlayer ||
                    !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) return true;
                if (ClientBusy || WorkshopRecovery.Outstanding)
                { MasteryPlugin.Log.LogWarning("[Workshop70] Craft blocked: busy=" + ClientBusy + " recovery=" + WorkshopRecovery.Outstanding); return false; }
                Recipe recipe = gui.m_craftRecipe;
                int quality = gui.m_craftUpgradeItem == null ? 1 : gui.m_craftUpgradeItem.m_quality + 1;
                int amount = gui.m_multiCrafting ? gui.m_multiCraftAmount : 1;
                if (recipe == null || WorkshopResourcePlan.TryBuild(player.GetInventory(), new List<Container>(),
                    recipe.m_resources, quality, out _, amount)) return true;
                // The ordinary timer completes here. Never let a preview count
                // grant vanilla output before the server has debited the chests.
                string status = BeginSelectedCraft(true);
                if (_client == null) MasteryPlugin.Log.LogWarning("[Workshop70] Request not started: " + status);
                if (_client == null) player.Message(MessageHud.MessageType.TopLeft, "Майстерня: " + status);
                return false;
            }
            if (!_executing.ReplayPermit) return false;
            _executing.ReplayPermit = false; return true;
        }
        internal static bool Consume(Player player, Piece.Requirement[] requirements, int quality, int amount)
        {
            var state = _executing;
            if (!Enabled || state == null || state.Recipe == null || player != Player.m_localPlayer || requirements != state.Recipe.m_resources ||
                quality != state.Quality || amount != state.Amount) return true;
            if (state.Paid) throw new InvalidOperationException("Repeated Workshop payment callback.");
            var preserve = Crafting35TransactionState.Current;
            if (preserve?.PreserveResources == true && preserve.Player == player)
            { preserve.ConsumptionSkipped = true; state.Preserve = true; }
            else
                PayPersonal(state, player);
            state.Paid = true; return false;
        }
        private static void PayPersonal(ClientAction state, Player player)
        {
            if (state.Paid) throw new InvalidOperationException("Repeated Workshop payment.");
            foreach (var cost in state.Costs)
                {
                    int debit = cost.Value - state.Credit[cost.Key];
                    int before = player.GetInventory().CountItems(cost.Key, -1, false);
                    if (before < debit) throw new InvalidOperationException("Personal stock changed.");
                    player.GetInventory().RemoveItem(cost.Key, debit, -1, false);
                    if (player.GetInventory().CountItems(cost.Key, -1, false) != before - debit)
                        throw new InvalidOperationException("Personal debit was not exact.");
                }
            state.Paid = true;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class WorkshopRemoteCraftReentryPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(InventoryGui __instance, Player player) => WorkshopRemoteCraft.AllowCraft(__instance, player);
    }
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class WorkshopRemoteCraftPaymentPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int multiplier) =>
            WorkshopRemoteCraft.Consume(__instance, requirements, qualityLevel, multiplier);
    }
}
