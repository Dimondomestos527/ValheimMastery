using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal enum ResidentDanceOwnerStep:byte { Approaching,Ready,WaitingStart,Dancing,Completed,Interrupted }
    // Physical owner only. Native AI moves; renderer ownership is kept in a separate presentation lease.
    internal sealed class ResidentDanceOwnerLease:IDisposable
    {
        internal readonly ResidentDanceTicket Ticket;internal readonly ResidentDanceMember Member;
        internal readonly Character Actor;
        private readonly Character Partner;private readonly ZNetView View,PartnerView;private readonly BaseAI Ai;
        private readonly ZDO Data,PartnerData;private readonly ZNet Net;private readonly ZDOMan Manager;private readonly ulong Generation;
        private readonly List<Vector3> Route=new List<Vector3>(32);private readonly List<MasterIdolSocialRoutes.Interval> Ranges=new List<MasterIdolSocialRoutes.Interval>(8);
        private readonly string Spot;private Vector3 Goal;private bool Disposed,Arrived,SpotPublished;private int Corner;
        private long Start;private double Deadline,NextSpot;
        private ResidentDanceOwnerLease(Character actor,Character partner,ResidentDanceTicket ticket,ResidentDanceMember member)
        {
            Actor=actor;Partner=partner;Ticket=ticket;Member=member;View=actor.m_nview;PartnerView=partner.m_nview;Ai=actor.GetComponent<BaseAI>();Data=View.GetZDO();PartnerData=PartnerView.GetZDO();
            Net=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;Spot=ticket.Nonce;Goal=member.Goal;
            double remaining=(ticket.PrepareUntil-Net.GetTime().Ticks)/(double)TimeSpan.TicksPerSecond;Deadline=Time.unscaledTimeAsDouble+Math.Max(0,remaining);
        }
        private static bool Identity(Character actor,ZDO data,ResidentDanceMember member,int family,bool owner)
            =>actor!=null&&member!=null&&actor.m_nview?.IsValid()==true&&(!owner||actor.m_nview.IsOwner())&&data!=null&&ReferenceEquals(actor.m_nview.GetZDO(),data)&&ReferenceEquals(ZDOMan.instance?.GetZDO(member.Uid),data)&&
                data.m_uid==member.Uid&&data.GetOwner()==member.Owner&&data.OwnerRevision==member.Revision&&MasterIdolForestGuardState.ModuleBorn(data)&&MasterIdolForestGuardState.Family(data)==family&&
                Utils.GetPrefabName(actor.gameObject)==MasterIdolForestRules.Families[family]&&data.GetLong(MasterIdolForestGuardState.BornKey,0)==member.Born&&data.GetString(MasterIdolForestGuardState.NonceKey,"")==member.BirthNonce;
        private static bool HardPeace(Character actor)
        {
            var ai=actor?.GetComponent<BaseAI>();
            return actor!=null&&ai!=null&&!actor.IsDead()&&float.IsFinite(actor.GetHealth())&&actor.GetHealth()>0&&!MasterIdolSocialSeats.Battle(ai,actor)&&ai.m_timeSinceHurt>=8&&!ai.IsSleeping()&&!actor.InDodge()&&!actor.InEmote()&&
                !MasterIdolResidentWork.Busy(actor)&&!MasterIdolResidentUniqueActions.Busy(actor)&&!MasterIdolResidentCargo.Busy(actor)&&!MasterIdolResidentBirthPresentation.Playing(actor)&&!MasterIdolResidentBirthPresentation.FreshIdleHold(actor)&&
                !MasterIdolResidentSceneAnimation.Playing(actor)&&!MasterIdolVisibleGesture.Playing(actor)&&actor.GetComponent<MasterIdolResidentPresentation>()?.Playing!=true&&actor.GetComponent<MasterIdolWelcome>()?.IsPresenting!=true&&actor.GetComponent<MasterIdolSocialSeats>()?.Active!=true;
        }
        private static bool StockModel(Character actor,int family)
        {
            var animator=actor.GetComponentInChildren<Animator>();var native=ZNetScene.instance?.GetPrefab(MasterIdolForestRules.Families[family])?.GetComponentInChildren<Animator>(true);
            return animator!=null&&native!=null&&animator.avatar==native.avatar&&animator.runtimeAnimatorController==native.runtimeAnimatorController&&!(animator.runtimeAnimatorController is AnimatorOverrideController)&&!animator.applyRootMotion&&animator.layerCount==1;
        }
        private bool Current(out MasterIdolEffectZone home)
        {
            home=null;int family=Ticket.First.Uid==Member.Uid?0:1;var other=Ticket.Partner(Member.Uid);
            return !Disposed&&Net==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady&&GoldCraftingService.Enabled&&
                Net?.GetWorld()!=null&&Net.GetWorldUID()==Ticket.World&&Identity(Actor,Data,Member,family,true)&&Identity(Partner,PartnerData,other,1-family,false)&&
                MasterIdolResidentPolicy.TryHome(Actor,out home)&&home.Type=="BlackForest"&&home.Id==Ticket.Home&&home.Contains(Actor.transform.position)&&
                MasterIdolResidentPolicy.TryHome(Partner,out var partnerHome)&&partnerHome.Id==home.Id&&home.Contains(Partner.transform.position)&&
                HardPeace(Actor)&&HardPeace(Partner)&&MasterIdolResidentPolicy.Friendly(Actor,Partner)&&MasterIdolResidentPolicy.Friendly(Partner,Actor)&&
                !Ashlands70Madness.SourceMarkedCurrentData(Data)&&!Ashlands70Madness.SourceMarkedCurrentData(PartnerData);
        }
        internal bool IsCurrent()=>Current(out _)&&Time.unscaledTimeAsDouble<=Deadline&&(Start!=0||Net.GetTime().Ticks<=Ticket.PrepareUntil);
        internal bool OwnsBody(Character actor)
        {
            int family=Ticket.First.Uid==Member.Uid?0:1;
            return !Disposed&&ReferenceEquals(actor,Actor)&&Net==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&Net?.GetWorld()!=null&&Net.GetWorldUID()==Ticket.World&&Identity(Actor,Data,Member,family,true);
        }
        internal static bool TryCreate(ResidentDanceTicket ticket,ResidentDanceMember member,out ResidentDanceOwnerLease lease)
        {
            lease=null;
            if(ticket==null||member==null||ZNet.instance?.GetWorld()==null||ticket.World!=ZNet.instance.GetWorldUID()||member.Owner!=ZNet.GetUID()||ticket.PrepareUntil<=ZNet.instance.GetTime().Ticks||
                ticket.Issued>ZNet.instance.GetTime().Ticks+TimeSpan.TicksPerSecond||ZNet.instance.GetTime().Ticks-ticket.Issued>(long)(ResidentDanceRules.PrepareSeconds*TimeSpan.TicksPerSecond))return false;
            int family=ticket.First.Uid==member.Uid?0:ticket.Second.Uid==member.Uid?1:-1;if(family<0)return false;
            var actor=ZNetScene.instance?.FindInstance(member.Uid)?.GetComponent<Character>();var partner=ZNetScene.instance?.FindInstance(ticket.Partner(member.Uid).Uid)?.GetComponent<Character>();
            if(actor==null||partner==null||!StockModel(actor,family)||actor.GetComponent<MasterIdolResidentWork>()==null)return false;
            ResidentDanceOwnerLease value=null;
            try
            {
                value=new ResidentDanceOwnerLease(actor,partner,ticket,member);
                if(!value.Current(out var home)||ZoneSystem.instance==null||Pathfinding.instance==null||!ResidentDanceRules.Finite(value.Goal)||!home.Contains(value.Goal)||!ZoneSystem.instance.IsZoneLoaded(value.Goal)||!MasterIdolResidentSpacing.Allowed(actor,home,value.Goal,true))return false;
                // One local support query also respects built floors. The actor is moved only by native AI.
                if(!Physics.Raycast(value.Goal+Vector3.up*1.5f,Vector3.down,out var support,4f,LayerMask.GetMask("terrain","piece","static_solid","Default"),QueryTriggerInteraction.Ignore))return false;
                var point=support.point;var normal=support.normal;
                if(support.collider==null||!support.collider.enabled||support.collider.isTrigger||!support.collider.gameObject.activeInHierarchy||!ResidentDanceRules.Finite(point)||!ResidentDanceRules.Finite(normal)||normal.y<.7f||!home.Contains(point)||point.y<ZoneSystem.instance.m_waterLevel+.15f||
                    Physics.CheckSphere(point+Vector3.up,.6f,LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore)||!MasterIdolResidentBudget.Path())return false;
                if(!Pathfinding.instance.GetPath(actor.transform.position,point,value.Route,value.Ai.m_pathAgentType,true,false,false)||value.Route.Count>128||!MasterIdolSocialRoutes.Inside(home,actor.transform.position,point,value.Route,value.Ranges))return false;
                value.Goal=point;value.PublishSpot();lease=value;value=null;return true;
            }
            catch(Exception){return false;}
            finally{if(value!=null)try{value.Dispose();}catch(Exception){}}
        }
        private void PublishSpot()
        {
            if(!Current(out var home))return;
            SpotPublished=true;Actor.GetComponent<MasterIdolResidentWork>()?.Spot(home.Id,Spot,Goal,10,false,true);NextSpot=Time.unscaledTimeAsDouble+5;
        }
        internal bool Schedule(ResidentDanceTicket ticket)
        {
            if(!Arrived||!ResidentDanceRules.Same(Ticket,ticket)||ticket.Start<=0||Start!=0&&Start!=ticket.Start||!Current(out _))return false;
            Start=ticket.Start;double remaining=(Start-Net.GetTime().Ticks)/(double)TimeSpan.TicksPerSecond+ResidentDanceRules.DanceSeconds+1;
            Deadline=Time.unscaledTimeAsDouble+Math.Max(0,remaining);return true;
        }
        internal ResidentDanceOwnerStep Step(float dt)
        {
            if(!float.IsFinite(dt)||dt<0||!Current(out var home)||Time.unscaledTimeAsDouble>Deadline)return ResidentDanceOwnerStep.Interrupted;
            long now=Net.GetTime().Ticks;
            if(Start==0&&now>Ticket.PrepareUntil)return ResidentDanceOwnerStep.Interrupted;
            if(Start!=0&&now>=Start+(long)((ResidentDanceRules.DanceSeconds+ResidentDanceRules.BlendSeconds+.1f)*TimeSpan.TicksPerSecond))return ResidentDanceOwnerStep.Completed;
            if(Start!=0&&now<Start-TimeSpan.TicksPerSecond)return ResidentDanceOwnerStep.Interrupted;
            if(Time.unscaledTimeAsDouble>=NextSpot)PublishSpot();
            if(!MasterIdolResidentSpacing.Allowed(Actor,home,Goal,true)||!MasterIdolResidentSpacing.Allowed(Actor,home,Actor.transform.position,true))return ResidentDanceOwnerStep.Interrupted;
            var difference=Goal-Actor.transform.position;
            if(Start!=0||Arrived)
            {
                if(difference.sqrMagnitude>ResidentDanceRules.ArrivalDistance*ResidentDanceRules.ArrivalDistance||!MasterIdolResidentGrounding.Slow(Actor))return ResidentDanceOwnerStep.Interrupted;
                Ai.StopMoving();Ai.LookAt(Partner.transform.position);
                return Start==0?ResidentDanceOwnerStep.Ready:now<Start?ResidentDanceOwnerStep.WaitingStart:ResidentDanceOwnerStep.Dancing;
            }
            if(difference.sqrMagnitude<=ResidentDanceRules.ArrivalDistance*ResidentDanceRules.ArrivalDistance)
            {
                Ai.StopMoving();Ai.LookAt(Partner.transform.position);var facing=Partner.transform.position-Actor.transform.position;facing.y=0;
                if(MasterIdolResidentGrounding.Slow(Actor)&&facing.sqrMagnitude>.25f&&Vector3.Angle(Actor.transform.forward,facing)<=12){Arrived=true;return ResidentDanceOwnerStep.Ready;}
                return ResidentDanceOwnerStep.Approaching;
            }
            if(!MasterIdolSocialRoutes.Inside(home,Actor.transform.position,Goal,Route,Ranges))return ResidentDanceOwnerStep.Interrupted;
            while(Corner<Route.Count&&(Actor.transform.position-Route[Corner]).sqrMagnitude<.64f)Corner++;
            Ai.MoveTo(dt,Corner<Route.Count?Route[Corner]:Goal,.35f,false);return ResidentDanceOwnerStep.Approaching;
        }
        public void Dispose()=>DisposeCore(true);
        internal void ResetLocal()=>DisposeCore(false);
        private void DisposeCore(bool publish)
        {
            if(Disposed)return;Disposed=true;
            try{if(SpotPublished)MasterIdolResidentSpacing.Remove(Actor,Ticket.Home,Spot);}
            finally
            {
                Route.Clear();
                if(SpotPublished&&Net==ZNet.instance&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&Net?.GetWorld()!=null&&Net.GetWorldUID()==Ticket.World&&
                    Actor!=null&&View?.IsValid()==true&&View.IsOwner()&&ReferenceEquals(View.GetZDO(),Data)&&Data.GetOwner()==Member.Owner&&Data.OwnerRevision==Member.Revision)
                {
                    try{if(publish)Actor.GetComponent<MasterIdolResidentWork>()?.Spot(Ticket.Home,Spot,Goal,0,false,false);}
                    finally{Ai?.StopMoving();}
                }
            }
        }
    }
}
