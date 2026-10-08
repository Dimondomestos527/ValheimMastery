using System;
namespace ValheimMastery
{
    internal static class MagicPresentationRules
    {
        internal const int FireParticleCapacity = 200;
        internal static float FireCharge(float held) => float.IsNaN(held) ? 0f : Math.Max(0f, Math.Min(1f, held / 3f));
        internal static int FireParticles(float held) => 8 + (int)Math.Round(192f * Math.Pow(FireCharge(held), 1.7));
        internal static float FireParticleSize(float held) => .02f + .055f * FireCharge(held);
        internal static float FireParticleAlpha(float held) => .25f + .7f * FireCharge(held);
        internal static float FireOrbitSpan(float held) => .45f + .50f * FireCharge(held);
        internal static float FireOrbitRadius(float held) => .07f + .15f * FireCharge(held);
        internal static float FireOrbitSize(float held) => .014f + .026f * FireCharge(held);
        internal static float FireOrbitPosition(int particle, float time, float held)
        {
            // Wrap along the staff instead of leaving embers collected at its head.
            double phase = particle * .61803398875 + (float.IsFinite(time) ? time : 0f) * (.16 + .32 * FireCharge(held));
            float along = (float)(phase - Math.Floor(phase));
            return .5f + (along - .5f) * FireOrbitSpan(held);
        }
        internal static float VerticalSpeed(float currentY, float desiredY, float dt)
        {
            if (!float.IsFinite(currentY) || !float.IsFinite(desiredY) || !float.IsFinite(dt) || dt <= 0f) return 0f;
            // Never cross the safe floor in one step; climb when terrain rises.
            float delta = desiredY - currentY;
            return Math.Max(-6f, Math.Min(4f, delta / Math.Max(.001f, dt)));
        }
    }
}

