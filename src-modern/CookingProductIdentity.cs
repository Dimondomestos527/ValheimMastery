using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class CookingProductIdentity
    {
        internal const string VersionKey = "valheim_mastery.cooking_product.v1";
        internal static bool Relevant(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null) return false;
            string key = TierDatabase.ItemKey(item);
            return Cooking35Service.IsPreparedMeal(item) || Cooking35Service.IsTimedMead(item) ||
                (key.StartsWith("MeadBase", StringComparison.Ordinal) || key=="BarleyWineBase") ||
                (key.StartsWith("Feast", StringComparison.Ordinal) && key.EndsWith("_Material", StringComparison.Ordinal));
        }
        internal static bool Fresh(ItemDrop.ItemData item) => item?.m_customData != null && item.m_customData.ContainsKey(VersionKey);
        internal static bool TryTier(ItemDrop.ItemData item, out int tier)
        {
            tier = 0;
            return item?.m_crafterID != 0 && item?.m_customData != null &&
                item.m_customData.TryGetValue(VersionKey, out string version) && version == "1" &&
                item.m_customData.TryGetValue(Cooking35Service.CookMasteryKey, out string value) &&
                int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out tier) &&
                (tier == 0 || tier == 35 || tier == 70);
        }
        internal static void Stamp(ItemDrop.ItemData item, Player producer)
        {
            if (!Relevant(item) || producer == null || producer.GetPlayerID() == 0) return;
            int tier = PerkRuntimeService.HasPerk(producer, Skills.SkillType.Cooking, 70) ? 70 :
                PerkRuntimeService.HasPerk(producer, Skills.SkillType.Cooking, 35) ? 35 : 0;
            item.m_customData[VersionKey] = "1";
            item.m_customData[Cooking35Service.CookMasteryKey] = tier.ToString(CultureInfo.InvariantCulture);
            item.m_crafterID = producer.GetPlayerID(); item.m_crafterName = producer.GetPlayerName();
            Cooking70FeastRetrofit.MarkBorn(item);
        }
        internal static bool Compatible(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            if (!Relevant(a) && !Relevant(b)) return true;
            if (a?.m_shared == null || b?.m_shared == null) return false;
            if (a.m_crafterID != b.m_crafterID ||
                !string.Equals(a.m_crafterName,b.m_crafterName,StringComparison.Ordinal)) return false;
            // Every preserved custom field is semantic until an explicit contract
            // proves otherwise. Native merge keeps the destination ItemData.
            if (a.m_customData == null || b.m_customData == null || a.m_customData.Count != b.m_customData.Count) return false;
            var target=b.m_customData.Comparer.Equals(StringComparer.Ordinal) || b.m_customData.Comparer.Equals(EqualityComparer<string>.Default)
                ? b.m_customData : new Dictionary<string,string>(b.m_customData,StringComparer.Ordinal);
            foreach (var entry in a.m_customData)
                if (!target.TryGetValue(entry.Key,out string value) ||
                    !string.Equals(entry.Value,value,StringComparison.Ordinal)) return false;
            return true;
        }
    }

    internal sealed class CookingProductionScope
    {
        [ThreadStatic] internal static CookingProductionScope Current;
        internal CookingProductionScope Previous;
        internal ItemDrop.ItemData Template;
        internal Inventory Inventory;
        internal Player Producer;
        internal string Prefab;
        internal bool Restored;
        internal bool Creates;
        internal static CookingProductionScope Begin(InventoryGui gui, Player player)
        {
            var state = new CookingProductionScope { Previous = Current };
            Current = state;
            var item = gui?.m_craftRecipe?.m_item?.m_itemData;
            if (gui?.m_craftUpgradeItem != null || gui?.m_craftRecipe?.m_item == null || item == null ||
                player == null || player != Player.m_localPlayer) return state;
            var prototype = item.Clone();
            prototype.m_dropPrefab = gui.m_craftRecipe.m_item.gameObject;
            if (!CookingProductIdentity.Relevant(prototype)) return state;
            state.Inventory = player.GetInventory(); state.Producer = player;
            state.Prefab = TierDatabase.ItemKey(prototype);
            state.Template = prototype;
            CookingProductIdentity.Stamp(state.Template, player);
            state.Template.m_worldLevel = Game.m_worldLevel;
            return state;
        }
        internal static void StampCreated(Inventory inventory, ItemDrop.ItemData item)
        {
            var state = Current;
            if (state?.Template == null || !state.Creates || state.Inventory != inventory ||
                item == null || TierDatabase.ItemKey(item) != state.Prefab) return;
            // Only the actual private chunk about to enter the inventory is changed.
            item.m_customData = new Dictionary<string, string>(state.Template.m_customData);
            item.m_crafterID = state.Template.m_crafterID;
            item.m_crafterName = state.Template.m_crafterName;
            item.m_worldLevel = state.Template.m_worldLevel;
        }
        internal static void End(CookingProductionScope state)
        {
            if (state == null || state.Restored) return;
            state.Restored = true; Current = state.Previous;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class CookingProductCreationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(InventoryGui __instance, Player player, out CookingProductionScope __state) =>
            __state = CookingProductionScope.Begin(__instance, player);
        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(CookingProductionScope __state, Exception __exception)
        { CookingProductionScope.End(__state); return __exception; }
    }
}
