using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class PeacefulXp
    {
        // Resource XP is intentionally a direct sum. No square-root compression: iron remains worth more than bronze.
        internal static void AwardCooking(Player player, ItemDrop.ItemData ingredient)
        {
            float value = TierDatabase.GetResourceValue(ingredient, 1);
            Award(player, Skills.SkillType.Cooking, "cook." + TierDatabase.ItemKey(ingredient), 0.10f + value);
        }

        internal static void AwardCraft(Player player, Recipe recipe, int quality, bool upgrade)
        {
            if (recipe == null) return;
            float value = ResourceValue(recipe.m_resources, quality);
            string item = recipe.m_item != null ? TierDatabase.ItemKey(recipe.m_item.m_itemData) : "unknown";
            string key = (upgrade ? "upgrade." : "craft.") + item + ".q" + quality;
            Award(player, Skills.SkillType.Crafting, key, 0.15f + value);
        }

        internal static void AwardPiece(Player player, Piece piece)
        {
            if (piece == null) return;
            float value = ResourceValue(piece.m_resources, 1);
            bool plant = piece.GetComponent<Plant>() != null;
            if (!plant && !CraftingBuildReuseService.AllowPlacement(player, piece)) return;
            Skills.SkillType skill = plant ? Skills.SkillType.Farming : Skills.SkillType.Crafting;
            string key = piece.gameObject != null ? piece.gameObject.name.Replace("(Clone)", string.Empty) : "piece";
            if (plant)
                ExperienceContext.ObserveGatheringAction(player, skill, key, GatheringProgressionService.GetResourceTier(key, skill));
            Award(player, skill, (plant ? "plant." : "build.") + key, 0.10f + value);
        }

        private static void Award(Player player, Skills.SkillType skill, string key, float baseXp)
        {
            if (player == null || player != Player.m_localPlayer || baseXp <= 0f) return;
            // Farming plants already install their biome-tier context in AwardPiece.
            if (skill != Skills.SkillType.Farming) ExperienceContext.ObserveAction(player, skill, key);
            player.RaiseSkill(skill, baseXp);
        }

        private static float ResourceValue(Piece.Requirement[] requirements, int quality)
        {
            float total = 0f;
            if (requirements == null) return total;
            foreach (Piece.Requirement requirement in requirements)
                if (requirement != null) total += TierDatabase.GetResourceValue(requirement.m_resItem?.m_itemData, requirement.GetAmount(quality));
            return Mathf.Max(0f, total);
        }
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.CookItem))]
    internal static class CookingXpPatch
    {
        private static void Postfix(Humanoid user, ItemDrop.ItemData item, bool __result)
        {
            if (__result) PeacefulXp.AwardCooking(user as Player, item);
        }
    }

    internal sealed class CraftingXpState
    {
        internal Recipe Recipe;
        internal int Quality;
        internal bool Upgrade;
        internal CraftingOutcomeSnapshot Outcome;
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class CraftingXpPatch
    {
        private static void Prefix(InventoryGui __instance, Player player, out CraftingXpState __state)
        {
            ItemDrop.ItemData upgraded = __instance?.m_craftUpgradeItem;
            __state = new CraftingXpState
            {
                Recipe = __instance?.m_craftRecipe,
                Upgrade = upgraded != null,
                Quality = upgraded != null ? upgraded.m_quality + 1 : 1
            };
            __state.Outcome = CraftingOutcomeSnapshot.Capture(player, __state.Recipe, __state.Quality);
        }
        private static void Postfix(Player player, CraftingXpState __state)
        {
            if (__state?.Outcome?.HasSuccessfulOutput() == true)
                PeacefulXp.AwardCraft(player, __state.Recipe, __state.Quality, __state.Upgrade);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PieceXpPatch
    {
        private static void Postfix(Player __instance, Piece piece) => PeacefulXp.AwardPiece(__instance, piece);
    }
}
