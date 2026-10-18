using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolResidentUniquePresentation:IDisposable
    {
        private const int Movement=unchecked((int)3885065915u);
        private struct Rotation{internal Transform Bone,Parent;internal Quaternion Before,Written;}
        private readonly Character Actor;private readonly Animator Animator;private readonly RuntimeAnimatorController Original;private readonly ResidentUniqueTicket Ticket;
        private readonly int Family;private readonly float Duration;private readonly long LeaseEpoch;private bool Lease,Disposed,OwnSpell,ClubHidden;
        private AnimatorOverrideController Override;private AnimationClip Clip;private Transform Head,Spine,LeftUpper,LeftLower,LeftHand,RightUpper,RightLower,RightHand;
        private readonly List<Rotation> Rotations=new List<Rotation>(5);private readonly List<GameObject> Props=new List<GameObject>(3);
        private Renderer Club;private bool ClubEnabled;private MasterIdolResidentPresentation Spell;
        private bool CascadeReady;private Vector3 LeftInner,LeftOuter,RightInner,RightOuter;
        private GameObject Cloud;private MasterIdolResidentReactionWaveUpperBody Wave;
        private MasterIdolResidentUniquePresentation(Character actor,ResidentUniqueTicket ticket,long lease)
        {Actor=actor;Ticket=ticket;Family=MasterIdolForestGuardState.Family(actor.m_nview.GetZDO());Duration=MasterIdolResidentUniqueRules.Seconds(Family,ticket.Kind,ticket.Choice);Animator=actor.GetComponentInChildren<Animator>();Original=Animator?.runtimeAnimatorController;LeaseEpoch=lease;Lease=true;}
        private static Transform Bone(Character actor,string name)
        {Transform found=null;foreach(var transform in actor.GetComponentsInChildren<Transform>(true))if(transform.name==name){if(found!=null)return null;found=transform;}return found;}
        internal static bool CanPrepare(Character actor,ResidentUniqueTicket ticket)
        {
            if(actor==null||ticket==null)return false;int family=MasterIdolForestGuardState.Family(actor.m_nview?.GetZDO());
            if(!MasterIdolResidentUniqueRules.MotionApproved(family,ticket.Kind,ticket.Choice))return false;var animator=actor.GetComponentInChildren<Animator>();var native=ZNetScene.instance?.GetPrefab(MasterIdolForestRules.Families[family])?.GetComponentInChildren<Animator>(true);
            if(native==null||animator==null||animator.avatar!=native.avatar||animator.applyRootMotion||animator.runtimeAnimatorController!=native.runtimeAnimatorController||animator.layerCount!=1)return false;
            if(MasterIdolResidentUniqueRules.UpperWave(family,ticket.Kind,ticket.Choice))
            {
                if(!MasterIdolResidentReactionWaveUpperBody.Capture(actor,animator,out var cue))return false;
                cue.Dispose();return NativeIdle(animator.runtimeAnimatorController,family)!=null;
            }
            if(ticket.Kind==ResidentUniqueKind.Gesture)return GestureClip(ticket.Choice)!=null&&animator.isHuman;
            if(family==3)return MasterIdolResidentPresentation.ResourcesReady(actor,false)&&HealCloud()!=null;
            if(Bone(actor,"head")==null)return false;
            if(family==0)return true;
            if(Bone(actor,"l_arm1")==null||Bone(actor,"l_arm2")==null||Bone(actor,"l_hand")==null||Bone(actor,"r_arm1")==null||Bone(actor,"r_arm2")==null||Bone(actor,"r_hand")==null)return false;
            return family==1?Stone()!=null:NativeClub(actor)!=null;
        }
        private static AnimationClip GestureClip(byte index)
        {
            if(index>9)return null;var controller=ZNetScene.instance?.GetPrefab("Player")?.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController;
            if(controller==null)return null;AnimationClip found=null;
            foreach(var clip in controller.animationClips)if(clip!=null&&clip.name==MasterIdolResidentUniqueRules.GestureClips[index]){if(found!=null&&found!=clip)return null;found=clip;}
            return found!=null&&!found.legacy&&found.isHumanMotion&&float.IsFinite(found.length)&&Mathf.Abs(found.length-MasterIdolResidentUniqueRules.GestureSeconds(index))<.05f?found:null;
        }
        private static AnimationClip NativeIdle(RuntimeAnimatorController controller,int family)
        {
            if(controller==null)return null;AnimationClip idle=null;string name=family==3?"Standing Idle":"Idle";
            foreach(var candidate in controller.animationClips)if(candidate!=null&&candidate.name==name){if(idle!=null)return null;idle=candidate;}
            return idle!=null&&!idle.legacy&&idle.events.Length==0&&float.IsFinite(idle.length)&&idle.length>0?idle:null;
        }
        private static MeshFilter Stone()
        {
            var prefab=ObjectDB.instance?.GetItemPrefab("Stone");if(prefab==null)return null;
            foreach(var mesh in prefab.GetComponentsInChildren<MeshFilter>(true))if(mesh.sharedMesh!=null&&mesh.GetComponent<MeshRenderer>()?.sharedMaterials?.Length>0)return mesh;return null;
        }
        private static MeshFilter NativeClub(Character actor)
        {
            var hand=Bone(actor,"r_hand");var root=hand?.Find("Rootsword/default");var mesh=root?.GetComponent<MeshFilter>();var renderer=root?.GetComponent<MeshRenderer>();
            return mesh?.sharedMesh!=null&&renderer!=null&&renderer.enabled&&renderer.gameObject.activeInHierarchy&&renderer.sharedMaterials.Length>0?mesh:null;
        }
        private GameObject Copy(MeshFilter mesh,string name)
        {
            var go=new GameObject(name);Props.Add(go);go.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;go.AddComponent<MeshRenderer>().sharedMaterials=mesh.GetComponent<MeshRenderer>().sharedMaterials;
            UnityEngine.Object.Destroy(go,8);return go; // No ItemDrop, AI, collider, rigidbody, attack or network component is copied.
        }
        private static GameObject HealCloud()=>ZNetScene.instance?.GetPrefab("shaman_heal_aoe")??NativeSoftVisualAssets.GetPrefab("shaman_heal_aoe");
        private void CloudOnly()
        {
            var native=HealCloud();if(native==null)throw new InvalidOperationException("Native cloud not loaded");
            var template=new GameObject("vfx_VM_ResidentNativeHealCloud");template.SetActive(false);Props.Add(template);UnityEngine.Object.Destroy(template,8);
            foreach(string name in new[]{"flames","flames_world"})
            {
                var source=native.transform.Find(name);if(source==null)throw new InvalidOperationException("Native cloud child missing");
                foreach(var component in source.GetComponentsInChildren<Component>(true))if(!(component is Transform)&&!(component is ParticleSystem)&&!(component is ParticleSystemRenderer))throw new InvalidOperationException("Cloud child has gameplay component");
                UnityEngine.Object.Instantiate(source.gameObject,template.transform,false);
            }
            // Clone only the verified native particle children, never the native Aoe/network/collider root.
            Cloud=PerkNativeFeedback.CreateVisualOnly(template);if(Cloud==null)throw new InvalidOperationException("Native cloud safe frame pending");
            Props.Add(Cloud);Cloud.transform.SetPositionAndRotation(Actor.transform.position,Quaternion.identity);Cloud.SetActive(true);UnityEngine.Object.Destroy(Cloud,8);
        }
        internal static MasterIdolResidentUniquePresentation Begin(Character actor,ResidentUniqueTicket ticket,ResidentUniqueOutcome outcome,float elapsed)
        {
            if(actor==null||ticket==null||!MasterIdolResidentUniqueRules.MotionApproved(MasterIdolForestGuardState.Family(actor.m_nview?.GetZDO()),ticket.Kind,ticket.Choice))return null;
            if(Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null||!MasterIdolResidentVisibility.Local(actor)||!MasterIdolVisibleGesture.ReserveSeat(out long epoch))return null;
            MasterIdolResidentUniquePresentation value=null;
            try
            {
                value=new MasterIdolResidentUniquePresentation(actor,ticket,epoch);
                if(value.Family==3&&ticket.Kind==ResidentUniqueKind.Family)
                {
                    value.Spell=actor.GetComponent<MasterIdolResidentPresentation>()??actor.gameObject.AddComponent<MasterIdolResidentPresentation>();
                    if(value.Spell.Playing){if(outcome!=ResidentUniqueOutcome.HealRequested||!value.Spell.OwnsHealCue(actor,ticket.Home))throw new InvalidOperationException("Foreign native presentation");}
                    else{if(!CanPrepare(actor,ticket)||!value.Spell.Begin(false,elapsed,ticket.Home))throw new InvalidOperationException("Native heal cloud not ready");value.OwnSpell=true;}
                    value.CloudOnly();
                    return value;
                }
                if(!CanPrepare(actor,ticket)||value.Animator.IsInTransition(0)||value.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)throw new InvalidOperationException("Native movement not ready");
                if(value.Family==2&&ticket.Kind==ResidentUniqueKind.Family&&ticket.Choice==0)
                {
                    // Reuse the approved free-hand wave over the current native idle without resetting its controller or phase.
                    if(!MasterIdolResidentReactionWaveUpperBody.Capture(actor,value.Animator,out value.Wave))throw new InvalidOperationException("Approved greeting wave bones unavailable");
                }
                else if(ticket.Kind==ResidentUniqueKind.Gesture)
                {
                    bool upperWave=MasterIdolResidentUniqueRules.UpperWave(value.Family,ticket.Kind,ticket.Choice);
                    AnimationClip idle=null,donor;
                    if(upperWave){idle=NativeIdle(value.Original,value.Family);donor=idle;}
                    else
                    {
                        donor=GestureClip(ticket.Choice);string name=value.Family==3?"Standing Idle":"Idle";
                        foreach(var candidate in value.Original.animationClips)if(candidate.name==name){if(idle!=null&&idle!=candidate)throw new InvalidOperationException("Native idle not unique");idle=candidate;}
                    }
                    if(upperWave&&!MasterIdolResidentReactionWaveUpperBody.Capture(actor,value.Animator,out value.Wave))throw new InvalidOperationException("Approved wave bones unavailable");
                    if(idle==null||donor==null)throw new InvalidOperationException("Native gesture clips missing");
                    value.Clip=UnityEngine.Object.Instantiate(donor);value.Clip.name="VM_EventlessResidentGesture";value.Clip.hideFlags=HideFlags.DontSave;value.Clip.events=Array.Empty<AnimationEvent>();
                    value.Override=new AnimatorOverrideController(value.Original);value.Override[idle]=value.Clip;value.Animator.runtimeAnimatorController=value.Override;
                    value.Animator.SetFloat("forward_speed",0);value.Animator.SetFloat("turn_speed",0);value.Animator.Play(Movement,0,elapsed/donor.length);value.Animator.Update(0);
                    bool effective=false;foreach(var current in value.Animator.GetCurrentAnimatorClipInfo(0))if(current.clip==value.Clip&&current.weight>0)effective=true;
                    if(!effective||!float.IsFinite(value.Animator.speed)||value.Animator.speed<=0||value.Animator.IsInTransition(0)||value.Animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)throw new InvalidOperationException("Native human motion not effective");
                }
                else
                {
                    value.Head=Bone(actor,"head");value.Spine=Bone(actor,"spine3");value.LeftUpper=Bone(actor,"l_arm1");value.LeftLower=Bone(actor,"l_arm2");value.LeftHand=Bone(actor,"l_hand");value.RightUpper=Bone(actor,"r_arm1");value.RightLower=Bone(actor,"r_arm2");value.RightHand=Bone(actor,"r_hand");
                    if(value.Family==1){var stone=Stone();for(int i=0;i<3;i++){var go=value.Copy(stone,"VM_ResidentJuggleStone");float size=Mathf.Max(stone.sharedMesh.bounds.size.x,Mathf.Max(stone.sharedMesh.bounds.size.y,stone.sharedMesh.bounds.size.z));if(!float.IsFinite(size)||size<=0)throw new InvalidOperationException("Native stone bounds");go.transform.localScale=Vector3.one*(.14f/size);}}
                    value.Tick(elapsed);
                }
                MasterIdolResidentMetrics.Count("unique.presentation-start");return value;
            }
            catch(Exception){if(value!=null)value.Dispose();else MasterIdolVisibleGesture.ReleaseSeat(epoch);return null;}
        }
        private void Rotate(Transform bone,Quaternion rotation)
        {if(bone==null)return;var saved=new Rotation{Bone=bone,Parent=bone.parent,Before=bone.localRotation};bone.rotation=rotation;saved.Written=bone.localRotation;Rotations.Add(saved);}
        private void RestoreBones()
        {foreach(var saved in Rotations)if(saved.Bone!=null&&saved.Bone.parent==saved.Parent&&Quaternion.Angle(saved.Bone.localRotation,saved.Written)<.001f)saved.Bone.localRotation=saved.Before;Rotations.Clear();}
        private void Arm(Transform upper,Transform lower,Transform hand,Vector3 target,float blend)
        {
            if(upper==null||lower==null||hand==null||blend<=0)return;
            Vector3 origin=upper.position,oldElbow=lower.position,oldHand=hand.position;float first=(oldElbow-origin).magnitude,second=(oldHand-oldElbow).magnitude;
            if(first<.01f||second<.01f)return;target=Vector3.Lerp(oldHand,target,blend);Vector3 delta=target-origin;float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(first-second)+.001f,(first+second)*.98f);Vector3 direction=delta.normalized;
            Vector3 pole=Vector3.ProjectOnPlane(oldElbow-origin,direction);if(pole.sqrMagnitude<.0001f)pole=Vector3.ProjectOnPlane(Actor.transform.forward,direction);if(pole.sqrMagnitude<.0001f)return;
            float along=(first*first-second*second+distance*distance)/(2*distance);Vector3 elbow=origin+direction*along+pole.normalized*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Rotate(upper,Quaternion.FromToRotation(oldElbow-origin,elbow-origin)*upper.rotation);Rotate(lower,Quaternion.FromToRotation(hand.position-lower.position,origin+direction*distance-lower.position)*lower.rotation);
        }
        private Vector3 JuggleStation(Transform upper,Transform lower,Transform hand,Vector3 target)
        {
            float a=(lower.position-upper.position).magnitude,b=(hand.position-lower.position).magnitude;var delta=target-upper.position;
            if(a<.01f||b<.01f||delta.sqrMagnitude<.000001f)throw new InvalidOperationException("Native juggling arm binding");
            float reach=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,(a+b)*.98f);
            return Actor.transform.InverseTransformPoint(upper.position+delta.normalized*reach);
        }
        private void Cascade(float elapsed,float height,Vector3 up,Vector3 right,Vector3 forward,Vector3 chest)
        {
            if(!CascadeReady)
            {
                var center=chest+forward*height*.23f-up*height*.18f;
                LeftInner=JuggleStation(LeftUpper,LeftLower,LeftHand,center-right*height*.12f);LeftOuter=JuggleStation(LeftUpper,LeftLower,LeftHand,center-right*height*.24f);
                RightInner=JuggleStation(RightUpper,RightLower,RightHand,center+right*height*.12f);RightOuter=JuggleStation(RightUpper,RightLower,RightHand,center+right*height*.24f);CascadeReady=true;
            }
            float blend=ResidentJuggleCascade.Envelope(elapsed,Duration);var left=ResidentJuggleCascade.Hand(elapsed,0,Duration);var rightHand=ResidentJuggleCascade.Hand(elapsed,1,Duration);
            var leftTarget=Actor.transform.TransformPoint(Vector3.Lerp(LeftInner,LeftOuter,left.Across))+up*height*left.Lift;
            var rightTarget=Actor.transform.TransformPoint(Vector3.Lerp(RightInner,RightOuter,rightHand.Across))+up*height*rightHand.Lift;
            Arm(LeftUpper,LeftLower,LeftHand,leftTarget,blend);Arm(RightUpper,RightLower,RightHand,rightTarget,blend);
            for(int i=0;i<3;i++)
            {
                var ball=ResidentJuggleCascade.Ball(elapsed,i,Duration);var hand=ball.Holder==0?LeftHand:RightHand;
                // Half a verified Stone width beside the real palm avoids merging the two held stones.
                var grip=forward*height*.03f+up*height*.015f+right*(i==2?.07f:-.07f);
                if(!ball.Airborne){Props[i].transform.position=hand.position+grip;Props[i].transform.rotation=hand.rotation;continue;}
                var from=Actor.transform.TransformPoint(ball.ThrowHand==0?LeftInner:RightInner)+grip;
                var to=Actor.transform.TransformPoint(ball.ThrowHand==0?RightOuter:LeftOuter)+grip;
                Props[i].transform.position=Vector3.Lerp(from,to,ball.FlightU)+up*height*ResidentJuggleCascade.Height(ball.FlightU);
                Props[i].transform.rotation=Quaternion.Euler(elapsed*100+i*35,elapsed*80,0);
            }
        }
        internal bool Tick(float elapsed)
        {
            if(Disposed||Actor==null||elapsed<0||elapsed>=Duration)return false;
            if(Spell!=null){if(Cloud!=null)Cloud.transform.position=Actor.transform.position;return Spell.Playing;}
            var owned=Override!=null?(RuntimeAnimatorController)Override:Original;
            if(Animator==null||Animator.runtimeAnimatorController!=owned||Animator.IsInTransition(0)||Animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)return false;
            Animator.SetFloat("forward_speed",0);Animator.SetFloat("turn_speed",0);
            if(Family==2&&Ticket.Kind==ResidentUniqueKind.Family&&Ticket.Choice==0)return Wave!=null&&Wave.Tick(elapsed,owned);
            if(Ticket.Kind==ResidentUniqueKind.Gesture)return Wave==null||Wave.Tick(elapsed,owned);
            RestoreBones();float envelope=Mathf.Clamp01(Mathf.Min(elapsed/.35f,(Duration-elapsed)/.5f));float height=Actor.GetHeight();if(!float.IsFinite(height)||height<=0)return false;height=Mathf.Clamp(height,.6f,3.5f);Vector3 up=Actor.transform.up,right=Actor.transform.right,forward=Actor.transform.forward;
            if(Family==0){Rotate(Head,Head.rotation*Quaternion.Euler(5*Mathf.Sin(elapsed*7)*envelope,3*Mathf.Sin(elapsed*3)*envelope,0));return true;}
            Vector3 chest=Spine.position;
            if(Family==1)
            {
                Cascade(elapsed,height,up,right,forward,chest);
            }
            return true;
        }
        public void Dispose()
        {
            if(Disposed)return;Disposed=true;
            try{try{Wave?.Dispose();}catch(Exception){/* Continue independent controller/lease cleanup. */}finally{Wave=null;}RestoreBones();if(ClubHidden&&Club!=null&&!Club.enabled)Club.enabled=ClubEnabled;if(Animator!=null&&Override!=null&&Animator.runtimeAnimatorController==Override)Animator.runtimeAnimatorController=Original;if(OwnSpell&&Spell!=null)Spell.Stop();}
            finally
            {
                try{foreach(var go in Props)if(go!=null)UnityEngine.Object.Destroy(go);if(Override!=null)UnityEngine.Object.Destroy(Override);if(Clip!=null)UnityEngine.Object.Destroy(Clip);}
                finally{if(Lease){MasterIdolVisibleGesture.ReleaseSeat(LeaseEpoch);Lease=false;}}
            }
        }
    }
}
