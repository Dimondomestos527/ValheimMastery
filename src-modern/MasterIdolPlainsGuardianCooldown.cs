using HarmonyLib;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(Player), "UpdateGuardianPower")]
    internal static class MasterIdolPlainsGuardianCooldown
    {
        // Native method owns subtraction/clamping/save behavior; scale only its
        // elapsed time, not the stored remaining timer or selected power.
        private static void Prefix(Player __instance, ref float dt)
        {
            if (MasteryPlugin.Settings?.Enabled.Value != true ||
                !MasterIdolPlains.Owned(__instance) || __instance.IsDead() || __instance.IsTeleporting() ||
                __instance.m_guardianPowerCooldown <= 0f ||
                MasterIdolEffectZones.At("Plains", __instance.transform.position) == null) return;
            dt = Elapsed(dt);
        }
        internal static float Elapsed(float elapsed)
            => elapsed > 0f && !float.IsNaN(elapsed) && !float.IsInfinity(elapsed) && elapsed <= float.MaxValue / 5f
                ? elapsed * 5f : elapsed;
    }
}
