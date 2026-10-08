using System;

namespace ValheimMastery
{
    // Kind1 is a deliberate crafting-button action. Kind2 is also an explicit station action; its Hammer requirement remains.
    // This policy does not waive Gold level, Favor, cooldown, receipts, wards or environment.
    internal static class GoldMasterworkRules
    {
        internal static bool ActionAllowed(int kind, bool armed, bool hammer) =>
            kind == 1 || (kind == 2 && hammer);

        internal static bool StationTypeMatches(string required, string current, bool upgrader, bool showsBasic)
        {
            if (upgrader) return false;
            if (required == null) return current == null || showsBasic;
            return current != null && string.Equals(required, current, StringComparison.Ordinal);
        }
    }
}