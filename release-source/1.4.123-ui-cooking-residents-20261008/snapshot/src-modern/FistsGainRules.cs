using System;
namespace ValheimMastery
{
    internal static class FistsGainRules
    {
        internal static bool WeaponEligible(bool mainUnarmed, bool offhandEmptyOrFist) => mainUnarmed && offhandEmptyOrFist;
        internal static bool Allow(bool enabled, bool fists, float gain) => !enabled || gain <= 0f || fists;
        internal static float PassiveScale(float level) => 1f + .015f * Math.Max(0f, Math.Min(100f, level));
    }
}
