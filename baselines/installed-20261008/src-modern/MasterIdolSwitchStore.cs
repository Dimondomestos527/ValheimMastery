using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ValheimMastery
{
    // Six current type slots only. Replacement output starts off; old output cannot transfer its state.
    internal sealed class MasterIdolSwitchStore
    {
        private readonly string _path;
        private readonly long _world;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private Task<MasterIdolSwitchStore> _pending;
        internal bool LoadDeferred { get; private set; }
        internal bool LegacyPending {get;private set;}
        private static readonly object PathLock = new object();
        private static readonly HashSet<string> Writing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string Lease => Path.GetFullPath(_path);
        internal bool BeginSet(string type, string output, string token, int expected, bool desired)
        {
            if (!Ready || LegacyPending || _pending != null || MasterIdolProfiles.Find(type) == null || string.IsNullOrEmpty(output) || output.Length > 80 || !MasterIdolNetworkQuota.ValidUid(output) || !Guid.TryParseExact(token,"N",out _) ||
                !MasterIdolToggleRules.CanChange(State(type, output,token), expected, desired)) return false;
            if((_entries.TryGetValue(output,out var existing)&&existing.Type!=type)||(!_entries.ContainsKey(output)&&_entries.Count>=4096))return false;
            var snapshot = new MasterIdolSwitchStore(_path, _world) { Ready = true };
            foreach (var pair in _entries) snapshot._entries.Add(pair.Key, new Entry { Type=pair.Value.Type, Token=pair.Value.Token, Legacy=pair.Value.Legacy, Output = pair.Value.Output, State = pair.Value.State });
            string path = Lease;
            lock (PathLock) { if (!Writing.Add(path)) return false; }
            try
            {
                _pending = Task.Run(() =>
                {
                    try { snapshot.Set(type, output, token, expected, desired); return snapshot; }
                    finally { lock (PathLock) Writing.Remove(path); }
                });
                return true;
            }
            catch { lock (PathLock) Writing.Remove(path); Ready = false; return false; }
        }
        // Main-thread publication only: no worker touches live entries or Unity state.
        internal bool Poll()
        {
            if (LoadDeferred)
            {
                lock (PathLock) { if (Writing.Contains(Lease)) return false; }
                Load(); return true; // retry only a known path lease, never an unknown write/corruption
            }
            if (_pending == null || !_pending.IsCompleted) return false;
            var finished = _pending; _pending = null;
            if (finished.IsFaulted || finished.IsCanceled || !finished.Result.Ready) { Ready = false; return true; }
            _entries.Clear();
            foreach (var pair in finished.Result._entries) _entries.Add(pair.Key, pair.Value);
            return true;
        }
        private sealed class Entry { internal string Type, Output, Token;internal bool Legacy; internal int State; }
        internal bool Ready { get; private set; }
        internal MasterIdolSwitchStore(string path, long world) { _path = path; _world = world; }
        internal int State(string type, string output,string token) => Ready && _entries.TryGetValue(output, out var e) && e.Type == type && e.Output == output && e.Token==token && !string.IsNullOrEmpty(token) ? e.State : 0;
        internal void BindLegacy(IEnumerable<MasterIdolAdmission> admissions)
        {
            if(!Ready || admissions==null)return;
            var matches=new Dictionary<string,MasterIdolAdmission>(StringComparer.Ordinal);var conflicts=new HashSet<string>(StringComparer.Ordinal);
            foreach(var e in admissions)
            {
                if(!e.Applied||string.IsNullOrEmpty(e.Output))continue;
                if(matches.ContainsKey(e.Output))conflicts.Add(e.Output);else matches[e.Output]=e;
            }
            foreach(var pair in _entries)
            {
                var e=pair.Value;if(!e.Legacy)continue;e.Legacy=false;
                if(!conflicts.Contains(pair.Key)&&matches.TryGetValue(pair.Key,out var record)&&record.Type==e.Type)e.Token=record.Token;
            }
            LegacyPending=false;
        }
        internal bool Load()
        {
            Ready = false; _entries.Clear(); LoadDeferred = false;LegacyPending=false;
            try
            {
                lock (PathLock) { if (Writing.Contains(Lease)) { LoadDeferred = true; return false; } }
                if (!File.Exists(_path)) { Ready = !File.Exists(_path + ".tmp") && !File.Exists(_path + ".bak"); return Ready; }
                if (new FileInfo(_path).Length > 2*1024*1024) return false;
                using var stream = File.OpenRead(_path); using var reader = new BinaryReader(stream, Encoding.UTF8);
                if(reader.ReadInt32()!=0x564D5331)return false;int version=reader.ReadInt32();
                if((version!=1 && version!=2)||reader.ReadInt64()!=_world)return false;
                int size = reader.ReadInt32(); if (size < 4 || size > 2*1024*1024-52 || stream.Length - stream.Position != size + 32) return false;
                byte[] payload = reader.ReadBytes(size), hash = reader.ReadBytes(32);
                using var sha = SHA256.Create(); byte[] computed = sha.ComputeHash(payload);
                for (int i = 0; i < 32; i++) if (hash[i] != computed[i]) return false;
                using var body = new MemoryStream(payload); using var data = new BinaryReader(body, Encoding.UTF8);
                int count = data.ReadInt32(); if (count < 0 || count > (version==1?MasterIdolProfiles.All.Count:4096)) return false;
                var oldTypes=new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string type = data.ReadString(), output = data.ReadString(), token=version==2?data.ReadString():""; int state = data.ReadInt32();
                    if (MasterIdolProfiles.Find(type) == null || string.IsNullOrEmpty(output) || output.Length > 80 || !MasterIdolNetworkQuota.ValidUid(output) || (token.Length!=0&&!Guid.TryParseExact(token,"N",out _)) || state < 0 || _entries.ContainsKey(output) || (version==1&&!oldTypes.Add(type))) return false;
                    _entries.Add(output, new Entry { Type=type, Token=token,Legacy=version==1, Output = output, State = state });
                }
                if (body.Position != body.Length) return false;
                LegacyPending=version==1;Ready = true; return true;
            }
            catch { return false; }
        }
        internal bool Set(string type, string output, string token, int expected, bool desired)
        {
            if (!Ready || MasterIdolProfiles.Find(type) == null || string.IsNullOrEmpty(output) || output.Length > 80 || !MasterIdolNetworkQuota.ValidUid(output) || !Guid.TryParseExact(token,"N",out _) ||
                !MasterIdolToggleRules.CanChange(State(type, output,token), expected, desired)) return false;
            if((_entries.TryGetValue(output,out var existing)&&existing.Type!=type)||(!_entries.ContainsKey(output)&&_entries.Count>=4096))return false;
            _entries[output] = new Entry { Type=type, Token=token, Output = output, State = expected + 1 };
            try
            {
                using var body = new MemoryStream();
                using (var data = new BinaryWriter(body, Encoding.UTF8, true))
                {
                    data.Write(_entries.Count);
                    foreach (var e in _entries) { data.Write(e.Value.Type); data.Write(e.Value.Output); data.Write(e.Value.Token??""); data.Write(e.Value.State); }
                }
                byte[] payload = body.ToArray(); if(payload.Length>2*1024*1024-52){Ready=false;return false;} using var sha = SHA256.Create();
                string folder = Path.GetDirectoryName(_path); if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                using (var file = new FileStream(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using var writer = new BinaryWriter(file, Encoding.UTF8, true);
                    writer.Write(0x564D5331); writer.Write(2); writer.Write(_world); writer.Write(payload.Length);
                    writer.Write(payload); writer.Write(sha.ComputeHash(payload)); writer.Flush(); file.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(_path + ".tmp", _path, _path + ".bak"); else File.Move(_path + ".tmp", _path);
                return true;
            }
            catch { Ready = false; return false; }
        }
    }
}
