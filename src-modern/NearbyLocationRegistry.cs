using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMastery
{
    // Shared lifecycle registry: consumers inspect only already-loaded native
    // proxies. It never scans the world, generates locations or sends RPCs.
    internal static class NearbyLocationRegistry
    {
        private static readonly HashSet<LocationProxy> Loaded = new HashSet<LocationProxy>();
        private static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        internal static void Add(LocationProxy proxy)
        {
            if (!Headless && proxy != null) Loaded.Add(proxy);
        }

        internal static void Remove(LocationProxy proxy)
        {
            if (proxy != null) Loaded.Remove(proxy);
        }

        internal static LocationProxy[] Snapshot()
        {
            if (Headless) return new LocationProxy[0];
            Loaded.RemoveWhere(proxy => proxy == null);
            var result = new LocationProxy[Loaded.Count];
            Loaded.CopyTo(result);
            return result;
        }
    }

    [HarmonyPatch(typeof(LocationProxy), "Awake")]
    internal static class NearbyLocationRegistryAwakePatch
    {
        private static void Postfix(LocationProxy __instance) => NearbyLocationRegistry.Add(__instance);
    }

    [HarmonyPatch(typeof(LocationProxy), "OnDestroy")]
    internal static class NearbyLocationRegistryDestroyPatch
    {
        private static void Prefix(LocationProxy __instance) => NearbyLocationRegistry.Remove(__instance);
    }
}
