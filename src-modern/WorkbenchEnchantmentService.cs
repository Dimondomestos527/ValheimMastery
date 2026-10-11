using System;
using System.Linq;
namespace ValheimMastery
{
    // Main-thread free grants; unconfirmed grants never participate in effects.
    internal sealed class WorkbenchEnchantmentService
    {
        readonly WorkbenchEnchantmentJournal journal;
        readonly Func<IWorkbenchEnchantmentCooldownAuthority> authority;
        IWorkbenchEnchantmentCooldownAuthority cooldown=>authority?.Invoke();
        internal WorkbenchEnchantmentService(WorkbenchEnchantmentJournal journal,IWorkbenchEnchantmentCooldownAuthority cooldown){this.journal=journal;authority=()=>cooldown;}
        internal WorkbenchEnchantmentService(WorkbenchEnchantmentJournal journal,Func<IWorkbenchEnchantmentCooldownAuthority> authority){this.journal=journal;this.authority=authority;}
        bool Valid(WorkbenchEnchantmentContext c)=>journal.Ready&&c!=null&&c.World==journal.World&&c.Actor!=0&&c.Revalidate?.Invoke()==true&&c.Station!=null&&c.NativeUid!=null;
        static bool Matches(WorkbenchEnchantmentStation b,WorkbenchEnchantmentContext c)=>b!=null&&!b.Removed&&b.Id==c.Station&&b.NativeUid==c.NativeUid;
        internal static bool Usable(WorkbenchEnchantmentGrant g)=>g!=null&&(g.FreeToken.Length==0||g.FreeConfirmed);
        internal static string[] Choices(WorkbenchEnchantmentStation b)=>b.Grants.Where(Usable).Select(g=>g.Choice).ToArray();
        static bool Busy(WorkbenchEnchantmentState s,string id)=>s.Pending.Values.Any(p=>p.Station==id&&p.Kind=="FreeLearn")||s.Stations.TryGetValue(id,out var b)&&b.Grants.Any(g=>g.FreeToken.Length!=0&&!g.FreeConfirmed);
        static bool Used(WorkbenchEnchantmentState s,string op)=>s.Pending.ContainsKey(op)||s.Cancelled.ContainsKey(op)||s.AdditionalOrigins.Values.Any(o=>o.Operation==op)||s.Stations.Values.Any(b=>b.RemovalOperation==op||b.Grants.Any(g=>g.Operation==op));
        internal bool CanLearn(WorkbenchEnchantmentContext c,string choice){var b=journal.ReadStation(c?.Station);return Valid(c)&&Matches(b,c)&&WorkbenchEnchantmentRules.CanLearn(Choices(b),choice,c.NativeLevel);}
        internal WorkbenchEnchantmentSnapshot Snapshot(WorkbenchEnchantmentContext c,WorkbenchCooldownContext clock)
        {
            if(!Valid(c))return null;var b=journal.ReadStation(c.Station);if(!Matches(b,c))return null;
            var learned=Choices(b);if(!WorkbenchEnchantmentRules.TryAvailable(learned,c.NativeLevel,out var available))return null;var p=journal.ReadPending(b.Id);
            string[] ids={"Meadows","BlackForest","Swamp","Mountain","Plains","Mistlands"};var statuses=new WorkbenchEnchantmentChoiceCooldown[6];
            for(int i=0;i<6;i++)
            {
                var v=cooldown?.Query(clock,ids[i]);bool finite=v!=null&&double.IsFinite(v.RemainingSeconds)&&v.RemainingSeconds>=0&&v.RemainingSeconds<=1800;
                statuses[i]=new WorkbenchEnchantmentChoiceCooldown{Choice=ids[i],Ready=finite&&v.Ready&&!v.Pending&&v.RemainingSeconds==0,Pending=!finite||v.Pending,RemainingSeconds=finite?v.RemainingSeconds:0};
                // Recover a reservation whose subsequent station write was interrupted.
                var permit=v?.Permit;
                if(p==null&&permit!=null&&(permit.Phase==WorkbenchCooldownPermitPhase.Reserved||permit.Phase==WorkbenchCooldownPermitPhase.CommitIntent)&&permit.Character==c.Actor&&permit.World==c.World&&permit.Station==c.Station)
                    p=new WorkbenchEnchantmentPending{Operation=permit.Operation,Station=c.Station,Choice=permit.Choice,Kind="FreeLearn",Actor=c.Actor};
            }
            return new WorkbenchEnchantmentSnapshot{World=c.World,Station=b.Id,Revision=journal.Revision,NativeLevel=c.NativeLevel,Learned=learned,Available=available,Active=available.Contains(b.Active)&&p==null?b.Active:"",Reason=p!=null?"pending":"",PendingOperation=p?.Operation??"",PendingChoice=p?.Choice??"",PendingKind=p?.Kind??"",PendingActor=p?.Actor??0,Cooldowns=statuses};
        }
        internal bool RegisterSelected(WorkbenchEnchantmentContext c,string witness)
        {
            if(!Valid(c)||!Guid.TryParseExact(c.Station,"N",out _)||string.IsNullOrEmpty(witness))return false;var s=journal.Snapshot();if(s.Stations.TryGetValue(c.Station,out var b))return Matches(b,c)&&b.NativeWitness==witness;
            return journal.Commit(next=>{if(!Valid(c)||next.Stations.ContainsKey(c.Station)||next.Stations.Values.Any(x=>!x.Removed&&x.NativeUid==c.NativeUid))return false;next.Stations.Add(c.Station,new WorkbenchEnchantmentStation{Id=c.Station,NativeUid=c.NativeUid,NativeWitness=witness});return true;});
        }
        internal bool BindReloadAlias(WorkbenchEnchantmentContext c,string witness,Func<bool> proof)
        {
            return Valid(c)&&BindNativeLoadedAlias(c.World,c.Station,c.NativeUid,witness,proof);
        }
        internal bool BindNativeLoadedAlias(long world,string station,string native,string witness,Func<bool> proof)
        {
            if(!journal.Ready||world!=journal.World||proof==null||string.IsNullOrEmpty(witness)||string.IsNullOrEmpty(native))return false;
            var prior=journal.ReadStation(station);if(prior==null||prior.Removed||prior.NativeWitness!=witness||!proof())return false;if(prior.NativeUid==native)return true;
            return journal.Commit(next=>
            {
                if(!proof()||!next.Stations.TryGetValue(station,out var b)||b.Removed||b.NativeWitness!=witness||next.Stations.Values.Any(x=>!x.Removed&&x.Id!=b.Id&&x.NativeUid==native)||b.ReloadAliases.Count>=256&&!b.ReloadAliases.Contains(b.NativeUid))return false;
                b.ReloadAliases.Add(b.NativeUid);b.ReloadAliases.Remove(native);b.NativeUid=native;return true;
            });
        }
        static bool Tuple(WorkbenchEnchantmentContext c,WorkbenchCooldownPermit p,string op,string choice)=>p!=null&&p.Operation==op&&p.Choice==choice&&p.Character==c.Actor&&p.World==c.World&&p.Station==c.Station&&Guid.TryParseExact(p.Token,"N",out _);
        internal bool Learn(WorkbenchEnchantmentContext c,string op,string choice,WorkbenchCooldownContext clock)
        {
            if(!Valid(c)||!Guid.TryParseExact(op,"N",out _)||!WorkbenchEnchantmentRules.Known(choice))return false;
            var s=journal.Snapshot();if(!s.Stations.TryGetValue(c.Station,out var b)||!Matches(b,c))return false;var same=b.Grants.FirstOrDefault(g=>g.Operation==op);
            if(same!=null){if(same.Choice!=choice||same.Payer!=c.Actor)return false;if(Usable(same))return true;if(clock==null||cooldown==null)return false;var prior=cooldown.Inspect(clock,op,choice)?.Permit;return Tuple(c,prior,op,choice)&&Finish(c,clock,prior,same);}
            if(s.Cancelled.TryGetValue(op,out var cancelled))
            {
                if(clock==null||cooldown==null)return false;
                var oldPermit=cooldown.Inspect(clock,op,choice)?.Permit;
                return Tuple(c,oldPermit,op,choice)&&FinishCancellation(c,clock,oldPermit,cancelled);
            }
            if(b.Grants.Any(g=>g.Choice==choice&&Usable(g)))
            {
                if(clock==null||cooldown==null)return true;
                var unused=cooldown.Inspect(clock,op,choice)?.Permit;
                return unused==null?true:Tuple(c,unused,op,choice)&&CancelUnused(c,clock,unused,WorkbenchCooldownNoGrantKind.NoOp);
            }
            if(clock==null||cooldown==null)return false;
            clock.MayLearn=()=>CanLearn(c,choice);
            WorkbenchCooldownPermit permit;
            if(s.Pending.TryGetValue(op,out var pending)){if(pending.Kind!="FreeLearn"||pending.Actor!=c.Actor||pending.Station!=c.Station||pending.Choice!=choice)return false;permit=cooldown.Inspect(clock,op,choice)?.Permit;if(!Tuple(c,permit,op,choice)||pending.FreeToken!=permit.Token)return false;}
            else
            {
                if(Used(s,op)||Busy(s,b.Id)||!CanLearn(c,choice))return false;permit=cooldown.Reserve(clock,op,choice)?.Permit;if(!Tuple(c,permit,op,choice))return false;
                if(!journal.Commit(next=>{if(!Valid(c)||Used(next,op)||Busy(next,c.Station)||!CanLearn(c,choice))return false;next.Pending.Add(op,new WorkbenchEnchantmentPending{Operation=op,Station=c.Station,Choice=choice,Kind="FreeLearn",Actor=c.Actor,FreeToken=permit.Token,OriginalNativeUid=permit.NativeUid,FreeWitness=permit.NativeWitness});return true;}))return false;
            }
            if(!CanLearn(c,choice))return CancelUnused(c,clock,permit,WorkbenchCooldownNoGrantKind.Cancelled);
            if(!cooldown.PrepareCommit(clock,permit))return false;
            long at=WorkbenchEnchantmentCooldownProtocol.UtcNowTicks;if(at<=0)return false;long ready=checked(at+TimeSpan.FromMinutes(30).Ticks);
            if(!journal.Commit(next=>{if(!Valid(c)||!next.Pending.TryGetValue(op,out var p)||p.Kind!="FreeLearn"||p.FreeToken!=permit.Token||!CanLearn(c,choice))return false;next.Stations[c.Station].Grants.Add(new WorkbenchEnchantmentGrant{Choice=choice,Operation=op,Payer=c.Actor,FreeToken=permit.Token,OriginalNativeUid=permit.NativeUid,FreeWitness=permit.NativeWitness,GrantedAtUtcTicks=at,ReadyAtUtcTicks=ready,FreeRevision=checked(next.Revision+1)});next.Pending.Remove(op);return true;}))return false;
            return Finish(c,clock,permit,journal.ReadStation(c.Station).Grants.First(g=>g.Operation==op));
        }
        bool Finish(WorkbenchEnchantmentContext c,WorkbenchCooldownContext clock,WorkbenchCooldownPermit permit,WorkbenchEnchantmentGrant grant)
        {
            var cert=new WorkbenchCooldownGrantCertificate{Operation=grant.Operation,Token=grant.FreeToken,Choice=grant.Choice,Station=c.Station,OriginalNativeUid=grant.OriginalNativeUid,NativeWitness=grant.FreeWitness,Character=grant.Payer,World=c.World,GrantedAtUtcTicks=grant.GrantedAtUtcTicks,ReadyAtUtcTicks=grant.ReadyAtUtcTicks,WorldRevision=grant.FreeRevision};
            cert.VerifyPersisted=()=>{if(!journal.VerifyDisk())return false;var g=journal.ReadStation(c.Station)?.Grants.FirstOrDefault(x=>x.Operation==grant.Operation);return g!=null&&g.FreeToken==cert.Token&&g.Choice==cert.Choice&&g.Payer==cert.Character&&g.OriginalNativeUid==cert.OriginalNativeUid&&g.FreeWitness==cert.NativeWitness&&g.GrantedAtUtcTicks==cert.GrantedAtUtcTicks&&g.ReadyAtUtcTicks==cert.ReadyAtUtcTicks&&g.FreeRevision==cert.WorldRevision;};
            if(!cooldown.Complete(clock,permit,cert))return false;
            return journal.Commit(next=>{if(!Valid(c)||!next.Stations.TryGetValue(c.Station,out var b))return false;var g=b.Grants.FirstOrDefault(x=>x.Operation==grant.Operation);if(g==null||g.FreeToken!=permit.Token)return false;g.FreeConfirmed=true;return true;});
        }
        bool CancelUnused(WorkbenchEnchantmentContext c,WorkbenchCooldownContext clock,WorkbenchCooldownPermit permit,WorkbenchCooldownNoGrantKind kind)
        {
            if(!journal.Commit(next=>
            {
                if(!Valid(c)||next.Stations[c.Station].Grants.Any(g=>g.Operation==permit.Operation)||next.Cancelled.ContainsKey(permit.Operation))return false;
                if(!next.Pending.TryGetValue(permit.Operation,out var p))p=new WorkbenchEnchantmentPending{Operation=permit.Operation,Station=permit.Station,Choice=permit.Choice,Actor=permit.Character,FreeToken=permit.Token,OriginalNativeUid=permit.NativeUid,FreeWitness=permit.NativeWitness};
                if(p.FreeToken!=permit.Token||p.Actor!=c.Actor||p.Station!=c.Station||p.Choice!=permit.Choice)return false;
                p.Kind=kind==WorkbenchCooldownNoGrantKind.NoOp?"FreeNoOp":"FreeCancelled";p.FreeRevision=checked(next.Revision+1);next.Cancelled.Add(p.Operation,p);next.Pending.Remove(p.Operation);return true;
            }))return false;
            return FinishCancellation(c,clock,permit,journal.ReadCancellation(permit.Operation));
        }
        bool FinishCancellation(WorkbenchEnchantmentContext c,WorkbenchCooldownContext clock,WorkbenchCooldownPermit permit,WorkbenchEnchantmentPending result)
        {
            if(result.FreeToken!=permit.Token||result.Actor!=c.Actor||result.Station!=c.Station||result.Choice!=permit.Choice||result.Kind!="FreeCancelled"&&result.Kind!="FreeNoOp")return false;
            var kind=result.Kind=="FreeNoOp"?WorkbenchCooldownNoGrantKind.NoOp:WorkbenchCooldownNoGrantKind.Cancelled;
            var cert=new WorkbenchCooldownNoGrantCertificate{Operation=permit.Operation,Token=permit.Token,Choice=permit.Choice,Station=permit.Station,OriginalNativeUid=permit.NativeUid,NativeWitness=permit.NativeWitness,Character=permit.Character,World=permit.World,WorldRevision=result.FreeRevision,Kind=kind};
            cert.VerifyPersisted=()=>journal.VerifyDisk()&&journal.ReadCancellation(permit.Operation) is WorkbenchEnchantmentPending p&&p.FreeToken==cert.Token&&p.Kind==result.Kind&&p.Actor==cert.Character&&p.Station==cert.Station&&p.Choice==cert.Choice&&p.OriginalNativeUid==cert.OriginalNativeUid&&p.FreeWitness==cert.NativeWitness&&p.FreeRevision==cert.WorldRevision;
            return cooldown.Cancel(clock,permit,cert)&&kind==WorkbenchCooldownNoGrantKind.NoOp;
        }
        static WorkbenchCooldownPermit CopyPermit(WorkbenchCooldownPermit p)=>p==null?null:new WorkbenchCooldownPermit{Operation=p.Operation,Token=p.Token,Choice=p.Choice,Station=p.Station,NativeUid=p.NativeUid,NativeWitness=p.NativeWitness,Character=p.Character,World=p.World,Generation=p.Generation,Phase=p.Phase};
        internal bool VerifyPersistedOperation(WorkbenchCooldownPermit original)
        {
            var p=CopyPermit(original);if(p==null||p.World!=journal.World||!journal.VerifyDisk())return false;
            var b=journal.ReadStation(p.Station);if(b==null||b.NativeWitness!=p.NativeWitness)return false;
            var g=b.Grants.FirstOrDefault(x=>x.Operation==p.Operation);
            if(g!=null)return g.FreeToken==p.Token&&g.Choice==p.Choice&&g.Payer==p.Character&&g.OriginalNativeUid==p.NativeUid&&g.FreeWitness==p.NativeWitness&&g.FreeRevision>0&&g.GrantedAtUtcTicks>0&&g.ReadyAtUtcTicks==g.GrantedAtUtcTicks+TimeSpan.FromMinutes(30).Ticks;
            var result=journal.ReadCancellation(p.Operation);return result!=null&&(result.Kind=="FreeCancelled"||result.Kind=="FreeNoOp")&&result.FreeToken==p.Token&&result.Actor==p.Character&&result.Station==p.Station&&result.Choice==p.Choice&&result.OriginalNativeUid==p.NativeUid&&result.FreeWitness==p.NativeWitness&&result.FreeRevision>0;
        }
        // Previous-epoch eligibility belongs to Gold, never bench absence or a timeout.
        internal bool PrepareRetiredNoGrant(WorkbenchEnchantmentContext c,WorkbenchCooldownPermit original,Func<WorkbenchCooldownPermit,bool> isRetired)
        {
            var p=CopyPermit(original);
            bool Retired(){try{return isRetired?.Invoke(CopyPermit(p))==true;}catch{return false;}}
            if(p==null||!Valid(c)||!Tuple(c,p,p.Operation,p.Choice)||!Retired()||!journal.VerifyDisk())return false;
            return journal.Commit(next=>
            {
                if(!Valid(c)||!Retired()||!Valid(c)||!next.Stations.TryGetValue(c.Station,out var b)||b.NativeWitness!=p.NativeWitness||next.Stations.Values.Any(x=>x.Grants.Any(g=>g.Operation==p.Operation))||next.Cancelled.ContainsKey(p.Operation))return false;
                if(next.Pending.TryGetValue(p.Operation,out var pending))
                {
                    if(pending.Kind!="FreeLearn"||pending.FreeToken!=p.Token||pending.Actor!=p.Character||pending.Station!=p.Station||pending.Choice!=p.Choice||pending.OriginalNativeUid!=p.NativeUid||pending.FreeWitness!=p.NativeWitness)return false;
                }
                else if(Used(next,p.Operation)||b.NativeUid!=p.NativeUid&&!b.ReloadAliases.Contains(p.NativeUid))return false;
                next.Cancelled.Add(p.Operation,new WorkbenchEnchantmentPending{Operation=p.Operation,Station=p.Station,Choice=p.Choice,Actor=p.Character,Kind="FreeCancelled",FreeToken=p.Token,OriginalNativeUid=p.NativeUid,FreeWitness=p.NativeWitness,FreeRevision=checked(next.Revision+1)});
                next.Pending.Remove(p.Operation);return true;
            });
        }
        // Settlement changes only the original result/character cooldown. No native attachment.
        internal bool RecoverPersistedResult(WorkbenchEnchantmentContext c,WorkbenchCooldownContext clock,WorkbenchCooldownPermit original)
        {
            var p=CopyPermit(original);if(p==null||!Valid(c)||clock==null||clock.MayLearn?.Invoke()!=false||cooldown==null||!Tuple(c,p,p.Operation,p.Choice)||!VerifyPersistedOperation(p))return false;
            var inspected=cooldown.Inspect(clock,p.Operation,p.Choice)?.Permit;
            if(!Tuple(c,inspected,p.Operation,p.Choice)||inspected.Token!=p.Token||inspected.NativeUid!=p.NativeUid||inspected.NativeWitness!=p.NativeWitness)return false;
            var b=journal.ReadStation(c.Station);var grant=b.Grants.FirstOrDefault(g=>g.Operation==p.Operation);
            if(grant!=null)return Finish(c,clock,inspected,grant);
            var result=journal.ReadCancellation(p.Operation);if(result==null)return false;FinishCancellation(c,clock,inspected,result);
            return cooldown.Inspect(clock,p.Operation,p.Choice)?.Status==WorkbenchCooldownStatus.Cancelled;
        }
        internal bool Switch(WorkbenchEnchantmentContext c,string choice,Func<string,string,WorkbenchEnchantmentState,bool> networkAllows)
        {
            if(!Valid(c)||choice==null||networkAllows==null)return false;var s=journal.Snapshot();if(!s.Stations.TryGetValue(c.Station,out var b)||!Matches(b,c)||Busy(s,b.Id))return false;if(!WorkbenchEnchantmentRules.TryAvailable(Choices(b),c.NativeLevel,out var available)||choice.Length!=0&&!available.Contains(choice))return false;if(b.Active==choice)return true;
            return journal.Commit(next=>{if(!Valid(c)||Busy(next,c.Station)||!Matches(next.Stations[c.Station],c)||!WorkbenchEnchantmentRules.TryAvailable(Choices(next.Stations[c.Station]),c.NativeLevel,out var current)||choice.Length!=0&&!current.Contains(choice)||!networkAllows(c.Station,choice,next))return false;next.Stations[c.Station].Active=choice;return true;});
        }
        internal bool DestroyVerified(long world,string station,string nativeUid,Func<bool> proof)
        {
            if(!journal.Ready||journal.World!=world||proof==null)return false;return journal.Commit(next=>{if(!proof()||!next.Stations.TryGetValue(station,out var b)||b.NativeUid!=nativeUid)return false;if(b.Removed)return true;b.Removed=true;b.Active="";b.RemovalKind="Destroy";b.RemovalOperation=Guid.NewGuid().ToString("N");b.RemovalActor=0;return true;});
        }
        // Recovery-only authenticated context plus persisted native destruction.
        // Never Reserve/Prepare, create a grant, spend, or grant access here.
        internal bool RecoverRemoved(WorkbenchEnchantmentContext c,WorkbenchCooldownContext clock,string op,string choice)
        {
            if(!Valid(c)||clock==null||clock.MayLearn?.Invoke()!=false||cooldown==null)return false;
            var b=journal.ReadStation(c.Station);
            if(b?.Removed!=true||b.RemovalKind!="Destroy"||b.NativeUid!=c.NativeUid||b.NativeWitness!=clock.NativeWitness||!Guid.TryParseExact(b.RemovalOperation,"N",out _))return false;
            var permit=cooldown.Inspect(clock,op,choice)?.Permit;
            if(!Tuple(c,permit,op,choice))return false;
            var grant=b.Grants.FirstOrDefault(g=>g.Operation==op);
            if(grant!=null)return grant.FreeToken==permit.Token&&grant.Payer==c.Actor&&grant.Choice==choice&&Finish(c,clock,permit,grant);
            var s=journal.Snapshot();
            if(s.Cancelled.TryGetValue(op,out var result)){FinishCancellation(c,clock,permit,result);return false;}
            return CancelUnused(c,clock,permit,WorkbenchCooldownNoGrantKind.Cancelled);
        }
    }
}


