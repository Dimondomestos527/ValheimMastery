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
            bool crafting = MasteryPlugin.Settings.Enabled.Value && skill == Skills.SkillType.Crafting &&
                state != null && state.CompletedRecipes > 0 && ReferenceEquals(receiver, state.Player);
            // PeacefulXp is the sole Crafting award for a confirmed recipe result.
            // Cooking keeps its separately authored recipe-stage semantics below.
            if (crafting) return;
            bool cooking = MasteryPlugin.Settings.Enabled.Value && skill == Skills.SkillType.Cooking && state != null &&
                !state.Upgrade && state.CompletedRecipes > 0 && ReferenceEquals(receiver, state.Player) &&
                CookingRecipeProgression.IsCooking(state.Recipe);
            if (!cooking) { receiver.RaiseSkill(skill, amount); return; }
            // Reuse the existing invocation flag; it now fences every recipe Cooking contribution.
            if (state.FeastBoostApplied) return;
            state.FeastBoostApplied = true;
            bool mead = CookingRecipeProgression.IsMeadBase(state.Recipe);
            float perRecipe = mead ? MeadXpReceiptPolicy.PreparationXp : IsPreparation(state.Recipe) ? 5f : 1f;
            using (ExperienceContext.BeginCookingRecipe(state.Player,
                CookingRecipeProgression.IngredientCoefficient(state.Recipe, state.Quality), mead))
                receiver.RaiseSkill(skill, state.CompletedRecipes * perRecipe);
        }
    }
}
