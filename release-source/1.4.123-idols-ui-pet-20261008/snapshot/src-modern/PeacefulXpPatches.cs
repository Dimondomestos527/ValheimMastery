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

        internal static void AwardCraft(Player player, Recipe recipe, int quality, bool upgrade, int completedRecipes = 1)
        {
            if (recipe == null || completedRecipes <= 0) return;
            float value = ResourceValue(recipe.m_resources, quality);
            string item = recipe.m_item != null ? TierDatabase.ItemKey(recipe.m_item.m_itemData) : "unknown";
            string key = (upgrade ? "upgrade." : "craft.") + item + ".q" + quality;
            Award(player, Skills.SkillType.Crafting, key, (0.15f + value) * completedRecipes);
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

    internal sealed class CookingDebitState
    {
        [System.ThreadStatic] internal static CookingDebitState Current;
        internal CookingDebitState Parent;
        internal Inventory Inventory;
        internal ItemDrop.ItemData Item;
        internal int Before;
        internal int Attempts;
        internal int SuccessfulDebits;
        internal int NestedDebits;
        internal bool Eligible;
        internal bool Awarded;

        internal int Remaining() => Inventory != null && Item != null && Inventory.GetAllItems().Contains(Item)
            ? Item.m_stack : 0;
    }

    [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.CookItem))]
    internal static class CookingXpPatch
    {
        private static void Prefix(CookingStation __instance, Humanoid user, ItemDrop.ItemData item,
            out CookingDebitState __state)
        {
            __state = new CookingDebitState { Parent = CookingDebitState.Current,
                Inventory = user?.GetInventory(), Item = item };
            // Install the scope even for rejected/nested calls so they cannot credit a parent call.
            CookingDebitState.Current = __state;
            __state.Before = __state.Remaining();
            if (!(user is Player player) || player != Player.m_localPlayer || __instance == null ||
                item?.m_shared == null || __state.Before <= 0) return;
            // Native incompatible-message handling returns true without consuming an item.
            if (__instance.m_incompatibleItems != null)
                foreach (var incompatible in __instance.m_incompatibleItems)
                    if (incompatible?.m_item?.m_itemData?.m_shared?.m_name == item.m_shared.m_name) return;
            __state.Eligible = __instance.IsItemAllowed(item) && __instance.GetFreeSlot() >= 0;
        }

        private static void Postfix(Humanoid user, ItemDrop.ItemData item, bool __result, CookingDebitState __state)
        {
            if (__state == null || __state.Awarded || !__state.Eligible || !__result ||
                !ReferenceEquals(__state.Inventory, user?.GetInventory()) || !ReferenceEquals(__state.Item, item) ||
                __state.Attempts != 1 || __state.SuccessfulDebits != 1 ||
                __state.Before - __state.Remaining() - __state.NestedDebits != 1) return;
            // This confirms local debit only, not asynchronous owner RPC acceptance.
            __state.Awarded = true;
            PeacefulXp.AwardCooking(user as Player, item);
        }

        private static void Finalizer(CookingDebitState __state)
        {
            if (__state == null) return;
            CookingDebitState parent = __state.Parent;
            if (parent != null && ReferenceEquals(parent.Inventory, __state.Inventory) &&
                ReferenceEquals(parent.Item, __state.Item))
                parent.NestedDebits += System.Math.Max(0, __state.Before - __state.Remaining());
            CookingDebitState.Current = parent;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveOneItem), new[] { typeof(ItemDrop.ItemData) })]
    internal static class CookingDebitObservationPatch
    {
        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, bool __result)
        {
            CookingDebitState state = CookingDebitState.Current;
            if (state == null || !state.Eligible || !ReferenceEquals(state.Inventory, __instance) ||
                !ReferenceEquals(state.Item, item)) return;
            state.Attempts++;
            if (__result) state.SuccessfulDebits++;
        }
    }

    internal sealed class CraftingXpState
    {
        [System.ThreadStatic] internal static CraftingXpState Current;
        internal CraftingXpState Parent;
        internal InventoryGui Gui;
        internal Player Player;
        internal int CompletedRecipes;
        internal bool Awarded;
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
                Parent = CraftingXpState.Current,
                Gui = __instance,
                Player = player,
                Recipe = __instance?.m_craftRecipe,
                Upgrade = upgraded != null,
                Quality = upgraded != null ? upgraded.m_quality + 1 : 1
            };
            CraftingXpState.Current = __state;
            __state.Outcome = CraftingOutcomeSnapshot.Capture(player, __state.Recipe, __state.Quality);
        }
        private static void Postfix(Player player, CraftingXpState __state)
        {
            if (__state == null || __state.Awarded || __state.CompletedRecipes <= 0 ||
                __state.Player != player || __state.Outcome?.HasSuccessfulOutput() != true) return;
            __state.Awarded = true;
            PeacefulXp.AwardCraft(player, __state.Recipe, __state.Quality, __state.Upgrade, __state.CompletedRecipes);
        }

        private static void Finalizer(CraftingXpState __state)
        {
            if (__state != null) CraftingXpState.Current = __state.Parent;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PieceXpPatch
    {
        private static void Postfix(Player __instance, Piece piece) => PeacefulXp.AwardPiece(__instance, piece);
    }
}



