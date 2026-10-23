using System;
using UnityEngine;
namespace ValheimMastery
{
    internal enum ResidentCargoDrainStatus { Pending,Ready,Rejected }
    internal enum ResidentCargoDrainReason
    {
        None,IntentUnknown,WrongRole,StaleWitness,ContextNotReady,BodyUnknown,
        IdentityConflict,Opening,Executing,StoreUnresolved,StoreBinding,
        ContainerLoading,NativeStateUnknown,OpenPeer,InUse,ManualLease,
        AwaitOwner,PendingOperation,SaveFailed,NotPersistent
    }
    internal static partial class MasterIdolResidentCargo
    {
        // Pure consumer of Utility's authenticated current-context snapshot.
        // In particular, neither caller ownership nor server-only Slots is required here.
        internal static bool CanOperate(ZDO body)=>body!=null&&MasterIdolForestGuardLifecycle.CanOperate(body);
        // V2 consumes only the provider's body and current-witness verdict. It
        // never manufactures mode/phase truth, native marks or retirement.
        internal static ResidentCargoDrainStatus TryDrainForDormancy(in ForestGuardDrainWitness expected,out ResidentCargoDrainReason reason)
        {
            reason=ResidentCargoDrainReason.None;
            if(ZNet.instance?.IsServer()!=true){reason=ResidentCargoDrainReason.WrongRole;return ResidentCargoDrainStatus.Rejected;}
            var intent=MasterIdolForestGuardLifecycle.StillCurrentCargoQuiesce(in expected);
            if(intent==ForestGuardDrainIntent.Unknown){reason=ResidentCargoDrainReason.IntentUnknown;return ResidentCargoDrainStatus.Pending;}
            if(intent!=ForestGuardDrainIntent.CurrentDormant){reason=ResidentCargoDrainReason.StaleWitness;return ResidentCargoDrainStatus.Rejected;}
            if(!Context()||!Complete||!MasterIdolWorldIndex.Ready||!MasterIdolForestGuardState.Ready)
            {reason=ResidentCargoDrainReason.ContextNotReady;return ResidentCargoDrainStatus.Pending;}
            var body=expected.Body;
            if(body==null){reason=ResidentCargoDrainReason.BodyUnknown;return ResidentCargoDrainStatus.Pending;}
            if(!ReferenceEquals(Manager.GetZDO(body.m_uid),body)||!MasterIdolForestGuardState.ModuleBorn(body))
            {reason=ResidentCargoDrainReason.IdentityConflict;return ResidentCargoDrainStatus.Rejected;}
            int family=MasterIdolForestGuardState.Family(body);string type=MasterIdolForestRules.Families[family];
            var slots=MasterIdolForestGuardState.Slots;
            if(slots==null){reason=ResidentCargoDrainReason.ContextNotReady;return ResidentCargoDrainStatus.Pending;}
            if(!slots.TryGetValue(type,out var record)||record.Death!=0||!MasterIdolForestGuardState.Matches(record,body)||!ResidentCargoRules.Living(body.GetFloat(ZDOVars.s_health,1)))
            {reason=ResidentCargoDrainReason.IdentityConflict;return ResidentCargoDrainStatus.Rejected;}
            var scene=ZNetScene.instance;
            if(scene==null){reason=ResidentCargoDrainReason.NativeStateUnknown;return ResidentCargoDrainStatus.Pending;}
            if(Opening(body)){reason=ResidentCargoDrainReason.Opening;return ResidentCargoDrainStatus.Pending;}
            var cargo=Store(family);
            if(Executing.ContainsKey(body.m_uid)||cargo!=null&&ExecutorLocked(cargo.m_uid))
            {reason=ResidentCargoDrainReason.Executing;return ResidentCargoDrainStatus.Pending;}
            if(cargo!=null)
            {
                if(body.GetZDOID(Link)!=cargo.m_uid){reason=ResidentCargoDrainReason.StoreBinding;return ResidentCargoDrainStatus.Pending;}
                var instance=scene.FindInstance(cargo.m_uid);var box=instance!=null?instance.GetComponentInChildren<Container>(true):null;
                if(box==null&&instance!=null){reason=ResidentCargoDrainReason.NativeStateUnknown;return ResidentCargoDrainStatus.Pending;}
                if(box!=null&&box.m_loading){reason=ResidentCargoDrainReason.ContainerLoading;return ResidentCargoDrainStatus.Pending;}
            }
            bool settled;
            try
            {
                var instance=scene.FindInstance(body.m_uid);var actor=instance!=null?instance.GetComponent<Character>():null;
                if(actor!=null)actor.GetComponent<MasterIdolResidentCargoAgent>()?.Stop();
                settled=TrySettleCargo(body,"Dormancy",out cargo,out reason);
            }
            catch(Exception){reason=ResidentCargoDrainReason.SaveFailed;return ResidentCargoDrainStatus.Pending;}
            intent=MasterIdolForestGuardLifecycle.StillCurrentCargoQuiesce(in expected);
            if(intent==ForestGuardDrainIntent.Unknown){reason=ResidentCargoDrainReason.IntentUnknown;return ResidentCargoDrainStatus.Pending;}
            if(intent!=ForestGuardDrainIntent.CurrentDormant){reason=ResidentCargoDrainReason.StaleWitness;return ResidentCargoDrainStatus.Rejected;}
            if(!ReferenceEquals(ZNetScene.instance,scene)||!Complete||!MasterIdolWorldIndex.Ready||!MasterIdolForestGuardState.Ready)
            {reason=ResidentCargoDrainReason.ContextNotReady;return ResidentCargoDrainStatus.Pending;}
            slots=MasterIdolForestGuardState.Slots;
            if(slots==null){reason=ResidentCargoDrainReason.ContextNotReady;return ResidentCargoDrainStatus.Pending;}
            if(!slots.TryGetValue(type,out var current)||current.Death!=0||!MasterIdolForestGuardState.Matches(current,body)||!ResidentCargoRules.Living(body.GetFloat(ZDOVars.s_health,1)))
            {reason=ResidentCargoDrainReason.IdentityConflict;return ResidentCargoDrainStatus.Rejected;}
            if(!settled)return ResidentCargoDrainStatus.Pending;
            if(!ReferenceEquals(Store(family),cargo)||(cargo==null?body.GetZDOID(Link)!=ZDOID.None:body.GetZDOID(Link)!=cargo.m_uid))
            {reason=ResidentCargoDrainReason.StoreBinding;return ResidentCargoDrainStatus.Pending;}
            reason=ResidentCargoDrainReason.None;return ResidentCargoDrainStatus.Ready;
        }
        private const string LifecycleWorldKey="vm.bf.lifecycle.world.v1",LifecycleCycleKey="vm.bf.lifecycle.cycle.v1",LifecyclePhaseKey="vm.bf.lifecycle.phase.v1";
        // Called by the lifecycle SERVER manager after owner applied phase2.
        // The remote owner acknowledges state application, not global cargo quiescence.
        internal static bool TryQuiesce(ZDO body)
        {
            if(!Context()||!Session.IsServer()||!Complete||!MasterIdolWorldIndex.Ready||!MasterIdolForestGuardState.Ready||
                body==null||!ReferenceEquals(Manager.GetZDO(body.m_uid),body)||!MasterIdolForestGuardState.ModuleBorn(body)||
                CanOperate(body)||body.GetLong(LifecycleWorldKey,0)!=World||body.GetLong(LifecycleCycleKey,0)<=0||body.GetInt(LifecyclePhaseKey,0)!=2)return false;
            int family=MasterIdolForestGuardState.Family(body);string type=MasterIdolForestRules.Families[family];
            if(!MasterIdolForestGuardState.Slots.TryGetValue(type,out var record)||record.Death!=0||!MasterIdolForestGuardState.Matches(record,body)||!ResidentCargoRules.Living(body.GetFloat(ZDOVars.s_health,1)))return false;
            var net=Session;var manager=Manager;var scene=ZNetScene.instance;long world=World,cycle=body.GetLong(LifecycleCycleKey,0),owner=body.GetOwner(),born=body.GetLong(MasterIdolForestGuardState.BornKey,0);ushort revision=body.OwnerRevision;ulong generation=Generation;
            string nonce=body.GetString(MasterIdolForestGuardState.NonceKey,""),home=body.GetString(MasterIdolNativeTaming.HomeKey,"");
            if(scene==null||Opening(body))return false;
            var actorInstance=scene.FindInstance(body.m_uid);var actor=actorInstance!=null?actorInstance.GetComponent<Character>():null;
            if(actor!=null)actor.GetComponent<MasterIdolResidentCargoAgent>()?.Stop();
            var cargo=Store(family);
            if(Executing.ContainsKey(body.m_uid)||cargo!=null&&ExecutorLocked(cargo.m_uid))return false;
            if(cargo!=null)
            {
                if(body.GetZDOID(Link)!=cargo.m_uid)return false;
                var boxInstance=scene.FindInstance(cargo.m_uid);var box=boxInstance!=null?boxInstance.GetComponentInChildren<Container>(true):null;
                if(box==null&&boxInstance!=null||box!=null&&box.m_loading)return false;
            }
            // Existing protected path requests current-owner saved-data consent,
            // waits on leases/Pending, and never interrupts a synchronous commit.
            // "Suspension" takes the nondeath path: no body destruction/identity reset.
            try{if(!BeforeRemoval(body,"Suspension")){ResidentCargoOpenTrace.WriteAuto("quiesce-settlement-wait",family);return false;}}
            catch(Exception){ResidentCargoOpenTrace.WriteAuto("quiesce-save-failed",family);return false;}
            return ReferenceEquals(Session,net)&&ReferenceEquals(ZNet.instance,net)&&ReferenceEquals(Manager,manager)&&ReferenceEquals(ZDOMan.instance,manager)&&
                ReferenceEquals(ZNetScene.instance,scene)&&Generation==generation&&MasterIdolLoadIdentity.Generation==generation&&MasterIdolLoadIdentity.LoadReady&&
                net.GetWorld()!=null&&net.GetWorldUID()==world&&ReferenceEquals(manager.GetZDO(body.m_uid),body)&&body.GetOwner()==owner&&body.OwnerRevision==revision&&
                body.GetLong(LifecycleWorldKey,0)==world&&body.GetLong(LifecycleCycleKey,0)==cycle&&body.GetInt(LifecyclePhaseKey,0)==2&&
                body.GetLong(MasterIdolForestGuardState.BornKey,0)==born&&body.GetString(MasterIdolForestGuardState.NonceKey,"")==nonce&&body.GetString(MasterIdolNativeTaming.HomeKey,"")==home&&!Opening(body)&&
                (cargo==null?body.GetZDOID(Link)==ZDOID.None:body.GetZDOID(Link)==cargo.m_uid)&&
                MasterIdolForestGuardState.Slots.TryGetValue(type,out var current)&&current.Death==0&&MasterIdolForestGuardState.Matches(current,body);
        }
    }
}
