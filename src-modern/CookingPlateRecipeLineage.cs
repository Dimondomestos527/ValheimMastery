using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // Exact native Steam cooking/oven/fermenter conversions, frozen in native-plate-assets.json.
    internal static class CookingPlateRecipeLineage
    {
        private static readonly Dictionary<string,string> Inputs = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BakedPoteitr", "BakedPoteitrUncooked" },
            { "BarleyWine", "BarleyWineBase" },
            { "Bread", "BreadDough" },
            { "CookedAsksvinMeat", "AsksvinMeat" },
            { "CookedBjornMeat", "BjornMeat" },
            { "CookedBoneMawSerpentMeat", "BoneMawSerpentMeat" },
            { "CookedBugMeat", "BugMeat" },
            { "CookedChickenMeat", "ChickenMeat" },
            { "CookedDeerMeat", "DeerMeat" },
            { "CookedHareMeat", "HareMeat" },
            { "CookedLoxMeat", "LoxMeat" },
            { "CookedMeat", "RawMeat" },
            { "CookedMooseMeat", "MooseMeat" },
            { "CookedSealBlubber", "SealBlubber" },
            { "CookedVoltureMeat", "VoltureMeat" },
            { "CookedWolfMeat", "WolfMeat" },
            { "FishAndBread", "FishAndBreadUncooked" },
            { "FishCooked", "FishRaw" },
            { "HoneyGlazedChicken", "HoneyGlazedChickenUncooked" },
            { "KaleChips", "KaleChipsUncooked" },
            { "LoxPie", "LoxPieUncooked" },
            { "MagicallyStuffedShroom", "MagicallyStuffedShroomUncooked" },
            { "MeadBugRepellent", "MeadBaseBugRepellent" },
            { "MeadBzerker", "MeadBaseBzerker" },
            { "MeadEitrLingering", "MeadBaseEitrLingering" },
            { "MeadEitrMinor", "MeadBaseEitrMinor" },
            { "MeadFrostResist", "MeadBaseFrostResist" },
            { "MeadHasty", "MeadBaseHasty" },
            { "MeadHealthLingering", "MeadBaseHealthLingering" },
            { "MeadHealthMajor", "MeadBaseHealthMajor" },
            { "MeadHealthMedium", "MeadBaseHealthMedium" },
            { "MeadHealthMinor", "MeadBaseHealthMinor" },
            { "MeadLightfoot", "MeadBaseLightFoot" },
            { "MeadPoisonResist", "MeadBasePoisonResist" },
            { "MeadStaminaLingering", "MeadBaseStaminaLingering" },
            { "MeadStaminaMedium", "MeadBaseStaminaMedium" },
            { "MeadStaminaMinor", "MeadBaseStaminaMinor" },
            { "MeadStrength", "MeadBaseStrength" },
            { "MeadSwimmer", "MeadBaseSwimmer" },
            { "MeadTamer", "MeadBaseTamer" },
            { "MeadTasty", "MeadBaseTasty" },
            { "MeatPlatter", "MeatPlatterUncooked" },
            { "MisthareSupreme", "MisthareSupremeUncooked" },
            { "NeckTailGrilled", "NeckTail" },
            { "OvenPancake", "OvenPancakeUncooked" },
            { "PiquantPie", "PiquantPieUncooked" },
            { "RoastedCrustPie", "RoastedCrustPieUncooked" },
            { "SerpentMeatCooked", "SerpentMeat" },
            { "VikingCupcake", "VikingCupcakeUncooked" },
        };
        internal static Recipe FindRecipe(string output, out string ingredient)
        {
            ingredient = output;
            if (ObjectDB.instance?.m_recipes != null)
                foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                    if (CookingRecipeProgression.IsCooking(recipe) && recipe.m_item != null &&
                        string.Equals(GatheringProgressionService.Normalize(recipe.m_item.gameObject.name), output, StringComparison.OrdinalIgnoreCase))
                        return recipe;
            if (!Inputs.TryGetValue(output, out string input)) return null;
            ingredient = input;
            if (ObjectDB.instance?.m_recipes != null)
                foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                    if (CookingRecipeProgression.IsCooking(recipe) && recipe.m_item != null &&
                        string.Equals(GatheringProgressionService.Normalize(recipe.m_item.gameObject.name), input, StringComparison.OrdinalIgnoreCase))
                        return recipe;
            return null;
        }
    }
}
