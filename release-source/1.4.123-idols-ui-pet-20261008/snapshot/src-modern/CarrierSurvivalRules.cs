using System;

namespace ValheimMastery
{
    internal static class CarrierSurvivalRules
    {
        internal const float HealthMultiplier = 5f;
        internal const float RegenDelay = 10f;
        internal const float RegenFractionPerSecond = .05f;
        internal const float ThreatRadius = 12f;
        internal const float FleeRange = 18f;
        internal const float TargetDistancePenalty = 3f;

        internal static float ResizedHealth(float health, float previousMax, float desiredMax)
        {
            if (!float.IsFinite(health) || !float.IsFinite(previousMax) || !float.IsFinite(desiredMax) ||
                health <= 0f || previousMax <= 0f || desiredMax <= 0f) return 0f;
            return Math.Min(1f, health / previousMax) * desiredMax;
        }
        internal static float RegenAmount(float health, float max, float sinceCombat, float dt)
        {
            if (!float.IsFinite(health) || !float.IsFinite(max) || !float.IsFinite(sinceCombat) ||
                !float.IsFinite(dt) || health <= 0f || max <= health || dt <= 0f || sinceCombat <= RegenDelay)
                return 0f;
            // Heal only the part of this live frame that is after the delay.
            // A stalled/offline client must not turn a hitch into a large heal.
            float eligible = Math.Min(.25f, Math.Min(dt, sinceCombat - RegenDelay));
            return Math.Min(max - health, max * RegenFractionPerSecond * eligible);
        }
        internal static bool PreferAlternate(float carrierDistance, float alternateDistance)
        {
            return float.IsFinite(carrierDistance) && float.IsFinite(alternateDistance) &&
                carrierDistance >= 0f && alternateDistance >= 0f &&
                alternateDistance < carrierDistance * TargetDistancePenalty;
        }
    }
}
