using System;
using UnityEngine;
namespace ValheimMastery
{
    // One owned native model binding. The pair coordinator reserves BOTH leases before either binding.
    internal sealed class ResidentDancePresentation:IDisposable
    {
        private const int Movement=unchecked((int)3885065915u),Jump=788460410;
        private sealed class Binding
        {
            internal Character Actor;internal Animator Animator;internal RuntimeAnimatorController Original;
            internal AnimationClip Idle,Jump,Donor;internal MasterIdolResidentReactionPose Pose;
            internal ZDO Data;internal ZNet Net;internal ZDOMan Manager;internal ulong Generation;internal long World;
            internal ResidentDanceMember Member;internal string Home;internal int Family;internal Avatar Avatar;
        }
        private readonly Binding Bound;private readonly long Epoch;private bool Lease=true,Disposed,Exiting;
        private AnimatorOverrideController Override;private AnimationClip Clip;
        private ResidentDancePresentation(Binding binding,long epoch){Bound=binding;Epoch=epoch;}
        private static AnimationClip Unique(RuntimeAnimatorController controller,string name)
        {
            AnimationClip found=null;
            foreach(var clip in controller.animationClips)if(clip!=null&&clip.name==name){if(found!=null)return null;found=clip;}
            return found;
        }
        private static bool IdleEffective(Animator animator,AnimationClip idle)
        {
            if(animator.IsInTransition(0)||animator.GetCurrentAnimatorStateInfo(0).fullPathHash!=Movement)return false;
            bool effective=false;
            foreach(var clip in animator.GetCurrentAnimatorClipInfo(0))
            {if(clip.clip==idle&&clip.weight>.999f)effective=true;else if(clip.weight>.001f)return false;}
            return effective;
        }
        private static bool Capture(Character actor,ResidentDanceMember member,string home,int family,out Binding binding)
        {
            binding=null;
            if(actor==null||member==null||family<0||family>1||!MasterIdolResidentVisibility.Local(actor)||!Peace(actor)||!MasterIdolLoadIdentity.LoadReady||
                !MasterIdolResidentPolicy.TryHome(actor,out var zone)||zone.Id!=home||!zone.Contains(actor.transform.position))return false;
            var data=actor.m_nview?.GetZDO();var net=ZNet.instance;var manager=ZDOMan.instance;
            if(!Identity(actor,data,member,home,family)||net?.GetWorld()==null||manager==null)return false;
            var animator=actor.GetComponentInChildren<Animator>();var native=ZNetScene.instance?.GetPrefab(MasterIdolForestRules.Families[family])?.GetComponentInChildren<Animator>(true);
            var original=animator?.runtimeAnimatorController;
            if(animator==null||native==null||original==null||original is AnimatorOverrideController||original!=native.runtimeAnimatorController||animator.avatar!=native.avatar||
                !animator.isInitialized||!animator.isHuman||animator.applyRootMotion||animator.layerCount!=1||!float.IsFinite(animator.speed)||Mathf.Abs(animator.speed-1)>.001f||!animator.HasState(0,Movement)||!animator.HasState(0,Jump))return false;
            var idle=Unique(original,"Idle");var jump=Unique(original,"Jump");var player=ZNetScene.instance?.GetPrefab("Player")?.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController;
            var donor=player==null?null:Unique(player,"Dance");
            if(idle==null||jump==null||donor==null||idle.legacy||jump.legacy||donor.legacy||!donor.isHumanMotion||!donor.isLooping||idle.events.Length!=0||
                !float.IsFinite(jump.length)||Mathf.Abs(jump.length-.8666667f)>.02f||!float.IsFinite(donor.length)||Mathf.Abs(donor.length-35.16847f)>.02f||!IdleEffective(animator,idle)||
                !MasterIdolResidentReactionPose.Capture(actor,animator,out var pose))return false;
            binding=new Binding{Actor=actor,Animator=animator,Original=original,Idle=idle,Jump=jump,Donor=donor,Pose=pose,Data=data,Net=net,Manager=manager,Generation=MasterIdolLoadIdentity.Generation,World=net.GetWorldUID(),Member=member,Home=home,Family=family,Avatar=animator.avatar};return true;
        }
        private static bool Identity(Character actor,ZDO data,ResidentDanceMember member,string home,int family)
            =>actor?.m_nview?.IsValid()==true&&data!=null&&ReferenceEquals(ZDOMan.instance?.GetZDO(member.Uid),data)&&ReferenceEquals(actor.m_nview.GetZDO(),data)&&
                MasterIdolForestGuardState.ModuleBorn(data)&&MasterIdolForestGuardState.Family(data)==family&&Utils.GetPrefabName(actor.gameObject)==MasterIdolForestRules.Families[family]&&data.m_uid==member.Uid&&data.GetOwner()==member.Owner&&data.OwnerRevision==member.Revision&&
                data.GetLong(MasterIdolForestGuardState.BornKey,0)==member.Born&&data.GetString(MasterIdolForestGuardState.NonceKey,"")==member.BirthNonce&&
                MasterIdolNativeTaming.SameHome(data.GetString(MasterIdolNativeTaming.HomeKey,""),home);
        private static bool Peace(Character actor)
        {
            var ai=actor?.GetComponent<BaseAI>();
            return actor!=null&&ai!=null&&!actor.IsDead()&&float.IsFinite(actor.GetHealth())&&actor.GetHealth()>0&&MasterIdolResidentGrounding.Slow(actor)&&!MasterIdolSocialSeats.Battle(ai,actor)&&ai.m_timeSinceHurt>=8&&!ai.IsSleeping()&&!actor.InDodge()&&!actor.InEmote()&&
                !MasterIdolResidentWork.Busy(actor)&&!MasterIdolResidentUniqueActions.Busy(actor)&&!MasterIdolResidentCargo.Busy(actor)&&!MasterIdolResidentBirthPresentation.Playing(actor)&&!MasterIdolResidentBirthPresentation.FreshIdleHold(actor)&&
                !MasterIdolResidentSceneAnimation.Playing(actor)&&actor.GetComponent<MasterIdolResidentPresentation>()?.Playing!=true&&actor.GetComponent<MasterIdolWelcome>()?.IsPresenting!=true&&actor.GetComponent<MasterIdolSocialSeats>()?.Active!=true;
        }
        private bool Current()
            =>!Disposed&&Bound.Animator!=null&&Bound.Animator.avatar==Bound.Avatar&&Bound.Net==ZNet.instance&&Bound.Manager==ZDOMan.instance&&Bound.Generation==MasterIdolLoadIdentity.Generation&&Bound.Net?.GetWorld()!=null&&Bound.World==Bound.Net.GetWorldUID()&&
                Identity(Bound.Actor,Bound.Data,Bound.Member,Bound.Home,Bound.Family)&&MasterIdolResidentPolicy.TryHome(Bound.Actor,out var zone)&&zone.Id==Bound.Home&&zone.Contains(Bound.Actor.transform.position);
        private bool OwnController()=>Bound.Animator!=null&&Override!=null&&ReferenceEquals(Bound.Animator.runtimeAnimatorController,Override);
        internal bool CanYield(Character actor)=>actor==Bound.Actor&&Current()&&OwnController();
        internal bool Finished=>Disposed;
        private void Begin(float fixedTimeOffset)
        {
            if(!float.IsFinite(fixedTimeOffset)||fixedTimeOffset<0||fixedTimeOffset>=Bound.Donor.length)throw new InvalidOperationException("Native dance offset");
            Clip=UnityEngine.Object.Instantiate(Bound.Donor);Clip.name="VM_EventlessResidentPairDance";Clip.hideFlags=HideFlags.DontSave;Clip.events=Array.Empty<AnimationEvent>();
            Override=new AnimatorOverrideController(Bound.Original);Override[Bound.Jump]=Clip;Bound.Animator.runtimeAnimatorController=Override;
            Bound.Animator.CrossFadeInFixedTime(Jump,ResidentDanceRules.BlendSeconds,0,fixedTimeOffset);
            Bound.Animator.Update(0); // One binding evaluation, never a per-frame animation clock.
        }
        internal static bool TryPair(Character first,Character second,ResidentDanceTicket ticket,float fixedTimeOffset,out ResidentDancePresentation a,out ResidentDancePresentation b)
            =>TryPair(first,second,ticket,fixedTimeOffset,out a,out b,out _);
        internal static bool TryPair(Character first,Character second,ResidentDanceTicket ticket,float fixedTimeOffset,out ResidentDancePresentation a,out ResidentDancePresentation b,out bool terminalFailure)
        {
            a=b=null;terminalFailure=false;
            if(ticket==null||ZNet.instance?.GetWorld()==null||ticket.World!=ZNet.instance.GetWorldUID()||ticket.Start<=0||!float.IsFinite(fixedTimeOffset)||fixedTimeOffset<0||fixedTimeOffset>=ResidentDanceRules.DanceSeconds-ResidentDanceRules.BlendSeconds||
                !Capture(first,ticket.First,ticket.Home,0,out var firstBinding)||!Capture(second,ticket.Second,ticket.Home,1,out var secondBinding)||
                !MasterIdolVisibleGesture.ReserveSeat(out long firstEpoch))return false;
            bool firstReserved=true,secondReserved=false,started=false;long secondEpoch=0;
            try
            {
                if(!MasterIdolVisibleGesture.ReserveSeat(out secondEpoch))return false;secondReserved=true;
                if(firstEpoch!=secondEpoch)return false;
                a=new ResidentDancePresentation(firstBinding,firstEpoch);firstReserved=false;
                b=new ResidentDancePresentation(secondBinding,secondEpoch);secondReserved=false;
                a.Begin(fixedTimeOffset);b.Begin(fixedTimeOffset);started=true;return true;
            }
            catch(Exception){terminalFailure=true;return false;}
            finally
            {
                if(!started)
                {
                    try{a?.Dispose();}catch(Exception){}
                    try{b?.Dispose();}catch(Exception){}
                    a=b=null;
                }
                if(firstReserved)MasterIdolVisibleGesture.ReleaseSeat(firstEpoch);
                if(secondReserved)MasterIdolVisibleGesture.ReleaseSeat(secondEpoch);
            }
        }
        internal bool Tick(float elapsed)
        {
            if(!float.IsFinite(elapsed)||elapsed<0||!Current()||!OwnController()||!Peace(Bound.Actor)||!MasterIdolResidentVisibility.Local(Bound.Actor)||Bound.Pose?.Stable()!=true||Mathf.Abs(Bound.Animator.speed-1)>.001f)return false;
            if(elapsed>=ResidentDanceRules.DanceSeconds&&!Exiting)
            {Exiting=true;Bound.Animator.CrossFadeInFixedTime(Movement,ResidentDanceRules.BlendSeconds,0,0);}
            bool transition=Bound.Animator.IsInTransition(0);var state=Bound.Animator.GetCurrentAnimatorStateInfo(0);
            if(Exiting)
            {
                if(!transition&&state.fullPathHash==Movement&&IdleEffective(Bound.Animator,Bound.Idle)){Dispose();return false;}
                if(elapsed>ResidentDanceRules.DanceSeconds+1)return false;
                return state.fullPathHash==Jump||state.fullPathHash==Movement;
            }
            if(!transition)return state.fullPathHash==Jump&&Mathf.Abs(state.speed-.8f)<.001f;
            var next=Bound.Animator.GetNextAnimatorStateInfo(0);
            return state.fullPathHash==Movement&&next.fullPathHash==Jump&&Mathf.Abs(next.speed-.8f)<.001f;
        }
        public void Dispose()
        {
            if(Disposed)return;Disposed=true;
            try
            {
                if(OwnController())
                {
                    bool reset=false;
                    try
                    {
                        var actor=Bound.Actor;var ai=actor?.GetComponent<BaseAI>();var animator=Bound.Animator;
                        bool ownedJump=animator.GetCurrentAnimatorStateInfo(0).fullPathHash==Jump||animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).fullPathHash==Jump;
                        reset=ownedJump&&animator.avatar==Bound.Avatar&&actor!=null&&!actor.IsDead()&&!actor.InAttack()&&!actor.IsStaggering()&&!actor.InDodge()&&ai!=null&&!MasterIdolSocialSeats.Battle(ai,actor)&&ai.m_timeSinceHurt>=8;
                    }
                    catch(Exception){/* State inspection cannot prevent releasing our own controller. */}
                    Bound.Animator.runtimeAnimatorController=Bound.Original;
                    if(reset){Bound.Animator.Play(Movement,0,0);Bound.Animator.Update(0);}
                }
            }
            finally
            {
                try{if(Override!=null)UnityEngine.Object.Destroy(Override);}
                finally
                {
                    try{if(Clip!=null)UnityEngine.Object.Destroy(Clip);}
                    finally{Override=null;Clip=null;if(Lease){MasterIdolVisibleGesture.ReleaseSeat(Epoch);Lease=false;}}
                }
            }
        }
    }
}
