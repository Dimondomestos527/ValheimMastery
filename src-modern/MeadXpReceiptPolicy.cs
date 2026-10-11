using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimMastery
{
    internal static class MeadXpReceiptPolicy
    {
        internal const float PreparationXp = 4f, LoadXp = 1f, CompletionXp = 5f;
        internal const string Prefix = "valheim_mastery.mead_xp.v1.";
        internal static float BaseXp(int stage) => stage == 2 ? LoadXp : stage == 3 ? CompletionXp : 0f;
        internal static bool ValidCoefficient(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 1f && value <= 4.5f;

        // Native player.customData is saved with the same character snapshot as skill state.
        // One monotonic watermark per station and stage; old receipts cannot be replayed.
        internal static bool TryRecord(IDictionary<string, string> data, string station, long serial, int stage, out string previous)
        {
            previous = null;
            if (data == null || string.IsNullOrEmpty(station) || station.Length > 160 || serial <= 0 || BaseXp(stage) == 0f) return false;
            string key = Prefix + station + "." + stage;
            if (data.TryGetValue(key, out previous) &&
                (!long.TryParse(previous, NumberStyles.None, CultureInfo.InvariantCulture, out long seen) || seen >= serial)) return false;
            data[key] = serial.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        internal static bool Seen(IDictionary<string, string> data, string station, long serial, int stage) =>
            serial > 0 && BaseXp(stage) > 0 && data != null && !string.IsNullOrEmpty(station) &&
            data.TryGetValue(Prefix + station + "." + stage, out string value) &&
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seen) && seen >= serial;
        internal static void Rollback(IDictionary<string, string> data, string station, long serial, int stage, string previous)
        {
            string key = Prefix + station + "." + stage;
            if (!data.TryGetValue(key, out string current) || current != serial.ToString(CultureInfo.InvariantCulture)) return;
            if (previous == null) data.Remove(key); else data[key] = previous;
        }
    }
}
