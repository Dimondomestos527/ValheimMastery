using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CookingLegacyMigrationService
    {

        internal static void Tick()
        {
            // Historical unknown products remain unknown. Existing tags are retained;
            // new products are stamped only at a confirmed production boundary.
            return;
        }
    }

    internal sealed class MeadDurationState
    {
        internal CookingMeadTiming.Plan Plan;
        internal StatusEffect Effect;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Cooking35MeadDurationPatch
    {
        private static void Prefix(Player __instance, ItemDrop.ItemData item, out MeadDurationState __state)
        {
            __state = null;
            StatusEffect effect = item?.m_shared?.m_consumeStatusEffect;
            if (__instance == null || effect == null || effect.m_ttl <= 0f || !item.m_shared.m_isDrink) return;
            var plan = CookingMeadTiming.Prepare(item, MasterFeastThemeService.GetTheme(__instance));
            if (plan != null) __state = new MeadDurationState { Effect = effect, Plan = plan };
        }
        private static void Postfix(Player __instance, bool __result, MeadDurationState __state)
        {
            if (!__result || __state?.Effect == null) return;
            // Set the actual instance, including vanilla's refresh of an existing
            // effect. Never mutate a shared prefab or compound a previous bonus.
            var active = __instance.GetSEMan().GetStatusEffect(__state.Effect.NameHash());
            if (active != null)
            {
                // Vanilla clones the template. Do not write through an unexpected
                // shared instance returned by another status-effect implementation.
                if (ReferenceEquals(active, __state.Effect)) return;
                __state.Plan.Apply(active);
            }
        }
    }

    internal sealed class CraftBonusState
    {
        internal float Chance;
        internal int Amount;
        internal bool Applied;
        internal bool PreviousBonusSoundContext;


    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class SkillCraftBonusPatch
    {
        [ThreadStatic] internal static bool ActiveBonusSoundContext;
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void Prefix(InventoryGui __instance, Player player, out CraftBonusState __state)
        {
            __state = new CraftBonusState();
            if (__instance == null || player == null || __instance.m_craftRecipe == null || __instance.m_craftUpgradeItem != null) return;
            __state.PreviousBonusSoundContext = ActiveBonusSoundContext;
            ActiveBonusSoundContext = true;
            CraftingStation station = player.GetCurrentCraftingStation();
            Skills.SkillType skill = station != null ? station.m_craftingSkill : Skills.SkillType.Crafting;

            __state.Chance = __instance.m_craftBonusChance;
            __state.Amount = __instance.m_craftBonusAmount;
            __state.Applied = true;
            __instance.m_craftBonusAmount = Mathf.Max(1, __instance.m_craftRecipe.m_amount);
            if (skill == Skills.SkillType.Cooking) __instance.m_craftBonusChance = CookingRecipeProgression.BonusChanceCoefficient(__instance.m_craftRecipe);
        }
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance, CraftBonusState __state) => Restore(__instance, __state);
        [HarmonyFinalizer]
        private static Exception Finalizer(InventoryGui __instance, CraftBonusState __state, Exception __exception) { Restore(__instance, __state); return __exception; }
        private static void Restore(InventoryGui gui, CraftBonusState state)
        {
            if (state == null) return;
            ActiveBonusSoundContext = state.PreviousBonusSoundContext;
            if (gui == null || !state.Applied) return;
            gui.m_craftBonusChance = state.Chance; gui.m_craftBonusAmount = state.Amount; state.Applied = false;

        }
    }

    [HarmonyPatch(typeof(CookingStation), "OnInteract")]
    internal static class CookingBonusChancePatch
    {
        [ThreadStatic] internal static bool ActiveBonusSoundContext;
        private static void Prefix(out CraftBonusState __state)
        {
            __state = new CraftBonusState();
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return;
            __state.PreviousBonusSoundContext = ActiveBonusSoundContext;
            ActiveBonusSoundContext = true;
            __state.Chance = gui.m_craftBonusChance; __state.Amount = gui.m_craftBonusAmount; __state.Applied = true;
            gui.m_craftBonusChance = 0.50f; gui.m_craftBonusAmount = 1;
        }
        private static void Postfix(CraftBonusState __state) => Restore(__state);
        private static Exception Finalizer(CraftBonusState __state, Exception __exception) { Restore(__state); return __exception; }
        private static void Restore(CraftBonusState state)
        {
            InventoryGui gui = InventoryGui.instance;
            if (state == null) return;
            ActiveBonusSoundContext = state.PreviousBonusSoundContext;
            if (gui == null || !state.Applied) return;
            gui.m_craftBonusChance = state.Chance; gui.m_craftBonusAmount = state.Amount; state.Applied = false;
        }
    }

    internal sealed class FermenterBonusState
    {
        internal bool Bonus;
        internal bool BonusApplied;
        internal bool FeedbackPlayed;
        internal Vector3 BonusPosition;
        internal Fermenter.ItemConversion Conversion;
        internal int Amount;
        internal ItemDrop Output;
        internal bool HadTag;
        internal string OldTag;
        internal int CookingTier;
        internal Player Player;
        internal long Author;
        internal int InstanceCountBefore;
    }
    internal static class FermenterBonusService
    {
        private static readonly ConditionalWeakTable<Fermenter, FermenterBonusState> States = new ConditionalWeakTable<Fermenter, FermenterBonusState>();
        internal static readonly int CookingTierKey = "valheim_mastery.fermenter.cooking_tier".GetStableHashCode();
        internal static readonly int AuthorKey = "valheim_mastery.fermenter.author".GetStableHashCode();
        internal static void RecordCook(Fermenter fermenter, long sender)
        {
            if (fermenter?.m_nview?.IsOwner() != true) return;
            CookingProductRecord product = CookingFermenterProtocol.ActiveProduct;
            if (product == null) return;
            int tier = 0;
            if (product.Data.TryGetValue(Cooking35Service.CookMasteryKey, out string raw)) int.TryParse(raw, out tier);
            fermenter.m_nview.GetZDO().Set(CookingTierKey, tier);
            fermenter.m_nview.GetZDO().Set(AuthorKey, product.Author);
        }
        internal static bool PendingBonus(Fermenter fermenter) => States.GetOrCreateValue(fermenter).Bonus;
        internal static void RestorePendingBonus(Fermenter fermenter, bool bonus) => States.GetOrCreateValue(fermenter).Bonus = bonus;
        internal static void Roll(Fermenter fermenter, long sender)
        {
            if (fermenter == null) return;
            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            Player player = peer != null ? OwnerSkillAuthority.ResolvePlayer(peer) : Player.m_localPlayer;
            float factor = CookingFermenterProtocol.TapFactor >= 0f ? CookingFermenterProtocol.TapFactor : (player != null ? player.GetSkillFactor(Skills.SkillType.Cooking) : 0f);
            float chance = Mathf.Clamp01(factor * 0.50f);
            FermenterBonusState state = States.GetOrCreateValue(fermenter);
            state.Player = player;
            state.Bonus = UnityEngine.Random.value < chance;
            state.BonusApplied = false;
            state.FeedbackPlayed = false;
            state.BonusPosition = fermenter.m_outputPoint != null ? fermenter.m_outputPoint.position : fermenter.transform.position;
        }
        internal static FermenterBonusState Begin(Fermenter fermenter)
        {
            FermenterBonusState state = States.GetOrCreateValue(fermenter);
            state.Conversion = fermenter.GetItemConversion(fermenter.m_delayedTapItem);
            if (state.Conversion == null) return null;
            state.Amount = state.Conversion.m_producedItems;
            state.BonusApplied = state.Bonus;
            if (state.Bonus) state.Conversion.m_producedItems = state.Amount + 1;
            state.Bonus = false;
            state.CookingTier = fermenter.m_nview?.GetZDO()?.GetInt(CookingTierKey, 0) ?? 0;
            state.Author = fermenter.m_nview?.GetZDO()?.GetLong(AuthorKey, 0) ?? 0;
            state.InstanceCountBefore = ItemDrop.s_instances.Count;
            state.Output = state.Conversion.m_to != null ? state.Conversion.m_to.GetComponent<ItemDrop>() : null;
            if (state.CookingTier >= 35 && state.Output?.m_itemData != null)
            {
                state.HadTag = state.Output.m_itemData.m_customData.TryGetValue(Cooking35Service.CookMasteryKey, out state.OldTag);
                state.Output.m_itemData.m_customData[Cooking35Service.CookMasteryKey] = state.CookingTier.ToString();
            }
            return state;
        }
        internal static void End(FermenterBonusState state, bool completed)
        {
            if (completed && state?.Output != null && state.CookingTier >= 35 && !CookingFermenterOutput.NativeSourceActive)
                CookingAuthorNetwork.StampNew(state.InstanceCountBefore, state.Output.gameObject.name,
                    state.BonusPosition, state.Author, state.CookingTier);
            if (completed && state?.BonusApplied == true && !state.FeedbackPlayed)
            {
                state.FeedbackPlayed = true;
                PerkVisualService.PlayGenericResourceBonus(state.Player, state.BonusPosition, skill: Skills.SkillType.Cooking);
            }
            if (state?.Conversion != null) state.Conversion.m_producedItems = state.Amount;
            if (state?.Output?.m_itemData != null && state.CookingTier >= 35)
            {
                if (state.HadTag) state.Output.m_itemData.m_customData[Cooking35Service.CookMasteryKey] = state.OldTag;
                else state.Output.m_itemData.m_customData.Remove(Cooking35Service.CookMasteryKey);
            }
        }
    }

    [HarmonyPatch(typeof(Fermenter), "RPC_Tap")]
    internal static class FermenterBonusRollPatch
    {
        private static void Prefix(Fermenter __instance, long sender) => FermenterBonusService.Roll(__instance, sender);
    }

    [HarmonyPatch(typeof(Fermenter), "RPC_AddItem")]
    internal static class FermenterCookRecordPatch
    {
        private static void Prefix(Fermenter __instance, out int __state) => __state = __instance.GetContent();
        private static void Postfix(Fermenter __instance, long sender, int __state)
        {
            if (__state == 0 && __instance.GetContent() != 0) FermenterBonusService.RecordCook(__instance, sender);
        }
    }

    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.DelayedTap))]
    internal static class FermenterBonusOutputPatch
    {
        private static void Prefix(Fermenter __instance, out FermenterBonusState __state) => __state = FermenterBonusService.Begin(__instance);
        private static void Postfix(FermenterBonusState __state) => FermenterBonusService.End(__state, true);
        private static Exception Finalizer(FermenterBonusState __state, Exception __exception) { FermenterBonusService.End(__state, false); return __exception; }
    }
}
