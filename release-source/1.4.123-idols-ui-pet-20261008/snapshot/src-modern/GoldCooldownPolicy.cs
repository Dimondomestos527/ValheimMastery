using System;

namespace ValheimMastery
{
    // Callers supply authoritative patron identities, never the selected UI card.
    // Legacy v1-v3 crafting state migrates exclusively to Volundr.
    internal static class GoldCooldownPolicy
    {
        internal const string LegacyPatronId = "Volundr";
        internal const double LegacySeconds = 20d * 60d;
        internal const double ModerateSeconds = 5d * 60d;

        internal static double ForFavorCost(float cost)
        {
            if (!ValidCost(cost)) throw new ArgumentOutOfRangeException(nameof(cost));
            return cost <= 250f ? 0d : cost <= 500f ? ModerateSeconds : LegacySeconds;
        }

        internal static bool CanStart(string actionPatron, float cost, string exhaustedPatron, double remaining)
        {
            if (!ValidPatron(actionPatron) || !ValidCost(cost) || !ValidRemaining(remaining)) return false;
            if (remaining == 0d) return true;
            // Unknown timer provenance must never grant the other-patron exception.
            return ValidPatron(exhaustedPatron) && cost <= 250f &&
                !String.Equals(actionPatron, exhaustedPatron, StringComparison.Ordinal);
        }

        internal static double RemainingAfterCommit(float cost, double existing)
        {
            if (!ValidRemaining(existing)) throw new ArgumentOutOfRangeException(nameof(existing));
            double duration = ForFavorCost(cost);
            // A permitted cheap action of another patron cannot cancel its peer's timer.
            return duration == 0d ? existing : duration;
        }

        private static bool ValidCost(float value) => !Single.IsNaN(value) && !Single.IsInfinity(value) && value >= 0f;
        private static bool ValidRemaining(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value) &&
            value >= 0d && value <= LegacySeconds;
        private static bool ValidPatron(string value) => !String.IsNullOrWhiteSpace(value) && value.Length <= 128;
    }
}
