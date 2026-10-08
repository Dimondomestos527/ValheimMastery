using System;
using System.Collections.Generic;
using System.Globalization;

namespace ValheimMastery
{
    internal static class WorkshopSettlementHistory
    {
        internal const int Limit = 128;
        private static List<string> Read(string text)
        {
            var entries = new List<string>();
            if (string.IsNullOrEmpty(text)) return entries;
            foreach (string line in text.Split('\n'))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 3 || (fields[0] != "C" && fields[0] != "R") ||
                    !long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long player) || player == 0 ||
                    !Guid.TryParseExact(fields[2], "N", out _)) throw new InvalidOperationException("Invalid Workshop settlement history.");
                entries.Add(line);
                if (entries.Count > Limit) throw new InvalidOperationException("Oversized Workshop settlement history.");
            }
            return entries;
        }
        internal static string Append(string text, long player, string id, bool commit)
        {
            var entries = Read(text);
            if (player == 0 || !Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid settlement identity.");
            string suffix = "|" + player.ToString(CultureInfo.InvariantCulture) + "|" + id;
            string record = (commit ? "C" : "R") + suffix;
            foreach (string entry in entries)
                if (entry.EndsWith(suffix, StringComparison.Ordinal))
                { if (entry != record) throw new InvalidOperationException("Conflicting settlement decision."); return text; }
            if (entries.Count == Limit) entries.RemoveAt(0);
            entries.Add(record); return string.Join("\n", entries);
        }
        internal static bool TryFind(string text, long player, string id, out bool commit)
        {
            string suffix = "|" + player.ToString(CultureInfo.InvariantCulture) + "|" + id;
            foreach (string entry in Read(text))
                if (entry.EndsWith(suffix, StringComparison.Ordinal)) { commit = entry[0] == 'C'; return true; }
            commit = false; return false;
        }
    }
}
