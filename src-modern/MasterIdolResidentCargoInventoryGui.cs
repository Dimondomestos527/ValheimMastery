using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class ResidentCargoGui
    {
        private static readonly ConditionalWeakTable<Inventory,Container> Inventories=new ConditionalWeakTable<Inventory,Container>();
        internal static void Register(Container box,Inventory inventory)
        {if(inventory!=null&&MasterIdolResidentCargo.IsCargo(box)&&!Inventories.TryGetValue(inventory,out _))Inventories.Add(inventory,box);}
        internal static Container Container(Inventory inventory)=>inventory!=null&&Inventories.TryGetValue(inventory,out var box)&&box!=null?box:null;
        internal static bool Allowed(Container box)
        {
            var player=Player.m_localPlayer;
            if(player==null||player.IsDead()||player.IsTeleporting()||!MasterIdolResidentCargo.Context()||!MasterIdolResidentCargo.IsCargo(box)||!box.IsOwner()||!box.IsInUse()||InventoryGui.instance?.m_currentContainer!=box)return false;
            var data=box.m_nview.GetZDO();var actor=ZDOMan.instance.GetZDO(data.GetZDOID(MasterIdolResidentCargo.BodyKey));
            return data.GetLong(MasterIdolResidentCargo.OpenPeerKey,0)==ZNet.GetUID()&&data.GetLong(MasterIdolResidentCargo.OpenPlayerKey,0)==player.GetPlayerID()&&
                MasterIdolForestGuardState.ModuleBorn(actor)&&actor.GetZDOID(MasterIdolResidentCargo.Link)==data.m_uid&&actor.GetString(MasterIdolForestGuardState.NonceKey,"")==data.GetString(MasterIdolResidentCargo.NonceKey,"")&&
                ResidentCargoRules.Living(actor.GetFloat(ZDOVars.s_health,1))&&(actor.GetPosition()-player.transform.position).sqrMagnitude<=25&&MasterIdolEffectZones.Allows(player.GetPlayerID(),actor.GetPosition());
        }
        internal static bool Access(Inventory from,Inventory to)
        {var a=Container(from);var b=Container(to);return (a==null||Allowed(a))&&(b==null||Allowed(b));}
    }
    [HarmonyPatch(typeof(Container),nameof(Container.GetInventory))]
    internal static class ResidentCargoInventoryTagPatch
    {private static void Postfix(Container __instance,Inventory __result)=>ResidentCargoGui.Register(__instance,__result);}
    [HarmonyPatch(typeof(InventoryGui),"OnTakeAll")]
    internal static class ResidentCargoGuiTakeAllPatch
    {
        private static bool Prefix(InventoryGui __instance)
        {var box=__instance.m_currentContainer;if(!MasterIdolResidentCargo.IsCargo(box))return true;if(ResidentCargoGui.Allowed(box))ResidentCargoTransfer.Manual(box,Player.m_localPlayer.GetInventory(),true);return false;}
    }
    [HarmonyPatch(typeof(InventoryGui),"OnStackAll")]
    internal static class ResidentCargoGuiStackAllPatch
    {
        private static bool Prefix(InventoryGui __instance)
        {var box=__instance.m_currentContainer;if(!MasterIdolResidentCargo.IsCargo(box))return true;if(ResidentCargoGui.Allowed(box))ResidentCargoTransfer.Manual(box,Player.m_localPlayer.GetInventory(),false);return false;}
    }
    [HarmonyPatch(typeof(Inventory),nameof(Inventory.MoveItemToThis),new[]{typeof(Inventory),typeof(ItemDrop.ItemData)})]
    internal static class ResidentCargoGuiQuickMovePatch
    {
        private static bool Prefix(Inventory __instance,Inventory fromInventory,ItemDrop.ItemData item)
        {if(ResidentCargoGui.Container(__instance)==null&&ResidentCargoGui.Container(fromInventory)==null)return true;if(ResidentCargoGui.Access(fromInventory,__instance))ResidentCargoTransfer.Move(fromInventory,__instance,item,item.m_stack);return false;}
    }
    // Native DropItem removes the dragged source before attempting a swap.
    // Own the whole cargo path before that removal; an incompatible occupied
    // cell is rejected without mutation rather than entering native swap.
    [HarmonyPatch(typeof(InventoryGrid),nameof(InventoryGrid.DropItem))]
    internal static class ResidentCargoGuiGridDropPatch
    {
        private static bool Prefix(InventoryGrid __instance,Inventory __0,ItemDrop.ItemData __1,int __2,Vector2i __3,ref bool __result)
        {
            var target=__instance.m_inventory;
            if(ResidentCargoGui.Container(target)==null&&ResidentCargoGui.Container(__0)==null)return true;
            __result=false;
            if(__1==null||!ResidentCargoGui.Access(__0,target))return false;
            if(target.GetItemAt(__3.x,__3.y)==__1){__result=true;return false;}
            int wanted=Math.Min(__2,__1.m_stack);
            __result=wanted>0&&ResidentCargoTransfer.Move(__0,target,__1,__2,__3.x,__3.y)==wanted;
            return false;
        }
    }
    [HarmonyPatch(typeof(Inventory),nameof(Inventory.MoveItemToThis),new[]{typeof(Inventory),typeof(ItemDrop.ItemData),typeof(int),typeof(int),typeof(int)})]
    internal static class ResidentCargoGuiDropPatch
    {
        private static bool Prefix(Inventory __instance,Inventory fromInventory,ItemDrop.ItemData item,int amount,int x,int y,ref bool __result)
        {if(ResidentCargoGui.Container(__instance)==null&&ResidentCargoGui.Container(fromInventory)==null)return true;__result=false;if(ResidentCargoGui.Access(fromInventory,__instance)){int wanted=Math.Min(amount,item.m_stack);__result=ResidentCargoTransfer.Move(fromInventory,__instance,item,amount,x,y)==wanted;}return false;}
    }
}
