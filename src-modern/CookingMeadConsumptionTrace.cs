using System;
using System.Globalization;
using HarmonyLib;

namespace ValheimMastery
{
    // Diagnostic only. No inventory, status instance or template is modified.
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class CookingMeadConsumptionTrace
    {
        private static int Remaining=16;
        internal sealed class Observation
        {
            internal StatusEffect Template;
            internal string Prefab, Version, Tier;
            internal bool Author, Mastery;
            internal float Expected;
        }
        // Mirror only the existing active-food selection, without its migration
        // writeback, persisted cleanup or MasteryStateStore cache mutation.
        internal static FeastTheme ThemeSnapshot(Player player)
        {
            if(player?.m_foods==null || player.m_customData==null)return FeastTheme.None;
            player.m_customData.TryGetValue("valheim_mastery.master_feast.active_food",out string persistedFood);
            if(!string.IsNullOrEmpty(persistedFood))
            {
                var migration=new FeastFoodIdentityMigration(persistedFood);
                foreach(var food in player.m_foods)
                    if(food!=null)migration.Observe(food.m_name,food.m_item?.m_shared?.m_name,food.m_time);
                persistedFood=migration.Resolve(); // Local value only.
            }
            player.m_customData.TryGetValue("valheim_mastery.master_feast.active_stamp",out string persistedStamp);
            player.m_customData.TryGetValue("valheim_mastery.master_feast.active_theme",out string persistedTheme);
            Player.Food selected=null;long bestStamp=long.MinValue;
            foreach(var food in player.m_foods)
            {
                var item=food?.m_item;
                if(item==null || food.m_time<=0f)continue;
                bool restored=!string.IsNullOrEmpty(persistedFood) && food.m_name==persistedFood;
                if(!restored && (!Cooking70Service.IsFeast(item) || !Cooking35Service.HasCookMastery(player,item,70)))continue;
                string raw="";item.m_customData?.TryGetValue(MasterFeastService.ConsumedAtKey,out raw);
                if(restored && string.IsNullOrEmpty(raw))raw=persistedStamp;
                long.TryParse(raw,NumberStyles.Integer,CultureInfo.InvariantCulture,out long stamp);
                if(selected==null || stamp>=bestStamp){selected=food;bestStamp=stamp;}
            }
            if(selected?.m_item==null)return FeastTheme.None;
            return selected.m_name==persistedFood && Enum.TryParse(persistedTheme,out FeastTheme savedTheme)?
                savedTheme:MasterFeastService.GetTheme(selected.m_item);
        }
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance,ItemDrop.ItemData item,out Observation __state)
        {
            __state=null;
            if(Remaining<=0 || __instance!=Player.m_localPlayer || !Cooking35Service.EffectsEnabled)return;
            try
            {
                var plan=CookingMeadTiming.Prepare(item,ThemeSnapshot(__instance));
                if(plan==null)return;
                item.m_customData.TryGetValue(CookingProductIdentity.VersionKey,out string version);
                item.m_customData.TryGetValue(Cooking35Service.CookMasteryKey,out string tier);
                __state=new Observation{Template=plan.Template,Expected=plan.Ttl,Prefab=TierDatabase.ItemKey(item),
                    Version=version??"none",Tier=tier??"none",Author=item.m_crafterID!=0,
                    Mastery=CookingMeadTiming.HasAuthoredMastery(item)};
            }
            catch { }
        }
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance,bool __result,Observation __state)
        {
            if(__state==null || !__result || Remaining<=0)return;
            Remaining--;
            try
            {
                var active=__instance.GetSEMan().GetStatusEffect(__state.Template.NameHash());
                MasteryPlugin.Log.LogInfo("[CookingMeadConsumeTrace] bottle="+__state.Prefab+
                    " version="+__state.Version+" tier="+__state.Tier+" authorPresent="+__state.Author+
                    " authoredMastery="+__state.Mastery+" baseTTL="+__state.Template.m_ttl+
                    " expectedTTL="+__state.Expected+" activePresent="+(active!=null)+
                    " sharedTemplate="+ReferenceEquals(active,__state.Template)+" actualTTL="+(active?.m_ttl??-1f));
            }
            catch { } // Observing an accepted drink must never interrupt consumption.
        }
    }
}
