using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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
        private sealed class DestroyCertificate {internal string Output,Token,Type;internal long Creator;}
        private sealed class Job
        {
            internal readonly object Gate=new object();internal MasterIdolAdmissionStore Committed,Initial;internal readonly Dictionary<string,DestroyCertificate> Destroyed=new Dictionary<string,DestroyCertificate>(StringComparer.Ordinal);
            internal Task Work;internal int Version;internal bool Complete,Failed,LeaseReleased;
        }
        private static readonly object PathGate=new object();private static readonly HashSet<string> Writing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> Changes=new HashSet<string>(StringComparer.Ordinal);private string[] TokensCache;
        internal string[] Tokens(){if(TokensCache==null||TokensCache.Length!=_records.Count){TokensCache=new string[_records.Count];_records.Keys.CopyTo(TokensCache,0);}return TokensCache;}
        internal string[] TakeChanges(){var result=new string[Changes.Count];Changes.CopyTo(result);Changes.Clear();return result;}
        private Job Pending;private int Published;internal bool LoadDeferred {get;private set;}internal long Revision {get;private set;}
        internal bool Busy=>Pending!=null;
        internal bool Ready { get; private set; }
        internal bool Poll()
        {
            using var aggregate=new MasterIdolPerf.Scope("admission.poll");
            if(LoadDeferred){lock(PathGate){if(Writing.Contains(Path.GetFullPath(_path)))return false;}Load();Revision++;return true;}
            var job=Pending;if(job==null)return false;bool changed=false;lock(job.Gate){if(job.Version>Published&&job.Committed!=null){foreach(var pair in job.Committed._records)if(!_records.TryGetValue(pair.Key,out var prior)||prior.Applied!=pair.Value.Applied||prior.Closed!=pair.Value.Closed||prior.Dismantling!=pair.Value.Dismantling||prior.Removed!=pair.Value.Removed||prior.Output!=pair.Value.Output)Changes.Add(pair.Key);_records=job.Committed._records;Published=job.Version;Revision++;changed=true;}if(job.Complete){if(job.Failed)Ready=false;Pending=null;changed=true;}}
            return changed;
        }
        private Dictionary<string,MasterIdolAdmission> CopyRecords(){var result=new Dictionary<string,MasterIdolAdmission>(StringComparer.Ordinal);foreach(var pair in _records)result.Add(pair.Key,pair.Value.Copy());return result;}
        private bool Change(Func<Dictionary<string,MasterIdolAdmission>,bool> mutation)
        {
            Poll();if(!Ready||Pending!=null)return false;var records=CopyRecords();if(!mutation(records))return false;
            var snapshot=new MasterIdolAdmissionStore(_path,_world){_records=records,Ready=true};var job=new Job{Initial=snapshot};string path=Path.GetFullPath(_path);lock(PathGate){if(!Writing.Add(path)){LoadDeferred=true;return false;}}
            Pending=job;Published=0;
            try{job.Work=Task.Run(()=>{
                try{var next=snapshot;while(true){bool success=next.Write();lock(job.Gate){if(!success){job.Failed=true;job.Complete=true;break;}job.Committed=next;job.Version++;if(job.Destroyed.Count==0){lock(PathGate)Writing.Remove(path);job.LeaseReleased=true;job.Complete=true;break;}
                    var following=new MasterIdolAdmissionStore(_path,_world){_records=next.CopyRecords(),Ready=true};foreach(var certificate in job.Destroyed.Values)ApplyDestroy(following._records,certificate);job.Destroyed.Clear();next=following;
                }}}
                catch{lock(job.Gate){job.Failed=true;job.Complete=true;}}
                finally{lock(job.Gate){if(!job.LeaseReleased){lock(PathGate)Writing.Remove(path);job.LeaseReleased=true;}job.Complete=true;}}
            });return false;}catch{lock(PathGate)Writing.Remove(path);Pending=null;Ready=false;return false;}
        }
        private static bool ApplyDestroy(Dictionary<string,MasterIdolAdmission> records,DestroyCertificate c)
        {if(!records.TryGetValue(c.Token,out var entry)||!entry.Applied||entry.Closed||entry.Output!=c.Output||entry.Type!=c.Type||entry.Player!=c.Creator)return false;entry.Closed=true;entry.Removed=entry.Dismantling;return true;}
        internal bool Capacity => Ready && (_records.Count+(Pending?.Initial._records.Count>_records.Count?1:0))<4096;
        internal MasterIdolAdmissionStore(string path, long world) { _path = path; _world = world; }
        internal IEnumerable<MasterIdolAdmission> Records { get { foreach (var value in _records.Values) yield return value.Copy(); } }
        internal MasterIdolAdmission Find(string token) => token != null && _records.TryGetValue(token, out var value) ? value.Copy() : null;
        internal bool Held(string type)
        { foreach (var value in _records.Values) if (!value.Closed && value.Type == type) return true; return false; }
        internal bool Outstanding(long player)
        {foreach(var e in _records.Values)if(e.Player==player&&e.Issued&&!e.Applied&&!e.Closed)return true;var pending=Pending?.Initial;if(pending!=null)foreach(var e in pending._records.Values)if(e.Player==player&&e.Issued&&!e.Applied&&!e.Closed)return true;return false;}
        internal bool Load()
        {
            Ready = false;LoadDeferred=false;
            lock(PathGate){if(Writing.Contains(Path.GetFullPath(_path))){LoadDeferred=true;return false;}}
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
        {if(!Ready||!Valid(entry)||entry.Closed||entry.Issued||entry.Applied||entry.Output.Length!=0 )return false;if(_records.TryGetValue(entry.Token,out var old))return SameCreation(old,entry)&&!old.Issued&&!old.Closed;if(_records.Count>=4096)return false;return Change(records=>{records.Add(entry.Token,entry.Copy());return true;});}
        private static bool SameCreation(MasterIdolAdmission a,MasterIdolAdmission b)=>a.Token==b.Token&&a.Type==b.Type&&a.Player==b.Player&&a.X==b.X&&a.Y==b.Y&&a.Z==b.Z&&a.RequiresFavor==b.RequiresFavor;
        internal bool ReserveIssued(MasterIdolAdmission entry)
        {if(!Ready||!Valid(entry)||entry.Closed||entry.Issued||entry.Applied||entry.Output.Length!=0 )return false;if(_records.TryGetValue(entry.Token,out var old))return SameCreation(old,entry)&&old.Issued&&!old.Closed&&!old.Applied;if(_records.Count>=4096)return false;return Change(records=>{var issued=entry.Copy();issued.Issued=true;records.Add(entry.Token,issued);return true;});}
        internal bool Apply(string token,long player,string output)
        {if(!Ready||string.IsNullOrEmpty(output)||output.Length>80||!_records.TryGetValue(token,out var entry)||entry.Player!=player||entry.Closed||!entry.Issued)return false;if(entry.Applied)return entry.Output==output;return Change(records=>{records[token].Output=output;records[token].Applied=true;return true;});}
        internal bool CancelUnstarted(string token,long player)
        {if(!Ready||!_records.TryGetValue(token,out var entry)||entry.Player!=player||entry.Applied||entry.Issued)return false;if(entry.Closed)return true;return Change(records=>{records[token].Closed=true;return true;});}
        internal bool Issue(string token)
        {if(!Ready||!_records.TryGetValue(token,out var entry)||entry.Closed||entry.Applied)return false;if(entry.Issued)return true;return Change(records=>{records[token].Issued=true;return true;});}
        // Same live-session revocation proof remains the caller's responsibility. Queued does not mean success.
        internal bool RevokeIssued(string token,long player)
        {if(!Ready||!_records.TryGetValue(token,out var entry)||entry.Player!=player||entry.Applied||!entry.Issued)return false;if(entry.Closed)return true;return Change(records=>{records[token].Closed=true;return true;});}
        internal bool BeginDismantle(string token,long player,string output)
        {if(!Ready||!_records.TryGetValue(token,out var entry)||entry.Player!=player||entry.Output!=output||!entry.Applied||entry.Closed)return false;if(entry.Dismantling)return true;return Change(records=>{records[token].Dismantling=true;return true;});}
        // One-shot native destruction certificate is frozen while writer is busy. No refund visibility before commit.
        internal bool Destroyed(string output,string token,string type,long creator)
        {
            Poll();if(!Ready||string.IsNullOrEmpty(output)||!_records.TryGetValue(token??"",out var entry)||!entry.Applied||entry.Closed||entry.Output!=output||entry.Type!=type||entry.Player!=creator)return false;
            var certificate=new DestroyCertificate{Output=output,Token=token,Type=type,Creator=creator};var job=Pending;
            if(job!=null){lock(job.Gate){if(!job.Complete){job.Destroyed[token]=certificate;return true;}}Poll();}
            Change(records=>ApplyDestroy(records,certificate));return Busy;
        }
        
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
