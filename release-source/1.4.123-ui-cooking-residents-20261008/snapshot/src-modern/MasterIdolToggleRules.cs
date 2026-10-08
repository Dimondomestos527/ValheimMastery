using System;

namespace ValheimMastery
{
    internal static class MasterIdolToggleRules
    {
        internal static bool On(int state) => state >= 0 && (state & 1) != 0;
        internal static bool CanChange(int current, int expected, bool desired) =>
            current >= 0 && current < int.MaxValue && current == expected && On(current) != desired;
        internal static float Brightness(float from, bool on, float elapsed)
        {
            float t = Math.Max(0f, Math.Min(1f, elapsed / .85f));
            float smooth = t * t * (3f - 2f * t);
            float fade = from + ((on ? 1f : 0f) - from) * smooth;
            float pulse = on && elapsed > .35f && elapsed < 1.15f
                ? .3f * (float)Math.Sin(Math.PI * (elapsed - .35f) / .8f) : 0f;
            return Math.Max(0f, Math.Min(1.3f, fade + pulse));
        }
    }
}
