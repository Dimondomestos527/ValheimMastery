using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    internal static class MasterIdolReceiptBoundary
    {
        internal static string Capture(string token, bool entered, bool matches) => !entered && matches ? token : null;
        internal static bool CanComplete(string attempt, string token, bool entered, bool completed, bool failed) =>
            attempt != null && string.Equals(attempt, token, StringComparison.Ordinal) && entered && completed && !failed;
    }
    internal readonly struct MasterIdolRecord
    {
        internal readonly string Type, Identity;
        internal MasterIdolRecord(string type, string identity) { Type = type; Identity = identity; }
    }
    // Derived world index. Never deletes objects or treats an unloaded scene object as destroyed.
    internal sealed class MasterIdolRegistryModel
    {
        private readonly Dictionary<string, SortedSet<string>> _objects = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        internal bool Ready { get; private set; }
        internal void Reset() { _objects.Clear(); Ready = false; }
        internal void Rebuild(IEnumerable<MasterIdolRecord> actualWorldRecords)
        {
            if (actualWorldRecords == null) throw new ArgumentNullException(nameof(actualWorldRecords));
            var next = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var record in actualWorldRecords)
            {
                if (MasterIdolProfiles.Find(record.Type) == null || string.IsNullOrEmpty(record.Identity)) continue;
                if (!next.TryGetValue(record.Type, out var identities))
                    next.Add(record.Type, identities = new SortedSet<string>(StringComparer.Ordinal));
                identities.Add(record.Identity);
            }
            _objects.Clear();
            foreach (var pair in next) _objects.Add(pair.Key, pair.Value);
            Ready = true;
        }
        internal int Count(string type) => type != null && _objects.TryGetValue(type, out var set) ? set.Count : 0;
        internal string Active(string type) => type != null && _objects.TryGetValue(type, out var set) && set.Count != 0 ? set.Min : null;
        internal bool CanPlace(string type) => Ready && MasterIdolProfiles.Find(type) != null && Count(type) == 0;
        internal bool IsActive(string type, string identity) => identity != null && string.Equals(Active(type), identity, StringComparison.Ordinal);
    }
}
