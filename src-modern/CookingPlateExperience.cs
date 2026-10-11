using System;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class CookingPlateExperience
    {
        internal sealed class Scope { internal Scope Previous; internal Placement Accepted; }
        internal sealed class Placement
        {
            internal Player Player; internal Piece Piece; internal ItemDrop Food;
            internal int Before; internal bool RemotePaid, Awarded;
            internal float Raw, Coefficient; internal string Key;
        }
        [ThreadStatic] private static Scope Current;

        internal static Scope Begin(Player player)
        {
            var scope = new Scope { Previous = Current };
            Current = scope;
            return scope;
        }
        internal static void End(Scope scope) { if (scope != null) Current = scope.Previous; }

        internal static Placement BeforeTry(Player player, Piece piece)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || player == null || player != Player.m_localPlayer ||
                piece == null || player.NoCostCheat() || player.m_noPlacementCost) return null;
            ItemDrop.ItemData tool = player.GetRightItem();
            if (tool?.m_dropPrefab == null || GatheringProgressionService.Normalize(tool.m_dropPrefab.name) != "Feaster" ||
                tool.m_shared?.m_buildPieces?.m_pieces == null || !tool.m_shared.m_buildPieces.m_pieces.Contains(piece.gameObject) ||
                piece.m_resources == null || piece.m_resources.Length != 1) return null;
            Piece.Requirement cost = piece.m_resources[0];
            if (cost?.m_resItem?.m_itemData?.m_shared == null || cost.GetAmount(0) != 1) return null;
            string key = GatheringProgressionService.Normalize(cost.m_resItem.gameObject.name);
            Recipe recipe = CookingPlateRecipeLineage.FindRecipe(key, out string ingredient);
            float raw = recipe != null && CookingFeastExperience.IsPreparation(recipe) ? 5f : 1f;
            float coefficient = recipe != null ? CookingRecipeProgression.IngredientCoefficient(recipe) :
                1f + .5f * (CookingRecipeProgression.IngredientTier(ingredient) - 1);
            return new Placement { Player = player, Piece = piece, Food = cost.m_resItem,
                Before = player.GetInventory().CountItems(cost.m_resItem.m_itemData.m_shared.m_name, -1, false),
                RemotePaid = WorkshopRemoteCraft.HasBuildCredit(player, piece), Raw = raw, Coefficient = coefficient, Key = key };
        }
        internal static void AfterTry(Placement state, bool succeeded)
        {
            if (state == null || !succeeded) return;
            // Existing workshop credit has already paid its personal/storage costs.
            // Preserve that transaction boundary; do not debit or consume it here.
            if (state.RemotePaid) { Award(state); return; }
            if (Current != null) Current.Accepted = state;
        }
        internal static void AfterPayment(Player player, Piece.Requirement[] requirements, int quality, int itemQuality, int multiplier)
        {
            Placement state = Current?.Accepted;
            if (state == null || state.Awarded || state.Player != player ||
                !ReferenceEquals(requirements, state.Piece.m_resources) || quality != 0 || itemQuality != -1 || multiplier != 1) return;
            int remaining = player.GetInventory().CountItems(state.Food.m_itemData.m_shared.m_name, -1, false);
            if (state.Before - remaining != 1) return;
            Award(state);
        }
        private static void Award(Placement state)
        {
            if (state.Awarded || !MasteryPlugin.Settings.Enabled.Value || state.Player != Player.m_localPlayer) return;
            state.Awarded = true;
            // Every paid placement is an action. A feast is one physical material,
            // not ten servings; plating is not a fourth resource-scaled mead stage.
            using (ExperienceContext.BeginCookingRecipe(state.Player, state.Coefficient, false, "plate." + state.Key))
                state.Player.RaiseSkill(Skills.SkillType.Cooking, state.Raw);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class CookingPlateScopePatch
    {
        private static void Prefix(Player __instance, out CookingPlateExperience.Scope __state) =>
            __state = CookingPlateExperience.Begin(__instance);
        private static void Finalizer(CookingPlateExperience.Scope __state) => CookingPlateExperience.End(__state);
    }
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class CookingPlateAcceptedPatch
    {
        private static void Prefix(Player __instance, Piece piece, out CookingPlateExperience.Placement __state) =>
            __state = CookingPlateExperience.BeforeTry(__instance, piece);
        private static void Postfix(bool __result, CookingPlateExperience.Placement __state) =>
            CookingPlateExperience.AfterTry(__state, __result);
    }
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class CookingPlatePaymentPatch
    {
        private static void Postfix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier) =>
            CookingPlateExperience.AfterPayment(__instance, requirements, qualityLevel, itemQuality, multiplier);
    }
}
