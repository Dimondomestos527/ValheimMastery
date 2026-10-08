using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class WorkshopStationRadius
    {
        internal static float EffectiveRadius(float baseRadius, int stationLevel)
        {
            return baseRadius <= 0f ? 0f : baseRadius * (1f + 0.25f * Mathf.Max(0, stationLevel - 1));
        }
    }

    // Vanilla GetExtensions recomputes m_buildRange and the visual/physical area every
    // two seconds. Apply the same radius to all three representations afterwards.
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetExtensions))]
    internal static class WorkshopStationRadiusPatch
    {
        private static void Postfix(CraftingStation __instance, List<StationExtension> __result)
        {
            if (__instance == null || __instance.m_rangeBuild <= 0f) return;
            float radius = WorkshopStationRadius.EffectiveRadius(__instance.m_rangeBuild, __instance.GetLevel(false));
            WorkshopNetwork.NoticeRadius(__instance, radius);
            __instance.m_buildRange = radius;
            if (__instance.m_areaMarkerCircle != null) __instance.m_areaMarkerCircle.m_radius = radius;
            if (__instance.m_effectAreaCollider is SphereCollider sphere) sphere.radius = radius;
            else if (__instance.m_effectAreaCollider is CapsuleCollider capsule) capsule.radius = radius;
        }
    }
}
