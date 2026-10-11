using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ValheimMastery
{
    // Separate store: never rewrites old Gold v9 bytes or total Favor.
    // Root-approved same-host character clocks; world-bound migration origins fence old tokens.
    internal sealed class Crafting100PermitLedger
    {
        private const int Magic = 0x43315031, Version = 2, MaximumBytes = 8 * 1024 * 1024;
        private const int MaximumPlayers = 50000, MaximumResults = 4096;
        private readonly string PathName;
        private Dictionary<long, State> Players = new Dictionary<long, State>();
        private bool Available, Dirty;
        internal bool IsAvailable => Available;
        internal sealed class Permit
        {
            internal string Token, Encoded;
            internal long World;
            internal int Kind;
            internal double Duration;
            internal int OriginPhase;
            internal string OriginRaw = "";
            internal bool Admitted = true;
            internal Permit Copy() => (Permit)MemberwiseClone();
        }
        internal sealed class Terminal
        {
            internal string Encoded;
            internal long World;
            internal int Phase;
            internal int OriginPhase;
            internal string OriginRaw = "";
            internal Terminal Copy() => (Terminal)MemberwiseClone();
        }
        internal sealed class State
        {
            internal double Craft, Forge;
            internal long Generation;
            internal Permit Pending;
            internal Dictionary<string, Terminal> Results = new Dictionary<string, Terminal>(StringComparer.Ordinal);
            internal State Copy()
            {
                var copy = (State)MemberwiseClone(); copy.Pending = Pending?.Copy();
                copy.Results = new Dictionary<string, Terminal>(StringComparer.Ordinal);
                foreach (var pair in Results) copy.Results.Add(pair.Key, pair.Value.Copy());
                return copy;
            }
        }
        internal Crafting100PermitLedger(string path) { PathName = Path.GetFullPath(path); }
        internal State Get(long id) => Available && id != 0 ? (Players.TryGetValue(id, out var p) ? p.Copy() : new State()) : null;
        internal bool Load()
        {
            Available = false; Dirty = false; Players = new Dictionary<long, State>();
            try
            {
                if (!File.Exists(PathName))
                {
                    if (File.Exists(PathName + ".tmp") || File.Exists(PathName + ".bak")) return false;
                    Available = true; return true;
                }
                byte[] bytes = File.ReadAllBytes(PathName);
                if (bytes.Length < 44 || bytes.Length > MaximumBytes) return false;
                using (var stream = new MemoryStream(bytes, false))
                using (var r = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (r.ReadInt32() != Magic) return false;
                    int version = r.ReadInt32(); if (version < 1 || version > Version) return false;
                    int size = r.ReadInt32();
                    if (size < 4 || size > MaximumBytes - 44 || stream.Length - stream.Position != size + 32L) return false;
                    byte[] payload = r.ReadBytes(size), hash = r.ReadBytes(32);
                    using (var sha = SHA256.Create())
                    { byte[] expected = sha.ComputeHash(payload); for (int i = 0; i < 32; i++) if (hash[i] != expected[i]) return false; }
                    using (var body = new MemoryStream(payload, false))
                    using (var read = new BinaryReader(body, new UTF8Encoding(false, true)))
                    {
                        int count = read.ReadInt32(); if (count < 0 || count > MaximumPlayers) return false;
                        for (int i = 0; i < count; i++)
                        {
                            long id = read.ReadInt64(); var p = new State { Craft = read.ReadDouble(), Forge = read.ReadDouble(), Generation = read.ReadInt64() };
                            if (id == 0 || Players.ContainsKey(id) || !Remaining(p.Craft) || !Remaining(p.Forge) || p.Generation < 0) return false;
                            if (read.ReadBoolean())
                            {
                                p.Pending = new Permit { Token = ReadText(read, 32), Encoded = ReadText(read, 1024), World = read.ReadInt64(), Kind = read.ReadInt32(), Duration = read.ReadDouble() };
                                if (version >= 2) { p.Pending.OriginPhase = read.ReadInt32(); p.Pending.OriginRaw = ReadText(read, 1030); p.Pending.Admitted = read.ReadBoolean(); }
                                if (!Valid(p.Pending) || p.Pending.Admitted && (p.Pending.Kind == 1 ? p.Craft : p.Forge) != 0) return false;
                            }
                            int results = read.ReadInt32(); if (results < 0 || results > MaximumResults) return false;
                            for (int j = 0; j < results; j++)
                            {
                                string token = ReadText(read, 32); var terminal = new Terminal { Encoded = ReadText(read, 1024), World = read.ReadInt64(), Phase = read.ReadInt32() };
                                if (version >= 2) { terminal.OriginPhase = read.ReadInt32(); terminal.OriginRaw = ReadText(read, 1030); }
                                if (!Token(token) || terminal.Encoded.Length == 0 || (terminal.Phase != 2 && terminal.Phase != 3) || p.Results.ContainsKey(token) || !ValidOrigin(terminal.OriginPhase, terminal.OriginRaw, terminal.Encoded)) return false;
                                p.Results.Add(token, terminal);
                            }
                            if (p.Pending != null && p.Results.ContainsKey(p.Pending.Token)) return false;
                            Players.Add(id, p);
                        }
                        if (body.Position != body.Length) return false;
                    }
                }
                Available = true; return true;
            }
            catch { return false; }
        }
        internal bool Admit(long id, Permit permit)
        {
            if (!Available || id == 0 || !Valid(permit) || permit.OriginPhase != 0) return false;
            State p = Get(id);
            if (p.Results.ContainsKey(permit.Token)) return false;
            if (p.Pending != null) return Same(p.Pending, permit);
            if (p.Results.Count >= MaximumResults || !Crafting100CooldownRules.CanStart((Crafting100CooldownChannel)permit.Kind, p.Craft, p.Forge, false)) return false;
            if (!Players.ContainsKey(id) && Players.Count >= MaximumPlayers || p.Generation == long.MaxValue) return false;
            p.Pending = permit.Copy(); p.Generation++; Players[id] = p; Dirty = true;
            return Flush();
        }
        internal bool PrepareMigration(long id, Permit permit)
        {
            if (!Available || id == 0 || !Valid(permit) || permit.OriginPhase == 0 || permit.Admitted) return false;
            var p = Get(id);
            if (p.Results.ContainsKey(permit.Token)) return false;
            if (p.Pending != null) return Same(p.Pending, permit);
            if (p.Results.Count >= MaximumResults || !Players.ContainsKey(id) && Players.Count >= MaximumPlayers || p.Generation == long.MaxValue) return false;
            // Preparation is a durable replay/rollback link, not permission to mutate an item.
            p.Pending = permit.Copy(); p.Generation++; Players[id] = p; Dirty = true; return Flush();
        }
        internal bool ActivateMigration(long id, string token, string encoded, long world)
        {
            if (!Available || !Players.TryGetValue(id, out var old) || old.Pending == null || old.Pending.Token != token || old.Pending.Encoded != encoded || old.Pending.World != world || old.Pending.OriginPhase != 1) return false;
            if (old.Pending.Admitted) return true;
            if (!Crafting100CooldownRules.CanStart((Crafting100CooldownChannel)old.Pending.Kind, old.Craft, old.Forge, false) || old.Generation == long.MaxValue) return false;
            var p = old.Copy(); p.Pending.Admitted = true; p.Generation++; Players[id] = p; Dirty = true; return Flush();
        }
        internal bool Settle(long id, string token, string encoded, long world, int phase)
        {
            if (!Available || (phase != 2 && phase != 3) || !Players.TryGetValue(id, out var old)) return false;
            if (old.Results.TryGetValue(token, out var terminal)) return terminal.World == world && terminal.Encoded == encoded && terminal.Phase == phase;
            if (old.Pending == null || old.Pending.Token != token || old.Pending.Encoded != encoded || old.Pending.World != world || old.Generation == long.MaxValue) return false;
            var p = old.Copy();
            if (phase == 2 && !p.Pending.Admitted && p.Pending.OriginPhase != 2) return false;
            if (phase == 2) { if (p.Pending.Kind == 1) p.Craft = Math.Max(p.Craft, p.Pending.Duration); else p.Forge = Math.Max(p.Forge, p.Pending.Duration); }
            p.Results.Add(token, new Terminal { Encoded = encoded, World = world, Phase = phase, OriginPhase = p.Pending.OriginPhase, OriginRaw = p.Pending.OriginRaw }); p.Pending = null; p.Generation++;
            Players[id] = p; Dirty = true; return Flush();
        }
        internal bool RejectUnadmitted(long id, string token, string encoded, long world)
        {
            if (!Available || id == 0 || !Token(token) || string.IsNullOrEmpty(encoded) || Encoding.UTF8.GetByteCount(encoded) > 1024) return false;
            var p = Get(id);
            if (p.Results.TryGetValue(token, out var terminal)) return terminal.Phase == 3 && terminal.World == world && terminal.Encoded == encoded;
            if (p.Pending != null || p.Results.Count >= MaximumResults || p.Generation == long.MaxValue || !Players.ContainsKey(id) && Players.Count >= MaximumPlayers) return false;
            // Fence an in-flight station callback before acknowledging cancellation.
            p.Results.Add(token, new Terminal { Encoded = encoded, World = world, Phase = 3 }); p.Generation++; Players[id] = p; Dirty = true;
            return Flush();
        }
        internal bool RecordMigrationRejected(long id, string token, string encoded, long world, int originPhase, string originRaw)
        {
            if (!Available || id == 0 || !Token(token) || string.IsNullOrEmpty(encoded) || Encoding.UTF8.GetByteCount(encoded) > 1024 || originPhase == 0 || !ValidOrigin(originPhase, originRaw, encoded)) return false;
            var p = Get(id);
            if (p.Results.TryGetValue(token, out var terminal)) return terminal.Phase == 3 && terminal.World == world && terminal.Encoded == encoded && terminal.OriginPhase == originPhase && terminal.OriginRaw == originRaw;
            if (p.Pending != null) return p.Pending.Token == token && p.Pending.Encoded == encoded && p.Pending.World == world && p.Pending.OriginPhase == originPhase && p.Pending.OriginRaw == originRaw && Settle(id, token, encoded, world, 3);
            if (p.Results.Count >= MaximumResults || !Players.ContainsKey(id) && Players.Count >= MaximumPlayers || p.Generation == long.MaxValue) return false;
            p.Results.Add(token, new Terminal { Encoded = encoded, World = world, Phase = 3, OriginPhase = originPhase, OriginRaw = originRaw }); p.Generation++; Players[id] = p; Dirty = true; return Flush();
        }
        internal bool Elapse(long id, double seconds)
        {
            if (!Available || !Finite(seconds) || seconds <= 0 || !Players.TryGetValue(id, out var p)) return false;
            double craft = Crafting100CooldownRules.RemainingAfterElapsed(p.Craft, seconds), forge = Crafting100CooldownRules.RemainingAfterElapsed(p.Forge, seconds);
            if (craft == p.Craft && forge == p.Forge) return true;
            if (p.Generation == long.MaxValue) { Available = false; return false; }
            p.Craft = craft; p.Forge = forge; p.Generation++; Dirty = true; return true;
        }
        internal bool Flush()
        {
            if (!Available) return false; if (!Dirty) return true;
            try
            {
                byte[] payload;
                using (var body = new MemoryStream())
                using (var w = new BinaryWriter(body, new UTF8Encoding(false, true)))
                {
                    w.Write(Players.Count);
                    foreach (var pair in Players)
                    {
                        w.Write(pair.Key); State p = pair.Value; w.Write(p.Craft); w.Write(p.Forge); w.Write(p.Generation); w.Write(p.Pending != null);
                        if (p.Pending != null)
                        { WriteText(w, p.Pending.Token, 32); WriteText(w, p.Pending.Encoded, 1024); w.Write(p.Pending.World); w.Write(p.Pending.Kind); w.Write(p.Pending.Duration); w.Write(p.Pending.OriginPhase); WriteText(w, p.Pending.OriginRaw, 1030); w.Write(p.Pending.Admitted); }
                        w.Write(p.Results.Count);
                        foreach (var result in p.Results)
                        { WriteText(w, result.Key, 32); WriteText(w, result.Value.Encoded, 1024); w.Write(result.Value.World); w.Write(result.Value.Phase); w.Write(result.Value.OriginPhase); WriteText(w, result.Value.OriginRaw, 1030); }
                    }
                    w.Flush(); payload = body.ToArray();
                }
                if (payload.Length > MaximumBytes - 44) throw new IOException("Crafting100 store limit");
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)); string temp = PathName + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var w = new BinaryWriter(stream, Encoding.UTF8))
                using (var sha = SHA256.Create())
                { w.Write(Magic); w.Write(Version); w.Write(payload.Length); w.Write(payload); w.Write(sha.ComputeHash(payload)); w.Flush(); stream.Flush(true); }
                if (File.Exists(PathName)) File.Replace(temp, PathName, PathName + ".bak", true); else File.Move(temp, PathName);
                Dirty = false; return true;
            }
            catch { Available = false; return false; } // Neither memory-only permit nor memory-only ACK is authoritative.
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Remaining(double value) => Crafting100CooldownRules.ValidRemaining(value);
        private static bool Token(string value) => Guid.TryParseExact(value, "N", out _);
        private static bool Valid(Permit p) => p != null && Token(p.Token) && !string.IsNullOrEmpty(p.Encoded) && Encoding.UTF8.GetByteCount(p.Encoded) <= 1024 &&
            (p.Kind == 1 || p.Kind == 2) && Finite(p.Duration) && p.Duration > 0 && p.Duration <= Crafting100CooldownRules.MaximumSeconds && ValidOrigin(p.OriginPhase, p.OriginRaw, p.Encoded) && (p.OriginPhase != 0 || p.Admitted);
        private static bool ValidOrigin(int phase, string raw, string encoded) => phase == 0 ? raw == "" : phase >= 1 && phase <= 3 && raw == phase + ":" + encoded;
        private static bool Same(Permit a, Permit b) => a.Token == b.Token && a.Encoded == b.Encoded && a.World == b.World && a.Kind == b.Kind && a.Duration == b.Duration && a.OriginPhase == b.OriginPhase && a.OriginRaw == b.OriginRaw && a.Admitted == b.Admitted;
        private static string ReadText(BinaryReader r, int maximum)
        { int size = r.ReadInt32(); if (size < 0 || size > maximum) throw new IOException("Crafting100 string limit"); byte[] b = r.ReadBytes(size); if (b.Length != size) throw new EndOfStreamException(); return new UTF8Encoding(false, true).GetString(b); }
        private static void WriteText(BinaryWriter w, string value, int maximum)
        { byte[] b = new UTF8Encoding(false, true).GetBytes(value); if (b.Length > maximum) throw new IOException("Crafting100 string limit"); w.Write(b.Length); w.Write(b); }
    }
}
