using System;
namespace ValheimMastery
{
    internal static class ClubsReserveRules
    {
        // Actual reserved stamina is the charge unit. Maces reach full at a
        // third of natural stamina: 15/s keeps the former 22.5/s half-cap timing.
        internal static float Capacity(float naturalMaximum, bool mace = false) =>
            Math.Max(0f, naturalMaximum) * (mace ? 1f / 3f : .5f);
        internal static float Rate(bool mace) => mace ? 15f : 10f;
        internal static float Fraction(float reserve, float naturalMaximum, bool mace) =>
            Math.Max(0f, Math.Min(1f, reserve / Math.Max(1f, Capacity(naturalMaximum, mace))));
        internal static float Fit(float reserve, float naturalMaximum, bool mace = false) =>
            Math.Max(0f, Math.Min(reserve, Capacity(naturalMaximum, mace)));
    }
}
