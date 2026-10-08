using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class StationBulkLoadService
    {
        internal static int ExtraToQuarterCapacity(int capacity, int occupied)
        {
            // Target one quarter of TOTAL capacity, including vanilla's first unit.
            // Round up to whole items; clamp to remaining capacity, then subtract
            // the unit vanilla has already accepted in the original interaction.
            int batch = Mathf.CeilToInt(capacity * 0.25f);
            return Mathf.Max(0, Mathf.Min(batch, capacity - occupied) - 1);
        }
        internal static void FillOre(Smelter station, Player player, int remaining)
        {
            if (station?.m_nview == null || player == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35)) return;
            Inventory inventory = player.GetInventory();
            while (remaining-- > 0)
            {
                ItemDrop.ItemData item = station.FindCookableItem(inventory);
                if (item?.m_dropPrefab == null || !station.IsItemAllowed(item)) break;
                string prefab = item.m_dropPrefab.name;
                bool cheated = item.m_cheated;
                if (!inventory.RemoveItem(item, 1)) break;
                station.m_nview.InvokeRPC("RPC_AddOre", prefab, cheated);
            }
        }

        internal static void FillFuel(Smelter station, Player player, int remaining)
        {
            if (station?.m_nview == null || station.m_fuelItem?.m_itemData?.m_shared == null || player == null ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35)) return;
            Inventory inventory = player.GetInventory();
            string name = station.m_fuelItem.m_itemData.m_shared.m_name;
            while (remaining-- > 0 && inventory.HaveItem(name, true))
            {
                inventory.RemoveItem(name, 1, -1, true);
                station.m_nview.InvokeRPC("RPC_AddFuel");
            }
        }

        internal static void FillCookingFuel(CookingStation station, Player player, int remaining)
        {
            if (station?.m_nview == null || station.m_fuelItem?.m_itemData?.m_shared == null || player == null ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35)) return;
            Inventory inventory = player.GetInventory();
            string name = station.m_fuelItem.m_itemData.m_shared.m_name;
            while (remaining-- > 0 && inventory.HaveItem(name, true))
            {
                inventory.RemoveItem(name, 1, -1, true);
                station.m_nview.InvokeRPC("RPC_AddFuel");
            }
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddOre))]
    internal static class Crafting35BulkOrePatch
    {
        private static void Prefix(Smelter __instance, Humanoid user, out int __state)
        {
            __state = user is Player player && PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35)
                ? StationBulkLoadService.ExtraToQuarterCapacity(__instance.m_maxOre, __instance.GetQueueSize()) : 0;
        }
        private static void Postfix(Smelter __instance, Humanoid user, bool __result, int __state)
        {
            if (__result) StationBulkLoadService.FillOre(__instance, user as Player, __state);
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddFuel))]
    internal static class Crafting35BulkFuelPatch
    {
        private static void Prefix(Smelter __instance, Humanoid user, out int __state)
        {
            __state = user is Player player && PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35)
                ? StationBulkLoadService.ExtraToQuarterCapacity(__instance.m_maxFuel, Mathf.CeilToInt(__instance.GetFuel())) : 0;
        }
        private static void Postfix(Smelter __instance, Humanoid user, bool __result, int __state)
        {
            if (__result) StationBulkLoadService.FillFuel(__instance, user as Player, __state);
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.OnAddFuelSwitch))]
    internal static class Crafting35BulkCookingFuelPatch
    {
        private static void Prefix(CookingStation __instance, Humanoid user, out int __state)
        {
            __state = user is Player player && PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35)
                ? StationBulkLoadService.ExtraToQuarterCapacity(__instance.m_maxFuel, Mathf.CeilToInt(__instance.GetFuel())) : 0;
        }
        private static void Postfix(CookingStation __instance, Humanoid user, bool __result, int __state)
        {
            if (__result) StationBulkLoadService.FillCookingFuel(__instance, user as Player, __state);
        }
    }
}
