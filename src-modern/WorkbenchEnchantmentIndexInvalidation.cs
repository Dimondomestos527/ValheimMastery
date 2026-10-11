using HarmonyLib;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(ZDO),nameof(ZDO.Set),new[]{typeof(int),typeof(string)})]
    internal static class WorkbenchEnchantmentMarkerChangedPatch
    {
        static readonly int StationMarkerHash=WorkbenchEnchantmentRuntime.StationKey.GetStableHashCode();
        static void Prefix(ZDO __instance,int __0,string __1,out bool __state)
        {__state=__0==StationMarkerHash&&__instance.GetString(__0,"")!=__1;}
        static void Postfix(bool __state,bool __runOriginal)
        {if(__state&&__runOriginal)WorkbenchEnchantmentRuntime.InvalidateIndex();}
    }
}
