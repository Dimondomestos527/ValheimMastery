using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolMistlands
    {
        private static readonly ConditionalWeakTable<UnityEngine.Object,MasterIdolObservedClock> Clocks=new ConditionalWeakTable<UnityEngine.Object,MasterIdolObservedClock>();
        internal static SE_Stats Feather(SEMan manager)
        {
            var player=manager.m_character as Player;if(!MasterIdolPlains.Owned(player)||player.IsDead()||MasterIdolEffectZones.At("Mistlands",player.transform.position)==null||manager.HaveStatusEffect("SlowFall".GetStableHashCode()))return null;
            return ObjectDB.instance?.GetStatusEffect("SlowFall".GetStableHashCode()) as SE_Stats;
        }
        internal static double Heat(UnityEngine.Object source,ZNetView view,Vector3 point,double delta)
        {
            string zone=view?.IsValid()==true&&view.IsOwner()?MasterIdolEffectZones.At("Mistlands",point)?.Id:null;
            float elapsed=Clocks.GetValue(source,_=>new MasterIdolObservedClock()).Step(zone,view?.GetZDO());
            return zone==null?delta:MasterIdolSanctuaryRules.BonusTime(delta,elapsed);
        }
        internal static void Observe(UnityEngine.Object source,ZNetView view)
        {if(view?.IsValid()!=true||!view.IsOwner())Clocks.GetValue(source,_=>new MasterIdolObservedClock()).Step(null,view?.GetZDO());}
        internal static bool Refinery(Smelter station)=>station!=null&&Utils.GetPrefabName(station.gameObject)=="eitrrefinery";
    }
    [HarmonyPatch(typeof(SEMan),"ModifyWalkVelocity")]
    internal static class MasterIdolMistFeatherVelocityPatch
    {private static void Postfix(SEMan __instance,ref Vector3 __0)=>MasterIdolMistlands.Feather(__instance)?.ModifyWalkVelocity(ref __0);}
    [HarmonyPatch(typeof(SEMan),"ModifyJumpStaminaUsage")]
    internal static class MasterIdolMistFeatherJumpPatch
    {private static void Postfix(SEMan __instance,float __0,ref float __1,bool __2){MasterIdolMistlands.Feather(__instance)?.ModifyJumpStaminaUsage(__0,ref __1);if(__2)__1=Mathf.Max(0,__1);}}
    [HarmonyPatch(typeof(SEMan),"ModifyFallDamage")]
    internal static class MasterIdolMistFeatherFallPatch
    {private static void Postfix(SEMan __instance,float __0,ref float __1)=>MasterIdolMistlands.Feather(__instance)?.ModifyFallDamage(__0,ref __1);}
    [HarmonyPatch(typeof(Smelter),"GetDeltaTime")]
    internal static class MasterIdolMistRefineryTimePatch
    {private static void Postfix(Smelter __instance,ref double __result){if(MasterIdolMistlands.Refinery(__instance))__result=MasterIdolMistlands.Heat(__instance,__instance.m_nview,__instance.transform.position,__result);}}
    [HarmonyPatch(typeof(Smelter),"UpdateSmelter")]
    internal static class MasterIdolMistRefineryOwnerPatch
    {private static void Prefix(Smelter __instance){if(MasterIdolMistlands.Refinery(__instance))MasterIdolMistlands.Observe(__instance,__instance.m_nview);}}
    [HarmonyPatch(typeof(SapCollector),"GetTimeSinceLastUpdate")]
    internal static class MasterIdolMistSapTimePatch
    {private static void Postfix(SapCollector __instance,ref float __result)=>__result=(float)MasterIdolMistlands.Heat(__instance,__instance.m_nview,__instance.transform.position,__result);}
    [HarmonyPatch(typeof(SapCollector),"UpdateTick")]
    internal static class MasterIdolMistSapOwnerPatch
    {private static void Prefix(SapCollector __instance)=>MasterIdolMistlands.Observe(__instance,__instance.m_nview);}

    internal static class MasterIdolRefineryHazard
    {
        [ThreadStatic]internal static ZDO Spawning;
        [ThreadStatic]internal static bool Hit;
        private const string SourceKey="vm.idol.radiation.source.v1",CreatorKey="vm.idol.radiation.creator.v1",WorldKey="vm.idol.radiation.world.v1";
        internal static ZDO Source(object iterator)
        {
            var radiator=AccessTools.Field(iterator.GetType(),"<>4__this")?.GetValue(iterator) as Radiator;
            var station=radiator?.GetComponentInParent<Smelter>();return MasterIdolMistlands.Refinery(station)&&station.m_nview?.IsValid()==true&&station.m_nview.IsOwner()?station.m_nview.GetZDO():null;
        }
        internal static void Mark(Projectile projectile)
        {
            if(Spawning==null||Utils.GetPrefabName(projectile.gameObject)!="radiation"||projectile.m_nview?.IsValid()!=true||!projectile.m_nview.IsOwner()||ZNet.instance?.GetWorld()==null)return;
            var zdo=projectile.m_nview.GetZDO();zdo.Set(SourceKey,Spawning.m_uid.ToString());zdo.Set(CreatorKey,Spawning.GetLong(ZDOVars.s_creator,0));zdo.Set(WorldKey,ZNet.instance.GetWorldUID());
        }
        internal static bool Proven(Projectile projectile)
        {
            if(projectile.m_nview?.IsValid()!=true||!projectile.m_nview.IsOwner()||Utils.GetPrefabName(projectile.gameObject)!="radiation"||ZNet.instance?.GetWorld()==null)return false;
            var zdo=projectile.m_nview.GetZDO();if(zdo.GetLong(WorldKey,0)!=ZNet.instance.GetWorldUID())return false;
            string id=zdo.GetString(SourceKey,"");if(!MasterIdolNetworkQuota.ValidUid(id))return false;int split=id.IndexOf(':');
            var source=ZDOMan.instance?.GetZDO(new ZDOID(long.Parse(id.Substring(0,split)),uint.Parse(id.Substring(split+1))));
            return source!=null&&source.GetPrefab()=="eitrrefinery".GetStableHashCode()&&source.GetLong(ZDOVars.s_creator,0)==zdo.GetLong(CreatorKey,-1);
        }
    }
    [HarmonyPatch]
    internal static class MasterIdolRadiatorSpawnScopePatch
    {
        private static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.Inner(typeof(Radiator),"<UpdateLoop>d__2"),"MoveNext");
        private static void Prefix(object __instance,out ZDO __state){__state=MasterIdolRefineryHazard.Spawning;MasterIdolRefineryHazard.Spawning=MasterIdolRefineryHazard.Source(__instance);}
        private static Exception Finalizer(ZDO __state,Exception __exception){MasterIdolRefineryHazard.Spawning=__state;return __exception;}
    }
    [HarmonyPatch(typeof(Projectile),"Setup")]
    internal static class MasterIdolRefineryProjectilePatch
    {private static void Postfix(Projectile __instance)=>MasterIdolRefineryHazard.Mark(__instance);}
    [HarmonyPatch(typeof(Projectile),"OnHit")]
    internal static class MasterIdolRefineryHitScopePatch
    {
        private static void Prefix(Projectile __instance,out bool __state){__state=MasterIdolRefineryHazard.Hit;MasterIdolRefineryHazard.Hit=MasterIdolRefineryHazard.Proven(__instance);}
        private static Exception Finalizer(bool __state,Exception __exception){MasterIdolRefineryHazard.Hit=__state;return __exception;}
    }
    [HarmonyPatch(typeof(WearNTear),"Damage")]
    internal static class MasterIdolRefineryBuildingProtectionPatch
    {private static bool Prefix(WearNTear __instance)=>!MasterIdolRefineryHazard.Hit||MasterIdolEffectZones.At("Mistlands",__instance.transform.position)==null;}
}

