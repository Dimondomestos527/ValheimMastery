using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // One server writer: committed desired state -> Cargo drain (OFF only) ->
    // native owner application. Presentation never supplies an authority receipt.
    internal static partial class MasterIdolForestGuardLifecycleController
    {
        internal struct ActivationChange {internal long World,Revision;internal string Station,NativeUid,Before,After;}
        sealed class Entry
        {internal MasterIdolForestGuardLifecycle.State Row;internal ZDO Data;internal long SeenActivation;internal float NextCommand;internal bool ApplicationSent,DrainAuthorized;internal ForestGuardDrainWitness DrainWitness;}
        static readonly Dictionary<string,ActivationChange> Changes=new Dictionary<string,ActivationChange>(StringComparer.Ordinal);
        static readonly Entry[] Entries=new Entry[4];
        static ZNet Net;static ZDOMan Manager;static ulong Generation;static long World,Sequence;static float Next;
        static bool Context()
        {
            var net=ZNet.instance;var man=ZDOMan.instance;
            if(net?.IsServer()!=true||net.GetWorld()==null||man==null||!MasterIdolLoadIdentity.LoadReady)return false;
            if(Net!=net||Manager!=man||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {Array.Clear(Entries,0,4);Changes.Clear();Net=net;Manager=man;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();Sequence=0;Next=0;}
            return World!=0;
        }
        internal static void OnCommittedActivation(long world,string stationToken,string nativeUid,string beforeActive,string afterActive,long journalRevision)
        {
            if(!Context()||world!=World||journalRevision<=0||beforeActive==afterActive||beforeActive!="BlackForest"&&afterActive!="BlackForest"||
                !MasterIdolForestRules.Nonce(stationToken)||!MasterIdolNetworkQuota.TryUid(nativeUid,out _,out _))return;
            if(Changes.TryGetValue(stationToken,out var previous)&&previous.Revision>=journalRevision)return;
            if(!Changes.ContainsKey(stationToken)&&Changes.Count>=4)
            {string oldest=null;long revision=long.MaxValue;foreach(var item in Changes)if(item.Value.Revision<revision){oldest=item.Key;revision=item.Value.Revision;}if(oldest!=null)Changes.Remove(oldest);}
            Changes[stationToken]=new ActivationChange{World=world,Revision=journalRevision,Station=stationToken,NativeUid=nativeUid,Before=beforeActive,After=afterActive};
            Next=0;Tick(); // Revoke admission before the committed Switch returns.
        }
        internal static bool TryLatestActivation(string home,out ActivationChange change)
        {change=default;string token=MasterIdolNativeTaming.Token(home);return Context()&&token!=null&&Changes.TryGetValue(token,out change);}
        internal static bool PendingAppearance=>false; // Legacy read facade; cosmetics never reserve gameplay.
        internal static void InitializeBirth(Character actor){Next=0;} // Registry/factory retains identity/persistence.
        static void Trace(MasterIdolForestGuardLifecycle.State row,string reason,string detail="",bool failure=false)
        {MasterIdolForestGuardDiagnostic.Event(row,MasterIdolForestGuardDiagnostic.Lane.Apply,reason,detail,failure,failure);}
        static bool Native(MasterIdolForestGuardLifecycle.State row,ZDO data)=>MasterIdolForestGuardLifecycleOwner.Native(row,data);
        internal static void OnOwnerOutcome(MasterIdolForestGuardLifecycle.State row,string epoch,ForestGuardOwnerAction action,ForestGuardOwnerOutcome outcome,long sender)
        {
            if(!Context()||row.Family<0||row.Family>=4||action!=ForestGuardOwnerAction.ApplyDesired)return;
            var entry=Entries[row.Family];if(entry==null||sender!=row.Owner||!MasterIdolForestGuardLifecycleOwner.Same(entry.Row,row))return;
            if(!entry.ApplicationSent||row.Status==MasterIdolForestGuardLifecycle.Phase.Suspended&&
                (!entry.DrainAuthorized||MasterIdolForestGuardLifecycle.StillCurrentCargoQuiesce(in entry.DrainWitness)!=ForestGuardDrainIntent.CurrentDormant))return;
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(entry.Data,out var accepted,out var currentEpoch)||epoch!=currentEpoch||!MasterIdolForestGuardLifecycleOwner.Same(accepted,row))return;
            Trace(row,"owner-outcome","outcome="+outcome,true);
            if(outcome!=ForestGuardOwnerOutcome.Applied||!Native(row,entry.Data))return;
            var applied=entry.Row;applied.Applied=true;entry.Row=applied;Next=0;
        }
        // External drain/diagnostic/native calls may synchronously replace the
        // controller context or any entry. Never dispatch a captured batch row
        // through the replacement entry, or write flags after it was replaced.
        static bool DispatchCurrent(Entry entry,MasterIdolForestGuardLifecycle.State row,string epoch,
            ZNet net,ZDOMan manager,long world,ulong generation,bool pending=true)
        {
            return entry!=null&&ReferenceEquals(Entries[row.Family],entry)&&
                Net==net&&net==ZNet.instance&&Manager==manager&&manager==ZDOMan.instance&&
                World==world&&net?.GetWorld()!=null&&net.GetWorldUID()==world&&net.IsServer()&&
                Generation==generation&&generation==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady&&
                MasterIdolForestGuardLifecycleOwner.Same(entry.Row,row)&&(!pending||!entry.Row.Applied)&&
                MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(entry.Data,out var accepted,out var currentEpoch)&&
                currentEpoch==epoch&&MasterIdolForestGuardLifecycleOwner.Same(accepted,row)&&(!pending||!accepted.Applied);
        }
        internal static void Tick()
        {
            MasterIdolForestGuardLifecycleVisibility.Tick();
            if(!Context()||Time.unscaledTime<Next||!MasterIdolForestGuardState.Ready)return;Next=Time.unscaledTime+1;
            var slots=MasterIdolForestGuardState.Slots;if(slots==null)return;
            var rows=new List<MasterIdolForestGuardLifecycle.State>(4);var current=new bool[4];
            for(int family=0;family<4;family++)
            {
                if(!slots.TryGetValue(MasterIdolForestRules.Families[family],out var record)||record.Death!=0){Entries[family]=null;continue;}
                var data=MasterIdolForestGuardLoadIdentity.Resolve(record);
                var diagnostic=new MasterIdolForestGuardLifecycle.State{Family=family,Born=record.Born,Nonce=record.Nonce,Home=record.Home};
                if(MasterIdolNetworkQuota.TryUid(record.Uid,out long user,out uint number))diagnostic.Id=new ZDOID(user,number);
                if(data==null||!MasterIdolForestGuardState.Matches(record,data))
                {Trace(diagnostic,"native-unresolved","life=ALIVE availability=UNRESOLVED reason="+MasterIdolForestGuardLoadIdentity.Diagnostic(record)+"; slot retained; no birth permission",true);continue;}
                if(!MasterIdolNativeTaming.SameHome(data.GetString(MasterIdolNativeTaming.HomeKey,""),record.Home))
                {Trace(diagnostic,"native-home-conflict","recovery=home-mismatch; no rehome or batch poisoning",true);continue;}
                float health=data.GetFloat(ZDOVars.s_health,1);
                if(!float.IsFinite(health)||health<=0){Trace(diagnostic,"native-health-conflict","life=ALIVE recovery=death-proof-required; no reset",true);continue;}
                var status=WorkbenchEnchantmentRuntime.ReadForestWardCommitted(record.Home,out var read);
                if(status==ForestWardCommittedStatus.Unknown){Trace(diagnostic,"station-proof-unknown","no inferred OFF",true);continue;}
                var desired=status==ForestWardCommittedStatus.Active?MasterIdolForestGuardLifecycle.Phase.Active:MasterIdolForestGuardLifecycle.Phase.Suspended;
                long activation=TryLatestActivation(record.Home,out var change)?change.Revision:0;
                var entry=Entries[family];
                bool changed=entry==null||entry.Data!=data||entry.Row.Id!=data.m_uid||entry.Row.Born!=record.Born||entry.Row.Nonce!=record.Nonce||entry.Row.Home!=record.Home||
                    entry.Row.Owner!=data.GetOwner()||entry.Row.OwnerRevision!=data.OwnerRevision||entry.Row.Status!=desired||activation>entry.SeenActivation;
                if(changed)
                {
                    Sequence=Math.Max(Sequence,Math.Max(read.JournalRevision,data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)));
                    if(Sequence==long.MaxValue){Trace(diagnostic,"intent-exhausted","pending; no wrap",true);continue;}
                    entry=new Entry{Data=data,SeenActivation=activation,Row=new MasterIdolForestGuardLifecycle.State{Id=data.m_uid,Family=family,Born=record.Born,Nonce=record.Nonce,Home=record.Home,
                        Owner=data.GetOwner(),OwnerRevision=data.OwnerRevision,Status=desired,Cycle=Math.Max(1,activation>0?activation:data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,record.Born)),
                        IntentRevision=++Sequence,Attempt=Guid.NewGuid().ToString("N"),Applied=false}};
                    Entries[family]=entry;Trace(entry.Row,"desired-current","life=ALIVE",true);
                }
                // A lost native receipt cannot leave a stale ACTIVE admission true.
                if(entry.Row.Applied&&!Native(entry.Row,data)){var pending=entry.Row;pending.Applied=false;entry.Row=pending;}
                if(!MasterIdolForestGuardLifecycle.NativeIdentity(entry.Row,data)){Trace(diagnostic,"native-identity-conflict","row omitted; no batch poisoning",true);continue;}
                rows.Add(entry.Row);current[family]=true;
            }
            var dispatchEntries=(Entry[])Entries.Clone();
            var dispatchNet=Net;var dispatchManager=Manager;long dispatchWorld=World;ulong dispatchGeneration=Generation;
            if(!MasterIdolForestGuardLifecycle.Commit(rows.ToArray()))return;
            foreach(var row in rows)
            {
                var entry=dispatchEntries[row.Family];
                if(!current[row.Family]||row.Applied||entry==null||
                    !MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(entry.Data,out _,out var dispatchEpoch)||
                    !DispatchCurrent(entry,row,dispatchEpoch,dispatchNet,dispatchManager,dispatchWorld,dispatchGeneration)||Time.unscaledTime<entry.NextCommand)continue;
                entry.NextCommand=Time.unscaledTime+1;
                if(row.Status==MasterIdolForestGuardLifecycle.Phase.Suspended)
                {
                    if(!MasterIdolForestGuardLifecycle.TryReadCargoQuiesce(entry.Data,out var witness)){Trace(row,"drain-proof-pending","",true);continue;}
                    var drain=MasterIdolResidentCargo.TryDrainForDormancy(in witness,out var reason);
                    if(!DispatchCurrent(entry,row,dispatchEpoch,dispatchNet,dispatchManager,dispatchWorld,dispatchGeneration))continue;
                    if(drain!=ResidentCargoDrainStatus.Ready||MasterIdolForestGuardLifecycle.StillCurrentCargoQuiesce(in witness)!=ForestGuardDrainIntent.CurrentDormant)
                    {Trace(row,"drain-pending","status="+drain+" reason="+reason,true);continue;}
                    entry.DrainAuthorized=true;entry.DrainWitness=witness;
                }
                Trace(row,"request-before","desired="+row.Status,true);
                if(!DispatchCurrent(entry,row,dispatchEpoch,dispatchNet,dispatchManager,dispatchWorld,dispatchGeneration))continue;
                entry.ApplicationSent=true;
                bool sent=MasterIdolForestGuardLifecycleOwner.Request(row,ForestGuardOwnerAction.ApplyDesired);
                if(!DispatchCurrent(entry,row,dispatchEpoch,dispatchNet,dispatchManager,dispatchWorld,dispatchGeneration,false))continue;
                if(!sent)entry.ApplicationSent=false;
                Trace(row,"request-after","sent="+sent+" applied="+entry.Row.Applied,true);
            }
        }
        internal static void Reset()
        {
            Array.Clear(Entries,0,4);Changes.Clear();Net=null;Manager=null;Generation=0;World=Sequence=0;Next=0;
            MasterIdolForestGuardLifecycleOwner.Reset();MasterIdolForestGuardLifecycleVisibility.Reset();
        }
    }
}
