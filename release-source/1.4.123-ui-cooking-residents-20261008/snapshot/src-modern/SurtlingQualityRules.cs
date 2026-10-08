namespace ValheimMastery
{
    // Appearance has two native upgrades. Stats retain the old quality3 strength,
    // then grow independently so unsupported native visual levels are never used.
    internal static class SurtlingQualityRules
    {
        internal static int VisualLevel(int quality) => quality <= 1 ? 1 : quality == 2 ? 2 : 3;
        internal static float StatLevel(int quality) => quality <= 1 ? 1f : quality == 2 ? 2f : quality + 1f;
        internal static int LegacyQuality(int nativeLevel) => nativeLevel <= 1 ? 1 : nativeLevel == 2 ? 2 : 3;
        internal static float NativeDamageFactor(int nativeLevel) => 1f + .5f * (nativeLevel > 1 ? nativeLevel - 1f : 0f);
        internal static float DamageCorrection(int quality, int nativeLevel) =>
            (1f + .5f * (StatLevel(quality) - 1f)) / NativeDamageFactor(nativeLevel);
    }
}
