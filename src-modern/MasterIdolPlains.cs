using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolPlains
    {
        private static readonly MethodInfo Copy=typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic);
        private sealed class Clock{internal float Next;}
        private static readonly ConditionalWeakTable<SEMan,Clock> Clocks=new ConditionalWeakTable<SEMan,Clock>();
        internal static bool Owned(Player player)=>player!=null&&player==Player.m_localPlayer&&player.m_nview?.IsValid()==true&&player.m_nview.IsOwner();
        internal static void Consume(Player player,ItemDrop.ItemData item,bool accepted)
        {
            if(!accepted||!Owned(player)||item?.m_shared==null||!Cooking70Service.IsFeast(item))return;
            string name=PerkRuntimeService.ItemPrefabName(item);if(string.IsNullOrEmpty(name)||name.Length>128)return;
            foreach(var food in player.m_foods)
            {
                if(food?.m_name!=name||food.m_item?.m_shared==null)continue;
                string key=MasterIdolFinalBiomeRules.FoodPrefix+name;player.m_customData.Remove(key);
                // MasterFeastConsumePatch has already replaced the active item snapshot. Never multiply an earlier boost.
                float normal=item.m_shared.m_foodBurnTime;
                if(MasterIdolEffectZones.At("Plains",player.transform.position)==null||!MasterIdolFinalBiomeRules.Duration(normal)||!MasterIdolFinalBiomeRules.Duration(normal*3))return;
                Apply(food,normal*3);food.m_time=normal*3;

                player.m_customData[key]=normal.ToString("R",CultureInfo.InvariantCulture)+"|"+(normal*3).ToString("R",CultureInfo.InvariantCulture);
                try{if(MasterFeastService.TryGetActive(player,out var state)&&ReferenceEquals(state.FoodItem,food.m_item)){state.ExpireTime=Time.time+food.m_time;MasterFeastStatusIconService.Show(player,state);}}catch(Exception error){MasteryPlugin.Log?.LogWarning("[MasterIdols] Feast icon: "+error.Message);}
                return;
            }
        }
        private static void Apply(Player.Food food,float maximum)
        {
            var item=food.m_item.Clone();item.m_shared=(ItemDrop.ItemData.SharedData)Copy.Invoke(item.m_shared,null);
            item.m_shared.m_foodBurnTime=maximum;food.m_item=item;
        }
        internal static void Restore(Player player)
        {
            if(!Owned(player)||player.m_customData==null||player.m_foods==null)return;
            foreach(var food in player.m_foods)
            {
                if(food?.m_item?.m_shared==null||food.m_time<=0||string.IsNullOrEmpty(food.m_name)||!player.m_customData.TryGetValue(MasterIdolFinalBiomeRules.FoodPrefix+food.m_name,out string saved))continue;
                var fields=saved.Split('|');if(fields.Length!=2||!float.TryParse(fields[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float normal)||!float.TryParse(fields[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float boosted)||!MasterIdolFinalBiomeRules.Duration(normal)||!MasterIdolFinalBiomeRules.Duration(boosted)||Mathf.Abs(boosted-normal*3)>.01f||Mathf.Abs(food.m_item.m_shared.m_foodBurnTime-normal)>.01f)continue;
                Apply(food,boosted); // Saved remaining time is retained, never reset to the maximum.
            }
            Prune(player);
        }
        internal static void Prune(Player player)
        {
            if(!Owned(player)||player.m_customData==null||player.m_foods==null)return;
            var stale=new List<string>();foreach(string key in player.m_customData.Keys)
            {
                if(!key.StartsWith(MasterIdolFinalBiomeRules.FoodPrefix,StringComparison.Ordinal))continue;
                bool present=false;foreach(var food in player.m_foods)if(food?.m_time>0&&key==MasterIdolFinalBiomeRules.FoodPrefix+food.m_name){present=true;break;}
                if(!present)stale.Add(key);
            }
            foreach(string key in stale)player.m_customData.Remove(key);
        }
        internal static void Refresh(SEMan manager)
        {
            var player=manager.m_character as Player;if(!Owned(player)||player.IsDead())return;
            var clock=Clocks.GetValue(manager,_=>new Clock());if(Time.time<clock.Next)return;clock.Next=Time.time+1;
            if(MasterIdolEffectZones.At("Plains",player.transform.position)==null)return;
            foreach(var effect in manager.GetStatusEffects())
            {
                if(effect==null||!MasterIdolFinalBiomeRules.Duration(effect.m_ttl)||effect.m_time>=effect.m_ttl)continue;
                if(effect is SE_Rested rested&&effect.name=="Rested"){rested.UpdateTTL();continue;}
                if(!MasterIdolFinalBiomeRules.Approved(effect.name))continue;
                var native=ObjectDB.instance?.GetStatusEffect(effect.name.GetStableHashCode());if(native==null)continue;
                effect.m_time=MasterIdolFinalBiomeRules.RefreshTime(effect.m_time,effect.m_ttl,native.m_ttl);
            }
        }
    }
    [HarmonyPatch(typeof(Player),"EatFood")]
    internal static class MasterIdolPlainsFeastPatch
    {
        [HarmonyPriority(Priority.Last-100)]
        private static void Postfix(Player __instance,ItemDrop.ItemData item,bool __result)=>MasterIdolPlains.Consume(__instance,item,__result);
    }
    [HarmonyPatch(typeof(Player),"Load")]
    internal static class MasterIdolPlainsFoodLoadPatch
    {private static void Postfix(Player __instance){MasterIdolPlainsFoodRestorePatch.Pending(__instance);MasterIdolPlains.Restore(__instance);}}
    [HarmonyPatch(typeof(Player),"Save")]
    internal static class MasterIdolPlainsFoodSavePatch
    {private static void Prefix(Player __instance)=>MasterIdolPlains.Prune(__instance);}
    [HarmonyPatch(typeof(SEMan),"Update")]
    internal static class MasterIdolPlainsPositivePatch
    {private static void Prefix(SEMan __instance)=>MasterIdolPlains.Refresh(__instance);}
    [HarmonyPatch(typeof(Player),"UpdateFood")]
    internal static class MasterIdolPlainsFoodRestorePatch
    {
        private sealed class Loaded{internal bool Done;}
        private static readonly ConditionalWeakTable<Player,Loaded> Players=new ConditionalWeakTable<Player,Loaded>();
        internal static void Pending(Player player)=>Players.GetValue(player,_=>new Loaded()).Done=false;
        private static void Prefix(Player __instance){if(!MasterIdolPlains.Owned(__instance))return;var loaded=Players.GetValue(__instance,_=>new Loaded());if(loaded.Done)return;MasterIdolPlains.Restore(__instance);loaded.Done=true;}
    }}


