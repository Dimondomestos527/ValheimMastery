using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    /// <summary>Normalizes vanilla combat callbacks without moving legacy perk logic yet.</summary>
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.Last)]
    internal static class MasteryHitResolvedDispatchPatch
    {
        private sealed class State
        {
            internal bool WasAlive;
            internal Player Attacker;
        }

        private static void Prefix(Character __instance, HitData hit, out State __state)
        {
            __state = new State
            {
                WasAlive = __instance != null && !__instance.IsDead(),
                Attacker = hit?.GetAttacker() as Player
            };
        }

        private static void Postfix(Character __instance, HitData hit, State __state)
        {
            if (__instance == null || hit == null || __state == null) return;
            PerkHitContext source = PerkRuntimeService.GetHitContext(hit);
            MasteryHitContext context = new MasteryHitContext
            {
                SourcePerk = source?.PerkId ?? "",
                GenerationDepth = source?.GenerationDepth ?? 0,
                IsGenerated = source?.IsPerkGenerated ?? false,
                AllowSelfProc = source?.AllowSelfProc ?? false,
                IgnoreReflect = source?.IgnoreReflect ?? (source != null && !source.AllowOtherPerkProc),
                IgnoreExecution = source?.IgnoreExecution ?? (source?.IsPerkGenerated ?? false),
                IgnoreOverdrawPayload = source?.IgnoreOverdrawPayload ?? (source?.IsPerkGenerated ?? false),
                AllowKnife70InitialProc = source?.AllowKnife70InitialProc ?? true,
                AllowShadowRecursion = source?.AllowShadowRecursion ?? false
            };
            if (__state.WasAlive && __instance.IsDead() && __state.Attacker != null)
                MasteryExtendedEventBus.Publish(new EnemyKilledEvent { Killer = __state.Attacker, Target = __instance, Hit = hit, Skill = hit.m_skill });

            MasteryEventBus.Publish(new HitResolvedEvent
            {
                Attacker = __state.Attacker,
                Target = __instance,
                Hit = hit,
                Skill = hit.m_skill,
                HitPoint = hit.m_point,
                Direction = hit.m_dir,
                IsSecondary = MasteryAttackTagService.Has(hit, MasteryAttackTag.Secondary),
                IsPrimary = !MasteryAttackTagService.Has(hit, MasteryAttackTag.Secondary),
                IsProjectile = hit.m_ranged,
                IsBackstab = hit.m_backstabBonus > 1f,
                TargetDied = __state.WasAlive && __instance.IsDead(),
                Mastery = context
            });
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    [HarmonyPriority(Priority.First)]
    internal static class MasteryAttackStartedDispatchPatch
    {
        private static void Prefix(Attack __instance)
        {
            Player player = __instance?.m_character as Player;
            if (player == null) return;
            MasteryEventBus.Publish(new AttackStartedEvent
            {
                Player = player,
                Weapon = __instance.m_weapon,
                IsSecondary = AttackIntentService.IsSecondary(__instance, player)
            });
        }
    }
}
