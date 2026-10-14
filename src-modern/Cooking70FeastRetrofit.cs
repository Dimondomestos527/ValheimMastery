using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

namespace ValheimMastery
{
    // Metadata provider only. An authenticated, world-scoped owner migration must
    // explicitly grant an existing item; merely loading/hovering never grants it.
    internal static class Cooking70FeastRetrofit
    {
        internal const string GrantKey = "vm.cooking.feast70.retrofit_v1";
        internal const string BirthKey = "vm.cooking.feast.birth_v1";
        internal const string Epoch = "20261013-v1";
        internal static bool ExactPrefab(string prefab, bool material = false)
        {
            if (prefab == null) return false;
            if (material && prefab.EndsWith("_Material", StringComparison.Ordinal))
                prefab = prefab.Substring(0, prefab.Length - 9);
            switch (prefab)
            {
                case "FeastMeadows": case "FeastBlackforest": case "FeastSwamps":
                case "FeastMountains": case "FeastPlains": case "FeastMistlands":
                case "FeastAshlands": case "FeastOceans": case "FeastDeepNorth": return true;
                default: return false;
            }
        }
        internal static bool ExactItem(ItemDrop.ItemData item) =>
            item?.m_shared != null && ExactPrefab(TierDatabase.ItemKey(item), true);
        internal static bool ValidGrant(string value)
        {
            if (value == null || value.Length > 64) return false;
            string[] fields = value.Split(':');
            return fields.Length == 3 && fields[0] == "1" && fields[2] == Epoch &&
                long.TryParse(fields[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long world) &&
                world != 0 && fields[1] == world.ToString(CultureInfo.InvariantCulture);
        }
        internal static bool HasGrant(ItemDrop.ItemData item) => ExactItem(item) &&
            item.m_customData != null && item.m_customData.TryGetValue(GrantKey, out string value) && ValidGrant(value);
        internal static bool ExactPlaced(ZDO data) => data != null && ExactPrefab(
            data.GetString(Cooking70Service.FeastPrefabKey, "")) &&
            data.GetPrefab() == data.GetString(Cooking70Service.FeastPrefabKey, "").GetStableHashCode();
        internal static bool HasGrant(ZDO data) => ExactPlaced(data) && ValidGrant(data.GetString(GrantKey, ""));
        internal static void MarkBorn(ItemDrop.ItemData item)
        {
            if (ExactItem(item)) item.m_customData[BirthKey] = "1";
        }
        // Build on a private copy and validate the actual existing receipt bounds.
        // The caller must revalidate authority/CAS/quiescence before assigning it.
        internal static bool TryPrepare(ItemDrop.ItemData item, long world, out Dictionary<string,string> data)
        {
            data = null;
            if (world == 0 || !ExactItem(item) || item.m_customData == null ||
                item.m_customData.ContainsKey(BirthKey) || item.m_customData.ContainsKey(GrantKey)) return false;
            var record = CookingProductRecord.Capture(item);
            record.Data[GrantKey] = "1:" + world.ToString(CultureInfo.InvariantCulture) + ":" + Epoch;
            try { record.Encode(); }
            catch (InvalidOperationException) { return false; }
            data = record.Data;
            return true;
        }
        internal static void TransferPaid(ZDO target, CookingProductRecord source)
        {
            if (!ExactPlaced(target) || source?.Data == null || !ExactPrefab(source.Prefab, true)) return;
            if (source.Data.TryGetValue(GrantKey, out string value) && ValidGrant(value)) target.Set(GrantKey, value);
        }
        internal static void CopyServing(ZDO source, ItemDrop.ItemData serving)
        {
            if (HasGrant(source) && ExactItem(serving)) serving.m_customData[GrantKey] = source.GetString(GrantKey, "");
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
    internal static class Cooking70FeastBirthPatch
    {
        private static void Prefix(Piece __instance)
        {
            var feast = __instance?.GetComponent<Feast>();
            var view = feast?.GetComponent<ZNetView>();
            if (view?.IsOwner() != true || view.GetZDO() == null) return;
            string prefab = feast.gameObject.name.Replace("(Clone)", "");
            if (!Cooking70FeastRetrofit.ExactPrefab(prefab) || view.GetZDO().GetPrefab() != prefab.GetStableHashCode()) return;
            view.GetZDO().Set(Cooking70FeastRetrofit.BirthKey, "1");
        }
    }
}
