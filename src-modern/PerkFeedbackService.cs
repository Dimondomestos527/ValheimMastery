using UnityEngine;

namespace ValheimMastery
{
    /// <summary>Gameplay handlers request semantic feedback; they never choose network or prefab details.</summary>
    internal static class PerkFeedbackService
    {
        internal static void Play(Player owner, string profileId, Vector3 position, bool showName = true)
        {
            if (owner == null || string.IsNullOrWhiteSpace(profileId)) return;
            PerkVisualService.PlayAtWorldPosition(owner, profileId, position, showName);
        }

        internal static void PlayLocal(Player owner, string profileId, Vector3 position)
        {
            if (owner == null || string.IsNullOrWhiteSpace(profileId)) return;
            if (VfxRecipeService.Play(profileId, owner, position)) return;
            PerkVisualService.PlayAtWorldPosition(owner, profileId, position, false);
        }
    }
}
