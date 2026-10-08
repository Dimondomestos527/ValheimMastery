using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Cooking70Service
    {
        internal const string FeastMarkerKey = "valheim_mastery.is_feast";
        internal const string FeastTierZdoKey = "valheim_mastery.feast_cooking_tier";
        internal const string FeastPrefabKey = "valheim_mastery.feast_prefab";
        internal const string CookMasteryKey = "valheim_mastery.cooking_tier";
        internal const string AuthorNameKey = "valheim_mastery.feast_author";

        internal static void EnsurePlacedRecord(Feast feast)
        {
            if (feast == null) return;
            // SetCreator runs before Feast.Start initializes m_nview.
            ZNetView view = feast.m_nview != null ? feast.m_nview : feast.GetComponent<ZNetView>();
            ZDO zdo = view?.GetZDO();
            if (zdo == null || !view.IsOwner()) return;
            if (string.IsNullOrEmpty(zdo.GetString(FeastPrefabKey, "")))
                zdo.Set(FeastPrefabKey, feast.gameObject.name.Replace("(Clone)", ""));
            if (zdo.GetInt(FeastTierZdoKey, 0) >= 70) return;
            Player author = Player.m_localPlayer;
            long creator = feast.GetComponent<Piece>()?.GetCreator() ?? 0L;
            // A remote replica does not contain that player's actual skills.
            // Only the owner/author stamps the persistent record; peers read it.
            if (author == null || creator == 0L || creator != author.GetPlayerID()) return;
            int tier = PerkRuntimeService.HasPerk(author, Skills.SkillType.Cooking, 70) ? 70 :
                PerkRuntimeService.HasPerk(author, Skills.SkillType.Cooking, 35) ? 35 : 0;
            if (zdo.GetInt(FeastTierZdoKey, 0) == tier && zdo.GetString(AuthorNameKey, "") == author.GetPlayerName()) return;
            zdo.Set(FeastTierZdoKey, tier);
            zdo.Set(AuthorNameKey, author.GetPlayerName());
        }

        internal static bool IsFeast(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue(FeastMarkerKey, out string marked) && marked == "1") return true;
            string key = item?.m_dropPrefab != null ? item.m_dropPrefab.name : item?.m_shared?.m_name;
            return !string.IsNullOrEmpty(key) && key.IndexOf("Feast", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Every later biome is strictly stronger in total bonus than the previous one.
        internal static Vector3 GetFeastBonus(ItemDrop.ItemData item)
        {
            string key = item?.m_dropPrefab != null ? item.m_dropPrefab.name : item?.m_shared?.m_name ?? "";
            if (item?.m_customData != null && item.m_customData.TryGetValue(FeastPrefabKey, out string placedPrefab)) key += " " + placedPrefab;
            if (key.IndexOf("DeepNorth", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(45f, 35f, 25f);
            if (key.IndexOf("Ashlands", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(40f, 30f, 20f);
            if (key.IndexOf("Mistlands", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(30f, 25f, 20f);
            if (key.IndexOf("Plains", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(30f, 25f, 5f);
            if (key.IndexOf("Mountain", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(25f, 20f, 5f);
            if (key.IndexOf("Swamp", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(20f, 15f, 5f);
            if (key.IndexOf("Blackforest", StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf("BlackForest", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(15f, 15f, 0f);
            if (key.IndexOf("Meadows", StringComparison.OrdinalIgnoreCase) >= 0) return new Vector3(10f, 10f, 0f);
            return new Vector3(20f, 20f, 10f);
        }

    }

    [HarmonyPatch(typeof(Feast), "Start")]
    internal static class Cooking70PlacedFeastRecordPatch
    {
        private static void Postfix(Feast __instance) => Cooking70Service.EnsurePlacedRecord(__instance);
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class Cooking70PlacedFeastAuthorPatch
    {
        private static void Postfix(Piece __instance) =>
            Cooking70Service.EnsurePlacedRecord(__instance.GetComponent<Feast>());
    }

    [HarmonyPatch(typeof(Feast), nameof(Feast.GetHoverText))]
    internal static class Cooking70PlacedFeastHoverPatch
    {
        private static void Postfix(Feast __instance, ref string __result)
        {
            if (string.IsNullOrEmpty(__result) || !__instance.InUseDistance(Player.m_localPlayer)) return;
            Cooking70Service.EnsurePlacedRecord(__instance);
            ZDO zdo = __instance?.m_nview?.GetZDO();
            if (zdo == null) return;
            if (zdo.GetInt(Cooking70Service.FeastTierZdoKey, 0) < 70) return;

            FeastTheme theme = MasterFeastService.GetThemeForPrefab(zdo.GetString(Cooking70Service.FeastPrefabKey, ""));
            string authorName = zdo.GetString(Cooking70Service.AuthorNameKey, "");
            if (theme == FeastTheme.None)
            {
                __result += "\n<color=#69b9d9>Майстерно приготований бенкет</color>\nЗберігає повну силу до останньої миті ситості.";
                if (!string.IsNullOrEmpty(authorName)) __result += "\nКухар: " + authorName;
                return;
            }
            __result += "\n<color=#69b9d9>Майстерно приготований бенкет — " + MasterFeastService.GetThemeDisplayName(theme) +
                "</color>\n" + MasterFeastService.GetNarrativeBonus(theme);
            if (!string.IsNullOrEmpty(authorName)) __result += "\nКухар: " + authorName;
        }
    }

    [HarmonyPatch(typeof(Feast), "RPC_EatConfirmation")]
    internal static class Cooking70FeastServingPatch
    {
        internal sealed class ServingContext
        {
            internal ServingContext Previous;
            internal ItemDrop.ItemData Template;
            internal ItemDrop.ItemData Serving;
            internal bool Restored;
        }

        [ThreadStatic] private static ServingContext Active;

        internal static ItemDrop.ItemData GetServing(ItemDrop.ItemData item)
        {
            return Active != null && ReferenceEquals(Active.Template, item) ? Active.Serving : item;
        }

        private static void Prefix(Feast __instance, out ServingContext __state)
        {
            Cooking70Service.EnsurePlacedRecord(__instance);
            ItemDrop.ItemData template = __instance?.m_foodItem?.m_itemData;
            __state = new ServingContext { Previous = Active, Template = template };
            Active = __state;
            if (template == null) return;
            // Feast shares its food prefab with other placed feasts. Keep all author,
            // theme and consumption metadata on this serving, never on that prefab.
            ItemDrop.ItemData item = template.Clone();
            __state.Serving = item;
            item.m_customData[Cooking70Service.FeastMarkerKey] = "1";
            item.m_customData.Remove(MasterFeastService.ConsumedAtKey);
            string feastPrefab = __instance.m_nview?.GetZDO()?.GetString(Cooking70Service.FeastPrefabKey, "") ?? "";
            if (!string.IsNullOrEmpty(feastPrefab)) item.m_customData[Cooking70Service.FeastPrefabKey] = feastPrefab;
            else item.m_customData.Remove(Cooking70Service.FeastPrefabKey);
            int tier = __instance.m_nview?.GetZDO()?.GetInt(Cooking70Service.FeastTierZdoKey, 0) ?? 0;
            item.m_crafterID = __instance.GetComponent<Piece>()?.GetCreator() ?? 0L;
            if (tier >= 35) item.m_customData[Cooking70Service.CookMasteryKey] = tier.ToString();
            else item.m_customData.Remove(Cooking70Service.CookMasteryKey);
        }

        private static void Postfix(ServingContext __state) => Restore(__state);
        private static Exception Finalizer(ServingContext __state, Exception __exception)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(ServingContext state)
        {
            if (state == null || state.Restored) return;
            Active = state.Previous;
            state.Restored = true;
        }
    }

}
