using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValheimMastery
{
    // Durable managed state only. This is not an inventory transaction protocol.
    internal sealed partial class GoldCraftingLedger
    {
        private const uint Magic = 0x47434C31; // GCL1
        private const int Version = 9; // Isolated deployed-v4 purchase fork; canonical v5 pins are incompatible.
        private const int MaxFileBytes = 8 * 1024 * 1024;
        private const int MaxPlayers = 50000;
        private const int MaxReceipts = 64;
        private const int MaxBackgroundStations = 64;
        private const float MaxBackgroundFavorPerHour = 300f;
        // Keep the old persisted upper bound; existing timers are not retroactively reset.
        private const double CooldownSeconds = GoldCooldownPolicy.LegacySeconds;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly object _gate = new object();
        private readonly string _path;
        private Dictionary<long, PlayerState> _players = new Dictionary<long, PlayerState>();
        private bool _loaded;
        private bool _faulted;
        private bool _dirty;
        private bool _verifyingEvidence;

        internal GoldCraftingLedger(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A ledger path is required.", "filePath");
            _path = Path.GetFullPath(filePath);
        }

        internal bool IsAvailable { get { lock (_gate) return _loaded && !_faulted; } }

        internal sealed class PendingGrant
        {
            internal string GuidToken;
            internal int Kind; // 1 = craft, 2 = upgrade
            internal string Prefab;
            internal int OldQuality;
            internal int NewQuality;
            internal int Variant;
            internal float Cost;
            internal string StationString;
            internal double ExpiresAt;

            internal PendingGrant Copy() { return (PendingGrant)MemberwiseClone(); }
        }

        internal sealed class BackgroundSequence
        {
            internal long High;
            internal ulong Seen;
            internal BackgroundSequence Copy() => (BackgroundSequence)MemberwiseClone();
        }

        internal sealed class PlayerState
        {
            internal long PlayerId;
            internal Dictionary<string, GoldPatronWallet> Wallets = NewWallets();
            // Compatibility aliases: there is exactly one canonical Volundr balance.
            internal bool Unlocked { get => Wallets[GoldCooldownPolicy.LegacyPatronId].Unlocked; set => Wallets[GoldCooldownPolicy.LegacyPatronId].Unlocked = value; }
            internal float Favor { get => Wallets[GoldCooldownPolicy.LegacyPatronId].Favor; set => Wallets[GoldCooldownPolicy.LegacyPatronId].Favor = value; }
            internal double ExhaustionRemaining;
            internal string ExhaustedPatron = string.Empty;
            internal long Generation;
            internal Dictionary<string, BackgroundSequence> PatronAwardSources = new Dictionary<string, BackgroundSequence>(StringComparer.Ordinal);
            internal Dictionary<string, GoldPatronGrant> Claims = new Dictionary<string, GoldPatronGrant>(StringComparer.Ordinal);
            internal Dictionary<string, GoldIdolPurchase> IdolPurchases = new Dictionary<string, GoldIdolPurchase>(StringComparer.Ordinal);
            internal Dictionary<string, GoldPatronResult> Results = new Dictionary<string, GoldPatronResult>(StringComparer.Ordinal);
            internal double BackgroundWindowStartUtc;
            internal float BackgroundFavorThisHour;
            internal Dictionary<string, BackgroundSequence> BackgroundStationSequences = new Dictionary<string, BackgroundSequence>(StringComparer.Ordinal);
            internal PendingGrant Pending;
            internal string LastCommitted;
            internal List<string> SettledTokens = new List<string>();

            internal PlayerState Copy()
            {
                var p = (PlayerState)MemberwiseClone();
                p.IdolPurchases = new Dictionary<string, GoldIdolPurchase>(StringComparer.Ordinal);
                foreach (var entry in IdolPurchases) p.IdolPurchases.Add(entry.Key, entry.Value.Copy());
                p.PatronAwardSources = new Dictionary<string, BackgroundSequence>(StringComparer.Ordinal);
                foreach (var pair in PatronAwardSources) p.PatronAwardSources.Add(pair.Key, pair.Value.Copy());
                p.Wallets = new Dictionary<string, GoldPatronWallet>(StringComparer.Ordinal);
                foreach (var pair in Wallets) { var wallet = pair.Value.Copy(); wallet.Held = Held(this, pair.Key); p.Wallets.Add(pair.Key, wallet); }
                p.Claims = new Dictionary<string, GoldPatronGrant>(StringComparer.Ordinal);
                foreach (var pair in Claims) p.Claims.Add(pair.Key, pair.Value.Copy());
                p.Results = new Dictionary<string, GoldPatronResult>(StringComparer.Ordinal);
                foreach (var pair in Results) p.Results.Add(pair.Key, pair.Value.Copy());
                p.Pending = Pending == null ? null : Pending.Copy();
                p.SettledTokens = new List<string>(SettledTokens);
                p.BackgroundStationSequences = new Dictionary<string, BackgroundSequence>(StringComparer.Ordinal);
                foreach (var pair in BackgroundStationSequences) p.BackgroundStationSequences.Add(pair.Key, pair.Value.Copy());
                return p;
            }
        }

        // Missing store means a new world. Any existing-but-invalid store fails closed.
        internal bool Load()
        {
            lock (_gate)
            {
                if (_verifyingEvidence) return false;
                _loaded = false;
                _faulted = false;
                _dirty = false;
                _players = new Dictionary<long, PlayerState>();
                if (!File.Exists(_path))
                {
                    if (File.Exists(_path + ".bak") || File.Exists(_path + ".tmp")) return Fault();
                    _loaded = true;
                    return true;
                }
                try
                {
                    var info = new FileInfo(_path);
                    if (info.Length < 16 || info.Length > MaxFileBytes) return Fault();
                    byte[] all = File.ReadAllBytes(_path);
                    using (var input = new MemoryStream(all, false))
                    using (var reader = new BinaryReader(input, Utf8))
                    {
                        if (reader.ReadUInt32() != Magic) return Fault();
                        int version = reader.ReadInt32();
                        if (version < 1 || version > Version || (version > 4 && version != 9)) return Fault();
                        int length = reader.ReadInt32();
                        if (length < 0 || length > MaxFileBytes - 16 || input.Length - input.Position != length + 4L) return Fault();
                        byte[] payload = reader.ReadBytes(length);
                        if (payload.Length != length || reader.ReadUInt32() != Crc32(payload)) return Fault();
                        var parsed = ReadPayload(payload, version);
                        if (parsed == null) return Fault();
                        _players = parsed;
                        if (!ValidateIdolPurchases()) return Fault();
                    }
                    _loaded = true;
                    return true;
                }
                catch { return Fault(); }
            }
        }

        internal PlayerState Get(long playerId)
        {
            lock (_gate)
            {
                if (!_loaded || playerId == 0) return null;
                PlayerState state;
                return _players.TryGetValue(playerId, out state) ? state.Copy() : new PlayerState { PlayerId = playerId, LastCommitted = string.Empty };
            }
        }

        internal bool Unlock(long playerId)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0) return false;
                PlayerState p = FindOrCreate(playerId);
                if (p.Unlocked) return false;
                p.Unlocked = true;
                return PersistOrRollback();
            }
        }

        internal bool Gain(long playerId, float amount)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !Finite(amount) || amount <= 0f) return false;
                PlayerState p;
                if (!_players.TryGetValue(playerId, out p) || !p.Unlocked) return false;
                p.Favor = Math.Min(1000f, p.Favor + amount);
                _dirty = true;
                return true;
            }
        }

        // Debug-only callers must authenticate BEFORE this method. No public RPC
        // may set a raw Favor balance, and an outstanding grant must not lose its funds.
        internal bool SetFavor(long playerId, float value)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !Finite(value) || value < 0 || value > 1000) return false;
                if (!_players.TryGetValue(playerId, out PlayerState p) || !p.Unlocked || Held(p, GoldCooldownPolicy.LegacyPatronId) > 0 || p.Pending != null || p.Claims.ContainsKey(GoldCooldownPolicy.LegacyPatronId)) return false;
                p.Favor = value; return PersistOrRollback();
            }
        }

        internal bool Elapse(long playerId, double seconds)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !Finite(seconds) || seconds <= 0d) return false;
                PlayerState p;
                if (!_players.TryGetValue(playerId, out p) || p.ExhaustionRemaining <= 0d) return false;
                p.ExhaustionRemaining = Math.Max(0d, p.ExhaustionRemaining - seconds);
                if (p.ExhaustionRemaining == 0d) p.ExhaustedPatron = string.Empty;
                _dirty = true;
                return true;
            }
        }

        internal bool HasBackgroundReceipt(long playerId, string stationKey, long sequence)
        {
            lock (_gate)
            {
                if (!_loaded || playerId == 0 || sequence <= 0 || String.IsNullOrEmpty(stationKey)) return false;
                PlayerState p; BackgroundSequence track;
                if (!_players.TryGetValue(playerId, out p) || !p.BackgroundStationSequences.TryGetValue(stationKey, out track) || sequence > track.High) return false;
                long offset = track.High - sequence;
                return offset < 64 && (track.Seen & (1UL << (int)offset)) != 0;
            }
        }

        internal bool IsBackgroundReceiptExpired(long playerId, string stationKey, long sequence)
        {
            lock (_gate)
            {
                if (!_loaded || playerId == 0 || sequence <= 0 || String.IsNullOrEmpty(stationKey)) return false;
                PlayerState p; BackgroundSequence track;
                return _players.TryGetValue(playerId, out p) && p.BackgroundStationSequences.TryGetValue(stationKey, out track) &&
                    sequence <= track.High && track.High - sequence >= 64;
            }
        }

        // Called only for a server-verified completed processing receipt. The 64-bit
        // replay window allows ordered ZDO/RPC delivery skew while keeping duplicates idempotent.
        internal float GainBackground(long playerId, float amount, double utcNow, string stationKey, long sequence)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !Finite(amount) || amount <= 0f || amount > 100f ||
                    !Finite(utcNow) || utcNow < 0d || String.IsNullOrWhiteSpace(stationKey) || stationKey.Length > 64 || sequence <= 0) return 0f;
                PlayerState p;
                if (!_players.TryGetValue(playerId, out p) || !p.Unlocked) return 0f;
                BackgroundSequence track;
                bool hasTrack = p.BackgroundStationSequences.TryGetValue(stationKey, out track);
                if (!hasTrack && p.BackgroundStationSequences.Count >= MaxBackgroundStations) return 0f;
                long newHigh = hasTrack ? track.High : 0;
                ulong newSeen = hasTrack ? track.Seen : 0UL;
                if (newHigh == 0) { newHigh = sequence; newSeen = 1UL; }
                else if (sequence > newHigh)
                {
                    long shift = sequence - newHigh;
                    newSeen = shift >= 64 ? 1UL : (newSeen << (int)shift) | 1UL;
                    newHigh = sequence;
                }
                else
                {
                    long offset = newHigh - sequence;
                    if (offset >= 64) return 0f; // Old receipt is outside the replay window: fail closed.
                    ulong bit = 1UL << (int)offset;
                    if ((newSeen & bit) != 0) return 0f;
                    newSeen |= bit;
                }
                double newWindow = p.BackgroundWindowStartUtc;
                float spentThisHour = p.BackgroundFavorThisHour;
                if (newWindow == 0d) newWindow = utcNow;
                else if (utcNow < newWindow) return 0f;
                else if (utcNow - newWindow >= 3600d) { newWindow = utcNow; spentThisHour = 0f; }
                float accepted = Math.Min(amount, Math.Min(MaxBackgroundFavorPerHour - spentThisHour, 1000f - p.Favor));
                if (accepted < 0f) accepted = 0f;
                if (!hasTrack) p.BackgroundStationSequences.Add(stationKey, track = new BackgroundSequence());
                track.High = newHigh; track.Seen = newSeen;
                p.BackgroundWindowStartUtc = newWindow; p.BackgroundFavorThisHour = spentThisHour + accepted;
                p.Favor += accepted;
                return PersistOrRollback() ? accepted : 0f;
            }
        }

        // Favor/cooldown updates are batched by the owner; security transitions remain synchronous.
        internal bool Flush()
        {
            lock (_gate)
            {
                if (!CanMutate()) return false;
                if (!_dirty) return true;
                try { SaveCore(); _dirty = false; return true; }
                catch { _faulted = true; _loaded = false; return false; }
            }
        }

        internal bool Reserve(long playerId, PendingGrant grant, double now)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || grant == null || !ValidGrant(grant) || !Finite(now) || now < 0d || grant.ExpiresAt <= now) return false;
                PlayerState p;
                if (!_players.TryGetValue(playerId, out p) || !CanAdmit(p, GoldCooldownPolicy.LegacyPatronId, grant.Cost)) return false;
                if (FindIdolToken(grant.GuidToken) != null || WasSettled(p, grant.GuidToken)) return false;
                foreach (var claim in p.Claims.Values) if (claim.Token == grant.GuidToken) return false;
                foreach (var result in p.Results.Values) if (result.Grant.Token == grant.GuidToken) return false;
                // An admitted request may already have produced an item. Never age it out.
                if (p.Pending != null) return false;
                p.Pending = grant.Copy();
                return PersistOrRollback();
            }
        }

        // Expiry is admission-only; after reservation, commit may arrive late and must settle.
        // Duplicate commits in the bounded receipt window succeed without charging again.
        internal bool Commit(long playerId, string token, double now)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidToken(token) || !Finite(now) || now < 0d) return false;
                PlayerState p;
                if (!_players.TryGetValue(playerId, out p) || !p.Unlocked) return false;
                if (WasSettled(p, token)) return true;
                if (p.Pending == null || !String.Equals(p.Pending.GuidToken, token, StringComparison.Ordinal)) return false;
                if (p.Favor < p.Pending.Cost) return false;
                p.Favor = Math.Max(0f, p.Favor - p.Pending.Cost);
                p.ExhaustionRemaining = GoldCooldownPolicy.RemainingAfterCommit(p.Pending.Cost, p.ExhaustionRemaining);
                if (p.Pending.Cost > 250f) p.ExhaustedPatron = GoldCooldownPolicy.LegacyPatronId;
                p.LastCommitted = token;
                p.SettledTokens.Add(token);
                if (p.SettledTokens.Count > MaxReceipts) p.SettledTokens.RemoveAt(0);
                p.Pending = null;
                return PersistOrRollback();
            }
        }

        internal bool Reject(long playerId, string token)
        {
            lock (_gate)
            {
                if (!CanMutate() || playerId == 0 || !ValidToken(token)) return false;
                PlayerState p;
                if (!_players.TryGetValue(playerId, out p) || p.Pending == null || !String.Equals(p.Pending.GuidToken, token, StringComparison.Ordinal)) return false;
                p.Pending = null;
                return PersistOrRollback();
            }
        }

        private bool CanMutate() { return _loaded && !_faulted && !_verifyingEvidence; }
        private bool Fault() { _players = new Dictionary<long, PlayerState>(); _loaded = false; _faulted = true; return false; }

        private PlayerState FindOrCreate(long id)
        {
            PlayerState p;
            if (_players.TryGetValue(id, out p)) return p;
            if (_players.Count >= MaxPlayers) throw new InvalidOperationException("Gold crafting ledger player limit reached.");
            p = new PlayerState { PlayerId = id, LastCommitted = string.Empty };
            _players.Add(id, p);
            return p;
        }

        private bool PersistOrRollback()
        {
            try { SaveCore(); _dirty = false; return true; }
            catch
            {
                _faulted = true;
                // The in-memory mutation is not authoritative if durable write failed.
                _loaded = false;
                return false;
            }
        }

        private void SaveCore()
        {
            string directory = Path.GetDirectoryName(_path);
            if (String.IsNullOrEmpty(directory)) throw new IOException("Ledger path has no directory.");
            Directory.CreateDirectory(directory);
            byte[] payload;
            using (var body = new MemoryStream())
            using (var writer = new BinaryWriter(body, Utf8))
            {
                writer.Write(_players.Count);
                foreach (var pair in _players)
                {
                    PlayerState p = pair.Value;
                    writer.Write(p.PlayerId); writer.Write(p.Unlocked); writer.Write(p.Favor); writer.Write(p.ExhaustionRemaining);
                    writer.Write(p.BackgroundWindowStartUtc); writer.Write(p.BackgroundFavorThisHour);
                    writer.Write(p.BackgroundStationSequences.Count);
                    foreach (var sequence in p.BackgroundStationSequences)
                    { WriteString(writer, sequence.Key, 64); writer.Write(sequence.Value.High); writer.Write(sequence.Value.Seen); }
                    WriteString(writer, p.LastCommitted ?? string.Empty, 32);
                    writer.Write(p.SettledTokens.Count);
                    foreach (string token in p.SettledTokens) WriteString(writer, token, 32);
                    writer.Write(p.Pending != null);
                    if (p.Pending != null) WritePending(writer, p.Pending);
                    WritePatrons(writer, p); WriteIdolPurchases(writer, p);
                }
                writer.Flush(); payload = body.ToArray();
            }
            if (payload.Length > MaxFileBytes - 16) throw new IOException("Gold crafting ledger size limit reached.");
            string temp = _path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic); writer.Write(Version); writer.Write(payload.Length); writer.Write(payload); writer.Write(Crc32(payload));
                writer.Flush(); stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak", true);
            else File.Move(temp, _path);
        }

        private static Dictionary<long, PlayerState> ReadPayload(byte[] payload, int version)
        {
            var result = new Dictionary<long, PlayerState>();
            using (var input = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(input, Utf8))
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxPlayers) return null;
                for (int i = 0; i < count; i++)
                {
                    var p = new PlayerState { PlayerId = reader.ReadInt64(), Unlocked = reader.ReadBoolean(), Favor = reader.ReadSingle(), ExhaustionRemaining = reader.ReadDouble() };
                    if (version >= 2)
                    {
                        p.BackgroundWindowStartUtc = reader.ReadDouble(); p.BackgroundFavorThisHour = reader.ReadSingle();
                        int stations = reader.ReadInt32();
                        if (stations < 0 || stations > MaxBackgroundStations) return null;
                        for (int j = 0; j < stations; j++)
                        {
                            string station = ReadString(reader, 64); long sequence = reader.ReadInt64();
                            ulong seen = version >= 3 ? reader.ReadUInt64() : ulong.MaxValue;
                            if (String.IsNullOrWhiteSpace(station) || sequence <= 0 || seen == 0 || p.BackgroundStationSequences.ContainsKey(station)) return null;
                            p.BackgroundStationSequences.Add(station, new BackgroundSequence { High = sequence, Seen = seen });
                        }
                    }
                    p.LastCommitted = ReadString(reader, 32);
                    if (p.PlayerId == 0 || !Finite(p.Favor) || p.Favor < 0f || p.Favor > 1000f || !Finite(p.ExhaustionRemaining) || p.ExhaustionRemaining < 0d || p.ExhaustionRemaining > CooldownSeconds || result.ContainsKey(p.PlayerId)) return null;
                    if (!Finite(p.BackgroundWindowStartUtc) || p.BackgroundWindowStartUtc < 0d || !Finite(p.BackgroundFavorThisHour) ||
                        p.BackgroundFavorThisHour < 0f || p.BackgroundFavorThisHour > MaxBackgroundFavorPerHour ||
                        (p.BackgroundFavorThisHour > 0 && p.BackgroundWindowStartUtc == 0d)) return null;
                    int receipts = reader.ReadInt32();
                    if (receipts < 0 || receipts > MaxReceipts) return null;
                    for (int j = 0; j < receipts; j++)
                    {
                        string receipt = ReadString(reader, 32);
                        if (!ValidToken(receipt) || p.SettledTokens.Contains(receipt)) return null;
                        p.SettledTokens.Add(receipt);
                    }
                    if (p.LastCommitted.Length != 0 && !ValidToken(p.LastCommitted)) return null;
                    if (reader.ReadBoolean())
                    {
                        p.Pending = ReadPending(reader);
                        if (!p.Unlocked || !ValidGrant(p.Pending) || p.Pending.Cost > p.Favor) return null;
                    }
                    p.ExhaustedPatron = p.ExhaustionRemaining > 0 ? GoldCooldownPolicy.LegacyPatronId : string.Empty;
                    if (version >= 4 && !ReadPatrons(reader, p)) return null;
                    if (version == 9 && !ReadIdolPurchases(reader, p)) return null;
                    result.Add(p.PlayerId, p);
                }
                if (input.Position != input.Length) return null;
            }
            return result;
        }

        private static bool ValidGrant(PendingGrant g)
        {
            return ValidToken(g.GuidToken) && (g.Kind == 1 || g.Kind == 2) && !String.IsNullOrWhiteSpace(g.Prefab) && g.Prefab.Length <= 128 &&
                g.OldQuality >= 0 && g.NewQuality >= 0 && g.Variant >= 0 && Finite(g.Cost) && g.Cost >= 0f && g.Cost <= 1000f &&
                (g.StationString == null || g.StationString.Length <= 1024) && Finite(g.ExpiresAt) && g.ExpiresAt >= 0d;
        }

        private static bool ValidToken(string value) { Guid parsed; return !String.IsNullOrEmpty(value) && Guid.TryParseExact(value, "N", out parsed); }
        private static bool WasSettled(PlayerState p, string token) { return String.Equals(p.LastCommitted, token, StringComparison.Ordinal) || p.SettledTokens.Contains(token); }
        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

        private static void WritePending(BinaryWriter w, PendingGrant p)
        {
            WriteString(w, p.GuidToken, 32); w.Write(p.Kind); WriteString(w, p.Prefab, 128); w.Write(p.OldQuality); w.Write(p.NewQuality); w.Write(p.Variant); w.Write(p.Cost);
            WriteString(w, p.StationString ?? string.Empty, 1024); w.Write(p.ExpiresAt);
        }

        private static PendingGrant ReadPending(BinaryReader r)
        {
            return new PendingGrant { GuidToken = ReadString(r, 32), Kind = r.ReadInt32(), Prefab = ReadString(r, 128), OldQuality = r.ReadInt32(), NewQuality = r.ReadInt32(), Variant = r.ReadInt32(), Cost = r.ReadSingle(), StationString = ReadString(r, 1024), ExpiresAt = r.ReadDouble() };
        }

        private static void WriteString(BinaryWriter writer, string value, int maxBytes)
        {
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > maxBytes) throw new InvalidDataException("Gold crafting ledger string limit exceeded.");
            writer.Write(bytes.Length); writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader, int maxBytes)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maxBytes) throw new InvalidDataException("Invalid ledger string length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return Utf8.GetString(bytes);
        }

        private static uint Crc32(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < bytes.Length; i++)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }
    }
}
