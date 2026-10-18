using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    [HarmonyPatch]
    internal static class ResidentCargoNativeRequestsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {foreach(string name in new[]{"RPC_RequestOpen","RPC_RequestStack","RPC_RequestTakeAll","RPC_OpenResponse","RPC_StackResponse","RPC_TakeAllResponse"})yield return AccessTools.Method(typeof(Container),name);}
        private static bool Prefix(Container __instance)=>!MasterIdolResidentCargo.IsCargo(__instance)&&!MasterIdolResidentCargo.Locked(__instance);
    }
    [HarmonyPatch(typeof(Container),nameof(Container.Interact))]
    internal static class ResidentCargoNativeInteractPatch
    {private static bool Prefix(Container __instance,ref bool __result){if(!MasterIdolResidentCargo.IsCargo(__instance)&&!MasterIdolResidentCargo.Locked(__instance))return true;__result=false;return false;}}
    [HarmonyPatch(typeof(Container),nameof(Container.TakeAll))]
    internal static class ResidentCargoTakeAllPatch
    {
        internal static bool Allowed(Container box,Humanoid player)=>player!=null&&player==Player.m_localPlayer&&ResidentCargoGui.Allowed(box);
        private static bool Prefix(Container __instance,Humanoid __0,ref bool __result)
        {if(!MasterIdolResidentCargo.IsCargo(__instance))return true;__result=false;if(Allowed(__instance,__0))__result=ResidentCargoTransfer.Manual(__instance,__0.GetInventory(),true)>0;return false;}
    }
    [HarmonyPatch(typeof(Container),"StackAll")]
    internal static class ResidentCargoStackAllPatch
    {private static bool Prefix(Container __instance){if(!MasterIdolResidentCargo.IsCargo(__instance))return true;if(ResidentCargoTakeAllPatch.Allowed(__instance,Player.m_localPlayer))ResidentCargoTransfer.Manual(__instance,Player.m_localPlayer.GetInventory(),false);return false;}}
    [HarmonyPatch(typeof(Container),nameof(Container.SetInUse))]
    internal static class ResidentCargoClosePatch
    {private static void Postfix(Container __instance,bool inUse){if(!inUse)MasterIdolResidentCargo.Closed(__instance);}}
    [HarmonyPatch]
    internal static class ResidentCargoProtectionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Piece),nameof(Piece.DropResources));
            foreach(string name in new[]{"Damage","RPC_Damage","Remove","RPC_Remove","Destroy"})
                yield return name=="Destroy"?AccessTools.Method(typeof(WearNTear),name,new[]{typeof(HitData),typeof(bool)}):AccessTools.Method(typeof(WearNTear),name);
            yield return AccessTools.Method(typeof(Container),"OnDestroyed");
            yield return AccessTools.Method(typeof(Container),"DropAllItems",Type.EmptyTypes);
            yield return AccessTools.Method(typeof(Container),"DropAllItems",new[]{typeof(GameObject)});
        }
        private static bool Prefix(object __instance)=>!MasterIdolResidentCargo.IsCargo((__instance as Component)?.GetComponent<Container>());
    }
    [HarmonyPatch(typeof(Container),nameof(Container.CanBeRemoved))]
    internal static class ResidentCargoRemovePatch
    {private static bool Prefix(Container __instance,ref bool __result){if(!MasterIdolResidentCargo.IsCargo(__instance))return true;__result=false;return false;}}
    [HarmonyPatch(typeof(Piece),nameof(Piece.CanBeRemoved))]
    internal static class ResidentCargoPieceRemovePatch
    {private static bool Prefix(Piece __instance,ref bool __result){if(!MasterIdolResidentCargo.IsCargo(__instance.GetComponent<Container>()))return true;__result=false;return false;}}
    [HarmonyPatch]
    internal static class ResidentCargoIncineratorLockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(Incinerator),"OnIncinerate");yield return AccessTools.Method(typeof(Incinerator),"RPC_RequestIncinerate");}
        private static bool Prefix(Incinerator __instance)=>!MasterIdolResidentCargo.Locked(__instance.GetComponentInChildren<Container>());
    }
    [HarmonyPatch]
    internal static class ResidentCargoWorkAdmissionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(MasterIdolResidentWork),"Update");yield return AccessTools.Method(typeof(MasterIdolResidentWork),"ReceiveWork");}
        private static bool Prefix(MasterIdolResidentWork __instance)=>!MasterIdolResidentCargo.ManualBusy(__instance.GetComponent<Character>());
    }
}
