using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterFeastThemeService
    {
        internal static FeastTheme GetTheme(Player player)
        {
            return MasterFeastService.TryGetActive(player, out MasterFeastState state) ? state.Theme : FeastTheme.None;
        }

        internal static float GetStaminaMultiplier(Player player)
        {
            FeastTheme theme = GetTheme(player);
            ItemDrop.ItemData weapon = player?.GetCurrentWeapon();
            Skills.SkillType skill = weapon?.m_shared?.m_skillType ?? Skills.SkillType.None;
            if (theme == FeastTheme.BlackForest && (skill == Skills.SkillType.Pickaxes || skill == Skills.SkillType.WoodCutting || skill == Skills.SkillType.Axes)) return 0.75f;
            if (theme == FeastTheme.Plains && IsMeleeSkill(skill)) return 0.80f;
            if (theme == FeastTheme.Mistlands && (skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic)) return 0.80f;
            return 1f;
        }

        private static bool IsMeleeSkill(Skills.SkillType skill)
        {
            return skill == Skills.SkillType.Swords || skill == Skills.SkillType.Axes || skill == Skills.SkillType.Clubs || skill == Skills.SkillType.Knives || skill == Skills.SkillType.Spears || skill == Skills.SkillType.Polearms || skill == Skills.SkillType.Unarmed;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    [HarmonyPriority(Priority.First)]
    internal static class MasterFeastStaminaPatch
    {
        private static void Prefix(Player __instance, ref float v) => v *= MasterFeastThemeService.GetStaminaMultiplier(__instance);
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetStaggerTreshold))]
    internal static class MasterFeastAshlandsStaggerPatch
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            if (__instance is Player player && MasterFeastThemeService.GetTheme(player) == FeastTheme.Ashlands) __result *= 1.30f;
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class MasterFeastMountainFallPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Player player = __instance as Player;
            if (player != null && hit != null && hit.m_hitType == HitData.HitType.Fall && MasterFeastThemeService.GetTheme(player) == FeastTheme.Mountains) hit.ApplyModifier(0.75f);
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class MasterFeastAshlandsKnockbackPatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Player player = __instance as Player;
            if (player != null && hit != null && MasterFeastThemeService.GetTheme(player) == FeastTheme.Ashlands) hit.m_pushForce *= 0.60f;
        }
    }

    internal static class MasterFeastStatusIconService
    {
        private const string MarkerName = "Valheim Mastery Feast";
        internal static void Hide(Player player)
        {
            if (player == Player.m_localPlayer && player?.m_seman != null)
                player.m_seman.RemoveStatusEffect(MarkerName.GetStableHashCode(), true);
        }
        internal static void Show(Player player, MasterFeastState state)
        {
            if (player == null || player != Player.m_localPlayer || player.m_seman == null || state == null) return;
            Hide(player);
            SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>();
            marker.name = MarkerName;
            marker.m_name = "Master Feast — " + MasterFeastService.GetThemeDisplayName(state.Theme);
            marker.m_tooltip = MasterFeastService.GetTooltip(state);
            marker.m_icon = state.FoodItem?.GetIcon() ?? PerkUiIconService.ForPerk("cooking_70", player.m_textIcon);
            marker.m_ttl = Mathf.Max(1f, state.ExpireTime - Time.time);
            marker.m_flashIcon = false;
            player.m_seman.AddStatusEffect(marker, false, 0, marker.m_ttl, 0);
        }
    }
}
