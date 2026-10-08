using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Axe70TargetState
    {
        internal float NextRollAt;
        internal float ReadyUntil;
        internal bool RollScheduled;
    }

    internal static class Axes70Service
    {
        private const float ExecutionRollInterval = 2.5f;
        private const float ReadyDuration = 7f;
        private const float ExecutionChance = 0.25f;
        private static readonly ConditionalWeakTable<Character, Axe70TargetState> Targets = new ConditionalWeakTable<Character, Axe70TargetState>();
        private static readonly ConditionalWeakTable<Player, Axe70SpecialState> SpecialAttacks = new ConditionalWeakTable<Player, Axe70SpecialState>();
        private static float NextTargetScanAt;

        private sealed class Axe70SpecialState { internal float Until; internal float CooldownUntil; internal float SlashBuffUntil; }

        internal static void MarkSpecialAttack(Player player)
        {
            if (player != null) SpecialAttacks.GetOrCreateValue(player).Until = Time.time + 2f;
        }

        internal static bool IsSpecialImpact(Player player) => player != null && SpecialAttacks.GetOrCreateValue(player).Until >= Time.time;

        internal static void Tick()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Time.time < NextTargetScanAt) return;
            NextTargetScanAt = Time.time + 0.25f;
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target.IsDead()) continue;
                if (!Axe35Service.TryGet(target, out TargetEffect chop) || chop.Stacks <= 0)
                {
                    if (Targets.TryGetValue(target, out Axe70TargetState stale))
                    {
                        stale.RollScheduled = false;
                        stale.ReadyUntil = 0f;
                    }
                    continue;
                }
                Player player = ZNetScene.instance?.FindInstance(chop.SourcePlayer)?.GetComponent<Player>();
                if (player == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 70)) continue;
                TryRollExecutionReady(target, player, chop);
            }
        }

        private static void TryRollExecutionReady(Character target, Player player, TargetEffect chop)
        {
            Axe70SpecialState playerState = SpecialAttacks.GetOrCreateValue(player);
            if (playerState.CooldownUntil > Time.time) return;

            Axe70TargetState state = Targets.GetOrCreateValue(target);
            if (state.ReadyUntil > Time.time) return;
            if (state.ReadyUntil > 0f)
            {
                state.ReadyUntil = 0f;
                state.NextRollAt = Time.time + ExecutionRollInterval;
                return;
            }
            if (!state.RollScheduled)
            {
                state.RollScheduled = true;
                state.NextRollAt = Time.time + ExecutionRollInterval;
                return;
            }
            int stacks = Mathf.Clamp(chop.Stacks, 1, Axe35Service.MaxStacks);
            CreatureClass creatureClass = MasteryClassificationService.GetCreatureClass(target);
            float max = Mathf.Max(1f, target.GetMaxHealth());
            float threshold = target.IsBoss() ? 0.20f : (max >= 1000f ? 0.40f : 0.50f);
            bool smallWithGouge = creatureClass == CreatureClass.SmallNormal;
            if ((!smallWithGouge && target.GetHealth() / max > threshold) || Time.time < state.NextRollAt) return;
            state.NextRollAt = Time.time + ExecutionRollInterval;
            float chance = ExecutionChance * (stacks / (float)Axe35Service.MaxStacks);
            if (creatureClass == CreatureClass.Heavy || creatureClass == CreatureClass.SmallNormal) chance *= 2f;
            if (target.IsBoss()) chance *= 0.50f;
            if (!PerkRuntimeService.RollChance(Mathf.Clamp01(chance))) return;
            state.ReadyUntil = Time.time + ReadyDuration;
            AxeTargetVisualService.SendExecutionReady(player, target, ReadyDuration);
        }
        internal static bool TryConsumeExecution(Character target, Player player, HitData hit)
        {
            if (target == null || player == null || hit == null || target.IsDead() || !Axe35Service.IsAxeHit(player, hit) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 70) || !MasteryAttackTagService.Has(hit, MasteryAttackTag.Secondary) ||
                !Targets.TryGetValue(target, out Axe70TargetState state) || state.ReadyUntil < Time.time)
                return false;

            float victimHealth = Mathf.Max(0f, target.GetHealth());
            state.ReadyUntil = 0f;
            state.RollScheduled = true;
            state.NextRollAt = Time.time + ExecutionRollInterval;
            Axe70SpecialState playerState = SpecialAttacks.GetOrCreateValue(player);
            playerState.CooldownUntil = Time.time + 15f;
            playerState.SlashBuffUntil = Time.time + 10f;
            player.Heal(victimHealth * 0.25f, true);
            // This hook runs at Character.ApplyDamage, after vanilla resistance and armor.
            // Account for the difficulty/world damage scale applied inside that method.
            float worldScale = Game.instance != null
                ? Game.instance.GetDifficultyDamageScaleEnemy(target.transform.position) * Game.m_playerDamageRate : 1f;
            hit.m_damage = new HitData.DamageTypes
            { m_slash = (victimHealth + 0.01f) / Mathf.Max(0.001f, worldScale) };
            AxeTargetVisualService.SendExecution(player, target);
            return true;
        }

        internal static void ApplySlashBuff(Player player, HitData hit)
        {
            if (player != null && hit != null && SpecialAttacks.TryGetValue(player, out Axe70SpecialState state) && state.SlashBuffUntil >= Time.time)
                hit.m_damage.m_slash *= 1.25f;
        }
    }
    internal static class Clubs70Service
    {
        internal static bool TryShockwave(Player player, Character origin, HitData landedHit)
        {
#if MASTERY_CLUBS70_EXPERIMENT
            return false; // Replaced by committed stamina-reservation charge.
#else
            if (player == null || origin == null || landedHit == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70) ||
                !MasteryAttackTagService.Has(landedHit, MasteryAttackTag.Secondary))
                return false;
            bool criticalHit = false;
            Vector3 originPoint = origin.GetCenterPoint();
            HitData.DamageTypes baseDamage = landedHit.m_damage;
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target == origin || target.IsDead() || target.IsPlayer() || !BaseAI.IsEnemy(player, target) ||
                    (target.GetCenterPoint() - originPoint).sqrMagnitude > 16f)
                    continue;

                bool staggered = target.IsStaggering();
                HitData aoe = new HitData();
                aoe.m_skill = Skills.SkillType.Clubs;
                aoe.m_point = target.GetCenterPoint();
                aoe.m_dir = (target.GetCenterPoint() - originPoint).normalized;
                aoe.m_damage = baseDamage;
                aoe.m_staggerMultiplier = 3f;
                if (staggered) aoe.m_damage.Modify(2f);
                aoe.SetAttacker(player);
                PerkHitContext context = PerkRuntimeService.GetHitContext(aoe);
                context.IsPerkGenerated = true; context.PerkId = "clubs_70"; context.AllowSelfProc = false;
                target.Damage(aoe);
                if (!staggered) continue;
                criticalHit = true;
                target.ApplyPushback(aoe.m_dir, 20f);
                PerkVisualService.PlayAtWorldPosition(player, "clubs_70_crit", target.GetCenterPoint(), false);
            }
            PerkVisualService.PlayAtWorldPosition(player, criticalHit ? "clubs_70_crit" : "clubs_70", originPoint, true);
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class AxesAndClubs70SpecialAttackPatch
    {
        private static void Prefix(Attack __instance)
        {
            Player player = __instance?.m_character as Player;
            ItemDrop.ItemData weapon = __instance?.m_weapon;
            if (player == null || weapon?.m_shared == null || !AttackIntentService.IsSecondary(__instance, player)) return;
            if (weapon.m_shared.m_skillType == Skills.SkillType.Clubs && PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70))
            {
#if !MASTERY_CLUBS70_EXPERIMENT
                PerkVisualService.PlayAtPlayer(player, "clubs_70_windup", false);
#endif
                return;
            }
            if (weapon.m_shared.m_skillType != Skills.SkillType.Axes || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 70)) return;
            // Geometry is handled once by AxeSkillGeometryPatch; do not multiply it again here.
            Axes70Service.MarkSpecialAttack(player);
            PerkFeedbackService.Play(player, "axes_70_windup", player.GetCenterPoint(), false);
        }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class AxesAndClubs70AreaAttackPatch
    {
        private static void Prefix(Attack __instance)
        {
            Player player = __instance?.m_character as Player;
            ItemDrop.ItemData weapon = __instance?.m_weapon;
            if (player == null || weapon?.m_shared == null || !AttackIntentService.IsSecondary(__instance, player)) return;

            if (weapon.m_shared.m_skillType == Skills.SkillType.Clubs && PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70))
            {
#if !MASTERY_CLUBS70_EXPERIMENT
                PerkVisualService.PlayAtPlayer(player, "clubs_70_windup", false);
#endif
                return;
            }

            if (weapon.m_shared.m_skillType != Skills.SkillType.Axes || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 70)) return;
            // DoAreaAttack uses attackRange as a forward center offset. Never scale it here.
            Axes70Service.MarkSpecialAttack(player);
        }

    }


    // Animation timing is initialized in Attack.Start, before DoMeleeAttack is reached.
    // Changing it at hit time was too late and made the Axe 70 speed bonus invisible.
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    [HarmonyPriority(Priority.Last)]
    internal static class Axes70SpecialStartSpeedPatch
    {
        private static bool Prepare() => false;
        private static void Prefix(Attack __instance, Humanoid character)
        {
            Player player = character as Player;
            if (player != null && __instance?.m_weapon?.m_shared?.m_skillType == Skills.SkillType.Axes &&
                AttackIntentService.IsSecondary(__instance, player) && PerkRuntimeService.HasPerk(player, Skills.SkillType.Axes, 70))
                __instance.m_speedFactor *= 1.50f;
        }
    }
    internal sealed class Elemental70State { internal int Element; internal float Until; }
    internal static class Elemental70Service
    {
        private static readonly ConditionalWeakTable<Character, Elemental70State> States = new ConditionalWeakTable<Character, Elemental70State>();
        internal static int GetElement(HitData.DamageTypes d)
        {
            float best = 0f; int element = 0;
            if (d.m_fire > best) { best = d.m_fire; element = 1; }
            if (d.m_frost > best) { best = d.m_frost; element = 2; }
            if (d.m_lightning > best) { best = d.m_lightning; element = 3; }
            if (d.m_poison > best) { best = d.m_poison; element = 4; }
            return element;
        }
        internal static void Observe(Character target, HitData hit)
        {
            if (target == null || hit == null || PerkRuntimeService.IsPerkGenerated(hit) || hit.m_skill != Skills.SkillType.ElementalMagic) return;
            Player player = hit.GetAttacker() as Player;
            int current = GetElement(hit.m_damage);
            if (current == 0 || !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 70)) return;
            Elemental70State state = States.GetOrCreateValue(target);
            bool resonance = state.Element != 0 && state.Element != current && state.Until >= Time.time;
            int previous = state.Element; state.Element = current; state.Until = Time.time + 5f;
            if (!resonance) return;
            HitData burst = new HitData(); burst.m_skill = Skills.SkillType.ElementalMagic; burst.m_point = target.GetCenterPoint(); burst.m_dir = hit.m_dir;
            float value = hit.GetTotalDamage() * 0.25f;
            ApplyElement(ref burst.m_damage, previous, value); ApplyElement(ref burst.m_damage, current, value); burst.SetAttacker(player);
            PerkHitContext context = PerkRuntimeService.GetHitContext(burst); context.IsPerkGenerated = true; context.PerkId = "elementalmagic_70"; context.AllowSelfProc = false;
            target.Damage(burst); PerkVisualService.PlayElementalResonance(player, previous, current, burst.m_point);
        }
        private static void ApplyElement(ref HitData.DamageTypes d, int element, float value)
        {
            if (element == 1) d.m_fire += value; else if (element == 2) d.m_frost += value; else if (element == 3) d.m_lightning += value; else if (element == 4) d.m_poison += value;
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class Axes70ExecutionDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (ZNet.instance != null && !ZNet.instance.IsServer()) return;
            Player attacker = hit?.GetAttacker() as Player;
            Axes70Service.ApplySlashBuff(attacker, hit);
            Axes70Service.TryConsumeExecution(__instance, attacker, hit);
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.Last)]
    internal static class Level70CombatResolutionPatch
    {
#if MASTERY_CLUBS70_EXPERIMENT
        private static bool Prepare() => false;
#endif
        private static void Postfix(Character __instance, HitData hit)
        {
            if (__instance == null || hit == null || PerkRuntimeService.IsPerkGenerated(hit)) return;
            // The old cross-element resonance is retired for staff-specific level-70 spells.
            Player player = hit.GetAttacker() as Player;
            if (hit.m_skill == Skills.SkillType.Clubs && player != null)
#if !MASTERY_CLUBS70_EXPERIMENT
                Clubs70Service.TryShockwave(player, __instance, hit);
#else
                return;
#endif
        }
    }
}
