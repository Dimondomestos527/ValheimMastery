using HarmonyLib;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(Container), nameof(Container.CheckAccess))]
    internal static class WorkshopStorageLockPatch
    {
        private static void Postfix(Container __instance, long playerID, ref bool __result)
        {
            // Covers the vanilla open/stack/take-all RPC checks too. A rollback
            // failure must not leave a quarantined inventory publicly writable.
            if (__result && (WorkshopChestLease.BlocksAccess(__instance, playerID) || WorkshopAtomicDebit.IsLocked(__instance?.GetInventory()) ||
                !string.IsNullOrEmpty(__instance?.m_nview?.GetZDO()?.GetString(WorkshopRemoteCraft.EscrowKey, "")))) __result = false;
        }
    }
}
