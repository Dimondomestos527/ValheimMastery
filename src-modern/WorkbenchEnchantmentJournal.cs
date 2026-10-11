using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace ValheimMastery
{
    internal sealed class WorkbenchEnchantmentGrant
    {
        internal string Choice, Operation, Origin, HomeAlias;
        internal long Payer; internal WorkbenchMaterial[] Paid=Array.Empty<WorkbenchMaterial>();
        internal long BindingRevision;
        internal bool Retired;
        internal string FreeToken="",OriginalNativeUid="",FreeWitness="";
        internal long GrantedAtUtcTicks,ReadyAtUtcTicks,FreeRevision;
        internal bool FreeConfirmed;
    }
    internal sealed class WorkbenchEnchantmentStation
    {
        internal string Id, NativeUid, NativeWitness, Active="";
        internal string RemovalOperation="", RemovalKind=""; internal long RemovalActor;
        internal bool Removed;
        internal readonly List<WorkbenchEnchantmentGrant> Grants=new List<WorkbenchEnchantmentGrant>();
        internal readonly System.Collections.Generic.HashSet<string> ReloadAliases=new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
    }
    internal sealed class WorkbenchEnchantmentPending
    {
        internal string Operation, Station, Choice, Kind, Origin="", HomeAlias="";
        internal long Actor; internal WorkbenchMaterial[] Cost=Array.Empty<WorkbenchMaterial>();
        internal string FreeToken="",OriginalNativeUid="",FreeWitness="";
        internal long FreeRevision;
    }
    internal sealed class WorkbenchEnchantmentState
    {
        internal long World, Revision;
        internal readonly Dictionary<string,WorkbenchEnchantmentStation> Stations=new Dictionary<string,WorkbenchEnchantmentStation>(StringComparer.Ordinal);
        internal readonly Dictionary<string,WorkbenchEnchantmentPending> Pending=new Dictionary<string,WorkbenchEnchantmentPending>(StringComparer.Ordinal);
        internal readonly Dictionary<string,WorkbenchEnchantmentOrigin> AdditionalOrigins=new Dictionary<string,WorkbenchEnchantmentOrigin>(StringComparer.Ordinal);
        internal readonly Dictionary<string,WorkbenchEnchantmentPending> Cancelled=new Dictionary<string,WorkbenchEnchantmentPending>(StringComparer.Ordinal);
    }
    internal sealed class WorkbenchEnchantmentOrigin
    {
        internal string Origin,Station,Choice,Operation,HomeAlias;internal bool Retired;internal long BindingRevision;
    }
    // One world journal contains entitlements, old home aliases and origin fences.
    // No mutation is published until exact bytes survive replace + readback.
    internal sealed class WorkbenchEnchantmentJournal
    {
        const int Limit=16384, MaxBytes=16*1024*1024;
        readonly string path; WorkbenchEnchantmentState state;
        byte[] verifiedBytes;
        bool committing; readonly int ownerThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
        readonly HashSet<string> pendingStations=new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string,WorkbenchEnchantmentPending> pendingIndex=new Dictionary<string,WorkbenchEnchantmentPending>(StringComparer.Ordinal);
        readonly Dictionary<string,string[]> originIndex=new Dictionary<string,string[]>(StringComparer.Ordinal);
        readonly Dictionary<long,List<WorkbenchEnchantmentPending>> unconfirmedByActor=new Dictionary<long,List<WorkbenchEnchantmentPending>>();
        readonly Dictionary<long,int> recoveryCursors=new Dictionary<long,int>();
        internal bool Ready {get;private set;}
        internal long World=>state?.World??0;
        internal long Revision=>state?.Revision??0;
        internal WorkbenchEnchantmentJournal(string path,long world,bool createNew,bool priorNativeIdentity=false)
        {
            this.path=Path.GetFullPath(path); if(world==0)throw new ArgumentException("world");
            if(File.Exists(this.path)){verifiedBytes=ReadBounded(this.path);state=Decode(verifiedBytes,world);Ready=true;Reindex();}
            else if(createNew)
            {
                string directory=Path.GetDirectoryName(this.path);
                if(priorNativeIdentity||File.Exists(this.path+".bak")||Directory.Exists(directory)&&Directory.GetFiles(directory,Path.GetFileName(this.path)+".*.tmp").Length!=0)
                    throw new InvalidDataException("missing established journal; explicit recovery required");
                state=new WorkbenchEnchantmentState{World=world};Ready=true;if(!Commit(s=>true))throw new IOException("initial journal commit");
            }
        }
        static byte[] ReadBounded(string file)
        {
            using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                if(input.Length<56||input.Length>MaxBytes)throw new InvalidDataException("disk size");
                var bytes=new byte[(int)input.Length];int offset=0;
                while(offset<bytes.Length){int count=input.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count;}
                if(input.ReadByte()!=-1)throw new InvalidDataException("file changed");return bytes;
            }
        }
        internal WorkbenchEnchantmentState Snapshot()=>Ready?Decode(Encode(state),World):null;
        internal bool VerifyDisk()
        {
            if(!Ready||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread)return false;
            try
            {
                if(verifiedBytes==null){if(state.Revision!=0||File.Exists(path))throw new IOException("unverified journal creation");return true;}
                if(!File.Exists(path)||!Equal(verifiedBytes,ReadBounded(path)))throw new IOException("established journal missing or diverged");return true;
            }
            catch{Ready=false;return false;}
        }
        void Reindex()
        {
            pendingStations.Clear();pendingIndex.Clear();originIndex.Clear();unconfirmedByActor.Clear();recoveryCursors.Clear();
            foreach(var b in state.Stations.Values)foreach(var g in b.Grants)if(g.Origin.Length!=0)originIndex.Add(g.Origin,new[]{g.Retired?"Retired":"GrantCommitted",b.Id,g.Operation,g.HomeAlias,g.BindingRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)});
            foreach(var o in state.AdditionalOrigins.Values)originIndex.Add(o.Origin,new[]{o.Retired?"Retired":"GrantCommitted",o.Station,o.Operation,o.HomeAlias,o.BindingRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)});
            foreach(var p in state.Pending.Values)if(p.Kind=="FreeLearn"){pendingStations.Add(p.Station);pendingIndex.Add(p.Station,p);}
            // Legacy pending rows are immutable history, never executable admission.
            foreach(var b in state.Stations.Values)foreach(var g in b.Grants)if(g.FreeToken.Length!=0&&!g.FreeConfirmed)
            {
                var entry=new WorkbenchEnchantmentPending{Operation=g.Operation,Station=b.Id,Choice=g.Choice,Kind="FreeLearn",Actor=g.Payer,FreeToken=g.FreeToken,OriginalNativeUid=g.OriginalNativeUid,FreeWitness=g.FreeWitness};
                pendingStations.Add(b.Id);pendingIndex.Add(b.Id,entry);
                if(!unconfirmedByActor.TryGetValue(g.Payer,out var rows))unconfirmedByActor[g.Payer]=rows=new List<WorkbenchEnchantmentPending>();rows.Add(entry);
            }
        }
        internal bool HasPending(string station)=>!Ready||pendingStations.Contains(station);
        internal WorkbenchEnchantmentPending[] ReadUnconfirmed(long actor)
        {
            if(!Ready||!unconfirmedByActor.TryGetValue(actor,out var rows)||rows.Count==0)return Array.Empty<WorkbenchEnchantmentPending>();
            recoveryCursors.TryGetValue(actor,out int cursor);int bounded=Math.Min(6,rows.Count);var result=new WorkbenchEnchantmentPending[bounded];
            for(int i=0;i<bounded;i++){var p=rows[(cursor+i)%rows.Count];result[i]=new WorkbenchEnchantmentPending{Operation=p.Operation,Station=p.Station,Choice=p.Choice,Kind=p.Kind,Actor=p.Actor,FreeToken=p.FreeToken,OriginalNativeUid=p.OriginalNativeUid,FreeWitness=p.FreeWitness};}
            recoveryCursors[actor]=(cursor+bounded)%rows.Count;return result;
        }
        internal WorkbenchEnchantmentPending ReadPending(string station)
        {
            if(!Ready||station==null||!pendingIndex.TryGetValue(station,out var p))return null;
            return new WorkbenchEnchantmentPending{Operation=p.Operation,Station=p.Station,Choice=p.Choice,Kind=p.Kind,Origin=p.Origin,HomeAlias=p.HomeAlias,Actor=p.Actor,Cost=(WorkbenchMaterial[])p.Cost.Clone(),FreeToken=p.FreeToken,OriginalNativeUid=p.OriginalNativeUid,FreeWitness=p.FreeWitness,FreeRevision=p.FreeRevision};
        }
        internal WorkbenchEnchantmentPending ReadCancellation(string operation)
        {
            if(!Ready||operation==null||!state.Cancelled.TryGetValue(operation,out var p))return null;
            return new WorkbenchEnchantmentPending{Operation=p.Operation,Station=p.Station,Choice=p.Choice,Kind=p.Kind,Origin=p.Origin,HomeAlias=p.HomeAlias,Actor=p.Actor,Cost=(WorkbenchMaterial[])p.Cost.Clone(),FreeToken=p.FreeToken,OriginalNativeUid=p.OriginalNativeUid,FreeWitness=p.FreeWitness,FreeRevision=p.FreeRevision};
        }
        internal WorkbenchEnchantmentOrigin[] ReadCommittedOrigins()
        {
            if(!Ready)return Array.Empty<WorkbenchEnchantmentOrigin>();var result=new List<WorkbenchEnchantmentOrigin>();
            foreach(var b in state.Stations.Values)foreach(var g in b.Grants)if(g.Origin.Length!=0)result.Add(new WorkbenchEnchantmentOrigin{Origin=g.Origin,Station=b.Id,Choice=g.Choice,Operation=g.Operation,HomeAlias=g.HomeAlias,BindingRevision=g.BindingRevision,Retired=g.Retired});
            foreach(var o in state.AdditionalOrigins.Values)result.Add(new WorkbenchEnchantmentOrigin{Origin=o.Origin,Station=o.Station,Choice=o.Choice,Operation=o.Operation,HomeAlias=o.HomeAlias,BindingRevision=o.BindingRevision,Retired=o.Retired});return result.ToArray();
        }
        internal string ReadOrigin(string origin,out string station,out string operation,out string homeAlias,out long bindingRevision)
        {
            station=operation=homeAlias="";bindingRevision=Revision;if(!Ready||string.IsNullOrEmpty(origin))return "Unresolved";
            if(!originIndex.TryGetValue(origin,out var row))return "None";station=row[1];operation=row[2];homeAlias=row[3];bindingRevision=long.Parse(row[4],System.Globalization.CultureInfo.InvariantCulture);return row[0];
        }
        // At most six grant rows; never serialize/hash the world for GUI reads.
        internal WorkbenchEnchantmentStation ReadStation(string id)
        {
            if(!Ready||id==null||!state.Stations.TryGetValue(id,out var b))return null;
            var copy=new WorkbenchEnchantmentStation{Id=b.Id,NativeUid=b.NativeUid,NativeWitness=b.NativeWitness,Active=b.Active,Removed=b.Removed,RemovalOperation=b.RemovalOperation,RemovalActor=b.RemovalActor,RemovalKind=b.RemovalKind};
            foreach(var alias in b.ReloadAliases)copy.ReloadAliases.Add(alias);
            foreach(var g in b.Grants)copy.Grants.Add(new WorkbenchEnchantmentGrant{Choice=g.Choice,Operation=g.Operation,Origin=g.Origin,HomeAlias=g.HomeAlias,Payer=g.Payer,Paid=(WorkbenchMaterial[])g.Paid.Clone(),Retired=g.Retired,BindingRevision=g.BindingRevision,FreeToken=g.FreeToken,OriginalNativeUid=g.OriginalNativeUid,FreeWitness=g.FreeWitness,GrantedAtUtcTicks=g.GrantedAtUtcTicks,ReadyAtUtcTicks=g.ReadyAtUtcTicks,FreeConfirmed=g.FreeConfirmed,FreeRevision=g.FreeRevision});return copy;
        }
        internal bool Commit(Func<WorkbenchEnchantmentState,bool> change)
        {
            if(!Ready||change==null||committing||System.Threading.Thread.CurrentThread.ManagedThreadId!=ownerThread)return false;
            committing=true;long expectedRevision=state.Revision;
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                if(!VerifyDisk())return false;
                var next=Snapshot(); if(!change(next))return false;
                next.Revision=checked(state.Revision+1); byte[] bytes=Encode(next);
                Decode(bytes,World);
                if(state.Revision!=expectedRevision||!VerifyDisk())throw new IOException("journal bytes or revision changed");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
                if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
                byte[] read=ReadBounded(path);
                if(!Equal(bytes,read))throw new IOException("journal readback differs");
                state=Decode(read,World);verifiedBytes=read;Reindex();return true;
            }
            catch {Ready=false;return false;} // uncertain replace cannot be treated as a rejected transaction
            finally {committing=false;try{if(File.Exists(temporary))File.Delete(temporary);}catch{}}
        }
        static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
        static void Text(BinaryWriter w,string s){if(s==null||s.Length>512)throw new InvalidDataException("string bound");w.Write(s);}
        static string Text(BinaryReader r){var s=r.ReadString();if(s.Length>512)throw new InvalidDataException("string bound");return s;}
        static int Count(BinaryReader r,int max){int n=r.ReadInt32();if(n<0||n>max)throw new InvalidDataException("count bound");return n;}
        static void Materials(BinaryWriter w,WorkbenchMaterial[] cost){if(cost==null||cost.Length>32)throw new InvalidDataException();w.Write(cost.Length);var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var c in cost){if(string.IsNullOrEmpty(c.Prefab)||c.Amount<1||!ids.Add(c.Prefab))throw new InvalidDataException("cost");Text(w,c.Prefab);w.Write(c.Amount);}}
        static WorkbenchMaterial[] Materials(BinaryReader r){int n=Count(r,32);var result=new WorkbenchMaterial[n];var ids=new HashSet<string>(StringComparer.Ordinal);for(int i=0;i<n;i++){string id=Text(r);int amount=r.ReadInt32();if(id.Length==0||amount<1||!ids.Add(id))throw new InvalidDataException("cost");result[i]=new WorkbenchMaterial(id,amount);}return result;}
        static void Id(string id){if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("identity");}
        internal static byte[] Encode(WorkbenchEnchantmentState s)
        {
            if(s.Stations.Count>Limit||s.Pending.Count>Limit||s.AdditionalOrigins.Count>Limit||s.Cancelled.Count>Limit)throw new InvalidDataException("journal capacity");
            using(var body=new MemoryStream())
            {
                using(var w=new BinaryWriter(body,Encoding.UTF8,true))
                {
                    w.Write(5);w.Write(s.World);w.Write(s.Revision);w.Write(s.Stations.Count);
                    foreach(var row in s.Stations){var b=row.Value;if(row.Key!=b.Id)throw new InvalidDataException();Text(w,b.Id);Text(w,b.NativeUid);Text(w,b.NativeWitness);Text(w,b.Active);w.Write(b.Removed);Text(w,b.RemovalOperation);Text(w,b.RemovalKind);w.Write(b.RemovalActor);w.Write(b.Grants.Count);
                        foreach(var g in b.Grants){Text(w,g.Choice);Text(w,g.Operation);Text(w,g.Origin??"");Text(w,g.HomeAlias??"");w.Write(g.Payer);Materials(w,g.Paid);w.Write(g.Retired);w.Write(g.BindingRevision);Text(w,g.FreeToken);Text(w,g.OriginalNativeUid);Text(w,g.FreeWitness);w.Write(g.GrantedAtUtcTicks);w.Write(g.ReadyAtUtcTicks);w.Write(g.FreeConfirmed);w.Write(g.FreeRevision);}if(b.ReloadAliases.Count>256)throw new InvalidDataException("alias bound");w.Write(b.ReloadAliases.Count);foreach(var alias in b.ReloadAliases)Text(w,alias);}
                    w.Write(s.Pending.Count);foreach(var row in s.Pending){var p=row.Value;if(row.Key!=p.Operation)throw new InvalidDataException();Text(w,p.Operation);Text(w,p.Station);Text(w,p.Choice);Text(w,p.Kind);Text(w,p.Origin);Text(w,p.HomeAlias);w.Write(p.Actor);Materials(w,p.Cost);Text(w,p.FreeToken);Text(w,p.OriginalNativeUid);Text(w,p.FreeWitness);w.Write(p.FreeRevision);}
                    w.Write(s.AdditionalOrigins.Count);foreach(var row in s.AdditionalOrigins){var o=row.Value;if(row.Key!=o.Origin)throw new InvalidDataException();Text(w,o.Origin);Text(w,o.Station);Text(w,o.Choice);Text(w,o.Operation);Text(w,o.HomeAlias);w.Write(o.Retired);w.Write(o.BindingRevision);}
                    w.Write(s.Cancelled.Count);foreach(var row in s.Cancelled){var p=row.Value;if(row.Key!=p.Operation)throw new InvalidDataException();Text(w,p.Operation);Text(w,p.Station);Text(w,p.Choice);w.Write(p.Actor);Materials(w,p.Cost);Text(w,p.Kind);Text(w,p.FreeToken);Text(w,p.OriginalNativeUid);Text(w,p.FreeWitness);w.Write(p.FreeRevision);}
                }
                byte[] payload=body.ToArray();if(payload.Length>MaxBytes-32)throw new InvalidDataException("size");using(var hash=SHA256.Create()){byte[] digest=hash.ComputeHash(payload);body.Write(digest,0,digest.Length);}return body.ToArray();
            }
        }
        internal static WorkbenchEnchantmentState Decode(byte[] bytes,long world)
        {
            if(bytes==null||bytes.Length<56||bytes.Length>MaxBytes)throw new InvalidDataException("size");
            int length=bytes.Length-32;using(var hash=SHA256.Create()){var digest=hash.ComputeHash(bytes,0,length);for(int i=0;i<32;i++)if(digest[i]!=bytes[length+i])throw new InvalidDataException("digest");}
            using(var stream=new MemoryStream(bytes,0,length,false))using(var r=new BinaryReader(stream,Encoding.UTF8))
            {
                int schema=r.ReadInt32();if(schema!=4&&schema!=5)throw new InvalidDataException("schema");var s=new WorkbenchEnchantmentState{World=r.ReadInt64(),Revision=r.ReadInt64()};if(s.World!=world||world==0||s.Revision<0)throw new InvalidDataException("world/revision");
                var origins=new HashSet<string>(StringComparer.Ordinal);var operations=new HashSet<string>(StringComparer.Ordinal);
                int n=Count(r,Limit);for(int i=0;i<n;i++)
                {
                    var b=new WorkbenchEnchantmentStation{Id=Text(r),NativeUid=Text(r),NativeWitness=Text(r),Active=Text(r),Removed=r.ReadBoolean(),RemovalOperation=Text(r),RemovalKind=Text(r),RemovalActor=r.ReadInt64()};Id(b.Id);if(b.NativeUid.Length==0||b.NativeWitness.Length==0||s.Stations.ContainsKey(b.Id))throw new InvalidDataException("station");
                    if(b.Removed){if(b.RemovalKind!="Destroy"&&b.RemovalKind!="Dismantle"||b.RemovalKind=="Dismantle"&&b.RemovalActor==0)throw new InvalidDataException("removal");Id(b.RemovalOperation);if(!operations.Add(b.RemovalOperation))throw new InvalidDataException("removal replay");}
                    else if(b.RemovalOperation.Length!=0||b.RemovalKind.Length!=0||b.RemovalActor!=0)throw new InvalidDataException("unexpected removal");
                    int grants=Count(r,6);var choices=new HashSet<string>(StringComparer.Ordinal);for(int j=0;j<grants;j++)
                    {
                        var g=new WorkbenchEnchantmentGrant{Choice=Text(r),Operation=Text(r),Origin=Text(r),HomeAlias=Text(r),Payer=r.ReadInt64(),Paid=Materials(r),Retired=r.ReadBoolean(),BindingRevision=r.ReadInt64()};Id(g.Operation);
                        if(schema>=5){g.FreeToken=Text(r);g.OriginalNativeUid=Text(r);g.FreeWitness=Text(r);g.GrantedAtUtcTicks=r.ReadInt64();g.ReadyAtUtcTicks=r.ReadInt64();g.FreeConfirmed=r.ReadBoolean();g.FreeRevision=r.ReadInt64();}
                        if(!WorkbenchEnchantmentRules.Known(g.Choice)||!choices.Add(g.Choice)||!operations.Add(g.Operation)||g.Payer==0)throw new InvalidDataException("grant");
                        if(g.Origin.Length!=0){if(!origins.Add(g.Origin)||g.HomeAlias.Length==0||g.Paid.Length!=0||g.BindingRevision<1||g.BindingRevision>s.Revision)throw new InvalidDataException("legacy origin");}
                        else if(g.HomeAlias.Length!=0||g.Retired||g.Paid.Length==0&&g.FreeToken.Length==0)throw new InvalidDataException("unexpected alias or unpaid new grant");
                        if(g.FreeToken.Length!=0){Id(g.FreeToken);if(g.Paid.Length!=0||g.Origin.Length!=0||g.OriginalNativeUid.Length==0||g.FreeWitness!=b.NativeWitness||g.GrantedAtUtcTicks<=0||g.ReadyAtUtcTicks!=checked(g.GrantedAtUtcTicks+TimeSpan.FromMinutes(30).Ticks)||g.ReadyAtUtcTicks>DateTime.MaxValue.Ticks||g.FreeRevision<1||g.FreeRevision>s.Revision)throw new InvalidDataException("free grant");}
                        else if(g.OriginalNativeUid.Length!=0||g.FreeWitness.Length!=0||g.GrantedAtUtcTicks!=0||g.ReadyAtUtcTicks!=0||g.FreeConfirmed||g.FreeRevision!=0)throw new InvalidDataException("legacy grant metadata");b.Grants.Add(g);
                    }
                    if(schema>=5){int aliases=Count(r,256);for(int a=0;a<aliases;a++){string alias=Text(r);if(alias.Length==0||alias==b.NativeUid||!b.ReloadAliases.Add(alias))throw new InvalidDataException("reload alias");}}
                    if(b.Active.Length!=0&&(!choices.Contains(b.Active)||b.Removed))throw new InvalidDataException("active");s.Stations.Add(b.Id,b);
                }
                n=Count(r,Limit);var pendingStations=new HashSet<string>(StringComparer.Ordinal);for(int i=0;i<n;i++)
                {
                    var p=new WorkbenchEnchantmentPending{Operation=Text(r),Station=Text(r),Choice=Text(r),Kind=Text(r),Origin=Text(r),HomeAlias=Text(r),Actor=r.ReadInt64(),Cost=Materials(r)};Id(p.Operation);
                    if(schema>=5){p.FreeToken=Text(r);p.OriginalNativeUid=Text(r);p.FreeWitness=Text(r);p.FreeRevision=r.ReadInt64();}
                    if(p.Actor==0||!s.Stations.TryGetValue(p.Station,out var b)||p.Kind=="FreeLearn"&&!pendingStations.Add(p.Station)||!operations.Add(p.Operation))throw new InvalidDataException("pending");
                    if(p.Kind!="Learn"&&p.Kind!="Dismantle"&&p.Kind!="Migration"&&p.Kind!="FreeLearn")throw new InvalidDataException("kind");
                    if(p.Kind!="Dismantle"&&!WorkbenchEnchantmentRules.Known(p.Choice))throw new InvalidDataException("pending choice");
                    if(p.Kind=="Migration"){if(p.Origin.Length==0||p.HomeAlias.Length==0||p.Cost.Length!=0||!origins.Add(p.Origin))throw new InvalidDataException("migration pending");}
                    else if(p.Origin.Length!=0||p.HomeAlias.Length!=0)throw new InvalidDataException("unexpected pending origin");s.Pending.Add(p.Operation,p);
                    if(p.Kind=="Learn"&&p.Cost.Length==0)throw new InvalidDataException("unpaid pending learn");
                    if(p.Kind=="FreeLearn"){Id(p.FreeToken);if(p.Cost.Length!=0||p.Origin.Length!=0||p.OriginalNativeUid.Length==0||p.FreeWitness!=b.NativeWitness)throw new InvalidDataException("free pending");}
                    else if(p.FreeToken.Length!=0||p.OriginalNativeUid.Length!=0||p.FreeWitness.Length!=0)throw new InvalidDataException("legacy pending metadata");
                }
                n=Count(r,Limit);for(int i=0;i<n;i++)
                {
                    var o=new WorkbenchEnchantmentOrigin{Origin=Text(r),Station=Text(r),Choice=Text(r),Operation=Text(r),HomeAlias=Text(r),Retired=r.ReadBoolean(),BindingRevision=r.ReadInt64()};Id(o.Operation);
                    if(o.Origin.Length==0||o.HomeAlias.Length==0||o.BindingRevision<1||o.BindingRevision>s.Revision||!origins.Add(o.Origin)||!operations.Add(o.Operation)||!s.Stations.TryGetValue(o.Station,out var b)||!b.Grants.Exists(g=>g.Choice==o.Choice))throw new InvalidDataException("additional origin");s.AdditionalOrigins.Add(o.Origin,o);
                }
                n=Count(r,Limit);for(int i=0;i<n;i++)
                {
                    var p=new WorkbenchEnchantmentPending{Operation=Text(r),Station=Text(r),Choice=Text(r),Actor=r.ReadInt64(),Cost=Materials(r),Kind="Cancelled"};Id(p.Operation);
                    if(schema>=5){p.Kind=Text(r);p.FreeToken=Text(r);p.OriginalNativeUid=Text(r);p.FreeWitness=Text(r);p.FreeRevision=r.ReadInt64();}
                    if(p.Actor==0||!WorkbenchEnchantmentRules.Known(p.Choice)||!s.Stations.ContainsKey(p.Station)||!operations.Add(p.Operation))throw new InvalidDataException("cancelled operation");
                    if(p.Kind=="Cancelled"){if(p.Cost.Length==0||p.FreeToken.Length!=0)throw new InvalidDataException("legacy cancel");}
                    else{if(p.Kind!="FreeCancelled"&&p.Kind!="FreeNoOp")throw new InvalidDataException("free cancel kind");Id(p.FreeToken);if(p.Cost.Length!=0||p.OriginalNativeUid.Length==0||p.FreeWitness!=s.Stations[p.Station].NativeWitness||p.FreeRevision<1||p.FreeRevision>s.Revision)throw new InvalidDataException("free cancel");}s.Cancelled.Add(p.Operation,p);
                }
                if(stream.Position!=length)throw new InvalidDataException("trailing bytes");return s;
            }
        }
    }
}




