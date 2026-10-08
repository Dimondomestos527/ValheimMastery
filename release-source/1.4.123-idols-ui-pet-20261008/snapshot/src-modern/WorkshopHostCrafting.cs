using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // First executable adapter: listen-server/single-player only. A remote Player
    // inventory is NOT authoritative; dedicated clients deliberately stay vanilla
    // until the owner/server action protocol exists. Do not remove this boundary.
    internal static class WorkshopHostCrafting
    {
        [ThreadStatic] internal static Attempt Current;
        [ThreadStatic] internal static int CraftCallDepth;
        internal sealed class Attempt
        {
            internal Player Player;
            internal Recipe Recipe;
            internal int Quality, Multiplier;
            internal CraftingOutcomeSnapshot Outcome;
            internal WorkshopInventorySnapshot PersonalBefore;
            internal WorkshopAtomicDebit Debit;
            internal List<WorkshopResourceDebit> Plan;
            internal bool PaymentObserved, Preserve, Closed;
        }

        internal static bool IsAuthority(Player player) => player != null && player == Player.m_localPlayer &&
            ZNet.instance != null && ZNet.instance.IsServer() && MasteryPlugin.Settings.Enabled.Value &&
            !player.IsDead() && !player.IsTeleporting() &&
            PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70);

        internal static bool Supported(Player player, Recipe recipe) => IsAuthority(player) &&
            recipe != null && !recipe.m_requireOnlyOneIngredient &&
            player.GetCurrentCraftingStation()?.m_upgrader != true && !player.NoCostCheat() &&
            (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost));

        internal static bool TryPlan(Player player, Recipe recipe, int quality, int amount,
            out List<WorkshopResourceDebit> plan)
        {
            plan = null;
            if (!Supported(player, recipe) || quality < 1 || amount < 1 || amount > 1000) return false;
            return WorkshopResourcePlan.TryBuild(player.GetInventory(),
                WorkshopNetworkStorage.FindServerOwnedEligible(player, player.transform.position),
                recipe.m_resources, quality, out plan, amount);
        }

        internal static bool Begin(InventoryGui gui, Player player, out Attempt attempt)
        {
            attempt = null;
            if (WorkshopRemoteCraft.Executing) return true; // Grant already owns exact chest debit.
            Recipe recipe = gui?.m_craftRecipe;
            if (!Supported(player, recipe)) return true;
            if (Current != null) return false; // No nested output against somebody else's credit.
            int quality = gui.m_craftUpgradeItem != null ? gui.m_craftUpgradeItem.m_quality + 1 : 1;
            int amount = gui.m_multiCrafting ? gui.m_multiCraftAmount : 1;
            // If the normal inventory can pay, leave every vanilla/mod hook alone.
            if (WorkshopResourcePlan.TryBuild(player.GetInventory(), new List<Container>(),
                recipe.m_resources, quality, out _, amount)) return true;
            if (!TryPlan(player, recipe, quality, amount, out var plan)) return true;
            if (!player.RequiredCraftingStation(recipe, quality, true)) return false;
            var stores = new Dictionary<Inventory, WorkshopInventoryStore>();
            var lines = new List<WorkshopDebitLine>();
            foreach (var debit in plan)
            {
                if (debit.Container == null) continue;
                if (!stores.TryGetValue(debit.Inventory, out var store))
                {
                    debit.Container.Load(); // Refresh before taking a snapshot.
                    stores.Add(debit.Inventory, store = new WorkshopInventoryStore(debit.Container));
                }
                lines.Add(new WorkshopDebitLine { Store = store, Item = debit.ItemName, Amount = debit.Amount });
            }
            if (lines.Count == 0) return true;
            var state = new Attempt
            {
                Player = player, Recipe = recipe, Quality = quality, Multiplier = amount, Plan = plan,
                Outcome = CraftingOutcomeSnapshot.Capture(player, recipe, quality),
                PersonalBefore = new WorkshopInventorySnapshot(player.GetInventory())
            };
            if (state.Outcome == null) return false;
            uint revision = WorkshopNetwork.Revision;
            try
            {
                state.Debit = WorkshopAtomicDebit.Begin(lines, () =>
                {
                    if (!Supported(player, recipe) ||
                        WorkshopNetwork.CoveredComponent(player.transform.position) < 0 ||
                        WorkshopNetwork.Revision != revision) return false;
                    foreach (var pair in stores)
                        if (!pair.Value.CanWrite()) return false;
                    return true;
                });
                if (state.Debit == null) return false;
                Current = attempt = state;
                return true;
            }
            catch (Exception error)
            {
                MasteryPlugin.Log.LogError("[Workshop70] Chest debit refused: " + error);
                return false;
            }
        }

        internal static bool HasCredit(Player player, Recipe recipe, int quality, int amount)
        {
            Attempt state = Current;
            return state != null && !state.Closed && state.Player == player && state.Recipe == recipe &&
                state.Quality == quality && state.Multiplier == amount;
        }

        internal static void ObservePayment(Player player, Piece.Requirement[] requirements, int quality, int amount)
        {
            Attempt state = Current;
            if (state == null || state.Player != player || state.Recipe.m_resources != requirements ||
                state.Quality != quality || state.Multiplier != amount) return;
            state.Preserve = Crafting35TransactionState.Current?.ConsumptionSkipped == true;
            bool exact = true;
            foreach (var debit in state.Plan)
                if (debit.Container == null && player.GetInventory().CountItems(debit.ItemName, -1, false) !=
                    state.PersonalBefore.Count(debit.ItemName) - (state.Preserve ? 0 : debit.Amount)) exact = false;
            state.PaymentObserved = exact;
        }

        internal static void Finish(Attempt state)
        {
            if (state == null || state.Closed) return;
            state.Closed = true;
            try
            {
                bool success = state.PaymentObserved && state.Outcome.HasSuccessfulOutput();
                if (success && !state.Preserve) state.Debit.Commit();
                else
                {
                    if (!success) state.PersonalBefore.Restore();
                    state.Debit.Dispose();
                }
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Workshop70] host craft " +
                        (success ? state.Preserve ? "completed; upgrade resources preserved" : "committed exact chest debit" : "rolled back; no confirmed output/payment"));
            }
            finally { if (ReferenceEquals(Current, state)) Current = null; }
        }
    }

    internal sealed class WorkshopInventorySnapshot
    {
        private readonly Inventory _inventory;
        private readonly List<ItemDrop.ItemData> _items;
        private readonly List<int> _stacks = new List<int>();
        private readonly List<ItemDrop.ItemData> _copies = new List<ItemDrop.ItemData>();
        internal WorkshopInventorySnapshot(Inventory inventory)
        {
            _inventory = inventory;
            _items = new List<ItemDrop.ItemData>(inventory.GetAllItems());
            foreach (var item in _items)
            {
                _stacks.Add(item.m_stack); var copy = item.Clone();
                copy.m_customData = new Dictionary<string, string>(item.m_customData);
                _copies.Add(copy);
            }
        }
        internal int Count(string name)
        {
            int count = 0;
            for (int i = 0; i < _items.Count; i++) if (_items[i].m_shared.m_name == name) count += _stacks[i];
            return count;
        }
        internal void Restore()
        {
            // Preserve original ItemData identities (equipment/selected upgrade),
            // instead of Inventory.Load replacing the player's objects with clones.
            _inventory.GetAllItems().Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i]; var copy = _copies[i];
                item.m_stack = _stacks[i]; item.m_quality = copy.m_quality;
                item.m_durability = copy.m_durability; item.m_variant = copy.m_variant;
                item.m_crafterID = copy.m_crafterID; item.m_crafterName = copy.m_crafterName;
                item.m_customData = new Dictionary<string, string>(copy.m_customData);
                _inventory.GetAllItems().Add(item);
            }
            _inventory.Changed();
        }
    }

    internal sealed class WorkshopInventoryStore : IWorkshopDebitStore
    {
        private readonly Container _container;
        private readonly Inventory _inventory;
        internal WorkshopInventoryStore(Container container) { _container = container; _inventory = container.GetInventory(); }
        public object Identity => _inventory;
        internal bool CanWrite() => _container != null && _container.m_nview != null &&
            _container.m_nview.IsValid() && _container.m_nview.IsOwner() && !_container.IsInUse() &&
            ReferenceEquals(_inventory, _container.GetInventory());
        public int Count(string item) => _inventory.CountItems(item, -1, false);
        public object Capture() => new WorkshopInventorySnapshot(_inventory);
        public void Remove(string item, int amount) => _inventory.RemoveItem(item, amount, -1, false);
        public void Restore(object snapshot) => ((WorkshopInventorySnapshot)snapshot).Restore();
        public void Flush()
        {
            if (!CanWrite()) throw new InvalidOperationException("Workshop chest ownership/access changed during debit.");
            _container.Save();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class WorkshopHostCraftActionPatch
    {
        internal sealed class Scope
        {
            internal WorkshopHostCrafting.Attempt Attempt;
        }
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(InventoryGui __instance, Player player, out Scope __state)
        {
            __state = new Scope();
            WorkshopHostCrafting.CraftCallDepth++;
            if (WorkshopHostCrafting.CraftCallDepth > 1) return false;
            return WorkshopHostCrafting.Begin(__instance, player, out __state.Attempt);
        }
        private static Exception Finalizer(Scope __state, Exception __exception)
        {
            if (__state == null) return __exception; // An earlier mod may have skipped our prefix.
            try { WorkshopHostCrafting.Finish(__state.Attempt); }
            catch (Exception error) { MasteryPlugin.Log.LogError("[Workshop70] Action rollback failed: " + error); return __exception ?? error; }
            finally { WorkshopHostCrafting.CraftCallDepth = Math.Max(0, WorkshopHostCrafting.CraftCallDepth - 1); }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirementItems))]
    internal static class WorkshopHostCraftRequirementsPatch
    {
        private static void Postfix(Player __instance, Recipe piece, bool discover, int qualityLevel, int amount, ref bool __result)
        {
            if (__result || discover) return;
            if (WorkshopRemoteCraft.HasCredit(__instance, piece, qualityLevel, amount)) { __result = true; return; }
            if (WorkshopHostCrafting.HasCredit(__instance, piece, qualityLevel, amount)) { __result = true; return; }
            if (WorkshopHostCrafting.CraftCallDepth == 0 && WorkshopRemoteCraft.Preview(__instance, piece, qualityLevel, amount))
            { __result = true; return; }
            // Only this adapter's executable host path can advertise stock. Remote
            // clients cannot use previews as authority to obtain free output.
            if (WorkshopHostCrafting.CraftCallDepth == 0 && WorkshopHostCrafting.Current == null && WorkshopHostCrafting.TryPlan(__instance, piece, qualityLevel, amount, out _))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class WorkshopHostCraftPaymentPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int multiplier) =>
            WorkshopHostCrafting.ObservePayment(__instance, requirements, qualityLevel, multiplier);
    }
}
