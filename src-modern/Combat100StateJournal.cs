using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ValheimMastery
{
    // Trusted SERVER producer storage, not a client participation/skill proof.
    // Keep completed event identities: deleting/pruning them would mint rewards again.
    // No prepared Tyr evidence is inferred from this award journal.
    internal sealed class Combat100StateJournal : IDisposable
    {
        internal const string AwardSource = "combat100-v1";
        internal const int MaxEncounters = 65536;
        private const int MaxBytes = 32 * 1024 * 1024;
        private const uint Magic = 0x43314A31;
        private const int Version = 1;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        internal enum Outcome { Pending = 0, Accepted = 1, Duplicate = 2, Expired = 3 }
        internal sealed class Award
        {
            internal long PlayerId, Sequence;
            internal string EncounterId;
            internal float Tyr, Odin;
            internal Outcome Result;
            internal Award Copy() => (Award)MemberwiseClone();
            internal Dictionary<string, float> Split()
            {
                var split = new Dictionary<string, float>(StringComparer.Ordinal);
                if (Tyr > 0) split.Add("Tyr", Tyr);
                if (Odin > 0) split.Add("Odin", Odin);
                return split;
            }
        }
        private sealed class Encounter
        {
            internal string Id;
            internal Award[] Awards;
            internal Encounter Copy()
            {
                var copy = new Encounter { Id = Id, Awards = new Award[Awards.Length] };
                for (int i = 0; i < Awards.Length; ++i) copy.Awards[i] = Awards[i].Copy();
                return copy;
            }
        }
        private readonly object _gate = new object();
        private readonly string _path;
        private readonly long _world;
        private Dictionary<string, Encounter> _events = new Dictionary<string, Encounter>(StringComparer.Ordinal);
        private bool _attemptedLoad, _ready, _faulted, _disposed;
        private FileStream _lease;
        internal Combat100StateJournal(string path, long worldUid)
        {
            if (string.IsNullOrWhiteSpace(path) || worldUid == 0) throw new ArgumentException("Journal path/world required.");
            _path = Path.GetFullPath(path); _world = worldUid;
        }
        internal long WorldUid => _world;
        internal bool IsAvailable { get { lock (_gate) return _ready && !_faulted; } }
        // One load per instance; a fault cannot silently restore old in-memory authority.
        internal bool Load()
        {
            lock (_gate)
            {
                if (_attemptedLoad || _disposed) return false;
                _attemptedLoad = true;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    bool previousLease = File.Exists(_path + ".lock");
                    _lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    if (!File.Exists(_path))
                    {
                        if (previousLease || File.Exists(_path + ".tmp") || File.Exists(_path + ".bak")) return Fault();
                        // Persist the world binding even for an empty journal.
                        Persist(_events); _ready = true; return true;
                    }
                    var info = new FileInfo(_path);
                    if (info.Length < 52 || info.Length > MaxBytes) return Fault();
                    byte[] data = File.ReadAllBytes(_path);
                    using (var stream = new MemoryStream(data, false))
                    using (var reader = new BinaryReader(stream, Utf8, true))
                    {
                        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version || reader.ReadInt64() != _world) return Fault();
                        int length = reader.ReadInt32();
                        if (length < 4 || length > MaxBytes - 52 || data.Length != length + 52) return Fault();
                        byte[] digest = reader.ReadBytes(32), body = reader.ReadBytes(length);
                        using (var sha = SHA256.Create())
                        {
                            byte[] actual = sha.ComputeHash(body);
                            for (int i = 0; i < 32; ++i) if (digest[i] != actual[i]) return Fault();
                        }
                        var parsed = Parse(body); _events = parsed;
                    }
                    _ready = true; return true;
                }
                catch { return Fault(); }
            }
        }
        // Freeze ALL personal awards for one completed encounter in ONE durable write.
        // An exact retry returns the original sequences/results; a changed retry fails.
        internal bool Prepare(string eventId, Combat100FavorModel.Reward[] rewards, out Award[] awards)
        {
            awards = null;
            if (!Identity(eventId) || rewards == null || rewards.Length > Combat100FavorModel.MaxParticipants) return false;
            var incoming = new Award[rewards.Length];
            var players = new HashSet<long>();
            for (int i = 0; i < rewards.Length; ++i)
            {
                var r = rewards[i];
                if (r == null || r.EncounterId != eventId || r.PlayerId == 0 || !players.Add(r.PlayerId) || !Amounts(r.Tyr, r.Odin)) return false;
                incoming[i] = new Award { PlayerId = r.PlayerId, EncounterId = eventId, Tyr = r.Tyr, Odin = r.Odin };
            }
            Array.Sort(incoming, (a, b) => a.PlayerId.CompareTo(b.PlayerId));
            lock (_gate)
            {
                if (!_ready || _faulted) return false;
                if (_events.TryGetValue(eventId, out var old))
                {
                    if (old.Awards.Length != incoming.Length) return false;
                    for (int i = 0; i < incoming.Length; ++i)
                        if (old.Awards[i].PlayerId != incoming[i].PlayerId || old.Awards[i].Tyr != incoming[i].Tyr || old.Awards[i].Odin != incoming[i].Odin) return false;
                    awards = old.Copy().Awards; return true;
                }
                if (_events.Count >= MaxEncounters) return false;
                foreach (var a in incoming)
                {
                    long high = 0;
                    foreach (var e in _events.Values) foreach (var previous in e.Awards)
                        if (previous.PlayerId == a.PlayerId && previous.Sequence > high) high = previous.Sequence;
                    if (high == long.MaxValue) return false;
                    a.Sequence = high + 1;
                }
                var candidate = CopyEvents();
                candidate.Add(eventId, new Encounter { Id = eventId, Awards = incoming });
                if (!Commit(candidate)) return false;
                awards = _events[eventId].Copy().Awards; return true;
            }
        }
        // Delivery coordinator may only send this player's earliest pending sequence.
        // Rejected/Unavailable awards stay here; newer awards cannot evict its Gold receipt.
        internal Award Peek(long playerId)
        {
            lock (_gate)
            {
                if (!_ready || _faulted || playerId == 0) return null;
                Award found = null;
                foreach (var e in _events.Values) foreach (var a in e.Awards)
                    if (a.PlayerId == playerId && a.Result == Outcome.Pending && (found == null || a.Sequence < found.Sequence)) found = a;
                return found?.Copy();
            }
        }
        // Call ONLY after durable Gold Accepted/Duplicate, or definitive Expired.
        // No timeout/rejection/local deletion is a successful acknowledgment.
        internal bool Acknowledge(long playerId, string eventId, long sequence, Outcome result)
        {
            if (result < Outcome.Accepted || result > Outcome.Expired) return false;
            lock (_gate)
            {
                if (!_ready || _faulted || !_events.TryGetValue(eventId ?? "", out var e)) return false;
                Award target = null;
                foreach (var a in e.Awards) if (a.PlayerId == playerId && a.Sequence == sequence) { target = a; break; }
                if (target == null) return false;
                if (target.Result != Outcome.Pending) return target.Result == result ||
                    target.Result != Outcome.Expired && result != Outcome.Expired;
                var head = Peek(playerId);
                if (head == null || head.Sequence != sequence) return false;
                var candidate = CopyEvents();
                foreach (var a in candidate[eventId].Awards) if (a.PlayerId == playerId) a.Result = result;
                return Commit(candidate);
            }
        }
        private Dictionary<string, Encounter> CopyEvents()
        {
            var copy = new Dictionary<string, Encounter>(StringComparer.Ordinal);
            foreach (var pair in _events) copy.Add(pair.Key, pair.Value.Copy());
            return copy;
        }
        private bool Commit(Dictionary<string, Encounter> candidate)
        {
            try { Persist(candidate); _events = candidate; return true; }
            // Replacement may already have committed. NEVER roll back and keep awarding.
            catch { return Fault(); }
        }
        private bool Fault() { _ready = false; _faulted = true; return false; }
        public void Dispose()
        {
            lock (_gate)
            {
                _ready = false; _disposed = true;
                _lease?.Dispose(); _lease = null;
            }
        }
        private void Persist(Dictionary<string, Encounter> candidate)
        {
            byte[] body;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                var keys = new List<string>(candidate.Keys); keys.Sort(StringComparer.Ordinal);
                writer.Write(keys.Count);
                foreach (var key in keys)
                {
                    var e = candidate[key]; writer.Write(e.Id); writer.Write(e.Awards.Length);
                    foreach (var a in e.Awards)
                    {
                        writer.Write(a.PlayerId); writer.Write(a.Sequence); writer.Write(a.Tyr); writer.Write(a.Odin); writer.Write((int)a.Result);
                    }
                }
                writer.Flush(); if (stream.Length > MaxBytes - 52) throw new IOException("Combat journal capacity exceeded.");
                body = stream.ToArray();
            }
            byte[] digest; using (var sha = SHA256.Create()) digest = sha.ComputeHash(body);
            string directory = Path.GetDirectoryName(_path); Directory.CreateDirectory(directory);
            string temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Utf8, true))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(_world); writer.Write(body.Length); writer.Write(digest); writer.Write(body);
                writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak", true);
            else File.Move(temp, _path);
        }
        private Dictionary<string, Encounter> Parse(byte[] body)
        {
            var parsed = new Dictionary<string, Encounter>(StringComparer.Ordinal);
            var perPlayer = new Dictionary<long, SortedDictionary<long, Outcome>>();
            using (var stream = new MemoryStream(body, false))
            using (var reader = new BinaryReader(stream, Utf8, true))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxEncounters) throw new InvalidDataException();
                for (int i = 0; i < count; ++i)
                {
                    // Bound string BEFORE allocation, rather than BinaryReader.ReadString.
                    string id = ReadIdentity(reader);
                    int size = reader.ReadInt32();
                    if (size < 0 || size > Combat100FavorModel.MaxParticipants || parsed.ContainsKey(id)) throw new InvalidDataException();
                    var e = new Encounter { Id = id, Awards = new Award[size] }; long previousPlayer = long.MinValue;
                    for (int j = 0; j < size; ++j)
                    {
                        var a = new Award { EncounterId = id, PlayerId = reader.ReadInt64(), Sequence = reader.ReadInt64(),
                            Tyr = reader.ReadSingle(), Odin = reader.ReadSingle(), Result = (Outcome)reader.ReadInt32() };
                        if (a.PlayerId == 0 || j > 0 && a.PlayerId <= previousPlayer || a.Sequence <= 0 || !Amounts(a.Tyr, a.Odin) || a.Result < Outcome.Pending || a.Result > Outcome.Expired) throw new InvalidDataException();
                        previousPlayer = a.PlayerId; e.Awards[j] = a;
                        if (!perPlayer.TryGetValue(a.PlayerId, out var sequences)) perPlayer.Add(a.PlayerId, sequences = new SortedDictionary<long, Outcome>());
                        if (sequences.ContainsKey(a.Sequence)) throw new InvalidDataException();
                        sequences.Add(a.Sequence, a.Result);
                    }
                    parsed.Add(id, e);
                }
                if (stream.Position != stream.Length) throw new InvalidDataException();
            }
            foreach (var sequences in perPlayer.Values)
            {
                long expected = 1; bool pending = false;
                foreach (var pair in sequences)
                {
                    if (pair.Key != expected++) throw new InvalidDataException();
                    if (pair.Value == Outcome.Pending) pending = true;
                    else if (pending) throw new InvalidDataException("Out-of-order delivery receipt.");
                }
            }
            return parsed;
        }
        private static string ReadIdentity(BinaryReader reader)
        {
            int length = 0, shift = 0;
            for (int i = 0; i < 3; ++i)
            {
                byte b = reader.ReadByte(); length |= (b & 127) << shift;
                if ((b & 128) == 0)
                {
                    if (length < 1 || length > 160) throw new InvalidDataException();
                    byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
                    string value = Utf8.GetString(bytes); if (!Identity(value)) throw new InvalidDataException(); return value;
                }
                shift += 7;
            }
            throw new InvalidDataException();
        }
        private static bool Identity(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 160) return false;
            foreach (char c in value) if (c < 33 || c > 126) return false;
            return true;
        }
        private static bool Amounts(float tyr, float odin) => !float.IsNaN(tyr) && !float.IsInfinity(tyr) &&
            !float.IsNaN(odin) && !float.IsInfinity(odin) && tyr >= 0 && odin >= 0 && tyr + (double)odin > 0 && tyr + (double)odin <= 1000;
    }
}


