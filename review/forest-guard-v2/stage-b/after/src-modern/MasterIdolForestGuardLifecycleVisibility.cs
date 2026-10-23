using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // At most four native bodies. Never destroys a body/ZDO or touches cargo.
    // Native OnDisable does not remove the target-search registries, so only
    // references actually removed by this claim are eligible for restoration.
    internal static class MasterIdolForestGuardLifecycleVisibility
    {
        sealed class Body
        {
            internal Character Actor;internal BaseAI Ai;internal ZNetView View;internal ZDO Data;
            internal ZNet Net;internal ZDOMan Manager;internal ulong Generation;internal long World,Born;internal string Nonce;
            internal bool Held,RemovedCharacter,RemovedAi,PhysicsHeld,ColliderHeld,ColliderEnabled;
            internal Collider Collider;
            internal Rigidbody Physics;
            internal RigidbodyConstraints Constraints;
            internal readonly List<Renderer> Renderers=new List<Renderer>(8);
            internal readonly List<bool> Enabled=new List<bool>(8);
        }
        static readonly Dictionary<Character,Body> Bodies=new Dictionary<Character,Body>();
        static float Next;
        static bool Current(Body body)=>MasterIdolForestGuardDiagnostic.Check(body!=null,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-body-missing")&&MasterIdolForestGuardDiagnostic.Check(body.Actor!=null,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-actor-missing")&&MasterIdolForestGuardDiagnostic.Check(body.View!=null,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-view-missing")&&MasterIdolForestGuardDiagnostic.Check(body.View.IsValid(),body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-view-invalid")&&
            MasterIdolForestGuardDiagnostic.Check(body.Net==ZNet.instance&&ReferenceEquals(ZNetScene.instance?.FindInstance(body.Data.m_uid)?.GetComponent<Character>(),body.Actor),body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-session-changed")&&MasterIdolForestGuardDiagnostic.Check(body.Manager==ZDOMan.instance,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-manager-changed")&&MasterIdolForestGuardDiagnostic.Check(body.Generation==MasterIdolLoadIdentity.Generation,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-generation-changed")&&
            MasterIdolForestGuardDiagnostic.Check(body.Net?.GetWorld()!=null,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-world-missing")&&MasterIdolForestGuardDiagnostic.Check(body.World==body.Net.GetWorldUID(),body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-world-changed")&&MasterIdolForestGuardDiagnostic.Check(ReferenceEquals(body.View.GetZDO(),body.Data),body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-view-zdo-changed")&&
            MasterIdolForestGuardDiagnostic.Check(ReferenceEquals(body.Manager.GetZDO(body.Data.m_uid),body.Data),body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-canonical-zdo-changed")&&MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardState.ModuleBorn(body.Data),body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-not-module-born")&&
            MasterIdolForestGuardDiagnostic.Check(body.Data.GetLong(MasterIdolForestGuardState.BornKey,0)==body.Born,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-born-changed")&&MasterIdolForestGuardDiagnostic.Check(body.Data.GetString(MasterIdolForestGuardState.NonceKey,"")==body.Nonce,body?.Actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"current-nonce-changed");
        static Body Get(Character actor)
        {
            if(MasterIdolForestGuardDiagnostic.Reject(actor==null,actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"actor-missing")||MasterIdolForestGuardDiagnostic.Reject(actor.IsDead(),actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"actor-dead")||MasterIdolForestGuardDiagnostic.Reject(actor.m_nview?.IsValid()!=true,actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"view-not-valid")||MasterIdolForestGuardDiagnostic.Reject(ZNet.instance?.GetWorld()==null,actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"world-missing"))return null;
            var data=actor.m_nview.GetZDO();if(!MasterIdolForestGuardDiagnostic.Check(ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data),actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"canonical-zdo-mismatch")||!MasterIdolForestGuardDiagnostic.Check(MasterIdolForestGuardState.ModuleBorn(data),actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"not-module-born"))return null;
            if(!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(data,out _,out _))return null;
            if(Bodies.TryGetValue(actor,out var old))return MasterIdolForestGuardDiagnostic.Check(Current(old),actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"cached-body-stale")?old:null;
            if(MasterIdolForestGuardDiagnostic.Reject(Bodies.Count>=4,actor,MasterIdolForestGuardDiagnostic.Lane.Stage,"body-capacity"))return null;
            var body=new Body{Actor=actor,Ai=actor.GetBaseAI(),View=actor.m_nview,Data=data,Net=ZNet.instance,Manager=ZDOMan.instance,
                Generation=MasterIdolLoadIdentity.Generation,World=ZNet.instance.GetWorldUID(),Born=data.GetLong(MasterIdolForestGuardState.BornKey,0),Nonce=data.GetString(MasterIdolForestGuardState.NonceKey,"")};
            Bodies.Add(actor,body);return body;
        }
        static void RemoveTargets(Body body)
        {
            if(!Current(body))return;
            body.RemovedCharacter|=Character.GetAllCharacters().Remove(body.Actor);
            var ai=body.Actor.GetBaseAI();
            if(!ReferenceEquals(body.Ai,ai)){body.Ai=ai;body.RemovedAi=false;}
            if(body.Ai!=null)body.RemovedAi|=BaseAI.GetAllInstances().Remove(body.Ai);
        }
        static void HoldVisual(Body body)
        {
            if(body.Renderers.Count==0&&body.Actor.m_visual!=null)
                foreach(var renderer in body.Actor.m_visual.GetComponentsInChildren<Renderer>(true))
                {body.Renderers.Add(renderer);body.Enabled.Add(renderer.enabled);}
            foreach(var renderer in body.Renderers)if(OwnRenderer(body,renderer))renderer.enabled=false;
        }
        static bool OwnRenderer(Body body,Renderer renderer)=>renderer!=null&&body.Actor.m_visual!=null&&
            (renderer.transform==body.Actor.m_visual.transform||renderer.transform.IsChildOf(body.Actor.m_visual.transform));
        static void RestoreVisual(Body body)
        {
            for(int i=0;i<body.Renderers.Count;i++)if(OwnRenderer(body,body.Renderers[i])&&!body.Renderers[i].enabled)body.Renderers[i].enabled=body.Enabled[i];
        }
        internal static bool NativeReady(Character actor)
        {
            var view=actor?.m_nview;var data=view?.GetZDO();var ai=actor!=null?actor.GetBaseAI():null;
            return actor!=null&&!actor.IsDead()&&view?.IsValid()==true&&ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)&&
                ai!=null&&ReferenceEquals(ai.m_nview,view)&&ReferenceEquals(ai.m_character,actor)&&ai.m_nview.IsValid()&&ReferenceEquals(ai.m_nview.GetZDO(),data)&&
                actor.m_body!=null&&ai.m_animator!=null&&ai.m_animator.m_animator!=null&&ReferenceEquals(ai.m_animator.m_nview,view);
        }
        static bool Accepted(Body body,MasterIdolForestGuardLifecycle.State expected,string epoch,bool owner)
        {
            if(!Current(body)||!NativeReady(body.Actor)||!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(body.Data,out var row,out var currentEpoch)||
                currentEpoch!=epoch||!MasterIdolForestGuardLifecycle.SameIntent(row,expected))return false;
            if(owner)return body.View.IsOwner();
            return row.Status==MasterIdolForestGuardLifecycle.Phase.Active?MasterIdolForestGuardLifecycle.CanOperate(body.Data):row.Applied&&MasterIdolForestGuardLifecycleOwner.Native(row,body.Data);
        }
        internal static bool IsHeld(Character actor)=>actor!=null&&Bodies.TryGetValue(actor,out var body)&&body.Held&&Current(body);
        internal static bool Suspend(Character actor)
        {
            if(!NativeReady(actor))return false;
            var body=Get(actor);if(body==null||!Current(body)||!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(body.Data,out var row,out var epoch)||row.Status!=MasterIdolForestGuardLifecycle.Phase.Suspended)return false;
            bool applyingOwner=body.View.IsOwner();if(!Accepted(body,row,epoch,applyingOwner))return false;
            try{MasterIdolResidentBirthPresentation.Stop(actor);}catch(Exception){MasterIdolResidentMetrics.Count("guard.cosmetic-stop-failed");}
            body.Held=true;HoldVisual(body);
            if(actor.m_body!=null&&!body.PhysicsHeld)
            {body.Physics=actor.m_body;body.Constraints=body.Physics.constraints;body.PhysicsHeld=true;body.Physics.constraints=RigidbodyConstraints.FreezeAll;}
            if(!body.ColliderHeld)
            {body.Collider=actor.GetCollider();if(body.Collider!=null){body.ColliderEnabled=body.Collider.enabled;body.Collider.enabled=false;body.ColliderHeld=true;}}
            if(actor.gameObject.activeSelf)actor.gameObject.SetActive(false);
            if(!Accepted(body,row,epoch,applyingOwner))return false; // OnDisable may supersede intent/owner/world.
            RemoveTargets(body);return true;
        }
        static bool Restore(Body body,MasterIdolForestGuardLifecycle.State expected,string epoch,bool owner)
        {
            var actor=body.Actor;if(!Accepted(body,expected,epoch,owner))return false;
            if(!actor.gameObject.activeSelf)actor.gameObject.SetActive(true);
            // Native OnEnable/user callbacks may have committed a newer intent or
            // transferred ownership. No old claim/native-key writes after that.
            if(!Accepted(body,expected,epoch,owner))return false;
            RestoreVisual(body);body.Renderers.Clear();body.Enabled.Clear();
            if(body.PhysicsHeld&&body.Physics!=null&&ReferenceEquals(actor.m_body,body.Physics)&&body.Physics.constraints==RigidbodyConstraints.FreezeAll)body.Physics.constraints=body.Constraints;
            body.PhysicsHeld=false;
            if(body.ColliderHeld&&body.Collider!=null&&ReferenceEquals(actor.GetCollider(),body.Collider)&&!body.Collider.enabled)body.Collider.enabled=body.ColliderEnabled;
            body.ColliderHeld=false;
            if(body.RemovedCharacter&&!Character.GetAllCharacters().Contains(actor))Character.GetAllCharacters().Add(actor);
            if(body.RemovedAi&&body.Ai!=null&&ReferenceEquals(actor.GetBaseAI(),body.Ai)&&!BaseAI.GetAllInstances().Contains(body.Ai))BaseAI.GetAllInstances().Add(body.Ai);
            if(!Accepted(body,expected,epoch,owner))return false;
            body.RemovedCharacter=body.RemovedAi=body.Held=false;return true;
        }
        internal static bool ReleaseActiveForOwner(Character actor,MasterIdolForestGuardLifecycle.State expected)
        {
            var body=Get(actor);
            if(body==null||body.View.IsOwner()!=true||!Current(body)||!NativeReady(actor)||
                !MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(body.Data,out var row,out var epoch)||row.Status!=MasterIdolForestGuardLifecycle.Phase.Active||
                !MasterIdolForestGuardLifecycle.SameIntent(row,expected))return false;
            // No CanOperate circular dependency: the owner receipt is stamped
            // only after successful restoration by the authorized caller.
            return Restore(body,row,epoch,true);
        }
        internal static bool ReleaseActive(Character actor)
        {
            if(actor==null||!Bodies.TryGetValue(actor,out var body)||!Current(body)||!MasterIdolForestGuardLifecycle.CanOperate(body.Data)||
                !MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(body.Data,out var row,out var epoch))return false;
            return Restore(body,row,epoch,false);
        }
        internal static bool Stage(Character actor)=>false; // No presentation-owned physical stage in V2.
        internal static void HandVisualToCue(Character actor){} // Compatibility no-op; cosmetics never own native renderer claims.
        internal static void HoldAfterCue(Character actor){}
        internal static void TrackLoaded(Character actor)
        {
            // Character Awake may precede BaseAI cached view readiness.
            // Observation here never interprets a persisted phase as fresh intent.
            var body=Get(actor);if(body==null||!NativeReady(actor))return;
            if(MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(body.Data,out var row,out _)&&row.Applied&&MasterIdolForestGuardLifecycleOwner.Native(row,body.Data))
            {if(row.Status==MasterIdolForestGuardLifecycle.Phase.Suspended)Suspend(actor);else if(row.Status==MasterIdolForestGuardLifecycle.Phase.Active)ReleaseActive(actor);}
        }
        internal static void Tick()
        {
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+1;
            // Bounded direct scene lookups from at most four accepted rows,
            // including observers whose body Awake preceded snapshot delivery.
            // No unadopted marker-only body may consume a runtime claim slot.
            foreach(var row in MasterIdolForestGuardLifecycle.DiagnosticRows(out _))
            {var instance=ZNetScene.instance?.FindInstance(row.Id);var actor=instance!=null?instance.GetComponent<Character>():null;if(actor!=null)Get(actor);}
            foreach(var pair in new List<KeyValuePair<Character,Body>>(Bodies))
            {
                var body=pair.Value;
                if(!Current(body)||body.Actor.IsDead()){Bodies.Remove(pair.Key);continue;}
                if(!NativeReady(body.Actor)||!MasterIdolForestGuardLifecycle.TryReadAcceptedIntent(body.Data,out var row,out _)||!row.Applied||!MasterIdolForestGuardLifecycleOwner.Native(row,body.Data))continue;
                if(row.Status==MasterIdolForestGuardLifecycle.Phase.Suspended){if(!body.Held||body.Actor.gameObject.activeSelf)Suspend(body.Actor);}
                else if(row.Status==MasterIdolForestGuardLifecycle.Phase.Active)
                {if(body.Held)ReleaseActive(body.Actor);if(!IsHeld(body.Actor))try{MasterIdolResidentBirthPresentation.NotifyActive(body.Actor,row);}catch(Exception){MasterIdolResidentMetrics.Count("guard.cosmetic-notify-failed");}}
            }
        }
        internal static void Forget(Character actor){if(!ReferenceEquals(actor,null))Bodies.Remove(actor);}
        internal static void Reset(){Bodies.Clear();Next=0;} // World exit never re-enables or re-registers objects.
    }
    [HarmonyPatch(typeof(Character),"Awake")]
    internal static class ForestLifecycleCharacterAttachPatch{static void Postfix(Character __instance)=>MasterIdolForestGuardLifecycleVisibility.TrackLoaded(__instance);}
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class ForestLifecycleAiAttachPatch{static void Postfix(BaseAI __instance)=>MasterIdolForestGuardLifecycleVisibility.TrackLoaded(__instance.GetComponent<Character>());}
    [HarmonyPatch(typeof(Character),"OnDestroy")]
    internal static class ForestLifecycleForgetPatch{static void Prefix(Character __instance)=>MasterIdolForestGuardLifecycleVisibility.Forget(__instance);}
    [HarmonyPatch(typeof(Character),"CustomFixedUpdate")]
    internal static class ForestLifecycleCharacterHoldPatch{static bool Prefix(Character __instance)=>!MasterIdolForestGuardLifecycleVisibility.IsHeld(__instance);}
    [HarmonyPatch(typeof(Humanoid),"CustomFixedUpdate")]
    internal static class ForestLifecycleHumanoidHoldPatch{static bool Prefix(Humanoid __instance)=>!MasterIdolForestGuardLifecycleVisibility.IsHeld(__instance);}
    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.UpdateAI))]
    internal static class ForestLifecycleAiHoldPatch
    {
        [HarmonyPriority(Priority.First)]static bool Prefix(MonsterAI __instance,ref bool __result)
        {
            var view=__instance.m_nview;
            if(view?.IsValid()!=true||!view.IsOwner())return true;
            if(MasterIdolForestGuardLifecycleVisibility.IsHeld(__instance.GetComponent<Character>())){__result=true;return false;}
            if(MasterIdolForestGuardLifecycleVisibility.IsHeld(__instance.m_targetCreature))
            {
                var data=view.GetZDO();
                if(data==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return true;
                __instance.m_targetCreature=null;
                if(data.GetBool(ZDOVars.s_haveTargetHash,false))data.Set(ZDOVars.s_haveTargetHash,false);
                __instance.m_updateTargetTimer=0;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Character),"RPC_Damage")]
    internal static class ForestLifecycleInactiveRpcDamagePatch{static bool Prefix(Character __instance)=>!MasterIdolForestGuardLifecycleVisibility.IsHeld(__instance);}
    [HarmonyPatch(typeof(Character),"ApplyDamage")]
    internal static class ForestLifecycleInactiveDamagePatch{static bool Prefix(Character __instance)=>!MasterIdolForestGuardLifecycleVisibility.IsHeld(__instance);}
}
