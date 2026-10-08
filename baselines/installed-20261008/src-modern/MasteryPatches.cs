using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(Character), "Damage")]
    internal static class CharacterDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit) => ExperienceContext.ObserveHit(__instance, hit);
    }

    [HarmonyPatch(typeof(TreeBase), "Damage")]
    internal static class TreeDamagePatch
    {
        private static void Prefix(TreeBase __instance, HitData hit) => ExperienceContext.ObserveHit(__instance, hit);
    }

    [HarmonyPatch(typeof(MineRock), "Damage")]
    internal static class MineRockDamagePatch
    {
        private static void Prefix(MineRock __instance, HitData hit) => ExperienceContext.ObserveHit(__instance, hit);
    }

    [HarmonyPatch(typeof(MineRock5), "Damage")]
    internal static class MineRock5DamagePatch
    {
        private static void Prefix(MineRock5 __instance, HitData hit) => ExperienceContext.ObserveHit(__instance, hit);
    }

    [HarmonyPatch(typeof(Destructible), "Damage")]
    internal static class DestructibleDamagePatch
    {
        private static void Prefix(Destructible __instance, HitData hit) => ExperienceContext.ObserveHit(__instance, hit);
    }

    [HarmonyPatch(typeof(Skills), "RaiseSkill")]
    internal static class RaiseSkillPatch
    {
        private sealed class State { internal float BaseXp; internal float FinalXp; internal float PreviousLevel; }
        private static void Prefix(Skills __instance, Skills.SkillType skillType, ref float factor, out State __state)
        {
            __state = new State { BaseXp = factor, PreviousLevel =
                PerkRuntimeService.GetActualSkillLevel(MasterySkillOwnerResolver.Resolve(__instance), skillType) };
            factor = ExperienceContext.Scale(skillType, factor);
            __state.FinalXp = factor;
        }
        private static void Postfix(Skills __instance, Skills.SkillType skillType, State __state)
        {
            Player player = MasterySkillOwnerResolver.Resolve(__instance);
            if (player != null && __state != null) MasteryExtendedEventBus.Publish(new SkillXpEvent { Player = player, Skill = skillType, BaseXp = __state.BaseXp, FinalXp = __state.FinalXp });
            Milestones.Check(player, __instance, skillType, __state?.PreviousLevel ?? float.NaN);
        }
    }

    [HarmonyPatch(typeof(Player), "OnJump")]
    internal static class JumpExperiencePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == null) return;
            MasteryExtendedEventBus.Publish(new JumpEvent { Player = __instance });
            if (__instance == Player.m_localPlayer && MasteryPlugin.Settings.Enabled.Value && MasteryPlugin.Settings.JumpBaseXp.Value > 0f)
                __instance.RaiseSkill(Skills.SkillType.Jump, MasteryPlugin.Settings.JumpBaseXp.Value);
        }
    }

    // Skills belongs to the actual Player on both client and dedicated server; Player.m_localPlayer is null on a dedicated server.
    internal static class MasterySkillOwnerResolver
    {
        private static readonly System.Reflection.FieldInfo PlayerField = AccessTools.Field(typeof(Skills), "m_player");
        internal static Player Resolve(Skills skills) => skills != null ? PlayerField?.GetValue(skills) as Player : null;
    }

    [HarmonyPatch(typeof(Skills), "LowerAllSkills")]
    internal static class SkillFloorPatch
    {
        private static void Prefix(Skills __instance)
        {
            Player player = MasterySkillOwnerResolver.Resolve(__instance);
            if (player == null)
                return;
            foreach (Skills.Skill skill in __instance.GetSkillList())
            {
                if (skill == null || skill.m_info == null || !PerkCatalog.Contains(skill.m_info.m_skill))
                    continue;
                foreach (PerkDefinition perk in PerkCatalog.Get(skill.m_info.m_skill))
                    if (skill.m_level >= perk.Milestone)
                        PerkStateService.MarkEverUnlocked(player, skill.m_info.m_skill, perk.Milestone);
            }
        }

        private static void Postfix(Skills __instance)
        {
            Player player = MasterySkillOwnerResolver.Resolve(__instance);
            if (player == null)
                return;
            foreach (Skills.Skill skill in __instance.GetSkillList())
            {
                if (skill == null || skill.m_info == null || !PerkCatalog.Contains(skill.m_info.m_skill))
                    continue;
                float floor = PerkStateService.WasEverUnlocked(player, skill.m_info.m_skill, 100) ? 100f :
                    (PerkStateService.WasEverUnlocked(player, skill.m_info.m_skill, 70) ? 70f :
                    (PerkStateService.WasEverUnlocked(player, skill.m_info.m_skill, 35) ? 35f : 0f));
                if (skill.m_level < floor)
                    skill.m_level = floor;
            }
        }
    }

    internal static class Milestones
    {
        internal static void Check(Player player, Skills skills, Skills.SkillType skillType, float previousLevel = float.NaN)
        {
            if (player == null || skills == null || !PerkCatalog.Contains(skillType))
                return;
            float level = PerkRuntimeService.GetActualSkillLevel(player, skillType);
            foreach (PerkDefinition perk in PerkCatalog.Get(skillType))
            {
                int milestone = perk.Milestone;
                if (level < milestone) continue;
                bool crossed = MilestoneCrossingRule.Crossed(previousLevel, level, milestone);
                // Keep first Gold unlock server-acknowledged. Returning to a
                // previously unlocked Gold threshold is a cosmetic milestone,
                // not a second ledger unlock or Favor/exhaustion reset.
                if (skillType == Skills.SkillType.Crafting && milestone == 100 && GoldCraftingService.Enabled)
                {
                    if (crossed && player == Player.m_localPlayer &&
                        PerkStateService.WasEverUnlocked(player, skillType, milestone))
                    {
                        MasteryRavenTutorials.Unlock(player, skillType, milestone);
                        if (MasteryPlugin.Settings.EnableMilestoneVFX.Value || MasteryPlugin.Settings.EnableMilestoneMessages.Value)
                            MasteryMilestonePresentation.Show(player, skillType, milestone, perk, false);
                        GoldAscensionVisual.Play(player, true);
                    }
                    continue;
                }
                bool firstUnlock = PerkStateService.MarkEverUnlocked(player, skillType, milestone);
                if (!firstUnlock && !crossed) continue;
                PerkVisualService.PlayMilestone(player, skillType, milestone, perk, firstUnlock);
            }
        }
    }
}
