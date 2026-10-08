using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    internal static class CookingFeastExperience
    {
        // Native Recipe outputs one physical *_Material token per execution.
        // The placed feast's ten servings are a separate native lifecycle.
        private static readonly HashSet<string> Materials = new HashSet<string>(StringComparer.Ordinal) {
            "FeastMeadows_Material", "FeastBlackforest_Material", "FeastSwamps_Material",
            "FeastMountains_Material", "FeastOceans_Material", "FeastPlains_Material",
            "FeastMistlands_Material", "FeastAshlands_Material", "FeastDeepNorth_Material"
        };

        internal static bool IsPreparation(Recipe recipe) => recipe?.m_item != null &&
            recipe.m_craftingStation != null && recipe.m_craftingStation.m_craftingSkill == Skills.SkillType.Cooking &&
            Materials.Contains(GatheringProgressionService.Normalize(recipe.m_item.gameObject.name));

        // Adapter for the one native DoCrafting RaiseSkill call, not a global hook.
        // Vanilla contributes one raw Cooking unit per recipe. Use the frozen confirmed
        // count for feasts instead of the later GUI batch reread.
        internal static void RaisePreparationSkill(Character receiver, Skills.SkillType skill, float amount)
        {
            CraftingXpState state = CraftingXpState.Current;
            bool boost = skill == Skills.SkillType.Cooking && state != null && !state.Upgrade &&
                state.CompletedRecipes > 0 && ReferenceEquals(receiver, state.Player) &&
                IsPreparation(state.Recipe);
            if (boost)
            {
                if (state.FeastBoostApplied) return; // The same native contribution cannot be replayed.
                state.FeastBoostApplied = true;
            }
            receiver.RaiseSkill(skill, boost ? state.CompletedRecipes * 5f : amount);
        }
    }
}


