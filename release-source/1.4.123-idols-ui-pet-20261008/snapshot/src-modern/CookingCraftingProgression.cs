using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CookingLegacyMigrationService
    {
        private const string MigrationKey = "valheim_mastery.cooking35.inventory_migration.v2";
        private static float _nextAt;

        internal static void Tick()
        {
            if (Time.time < _nextAt) return;
            _nextAt = Time.time + 2f;
            Player player = Player.m_localPlayer;
            if (player == null || player.m_customData.ContainsKey(MigrationKey) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Cooking, 35)) return;
            int tier = PerkRuntimeService.HasPerk(player, Skills.SkillType.Cooking, 70) ? 70 : 35;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (item?.m_shared == null) continue;
                bool timedMead = item.m_shared.m_isDrink && item.m_shared.m_consumeStatusEffect != null && item.m_shared.m_consumeStatusEffect.m_ttl > 0f;
                if (Cooking35Service.IsPreparedMeal(item) || timedMead)
                    item.m_customData[Cooking35Service.CookMasteryKey] = tier.ToString();
            }
            player.m_customData[MigrationKey] = "1";
            player.GetInventory().Changed();
            MasteryPlugin.Log.LogInfo("Cooking35 migrated existing prepared food and timed meads in the local inventory.");
        }
    }

    internal sealed class MeadDurationState
    {
        internal StatusEffect Effect;
        internal float Ttl;
        internal bool Mastery;
        internal bool Swamp;
        internal bool MountainsCooldown;
        internal bool Mountains;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Cooking35MeadDurationPatch
    {
        private static void Prefix(Player __instance, ItemDrop.ItemData item, out MeadDurationState __state)
        {
            __state = null;
            StatusEffect effect = item?.m_shared?.m_consumeStatusEffect;
            if (__instance == null || effect == null || effect.m_ttl <= 0f || !item.m_shared.m_isDrink) return;
            __state = new MeadDurationState { Effect = effect, Ttl = effect.m_ttl,
                Mastery = Cooking35Service.HasCookMastery(__instance, item, 35),
                Swamp = MasterFeastThemeService.GetTheme(__instance) == FeastTheme.Swamp,
                Mountains = MasterFeastThemeService.GetTheme(__instance) == FeastTheme.Mountains,
                MountainsCooldown = MasterFeastThemeService.GetTheme(__instance) == FeastTheme.Mountains && FeastMeadStrength.IsRecoveryCooldown(effect) };
        }
        private static void Postfix(Player __instance, bool __result, MeadDurationState __state)
        {
            if (!__result || __state?.Effect == null) return;
            // Set the actual instance, including vanilla's refresh of an existing
            // effect. Never mutate a shared prefab or compound a previous bonus.
            var active = __instance.GetSEMan().GetStatusEffect(__state.Effect.NameHash());
            if (active != null)
            {
                active.m_ttl = __state.Ttl *
                    (1f + (__state.Mastery ? .40f : 0f) + (__state.Swamp ? .30f : 0f)) *
                    (__state.MountainsCooldown ? .5f : 1f);
                FeastMeadRetaliation.Register(active, __state.Effect, __state.Mountains);
            }
        }
    }

    internal sealed class CraftBonusState
    {
        internal float Chance;
        internal int Amount;
        internal bool Applied;
        internal bool PreviousBonusSoundContext;
        internal ItemDrop.ItemData TaggedTemplate;
        internal bool HadTag;
        internal string OldTag;
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
            ItemDrop.ItemData output = __instance.m_craftRecipe.m_item?.m_itemData;
            bool masteryFood = skill == Skills.SkillType.Cooking && PerkRuntimeService.HasPerk(player, Skills.SkillType.Cooking, 35) &&
                (Cooking35Service.IsPreparedMeal(output) || (output?.m_shared?.m_isDrink == true && output.m_shared.m_consumeStatusEffect?.m_ttl > 0f));
            if (masteryFood)
            {
                __state.TaggedTemplate = output;
                __state.HadTag = output.m_customData.TryGetValue(Cooking35Service.CookMasteryKey, out __state.OldTag);
                output.m_customData[Cooking35Service.CookMasteryKey] =
                    (PerkRuntimeService.HasPerk(player, Skills.SkillType.Cooking, 70) ? 70 : 35).ToString();
            }
            __state.Chance = __instance.m_craftBonusChance;
            __state.Amount = __instance.m_craftBonusAmount;
            __state.Applied = true;
            __instance.m_craftBonusAmount = Mathf.Max(1, __instance.m_craftRecipe.m_amount);
            if (skill == Skills.SkillType.Cooking) __instance.m_craftBonusChance = 0.50f;
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
            if (state.TaggedTemplate != null)
            {
                if (state.HadTag) state.TaggedTemplate.m_customData[Cooking35Service.CookMasteryKey] = state.OldTag;
                else state.TaggedTemplate.m_customData.Remove(Cooking35Service.CookMasteryKey);
            }
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
            long author = ProcessingStationProgressionService.ResolveSender(sender);
            int tier = CookingAuthorNetwork.Tier(fermenter.m_nview, author);
            fermenter.m_nview.GetZDO().Set(CookingTierKey, tier);
            fermenter.m_nview.GetZDO().Set(AuthorKey, author);
        }
        internal static void Roll(Fermenter fermenter, long sender)
        {
            if (fermenter == null) return;
            ZNetPeer peer = ZNet.instance?.GetPeer(sender);
            Player player = peer != null ? OwnerSkillAuthority.ResolvePlayer(peer) : Player.m_localPlayer;
            float chance = player != null ? Mathf.Clamp01(player.GetSkillFactor(Skills.SkillType.Cooking) * 0.50f) : 0f;
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
            if (completed && state?.Output != null && state.CookingTier >= 35)
                CookingAuthorNetwork.StampNew(state.InstanceCountBefore, state.Output.gameObject.name,
                    state.BonusPosition, state.Author, state.CookingTier);
            if (completed && state?.BonusApplied == true && !state.FeedbackPlayed)
            {
                state.FeedbackPlayed = true;
                PerkVisualService.PlayGenericResourceBonus(state.Player, state.BonusPosition);
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
