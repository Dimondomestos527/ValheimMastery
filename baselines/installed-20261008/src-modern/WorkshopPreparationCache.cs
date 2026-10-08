using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace ValheimMastery
{
    // Scheduling hints only: contains no inventory, credit or action permission.
    internal sealed class WorkshopPreparationCache
    {
        private readonly Dictionary<string, float> _sent = new Dictionary<string, float>(StringComparer.Ordinal);
        private string _selected;
        private float _selectedAt;
        internal int Count => _sent.Count;
        internal void Clear() { _sent.Clear(); Idle(); }
        internal void Idle() { _selected = null; }
        internal bool ShouldSend(string key, float now)
        {
            foreach (var entry in new List<KeyValuePair<string, float>>(_sent))
                if (now - entry.Value >= 30f) _sent.Remove(entry.Key);
            if (_selected != key) { _selected = key; _selectedAt = now; return false; }
            if (now - _selectedAt < .15f || (_sent.TryGetValue(key, out float last) && now - last < 4f)) return false;
            if (!_sent.ContainsKey(key) && _sent.Count >= 32)
            {
                string oldest = null; float oldestAt = float.MaxValue;
                foreach (var entry in _sent) if (entry.Value < oldestAt) { oldest = entry.Key; oldestAt = entry.Value; }
                if (oldest != null) _sent.Remove(oldest);
            }
            _sent[key] = now;
            return true;
        }
    }
    internal static class WorkshopPreparationReturn
    {
        internal static string Stamp(string token, byte[] bytes)
        {
            if (bytes == null || !Guid.TryParseExact(token, "N", out _)) return "";
            using (var hash = SHA256.Create()) return token + "|" + Convert.ToBase64String(hash.ComputeHash(bytes));
        }
        internal static bool Matches(string stamp, string token, byte[] bytes) =>
            !string.IsNullOrEmpty(stamp) && stamp == Stamp(token, bytes);
    }
    internal enum WorkshopPreparationRetirement { Wait, Ready, Quarantine }
    internal static class WorkshopPreparationRetirementRules
    {
        internal static WorkshopPreparationRetirement Evaluate(float now, float deadline, bool serverOwned,
            bool originDisconnected, bool acknowledgedBytesMatch, bool protectedStock)
        {
            if (protectedStock) return WorkshopPreparationRetirement.Wait;
            if (serverOwned && (acknowledgedBytesMatch || originDisconnected)) return WorkshopPreparationRetirement.Ready;
            return now > deadline ? WorkshopPreparationRetirement.Quarantine : WorkshopPreparationRetirement.Wait;
        }
    }
}
