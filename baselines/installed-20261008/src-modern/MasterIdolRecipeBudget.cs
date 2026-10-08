using System;

namespace ValheimMastery
{
    internal static class MasterIdolRecipeBudget
    {
        internal const double MaxWeight = 100d;
        // Uses prefab shared base weights, never player-specific perks or a guessed weight table.
        internal static int[] Fit(int[] requested, float[] weights)
        {
            if (requested == null || weights == null || requested.Length != weights.Length || requested.Length == 0) return null;
            double minimum = 0, total = 0;
            var counts = (int[])requested.Clone();
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] < 1 || float.IsNaN(weights[i]) || float.IsInfinity(weights[i]) || weights[i] < 0) return null;
                minimum += weights[i]; total += counts[i] * (double)weights[i];
            }
            if (minimum > MaxWeight) return null; // cannot preserve every ingredient even at one each
            if (total <= MaxWeight) return counts;
            double ratio = (MaxWeight - minimum) / (total - minimum);
            for (int i = 0; i < counts.Length; i++) counts[i] = 1 + (int)Math.Floor((counts[i] - 1) * ratio);
            return counts;
        }
    }
}
