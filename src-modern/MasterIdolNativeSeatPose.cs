using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace ValheimMastery
{
    // Native humanoid chair clip on the existing combat controller's idle only.
    internal sealed class MasterIdolNativeSeatPose:IDisposable
    {
        private const int Movement=unchecked((int)3885065915u);
        private Character Actor;private Animator Animator;private RuntimeAnimatorController Original;
        private AnimatorOverrideController Override;private AnimationClip Clip;private Transform Visual,Hips,Left,Right;
        private Vector3 OriginalPosition,AppliedPosition;private bool Applied;
        private sealed class SampleCache{internal RuntimeAnimatorController Controller;internal AnimationClip Donor;internal Avatar Avatar;internal Vector3 Scale,VisualScale,VisualPosition;internal Quaternion VisualRotation;internal ZNet Net;internal ZNetScene Scene;internal ulong Generation;internal Vector3[] Hips,Left,Right;}
        private static readonly ConditionalWeakTable<Animator,SampleCache> Samples=new ConditionalWeakTable<Animator,SampleCache>();
        internal Vector3[] HipOffsets,LeftOffsets,RightOffsets;
        internal static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        internal static MasterIdolNativeSeatPose Prepare(Character actor)
        {
            var animator=actor?.m_visual?.GetComponent<Animator>();var original=animator?.runtimeAnimatorController;
            if(animator==null||!animator.isHuman||animator.avatar?.isValid!=true||!animator.avatar.isHuman||animator.applyRootMotion||
                original==null||original is AnimatorOverrideController||animator.layerCount!=1||!animator.HasState(0,Movement)||animator.IsInTransition(0)||animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)return null;
            var player=ZNetScene.instance?.GetPrefab("Player")?.GetComponentInChildren<Animator>();AnimationClip donor=null,idle=null;
            foreach(var clip in player?.runtimeAnimatorController?.animationClips??Array.Empty<AnimationClip>())
                if(clip.name=="Player_Sit_Chair"&&clip.isHumanMotion&&!clip.legacy&&clip.isLooping&&Mathf.Abs(clip.length-16.7f)<.05f&&clip.events.Length==0)donor=clip;
            string own=Utils.GetPrefabName(actor.gameObject)=="Greydwarf_Shaman"?"Standing Idle":"Idle";
            foreach(var clip in original.animationClips)if(clip.name==own&&clip.events.Length==0)idle=clip;
            if(donor==null||idle==null)return null;
            var pose=new MasterIdolNativeSeatPose{Actor=actor,Animator=animator,Original=original,Visual=actor.m_visual.transform};
            try
            {
                pose.Clip=UnityEngine.Object.Instantiate(donor);pose.Clip.name="VM_NativeChairSit";pose.Clip.hideFlags=HideFlags.DontSave;pose.Clip.events=Array.Empty<AnimationEvent>();
                pose.Override=new AnimatorOverrideController(original);pose.Override[idle]=pose.Clip;
                var cache=Samples.GetValue(animator,_=>new SampleCache());
                if(cache.Hips!=null&&ReferenceEquals(cache.Controller,original)&&cache.Donor==donor&&cache.Avatar==animator.avatar&&cache.Scale==actor.transform.lossyScale&&cache.VisualScale==pose.Visual.localScale&&cache.VisualPosition==pose.Visual.localPosition&&cache.VisualRotation==pose.Visual.localRotation&&cache.Net==ZNet.instance&&cache.Scene==ZNetScene.instance&&cache.Generation==MasterIdolLoadIdentity.Generation)
                {pose.HipOffsets=cache.Hips;pose.LeftOffsets=cache.Left;pose.RightOffsets=cache.Right;return pose;}
                if(!pose.Sample()){pose.Dispose();return null;}
                cache.Controller=original;cache.Donor=donor;cache.Avatar=animator.avatar;cache.Scale=actor.transform.lossyScale;cache.VisualScale=pose.Visual.localScale;cache.VisualPosition=pose.Visual.localPosition;cache.VisualRotation=pose.Visual.localRotation;cache.Net=ZNet.instance;cache.Scene=ZNetScene.instance;cache.Generation=MasterIdolLoadIdentity.Generation;
                cache.Hips=pose.HipOffsets;cache.Left=pose.LeftOffsets;cache.Right=pose.RightOffsets;return pose;
            }
            catch{pose.Dispose();return null;}
        }
        private bool Sample()
        {
            GameObject root=null;
            try
            {
                var nodes=Visual.GetComponentsInChildren<Transform>(true);if(nodes.Length>64)return false;
                root=new GameObject("VM_PrivateSeatPoseProbe");root.hideFlags=HideFlags.HideAndDontSave;
                root.transform.SetPositionAndRotation(Actor.transform.position,Actor.transform.rotation);root.transform.localScale=Actor.transform.lossyScale;
                var visual=new GameObject(Visual.name).transform;visual.SetParent(root.transform,false);Copy(Visual,visual);
                var map=new Dictionary<Transform,Transform>{{Visual,visual}};
                foreach(var node in nodes)
                {
                    if(node==Visual)continue;if(!map.TryGetValue(node.parent,out var parent))return false;
                    var clone=new GameObject(node.name).transform;clone.SetParent(parent,false);Copy(node,clone);map.Add(node,clone);
                }
                var probe=visual.gameObject.AddComponent<Animator>();probe.avatar=Animator.avatar;probe.runtimeAnimatorController=Override;
                probe.applyRootMotion=false;probe.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                if(!probe.isHuman||probe.GetBehaviours<StateMachineBehaviour>().Length!=0)return false;
                HipOffsets=new Vector3[3];LeftOffsets=new Vector3[3];RightOffsets=new Vector3[3];
                for(int i=0;i<3;i++)
                {
                    probe.SetFloat("forward_speed",0);probe.SetFloat("sideway_speed",0);probe.SetFloat("turn_speed",0);
                    probe.Play(Movement,0,i*.35f);probe.Update(.01f);
                    var hip=probe.GetBoneTransform(HumanBodyBones.Hips);var left=probe.GetBoneTransform(HumanBodyBones.LeftFoot);var right=probe.GetBoneTransform(HumanBodyBones.RightFoot);
                    if(hip==null||left==null||right==null||!hip.IsChildOf(visual)||!left.IsChildOf(visual)||!right.IsChildOf(visual))return false;
                    HipOffsets[i]=Actor.transform.InverseTransformPoint(hip.position);LeftOffsets[i]=Actor.transform.InverseTransformPoint(left.position);RightOffsets[i]=Actor.transform.InverseTransformPoint(right.position);
                    if(!Finite(HipOffsets[i])||!Finite(LeftOffsets[i])||!Finite(RightOffsets[i]))return false;
                }
                return true;
            }
            finally{if(root!=null)UnityEngine.Object.Destroy(root);}
        }
        private static void Copy(Transform from,Transform to){to.localPosition=from.localPosition;to.localRotation=from.localRotation;to.localScale=from.localScale;}
        internal bool Align(Vector3 seat,out Vector3 shift)
        {
            shift=seat-Actor.transform.TransformPoint(HipOffsets[0]);float radius=Actor.GetRadius()+.35f;
            return Finite(shift)&&Mathf.Abs(shift.y)<=.5f&&shift.x*shift.x+shift.z*shift.z<=radius*radius&&shift.sqrMagnitude<=1;
        }
        internal Vector3 Foot(int phase,bool left,Vector3 shift)=>Actor.transform.TransformPoint(left?LeftOffsets[phase]:RightOffsets[phase])+shift;
        internal bool Apply(Vector3 shift)
        {
            if(Applied||Animator==null||Visual==null||!ReferenceEquals(Animator.runtimeAnimatorController,Original)||Animator.applyRootMotion||
                Animator.IsInTransition(0)||Animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)return false;
            OriginalPosition=Visual.localPosition;AppliedPosition=OriginalPosition+Visual.parent.InverseTransformVector(shift);
            try
            {
                Hips=Animator.GetBoneTransform(HumanBodyBones.Hips);Left=Animator.GetBoneTransform(HumanBodyBones.LeftFoot);Right=Animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if(Hips==null||Left==null||Right==null)return false;
                Applied=true;Animator.runtimeAnimatorController=Override;Visual.localPosition=AppliedPosition;
                Animator.Play(Movement,0,0);Animator.Update(0);return true;
            }
            catch{Dispose();return false;}
        }
        internal bool Owned=>Applied&&Animator!=null&&Visual!=null&&ReferenceEquals(Animator.runtimeAnimatorController,Override)&&Visual.localPosition==AppliedPosition&&
            !Animator.applyRootMotion&&!Animator.IsInTransition(0)&&Animator.GetCurrentAnimatorStateInfo(0).fullPathHash==Movement;
        internal bool Contact(Vector3 seat,Vector3 leftFloor,Vector3 rightFloor)
        {
            return Owned&&Hips!=null&&Left!=null&&Right!=null&&Finite(Hips.position)&&Finite(Left.position)&&Finite(Right.position)&&
                (Hips.position-seat).sqrMagnitude<=.04f&&Feet(Left.position,leftFloor)&&Feet(Right.position,rightFloor);
        }
        private static bool Feet(Vector3 point,Vector3 floor)=>Finite(point)&&Finite(floor)&&point.y-floor.y>=-.05f&&point.y-floor.y<=.18f&&
            (point.x-floor.x)*(point.x-floor.x)+(point.z-floor.z)*(point.z-floor.z)<=.09f;
        internal void Hold(){if(Owned){Animator.SetFloat("forward_speed",0);Animator.SetFloat("sideway_speed",0);Animator.SetFloat("turn_speed",0);}}
        public void Dispose()
        {
            try{if(Applied&&Visual!=null&&Visual.localPosition==AppliedPosition)Visual.localPosition=OriginalPosition;}
            finally
            {
                try{if(Animator!=null&&Override!=null&&ReferenceEquals(Animator.runtimeAnimatorController,Override))Animator.runtimeAnimatorController=Original;}
                finally
                {
                    Applied=false;var controller=Override;var clip=Clip;Override=null;Clip=null;
                    try{if(controller!=null)UnityEngine.Object.Destroy(controller);}finally{if(clip!=null)UnityEngine.Object.Destroy(clip);}
                }
            }
        }
    }
}
