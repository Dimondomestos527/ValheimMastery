using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ValheimMastery
{
    [HarmonyPatch]
    internal static class MasterIdolIndexLoadPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { foreach (var method in AccessTools.GetDeclaredMethods(typeof(ZDOMan))) if (method.Name == "Load" || method.Name == "LoadChunks") yield return method; }
        private static void Prefix() => MasterIdolWorldIndex.BeginLoad();
        private static Exception Finalizer(Exception __exception) { MasterIdolWorldIndex.EndLoad(__exception != null); return __exception; }
    }
    [HarmonyPatch(typeof(ZDOMan), "ResetBeforeLoad")]
    internal static class MasterIdolIndexResetPatch
    { private static void Prefix() { MasterIdolWorldIndex.Reset(); MasterIdolWorldRegistry.Reset(); } }
    [HarmonyPatch(typeof(ZDOMan), "ShutDown")]
    internal static class MasterIdolIndexShutdownPatch
    { private static void Prefix() { MasterIdolWorldIndex.Shutdown(); MasterIdolWorldRegistry.Reset(); } }
    [HarmonyPatch(typeof(ZDOMan), "CreateNewZDO", new[] { typeof(ZDOID), typeof(UnityEngine.Vector3), typeof(int) })]
    internal static class MasterIdolIndexCreatePatch
    { private static void Postfix(ZDO __result) => MasterIdolWorldIndex.Observe(__result); }
    [HarmonyPatch]
    internal static class MasterIdolIndexChangePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { foreach (var method in AccessTools.GetDeclaredMethods(typeof(ZDO))) if (method.Name == "SetPrefab" || method.Name == "Deserialize") yield return method; }
        private static void Postfix(ZDO __instance) => MasterIdolWorldIndex.Observe(__instance);
    }
    [HarmonyPatch(typeof(ZDO), "InternalSetPosition")]
    internal static class MasterIdolIndexMovePatch
    { private static void Postfix(ZDO __instance) => MasterIdolWorldIndex.Moved(__instance); }
    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
    internal static class MasterIdolIndexRemovePatch
    { private static void Postfix(ZDOID uid) => MasterIdolWorldIndex.Removed(uid); }
}
