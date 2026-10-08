using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterIdolFavor
    {
        internal static GoldCraftingLedger Ledger => MasterIdolPlacement.CurrentLedger;
        private static long World => ZNet.instance?.GetWorldUID()??0;
        private static bool Success(GoldIdolStatus status)=>status==GoldIdolStatus.Accepted||status==GoldIdolStatus.Duplicate;
        internal static bool Buy(long payer,string token,string type)
        {
            var ledger=Ledger;if(ledger==null)return false;
            if(!Success(ledger.BuyIdol(World,payer,token,type)))return false;
            var paid=ledger.FindIdol(World,payer,token,type);
            if(paid?.Phase!=GoldIdolPhase.Paid)return false;
            GoldCraftingService.NotifyFavor(payer);return true;
        }
        internal static bool Bind(MasterIdolAdmission entry)
        {
            var ledger=Ledger;if(ledger==null||entry==null)return false;
            var paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            if(paid==null)return !entry.RequiresFavor; // Only explicitly migrated material-only admissions are free.
            bool ok=Success(ledger.BindIdol(World,entry.Player,entry.Token,entry.Type,entry.Output,new Evidence()));
            paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            return ok&&paid?.Phase==GoldIdolPhase.Bound&&paid.Output==entry.Output;
        }
        internal static bool Allows(MasterIdolAdmission entry)
        {
            var ledger=Ledger;if(ledger==null||entry==null||entry.Dismantling)return false;
            var paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            return paid==null?!entry.RequiresFavor:paid.Phase==GoldIdolPhase.Bound&&paid.Output==entry.Output;
        }
        internal static bool Cancel(MasterIdolAdmission entry)
        {
            var ledger=Ledger;if(ledger==null||entry==null)return false;
            var paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            if(paid==null)return !entry.RequiresFavor;
            if(paid.Phase==GoldIdolPhase.Cancelled)return true;
            var status=ledger.CancelIdol(World,entry.Player,entry.Token,entry.Type,new Evidence());
            if(status==GoldIdolStatus.Accepted)GoldCraftingService.NotifyFavor(entry.Player);return Success(status);
        }
        internal static bool Begin(MasterIdolAdmission entry,WorkshopActor actor)
        {
            var ledger=Ledger;if(ledger==null||entry==null)return false;
            var paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            if(paid==null)return !entry.RequiresFavor;
            // A restored world output of an already refunded creation can be removed, never reactivated/refunded twice.
            if(paid.Phase==GoldIdolPhase.Refunded&&paid.Output==entry.Output)return true;
            bool ok=Success(ledger.BeginIdolDismantle(World,entry.Player,entry.Token,entry.Type,entry.Output,new Evidence(actor)));
            paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            return ok&&paid?.Phase==GoldIdolPhase.Dismantling&&paid.Output==entry.Output;
        }
        private static MasterIdolAdmissionStore AuditStore;private static GoldCraftingLedger AuditLedger;private static int Cursor;
        private static readonly Queue<string> Urgent=new Queue<string>();private static readonly HashSet<string> Queued=new HashSet<string>(StringComparer.Ordinal);
        internal static void Reconcile()
        {
            var ledger=Ledger;var store=MasterIdolWorldRegistry.Store;if(ledger==null||store?.Ready!=true)return;using var scope=new MasterIdolPerf.Scope("favor.reconcile");
            if(!ReferenceEquals(AuditStore,store)||!ReferenceEquals(AuditLedger,ledger)){AuditStore=store;AuditLedger=ledger;Cursor=0;Urgent.Clear();Queued.Clear();}
            foreach(string token in store.TakeChanges())if(Queued.Add(token))Urgent.Enqueue(token);
            bool rebound=false;int count=Math.Min(64,Urgent.Count);for(int i=0;i<count;i++){string token=Urgent.Dequeue();Queued.Remove(token);if(!ReconcileOne(store.Find(token),ledger,ref rebound)&&Queued.Add(token))Urgent.Enqueue(token);}
            var ids=store.Tokens();for(int i=0;i<Math.Min(32,ids.Length);i++){if(Cursor>=ids.Length)Cursor=0;string token=ids[Cursor++];if(!ReconcileOne(store.Find(token),ledger,ref rebound)&&Queued.Add(token))Urgent.Enqueue(token);}
            if(rebound)MasterIdolWorldRegistry.Refresh(true);
        }
        private static bool ReconcileOne(MasterIdolAdmission entry,GoldCraftingLedger ledger,ref bool rebound)
        {
            if(entry==null)return true;var paid=ledger.FindIdol(World,entry.Player,entry.Token,entry.Type);
            if(entry.Applied&&!entry.Closed&&!entry.Dismantling){if(paid?.Phase==GoldIdolPhase.Paid){bool bound=Bind(entry);rebound|=bound;return bound;}return true;}
            if(entry.Closed&&!entry.Applied)return paid==null||Cancel(entry);
            if(!entry.Closed||!entry.Dismantling||!entry.Removed||paid?.Phase!=GoldIdolPhase.Dismantling)return true;
            var result=ledger.RefundIdol(World,entry.Player,entry.Token,entry.Type,entry.Output,new Evidence());if(result==GoldIdolStatus.Accepted)GoldCraftingService.NotifyFavor(entry.Player);return Success(result);
        }
        private sealed class Evidence:IGoldIdolEvidence
        {
            private readonly ZNet Session=ZNet.instance;
            private readonly WorkshopActor Actor;
            internal Evidence(WorkshopActor actor=null){Actor=actor;}
            public bool Verify(GoldIdolPurchase frozen,GoldIdolEvidenceKind kind)
            {
                if(Session==null||Session!=ZNet.instance||!Session.IsServer()||Session.GetWorldUID()!=frozen.World)return false;
                var store=MasterIdolWorldRegistry.Store;var entry=store?.Find(frozen.Token);
                if(store?.Ready!=true||entry==null||entry.Player!=frozen.Payer||entry.Type!=frozen.Type)return false;
                if(kind==GoldIdolEvidenceKind.CancelUnstarted)return entry.Closed&&!entry.Applied&&!entry.Dismantling;
                if(entry.Output!=frozen.Output||!entry.Applied)return false;
                var zdo=MasterIdolLoadIdentity.Resolve(entry);
                if(kind==GoldIdolEvidenceKind.Destroyed)return entry.Closed&&entry.Dismantling&&entry.Removed&&MasterIdolLoadIdentity.TokenAbsent(entry.Token);
                if(entry.Closed||zdo==null||MasterIdolWorldRegistry.Profile(zdo.GetPrefab())?.Id!=entry.Type||
                    zdo.GetString(MasterIdolWorldRegistry.TokenKey,"")!=entry.Token||zdo.GetLong(ZDOVars.s_creator,0)!=entry.Player)return false;
                if(kind==GoldIdolEvidenceKind.BindOutput)return !entry.Dismantling;
                return kind==GoldIdolEvidenceKind.BeginDismantle&&entry.Dismantling&&Actor?.Available==true&&!Actor.IsDead()&&!Actor.IsTeleporting()&&
                    Vector3.Distance(Actor.EyePoint,zdo.GetPosition())<=Actor.PlaceDistance+1f&&MasterIdolWorldIndex.WardAllows(Actor.GetPlayerID(),zdo.GetPosition());
            }
        }
    }
}
