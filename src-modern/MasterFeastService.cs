using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal enum FeastTheme { None, Meadows, BlackForest, Swamp, Mountains, Plains, Mistlands, Ashlands, Oceans, DeepNorth }

    // Pure managed identity selection: testable without constructing a Unity Player.
    internal struct FeastFoodIdentityMigration
    {
        private readonly string Saved;
        private string Exact;
        private string Legacy;
        private bool Ambiguous;

        internal FeastFoodIdentityMigration(string saved)
        { Saved = saved; Exact = null; Legacy = null; Ambiguous = false; }

        internal void Observe(string prefab, string sharedName, float remaining)
        {
            if (remaining <= 0f || string.IsNullOrEmpty(prefab)) return;
            if (string.Equals(prefab, Saved, StringComparison.Ordinal)) Exact = prefab;
            if (!string.Equals(sharedName, Saved, StringComparison.Ordinal)) return;
            if (Legacy != null) Ambiguous = true;
            else Legacy = prefab;
        }

        internal string Resolve() => Exact ?? (!Ambiguous && Legacy != null ? Legacy : Saved);
    }

    internal sealed class MasterFeastState
    {
        internal FeastTheme Theme;
        internal float ExpireTime;
        internal ZDOID CookId;
        internal ItemDrop.ItemData FoodItem;
        internal string SourceItemPrefab = "";
        internal long ConsumedAt;
        internal long LastFeedbackConsumedAt;
    }

    internal static class MasterFeastService
    {
        internal const string ConsumedAtKey = "valheim_mastery.master_feast_consumed_at";
        private const string ActiveFoodKey = "valheim_mastery.master_feast.active_food";
        private const string ActivePrefabKey = "valheim_mastery.master_feast.active_prefab";
        private const string ActiveStampKey = "valheim_mastery.master_feast.active_stamp";
        private const string ActiveThemeKey = "valheim_mastery.master_feast.active_theme";

        internal static FeastTheme GetTheme(ItemDrop.ItemData item)
        {
            string prefab = null;
            item?.m_customData?.TryGetValue(Cooking70Service.FeastPrefabKey, out prefab);
            if (string.IsNullOrEmpty(prefab)) prefab = PerkRuntimeService.ItemPrefabName(item);
            return GetThemeForPrefab(prefab);
        }

        internal static FeastTheme GetThemeForPrefab(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return FeastTheme.None;
            if (prefab.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.DeepNorth;
            if (prefab.IndexOf("Ocean", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Oceans;
            if (prefab.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Ashlands;
            if (prefab.IndexOf("Mistlands", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Mistlands;
            if (prefab.IndexOf("Plains", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Plains;
            if (prefab.IndexOf("Mountain", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Mountains;
            if (prefab.IndexOf("Swamp", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Swamp;
            if (prefab.IndexOf("BlackForest", StringComparison.OrdinalIgnoreCase) >= 0 || prefab.IndexOf("Blackforest", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.BlackForest;
            if (prefab.IndexOf("Meadows", StringComparison.OrdinalIgnoreCase) >= 0) return FeastTheme.Meadows;
            return FeastTheme.None;
        }

        internal static void MarkConsumed(Player player, ItemDrop.ItemData item)
        {
            if (player?.m_customData == null || item?.m_customData == null || item.m_shared == null) return;
            FeastTheme theme = GetTheme(item);
            if (theme == FeastTheme.None) return;
            string foodPrefab = PerkRuntimeService.ItemPrefabName(item);
            if (string.IsNullOrEmpty(foodPrefab)) return;
            string stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            item.m_customData[ConsumedAtKey] = stamp;
            // Player.Save/Load persists Food.m_name as the item prefab, not its
            // localized SharedData.m_name. Those are different identifiers.
            player.m_customData[ActiveFoodKey] = foodPrefab;
            player.m_customData[ActivePrefabKey] = item.m_customData.TryGetValue(Cooking70Service.FeastPrefabKey, out string source) ? source : PerkRuntimeService.ItemPrefabName(item);
            player.m_customData[ActiveStampKey] = stamp;
            player.m_customData[ActiveThemeKey] = theme.ToString();
        }

        private static string GetPersistedFood(Player player)
        {
            if (player?.m_customData == null || !player.m_customData.TryGetValue(ActiveFoodKey, out string saved)) return null;
            if (string.IsNullOrEmpty(saved) || player.m_foods == null) return saved;
            FeastFoodIdentityMigration migration = new FeastFoodIdentityMigration(saved);
            foreach (Player.Food food in player.m_foods)
            {
                if (food != null) migration.Observe(food.m_name, food.m_item?.m_shared?.m_name, food.m_time);
            }
            string resolved = migration.Resolve();
            if (!string.Equals(saved, resolved, StringComparison.Ordinal)) player.m_customData[ActiveFoodKey] = resolved;
            return resolved;
        }

        internal static ItemDrop.ItemData RefreshConsumedFood(Player player, ItemDrop.ItemData item)
        {
            if (player?.m_foods == null || item == null) return item;
            string prefab = PerkRuntimeService.ItemPrefabName(item);
            string previousMasterFood = GetPersistedFood(player);
            foreach (Player.Food food in player.m_foods)
            {
                if (food == null || !string.Equals(food.m_name, prefab, StringComparison.Ordinal)) continue;
                // Vanilla refreshes time/stats when eating the same food again,
                // but keeps its old m_item. Replace that metadata snapshot too.
                // Clone keeps the active meal independent of inventory/prefab data.
                food.m_item = item.Clone();
                if (food.m_name == previousMasterFood &&
                    (!Cooking70Service.IsFeast(food.m_item) || !Cooking35Service.HasCookMastery(player, food.m_item, 70)))
                    ClearPersisted(player);
                return food.m_item;
            }
            return item;
        }

        private static void ClearPersisted(Player player)
        {
            if (player?.m_customData == null) return;
            player.m_customData.Remove(ActiveFoodKey);
            player.m_customData.Remove(ActivePrefabKey);
            player.m_customData.Remove(ActiveStampKey);
            player.m_customData.Remove(ActiveThemeKey);
        }

        internal static bool TryGetActive(Player player, out MasterFeastState result)
        {
            result = null;
            if (player?.m_foods == null) return false;
            string persistedFood = GetPersistedFood(player);
            player.m_customData.TryGetValue(ActiveStampKey, out string persistedStamp);
            player.m_customData.TryGetValue(ActivePrefabKey, out string persistedPrefab);
            player.m_customData.TryGetValue(ActiveThemeKey, out string persistedTheme);
            Player.Food selected = null;
            long bestStamp = long.MinValue;
            foreach (Player.Food food in player.m_foods)
            {
                ItemDrop.ItemData item = food?.m_item;
                if (item == null || food.m_time <= 0f) continue;
                bool restored = !string.IsNullOrEmpty(persistedFood) && food.m_name == persistedFood;
                if (!restored && (!Cooking70Service.IsFeast(item) || !Cooking35Service.HasCookMastery(player, item, 70))) continue;
                long stamp = 0;
                string raw = "";
                item.m_customData?.TryGetValue(ConsumedAtKey, out raw);
                if (restored && string.IsNullOrEmpty(raw)) raw = persistedStamp;
                long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out stamp);
                if (selected == null || stamp >= bestStamp) { selected = food; bestStamp = stamp; }
            }
            if (selected?.m_item == null)
            {
                if (!string.IsNullOrEmpty(persistedFood)) ClearPersisted(player);
                return false;
            }
            bool selectedPersisted = selected.m_name == persistedFood;
            FeastTheme theme = selectedPersisted && Enum.TryParse(persistedTheme, out FeastTheme savedTheme) ? savedTheme : GetTheme(selected.m_item);
            if (theme == FeastTheme.None) return false;
            result = MasteryStateStore.GetPlayerState<MasterFeastState>(player);
            // On relog the state is rebuilt from remaining food time once; it must not slide forward every food tick.
            if (result.ConsumedAt != bestStamp || result.ExpireTime <= Time.time)
                result.ExpireTime = Time.time + Mathf.Max(0f, selected.m_time);
            result.Theme = theme;
            result.FoodItem = selected.m_item;
            result.SourceItemPrefab = selectedPersisted && !string.IsNullOrEmpty(persistedPrefab) ? persistedPrefab : PerkRuntimeService.ItemPrefabName(selected.m_item);
            result.CookId = selected.m_item.m_crafterID != 0L ? new ZDOID(selected.m_item.m_crafterID, 0u) : ZDOID.None;
            result.ConsumedAt = bestStamp;
            return true;
        }

        internal static void ApplyNoDecayFoodStats(Player player)
        {
            if (player?.m_foods == null) return;
            string persistedFood = GetPersistedFood(player);
            foreach (Player.Food food in player.m_foods)
            {
                ItemDrop.ItemData item = food?.m_item;
                if (item?.m_shared == null || food.m_time <= 0f ||
                    !(food.m_name == persistedFood || (Cooking70Service.IsFeast(item) && Cooking35Service.HasCookMastery(player, item, 70)))) continue;
                // Master Feast adds no extra raw values; the obsolete +5 constant is zero.
                food.m_health = item.m_shared.m_food + (item.m_shared.m_food > 0f ? Cooking35Service.PreparedMealStatBonus : 0f);
                food.m_stamina = item.m_shared.m_foodStamina + (item.m_shared.m_foodStamina > 0f ? Cooking35Service.PreparedMealStatBonus : 0f);
                food.m_eitr = item.m_shared.m_foodEitr + (item.m_shared.m_foodEitr > 0f ? Cooking35Service.PreparedMealStatBonus : 0f);
            }
        }

        internal static string GetThemeDisplayName(FeastTheme theme)
        {
            switch (theme)
            {
                case FeastTheme.Meadows: return "Луки";
                case FeastTheme.BlackForest: return "Чорний ліс";
                case FeastTheme.Swamp: return "Болота";
                case FeastTheme.Mountains: return "Гори";
                case FeastTheme.Plains: return "Рівнини";
                case FeastTheme.Mistlands: return "Туманні землі";
                case FeastTheme.Ashlands: return "Попелясті землі";
                case FeastTheme.Oceans: return "Океан";
                case FeastTheme.DeepNorth: return "Глибока Північ";
                default: return "";
            }
        }
        internal static string GetBonusText(FeastTheme theme)
        {
            switch (theme)
            {
                case FeastTheme.Meadows: return "Значення їжі не зменшуються; отримання Rested за 7 с; Rested +5 хв без зміни комфорту.";
                case FeastTheme.BlackForest: return "Значення їжі не зменшуються; дерево й метал -25% ваги; сокири/кайла -25% витривалості.";
                case FeastTheme.Swamp: return "Значення їжі не зменшуються; опір отруті; медовиці +30% тривалості (разом з Cooking35 +70%).";
                case FeastTheme.Mountains: return "Значення їжі не зменшуються; шкода від падіння -25%; числові бонуси медовиць +50%; перезарядка відновлювальних медовиць вдвічі менша. Медовиці опору отруті/морозу/вогню повертають відповідний ефект прямому ворогу: 25% отриманої шкоди, перезарядка 5 с окремо для кожного типу. Інші фіксовані/обмежені ефекти ще без додаткового бонусу.";
                case FeastTheme.Plains: return "Значення їжі не зменшуються; ближній бій -20% витривалості.";
                case FeastTheme.Mistlands: return "Значення їжі не зменшуються; магія -20% витривалості; стрибки +2 м; безпечна висота падіння +2 м.";
                case FeastTheme.Ashlands: return "Значення їжі не зменшуються; поріг стагеру +30%; відкидання -40%.";
                case FeastTheme.Oceans: return "Значення їжі не зменшуються; плавання +20 до навички (до 100); +100% базового досвіду плавання.";
                case FeastTheme.DeepNorth: return "Значення їжі не зменшуються; при падінні HP/витривалості/ейтиру нижче 15% — відновлення 25% максимуму; окремі перезарядки 60 с, потрібне повторне падіння.";
                default: return "Тематичний бонус не визначено.";
            }
        }

        internal static string GetNarrativeBonus(FeastTheme theme)
        {
            switch (theme)
            {
                case FeastTheme.Meadows: return "Домашній затишок швидше повертає сили й довше супроводжує в дорозі.";
                case FeastTheme.BlackForest: return "Лісова витривалість полегшує роботу з деревом і металом.";
                case FeastTheme.Swamp: return "Болотний гарт захищає від отрути й подовжує силу медовиць.";
                case FeastTheme.Mountains: return "Гірський гарт пом’якшує падіння й посилює числові бонуси медовиць.";
                case FeastTheme.Plains: return "Сила рівнин робить ближній бій менш виснажливим.";
                case FeastTheme.Mistlands: return "Туманні землі допомагають берегти силу для чаклунства й стрибати вище.";
                case FeastTheme.Ashlands: return "Гарт попелу допомагає втриматися під натиском і не втратити рівновагу.";
                case FeastTheme.Oceans: return "Морський бенкет допомагає впевненіше плавати й швидше вчитися.";
                case FeastTheme.DeepNorth: return "Друге дихання повертає сили на межі виснаження.";
                default: return "Тематичний ефект цього бенкету невідомий.";
            }
        }

        internal static string GetTooltip(MasterFeastState state)
        {
            if (state == null) return "";
            TimeSpan remaining = TimeSpan.FromSeconds(Mathf.Max(0f, state.ExpireTime - Time.time));
            string source = string.IsNullOrEmpty(state.SourceItemPrefab) ? "невідомий feast" : state.SourceItemPrefab;
            return "Master Feast — " + GetThemeDisplayName(state.Theme) + "\nДжерело: " + source +
                "\nЗалишилось: " + remaining.Minutes.ToString("00") + ":" + remaining.Seconds.ToString("00") +
                "\n" + GetBonusText(state.Theme);
        }
    }
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class MasterFeastPrefabAuditPatch
    {
        private static bool Logged;

        private static void Postfix(ObjectDB __instance)
        {
            if (Logged || __instance?.m_items == null) return;
            Logged = true;
            foreach (GameObject prefab in __instance.m_items)
                if (prefab != null && prefab.name.IndexOf("feast", StringComparison.OrdinalIgnoreCase) >= 0)
                    MasteryPlugin.Log.LogInfo("[MasterFeast] Vanilla prefab: " + prefab.name);
        }
    }

    [HarmonyPatch(typeof(Player), "EatFood")]
    internal static class MasterFeastConsumePatch
    {
        private static void Prefix(ref ItemDrop.ItemData item)
        {
            item = Cooking70FeastServingPatch.GetServing(item);
        }

        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || __instance == null || item == null) return;
            MasteryExtendedEventBus.Publish(new FoodConsumedEvent { Player = __instance, Food = item });
            item = MasterFeastService.RefreshConsumedFood(__instance, item);
            if (!Cooking70Service.IsFeast(item) ||
                !Cooking35Service.HasCookMastery(__instance, item, 70)) return;
            MasterFeastService.MarkConsumed(__instance, item);
            if (__instance == Player.m_localPlayer) PerkVisualService.PlayAtPlayer(__instance, "cooking_70", false);
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateFood")]
    [HarmonyPriority(Priority.Last)]
    internal static class MasterFeastFoodPatch
    {
        private static void Postfix(Player __instance)
        {
            MasterFeastService.ApplyNoDecayFoodStats(__instance);
            if (MasterFeastService.TryGetActive(__instance, out MasterFeastState state) && state.LastFeedbackConsumedAt != state.ConsumedAt)
            {
                state.LastFeedbackConsumedAt = state.ConsumedAt;
                MasterFeastStatusIconService.Show(__instance, state);
            }
            else if (state == null) MasterFeastStatusIconService.Hide(__instance);
        }
    }
}
