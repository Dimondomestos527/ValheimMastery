using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolMountain
    {
        private static readonly ConditionalWeakTable<UnityEngine.Object,MasterIdolObservedClock> Clocks=new ConditionalWeakTable<UnityEngine.Object,MasterIdolObservedClock>();
        internal static bool Thermal(Smelter station){string name=Utils.GetPrefabName(station.gameObject);return name=="smelter"||name=="blastfurnace"||name=="charcoal_kiln";}
        internal static void Observe(UnityEngine.Object station,ZNetView view){if(view?.IsValid()!=true||!view.IsOwner())Clocks.GetValue(station,_=>new MasterIdolObservedClock()).Step(null,view?.GetZDO());}
        internal static string Zone(ZNetView view,Vector3 point)=>view?.IsValid()==true&&view.IsOwner()?MasterIdolEffectZones.At("Mountain",point)?.Id:null;
        internal static double Heat(UnityEngine.Object station,ZNetView view,Vector3 point,double delta)
        {string zone=Zone(view,point);float elapsed=Clocks.GetValue(station,_=>new MasterIdolObservedClock()).Step(zone,view?.GetZDO());return zone==null?delta:MasterIdolSanctuaryRules.BonusTime(delta,elapsed);}
        internal static void RepairPiece(WearNTear wear)
        {
            if(wear==null||wear.m_nview?.IsValid()!=true)return;
            string zone=Zone(wear.m_nview,wear.transform.position);float elapsed=Clocks.GetValue(wear,_=>new MasterIdolObservedClock()).Step(zone,wear.m_nview.GetZDO(),1);
            if(zone==null||elapsed<=0||wear.m_health<=0||MasterIdolDurability.Protected(wear))return;
            var zdo=wear.m_nview.GetZDO();float health=zdo.GetFloat(ZDOVars.s_health,wear.m_health);
            float next=MasterIdolSanctuaryRules.Repair(health,wear.m_health,elapsed);if(next<=health)return;
            zdo.Set(ZDOVars.s_health,next);wear.m_nview.InvokeRPC(ZNetView.Everybody,"RPC_HealthChanged",next);
        }
        internal static void RepairInventory(Player player)
        {
            string zone=player==Player.m_localPlayer&&!player.IsDead()?Zone(player.m_nview,player.transform.position):null;
            float elapsed=Clocks.GetValue(player,_=>new MasterIdolObservedClock()).Step(zone,player.m_nview?.GetZDO());if(zone==null||elapsed<=0)return;
            var inventory=player.GetInventory();if(inventory==null)return;bool changed=false;
            foreach(var item in inventory.GetAllItems())
            {
                if(item?.m_shared?.m_useDurability!=true||!item.IsEquipable())continue;
                // Broken equipment (0 durability) is repairable; dead buildings are not.
                float maximum=item.GetMaxDurability(),current=item.m_durability;
                if(!MasterIdolSanctuaryRules.Finite(current)||!MasterIdolSanctuaryRules.Finite(maximum)||maximum<=0)continue;
                float next=Mathf.Min(maximum,Mathf.Max(0,current)+maximum*.01f*elapsed);if(next<=current)continue;
                item.m_durability=next;changed=true;
            }
            if(changed)inventory.Changed();
        }
    }
    [HarmonyPatch(typeof(Smelter),"GetDeltaTime")]
    internal static class MasterIdolMountainSmeltingPatch
    {private static void Postfix(Smelter __instance,ref double __result){if(MasterIdolMountain.Thermal(__instance))__result=MasterIdolMountain.Heat(__instance,__instance.m_nview,__instance.transform.position,__result);}}
    [HarmonyPatch(typeof(Smelter),"UpdateSmelter")]
    internal static class MasterIdolMountainSmeltingOwnerPatch
    {private static void Prefix(Smelter __instance)=>MasterIdolMountain.Observe(__instance,__instance.m_nview);}
    [HarmonyPatch(typeof(CookingStation),"UpdateCooking")]
    internal static class MasterIdolMountainCookingOwnerPatch
    {private static void Prefix(CookingStation __instance)=>MasterIdolMountain.Observe(__instance,__instance.m_nview);}
    [HarmonyPatch(typeof(CookingStation),"GetDeltaTime")]
    internal static class MasterIdolMountainCookingPatch
    {private static void Postfix(CookingStation __instance,ref float __result)=>__result=(float)MasterIdolMountain.Heat(__instance,__instance.m_nview,__instance.transform.position,__result);}
    [HarmonyPatch(typeof(WearNTear),"RPC_Damage")]
    internal static class MasterIdolMountainDamagePatch
    {private static void Prefix(WearNTear __instance,HitData __1){if(__1!=null&&MasterIdolMountain.Zone(__instance.m_nview,__instance.transform.position)!=null)__1.m_damage.Modify(.1f);}}
    [HarmonyPatch(typeof(WearNTear),"UpdateWear")]
    internal static class MasterIdolMountainBuildingRepairPatch
    {private static void Postfix(WearNTear __instance)=>MasterIdolMountain.RepairPiece(__instance);}
    [HarmonyPatch(typeof(Player),"Update")]
    internal static class MasterIdolMountainEquipmentRepairPatch
    {
        private static Player Last;private static float Next;
        private static void Postfix(Player __instance)
        {if(__instance!=Player.m_localPlayer)return;if(Last!=__instance){Last=__instance;Next=0;}if(Time.time<Next)return;Next=Time.time+1;MasterIdolMountain.RepairInventory(__instance);}
    }
}





