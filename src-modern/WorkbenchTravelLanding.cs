using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Only the exact local travel accepted by this provider may retarget native
    // pending fields. No transform, collider, world or ordinary-teleport writes.
    internal static class WorkbenchTravelLanding
    {
        internal const int CandidateLimit=16;
        enum Probe { Pending, Blocked, Clear }
        sealed class Flight
        {
            internal Player Player;internal ZNet Net;internal ZNetScene Scene;internal ZDOMan Manager;
            internal ZNetView View;internal ZDO Character;internal ZDOID CharacterId,Source,Target;
            internal long World,Actor;internal ulong Generation;internal string SourceMarker,TargetMarker,Type;
            internal Vector3 Bench,Expected,From;internal Quaternion Rotation,FromRotation;
            internal Vector3[] Candidates,ReturnCandidates;internal bool Returning,Validated,Noticed;
            internal float NextProbe;
        }
        static Flight Active;
        static readonly Collider[] Blockers=new Collider[32];
        static int FloorMask=>LayerMask.GetMask("Default","static_solid","piece","terrain","vehicle");
        static int BodyMask=>LayerMask.GetMask("Default","static_solid","piece","terrain","vehicle","character","character_net");
        internal static bool ValidRotation(Quaternion q)
        {
            if(!Finite(q.x)||!Finite(q.y)||!Finite(q.z)||!Finite(q.w))return false;
            float length=q.x*q.x+q.y*q.y+q.z*q.z+q.w*q.w;return length>.99f&&length<1.01f;
        }
        static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        internal static Vector3[] AuthorizedCandidates(WorkbenchTravelEndpoint target,long actor)
        {
            var points=new List<Vector3>(CandidateLimit);
            if(target==null||ZNet.instance?.IsServer()!=true||!Finite(target.Point)||!ValidRotation(target.Rotation))return points.ToArray();
            for(int i=0;i<CandidateLimit;i++)
            {
                float angle=(i%8)*Mathf.PI/4f,radius=i<8?2.5f:3.5f;
                var point=target.Point+target.Rotation*new Vector3(Mathf.Sin(angle)*radius,.5f,Mathf.Cos(angle)*radius);
                if(Finite(point)&&MasterIdolWorldIndex.WardAllows(actor,point))points.Add(point);
            }
            return points.ToArray();
        }
        internal static bool Begin(Player player,ZDOID source,string sourceMarker,WorkbenchTravelDestination target,
            string targetMarker,Vector3 bench,Quaternion rotation,Vector3[] candidates)
        {
            if(Active!=null||!MasterIdolPlains.Owned(player)||player.IsDead()||player.IsTeleporting()||
                !OwnerSkillAuthority.Has(player,Skills.SkillType.Crafting,100)||!MasterIdolTravel.PortalRules(player)||
                target==null||target.Id==source||!Guid.TryParseExact(sourceMarker,"N",out _)||!Guid.TryParseExact(targetMarker,"N",out _)||
                !WorkbenchEnchantmentRules.Known(target.EnchantmentId)||!Finite(bench)||!ValidRotation(rotation)||
                candidates==null||candidates.Length<1||candidates.Length>CandidateLimit||ZNet.instance?.GetWorld()==null||
                ZNetScene.instance==null||ZDOMan.instance==null)return false;
            var data=player.m_nview.GetZDO();if(data==null||data.m_uid==ZDOID.None)return false;
            var points=(Vector3[])candidates.Clone();
            foreach(var point in points)if(!Finite(point)||(point-bench).sqrMagnitude>25f)return false;
            var run=new Flight{Player=player,Net=ZNet.instance,Scene=ZNetScene.instance,Manager=ZDOMan.instance,
                View=player.m_nview,Character=data,CharacterId=data.m_uid,Actor=player.GetPlayerID(),World=ZNet.instance.GetWorldUID(),
                Generation=MasterIdolLoadIdentity.Generation,Source=source,SourceMarker=sourceMarker,Target=target.Id,
                TargetMarker=targetMarker,Type=target.EnchantmentId,Bench=bench,Rotation=rotation,Candidates=points};
            // Validate the original location while it is still physically loaded.
            if(FindSafe(run,new[]{player.transform.position},out var safeFrom)!=Probe.Clear)return false;
            run.From=safeFrom;run.FromRotation=player.transform.rotation;run.Expected=points[0];
            run.ReturnCandidates=ReturnPoints(safeFrom,run.FromRotation);
            Active=run;
            bool accepted;
            try{accepted=player.TeleportTo(run.Expected,rotation,true);}
            catch{Reset();throw;}
            if(!accepted){Reset();return false;}
            // Native stores the real source rotation; do not invent a new facing.
            run.FromRotation=player.m_teleportFromRot;
            try{WorkbenchTravelLoadingRune.Begin(player,run.Type);}catch{}
            return true;
        }
        static Vector3[] ReturnPoints(Vector3 from,Quaternion rotation)
        {
            var points=new Vector3[9];points[0]=from;
            for(int i=1;i<points.Length;i++)
            {float angle=(i-1)*Mathf.PI/4f;points[i]=from+rotation*new Vector3(Mathf.Sin(angle)*1.25f,0,Mathf.Cos(angle)*1.25f);}
            return points;
        }
        static bool Identity(Flight run)=>run!=null&&ReferenceEquals(run,Active)&&run.Net==ZNet.instance&&run.Scene==ZNetScene.instance&&
            run.Manager==ZDOMan.instance&&run.Generation==MasterIdolLoadIdentity.Generation&&run.Net?.GetWorld()!=null&&run.World==run.Net.GetWorldUID()&&
            run.Player!=null&&run.Player==Player.m_localPlayer&&run.Player.GetPlayerID()==run.Actor&&run.Player.m_nview==run.View&&run.View?.IsOwner()==true&&
            ReferenceEquals(run.View.GetZDO(),run.Character)&&run.Character.m_uid==run.CharacterId;
        static bool Expected(Flight run)=>
            (run.Player.m_teleportTargetPos-run.Expected).sqrMagnitude<=.0001f&&Quaternion.Angle(run.Player.m_teleportTargetRot,run.Rotation)<=.1f;
        static bool DestinationCurrent(Flight run)
        {
            var data=run.Manager.GetZDO(run.Target);var instance=run.Scene.FindInstance(run.Target);
            if(data==null||instance==null||instance.GetComponent<ZNetView>()?.GetZDO()!=data||
                data.GetPrefab()!="piece_workbench".GetStableHashCode()||data.GetString(WorkbenchEnchantmentRuntime.StationKey,"")!=run.TargetMarker||
                (data.GetPosition()-run.Bench).sqrMagnitude>.01f||Quaternion.Angle(data.GetRotation(),run.Rotation)>1f)return false;
            var zone=MasterIdolEffectZones.Find(run.Target+"/"+run.TargetMarker);
            return zone!=null&&zone.Type==run.Type&&(zone.Home-run.Bench).sqrMagnitude<=.01f;
        }
        static Probe FindSafe(Flight run,Vector3[] candidates,out Vector3 point)
        {
            point=default;bool pending=false;var player=run.Player;
            float radius=player.GetRadius(),height=player.GetHeight();
            if(!Finite(radius)||!Finite(height)||radius<=0||height<radius*2f||FloorMask==0||BodyMask==0)return Probe.Blocked;
            foreach(var candidate in candidates)
            {
                if(!run.Scene.IsAreaReady(candidate)){pending=true;continue;}
                if(!Physics.Raycast(candidate+Vector3.up*2.5f,Vector3.down,out var hit,6f,FloorMask,QueryTriggerInteraction.Ignore)||
                    !Finite(hit.point)||hit.normal.y<.65f||Mathf.Abs(hit.point.y-candidate.y)>2f)continue;
                var feet=hit.point+Vector3.up*.08f;
                if(ZoneSystem.instance==null||!ZoneSystem.instance.FindFloor(feet,out float nativeFloor)||!Finite(nativeFloor)||Mathf.Abs(nativeFloor-hit.point.y)>.25f)continue;
                if(!PrivateArea.CheckAccess(feet,0f,false,false))continue;
                int count=Physics.OverlapCapsuleNonAlloc(feet+Vector3.up*(radius+.02f),feet+Vector3.up*(height-radius+.02f),
                    radius+.02f,Blockers,BodyMask,QueryTriggerInteraction.Ignore);
                bool clear=count<Blockers.Length;
                for(int i=0;i<count&&i<Blockers.Length;i++)
                {
                    var blocker=Blockers[i];Blockers[i]=null;
                    if(blocker!=null&&!blocker.transform.IsChildOf(player.transform))clear=false;
                }
                if(clear){point=feet;return Probe.Clear;}
            }
            return pending?Probe.Pending:Probe.Blocked;
        }
        static void Hold(Player player,ref float dt)
        {
            // Native still advances beyond2s and moves its own transform/load center.
            // Only its >8s completion / >15s solid-height fallback stays unreachable
            // while this exact owned flight lacks a validated floor and capsule.
            if(!Finite(dt)||dt<0)dt=0;
            if(player.m_teleportTimer+dt>=7.5f)
            {player.m_teleportTimer=Mathf.Min(player.m_teleportTimer,7f);dt=Mathf.Min(dt,.1f);}
        }
        static void Return(Flight run)
        {
            run.Returning=true;run.Validated=false;run.Expected=run.From;run.Rotation=run.FromRotation;run.NextProbe=0;
            run.Player.m_teleportTargetPos=run.Expected;run.Player.m_teleportTargetRot=run.Rotation;
            if(!run.Noticed){run.Noticed=true;Notice("Немає безпечного місця прибуття. Очікую на вільний простір для повернення.");}
        }
        internal static void BeforeNative(Player player,ref float dt)
        {
            var run=Active;if(run==null||run.Player!=player)return;
            if(!Identity(run)||!player.IsTeleporting()||!Expected(run)){Reset();return;}
            if(player.IsDead()){Reset();return;}
            if(!run.Scene.IsAreaReady(run.Expected)){run.Validated=false;Hold(player,ref dt);return;}
            bool completing=player.m_teleportTimer+dt>8f;
            if(Time.unscaledTime<run.NextProbe&&!completing)
            {if(!run.Validated)Hold(player,ref dt);return;}
            run.NextProbe=Time.unscaledTime+.25f;
            if(!run.Returning&&(!MasteryPlugin.Settings.Enabled.Value||!OwnerSkillAuthority.Has(player,Skills.SkillType.Crafting,100)||
                !MasterIdolTravel.PortalRules(player)||!DestinationCurrent(run)))
            {Return(run);Hold(player,ref dt);return;}
            var status=FindSafe(run,run.Returning?run.ReturnCandidates:run.Candidates,out var safe);
            if(status==Probe.Clear)
            {run.Validated=true;run.Expected=safe;player.m_teleportTargetPos=safe;return;}
            run.Validated=false;
            if(status==Probe.Blocked&&!run.Returning)Return(run);
            Hold(player,ref dt);
        }
        internal static void Tick()
        {
            var run=Active;if(run!=null&&(!Identity(run)||!run.Player.IsTeleporting()||!Expected(run)))Reset();
        }
        internal static void Reset()
        {Active=null;try{WorkbenchTravelLoadingRune.End();}catch{}}
        static void Notice(string text)=>Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"<color=#FFD36A>"+text+"</color>");
    }
    [HarmonyPatch(typeof(Player),"UpdateTeleport",new[]{typeof(float)})]
    internal static class WorkbenchTravelOwnedLandingPatch
    {
        static void Prefix(Player __instance,ref float __0)=>WorkbenchTravelLanding.BeforeNative(__instance,ref __0);
        static void Postfix(Player __instance)=>WorkbenchTravelLanding.Tick();
    }
}
