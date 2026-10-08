using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Mount mitigation lives on the mount's incoming hit, so the rider remains the authority for eligibility.
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class Riding70MountMitigationPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null) return;
            Sadle saddle = __instance.GetComponent<Sadle>();
            Player rider = saddle != null ? Player.GetPlayer(saddle.GetUser()) : null;
            if (!RidingPerkAuthority.Has(rider, 70)) return;

            bool fall = hit.m_hitType == HitData.HitType.Fall;
            float incoming = hit.GetTotalDamage();
            if (fall) hit.ApplyModifier(0.25f);
            hit.m_staggerMultiplier *= 0.25f;
            if ((fall || incoming >= __instance.GetMaxHealth() * 0.10f) && rider != null)
                PerkVisualService.PlayProc(rider, "ride_70", __instance.transform.position, false, false);
        }
    }

    // The bare default Fist prefab has no meaningful ItemDrop prefab identity; custom fist weapons do.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), new[] { typeof(float) })]
    internal static class Fists70BlockPowerPatch
    {
        private static bool Prepare() => false;
        private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            Player player = Player.m_localPlayer;
            if (player == null || __instance == null || player.GetCurrentWeapon() != __instance ||
                __instance.m_shared?.m_skillType != Skills.SkillType.Unarmed ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 70)) return;
            bool bareHands = PerkRuntimeService.IsBareHands(__instance);
            __result *= bareHands ? 5f : 3f;
        }
    }

}
