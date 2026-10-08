using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Clubs35CounterblowService
    {
        internal const string PerkId = "clubs_35_counter";
        internal static bool TryApply(Character target, HitData hit)
        {
            Player player = hit?.GetAttacker() as Player;
#if MASTERY_CLUBS35_EXPERIMENT
            return false; // Replaced by mace corpse launch / hammer epicenter.
#else
            if (target == null || hit == null || player == null || hit.m_skill != Skills.SkillType.Clubs || !target.InAttack() || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 35) || !PerkRuntimeService.TryMarkApplied(hit, PerkId)) return false;
            float bonus = MasteryClassificationService.GetCreatureClass(target) == CreatureClass.Boss ? 0.40f : MasteryClassificationService.GetCreatureClass(target) == CreatureClass.Heavy ? 1.25f : 0.75f;
            hit.m_staggerMultiplier *= 1f + bonus;
            PerkRuntimeService.GetHitContext(hit).PerkId = PerkId;
            PerkFeedbackService.Play(player, "clubs_35_counter", target.GetCenterPoint(), false);
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class Clubs35CounterblowPatch
    {
#if MASTERY_CLUBS35_EXPERIMENT
        private static bool Prepare() => false;
#endif
        private sealed class State { internal bool WasStaggering; internal Player Player; internal bool Counter; }
        private static void Prefix(Character __instance, HitData hit, out State __state)
        {
            __state = new State { WasStaggering = __instance != null && __instance.IsStaggering() };
            __state.Counter = Clubs35CounterblowService.TryApply(__instance, hit);
            __state.Player = hit?.GetAttacker() as Player;
        }
        private static void Postfix(Character __instance, State __state)
        {
            if (__state?.Counter == true && !__state.WasStaggering && __instance != null && __instance.IsStaggering())
            {
                PerkRuntimeService.RestoreStamina(__state.Player, 10f);
                PerkFeedbackService.Play(__state.Player, "clubs_35_counter_stagger", __instance.GetCenterPoint(), false);
            }
        }
    }
}
