using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolMeadows
    {
        internal static bool Animal(Character creature)=>creature?.m_nview?.IsValid()==true&&creature.m_nview.IsOwner()&&creature.IsTamed()&&MasterIdolEffectZones.At("Meadows",creature.transform.position)!=null;
        internal static bool Crop(Plant plant)
        {if(plant?.m_grownPrefabs==null)return false;foreach(var grown in plant.m_grownPrefabs)if(grown?.GetComponent<Pickable>()!=null&&grown.GetComponent<TreeBase>()==null)return true;return false;}
    }
    [HarmonyPatch(typeof(Plant),"GetGrowTime")]
    internal static class MasterIdolCropGrowthPatch
    {private static void Postfix(Plant __instance,ref float __result){if(__result>0&&MasterIdolMeadows.Crop(__instance)&&MasterIdolEffectZones.At("Meadows",__instance.transform.position)!=null)__result*=.5f;}}
    [HarmonyPatch(typeof(Procreation),"Awake")]
    internal static class MasterIdolBreedingScheduleAttachPatch
    {private static void Postfix(Procreation __instance){if(__instance.GetComponent<MasterIdolBreedingSchedule>()==null)__instance.gameObject.AddComponent<MasterIdolBreedingSchedule>();}}
    internal sealed class MasterIdolBreedingSchedule:MonoBehaviour
    {
        private Procreation Reproduction;private Character Creature;private bool Fast;private float Next,Interval;
        private void Awake(){Reproduction=GetComponent<Procreation>();Creature=GetComponent<Character>();Interval=Reproduction?.m_updateInterval??0;}
        private void Update()
        {
            if(Reproduction==null||Interval<=0||Time.unscaledTime<Next)return;Next=Time.unscaledTime+1f;
            bool fast=MasterIdolMeadows.Animal(Creature);if(fast==Fast)return;Fast=fast;
            Reproduction.CancelInvoke("Procreate");float interval=Interval*(Fast?.5f:1f);Reproduction.InvokeRepeating("Procreate",interval,interval);
        }
        private void OnDisable(){if(Fast&&Reproduction!=null){Fast=false;Reproduction.CancelInvoke("Procreate");Reproduction.InvokeRepeating("Procreate",Interval,Interval);}}
    }
    [HarmonyPatch(typeof(Procreation),"Procreate")]
    internal static class MasterIdolBreedingPatch
    {
        private struct State{internal bool Applied;internal float Pregnancy;internal int Cap;}
        private static void Prefix(Procreation __instance,out State __state)
        {
            __state=default;if(!MasterIdolMeadows.Animal(__instance.m_character))return;
            __state=new State{Applied=true,Pregnancy=__instance.m_pregnancyDuration,Cap=__instance.m_maxCreatures};
            __instance.m_pregnancyDuration*=.5f;__instance.m_maxCreatures=(int)Math.Min(int.MaxValue,(long)__instance.m_maxCreatures*2);
        }
        private static Exception Finalizer(Procreation __instance,State __state,Exception __exception)
        {if(__state.Applied&&__instance!=null){__instance.m_pregnancyDuration=__state.Pregnancy;__instance.m_maxCreatures=__state.Cap;}return __exception;}
    }
    [HarmonyPatch(typeof(Growup),"GrowUpdate")]
    internal static class MasterIdolMaturationPatch
    {
        private struct State{internal bool Applied;internal float Duration;}
        private static void Prefix(Growup __instance,out State __state)
        {
            __state=default;if(!MasterIdolMeadows.Animal(__instance.GetComponent<Character>()))return;
            __state=new State{Applied=true,Duration=__instance.m_growTime};__instance.m_growTime*=.5f;
        }
        private static Exception Finalizer(Growup __instance,State __state,Exception __exception)
        {if(__state.Applied&&__instance!=null)__instance.m_growTime=__state.Duration;return __exception;}
    }
    [HarmonyPatch(typeof(Character),"RPC_Damage")]
    internal static class MasterIdolTameProtectionPatch
    {private static void Prefix(Character __instance,HitData __1){if(__1!=null&&MasterIdolMeadows.Animal(__instance))__1.m_damage.Modify(.25f);}}
}
