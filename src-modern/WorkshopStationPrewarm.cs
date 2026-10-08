using HarmonyLib;

namespace ValheimMastery
{
    // Prime only the selected recipe's exact station environment proof. The
    // protocol throttles by station and never participates in action admission.
    [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
    internal static class WorkshopStationPrewarmPatch
    {
        private static void Postfix(InventoryGui __instance) => WorkshopStationProof.Prewarm(__instance);
    }
}
