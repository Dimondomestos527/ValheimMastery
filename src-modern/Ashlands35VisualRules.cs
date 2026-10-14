using System;
namespace ValheimMastery
{
    internal static class Ashlands35VisualRules
    {
        internal const int RubyCapacity = 9;
        internal static float Charge(float held) => float.IsFinite(held) ? Math.Clamp(held / 3f, 0f, 1f) : 0f;
        internal static int Rubies(float held) => 3 + Math.Min(6, (int)Math.Floor(Charge(held) * 6f));
        internal static float OrbitSpeed(float held) => 1.2f + 3.6f * Charge(held);
        internal static float Radius(float held) => .15f + .09f * Charge(held);
        internal static float RubySize(float held) => .065f + .015f * Charge(held);
        internal static float Along(int index)
        {
            // Each prefix covers the whole shaft; new gems fill the gaps.
            switch (index)
            {
                case 0: return .18f; case 1: return .82f; case 2: return .5f;
                case 3: return .34f; case 4: return .66f; case 5: return .1f;
                case 6: return .9f; case 7: return .42f; default: return .58f;
            }
        }
        internal static bool RootBoostActive(bool enabled, bool batch, bool hasObserver, bool valid, bool dead,
            bool managed, int generation, long now, long until) => enabled && !batch && hasObserver && valid && !dead &&
            managed && generation == 1 && now > 0 && until > now;
        internal static float RootPulse(float time) => .78f + .22f * (float)Math.Sin(time * Math.PI * 2d);
    }
}
