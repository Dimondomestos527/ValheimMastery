using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;

namespace ValheimMastery
{
    // Bounded TEST-only observation of the reported SeekerAspic regression.
    // Never stamps, merges, grants, saves or modifies a native argument/result.
    internal static class CookingProductTrace
    {
        internal const string Target = "SeekerAspic";
        internal sealed class Attempt
        {
            internal Attempt Previous;
            internal int Number;
            internal int Events;
            internal Player Player;
            internal Inventory Inventory;
            internal HashSet<ItemDrop> ExistingDrops;
        }
        [ThreadStatic] internal static Attempt Current;
        private static int attempts;
        internal static bool Matches(ItemDrop.ItemData item) => item != null && TierDatabase.ItemKey(item) == Target;
        internal static string Describe(ItemDrop.ItemData item, Player producer)
        {
            if (item == null) return "null";
            var data = item.m_customData;
            string tier = data != null && data.TryGetValue(Cooking35Service.CookMasteryKey, out var t) ? t : "absent";
            string version = data != null && data.TryGetValue(CookingProductIdentity.VersionKey, out var v) ? v : "absent";
            // Only two bounded numeric/version fields are printed; arbitrary custom
            // values, actor IDs and names never enter the trace.
            if (tier != "0" && tier != "35" && tier != "70" && tier != "absent") tier = "invalid";
            if (version != "1" && version != "absent") version = "invalid";
            int foreignKeys = data?.Count ?? 0;
            if (data?.ContainsKey(Cooking35Service.CookMasteryKey) == true) foreignKeys--;
            if (data?.ContainsKey(CookingProductIdentity.VersionKey) == true) foreignKeys--;
            string digest = "oversize";
            // Cap both key/value count and digest work; never print foreign keys.
            if (data == null || (data.Count <= 32 && data.Sum(e => (long)e.Key.Length + (e.Value?.Length ?? 0)) <= 8192))
            {
                string canonical = data == null ? "null" : string.Concat(data.OrderBy(e => e.Key, StringComparer.Ordinal)
                    .Select(e => e.Key.Length + ":" + e.Key + (e.Value ?? "").Length + ":" + (e.Value ?? "")));
                using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "");
            }
            return "prefab=" + TierDatabase.ItemKey(item) + " q=" + item.m_quality + " variant=" + item.m_variant +
                " world=" + item.m_worldLevel + " stack=" + item.m_stack + " tier=" + tier + " version=" + version +
                " authorPresent=" + (item.m_crafterID != 0) + " authorMatches=" + (producer != null && item.m_crafterID == producer.GetPlayerID()) +
                " nameMatches=" + (producer != null && string.Equals(item.m_crafterName, producer.GetPlayerName(), StringComparison.Ordinal)) +
                " foreignKeyCount=" + foreignKeys + " customSHA256=" + digest;
        }
        internal static void Observe(string boundary, ItemDrop.ItemData item, Inventory inventory)
        {
            var state = Current;
            if (state == null || state.Events >= 48 || !ReferenceEquals(state.Inventory, inventory)) return;
            try
            {
                if (!Matches(item)) return;
                state.Events++;
                var production = CookingProductionScope.Current;
                MasteryPlugin.Log.LogInfo("[CookingIdentityTrace] attempt=" + state.Number + " boundary=" + boundary +
                    " creates=" + (production?.Creates == true) + " template=" + (production?.Template != null) +
                    " " + Describe(item, state.Player));
            }
            catch { /* Diagnostics must never alter an inventory operation. */ }
        }
        internal static Attempt Begin(InventoryGui gui, Player player)
        {
            if (player == null || player != Player.m_localPlayer || gui?.m_craftRecipe?.m_item == null ||
                gui.m_craftRecipe.m_item.gameObject.name != Target || gui.m_craftUpgradeItem != null || attempts >= 32) return null;
            var state = new Attempt { Previous = Current, Number = ++attempts, Player = player, Inventory = player.GetInventory() };
            try
            {
                state.ExistingDrops = new HashSet<ItemDrop>(ItemDrop.s_instances);
                Current = state;
                foreach (var item in state.Inventory.GetAllItems().Where(Matches)) Observe("inventory-before", item, state.Inventory);
                MasteryPlugin.Log.LogInfo("[CookingIdentityTrace] attempt=" + state.Number + " begin recipeAmount=" + gui.m_craftRecipe.m_amount +
                    " requestedRecipes=" + gui.m_multiCraftAmount + " emptySlots=" + state.Inventory.GetEmptySlots());
                return state;
            }
            catch { Current = state.Previous; return null; }
        }
        internal static void End(Attempt state, Exception error)
        {
            if (state == null) return;
            try
            {
                foreach (var item in state.Inventory.GetAllItems().Where(Matches)) Observe("inventory-after", item, state.Inventory);
                foreach (var drop in ItemDrop.s_instances)
                    if (drop != null && state.Events < 48 && !state.ExistingDrops.Contains(drop) && Matches(drop.m_itemData))
                    {
                        state.Events++;
                        MasteryPlugin.Log.LogInfo("[CookingIdentityTrace] attempt=" + state.Number + " new-ground=" + Describe(drop.m_itemData, state.Player));
                    }
                MasteryPlugin.Log.LogInfo("[CookingIdentityTrace] attempt=" + state.Number + " end exception=" + (error != null) + " eventLimitReached=" + (state.Events >= 48));
            }
            catch { }
            finally { Current = state.Previous; }
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class CookingProductTraceCraftPatch
    {
        [HarmonyPriority(Priority.First + 1000)]
        private static void Prefix(InventoryGui __instance, Player player, out CookingProductTrace.Attempt __state) =>
            __state = CookingProductTrace.Begin(__instance, player);
        [HarmonyPriority(Priority.Last - 1000)]
        private static Exception Finalizer(CookingProductTrace.Attempt __state, Exception __exception)
        { CookingProductTrace.End(__state, __exception); return __exception; }
    }
    [HarmonyPatch]
    internal static class CookingProductTraceInventoryPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[] {
            AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData) }),
            AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) }) };
        [HarmonyPriority(Priority.First + 1000)]
        private static void Prefix(Inventory __instance, ItemDrop.ItemData item) => CookingProductTrace.Observe("add-before", item, __instance);
        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __result) =>
            CookingProductTrace.Observe(__result ? "add-accepted" : "add-rejected", item, __instance);
    }
}



