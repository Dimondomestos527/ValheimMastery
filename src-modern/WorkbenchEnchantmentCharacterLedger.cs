using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace ValheimMastery
{
    // Independent character/type UTC authority; does not read/write C100 state.
    internal sealed class WorkbenchEnchantmentCharacterLedger : IWorkbenchEnchantmentCooldownAuthority
    {
        internal const long DurationTicks=1800L*TimeSpan.TicksPerSecond;
        const int MaxBytes=16*1024*1024,MaxRecords=32768,MaxPerCharacter=4096;
        sealed class Record
        {internal WorkbenchCooldownPermit Permit;internal long ReservedAt,GrantedAt,ReadyAt,WorldRevision;internal WorkbenchCooldownNoGrantKind CancelKind;}
        sealed class State
        {internal long Revision,LastUtc;internal readonly Dictionary<string,Record> Records=new Dictionary<string,Record>(StringComparer.Ordinal);}
        readonly string path;readonly Func<long> utc;readonly int ownerThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
        State state;byte[] verified;bool writing,validating;
        readonly Dictionary<string,Record> pending=new Dictionary<string,Record>(StringComparer.Ordinal);
        readonly Dictionary<string,long> deadlines=new Dictionary<string,long>(StringComparer.Ordinal);
        readonly Dictionary<long,int> counts=new Dictionary<long,int>();
        readonly Dictionary<string,WorkbenchCooldownPermit> retired=new Dictionary<string,WorkbenchCooldownPermit>(StringComparer.Ordinal);
        long retiredWorld;
        internal bool Available{get;private set;}
        internal string Failure{get;private set;}="";
        internal string PathName=>path;
        internal WorkbenchEnchantmentCharacterLedger(string filename,Func<long> utcNow)
        {
            path=Path.GetFullPath(filename);utc=utcNow??throw new ArgumentNullException(nameof(utcNow));
            try
            {
                if(File.Exists(path)){verified=Read(path);state=Decode(verified);Available=true;Index();}
                else
                {
                    string dir=Path.GetDirectoryName(path);
                    if(File.Exists(path+".bak")||Directory.Exists(dir)&&Directory.GetFiles(dir,Path.GetFileName(path)+".*.tmp").Length!=0)throw new InvalidDataException("established cooldown store missing");
                    state=new State();Available=true;if(!Commit(_=>true))throw new IOException("initial cooldown commit");
                }
            }
            catch(Exception e){Available=false;Failure=e.Message;}
        }
        static string Key(long actor,string op)=>actor.ToString(System.Globalization.CultureInfo.InvariantCulture)+"/"+op;
        static string Channel(long actor,string choice)=>Key(actor,choice);
        static bool Known(string c)=>c=="Meadows"||c=="BlackForest"||c=="Swamp"||c=="Mountain"||c=="Plains"||c=="Mistlands";
        static bool Token(string t)=>Guid.TryParseExact(t,"N",out _);
        static bool Timestamp(long n)=>n>=DateTime.UnixEpoch.Ticks&&n<=DateTime.MaxValue.Ticks-DurationTicks;
        long Now(){long n=utc();if(!Timestamp(n)||n<state.LastUtc){Failure=Timestamp(n)?"clock-rollback":"invalid-utc";return 0;}if(Failure=="clock-rollback"||Failure=="invalid-utc")Failure="";return n;}
        internal long UtcNowTicks=>Available&&System.Threading.Thread.CurrentThread.ManagedThreadId==ownerThread?Now():0;
        // One verified snapshot before admissions in a new protocol epoch.
        internal bool BeginEpoch(long world)
        {
            if(world==0||validating||writing||!VerifyDisk()||Now()==0)return false;
            retired.Clear();retiredWorld=world;
            foreach(var row in pending.Values)if(row.Permit.World==world)retired.Add(Key(row.Permit.Character,row.Permit.Operation),Copy(row.Permit));
            return true;
        }
        internal bool IsRetiredPending(WorkbenchCooldownPermit original)
        {
            if(original==null||!Available||original.World!=retiredWorld||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread||Now()==0)return false;
            string key=Key(original.Character,original.Operation);
            return retired.TryGetValue(key,out var old)&&SamePermit(old,original)&&state.Records.TryGetValue(key,out var row)&&SamePermit(row.Permit,original)&&(row.Permit.Phase==WorkbenchCooldownPermitPhase.Reserved||row.Permit.Phase==WorkbenchCooldownPermitPhase.CommitIntent)&&VerifyDisk();
        }
        void Resume(WorkbenchCooldownPermit original)
        {if(original!=null&&retired.TryGetValue(Key(original.Character,original.Operation),out var old)&&SamePermit(old,original))retired.Remove(Key(original.Character,original.Operation));}
        internal WorkbenchCooldownPermit ReadOriginal(long character,long world,string operation,string choice)
        {
            if(character==0||world==0||!Known(choice)||!Token(operation)||!Available||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread||Now()==0)return null;
            if(!state.Records.TryGetValue(Key(character,operation),out var row)||row.Permit.World!=world||row.Permit.Choice!=choice||!VerifyDisk())return null;
            return Copy(row.Permit);
        }
        internal WorkbenchCooldownPermit[] ReadPending(long character,long world)
        {
            if(character==0||world==0||!Available||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread||Now()==0)return Array.Empty<WorkbenchCooldownPermit>();
            var result=new List<WorkbenchCooldownPermit>(6);
            foreach(string type in new[]{"Meadows","BlackForest","Swamp","Mountain","Plains","Mistlands"})
                if(pending.TryGetValue(Channel(character,type),out var row)&&row.Permit.World==world)result.Add(Copy(row.Permit));
            return result.Count==0||!VerifyDisk()?Array.Empty<WorkbenchCooldownPermit>():result.ToArray();
        }
        bool Current(WorkbenchCooldownContext c)
        {
            try{return Available&&System.Threading.Thread.CurrentThread.ManagedThreadId==ownerThread&&c!=null&&c.Character!=0&&c.World!=0&&Token(c.Station)&&!string.IsNullOrEmpty(c.NativeUid)&&!string.IsNullOrEmpty(c.NativeWitness)&&!string.IsNullOrEmpty(c.CharacterUid)&&Proof(c.IdentityCurrent)&&Now()!=0;}
            catch{return false;}
        }
        bool Matches(WorkbenchCooldownContext c,WorkbenchCooldownPermit p)
        {
            if(c.Character!=p.Character||c.World!=p.World||c.Station!=p.Station||c.NativeWitness!=p.NativeWitness)return false;
            try{return c.NativeUid==p.NativeUid||Proof(()=>c.OriginalNativeIdentity?.Invoke(p.NativeUid)==true);}catch{return false;}
        }
        static bool SamePermit(WorkbenchCooldownPermit a,WorkbenchCooldownPermit b)=>a!=null&&b!=null&&a.Character==b.Character&&a.World==b.World&&a.Operation==b.Operation&&a.Token==b.Token&&a.Choice==b.Choice&&a.Station==b.Station&&a.NativeUid==b.NativeUid&&a.NativeWitness==b.NativeWitness&&a.Generation==b.Generation;
        static WorkbenchCooldownPermit Copy(WorkbenchCooldownPermit p)=>new WorkbenchCooldownPermit{Character=p.Character,World=p.World,Operation=p.Operation,Token=p.Token,Choice=p.Choice,Station=p.Station,NativeUid=p.NativeUid,NativeWitness=p.NativeWitness,Generation=p.Generation,Phase=p.Phase};
        WorkbenchCooldownView View(WorkbenchCooldownContext c,string choice,WorkbenchCooldownStatus status,Record r=null)
        {
            deadlines.TryGetValue(Channel(c?.Character??0,choice),out long deadline);long now=Available?Now():0;
            return new WorkbenchCooldownView{Choice=choice,Status=status,RemainingSeconds=now==0?0:Math.Min(1800,Math.Max(0,(deadline-now)/(double)TimeSpan.TicksPerSecond)),Generation=state?.Revision??0,Permit=r==null?null:Copy(r.Permit)};
        }
        public WorkbenchCooldownView Query(WorkbenchCooldownContext c,string choice)
        {
            if(!Known(choice)||!Current(c))return View(c,choice,WorkbenchCooldownStatus.Unknown);
            if(pending.TryGetValue(Channel(c.Character,choice),out var row))return View(c,choice,WorkbenchCooldownStatus.Pending,Matches(c,row.Permit)?row:null);
            deadlines.TryGetValue(Channel(c.Character,choice),out long deadline);
            return View(c,choice,deadline>Now()?WorkbenchCooldownStatus.Cooldown:WorkbenchCooldownStatus.Ready);
        }
        public WorkbenchCooldownView Inspect(WorkbenchCooldownContext c,string operation,string choice)
        {
            if(!Known(choice)||!Token(operation)||!Current(c))return View(c,choice,WorkbenchCooldownStatus.Unknown);
            if(!state.Records.TryGetValue(Key(c.Character,operation),out var r))return Query(c,choice);
            if(r.Permit.Choice!=choice||!Matches(c,r.Permit))return View(c,choice,WorkbenchCooldownStatus.Unknown);
            var status=r.Permit.Phase==WorkbenchCooldownPermitPhase.Committed?WorkbenchCooldownStatus.Committed:r.Permit.Phase==WorkbenchCooldownPermitPhase.Cancelled?WorkbenchCooldownStatus.Cancelled:r.Permit.Phase==WorkbenchCooldownPermitPhase.CommitIntent?WorkbenchCooldownStatus.Pending:WorkbenchCooldownStatus.Ready;
            return View(c,choice,status,r);
        }
        public WorkbenchCooldownView Reserve(WorkbenchCooldownContext c,string operation,string choice)
        {
            if(validating||writing||!VerifyDisk())return View(c,choice,WorkbenchCooldownStatus.Unknown);
            c=Snapshot(c);
            var prior=Inspect(c,operation,choice);
            if(prior.Permit!=null)
            {
                if(prior.Permit.Operation==operation&&(prior.Permit.Phase==WorkbenchCooldownPermitPhase.Reserved||prior.Permit.Phase==WorkbenchCooldownPermitPhase.CommitIntent)&&Current(c)&&Proof(c.MayLearn)&&Current(c))Resume(prior.Permit);
                return prior;
            }
            if(prior.Status!=WorkbenchCooldownStatus.Ready)return prior;
            if(!Token(operation)||!Current(c)||!Proof(c.MayLearn)||!Current(c)||state.Records.Count>=MaxRecords||counts.TryGetValue(c.Character,out int n)&&n>=MaxPerCharacter)return View(c,choice,WorkbenchCooldownStatus.Unknown);
            long now=Now();
            if(!Commit(next=>
            {
                if(!Current(c)||!Proof(c.MayLearn)||!Current(c)||next.Records.ContainsKey(Key(c.Character,operation))||pending.ContainsKey(Channel(c.Character,choice)))return false;
                deadlines.TryGetValue(Channel(c.Character,choice),out long deadline);if(deadline>now)return false;
                next.Records.Add(Key(c.Character,operation),new Record{ReservedAt=now,Permit=new WorkbenchCooldownPermit{Character=c.Character,World=c.World,Station=c.Station,NativeUid=c.NativeUid,NativeWitness=c.NativeWitness,Choice=choice,Operation=operation,Token=Guid.NewGuid().ToString("N"),Generation=checked(next.Revision+1),Phase=WorkbenchCooldownPermitPhase.Reserved}});return true;
            }))return View(c,choice,WorkbenchCooldownStatus.Unknown);
            return Inspect(c,operation,choice);
        }
        public bool PrepareCommit(WorkbenchCooldownContext c,WorkbenchCooldownPermit permit)
        {
            if(validating||writing||!VerifyDisk())return false;
            c=Snapshot(c);permit=permit==null?null:Copy(permit);
            if(!Current(c)||!Proof(c.MayLearn)||!Find(c,permit,out var r)||!Current(c)||!VerifyDisk())return false;
            Resume(permit); // A resumed current-epoch action is never auto-cancel eligible.
            if(r.Permit.Phase==WorkbenchCooldownPermitPhase.CommitIntent)return true;
            if(r.Permit.Phase!=WorkbenchCooldownPermitPhase.Reserved)return false;
            return Commit(next=>{if(!Current(c)||!Proof(c.MayLearn)||!Find(c,permit,out _)||!Current(c))return false;next.Records[Key(c.Character,permit.Operation)].Permit.Phase=WorkbenchCooldownPermitPhase.CommitIntent;return true;});
        }
        bool Find(WorkbenchCooldownContext c,WorkbenchCooldownPermit p,out Record record)
        {record=null;return p!=null&&state.Records.TryGetValue(Key(c.Character,p.Operation),out record)&&SamePermit(record.Permit,p)&&Matches(c,record.Permit);}
        static bool GrantMatches(WorkbenchCooldownPermit p,WorkbenchCooldownGrantCertificate g)=>g!=null&&g.Character==p.Character&&g.World==p.World&&g.Operation==p.Operation&&g.Token==p.Token&&g.Choice==p.Choice&&g.Station==p.Station&&g.OriginalNativeUid==p.NativeUid&&g.NativeWitness==p.NativeWitness&&g.WorldRevision>0&&Timestamp(g.GrantedAtUtcTicks)&&g.ReadyAtUtcTicks==g.GrantedAtUtcTicks+DurationTicks;
        static bool NoGrantMatches(WorkbenchCooldownPermit p,WorkbenchCooldownNoGrantCertificate g)=>g!=null&&g.Character==p.Character&&g.World==p.World&&g.Operation==p.Operation&&g.Token==p.Token&&g.Choice==p.Choice&&g.Station==p.Station&&g.OriginalNativeUid==p.NativeUid&&g.NativeWitness==p.NativeWitness&&g.WorldRevision>0&&(g.Kind==WorkbenchCooldownNoGrantKind.Cancelled||g.Kind==WorkbenchCooldownNoGrantKind.NoOp);
        bool Proof(Func<bool> verify){bool prior=validating;validating=true;try{return verify?.Invoke()==true;}catch{return false;}finally{validating=prior;}}
        static WorkbenchCooldownContext Snapshot(WorkbenchCooldownContext c)
        {
            if(c==null)return null;
            var s=new WorkbenchCooldownContext{Character=c.Character,World=c.World,Station=c.Station,NativeUid=c.NativeUid,NativeWitness=c.NativeWitness,CharacterUid=c.CharacterUid};
            Func<bool> unchanged=()=>c.Character==s.Character&&c.World==s.World&&c.Station==s.Station&&c.NativeUid==s.NativeUid&&c.NativeWitness==s.NativeWitness&&c.CharacterUid==s.CharacterUid;
            var identity=c.IdentityCurrent;var learn=c.MayLearn;var alias=c.OriginalNativeIdentity;
            s.IdentityCurrent=()=>unchanged()&&identity?.Invoke()==true&&unchanged();
            s.MayLearn=()=>unchanged()&&learn?.Invoke()==true&&unchanged();
            s.OriginalNativeIdentity=old=>unchanged()&&alias?.Invoke(old)==true&&unchanged();return s;
        }
        public bool Complete(WorkbenchCooldownContext c,WorkbenchCooldownPermit permit,WorkbenchCooldownGrantCertificate grant)
        {
            if(validating||writing||!VerifyDisk()||grant==null)return false;
            c=Snapshot(c);permit=permit==null?null:Copy(permit);
            var source=grant;var check=grant.VerifyPersisted;
            grant=new WorkbenchCooldownGrantCertificate{Character=source.Character,World=source.World,Operation=source.Operation,Token=source.Token,Choice=source.Choice,Station=source.Station,OriginalNativeUid=source.OriginalNativeUid,NativeWitness=source.NativeWitness,WorldRevision=source.WorldRevision,GrantedAtUtcTicks=source.GrantedAtUtcTicks,ReadyAtUtcTicks=source.ReadyAtUtcTicks};
            var frozen=grant;grant.VerifyPersisted=()=>check?.Invoke()==true&&GrantMatches(permit,source)&&source.WorldRevision==frozen.WorldRevision&&source.GrantedAtUtcTicks==frozen.GrantedAtUtcTicks&&source.ReadyAtUtcTicks==frozen.ReadyAtUtcTicks;
            if(!Current(c)||!Find(c,permit,out var r)||!GrantMatches(r.Permit,grant)||!Proof(grant.VerifyPersisted)||!Current(c)||!Find(c,permit,out r)||!VerifyDisk()||grant.GrantedAtUtcTicks<r.ReservedAt||grant.GrantedAtUtcTicks>Now())return false;
            if(r.Permit.Phase==WorkbenchCooldownPermitPhase.Committed)return r.GrantedAt==grant.GrantedAtUtcTicks&&r.ReadyAt==grant.ReadyAtUtcTicks&&r.WorldRevision==grant.WorldRevision;
            if(r.Permit.Phase!=WorkbenchCooldownPermitPhase.CommitIntent)return false;
            return Commit(next=>
            {
                if(!Current(c)||!Find(c,permit,out var current)||current.Permit.Phase!=WorkbenchCooldownPermitPhase.CommitIntent||!Proof(grant.VerifyPersisted)||!Current(c))return false;
                var entry=next.Records[Key(c.Character,permit.Operation)];entry.Permit.Phase=WorkbenchCooldownPermitPhase.Committed;entry.GrantedAt=grant.GrantedAtUtcTicks;entry.ReadyAt=grant.ReadyAtUtcTicks;entry.WorldRevision=grant.WorldRevision;return true;
            });
        }
        public bool Cancel(WorkbenchCooldownContext c,WorkbenchCooldownPermit permit,WorkbenchCooldownNoGrantCertificate noGrant)
        {
            if(validating||writing||!VerifyDisk()||noGrant==null)return false;
            c=Snapshot(c);permit=permit==null?null:Copy(permit);
            var source=noGrant;var check=noGrant.VerifyPersisted;
            noGrant=new WorkbenchCooldownNoGrantCertificate{Character=source.Character,World=source.World,Operation=source.Operation,Token=source.Token,Choice=source.Choice,Station=source.Station,OriginalNativeUid=source.OriginalNativeUid,NativeWitness=source.NativeWitness,WorldRevision=source.WorldRevision,Kind=source.Kind};
            var frozen=noGrant;noGrant.VerifyPersisted=()=>check?.Invoke()==true&&NoGrantMatches(permit,source)&&source.WorldRevision==frozen.WorldRevision&&source.Kind==frozen.Kind;
            if(!Current(c)||!Find(c,permit,out var r)||!NoGrantMatches(r.Permit,noGrant)||!Proof(noGrant.VerifyPersisted)||!Current(c)||!Find(c,permit,out r)||!VerifyDisk())return false;
            if(r.Permit.Phase==WorkbenchCooldownPermitPhase.Cancelled)return r.WorldRevision==noGrant.WorldRevision&&r.CancelKind==noGrant.Kind;
            if(r.Permit.Phase==WorkbenchCooldownPermitPhase.Committed)return false;
            return Commit(next=>
            {
                if(!Current(c)||!Find(c,permit,out var current)||current.Permit.Phase==WorkbenchCooldownPermitPhase.Committed||!Proof(noGrant.VerifyPersisted)||!Current(c))return false;
                var entry=next.Records[Key(c.Character,permit.Operation)];entry.Permit.Phase=WorkbenchCooldownPermitPhase.Cancelled;entry.WorldRevision=noGrant.WorldRevision;entry.CancelKind=noGrant.Kind;return true;
            });
        }
        void Index()
        {
            pending.Clear();deadlines.Clear();counts.Clear();
            foreach(var r in state.Records.Values)
            {
                var p=r.Permit;string channel=Channel(p.Character,p.Choice);counts.TryGetValue(p.Character,out int n);counts[p.Character]=n+1;
                if(p.Phase==WorkbenchCooldownPermitPhase.Reserved||p.Phase==WorkbenchCooldownPermitPhase.CommitIntent){if(pending.ContainsKey(channel))throw new InvalidDataException("duplicate pending channel");pending.Add(channel,r);}
                if(p.Phase==WorkbenchCooldownPermitPhase.Committed){deadlines.TryGetValue(channel,out long value);deadlines[channel]=Math.Max(value,r.ReadyAt);}
            }
        }
        bool VerifyDisk()
        {
            if(!Available||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread)return false;
            try{if(verified==null){if(File.Exists(path))throw new IOException("unexpected store");}else if(!File.Exists(path)||!verified.SequenceEqual(Read(path)))throw new IOException("cooldown store diverged or missing");return true;}
            catch(Exception e){Available=false;Failure=e.Message;return false;}
        }
        bool Commit(Func<State,bool> change)
        {
            if(!Available||writing||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread||!VerifyDisk())return false;
            writing=true;string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                long now=Now();if(now==0)return false;var next=Decode(Encode(state));if(!change(next))return false;
                next.Revision=checked(state.Revision+1);next.LastUtc=Math.Max(next.LastUtc,now);byte[] bytes=Encode(next);Decode(bytes);
                if(!VerifyDisk())return false;Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
                if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
                byte[] actual=Read(path);if(!actual.SequenceEqual(bytes))throw new IOException("cooldown readback mismatch");state=Decode(actual);verified=actual;Index();return true;
            }
            catch(Exception e){Available=false;Failure=e.Message;return false;}
            finally{writing=false;} // Preserve uncertain .tmp/.bak/corrupt bytes; never auto reset/recover.
        }
        static byte[] Read(string file)
        {
            using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
            {if(f.Length<56||f.Length>MaxBytes)throw new InvalidDataException("cooldown size");var b=new byte[(int)f.Length];int at=0;while(at<b.Length){int n=f.Read(b,at,b.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}if(f.ReadByte()!=-1)throw new IOException("store changed during read");return b;}
        }
        static void Text(BinaryWriter w,string s){if(s==null||s.Length>512)throw new InvalidDataException("text");w.Write(s);}
        static string Text(BinaryReader r){string s=r.ReadString();if(s.Length>512)throw new InvalidDataException("text");return s;}
        static byte[] Encode(State s)
        {
            if(s.Records.Count>MaxRecords)throw new InvalidDataException("capacity");using(var stream=new MemoryStream())
            {
                using(var w=new BinaryWriter(stream,new UTF8Encoding(false,true),true))
                {
                    w.Write(1);w.Write(s.Revision);w.Write(s.LastUtc);w.Write(s.Records.Count);
                    foreach(var row in s.Records.OrderBy(x=>x.Key,StringComparer.Ordinal))
                    {
                        var r=row.Value;var p=r.Permit;w.Write(p.Character);w.Write(p.World);Text(w,p.Operation);Text(w,p.Token);Text(w,p.Choice);Text(w,p.Station);Text(w,p.NativeUid);Text(w,p.NativeWitness);w.Write(p.Generation);w.Write((int)p.Phase);w.Write(r.ReservedAt);w.Write(r.GrantedAt);w.Write(r.ReadyAt);w.Write(r.WorldRevision);w.Write((int)r.CancelKind);
                    }
                }
                var payload=stream.ToArray();if(payload.Length>MaxBytes-32)throw new InvalidDataException("store capacity");using(var sha=SHA256.Create())stream.Write(sha.ComputeHash(payload),0,32);return stream.ToArray();
            }
        }
        static State Decode(byte[] bytes)
        {
            if(bytes==null||bytes.Length<56||bytes.Length>MaxBytes)throw new InvalidDataException("size");int len=bytes.Length-32;
            using(var sha=SHA256.Create()){byte[] hash=sha.ComputeHash(bytes,0,len);for(int i=0;i<32;i++)if(hash[i]!=bytes[len+i])throw new InvalidDataException("digest");}
            using(var stream=new MemoryStream(bytes,0,len,false))using(var r=new BinaryReader(stream,new UTF8Encoding(false,true)))
            {
                if(r.ReadInt32()!=1)throw new InvalidDataException("schema");var s=new State{Revision=r.ReadInt64(),LastUtc=r.ReadInt64()};int count=r.ReadInt32();
                if(s.Revision<0||s.LastUtc!=0&&!Timestamp(s.LastUtc)||count<0||count>MaxRecords)throw new InvalidDataException("header");
                var channels=new HashSet<string>(StringComparer.Ordinal);var tokens=new HashSet<string>(StringComparer.Ordinal);var perActor=new Dictionary<long,int>();
                for(int i=0;i<count;i++)
                {
                    var p=new WorkbenchCooldownPermit{Character=r.ReadInt64(),World=r.ReadInt64(),Operation=Text(r),Token=Text(r),Choice=Text(r),Station=Text(r),NativeUid=Text(r),NativeWitness=Text(r),Generation=r.ReadInt64(),Phase=(WorkbenchCooldownPermitPhase)r.ReadInt32()};
                    var entry=new Record{Permit=p,ReservedAt=r.ReadInt64(),GrantedAt=r.ReadInt64(),ReadyAt=r.ReadInt64(),WorldRevision=r.ReadInt64(),CancelKind=(WorkbenchCooldownNoGrantKind)r.ReadInt32()};string key=Key(p.Character,p.Operation);
                    if(p.Character==0||p.World==0||!Token(p.Operation)||!Token(p.Token)||!tokens.Add(p.Token)||!Known(p.Choice)||!Token(p.Station)||p.NativeUid.Length==0||p.NativeWitness.Length==0||p.Generation<1||p.Generation>s.Revision||(int)p.Phase<0||(int)p.Phase>3||!Timestamp(entry.ReservedAt)||entry.ReservedAt>s.LastUtc||s.Records.ContainsKey(key))throw new InvalidDataException("record");
                    if(p.Phase==WorkbenchCooldownPermitPhase.Committed){if(!Timestamp(entry.GrantedAt)||entry.GrantedAt<entry.ReservedAt||entry.GrantedAt>s.LastUtc||entry.ReadyAt!=entry.GrantedAt+DurationTicks||entry.WorldRevision<1)throw new InvalidDataException("grant");}
                    else if(entry.GrantedAt!=0||entry.ReadyAt!=0||p.Phase==WorkbenchCooldownPermitPhase.Cancelled&&(entry.WorldRevision<1||(int)entry.CancelKind<0||(int)entry.CancelKind>1)||p.Phase!=WorkbenchCooldownPermitPhase.Cancelled&&entry.WorldRevision!=0)throw new InvalidDataException("non-grant");
                    if((p.Phase==WorkbenchCooldownPermitPhase.Reserved||p.Phase==WorkbenchCooldownPermitPhase.CommitIntent)&&!channels.Add(Channel(p.Character,p.Choice)))throw new InvalidDataException("pending channel");
                    perActor.TryGetValue(p.Character,out int n);if(n>=MaxPerCharacter)throw new InvalidDataException("character capacity");perActor[p.Character]=n+1;s.Records.Add(key,entry);
                }
                if(stream.Position!=len)throw new InvalidDataException("trailing bytes");return s;
            }
        }
    }
}
