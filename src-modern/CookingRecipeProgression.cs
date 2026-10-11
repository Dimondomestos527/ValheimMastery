using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CookingRecipeProgression
    {
        // Actual Fermenter.m_conversion inputs from current native Steam assets.
        private static readonly HashSet<string> MeadBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MeadBaseHealthMinor", "MeadBaseHealthMedium", "MeadBaseStaminaMinor", "MeadBaseStaminaMedium",
            "MeadBasePoisonResist", "MeadBaseFrostResist", "BarleyWineBase", "MeadBaseTasty",
            "MeadBaseHealthMajor", "MeadBaseStaminaLingering", "MeadBaseEitrMinor", "MeadBaseEitrLingering",
            "MeadBaseHealthLingering", "MeadBaseBzerker", "MeadBaseStrength", "MeadBaseHasty",
            "MeadBaseLightFoot", "MeadBaseSwimmer", "MeadBaseTamer", "MeadBaseBugRepellent"
        };

        internal static bool IsMeadBase(string key) => MeadBases.Contains(GatheringProgressionService.Normalize(key));
        internal static bool IsMeadBase(Recipe recipe) => recipe?.m_item != null &&
            IsMeadBase(recipe.m_item.gameObject.name);
        internal static bool IsCooking(Recipe recipe) => recipe?.m_craftingStation != null &&
            recipe.m_craftingStation.m_craftingSkill == Skills.SkillType.Cooking;

        internal static int IngredientTier(string key) => Math.Max(1, Math.Min(8, Math.Max(
            TierDatabase.GetResourceTier(key), GatheringProgressionService.GetResourceTier(key, Skills.SkillType.Farming))));

        internal static float IngredientCoefficient(Recipe recipe, int quality = 1)
        {
            // Alternative-input recipes need the actual chosen ingredient. Do not price
            // every alternative as if it had been consumed. Native meads/feasts use all inputs.
            if (recipe?.m_resources == null || recipe.m_requireOnlyOneIngredient) return 1f;
            double weight = 0, weightedTier = 0;
            foreach (Piece.Requirement requirement in recipe.m_resources)
            {
                ItemDrop.ItemData item = requirement?.m_resItem?.m_itemData;
                if (item == null) continue;
                int amount = requirement.GetAmount(quality);
                if (amount <= 0) continue;
                string key = GatheringProgressionService.Normalize(requirement.m_resItem.gameObject.name);
                int tier = IngredientTier(key);
                weight += amount; weightedTier += (double)amount * tier;
            }
            return weight > 0 ? (float)(1d + .5d * (weightedTier / weight - 1d)) : 1f;
        }

        internal static float MeadCoefficient(string input)
        {
            if (!IsMeadBase(input) || ObjectDB.instance?.m_recipes == null) return 1f;
            string key = GatheringProgressionService.Normalize(input);
            foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                if (IsCooking(recipe) && recipe.m_item != null &&
                    string.Equals(GatheringProgressionService.Normalize(recipe.m_item.gameObject.name), key, StringComparison.OrdinalIgnoreCase))
                    return IngredientCoefficient(recipe);
            return 1f;
        }

        internal static float BonusChanceCoefficient(Recipe recipe) => IsMeadBase(recipe) ? .25f : .50f;
    }
}
