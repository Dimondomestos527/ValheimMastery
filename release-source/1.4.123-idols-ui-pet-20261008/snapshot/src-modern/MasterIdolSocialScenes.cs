using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolSocialScenes
    {
        private sealed class Lease
        {
            internal MasterIdolSocial A,B;internal string Home,Kind;internal float End;internal Vector3 Look,GoalA,GoalB;internal List<Vector3> RouteA,RouteB;internal int StepA,StepB;internal ZDO DataA,DataB;internal ZDOMan Manager;internal ZNet Session;internal ulong Generation;internal long OwnerA,OwnerB;internal ushort EpochA,EpochB;
        }
        private sealed class Cooldown{internal float Next;}
        private static readonly ConditionalWeakTable<MasterIdolSocial,Cooldown> Times=new ConditionalWeakTable<MasterIdolSocial,Cooldown>();
        private static readonly Dictionary<MasterIdolSocial,Lease> Actors=new Dictionary<MasterIdolSocial,Lease>();private static readonly Dictionary<string,Lease> Homes=new Dictionary<string,Lease>(StringComparer.Ordinal);
        private static readonly List<Lease> Ending=new List<Lease>();private static readonly List<MasterIdolProp> Props=new List<MasterIdolProp>();private static readonly List<MasterIdolSocialRoutes.Interval> Ranges=new List<MasterIdolSocialRoutes.Interval>();
        internal static bool Active(MasterIdolSocial actor)=>actor!=null&&Actors.ContainsKey(actor);
        private static bool Valid(Lease lease)
        {
            if(lease.A==null||lease.B==null||lease.Session!=ZNet.instance||lease.Manager!=ZDOMan.instance||lease.Generation!=MasterIdolLoadIdentity.Generation||!MasterIdolEffectZones.Ready||!lease.A.SceneAvailable||!lease.B.SceneAvailable)return false;
            var a=lease.A.SceneView?.GetZDO();var b=lease.B.SceneView?.GetZDO();var zone=MasterIdolEffectZones.Find(lease.Home);
            return a!=null&&b!=null&&ReferenceEquals(a,lease.DataA)&&ReferenceEquals(b,lease.DataB)&&a.GetOwner()==lease.OwnerA&&b.GetOwner()==lease.OwnerB&&a.OwnerRevision==lease.EpochA&&b.OwnerRevision==lease.EpochB&&zone!=null&&lease.A.SceneHome?.Id==lease.Home&&lease.B.SceneHome?.Id==lease.Home&&zone.Contains(lease.A.transform.position)&&zone.Contains(lease.B.transform.position);
        }
        private static void End(Lease lease){Actors.Remove(lease.A);Actors.Remove(lease.B);Homes.Remove(lease.Home);if(lease.A!=null&&lease.A.SceneAvailable)lease.A.SceneAi.StopMoving();if(lease.B!=null&&lease.B.SceneAvailable)lease.B.SceneAi.StopMoving();}
        internal static void Tick(){if(Homes.Count==0)return;Ending.Clear();foreach(var lease in Homes.Values)if(Time.unscaledTime>=lease.End||!Valid(lease))Ending.Add(lease);foreach(var lease in Ending)End(lease);}
        internal static void Cancel(MasterIdolSocial actor){if(actor!=null&&Actors.TryGetValue(actor,out var lease))End(lease);}
        internal static bool Idle(MasterIdolSocial actor,MasterIdolEffectZone zone,float dt,bool mayStart)
        {
            if(Actors.TryGetValue(actor,out var lease)){if(!Valid(lease)||Time.unscaledTime>=lease.End){End(lease);return false;}if(lease.Kind=="mimic"){bool first=ReferenceEquals(actor,lease.A);var route=first?lease.RouteA:lease.RouteB;int step=first?lease.StepA:lease.StepB;while(step<route.Count&&(actor.transform.position-route[step]).sqrMagnitude<.36f)step++;if(first)lease.StepA=step;else lease.StepB=step;if(step<route.Count){var current=MasterIdolEffectZones.Find(lease.Home);if(current==null||!MasterIdolSocialRoutes.Inside(current,actor.transform.position,route[step],Array.Empty<Vector3>(),Ranges)){End(lease);return false;}actor.SceneAi.MoveTo(dt,route[step],.5f,false);}else actor.SceneAi.StopMoving();actor.SceneAi.LookAt(first?lease.B.transform.position:lease.GoalB+lease.B.transform.forward);}else{actor.SceneAi.StopMoving();actor.SceneAi.LookAt(lease.Kind=="admire"?lease.Look:ReferenceEquals(actor,lease.A)?lease.B.transform.position:lease.A.transform.position);}return true;}
            if(!mayStart||!actor.SceneAvailable||Homes.Count>=8||Homes.ContainsKey(zone.Id)||!MasterIdolActivity.Near(actor.transform.position))return false;
            var cooldown=Times.GetValue(actor,_=>new Cooldown());if(cooldown.Next==0){cooldown.Next=Time.unscaledTime+UnityEngine.Random.Range(90f,180f);return false;}if(Time.unscaledTime<cooldown.Next)return false;cooldown.Next=Time.unscaledTime+UnityEngine.Random.Range(120f,240f);
            string uid=actor.SceneView.GetZDO().m_uid.ToString();foreach(var member in zone.Residents)if((member.Value=="Greyling"||member.Value=="Greydwarf")&&string.CompareOrdinal(member.Key,uid)<0)return false;
            MasterIdolSocial partner=null;foreach(var member in zone.Residents){if(member.Key==uid||member.Value!="Greyling"&&member.Value!="Greydwarf"||!MasterIdolNetworkQuota.TryUid(member.Key,out long user,out uint id))continue;var other=ZNetScene.instance?.FindInstance(new ZDOID(user,id))?.GetComponent<MasterIdolSocial>();if(other==null||!other.SceneAvailable||Active(other)||other.SceneHome?.Id!=zone.Id)continue;float distance=(other.transform.position-actor.transform.position).sqrMagnitude;if(distance>=4&&distance<=49&&Mathf.Abs(other.transform.position.y-actor.transform.position.y)<=1.5f){partner=other;break;}}
            if(partner==null)return false;int choice=UnityEngine.Random.Range(0,3);var scene=new Lease{A=actor,B=partner,Home=zone.Id,Kind=choice==0?"exchange":choice==1?"mimic":"admire",End=Time.unscaledTime+UnityEngine.Random.Range(3f,5f),Session=ZNet.instance,Generation=MasterIdolLoadIdentity.Generation};
            if(choice==1){bool aSmall=Utils.GetPrefabName(actor.gameObject)=="Greyling",bSmall=Utils.GetPrefabName(partner.gameObject)=="Greyling";if(aSmall==bSmall)return false;if(!aSmall){scene.A=partner;scene.B=actor;}var direction=scene.B.transform.forward;direction.y=0;direction=direction.normalized;scene.GoalB=scene.B.transform.position+direction*1.5f;scene.GoalA=scene.B.transform.position+direction*.25f;if(!Route(scene.A,scene.GoalA,zone,out scene.RouteA)||!Route(scene.B,scene.GoalB,zone,out scene.RouteB))return false;scene.End=Time.unscaledTime+5;}
            if(choice==2){MasterIdolProps.Nearby(actor.transform.position,10,Props);bool found=false;foreach(var prop in Props)if(zone.Contains(prop.transform.position)&&(prop.transform.position-partner.transform.position).sqrMagnitude<100){scene.Look=prop.transform.position;found=true;break;}if(!found)return false;}
            var za=scene.A.SceneView.GetZDO();var zb=scene.B.SceneView.GetZDO();scene.DataA=za;scene.DataB=zb;scene.Manager=ZDOMan.instance;scene.OwnerA=za.GetOwner();scene.OwnerB=zb.GetOwner();scene.EpochA=za.OwnerRevision;scene.EpochB=zb.OwnerRevision;
            Actors[scene.A]=scene;Actors[scene.B]=scene;Homes[zone.Id]=scene;Times.GetValue(partner,_=>new Cooldown()).Next=cooldown.Next;MasterIdolPerf.Count("social.scene."+scene.Kind);return Idle(actor,zone,dt,false);
        }
        private static bool Route(MasterIdolSocial actor,Vector3 goal,MasterIdolEffectZone zone,out List<Vector3> route)
        {route=new List<Vector3>();if(!MasterIdolResidentSpacing.Allowed(actor.SceneCreature,zone,goal,true)||!zone.Contains(goal)||Pathfinding.instance==null||!MasterIdolWorkBudget.Path()||Physics.CheckSphere(goal+Vector3.up,.6f,LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore))return false;return Pathfinding.instance.GetPath(actor.transform.position,goal,route,actor.SceneAi.m_pathAgentType,true,false,false)&&MasterIdolSocialRoutes.Inside(zone,actor.transform.position,goal,route,Ranges);}
    }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolSocialScenesTickPatch{private static void Postfix()=>MasterIdolSocialScenes.Tick();}
}
