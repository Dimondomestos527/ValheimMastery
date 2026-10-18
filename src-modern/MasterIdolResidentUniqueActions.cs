using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolResidentUniqueActions:MonoBehaviour
    {
        private Character Actor;private BaseAI Ai;private ZNetView View;private ResidentUniqueTicket OwnerTicket,RenderTicket;
        private ResidentUniqueOutcome OwnerOutcome;private float OwnerUntil;private long RenderStamp;private string LastCue,LastAccepted;private ResidentUniqueOutcome LastOutcome;
        private ZNet Session;private ZDOMan Manager;private ulong Generation;private long World,Owner;private ushort Revision;private ZDO Data;
        private MasterIdolResidentUniquePresentation Presentation;private MasterIdolResidentUniqueVoice Voice;private bool FallingBack;private float NextRequest;
        private void Awake(){Actor=GetComponent<Character>();Ai=GetComponent<BaseAI>();View=GetComponent<ZNetView>();}
        private static MasterIdolResidentUniqueActions Component(Character actor)
            =>actor?.GetComponent<MasterIdolResidentUniqueActions>()??actor?.gameObject.AddComponent<MasterIdolResidentUniqueActions>();
        private bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;var data=View?.GetZDO();if(net?.GetWorld()==null||View?.IsValid()!=true||!MasterIdolLoadIdentity.LoadReady||!MasterIdolForestGuardState.ModuleBorn(data)||!ReferenceEquals(manager?.GetZDO(data.m_uid),data)){Stop(false);return false;}
            if(Session!=net||Manager!=manager||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID()||!ReferenceEquals(Data,data)||Owner!=data.GetOwner()||Revision!=data.OwnerRevision)
            {Stop(false);LastCue=LastAccepted=null;Session=net;Manager=manager;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();Data=data;Owner=data.GetOwner();Revision=data.OwnerRevision;}
            return true;
        }
        private bool Peace()
            =>Actor!=null&&Ai!=null&&!Actor.IsDead()&&float.IsFinite(Actor.GetHealth())&&Actor.GetHealth()>0&&!MasterIdolSocialSeats.Battle(Ai,Actor)&&Ai.m_timeSinceHurt>=8&&!Ai.IsSleeping()&&!Actor.InDodge()&&!Actor.InEmote()&&MasterIdolResidentGrounding.Slow(Actor)&&
                !MasterIdolResidentBirthPresentation.Playing(Actor)&&!MasterIdolResidentBirthPresentation.FreshIdleHold(Actor)&&GetComponent<MasterIdolSocialSeats>()?.Active!=true&&GetComponent<MasterIdolWelcome>()?.IsPresenting!=true;
        internal static bool Busy(Character actor)
        {var value=actor?.GetComponent<MasterIdolResidentUniqueActions>();return value!=null&&!value.FallingBack&&(value.OwnerTicket!=null||value.RenderTicket!=null);}
        internal static bool TryRequest(Character actor,Player player)
        {
            if(!MasterIdolForestGuardState.ModuleBorn(actor?.m_nview?.GetZDO())||!MasterIdolResidentUniqueRules.TryApprovedFamilyChoice(MasterIdolForestGuardState.Family(actor.m_nview.GetZDO()),0,out _))return false;var value=Component(actor);
            if(value==null||player==null||player!=Player.m_localPlayer||!value.Context()||Time.unscaledTime<value.NextRequest)return true;
            value.NextRequest=Time.unscaledTime+.35f;
            if(!value.Peace()||player.IsDead()||player.InGhostMode()||player.IsDebugFlying()||player.InAttack()||player.InPlaceMode())return true;
            MasterIdolResidentUniqueProtocol.Send(actor,player,ResidentUniqueKind.Family,0);return true;
        }
        internal static bool TryReact(Character actor,Player player)
        {
            var value=Component(actor);if(value==null||value.FallingBack||!value.Context()||!value.View.IsOwner()||!value.Peace()||Busy(actor)||player==null)return false;
            int id=player.m_nview?.GetZDO()?.GetInt(ZDOVars.s_emoteID,0)??0;string name=player.m_nview?.GetZDO()?.GetString(ZDOVars.s_emote,"")??"";int index=MasterIdolResidentUniqueRules.Gesture(name);
            if(id<=0||index<0||!MasterIdolResidentUniqueRules.MotionApproved(MasterIdolForestGuardState.Family(value.Data),ResidentUniqueKind.Gesture,(byte)index))return false;
            var probe=new ResidentUniqueTicket{Kind=ResidentUniqueKind.Gesture,Choice=(byte)index};if(!MasterIdolResidentUniquePresentation.CanPrepare(actor,probe)&&!MasterIdolResidentDance.CanYield(actor))return false;
            return MasterIdolResidentUniqueProtocol.Send(actor,player,ResidentUniqueKind.Gesture,id);
        }
        internal static ResidentUniqueOutcome Accept(ResidentUniqueTicket ticket)
        {
            if(!MasterIdolResidentUniqueProtocol.Current(ticket,ZNet.instance?.IsServer()==true,out var actor,out var player)||actor==null||player==null||actor.m_nview?.IsOwner()!=true)return ResidentUniqueOutcome.Denied;
            var value=Component(actor);if(value==null||!value.Context())return ResidentUniqueOutcome.Denied;
            if(value.LastAccepted==ticket.Nonce)return value.OwnerTicket!=null?value.LastOutcome:ResidentUniqueOutcome.Denied;
            value.LastAccepted=ticket.Nonce;value.LastOutcome=ResidentUniqueOutcome.Denied;
            if(!value.Peace()||Busy(actor)||MasterIdolResidentWork.Busy(actor)||MasterIdolResidentSceneAnimation.Playing(actor)||player.IsDead()||player.InAttack()||player.InPlaceMode())return ResidentUniqueOutcome.Denied;
            if(!MasterIdolResidentCargo.Cancel(actor)||MasterIdolResidentCargo.Busy(actor)||!value.Context()||!value.View.IsOwner()||
                !MasterIdolResidentUniqueProtocol.Current(ticket,ZNet.instance?.IsServer()==true,out var currentActor,out var currentPlayer)||currentActor!=actor||currentPlayer!=player)return ResidentUniqueOutcome.Denied;
            MasterIdolResidentDance.Cancel(actor,"unique.accept");
            if(MasterIdolResidentCargo.Busy(actor)||!value.Context()||!value.View.IsOwner()||!value.Peace()||Busy(actor)||MasterIdolResidentWork.Busy(actor)||MasterIdolResidentSceneAnimation.Playing(actor)||player.IsDead()||player.InAttack()||player.InPlaceMode()||
                !MasterIdolResidentUniqueProtocol.Current(ticket,ZNet.instance?.IsServer()==true,out var afterActor,out var afterPlayer)||afterActor!=actor||afterPlayer!=player)return ResidentUniqueOutcome.Denied;
            MasterIdolSocialScenes.Cancel(actor.GetComponent<MasterIdolSocial>());MasterIdolVisibleGesture.Cancel(actor);
            if(!MasterIdolResidentUniquePresentation.CanPrepare(actor,ticket))return ResidentUniqueOutcome.Denied;
            value.OwnerTicket=ticket;value.OwnerOutcome=ResidentUniqueOutcome.Visual;value.OwnerUntil=Time.unscaledTime+MasterIdolResidentUniqueRules.Seconds(MasterIdolForestGuardState.Family(value.Data),ticket.Kind,ticket.Choice)+1;
            value.Ai.StopMoving();value.Ai.LookAt(player.transform.position);
            if(ticket.Kind==ResidentUniqueKind.Family&&MasterIdolForestGuardState.Family(value.Data)==3)
            {
                var result=MasterIdolResidentWork.TryInteractionHeal(actor);
                if(result==ResidentInteractionHealOutcome.Requested)value.OwnerOutcome=ResidentUniqueOutcome.HealRequested;
                else if(result!=ResidentInteractionHealOutcome.NoTargets){value.Stop(false);return ResidentUniqueOutcome.Denied;}
            }
            value.LastOutcome=value.OwnerOutcome;return value.OwnerOutcome;
        }
        internal static void BeginCue(Character actor,Player player,ResidentUniqueTicket ticket,ResidentUniqueOutcome outcome,long stamp)
        {
            var value=Component(actor);if(value==null||!value.Context()||value.LastCue==ticket.Nonce)return;
            value.LastCue=ticket.Nonce;value.ClearGraphics();float elapsed=(float)((ZNet.instance.GetTime().Ticks-stamp)/(double)TimeSpan.TicksPerSecond);
            float duration=MasterIdolResidentUniqueRules.Seconds(MasterIdolForestGuardState.Family(value.Data),ticket.Kind,ticket.Choice);
            if(elapsed<0||elapsed>=duration||!value.Peace())return;
            if(value.OwnerTicket?.Nonce==ticket.Nonce)value.OwnerUntil=Time.unscaledTime+duration-elapsed;
            if(!MasterIdolResidentVisibility.Local(actor))return;
            value.RenderTicket=ticket;value.RenderStamp=stamp;
            if(MasteryPlugin.Settings.EnablePerkProcVFX.Value)value.Presentation=MasterIdolResidentUniquePresentation.Begin(actor,ticket,outcome,elapsed);
            if(ticket.Kind==ResidentUniqueKind.Gesture&&value.Presentation==null&&value.View.IsOwner())
            {
                value.Stop(true);value.FallingBack=true;
                try{MasterIdolSocialScenes.React(actor.GetComponent<MasterIdolSocial>(),player);}
                finally{value.FallingBack=false;}
                MasterIdolResidentMetrics.Count("unique.gesture-own-family-fallback-requested");return;
            }
            byte phrase=ticket.Kind==ResidentUniqueKind.Family&&MasterIdolForestGuardState.Family(value.Data)==0?(byte)0:ticket.Choice;if(ticket.Kind==ResidentUniqueKind.Gesture)phrase=new byte[]{0,8,4,1,7,2,9,8,4,6}[ticket.Choice];
            value.Voice=MasterIdolResidentUniqueVoice.Begin(actor,MasterIdolForestGuardState.Family(value.Data),phrase,duration);
            if(ticket.Kind==ResidentUniqueKind.Family&&MasterIdolForestGuardState.Family(value.Data)==0&&Chat.instance!=null)
                Chat.instance.SetNpcText(actor.gameObject,Vector3.up*(actor.GetHeight()+.2f),10,3.5f,actor.GetHoverName(),MasterIdolResidentUniqueRules.Joke(ticket.Choice),false);
            MasterIdolResidentMetrics.Count("unique.trusted-cue-consumed");
        }
        internal static void EndCue(ResidentUniqueTicket ticket)
        {var actor=ZNetScene.instance?.FindInstance(ticket.Actor)?.GetComponent<Character>();var value=actor?.GetComponent<MasterIdolResidentUniqueActions>();if(value?.RenderTicket?.Nonce==ticket.Nonce||value?.OwnerTicket?.Nonce==ticket.Nonce)value.Stop(false);}
        internal static bool Idle(Character actor,float dt)
        {
            var value=actor?.GetComponent<MasterIdolResidentUniqueActions>();
            if(value==null||value.OwnerTicket==null||!value.Context()||!value.View.IsOwner()||!value.Peace())return false;
            value.Ai.StopMoving();var player=ZNetScene.instance?.FindInstance(value.OwnerTicket.Player)?.GetComponent<Player>();if(player!=null)value.Ai.LookAt(player.transform.position);return true;
        }
        private void Update()
        {
            try
            {
                if(!Context())return;
                if(OwnerTicket!=null&&(Time.unscaledTime>=OwnerUntil||!Peace()||!MasterIdolResidentUniqueProtocol.Current(OwnerTicket,ZNet.instance.IsServer(),out _,out _,false))){Stop(true);return;}
                if(RenderTicket!=null&&(!Peace()||!MasterIdolResidentVisibility.Local(Actor)||!MasterIdolResidentUniqueProtocol.Current(RenderTicket,ZNet.instance.IsServer(),out _,out _,false)))ClearGraphics();
            }
            catch(Exception){Stop(true);}
        }
        private void LateUpdate()
        {
            try
            {
                if(RenderTicket==null)return;float elapsed=(float)((ZNet.instance.GetTime().Ticks-RenderStamp)/(double)TimeSpan.TicksPerSecond);
                float duration=MasterIdolResidentUniqueRules.Seconds(MasterIdolForestGuardState.Family(Data),RenderTicket.Kind,RenderTicket.Choice);
                if(elapsed<0||elapsed>=duration){ClearGraphics();return;}
                if(Presentation!=null&&!Presentation.Tick(elapsed)){Presentation.Dispose();Presentation=null;}
                if(Voice!=null&&!Voice.Tick(elapsed)){Voice.Dispose();Voice=null;}
            }
            catch(Exception){ClearGraphics();}
        }
        private void ClearGraphics()
        {
            RenderTicket=null;
            try{Presentation?.Dispose();}finally{Presentation=null;try{Voice?.Dispose();}finally{Voice=null;}}
        }
        private void Stop(bool notify)
        {
            var ticket=OwnerTicket;var outcome=OwnerOutcome;OwnerTicket=null;
            try{ClearGraphics();}finally{if(notify&&ticket!=null&&View?.IsOwner()==true)MasterIdolResidentUniqueProtocol.Decision(ticket,outcome,4,true);}
        }
        internal static void Cancel(Character actor)=>actor?.GetComponent<MasterIdolResidentUniqueActions>()?.Stop(true);
        private void OnDisable()=>Stop(true);private void OnDestroy()=>Stop(false);
    }
    [HarmonyPatch(typeof(BaseAI),"Awake")]
    internal static class MasterIdolResidentUniqueAttachPatch
    {private static void Postfix(BaseAI __instance){if(MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject))>0&&__instance.GetComponent<MasterIdolResidentUniqueActions>()==null)__instance.gameObject.AddComponent<MasterIdolResidentUniqueActions>();}}
}
