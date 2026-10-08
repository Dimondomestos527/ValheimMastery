using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    internal enum WorkshopRequestAdmission { Accepted, Duplicate, Capacity, Invalid }

    // World-session replay protection follows the authenticated CHARACTER, not
    // the disposable connection. Entries are never evicted to admit a replay.
    // Restart durability is a separate escrow/journal concern.
    internal sealed class WorkshopRequestLedger
    {
        private readonly Dictionary<long, HashSet<string>> _used = new Dictionary<long, HashSet<string>>();
        private readonly int _perCharacter, _totalLimit;
        private int _count;
        internal WorkshopRequestLedger(int perCharacter = 65536, int totalLimit = 1000000)
        {
            if (perCharacter <= 0 || totalLimit <= 0) throw new ArgumentOutOfRangeException();
            _perCharacter = perCharacter; _totalLimit = totalLimit;
        }
        internal WorkshopRequestAdmission Remember(long playerId, string id)
        {
            if (playerId == 0 || !Guid.TryParseExact(id, "N", out _)) return WorkshopRequestAdmission.Invalid;
            if (_used.TryGetValue(playerId, out var ids) && ids.Contains(id)) return WorkshopRequestAdmission.Duplicate;
            if (_count >= _totalLimit || (ids != null && ids.Count >= _perCharacter)) return WorkshopRequestAdmission.Capacity;
            if (ids == null) _used.Add(playerId, ids = new HashSet<string>(StringComparer.Ordinal));
            ids.Add(id); _count++; return WorkshopRequestAdmission.Accepted;
        }
        internal void Clear() { _used.Clear(); _count = 0; }
    }
}
