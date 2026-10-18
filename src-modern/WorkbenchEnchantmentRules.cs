using System;
using System.Collections.Generic;
namespace ValheimMastery
{
    // Pure admission policy. Native station level and saved learned state MUST be
    // authenticated by the caller; this class cannot debit, persist or admit a RPC.
    internal static class WorkbenchEnchantmentRules
    {
        internal const int MaximumChoices = 6;
        internal static bool Known(string id) => id == "Meadows" || id == "BlackForest" ||
            id == "Swamp" || id == "Mountain" || id == "Plains" || id == "Mistlands";
        internal static int Capacity(int nativeLevel) => nativeLevel < 1 ? 0 : Math.Min(nativeLevel, MaximumChoices - 1) + 1;
        private static bool TrySnapshot(IEnumerable<string> learned, out HashSet<string> seen)
        {
            seen = new HashSet<string>(StringComparer.Ordinal);
            if (learned == null) return false;
            foreach (var id in learned)
                if (!Known(id) || !seen.Add(id) || seen.Count > MaximumChoices) return false;
            return true;
        }
        internal static bool TryLearnedCount(IEnumerable<string> learned, out int count)
        {
            bool valid = TrySnapshot(learned, out var seen);
            count = valid ? seen.Count : 0; return valid;
        }
        internal static bool CanLearn(IEnumerable<string> learned, string candidate, int currentNativeLevel)
        {
            return Known(candidate) && TrySnapshot(learned, out var seen) &&
                seen.Count < Capacity(currentNativeLevel) && !seen.Contains(candidate);
        }
        // The persisted purchase order is authoritative. A level drop changes only
        // this projection; it must never truncate the paid history or refund it.
        internal static bool TryAvailable(IEnumerable<string> purchaseOrder, int currentNativeLevel,
            out string[] available)
        {
            available = Array.Empty<string>();
            if (purchaseOrder == null) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var selected = new List<string>();
            int capacity = Capacity(currentNativeLevel);
            foreach (var id in purchaseOrder)
            {
                if (!Known(id) || !seen.Add(id) || seen.Count > MaximumChoices) return false;
                if (selected.Count < capacity) selected.Add(id);
            }
            available = selected.ToArray();
            return true;
        }
    }
}
