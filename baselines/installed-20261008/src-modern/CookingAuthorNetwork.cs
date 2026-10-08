using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class CookingAuthorNetwork
    {
        private static readonly ConditionalWeakTable<ZNetView, Dictionary<long, int>> Claims = new ConditionalWeakTable<ZNetView, Dictionary<long, int>>();
        internal static void Register(ZNetView view)
        {
            if (view?.IsValid() != true) return;
            view.Register<int>("VM_CookingAuthor", (sender, tier) =>
            {
                if (!view.IsOwner() || (tier != 0 && tier != 35 && tier != 70)) return;
                long id = ProcessingStationProgressionService.ResolveSender(sender);
                if (id != 0) Claims.GetOrCreateValue(view)[id] = tier;
            });
        }
        internal static int Tier(ZNetView view, long author)
        {
            if (author == 0 || !MasteryPlugin.Settings.Enabled.Value) return 0;
            if (Claims.GetOrCreateValue(view).TryGetValue(author, out int tier)) return tier;
            var local = Player.m_localPlayer;
            if (local == null || local.GetPlayerID() != author) return 0;
            return PerkRuntimeService.HasPerk(local, Skills.SkillType.Cooking, 70) ? 70 :
                PerkRuntimeService.HasPerk(local, Skills.SkillType.Cooking, 35) ? 35 : 0;
        }
        internal static void StampNew(int start, string prefab, Vector3 point, long author, int tier)
        {
            if (tier < 35 || author == 0) return;
            for (int i = start; i < ItemDrop.s_instances.Count; i++)
            {
                var drop = ItemDrop.s_instances[i];
                if (drop?.m_itemData == null || TierDatabase.ItemKey(drop.m_itemData) != GatheringProgressionService.Normalize(prefab) ||
                    (drop.transform.position - point).sqrMagnitude > 16f) continue;
                drop.m_itemData.m_customData[Cooking35Service.CookMasteryKey] = tier.ToString();
                drop.m_itemData.m_crafterID = author;
                var cook = Player.GetPlayer(author);
                if (cook != null) drop.m_itemData.m_crafterName = cook.GetPlayerName();
                drop.Save();
            }
        }
    }
    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class FermenterAuthorRpcPatch
    { private static void Postfix(Fermenter __instance) => CookingAuthorNetwork.Register(__instance.m_nview); }
    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class CookingStationAuthorRpcPatch
    { private static void Postfix(CookingStation __instance) => CookingAuthorNetwork.Register(__instance.m_nview); }
    [HarmonyPatch(typeof(Fermenter), "AddItem")]
    internal static class FermenterAuthorClaimPatch
    {
        private static void Prefix(Fermenter __instance, Humanoid user)
        {
            if (!(user is Player cook) || cook != Player.m_localPlayer || __instance.m_nview?.IsValid() != true) return;
            int tier = PerkRuntimeService.HasPerk(cook, Skills.SkillType.Cooking, 70) ? 70 : PerkRuntimeService.HasPerk(cook, Skills.SkillType.Cooking, 35) ? 35 : 0;
            __instance.m_nview.InvokeRPC("VM_CookingAuthor", tier);
        }
    }
    internal sealed class CookingSlotAuthorState { internal int Slot; internal long Author; internal string Input; }
    [HarmonyPatch(typeof(CookingStation), "RPC_AddItem")]
    internal static class CookingSlotAuthorPatch
    {
        private static void Prefix(CookingStation __instance, long sender, string itemName, out CookingSlotAuthorState __state)
        {
            __state = null;
            if (__instance.m_nview?.IsOwner() != true || !__instance.IsItemAllowed(itemName)) return;
            int slot = __instance.GetFreeSlot();
            if (slot >= 0) __state = new CookingSlotAuthorState { Slot = slot, Author = ProcessingStationProgressionService.ResolveSender(sender), Input = itemName };
        }
        private static void Postfix(CookingStation __instance, CookingSlotAuthorState __state)
        {
            if (__state == null) return;
            __instance.GetSlot(__state.Slot, out string item, out float elapsed, out CookingStation.Status status, out bool cheated);
            if (item != __state.Input) return;
            var zdo = __instance.m_nview.GetZDO();
            zdo.Set("vm.cook.slot.author." + __state.Slot, __state.Author);
            zdo.Set("vm.cook.slot.tier." + __state.Slot, CookingAuthorNetwork.Tier(__instance.m_nview, __state.Author));
        }
    }
    internal static class CookingFoodPersistence
    {
        private const string Prefix = "vm.cook.eaten35.";
        internal static bool Has(Player player, string prefab) => !string.IsNullOrEmpty(prefab) &&
            player.m_customData.TryGetValue(Prefix + prefab, out string value) && value == "1";
        internal static void Record(Player player, ItemDrop.ItemData item)
        {
            if (player != Player.m_localPlayer || !Cooking35Service.IsPreparedMeal(item)) return;
            foreach (var food in player.m_foods)
            {
                if (food?.m_item?.m_shared?.m_name != item.m_shared.m_name || string.IsNullOrEmpty(food.m_name)) continue;
                string key = Prefix + food.m_name;
                if (Cooking35Service.HasCookMastery(player, item, 35)) player.m_customData[key] = "1";
                else player.m_customData.Remove(key);
            }
        }
    }
}
