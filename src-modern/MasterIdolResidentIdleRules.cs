using System;
namespace ValheimMastery
{
    internal static class MasterIdolResidentIdleRules
    {
        internal const float SlotSeconds = 4f, VibeSeconds = 2.4f, ElectionCadence = .5f, GapSeconds = .25f, SpreadCadence = 12f;
        internal const int MaxHomeRecords = 64, MaxActiveHomes = 8, MaxGraphics = 4, MaxResidents = 8;
        internal static bool Due(float now, float next) => float.IsFinite(now) && float.IsFinite(next) && now >= next;
        internal static bool Current(float now, float until) => float.IsFinite(now) && float.IsFinite(until) && until > now;
        internal static int Next(int previous, int count) => count < 1 || count > MaxResidents ? -1 : (previous + 1) % count;
        internal static bool Vibe(string family, int turn) => family == "Greydwarf" && (turn & 1) != 0;
    }
}
