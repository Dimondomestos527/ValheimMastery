using System;
using System.Collections.Generic;
namespace ValheimMastery
{
    // ONLY module guard ZDO.Load witnesses inside native server world/chunk loading.
    // A matching marker/nonce copied into an arbitrary runtime ZDO is not a load witness.
    internal static class MasterIdolForestGuardLoadIdentity
    {
        private sealed class Proof
        {internal ZDO Data;internal ZDOID Uid;internal string Family,Nonce,Name,Home;internal long Born,World;internal ZNet Session;internal ZDOMan Manager;internal ulong Generation;}
        private sealed class Alias{internal Proof Proof;internal MasterIdolForestGuardRecord Before;}
        private static readonly Dictionary<string,Proof> LoadedGuards=new Dictionary<string,Proof>(StringComparer.Ordinal);
        private static readonly HashSet<string> Duplicates=new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string,Alias> Aliases=new Dictionary<string,Alias>(StringComparer.Ordinal);
        private static readonly Dictionary<string,Alias> ReplayAliases=new Dictionary<string,Alias>(StringComparer.Ordinal);
        private static bool Overflow;
        private static string Key(string family,string nonce,long born)=>family+"/"+nonce+"/"+born;
        internal static void Reset(){LoadedGuards.Clear();Duplicates.Clear();Aliases.Clear();ReplayAliases.Clear();Overflow=false;}
        internal static void Loaded(ZDO data)
        {
            if(!MasterIdolLoadIdentity.CapturingGuardNative||!MasterIdolForestGuardState.ModuleBorn(data))return;
            int family=MasterIdolForestGuardState.Family(data);string name=data.GetString(ZDOVars.s_tamedName,""),home=data.GetString(MasterIdolNativeTaming.HomeKey,"");
            if(!MasterIdolForestRules.Name(name)||!MasterIdolNativeTaming.SameHome(home,home))return;
            string nonce=data.GetString(MasterIdolForestGuardState.NonceKey,"");long born=data.GetLong(MasterIdolForestGuardState.BornKey,0);string key=Key(MasterIdolForestRules.Families[family],nonce,born);
            if(LoadedGuards.TryGetValue(key,out var previous)){if(!ReferenceEquals(previous.Data,data))Duplicates.Add(key);return;}
            if(LoadedGuards.Count>=64){Overflow=true;return;}
            LoadedGuards.Add(key,new Proof{Data=data,Uid=data.m_uid,Family=MasterIdolForestRules.Families[family],Nonce=nonce,Born=born,Name=name,Home=home,
                World=ZNet.instance.GetWorldUID(),Session=ZNet.instance,Manager=ZDOMan.instance,Generation=MasterIdolLoadIdentity.Generation});
        }
        private static bool Context(Proof p)
            =>p!=null&&!Overflow&&MasterIdolLoadIdentity.LoadReady&&p.Session==ZNet.instance&&p.Manager==ZDOMan.instance&&p.Generation==MasterIdolLoadIdentity.Generation&&
                p.Session?.IsServer()==true&&p.Session.GetWorld()!=null&&p.World==p.Session.GetWorldUID()&&!Duplicates.Contains(Key(p.Family,p.Nonce,p.Born));
        private static bool Record(Proof p,MasterIdolForestGuardRecord record)
            =>Context(p)&&record?.Valid()==true&&record.Family==p.Family&&record.Nonce==p.Nonce&&record.Born==p.Born&&record.Name==p.Name&&MasterIdolNativeTaming.SameHome(record.Home,p.Home);
        private static bool Current(Proof p)
            =>Context(p)&&ReferenceEquals(p.Manager.GetZDO(p.Uid),p.Data)&&p.Data.m_uid==p.Uid&&MasterIdolForestGuardState.ModuleBorn(p.Data)&&
                MasterIdolForestRules.Families[MasterIdolForestGuardState.Family(p.Data)]==p.Family&&p.Data.GetLong(MasterIdolForestGuardState.BornKey,0)==p.Born&&
                p.Data.GetString(MasterIdolForestGuardState.NonceKey,"")==p.Nonce&&p.Data.GetString(ZDOVars.s_tamedName,"")==p.Name&&MasterIdolNativeTaming.SameHome(p.Data.GetString(MasterIdolNativeTaming.HomeKey,""),p.Home);
        private static bool State(Proof p,MasterIdolForestGuardRecord record)
        {float health=p.Data.GetFloat(ZDOVars.s_health,record.Death==0?1f:float.NaN);return float.IsFinite(health)&&(record.Death==0?health>0:health<=0);}
        private static bool SameRecord(MasterIdolForestGuardRecord a,MasterIdolForestGuardRecord b)
            =>a!=null&&b!=null&&a.Family==b.Family&&a.Uid==b.Uid&&a.Nonce==b.Nonce&&a.Name==b.Name&&a.Born==b.Born&&a.Death==b.Death&&a.Respawn==b.Respawn&&MasterIdolNativeTaming.SameHome(a.Home,b.Home);
        private static bool SameBirthState(MasterIdolForestGuardRecord a,MasterIdolForestGuardRecord b)
            =>a?.Valid()==true&&b?.Valid()==true&&a.Family==b.Family&&a.Nonce==b.Nonce&&a.Name==b.Name&&a.Born==b.Born&&a.Death==b.Death&&a.Respawn==b.Respawn&&MasterIdolNativeTaming.SameHome(a.Home,b.Home);
        // Explicit pre-consume admission for the protected one-off request, which can be older than
        // the persisted slot's last load UID. Never infer a live target from historical UID text.
        internal static bool AdmitReplayAlias(MasterIdolForestGuardRecord expected,MasterIdolForestGuardRecord current)
        {
            if(!SameBirthState(expected,current)||expected.Uid==current.Uid)return false;
            var slots=MasterIdolForestGuardState.Slots;
            if(slots==null||!slots.TryGetValue(current.Family,out var slot)||!ReferenceEquals(slot,current)||
                !LoadedGuards.TryGetValue(Key(current.Family,current.Nonce,current.Born),out var proof)||!Record(proof,current)||!Current(proof)||!State(proof,current)||
                current.Uid!=proof.Uid.ToString()||!MasterIdolForestGuardState.Matches(current,proof.Data))return false;
            if(ReplayAliases.TryGetValue(expected.Uid,out var existing))return ReferenceEquals(existing.Proof,proof)&&SameRecord(existing.Before,expected);
            if(ReplayAliases.Count>=4)return false;
            ReplayAliases.Add(expected.Uid,new Alias{Proof=proof,Before=expected.Copy()});return true;
        }
        internal static bool Rebind(MasterIdolForestGuardRecord record)
        {
            if(record?.Valid()!=true||!LoadedGuards.TryGetValue(Key(record.Family,record.Nonce,record.Born),out var proof)||!Record(proof,record)||!Current(proof)||!State(proof,record)||record.Uid==proof.Uid.ToString())return false;
            if(Aliases.Count>=4||Aliases.ContainsKey(record.Uid))return false;
            Aliases.Add(record.Uid,new Alias{Proof=proof,Before=record.Copy()});record.Uid=proof.Uid.ToString();return true;
        }
        internal static ZDO Resolve(MasterIdolForestGuardRecord record)
        {
            if(record?.Valid()!=true)return null;
            if(ReplayAliases.TryGetValue(record.Uid,out var replay))return SameRecord(replay.Before,record)&&Record(replay.Proof,record)&&Current(replay.Proof)&&State(replay.Proof,record)?replay.Proof.Data:null;
            // A historical 1:ordinal can now belong to a different piece. An issued unique load proof
            // resolves the real guard; the object currently using the historical UID is never touched.
            if(Aliases.TryGetValue(record.Uid,out var alias))return SameRecord(alias.Before,record)&&Record(alias.Proof,record)&&Current(alias.Proof)&&State(alias.Proof,record)?alias.Proof.Data:null;
            var direct=MasterIdolForestGuardState.Data(record.Uid);return direct!=null&&MasterIdolForestGuardState.Matches(record,direct)?direct:null;
        }
        internal static bool Matches(MasterIdolForestGuardRecord record,ZDO data)
            =>data!=null&&(MasterIdolForestGuardState.Matches(record,data)||ReferenceEquals(Resolve(record),data));
        // Metadata equivalence survives our own deliberate destruction. This is NOT a live-target admission.
        internal static bool SameReboundRecord(MasterIdolForestGuardRecord a,MasterIdolForestGuardRecord b)
            =>ReplayRebound(a,b)||ReplayRebound(b,a)||Rebound(a,b)||Rebound(b,a);
        private static bool ReplayRebound(MasterIdolForestGuardRecord old,MasterIdolForestGuardRecord current)
            =>old!=null&&current!=null&&ReplayAliases.TryGetValue(old.Uid,out var alias)&&SameRecord(alias.Before,old)&&Record(alias.Proof,current)&&current.Uid==alias.Proof.Uid.ToString();
        private static bool Rebound(MasterIdolForestGuardRecord old,MasterIdolForestGuardRecord current)
            =>old!=null&&current!=null&&Aliases.TryGetValue(old.Uid,out var alias)&&SameRecord(alias.Before,old)&&Record(alias.Proof,current)&&current.Uid==alias.Proof.Uid.ToString();
    }
}
