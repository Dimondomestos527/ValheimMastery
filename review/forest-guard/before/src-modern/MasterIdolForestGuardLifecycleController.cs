using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Server-only accepted intent controller; publication is separate from owner
    // execution and from SERVER cargo quiescence. At most four native identities.
    internal static partial class MasterIdolForestGuardLifecycleController
    {
        internal struct ActivationChange
        {internal long World,Revision;internal string Station,NativeUid,Before,After;}
        static readonly Dictionary<string,ActivationChange> Changes=new Dictionary<string,ActivationChange>(StringComparer.Ordinal);
        static ZNet ChangeSession;static ZDOMan ChangeManager;static ulong ChangeGeneration;static long ChangeWorld;
        static bool ChangeContext()
        {
            var net=ZNet.instance;
            if(net?.IsServer()!=true||net.GetWorld()==null||ZDOMan.instance==null||!MasterIdolLoadIdentity.LoadReady)return false;
            long world=net.GetWorldUID();if(world==0)return false;
            if(ChangeSession!=net||ChangeManager!=ZDOMan.instance||ChangeGeneration!=MasterIdolLoadIdentity.Generation||ChangeWorld!=world)
            {Entries.Clear();Changes.Clear();NextTick=0;Sequence=0;DeferLines=0;ChangeSession=net;ChangeManager=ZDOMan.instance;ChangeGeneration=MasterIdolLoadIdentity.Generation;ChangeWorld=world;}
            return true;
        }
        internal static void OnCommittedActivation(long world,string stationToken,string nativeUid,string beforeActive,string afterActive,long journalRevision)
        {
            if(!ChangeContext()||world!=ChangeWorld||journalRevision<=0||beforeActive==afterActive||
                beforeActive!="BlackForest"&&afterActive!="BlackForest"||!MasterIdolForestRules.Nonce(stationToken)||
                !MasterIdolNetworkQuota.TryUid(nativeUid,out _,out _))return;
            if(Changes.TryGetValue(stationToken,out var old)&&old.Revision>=journalRevision)return;
            if(!Changes.ContainsKey(stationToken)&&Changes.Count>=4)
            {
                string oldest=null;long at=long.MaxValue;
                foreach(var pair in Changes)if(pair.Value.Revision<at){oldest=pair.Key;at=pair.Value.Revision;}
                if(oldest!=null)Changes.Remove(oldest);
            }
            Changes[stationToken]=new ActivationChange{World=world,Station=stationToken,NativeUid=nativeUid,Before=beforeActive,After=afterActive,Revision=journalRevision};
            NextTick=0;Tick(); // Revoke old Active admission before accepted Switch returns.
        }
        internal static bool TryLatestActivation(string home,out ActivationChange change)
        {
            change=default;if(!ChangeContext())return false;
            string token=MasterIdolNativeTaming.Token(home);
            return token!=null&&Changes.TryGetValue(token,out change);
        }

        sealed class Entry
        {
            internal MasterIdolForestGuardLifecycle.State Row;
            internal bool WantsActive,Ack,Completed,ReleaseAck,Published;
            internal long SeenActivation;
            internal float Started=-1,NextCommand,RetryAt;
            internal string Epoch,LastDefer;internal float NextDefer;
        }
        static readonly Dictionary<ZDOID,Entry> Entries=new Dictionary<ZDOID,Entry>();
        static float NextTick;
        static long Sequence;static int DeferLines;
        static void Defer(Entry entry,string reason)
        {
            if(DeferLines>=32||entry.LastDefer==reason&&Time.unscaledTime<entry.NextDefer)return;
            entry.LastDefer=reason;entry.NextDefer=Time.unscaledTime+15;DeferLines++;
            MasteryPlugin.Log?.LogInfo("[ForestLifecycleDefer] family="+entry.Row.Family+" uid="+entry.Row.Id+" phase="+entry.Row.Status+" owner="+entry.Row.Owner+" reason="+reason);
        }
        static long Intent()
        {
            if(Sequence<long.MaxValue)Sequence++;
            return Sequence;
        }
        static void Transition(Entry entry,MasterIdolForestGuardLifecycle.Phase phase,bool newAttempt=false)
        {
            var row=entry.Row;row.Status=phase;row.IntentRevision=Intent();
            if(newAttempt||!MasterIdolForestRules.Nonce(row.Attempt))row.Attempt=Guid.NewGuid().ToString("N");
            entry.Row=row;entry.Ack=entry.Completed=entry.ReleaseAck=entry.Published=false;entry.Started=-1;entry.NextCommand=0;entry.Epoch=null;
        }
        internal static void OnOwnerOutcome(MasterIdolForestGuardLifecycle.State row,string epoch,ForestGuardOwnerAction action,ForestGuardOwnerOutcome outcome,long sender)
        {
            if(!ChangeContext()||!Entries.TryGetValue(row.Id,out var entry)||sender!=row.Owner||!MasterIdolForestGuardLifecycleOwner.Same(entry.Row,row))return;
            var data=ChangeManager.GetZDO(row.Id);
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out var accepted,out var currentEpoch)||currentEpoch!=epoch||!MasterIdolForestGuardLifecycleOwner.Same(row,accepted))return;
            if(action!=Action(entry))return;
            if(outcome==ForestGuardOwnerOutcome.Deferred)
            {
                if(row.Status==MasterIdolForestGuardLifecycle.Phase.Appearing)
                {Transition(entry,MasterIdolForestGuardLifecycle.Phase.Quiescing,true);entry.RetryAt=Time.unscaledTime+2;}
                return;
            }
            if(action==ForestGuardOwnerAction.PrepareAppearance)
            {
                if(outcome==ForestGuardOwnerOutcome.Started&&entry.Started<0)entry.Started=Time.unscaledTime;
                // A terminal ACK may overtake a lost Started packet; never infer a
                // start from completion, retry the same family instead.
                else if(outcome==ForestGuardOwnerOutcome.Completed)
                {
                    if(entry.Started>=0)entry.Completed=true;
                    else {Transition(entry,MasterIdolForestGuardLifecycle.Phase.Quiescing,true);entry.RetryAt=Time.unscaledTime+2;}
                }
                return;
            }
            if(outcome!=ForestGuardOwnerOutcome.Applied)return;
            if(action==ForestGuardOwnerAction.ReleaseActive)entry.ReleaseAck=true;
            else entry.Ack=true;
            entry.Epoch=epoch;
        }
        static ForestGuardOwnerAction Action(Entry entry)
        {
            switch(entry.Row.Status)
            {
                case MasterIdolForestGuardLifecycle.Phase.Quiescing:return ForestGuardOwnerAction.MarkQuiescing;
                case MasterIdolForestGuardLifecycle.Phase.Suspended:return ForestGuardOwnerAction.Suspend;
                case MasterIdolForestGuardLifecycle.Phase.Queued:return ForestGuardOwnerAction.Queue;
                default:return entry.Completed&&entry.Started>=0&&Time.unscaledTime-entry.Started>=10f?ForestGuardOwnerAction.ReleaseActive:ForestGuardOwnerAction.PrepareAppearance;
            }
        }
        static bool Native(Entry entry,ZDO data)=>MasterIdolForestGuardLifecycleOwner.Native(entry.Row,data);
        static bool Canonical(Entry entry,ZDO data)
        {
            var row=entry.Row;var slots=MasterIdolForestGuardState.Slots;
            return data!=null&&ReferenceEquals(ChangeManager.GetZDO(row.Id),data)&&slots!=null&&slots.TryGetValue(MasterIdolForestRules.Families[row.Family],out var record)&&record.Death==0&&MasterIdolForestGuardState.Matches(record,data)&&
                record.Home==row.Home&&data.GetOwner()==row.Owner&&data.OwnerRevision==row.OwnerRevision;
        }
        internal static bool PendingAppearance
        {
            get
            {
                if(!ChangeContext())return false;
                foreach(var entry in Entries.Values)// A different family's unresolved owner/cargo must not block
                // an otherwise valid empty/dead slot birth. Only actual cue work
                // (including asset preparation) serializes the birth scheduler.
                if(entry.WantsActive&&entry.Row.Status==MasterIdolForestGuardLifecycle.Phase.Appearing)return true;
                return false;
            }
        }
        internal static void InitializeBirth(Character actor)
        {
            var view=actor?.m_nview;var data=view?.GetZDO();var net=ZNet.instance;
            if(view?.IsOwner()!=true||data==null||net?.GetWorld()==null||!MasterIdolForestGuardState.ModuleBorn(data))return;
            // Newborns enter the same controller queue before any old birth cue.
            // No change to birth identity, quota or persistent inventory links.
            data.Set(MasterIdolForestGuardLifecycle.WorldKey,net.GetWorldUID());data.Set(MasterIdolForestGuardLifecycle.CycleKey,Math.Max(1,data.GetLong(MasterIdolForestGuardState.BornKey,0)));
            data.Set(MasterIdolForestGuardLifecycle.IntentKey,1L);data.Set(MasterIdolForestGuardLifecycle.AttemptKey,Guid.NewGuid().ToString("N"));
            data.Set(MasterIdolForestGuardLifecycle.PhaseKey,(int)MasterIdolForestGuardLifecycle.Phase.Queued);
            MasterIdolForestGuardLifecycleVisibility.TrackLoaded(actor);
        }
        internal static void Tick()
        {
            MasterIdolForestGuardLifecycleVisibility.Tick();
            if(!ChangeContext()||Time.unscaledTime<NextTick||!MasterIdolForestGuardState.Ready)return;
            NextTick=Time.unscaledTime+.25f;var slots=MasterIdolForestGuardState.Slots;if(slots==null)return;
            var live=new HashSet<ZDOID>();var publish=new List<MasterIdolForestGuardLifecycle.State>(4);
            for(int family=0;family<4;family++)
            {
                if(!slots.TryGetValue(MasterIdolForestRules.Families[family],out var record)||record.Death!=0)continue;
                var data=MasterIdolForestGuardState.Data(record.Uid);
                if(!MasterIdolForestGuardState.Matches(record,data)||!(data.GetFloat(ZDOVars.s_health,1)>0))continue;
                var status=WorkbenchEnchantmentRuntime.ReadForestWardCommitted(record.Home,out var read);
                if(status==ForestWardCommittedStatus.Unknown)continue; // Missing proof is never OFF or renewed authority.
                live.Add(data.m_uid);bool wants=status==ForestWardCommittedStatus.Active;
                if(!Entries.TryGetValue(data.m_uid,out var entry))
                {
                    if(Entries.Count>=4)foreach(var stale in new List<ZDOID>(Entries.Keys))
                    {var old=Entries[stale];if(!slots.TryGetValue(MasterIdolForestRules.Families[old.Row.Family],out var oldRecord)||oldRecord.Death!=0||oldRecord.Uid!=stale.ToString())Entries.Remove(stale);}
                    if(Entries.Count>=4)continue;
                    var row=new MasterIdolForestGuardLifecycle.State{Id=data.m_uid,Family=family,Born=record.Born,Nonce=record.Nonce,Home=record.Home,Owner=data.GetOwner(),OwnerRevision=data.OwnerRevision,
                        Cycle=Math.Max(1,data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0)),Attempt=data.GetString(MasterIdolForestGuardLifecycle.AttemptKey,"")};
                    Sequence=Math.Max(Sequence,Math.Max(read.JournalRevision,data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)));
                    entry=new Entry{Row=row,WantsActive=wants};Entries.Add(row.Id,entry);
                    bool persistedActive=wants&&data.GetLong(MasterIdolForestGuardLifecycle.WorldKey,0)==ChangeWorld&&data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)==1&&
                        data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)>0&&MasterIdolForestRules.Nonce(row.Attempt);
                    if(persistedActive){row.Status=MasterIdolForestGuardLifecycle.Phase.Active;row.IntentRevision=data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0);entry.Row=row;}
                    else Transition(entry,MasterIdolForestGuardLifecycle.Phase.Quiescing,true);
                }
                var current=entry.Row;
                if(current.Owner!=data.GetOwner()||current.OwnerRevision!=data.OwnerRevision||current.Home!=record.Home)
                {
                    current.Owner=data.GetOwner();current.OwnerRevision=data.OwnerRevision;current.Home=record.Home;entry.Row=current;
                    Transition(entry,MasterIdolForestGuardLifecycle.Phase.Quiescing,true);
                }
                bool switched=TryLatestActivation(record.Home,out var change)&&change.Revision>entry.SeenActivation;
                if(switched)
                {
                    entry.SeenActivation=change.Revision;
                    if(change.After=="BlackForest") {current=entry.Row;current.Cycle=change.Revision;entry.Row=current;}
                }
                if(switched||entry.WantsActive!=wants)
                {entry.WantsActive=wants;Transition(entry,MasterIdolForestGuardLifecycle.Phase.Quiescing,true);}
                if(Time.unscaledTime>=entry.RetryAt)
                {
                    if(entry.Row.Status==MasterIdolForestGuardLifecycle.Phase.Quiescing&&entry.Ack&&Native(entry,data))
                    {
                        var before=entry.Row;
                        // This is the actual server cargo settlement API, not an
                        // owner ACK or a local empty-tables approximation.
                        if(MasterIdolResidentCargo.TryQuiesce(data)&&MasterIdolForestGuardLifecycleOwner.Same(before,entry.Row)&&Canonical(entry,data)&&Native(entry,data))
                            Transition(entry,MasterIdolForestGuardLifecycle.Phase.Suspended);
                        else Defer(entry,"cargo-settlement-or-current-witness-wait");
                    }
                    else if(entry.Row.Status==MasterIdolForestGuardLifecycle.Phase.Suspended&&entry.Ack&&Native(entry,data)&&wants)
                        Transition(entry,MasterIdolForestGuardLifecycle.Phase.Queued);
                    else if(entry.Row.Status==MasterIdolForestGuardLifecycle.Phase.Appearing&&entry.Completed&&entry.Started>=0&&Time.unscaledTime-entry.Started>=10f&&entry.ReleaseAck&&Canonical(entry,data))
                    {
                        var active=entry.Row;active.Status=MasterIdolForestGuardLifecycle.Phase.Active;
                        if(MasterIdolForestGuardLifecycleOwner.Native(active,data)){entry.Row=active;entry.Ack=false;entry.Published=false;}
                    }
                }
                publish.Add(entry.Row);
            }
            // Keep UNKNOWN entries in memory for reconciliation, but not in the
            // fresh authority publication. Remove only dead/replaced canonical IDs.
            foreach(var id in new List<ZDOID>(Entries.Keys))
            {
                var entry=Entries[id];var data=ChangeManager.GetZDO(id);
                if(!Canonical(entry,data))Entries.Remove(id);
            }
            bool occupied=false;
            // UNKNOWN proof must not release a still-reserved appearance.
            foreach(var pending in Entries.Values)occupied|=pending.Row.Status==MasterIdolForestGuardLifecycle.Phase.Appearing;
            // Per-family readiness/settlement below remains mandatory. A stalled
            // sibling does not own this family's cargo or presentation lease.
            if(!occupied)
            {
                for(int i=0;i<publish.Count;i++)
                {
                    var entry=Entries[publish[i].Id];
                    if(entry.WantsActive&&entry.Row.Status==MasterIdolForestGuardLifecycle.Phase.Queued&&entry.Ack&&Native(entry,ChangeManager.GetZDO(entry.Row.Id))&&Time.unscaledTime>=entry.RetryAt)
                    {Transition(entry,MasterIdolForestGuardLifecycle.Phase.Appearing);publish[i]=entry.Row;break;}
                }
            }
            if(!MasterIdolForestGuardLifecycle.Commit(publish.ToArray()))return;
            foreach(var row in publish)
            {
                var entry=Entries[row.Id];entry.Published=true;
                if(row.Status==MasterIdolForestGuardLifecycle.Phase.Active||row.Status==MasterIdolForestGuardLifecycle.Phase.Suspended&&entry.Ack||Time.unscaledTime<entry.NextCommand||Time.unscaledTime<entry.RetryAt)continue;
                entry.NextCommand=Time.unscaledTime+1;
                var action=Action(entry);
                if(!MasterIdolForestGuardLifecycleOwner.Request(row,action))Defer(entry,"owner-command-unavailable");
                else if(action==ForestGuardOwnerAction.PrepareAppearance&&entry.Started<0)Defer(entry,"appearance-start-pending");
                else if(action==ForestGuardOwnerAction.ReleaseActive&&!entry.ReleaseAck)Defer(entry,"owner-release-ack-pending");
                else if(action!=ForestGuardOwnerAction.PrepareAppearance&&action!=ForestGuardOwnerAction.ReleaseActive&&!entry.Ack)Defer(entry,"owner-phase-ack-pending");
            }
        }
        internal static void Reset()
        {
            Entries.Clear();Changes.Clear();ChangeSession=null;ChangeManager=null;ChangeGeneration=0;ChangeWorld=0;NextTick=0;Sequence=0;DeferLines=0;
            MasterIdolForestGuardLifecycleOwner.Reset();MasterIdolForestGuardLifecycleVisibility.Reset();
        }
    }
}
