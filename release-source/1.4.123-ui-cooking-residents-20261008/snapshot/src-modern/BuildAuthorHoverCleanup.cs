using HarmonyLib;

namespace ValheimMastery
{
    // Hide only the vanilla hover window. Piece/ZDO creator data must remain intact
    // for ownership, permissions, and the Crafting70 structural bonus.
    [HarmonyPatch(typeof(Hud), nameof(Hud.TryShowBuildPieceAuthorInfo))]
    internal static class BuildAuthorHoverCleanupPatch
    {
        private static bool Prefix(Hud __instance)
        {
            if (__instance?.m_hoveredPieceAuthorWindow != null)
                __instance.m_hoveredPieceAuthorWindow.SetActive(false);
            return false;
        }
    }
}
