using System;
using UnityEngine;
namespace ValheimMastery
{
    // Compare sampled native bones with this same actor's planted idle pose. No transform writes,
    // terrain offsets, physics changes or invented world-height correction.
    internal sealed class MasterIdolResidentReactionPose
    {
        private readonly Character Actor;private readonly Animator Animator;private readonly Avatar Avatar;
        private readonly Transform Hips,Left,Right,HipsParent,LeftParent,RightParent;private readonly Vector3 Up;
        private readonly float HipHeight,LeftHeight,RightHeight,Height;
        private MasterIdolResidentReactionPose(Character actor,Animator animator,Transform hips,Transform left,Transform right,float height)
        {Actor=actor;Animator=animator;Avatar=animator.avatar;Hips=hips;Left=left;Right=right;HipsParent=hips.parent;LeftParent=left.parent;RightParent=right.parent;Up=actor.transform.up.normalized;Height=height;HipHeight=Read(hips);LeftHeight=Read(left);RightHeight=Read(right);}
        private static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        private float Read(Transform bone)=>Vector3.Dot(bone.position-Actor.transform.position,Up);
        internal static bool Required(Character actor)
        {string family=actor==null?"":Utils.GetPrefabName(actor.gameObject);return family=="Greyling"||family=="Greydwarf_Elite";}
        internal static bool Capture(Character actor,Animator animator,out MasterIdolResidentReactionPose pose)
        {
            pose=null;
            try
            {
                if(actor==null||actor.IsDead()||animator==null||!animator.isHuman||!animator.isInitialized||!Finite(actor.transform.position)||!Finite(actor.transform.up)||actor.transform.up.sqrMagnitude<.5f)return false;
                float height=actor.GetHeight();if(!float.IsFinite(height)||height<=0)return false;
                var hips=animator.GetBoneTransform(HumanBodyBones.Hips);var left=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var right=animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if(hips==null||left==null||right==null||hips==left||hips==right||left==right||!hips.IsChildOf(actor.transform)||!left.IsChildOf(actor.transform)||!right.IsChildOf(actor.transform)||!Finite(hips.position)||!Finite(left.position)||!Finite(right.position))return false;
                pose=new MasterIdolResidentReactionPose(actor,animator,hips,left,right,height);return pose.Stable();
            }
            catch(Exception){pose=null;return false;}
        }
        internal bool Stable()
        {
            if(Actor==null||Animator==null||Animator.avatar!=Avatar||Hips==null||Left==null||Right==null||Hips.parent!=HipsParent||Left.parent!=LeftParent||Right.parent!=RightParent||
                !Finite(Actor.transform.position)||!Finite(Hips.position)||!Finite(Left.position)||!Finite(Right.position))return false;
            float hips=Read(Hips),left=Read(Left),right=Read(Right);
            // Relative anatomical tolerances permit normal planted-foot weight shift and a torso bend,
            // while refusing birth/emerge body drops. Initial physics contact is checked by caller.
            return float.IsFinite(hips)&&float.IsFinite(left)&&float.IsFinite(right)&&hips>=HipHeight-Height*.25f&&left>=LeftHeight-Height*.08f&&right>=RightHeight-Height*.08f;
        }
    }
}
