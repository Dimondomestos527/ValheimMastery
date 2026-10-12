using System;
namespace ValheimMastery
{
    internal static class MistlandsFrostVisualRules
    {
        internal const int Zones = 8, GroundNear = 64, AirNear = 48;
        internal const float VisibilityInterval = .15f, EmitInterval = .125f, AirLifetime = 4f;
        // Cosmetic hysteresis only; no gameplay radius or expiry changes.
        internal static int Tier(float squaredDistance, int previous)
        {
            if (float.IsNaN(squaredDistance) || float.IsInfinity(squaredDistance) || squaredDistance < 0f) return 3;
            if (previous == 0 && squaredDistance <= 22f * 22f) return 0;
            if (previous == 1 && squaredDistance >= 18f * 18f && squaredDistance <= 42f * 42f) return 1;
            if (previous == 2 && squaredDistance >= 38f * 38f && squaredDistance <= 82f * 82f) return 2;
            if (previous == 3 && squaredDistance >= 78f * 78f) return 3;
            return squaredDistance <= 20f * 20f ? 0 : squaredDistance <= 40f * 40f ? 1 : squaredDistance <= 80f * 80f ? 2 : 3;
        }
        internal static int Ground(int tier) => tier == 0 ? GroundNear : tier == 1 ? 32 : 0;
        internal static int Air(int tier) => tier == 0 ? AirNear : tier == 1 ? 16 : 0;
        internal static int StormDivisor(int tier) => tier <= 0 ? 1 : tier == 1 ? 2 : 4;
        internal static int EmitCount(ref float credit, int population, float elapsed)
        {
            if (population <= 0) { credit = 0f; return 0; }
            if (float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed <= 0f) return 0;
            credit += population / AirLifetime * Math.Min(elapsed, .25f);
            int result = (int)credit; credit -= result;
            return result;
        }
    }
}
