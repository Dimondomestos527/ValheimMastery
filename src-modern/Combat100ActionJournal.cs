using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ValheimMastery
{
    // SERVER action plans and authenticated owner-proof retention. This store
    // NEVER authenticates an RPC or authorizes an effect by itself.
    internal sealed class Combat100ActionJournal : IDisposable
    {
        internal enum Kind { Muster = 1, SecondBreath = 2 }
        internal enum Phase { Planned, Reserved, Consumed, Committed, Revoked, Finished, Released, Cancelled, Abandoned }
        internal sealed class Recipient
        {
            internal long PlayerId, Owner;
            internal string Character;
            internal bool Delivered;
            internal Recipient Copy() => (Recipient)MemberwiseClone();
        }
        internal sealed class Record
        {
            internal Kind Action;
            internal Phase State;
            internal GoldPatronGrant Grant;
            internal long PlayerId, Owner, CreatedUtcTicks, ConsumedUtcTicks, DeadlineUtcTicks;
            internal string Character, Epoch, OwnerProof;
            internal Recipient[] Recipients;
            internal Record Copy()
            {
                var r = (Record)MemberwiseClone(); r.Grant = Grant.Copy();
                r.Recipients = new Recipient[Recipients.Length];
                for (int i = 0; i < Recipients.Length; ++i) r.Recipients[i] = Recipients[i].Copy();
                return r;
            }
        }
        private const uint Magic = 0x43314131;
        private const int Version = 2, MaxRecords = 65536, MaxBytes = 32 * 1024 * 1024;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly long _world;
        private Dictionary<string, Record> _records = new Dictionary<string, Record>(StringComparer.Ordinal);
        private FileStream _lease;
        private bool _loaded, _attempted, _faulted, _disposed;
        internal Combat100ActionJournal(string path, long world)
        {
            if (string.IsNullOrWhiteSpace(path) || world == 0) throw new ArgumentException("Action journal path/world required.");
            _path = Path.GetFullPath(path); _world = world;
        }
        internal long WorldUid => _world;
        internal bool IsAvailable { get { lock (_gate) return _loaded && !_faulted && !_disposed; } }
        internal bool Load()
        {
            lock (_gate)
            {
                if (_attempted || _disposed) return false; _attempted = true;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    bool prior = File.Exists(_path + ".lock");
                    _lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    if (!File.Exists(_path))
                    {
                        if (prior || File.Exists(_path + ".bak") || File.Exists(_path + ".tmp")) return Fault();
                        Persist(_records); _loaded = true; return true;
                    }
                    long length = new FileInfo(_path).Length;
                    if (length < 56 || length > MaxBytes) return Fault();
                    using (var stream = new MemoryStream(File.ReadAllBytes(_path), false))
                    using (var r = new BinaryReader(stream, Utf8, true))
                    {
                        if (r.ReadUInt32() != Magic) return Fault();
                        int version = r.ReadInt32();
                        if (version < 1 || version > Version || r.ReadInt64() != _world) return Fault();
                        int size = r.ReadInt32(); if (size < 4 || size != length - 52) return Fault();
                        byte[] hash = r.ReadBytes(32), body = r.ReadBytes(size);
                        using (var sha = SHA256.Create())
                        {
                            var actual = sha.ComputeHash(body); for (int i = 0; i < 32; ++i) if (hash[i] != actual[i]) return Fault();
                        }
                        _records = Parse(body);
                        if (version == 1) foreach (var record in _records.Values) if (record.State >= Phase.Cancelled) return Fault();
                    }
                    _loaded = true; return true;
                }
                catch { return Fault(); }
            }
        }
        internal Record Get(string token)
        {
            lock (_gate) return IsAvailable && token != null && _records.TryGetValue(token, out var r) ? r.Copy() : null;
        }
        internal Record[] Pending()
        {
            lock (_gate)
            {
                if (!IsAvailable) return Array.Empty<Record>();
                var result = new List<Record>();
                foreach (var r in _records.Values) if (r.State != Phase.Finished && r.State != Phase.Released && r.State != Phase.Cancelled && r.State != Phase.Abandoned) result.Add(r.Copy());
                result.Sort((a, b) => a.PlayerId != b.PlayerId ? a.PlayerId.CompareTo(b.PlayerId) : a.Grant.Generation.CompareTo(b.Grant.Generation));
                return result.ToArray();
            }
        }
        internal string[] RecoveryTokens()
        {
            lock (_gate)
            {
                if (!IsAvailable) return Array.Empty<string>();
                var result = new List<string>();
                foreach (var r in _records.Values)
                    if (r.Action == Kind.Muster && r.State != Phase.Abandoned) result.Add(r.Grant.Token);
                result.Sort(StringComparer.Ordinal); return result.ToArray();
            }
        }
        internal bool Prepare(Record proposed)
        {
            Record frozen;
            try { frozen = proposed?.Copy(); } catch { return false; }
            if (frozen == null || frozen.State != Phase.Planned || !Valid(frozen) ||
                frozen.ConsumedUtcTicks != 0 || !string.IsNullOrEmpty(frozen.OwnerProof)) return false;
            lock (_gate)
            {
                if (!IsAvailable) return false;
                if (_records.TryGetValue(frozen.Grant.Token, out var old)) return SamePlan(old, frozen);
                if (_records.Count >= MaxRecords) return false;
                // No second unresolved prepared grant for the same actor.
                foreach (var other in _records.Values)
                    if (other.State != Phase.Abandoned && other.PlayerId == frozen.PlayerId && other.Grant.Generation == frozen.Grant.Generation ||
                        frozen.Action == Kind.SecondBreath && other.Action == Kind.SecondBreath && other.PlayerId == frozen.PlayerId &&
                        other.State != Phase.Finished && other.State != Phase.Released) return false;
                var next = Copy(); next.Add(frozen.Grant.Token, frozen); return Commit(next);
            }
        }
        internal bool MarkReserved(string token)
        {
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r)) return false;
                if (r.State == Phase.Reserved) return true;
                if (r.State != Phase.Planned) return false;
                var next = Copy(); next[token].State = Phase.Reserved; return Commit(next);
            }
        }
        // Called ONLY by authenticated owner-receipt ingress after validating real
        // durable character CONSUMED/REVOKED evidence, original grant, world/session
        // and current native peer/player binding. A timeout is NOT this evidence.
        internal bool StorePreparedProof(string token, bool consumed, long utcTicks, string immutableProof)
        {
            if (!Utc(utcTicks) || !Text(immutableProof, 4096)) return false;
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r) || r.Action != Kind.SecondBreath) return false;
                Phase terminal = consumed ? Phase.Consumed : Phase.Revoked;
                if (r.State == terminal || consumed && (r.State == Phase.Committed || r.State == Phase.Finished))
                    return r.OwnerProof == immutableProof && r.ConsumedUtcTicks == utcTicks;
                if (r.State != Phase.Reserved || utcTicks < r.CreatedUtcTicks) return false;
                var next = Copy(); var n = next[token]; n.State = terminal; n.OwnerProof = immutableProof; n.ConsumedUtcTicks = utcTicks;
                n.DeadlineUtcTicks = consumed ? utcTicks + TimeSpan.FromSeconds(60).Ticks : 0;
                if (!Valid(n)) return false;
                return Commit(next);
            }
        }
        internal bool MarkCommitted(string token)
        {
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r)) return false;
                if (r.State == Phase.Committed || r.State == Phase.Finished) return true;
                if (!(r.Action == Kind.Muster && r.State == Phase.Reserved || r.Action == Kind.SecondBreath && r.State == Phase.Consumed)) return false;
                var next = Copy(); next[token].State = Phase.Committed; return Commit(next);
            }
        }
        internal bool Delivered(string token, long recipient)
        {
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r) || r.Action != Kind.Muster || r.State != Phase.Committed) return false;
                int index = Array.FindIndex(r.Recipients, x => x.PlayerId == recipient);
                if (index < 0) return false; if (r.Recipients[index].Delivered) return true;
                var next = Copy(); next[token].Recipients[index].Delivered = true; return Commit(next);
            }
        }
        // Coordinator confirms absolute expiry/real terminal evidence before call.
        // This is not an unused/refund proof, and never deletes the original token.
        internal bool Finish(string token)
        {
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r)) return false;
                if (r.State == Phase.Finished) return true;
                if (r.State != Phase.Committed) return false;
                var next = Copy(); next[token].State = Phase.Finished; return Commit(next);
            }
        }
        // Called only AFTER durable Gold Unused settlement of the retained revoked proof.
        internal bool MarkReleased(string token)
        {
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r) || r.Action != Kind.SecondBreath) return false;
                if (r.State == Phase.Released) return true;
                if (r.State != Phase.Revoked) return false;
                var next = Copy(); next[token].State = Phase.Released; return Commit(next);
            }
        }
        // Coordinator-only after exact Gold Unused, OR current available Gold
        // generation strictly below this never-admitted plan. No timeout proof.
        // Reserved is not effect authority; committed actions can never cancel.
        internal bool CancelMuster(string token, bool neverAdmitted = false)
        {
            lock (_gate)
            {
                if (!IsAvailable || !_records.TryGetValue(token ?? "", out var r) || r.Action != Kind.Muster) return false;
                Phase terminal = neverAdmitted ? Phase.Abandoned : Phase.Cancelled;
                if (r.State == terminal) return true;
                if (r.State != Phase.Planned && r.State != Phase.Reserved || neverAdmitted && r.State != Phase.Planned) return false;
                var next = Copy(); next[token].State = terminal; return Commit(next);
            }
        }
        private static bool SamePlan(Record a, Record b)
        {
            if (a.Action != b.Action || !a.Grant.Same(b.Grant) || a.PlayerId != b.PlayerId || a.Owner != b.Owner ||
                a.Character != b.Character || a.Epoch != b.Epoch || a.CreatedUtcTicks != b.CreatedUtcTicks || a.Recipients.Length != b.Recipients.Length) return false;
            for (int i = 0; i < a.Recipients.Length; ++i)
                if (a.Recipients[i].PlayerId != b.Recipients[i].PlayerId || a.Recipients[i].Owner != b.Recipients[i].Owner || a.Recipients[i].Character != b.Recipients[i].Character) return false;
            return true;
        }
        private Dictionary<string, Record> Copy()
        {
            var copy = new Dictionary<string, Record>(StringComparer.Ordinal);
            foreach (var pair in _records) copy.Add(pair.Key, pair.Value.Copy()); return copy;
        }
        private bool Commit(Dictionary<string, Record> next)
        {
            try { Persist(next); _records = next; return true; } catch { return Fault(); }
        }
        private bool Fault() { _loaded = false; _faulted = true; return false; }
        public void Dispose() { lock (_gate) { _disposed = true; _loaded = false; _lease?.Dispose(); _lease = null; } }
        private void Persist(Dictionary<string, Record> records)
        {
            byte[] body;
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream, Utf8, true))
            {
                var tokens = new List<string>(records.Keys); tokens.Sort(StringComparer.Ordinal); w.Write(tokens.Count);
                foreach (var token in tokens)
                {
                    var r = records[token]; w.Write((int)r.Action); w.Write((int)r.State);
                    Write(w, r.Grant.Token); w.Write(r.Grant.Generation); w.Write(r.PlayerId); w.Write(r.Owner);
                    Write(w, r.Character); Write(w, r.Epoch); w.Write(r.CreatedUtcTicks); w.Write(r.ConsumedUtcTicks); w.Write(r.DeadlineUtcTicks);
                    Write(w, r.OwnerProof ?? ""); w.Write(r.Recipients.Length);
                    foreach (var recipient in r.Recipients)
                    { w.Write(recipient.PlayerId); w.Write(recipient.Owner); Write(w, recipient.Character); w.Write(recipient.Delivered); }
                }
                w.Flush(); if (stream.Length > MaxBytes - 52) throw new IOException("Action journal capacity exceeded."); body = stream.ToArray();
            }
            byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(body);
            using (var stream = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new BinaryWriter(stream, Utf8, true))
            { w.Write(Magic); w.Write(Version); w.Write(_world); w.Write(body.Length); w.Write(hash); w.Write(body); w.Flush(); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, _path + ".bak", true); else File.Move(_path + ".tmp", _path);
        }
        private Dictionary<string, Record> Parse(byte[] body)
        {
            var parsed = new Dictionary<string, Record>(StringComparer.Ordinal);
            var generations = new Dictionary<long, HashSet<long>>();
            var unresolvedTyr = new HashSet<long>();
            using (var stream = new MemoryStream(body, false)) using (var r = new BinaryReader(stream, Utf8, true))
            {
                int count = r.ReadInt32(); if (count < 0 || count > MaxRecords) throw new InvalidDataException();
                for (int i = 0; i < count; ++i)
                {
                    var record = new Record { Action = (Kind)r.ReadInt32(), State = (Phase)r.ReadInt32(), Grant = new GoldPatronGrant() };
                    record.Grant.Token = Read(r, 32); record.Grant.Generation = r.ReadInt64();
                    record.PlayerId = r.ReadInt64(); record.Owner = r.ReadInt64(); record.Character = Read(r, 128); record.Epoch = Read(r, 32);
                    record.CreatedUtcTicks = r.ReadInt64(); record.ConsumedUtcTicks = r.ReadInt64(); record.DeadlineUtcTicks = r.ReadInt64(); record.OwnerProof = Read(r, 4096);
                    int recipients = r.ReadInt32(); if (recipients < 0 || recipients > 64) throw new InvalidDataException();
                    record.Recipients = new Recipient[recipients];
                    for (int j = 0; j < recipients; ++j) record.Recipients[j] = new Recipient { PlayerId = r.ReadInt64(), Owner = r.ReadInt64(), Character = Read(r, 128), Delivered = r.ReadBoolean() };
                    CanonicalGrant(record);
                    if (!Valid(record) || parsed.ContainsKey(record.Grant.Token)) throw new InvalidDataException();
                    if (!generations.TryGetValue(record.PlayerId, out var known)) generations.Add(record.PlayerId, known = new HashSet<long>());
                    if (record.State != Phase.Abandoned && !known.Add(record.Grant.Generation)) throw new InvalidDataException();
                    if (record.Action == Kind.SecondBreath && record.State != Phase.Finished && record.State != Phase.Released &&
                        !unresolvedTyr.Add(record.PlayerId)) throw new InvalidDataException("Multiple unresolved Tyr grants.");
                    parsed.Add(record.Grant.Token, record);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException();
            }
            return parsed;
        }
        private static void CanonicalGrant(Record r)
        {
            r.Grant.Patron = r.Action == Kind.Muster ? "Odin" : "Tyr";
            r.Grant.Action = r.Action == Kind.Muster ? "OdinMuster" : "TyrSecondBreath";
            r.Grant.Cost = 750; r.Grant.Prepared = r.Action == Kind.SecondBreath;
        }
        private static bool Valid(Record r)
        {
            if (r == null || r.Grant == null || !Guid.TryParseExact(r.Grant.Token, "N", out _) ||
                !Guid.TryParseExact(r.Epoch, "N", out _) || r.PlayerId == 0 || r.Owner == 0 || !Text(r.Character, 128) ||
                !Utc(r.CreatedUtcTicks) || r.CreatedUtcTicks > DateTime.MaxValue.Ticks - TimeSpan.FromSeconds(300).Ticks ||
                r.Grant.Generation <= 0 || r.Grant.Cost != 750 || r.State < Phase.Planned || r.State > Phase.Abandoned ||
                (r.Action != Kind.Muster && r.Action != Kind.SecondBreath) || r.Recipients == null || r.Recipients.Length > 64) return false;
            if (r.Action == Kind.Muster)
            {
                if (r.Grant.Patron != "Odin" || r.Grant.Action != "OdinMuster" || r.Grant.Prepared || r.Recipients.Length < 1 ||
                    r.DeadlineUtcTicks != r.CreatedUtcTicks + TimeSpan.FromSeconds(300).Ticks || r.ConsumedUtcTicks != 0 ||
                    !string.IsNullOrEmpty(r.OwnerProof) || r.State == Phase.Consumed || r.State == Phase.Revoked || r.State == Phase.Released) return false;
                var ids = new HashSet<long>(); bool caster = false;
                foreach (var recipient in r.Recipients)
                {
                    if (recipient == null || recipient.PlayerId == 0 || recipient.Owner == 0 || !Text(recipient.Character, 128) || !ids.Add(recipient.PlayerId) ||
                        recipient.Delivered && r.State != Phase.Committed && r.State != Phase.Finished) return false;
                    if (recipient.PlayerId == r.PlayerId && recipient.Owner == r.Owner && recipient.Character == r.Character) caster = true;
                }
                return caster;
            }
            if (r.State == Phase.Cancelled || r.State == Phase.Abandoned) return false;
            if (r.Grant.Patron != "Tyr" || r.Grant.Action != "TyrSecondBreath" || !r.Grant.Prepared || r.Recipients.Length != 0) return false;
            bool hasProof = r.State >= Phase.Consumed;
            if (!hasProof) return r.ConsumedUtcTicks == 0 && r.DeadlineUtcTicks == 0 && string.IsNullOrEmpty(r.OwnerProof);
            if (!Utc(r.ConsumedUtcTicks) || r.ConsumedUtcTicks < r.CreatedUtcTicks || r.ConsumedUtcTicks > DateTime.MaxValue.Ticks - TimeSpan.FromSeconds(60).Ticks || !Text(r.OwnerProof, 4096)) return false;
            return r.State == Phase.Revoked || r.State == Phase.Released ? r.DeadlineUtcTicks == 0 : r.DeadlineUtcTicks == r.ConsumedUtcTicks + TimeSpan.FromSeconds(60).Ticks;
        }
        private static bool Utc(long value) => value > 0 && value <= DateTime.MaxValue.Ticks;
        private static bool Text(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length > max) return false;
            foreach (char c in value) if (c < 33 || c > 126) return false; return true;
        }
        private static void Write(BinaryWriter w, string value) { byte[] bytes = Utf8.GetBytes(value); w.Write(bytes.Length); w.Write(bytes); }
        private static string Read(BinaryReader r, int max)
        {
            int size = r.ReadInt32(); if (size < 0 || size > max) throw new InvalidDataException();
            byte[] bytes = r.ReadBytes(size); if (bytes.Length != size) throw new EndOfStreamException(); return Utf8.GetString(bytes);
        }
    }
}



