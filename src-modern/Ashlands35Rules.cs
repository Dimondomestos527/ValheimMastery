using System;
namespace ValheimMastery
{
    internal static class Ashlands35Rules
    {
        internal const float ChargeSeconds = 3f, ChainRadius = 5f;
        internal const double FriendlySeconds = 15d, RootBoostSeconds = 15d;
        internal static int Steps(float held) => !float.IsFinite(held) || held <= 0f ? 0 : Math.Min(6, (int)Math.Floor(Math.Min(held, 3f) * 2f));
        internal static int Fragments(int steps)
        {
            steps = Math.Clamp(steps, 0, 6);
            // Half-second charge stages reach +60% before the full-charge jump.
            return steps == 6 ? 24 : (int)Math.Ceiling(12d * (1d + .6d * steps / 5d));
        }
        internal static float Damage(int steps) => 1f + Math.Clamp(steps, 0, 6) / 12f;
        internal static float Radius(int steps) => 1f + Math.Clamp(steps, 0, 6) / 12f;
        internal static float Cost(float held) => 1f + .5f * Math.Clamp(float.IsFinite(held) ? held : 0f, 0f, 3f) / 3f;
        internal static long Until(long now, double seconds) => checked(now + (long)(seconds * TimeSpan.TicksPerSecond));
        internal static bool Active(long now, long until) => now > 0 && until > now;
        internal static bool RootCanRegrow(int generation, bool spent) => generation == 0 && !spent;
        // Durable 64-serial receipt window accepts out-of-order volleys without a per-pellet proc.
        internal static bool Claim(ref long head, ref ulong bits, long serial)
        {
            if (serial <= 0 || head < 0) return false;
            if (serial > head) { long shift = serial - head; bits = shift >= 64 ? 0UL : bits << (int)shift; head = serial; }
            long behind = head - serial;
            if (behind >= 64 || (bits & (1UL << (int)behind)) != 0) return false;
            bits |= 1UL << (int)behind; return true;
        }
        internal static bool AcceptPellet(ref ushort mask, int pellet, int total)
        {
            if (total < 1 || total > 12 || pellet < 0 || pellet >= total || (mask & (1 << pellet)) != 0) return false;
            mask |= (ushort)(1 << pellet); return true;
        }
        internal static bool Majority(ushort mask, int total)
        {
            if (total < 1 || total > 12) return false;
            int count = 0; for (int i = 0; i < total; i++) if ((mask & (1 << i)) != 0) count++;
            return count > total / 2;
        }
        internal static int ExtraPellets(int total) => (int)Math.Ceiling(Math.Clamp(total, 1, 12) * .4d);
    }
}
