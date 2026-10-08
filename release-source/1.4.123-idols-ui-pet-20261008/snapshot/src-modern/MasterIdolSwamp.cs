using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolSanctuary
    {internal long Visit;internal string Id;internal Vector3 Center;internal float Radius;internal MasterIdolEffectCircle[] Coverage;}
    internal static class MasterIdolSwamp
    {
        private static long Sweep;private static MasterIdolEffectGeometry Source;
        private static readonly Dictionary<string,MasterIdolSanctuary> Previous=new Dictionary<string,MasterIdolSanctuary>(StringComparer.Ordinal);
        private static bool Same(MasterIdolEffectCircle[] a,MasterIdolEffectCircle[] b){if(a==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i].Radius!=b[i].Radius||(a[i].Point-b[i].Point).sqrMagnitude!=0)return false;return true;}
        private static readonly List<MasterIdolSanctuary> Domes=new List<MasterIdolSanctuary>();
        private static readonly Dictionary<long,List<MasterIdolSanctuary>> Cells=new Dictionary<long,List<MasterIdolSanctuary>>();
        private static int Cell(float x)=>(int)Math.Floor(x/32f);
        private static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        internal static IReadOnlyList<MasterIdolSanctuary> Snapshot()
        {
            var source=MasterIdolEffectZones.Snapshot;if(ReferenceEquals(Source,source))return Domes;
            Source=source;Previous.Clear();foreach(var old in Domes)Previous[old.Id]=old;Domes.Clear();Cells.Clear();if(source==null){Previous.Clear();return Domes;}
            foreach(var zone in source.ById.Values)
            {
                if(zone.Type!="Swamp"||zone.Circles.Length==0)continue;
                MasterIdolSanctuary dome;
                if(Previous.TryGetValue(zone.Id,out var saved)&&Same(saved.Coverage,zone.Circles))dome=saved;
                else{
                var center=Vector3.zero;foreach(var circle in zone.Circles)center+=circle.Point;center/=zone.Circles.Length;
                float radius=0;foreach(var circle in zone.Circles)radius=Mathf.Max(radius,Vector3.Distance(center,circle.Point)+circle.Radius);
                dome=new MasterIdolSanctuary{Id=zone.Id,Center=center,Radius=Mathf.Min(MasterIdolSanctuaryRules.MaximumRadius,radius),Coverage=zone.Circles};
                }Domes.Add(dome);var location=dome.Center;
                for(int x=Cell(location.x-dome.Radius-2);x<=Cell(location.x+dome.Radius+2);x++)for(int z=Cell(location.z-dome.Radius-2);z<=Cell(location.z+dome.Radius+2);z++)
                {long key=Key(x,z);if(!Cells.TryGetValue(key,out var list)){list=new List<MasterIdolSanctuary>();Cells[key]=list;}list.Add(dome);}
            }
            Previous.Clear();return Domes;
        }
        internal static bool Contains(Vector3 point)
        {Snapshot();if(Cells.TryGetValue(Key(Cell(point.x),Cell(point.z)),out var list))foreach(var dome in list)if(MasterIdolSanctuaryRules.Contains(dome.Center,dome.Radius,point))return true;return false;}
        internal static bool Shield(Vector3 point)=>ShieldGenerator.IsInsideShield(point)||Contains(point);
        internal static IEnumerable<CodeInstruction> RainWet(IEnumerable<CodeInstruction> instructions)
        {
            var native=AccessTools.Method(typeof(ShieldGenerator),nameof(ShieldGenerator.IsInsideShield),new[]{typeof(Vector3)});
            var replacement=AccessTools.Method(typeof(MasterIdolSwamp),nameof(Shield));int matches=0;
            foreach(var instruction in instructions){if(instruction.Calls(native)){instruction.opcode=OpCodes.Call;instruction.operand=replacement;matches++;}yield return instruction;}
            if(matches!=1)throw new InvalidOperationException("Swamp rain guard: expected one native shield call, found "+matches);
        }
                private readonly struct Interval
        {internal readonly float Start,End;internal Interval(float start,float end){Start=start;End=end;}}
        private static readonly List<Interval> Intervals=new List<Interval>();
        // Merge ray intervals through the cached union. Horizontal exits do not launch NPCs skyward.
        private static Vector3 ExitUnion(Vector3 point,float bodyRadius)
        {
            Vector3 best=point;float bestDistance=float.MaxValue;
            for(int direction=0;direction<8;direction++)
            {
                float angle=direction*Mathf.PI/4;var ray=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));Intervals.Clear();
                foreach(var dome in Domes)
                {
                    var offset=point-dome.Center;float radius=dome.Radius+bodyRadius+.15f,b=Vector3.Dot(offset,ray),disc=b*b-offset.sqrMagnitude+radius*radius;
                    if(disc<0)continue;float root=Mathf.Sqrt(disc),end=-b+root;if(end<0)continue;Intervals.Add(new Interval(-b-root,end));
                }
                Intervals.Sort((a,b)=>a.Start.CompareTo(b.Start));float distance=0;
                foreach(var interval in Intervals){if(interval.Start>distance+.01f)break;distance=Mathf.Max(distance,interval.End+.05f);}
                if(distance<bestDistance){bestDistance=distance;best=point+ray*distance;}
            }
            return best;
        }
        private sealed class Movement
        {internal Vector3 Previous;internal bool Started;internal ZNet Session;internal long World,Owner;internal ushort Epoch;internal ZDO Identity;}
        private static readonly ConditionalWeakTable<Character,Movement> Motion=new ConditionalWeakTable<Character,Movement>();
        private static bool Hostile(Character creature)
        {
            if(creature is Player||creature.IsTamed()||creature.IsDead()||MasterIdolLeash.For(creature)?.Resident==true)return false;
            bool players=false;foreach(var player in Player.GetAllPlayers())if(player!=null){players=true;if(BaseAI.IsEnemy(creature,player))return true;}
            if(players)return false;
            // Native player-faction matrix fallback for owner sectors without loaded Player objects.
            var faction=creature.GetFaction();return faction>=Character.Faction.AnimalsVeg&&faction<=Character.Faction.MistlandsMonsters||faction==Character.Faction.PlayerSpawned||faction==Character.Faction.TrainingDummy||faction==Character.Faction.DeepNorth||faction==Character.Faction.Dverger&&creature.GetBaseAI()?.IsAggravated()==true;
        }
        internal static void Boundary(Character creature,float dt,bool afterMotion)
        {
            if(creature==null||creature is Player||creature.IsTamed()||creature.IsDead()||creature.m_nview?.IsValid()!=true||!creature.m_nview.IsOwner()||creature.m_body==null)return;
            Snapshot();if(Domes.Count==0)return;
            var state=Motion.GetValue(creature,_=>new Movement());var zdo=creature.m_nview.GetZDO();var session=ZNet.instance;long world=session?.GetWorld()!=null?session.GetWorldUID():0;
            var point=creature.m_body.position;
            if(!ReferenceEquals(state.Identity,zdo)||state.Session!=session||state.World!=world||state.Owner!=zdo.GetOwner()||state.Epoch!=zdo.OwnerRevision)state.Started=false;
            state.Identity=zdo;state.Session=session;state.World=world;state.Owner=zdo.GetOwner();state.Epoch=zdo.OwnerRevision;
            bool nearby=false;var velocityPoint=point+creature.m_body.linearVelocity*Mathf.Clamp(dt,0,.1f);var previous=state.Started?state.Previous:point;
            if((velocityPoint-previous).sqrMagnitude>128*128)previous=point;
            for(int x=Cell(Mathf.Min(previous.x,velocityPoint.x));x<=Cell(Mathf.Max(previous.x,velocityPoint.x))&&!nearby;x++)for(int z=Cell(Mathf.Min(previous.z,velocityPoint.z));z<=Cell(Mathf.Max(previous.z,velocityPoint.z));z++)if(Cells.ContainsKey(Key(x,z))){nearby=true;break;}
            if(!nearby||!Hostile(creature)){state.Previous=point;state.Started=true;return;}
            Vector3 from=afterMotion?point:state.Started?state.Previous:point;
            Vector3 to=afterMotion?point+creature.m_body.linearVelocity*Mathf.Clamp(dt,0,.1f):point;
            // Normal physics sweep bounded to at most 10x10 cells. Long teleports eject only at destination.
            if((to-from).sqrMagnitude>128*128)from=to;
            long visit=unchecked(++Sweep);float best=float.MaxValue;Vector3 stop=to;MasterIdolSanctuary barrier=null;
            for(int x=Cell(Mathf.Min(from.x,to.x));x<=Cell(Mathf.Max(from.x,to.x));x++)for(int z=Cell(Mathf.Min(from.z,to.z));z<=Cell(Mathf.Max(from.z,to.z));z++)
            {
                if(!Cells.TryGetValue(Key(x,z),out var candidates))continue;
                foreach(var dome in candidates)
                {
                    if(dome.Visit==visit)continue;dome.Visit=visit;
                    if(!MasterIdolSanctuaryRules.Entry(dome.Center,dome.Radius+Mathf.Min(2,creature.GetRadius()),from,to,out var clipped))continue;
                    float distance=(clipped-from).sqrMagnitude;if(distance>=best)continue;best=distance;stop=clipped;barrier=dome;
                }
            }
            if(barrier!=null)
            {
                if(!afterMotion){
                    // A valid boundary of one sphere can still lie inside its neighbour.
                    bool overlap=false;foreach(var dome in Domes)if(MasterIdolSanctuaryRules.Contains(dome.Center,dome.Radius+Mathf.Min(2,creature.GetRadius()),stop)){overlap=true;break;}
                    if(MasterIdolSanctuaryRules.Contains(barrier.Center,barrier.Radius+Mathf.Min(2,creature.GetRadius()),from))stop=ExitUnion(point,Mathf.Min(2,creature.GetRadius()));
                    else if(overlap)stop=ExitUnion(stop,Mathf.Min(2,creature.GetRadius()));
                    creature.m_body.position=stop;creature.transform.position=stop;point=stop;}
                var normal=stop-barrier.Center;normal.y=0;normal=normal.normalized;var velocity=creature.m_body.linearVelocity;float inward=Vector3.Dot(velocity,normal);
                if(inward<0)creature.m_body.linearVelocity=velocity-normal*inward;
            }
            if(!afterMotion){state.Previous=point;state.Started=true;}
        }
    }
    [HarmonyPatch(typeof(Player),"UpdateEnvStatusEffects",new[]{typeof(float)})]
    internal static class MasterIdolSwampWeatherPatch
    {private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>MasterIdolSwamp.RainWet(instructions);}
    [HarmonyPatch(typeof(WearNTear),"UpdateWear")]
    internal static class MasterIdolSwampBuildingRainPatch
    {
        private struct State{internal bool Applied,RoofWear;}
        private static void Prefix(WearNTear __instance,out State __state)
        {
            __state=default;if(!MasterIdolSwamp.Contains(__instance.transform.position))return;
            __state=new State{Applied=true,RoofWear=__instance.m_noRoofWear};__instance.m_noRoofWear=false;__instance.m_rainTimer=0;
        }
        private static Exception Finalizer(WearNTear __instance,State __state,Exception __exception)
        {if(__state.Applied&&__instance!=null)__instance.m_noRoofWear=__state.RoofWear;return __exception;}
    }
    [HarmonyPatch(typeof(Character),"CustomFixedUpdate")]
    internal static class MasterIdolSwampBoundaryPatch
    {private static void Prefix(Character __instance,float __0)=>MasterIdolSwamp.Boundary(__instance,__0,false);}
    [HarmonyPatch(typeof(Character),"UpdateMotion")]
    internal static class MasterIdolSwampVelocityPatch
    {private static void Postfix(Character __instance,float __0)=>MasterIdolSwamp.Boundary(__instance,__0,true);}
}







