using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Preview candidate: an expressive free LEFT hand greeting over the exact native idle.
    // It is procedural upper-body motion, not an authored native Wave clip or the club strength action.
    internal sealed class MasterIdolResidentReactionWaveUpperBody : IDisposable
    {
        internal const float Seconds=3.2f;
        private struct Rotation{internal Transform Bone,Parent;internal Quaternion Before,Written;}
        private readonly Character Actor;private readonly Animator Animator;private readonly Avatar Avatar;
        private readonly Transform Head,Upper,Lower,Hand,HeadParent,UpperParent,LowerParent,HandParent;
        private readonly List<Rotation> Rotations=new List<Rotation>(3);private bool Disposed;
        private MasterIdolResidentReactionWaveUpperBody(Character actor,Animator animator,Transform head,Transform upper,Transform lower,Transform hand)
        {Actor=actor;Animator=animator;Avatar=animator.avatar;Head=head;Upper=upper;Lower=lower;Hand=hand;HeadParent=head.parent;UpperParent=upper.parent;LowerParent=lower.parent;HandParent=hand.parent;}
        private static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        private static bool Finite(Quaternion v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&float.IsFinite(v.w);
        internal static bool Capture(Character actor,Animator animator,out MasterIdolResidentReactionWaveUpperBody cue)
        {
            cue=null;
            try
            {
                if(actor==null||actor.IsDead()||animator==null||!animator.isHuman||!animator.isInitialized||animator.avatar==null)return false;
                var head=animator.GetBoneTransform(HumanBodyBones.Head);var upper=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var lower=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);var hand=animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if(head==null||upper==null||lower==null||hand==null||head==upper||head==lower||head==hand||upper==lower||upper==hand||lower==hand||
                    head.parent==null||upper.parent==null||lower.parent==null||hand.parent==null||!head.IsChildOf(actor.transform)||!upper.IsChildOf(actor.transform)||!lower.IsChildOf(upper)||!hand.IsChildOf(lower))return false;
                float height=actor.GetHeight();if(!float.IsFinite(height)||height<=0||!Finite(upper.position)||!Finite(lower.position)||!Finite(hand.position))return false;
                cue=new MasterIdolResidentReactionWaveUpperBody(actor,animator,head,upper,lower,hand);return true;
            }
            catch(Exception){return false;}
        }
        private void Rotate(Transform bone,Quaternion rotation)
        {var saved=new Rotation{Bone=bone,Parent=bone.parent,Before=bone.localRotation};bone.rotation=rotation;saved.Written=bone.localRotation;Rotations.Add(saved);}
        private void RestoreFrame()
        {
            try{foreach(var saved in Rotations)if(saved.Bone!=null&&saved.Bone.parent==saved.Parent&&Finite(saved.Bone.localRotation)&&Quaternion.Angle(saved.Bone.localRotation,saved.Written)<.001f)saved.Bone.localRotation=saved.Before;}
            finally{Rotations.Clear();}
        }
        internal bool Tick(float elapsed,RuntimeAnimatorController owned)
        {
            if(Disposed||Actor==null||Actor.IsDead()||Animator==null||Animator.avatar!=Avatar||owned==null||!ReferenceEquals(Animator.runtimeAnimatorController,owned)||
                Head==null||Upper==null||Lower==null||Hand==null||Head.parent!=HeadParent||Upper.parent!=UpperParent||Lower.parent!=LowerParent||Hand.parent!=HandParent||
                !float.IsFinite(elapsed)||elapsed<0||elapsed>=Seconds)return false;
            RestoreFrame();
            float height=Actor.GetHeight();var up=Actor.transform.up;var right=Actor.transform.right;var forward=Actor.transform.forward;
            if(!float.IsFinite(height)||height<=0||!Finite(up)||!Finite(right)||!Finite(forward)||!Finite(Head.position)||!Finite(Upper.position)||!Finite(Lower.position)||!Finite(Hand.position)||
                !Finite(Upper.localRotation)||!Finite(Lower.localRotation)||!Finite(Hand.localRotation))return false;
            height=Mathf.Clamp(height,.6f,3.5f);float envelope=Mathf.Clamp01(Mathf.Min(elapsed/.65f,(Seconds-elapsed)/.65f));
            if(envelope<=0)return true;
            float sway=Mathf.Sin(Mathf.Max(0,elapsed-.65f)*8);
            Vector3 target=Head.position-right*(height*(.32f+.07f*sway*envelope))+up*(height*.03f)+forward*(height*.14f);
            // Same two-bone reach construction as the existing E969 family presentation. Only the free left arm is written.
            Vector3 origin=Upper.position,oldElbow=Lower.position,oldHand=Hand.position;float first=(oldElbow-origin).magnitude,second=(oldHand-oldElbow).magnitude;
            if(first<.01f||second<.01f)return false;target=Vector3.Lerp(oldHand,target,envelope);Vector3 delta=target-origin;
            float distance=Mathf.Clamp(delta.magnitude,Mathf.Abs(first-second)+.001f,(first+second)*.98f);Vector3 direction=delta.normalized;
            Vector3 pole=Vector3.ProjectOnPlane(oldElbow-origin,direction);if(pole.sqrMagnitude<.0001f)pole=Vector3.ProjectOnPlane(forward,direction);if(pole.sqrMagnitude<.0001f)return false;
            float along=(first*first-second*second+distance*distance)/(2*distance);Vector3 elbow=origin+direction*along+pole.normalized*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Rotate(Upper,Quaternion.FromToRotation(oldElbow-origin,elbow-origin)*Upper.rotation);
            Rotate(Lower,Quaternion.FromToRotation(Hand.position-Lower.position,origin+direction*distance-Lower.position)*Lower.rotation);
            Rotate(Hand,Quaternion.AngleAxis(18*sway*envelope,forward)*Hand.rotation);
            return true;
        }
        public void Dispose(){if(Disposed)return;Disposed=true;RestoreFrame();}
    }
}
