using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimMastery
{
    // A demolition creates one negative placement credit, not a blanket ban.
    // Positive placement credits are intentionally not banked: old walls can also
    // be recycled for XP unless every real demolition creates a matching debt.
    internal sealed class CraftingBuildReuseModel
    {
        internal const double WindowSeconds = 300d;
        private const int MaxKinds = 128, MaxDebt = 100000;
        private sealed class Entry { internal int Debt; internal double Until; }
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal bool Removed(string kind, double now)
        {
            if (!Valid(kind, now)) return false;
            Prune(now);
            if (!_entries.TryGetValue(kind, out Entry entry))
            {
                // Do not evict a live penalty to reward spam of many recipe names.
                if (_entries.Count >= MaxKinds) return false;
                _entries.Add(kind, entry = new Entry());
            }
            entry.Debt = Math.Min(MaxDebt, entry.Debt + 1);
            entry.Until = now + WindowSeconds;
            return true;
        }

        internal bool ConsumeReplacement(string kind, double now)
        {
            if (!Valid(kind, now)) return false;
            Prune(now);
            if (!_entries.TryGetValue(kind, out Entry entry) || entry.Debt <= 0) return false;
            if (--entry.Debt == 0) _entries.Remove(kind);
            return true;
        }

        internal string Encode(double now)
        {
            Prune(now);
            var text = new StringBuilder("1\n");
            foreach (var pair in _entries)
            {
                text.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key))).Append('|')
                    .Append(pair.Value.Debt.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(pair.Value.Until.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            return text.ToString();
        }

        internal static CraftingBuildReuseModel Decode(string raw, double now)
        {
            var result = new CraftingBuildReuseModel();
            if (String.IsNullOrEmpty(raw) || raw.Length > 40000 || !GoldFavorModel.Finite(now)) return result;
            string[] rows = raw.Split('\n');
            if (rows.Length > MaxKinds + 2 || rows[0] != "1") return result;
            foreach (string row in rows)
            {
                string[] parts = row.Split('|');
                if (parts.Length != 3) continue;
                try
                {
                    string kind = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                    if (!Valid(kind, now) || !Int32.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ||
                        count <= 0 || count > MaxDebt || !Double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double until) ||
                        !GoldFavorModel.Finite(until) || until <= now || until > now + WindowSeconds) continue;
                    result._entries[kind] = new Entry { Debt = count, Until = until };
                }
                catch (FormatException) { }
            }
            return result;
        }

        private void Prune(double now)
        {
            var expired = new List<string>();
            foreach (var pair in _entries) if (pair.Value.Until <= now) expired.Add(pair.Key);
            foreach (string kind in expired) _entries.Remove(kind);
        }
        private static bool Valid(string kind, double now) => !String.IsNullOrWhiteSpace(kind) && kind.Length <= 160 &&
            GoldFavorModel.Finite(now) && now >= 0d;
    }
}
