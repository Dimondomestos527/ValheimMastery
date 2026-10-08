using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Cooking35Service
    {
        // Only 40% of normal elapsed decay is applied: stats fall 60% slower.
        internal const float DecayRateMultiplier = 0.40f;
        internal const float PreparedMealStatBonus = 0f;
        internal const string CookMasteryKey = "valheim_mastery.cooking_tier";
        private static readonly HashSet<string> RawIngredients = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Raspberry", "Blueberries", "Cloudberry", "Honey", "Mushroom", "MushroomYellow", "MushroomJotunPuffs", "Magecap",
            "Carrot", "Turnip", "Onion", "Barley", "Flax", "Dandelion", "Thistle", "Bloodbag", "Entrails",
            "RawMeat", "RawLoxMeat", "RawHareMeat", "FishRaw", "ChickenEgg", "ChickenMeat", "BreadDough", "Pukeberries"
        };

        internal static bool IsPreparedMeal(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable ||
                item.m_shared.m_isDrink || item.m_shared.m_foodBurnTime <= 0f ||
                (item.m_shared.m_food <= 0f && item.m_shared.m_foodStamina <= 0f && item.m_shared.m_foodEitr <= 0f)) return false;
            string key = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
            if (string.IsNullOrEmpty(key)) return false;
            if (key.IndexOf("Feast", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return !RawIngredients.Contains(key) && key.IndexOf("Raw", StringComparison.OrdinalIgnoreCase) < 0;
        }

        internal static bool IsTimedMead(ItemDrop.ItemData item)
        {
            return item?.m_shared != null && item.m_shared.m_isDrink &&
                item.m_shared.m_consumeStatusEffect != null && item.m_shared.m_consumeStatusEffect.m_ttl > 0f;
        }

        // A cooked item carries Valheim's native crafter id. That lets the cook's mastery
        // follow the meal to another player without trusting the eater's local config.
        internal static bool HasCookMastery(Player eater, ItemDrop.ItemData item, int milestone)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(CookMasteryKey, out string stored) && int.TryParse(stored, out int storedTier) && storedTier >= milestone) return true;
            // Placed feasts carry the creator's confirmed tier on the serving.
            // The eater's level must not promote an unmarked feast to Master Feast.
            if (Cooking70Service.IsFeast(item)) return false;
            if (eater != null && PerkRuntimeService.HasPerk(eater, Skills.SkillType.Cooking, milestone)) return true;
            long crafterId = item?.m_crafterID ?? 0L;
            if (crafterId == 0L) return false;
            foreach (Player candidate in Player.GetAllPlayers())
                if (candidate != null && candidate.GetPlayerID() == crafterId && PerkRuntimeService.HasPerk(candidate, Skills.SkillType.Cooking, milestone))
                    return true;
            return false;
        }

        internal static void RecomputeSlowDecay(Player player, Player.Food food)
        {
            if (player == null || food?.m_item?.m_shared == null || !IsPreparedMeal(food.m_item) ||
                (!HasCookMastery(player, food.m_item, 35) && !CookingFoodPersistence.Has(player, food.m_name))) return;
            float burn = food.m_item.m_shared.m_foodBurnTime;
            if (burn <= 0f) return;
            float remaining01 = Mathf.Clamp01(food.m_time / burn);
            float effectiveRemaining01 = Mathf.Clamp01(1f - (1f - remaining01) * DecayRateMultiplier);
            float strength = Mathf.Pow(effectiveRemaining01, 0.30f);
            food.m_health = food.m_item.m_shared.m_food * strength;
            food.m_stamina = food.m_item.m_shared.m_foodStamina * strength;
            food.m_eitr = food.m_item.m_shared.m_foodEitr * strength;
        }

        internal static void TryPlayMealFeedback(Player player, ItemDrop.ItemData item)
        {
            if (player == null || !IsPreparedMeal(item) || !HasCookMastery(player, item, 35)) return;
            // The old blue meal burst is intentionally retired; it is reserved for Fists 70.
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateFood")]
    internal static class Cooking35FoodDecayPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance?.m_foods == null) return;
            foreach (Player.Food food in __instance.m_foods) Cooking35Service.RecomputeSlowDecay(__instance, food);
        }
    }

    [HarmonyPatch(typeof(Player), "EatFood")]
    internal static class Cooking35EatFeedbackPatch
    {
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result)
            {
                CookingFoodPersistence.Record(__instance, item);
                Cooking35Service.TryPlayMealFeedback(__instance, item);
            }
        }
    }

    [HarmonyPatch(typeof(CookingStation), "CookItem")]
    internal static class CookingMasteryStationRecordPatch
    {
        private static void Prefix(CookingStation __instance, Humanoid user)
        {
            Player cook = user as Player;
            if (cook == null || __instance?.m_nview?.GetZDO() == null) return;
            int tier = PerkRuntimeService.HasPerk(cook, Skills.SkillType.Cooking, 70) ? 70 : (PerkRuntimeService.HasPerk(cook, Skills.SkillType.Cooking, 35) ? 35 : 0);
            if (cook == Player.m_localPlayer) __instance.m_nview.InvokeRPC("VM_CookingAuthor", tier);
        }
    }

    [HarmonyPatch(typeof(CookingStation), "SpawnItem")]
    internal static class CookingMasteryFoodStampPatch
    {
        private static void Prefix(out int __state) => __state = ItemDrop.s_instances.Count;
        private static void Postfix(CookingStation __instance, string name, int slot, Vector3 userPoint, int __state)
        {
            ZDO zdo = __instance?.m_nview?.GetZDO();
            if (zdo == null || !__instance.m_nview.IsOwner()) return;
            long author = zdo.GetLong("vm.cook.slot.author." + slot, 0);
            int tier = zdo.GetInt("vm.cook.slot.tier." + slot, 0);
            Vector3 point = __instance.m_spawnPoint != null ? __instance.m_spawnPoint.position : __instance.m_slots[slot].position;
            CookingAuthorNetwork.StampNew(__state, name, point, author, tier);
        }
    }}
