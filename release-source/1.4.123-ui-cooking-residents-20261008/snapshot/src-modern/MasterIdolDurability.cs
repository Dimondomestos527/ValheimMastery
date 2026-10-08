using HarmonyLib;

namespace ValheimMastery
{
    // Block native damage/wear only. Hammer Remove -> RPC_Remove -> Destroy is preserved.
    internal static class MasterIdolDurability
    {
        internal static bool Protected(WearNTear piece) => piece != null && piece.GetComponent<MasterIdolPiece>() != null;
    }
    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class MasterIdolDamagePatch
    { private static bool Prefix(WearNTear __instance) => !MasterIdolDurability.Protected(__instance); }
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class MasterIdolDamageRpcPatch
    { private static bool Prefix(WearNTear __instance) => !MasterIdolDurability.Protected(__instance); }
    [HarmonyPatch(typeof(WearNTear), "ApplyDamage")]
    internal static class MasterIdolWearDamagePatch
    {
        private static bool Prefix(WearNTear __instance, ref bool __result)
        { if(!MasterIdolDurability.Protected(__instance))return true;__result=false;return false; }
    }
}
