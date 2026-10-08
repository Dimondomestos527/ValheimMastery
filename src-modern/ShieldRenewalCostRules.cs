using System;
namespace ValheimMastery
{
    internal static class ShieldRenewalCostRules
    {
        internal const float ResetSeconds = 5f;
        internal static bool ResetDue(float lastPaid, float now) => !float.IsFinite(lastPaid) || !float.IsFinite(now) ||
            now < lastPaid || now - lastPaid >= ResetSeconds;
        internal static float Cost(float maximumHealth, int paidPulses)
        {
            if (!float.IsFinite(maximumHealth) || maximumHealth <= 0f) return 2f;
            return Math.Max(2f, maximumHealth * .05f) + maximumHealth * .01f * Math.Max(0, paidPulses);
        }
    }
}
