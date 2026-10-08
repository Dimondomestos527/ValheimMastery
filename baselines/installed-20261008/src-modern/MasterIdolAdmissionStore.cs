using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ValheimMastery
{
    internal sealed class MasterIdolAdmission
    {
        internal string Token, Type, Output = "";
        internal long Player;
        internal float X, Y, Z;
        internal bool Issued, Applied, Closed, Dismantling, Removed, RequiresFavor;
        internal MasterIdolAdmission Copy() => (MasterIdolAdmission)MemberwiseClone();
    }
    // Independent world-piece admission journal, not a Favor wallet. Unknown outcomes retain their slot.
    internal sealed class MasterIdolAdmissionStore
    {
        private readonly string _path;
        private readonly long _world;
        private Dictionary<string, MasterIdolAdmission> _records = new Dictionary<string, MasterIdolAdmission>(StringComparer.Ordinal);
        internal bool Ready { get; private set; }
        internal bool Capacity => Ready && _records.Count<4096;
        internal MasterIdolAdmissionStore(string path, long world) { _path = path; _world = world; }
        internal IEnumerable<MasterIdolAdmission> Records { get { foreach (var value in _records.Values) yield return value.Copy(); } }
        internal MasterIdolAdmission Find(string token) => token != null && _records.TryGetValue(token, out var value) ? value.Copy() : null;
        internal bool Held(string type)
        { foreach (var value in _records.Values) if (!value.Closed && value.Type == type) return true; return false; }
        internal bool Outstanding(long player)
        {foreach(var e in _records.Values)if(e.Player==player&&e.Issued&&!e.Applied&&!e.Closed)return true;return false;}
        internal bool Load()
        {
            Ready = false;
            try
            {
                if (!File.Exists(_path))
                { Ready = !File.Exists(_path + ".tmp") && !File.Exists(_path + ".bak"); return Ready; }
                var info = new FileInfo(_path); if (info.Length < 48 || info.Length > 2 * 1024 * 1024) return false;
                byte[] bytes = File.ReadAllBytes(_path);
                using var input = new MemoryStream(bytes, false);
                using var reader = new BinaryReader(input, Encoding.UTF8);
                if (reader.ReadInt32() != 0x564D4931) return false;
                int version=reader.ReadInt32();if(version<1||version>2||reader.ReadInt64()!=_world)return false;
                int size = reader.ReadInt32(); if (size < 4 || size != bytes.Length - 52) return false;
                byte[] payload = reader.ReadBytes(size), expected = reader.ReadBytes(32);
                using var sha = SHA256.Create(); byte[] actual = sha.ComputeHash(payload);
                for (int i = 0; i < 32; i++) if (actual[i] != expected[i]) return false;
                using var body = new MemoryStream(payload, false);
                using var data = new BinaryReader(body, Encoding.UTF8);
                int count = data.ReadInt32(); if (count < 0 || count > 4096) return false;
                var parsed = new Dictionary<string, MasterIdolAdmission>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    var entry = new MasterIdolAdmission { Token = data.ReadString(), Type = data.ReadString(), Player = data.ReadInt64(),
                        X = data.ReadSingle(), Y = data.ReadSingle(), Z = data.ReadSingle(), Output = data.ReadString(),
                        Issued = data.ReadBoolean(), Applied = data.ReadBoolean(), Closed = data.ReadBoolean() };
                    if(version>=2){entry.Dismantling=data.ReadBoolean();entry.Removed=data.ReadBoolean();entry.RequiresFavor=data.ReadBoolean();}
                    if (!Valid(entry) || parsed.ContainsKey(entry.Token) || entry.Dismantling&&!entry.Applied || entry.Removed&&(!entry.Dismantling||!entry.Closed)) return false;
                    parsed.Add(entry.Token, entry);
                }
                if (body.Position != body.Length) return false;
                _records = parsed; Ready = true; return true;
            }
            catch { return false; }
        }
        internal bool Reserve(MasterIdolAdmission entry)
        {
            if (!Ready || !Valid(entry) || entry.Closed || entry.Issued || entry.Applied || entry.Output.Length != 0 ||
                _records.Count >= 4096 || _records.ContainsKey(entry.Token)) return false;
            _records.Add(entry.Token, entry.Copy()); return Save();
        }
        // Same durable pre-grant contract, one flush instead of Reserve followed by Issue.
        internal bool ReserveIssued(MasterIdolAdmission entry)
        {
            if (!Ready || !Valid(entry) || entry.Closed || entry.Issued || entry.Applied || entry.Output.Length != 0 ||
                _records.Count >= 4096 || _records.ContainsKey(entry.Token)) return false;
            var issued = entry.Copy(); issued.Issued = true; _records.Add(issued.Token, issued); return Save();
        }
        internal bool Apply(string token, long player, string output)
        {
            if (!Ready || string.IsNullOrEmpty(output) || output.Length > 80 || !_records.TryGetValue(token, out var entry) ||
                entry.Player != player || entry.Closed || !entry.Issued) return false;
            if (entry.Applied) return entry.Output == output;
            entry.Output = output; entry.Applied = true; return Save();
        }
        internal bool CancelUnstarted(string token, long player)
        {
            if (!Ready || !_records.TryGetValue(token, out var entry) || entry.Player != player || entry.Applied || entry.Issued) return false;
            if (entry.Closed) return true;
            entry.Closed = true; return Save();
        }
        internal bool Issue(string token)
        {
            if (!Ready || !_records.TryGetValue(token, out var entry) || entry.Closed || entry.Applied) return false;
            if (entry.Issued) return true;
            entry.Issued = true; return Save();
        }
        // Caller must verify original live connection + character + world + nonce certificate.
        // Never call from a loaded Requested/Cancelled journal or an absent-world-object inference.
        internal bool RevokeIssued(string token, long player)
        {
            if (!Ready || !_records.TryGetValue(token, out var entry) || entry.Player != player || entry.Applied || !entry.Issued) return false;
            if (entry.Closed) return true;
            entry.Closed = true; return Save();
        }
        internal bool BeginDismantle(string token,long player,string output)
        {
            if(!Ready||!_records.TryGetValue(token,out var entry)||entry.Player!=player||entry.Output!=output||!entry.Applied||entry.Closed)return false;
            if(entry.Dismantling)return true;
            entry.Dismantling=true;return Save();
        }
        // Call only on an authoritative destroy observation, never on unload or a missing scan result.
        internal bool Destroyed(string output,string token,string type,long creator)
        {
            if (!Ready || string.IsNullOrEmpty(output) || !_records.TryGetValue(token??"",out var entry) ||
                !entry.Applied || entry.Closed || entry.Output!=output || entry.Type!=type || entry.Player!=creator)return false;
            entry.Closed=true;entry.Removed=entry.Dismantling;return Save();
        }
        private bool Save() => MasterIdolTiming.Measure("admission.save", Write);
        private bool Write()
        {
            try
            {
                using var body = new MemoryStream();
                using (var data = new BinaryWriter(body, Encoding.UTF8, true))
                {
                    data.Write(_records.Count);
                    foreach (var entry in _records.Values)
                    {
                        data.Write(entry.Token); data.Write(entry.Type); data.Write(entry.Player);
                        data.Write(entry.X); data.Write(entry.Y); data.Write(entry.Z); data.Write(entry.Output);
                        data.Write(entry.Issued); data.Write(entry.Applied); data.Write(entry.Closed);data.Write(entry.Dismantling);data.Write(entry.Removed);data.Write(entry.RequiresFavor);
                    }
                }
                byte[] payload = body.ToArray(); using var sha = SHA256.Create();
                string directory = Path.GetDirectoryName(_path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using (var file = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using var writer = new BinaryWriter(file, Encoding.UTF8, true);
                    writer.Write(0x564D4931); writer.Write(2); writer.Write(_world); writer.Write(payload.Length);
                    writer.Write(payload); writer.Write(sha.ComputeHash(payload)); writer.Flush(); file.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, _path + ".bak");
                else File.Move(_path + ".tmp", _path);
                return true;
            }
            catch { Ready = false; return false; }
        }
        private static bool Valid(MasterIdolAdmission entry) => entry != null && Guid.TryParseExact(entry.Token, "N", out _) &&
            MasterIdolProfiles.Find(entry.Type) != null && entry.Player != 0 && entry.Output != null && entry.Output.Length <= 80 &&
            (!entry.Applied || entry.Issued && entry.Output.Length != 0) && (!entry.Dismantling||entry.Applied) &&
            (!entry.Removed||entry.Dismantling&&entry.Closed) && Finite(entry.X) && Finite(entry.Y) && Finite(entry.Z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
