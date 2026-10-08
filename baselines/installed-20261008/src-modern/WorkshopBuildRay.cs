using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Same native PieceRayTest rules, with the click's immutable camera ray.
    // UpdatePlacementGhost still performs every normal geometry/ward/biome check.
    // Never move the camera or write PlacementStatus to force a valid result.
    [HarmonyPatch(typeof(Player), "PieceRayTest")]
    internal static class WorkshopBuildRayPatch
    {
        private static bool Prefix(Player __instance, ref Vector3 point, ref Vector3 normal,
            ref Piece piece, ref Heightmap heightmap, ref Collider waterSurface, bool water, ref bool __result)
        {
            if (!WorkshopRemoteCraft.BuildRay(__instance, out Vector3 origin, out Vector3 direction)) return true;
            point = normal = Vector3.zero; piece = null; heightmap = null; waterSurface = null; __result = false;
            int mask = water ? __instance.m_placeWaterRayMask : __instance.m_placeRayMask;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, 50f, mask) ||
                hit.collider == null || hit.collider.attachedRigidbody != null) return false;
            float range = __instance.m_maxPlaceDistance;
            Piece ghost = __instance.m_placementGhost?.GetComponent<Piece>();
            if (ghost != null) range += ghost.m_extraPlacementDistance;
            if (Vector3.Distance(__instance.m_eye.position, hit.point) >= range) return false;
            point = hit.point; normal = hit.normal;
            piece = hit.collider.GetComponentInParent<Piece>();
            heightmap = hit.collider.GetComponent<Heightmap>();
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Water")) waterSurface = hit.collider;
            __result = true; return false;
        }
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftCancelPressed))]
    internal static class WorkshopPendingCancelPatch
    {
        private static void Prefix() => WorkshopRemoteCraft.CancelPendingCraft();
    }
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
    internal static class WorkshopPendingStartPatch
    {
        private static bool Prefix() => !WorkshopRemoteCraft.ClientBusy;
    }
}
