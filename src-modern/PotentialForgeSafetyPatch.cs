using System;
using HarmonyLib;

namespace ValheimMastery
{
    internal sealed class PotentialForgeSafetyState
    {
        internal ItemDrop.ItemData.SharedData Shared;
        internal float OriginalBreakChance;
        internal float OriginalUpgradeChance;
        internal bool Applied;
    }

    /// <summary>
    /// The Forge of Potential may upgrade beyond the normal resource-upgrade cap.
    /// Attempts that are still inside that vanilla cap must never destroy the item;
    /// above the cap, the forge keeps its original success/failure/break chances.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class PotentialForgeSafetyPatch
    {
        internal static bool ProtectsTarget(int currentQuality, int resourceUpgradeCap) =>
            currentQuality >= 1 && currentQuality < resourceUpgradeCap;
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(InventoryGui __instance, Player player, out PotentialForgeSafetyState __state)
        {
            __state = new PotentialForgeSafetyState();

            ItemDrop.ItemData item = __instance?.m_craftUpgradeItem;
            CraftingStation station = player?.GetCurrentCraftingStation();
            ItemDrop.ItemData.SharedData shared = item?.m_shared;
            if (item == null || shared == null || station == null || !station.m_upgrader)
                return;

            // Example for the usual maxQuality=4 item:
            // q1->q2, q2->q3 and q3->q4 are protected; q4->q5 is not.
            if (!ProtectsTarget(item.m_quality, shared.m_maxQuality))
                return;

            // Vanilla DoCrafting reads failure/break chances from the FIRST
            // upgrader resource in this recipe, not from the upgraded weapon.
            ItemDrop.ItemData.SharedData upgradeResource = null;
            if (__instance.m_craftRecipe?.m_resources == null) return;
            foreach (var requirement in __instance.m_craftRecipe.m_resources)
            {
                if (requirement?.m_upgraderResource != true) continue;
                upgradeResource = requirement.m_resItem?.m_itemData?.m_shared;
                break;
            }
            if (upgradeResource == null) return;
            __state.Shared = upgradeResource;
            __state.OriginalBreakChance = upgradeResource.m_breakChance;
            __state.OriginalUpgradeChance = upgradeResource.m_upgradeChance;
            __state.Applied = true;

            // DoCrafting checks breakChance against (1 - Random.value). A negative
            // sentinel makes the destruction branch mathematically unreachable,
            // including UnityEngine.Random.value's inclusive 1.0 endpoint.
            upgradeResource.m_breakChance = -1f;
            upgradeResource.m_upgradeChance = .90f;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(PotentialForgeSafetyState __state)
        {
            Restore(__state);
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception, PotentialForgeSafetyState __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(PotentialForgeSafetyState state)
        {
            if (state == null || !state.Applied || state.Shared == null)
                return;

            state.Shared.m_breakChance = state.OriginalBreakChance;
            state.Shared.m_upgradeChance = state.OriginalUpgradeChance;
            state.Applied = false;
        }
    }
}
