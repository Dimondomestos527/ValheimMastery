using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Fists35AttackSpeedService
    {
        private sealed class ActiveAttack { internal Attack Attack; internal float Speed; internal float Started; }
        private static readonly ConditionalWeakTable<CharacterAnimEvent, ActiveAttack> Active =
            new ConditionalWeakTable<CharacterAnimEvent, ActiveAttack>();

        internal static void Track(CharacterAnimEvent animEvent, Attack attack, float speed)
        {
            if (animEvent == null) return;
            if (attack == null || speed <= 1f) { Clear(animEvent); return; }
            ActiveAttack state = Active.GetOrCreateValue(animEvent);
            state.Attack = attack;
            state.Speed = speed;
            state.Started = Time.time;
            // Native clip events supply the phase's speed; never replace it here.
        }

        internal static void Clear(CharacterAnimEvent animEvent)
        {
            if (animEvent == null || !Active.TryGetValue(animEvent, out ActiveAttack state)) return;
            Player player = animEvent.m_character as Player;
            bool canReset = player == null || player.m_currentAttack == null || player.m_currentAttack == state.Attack;
            Active.Remove(animEvent);
            if (!canReset) return;
            // FreezeFrame restores m_pauseSpeed itself; preserve the native hit pause.
            if (animEvent.m_pauseTimer > 0f) animEvent.m_pauseSpeed = 1f;
            else animEvent.Speed(1f);
        }

        internal static void ScaleEvent(CharacterAnimEvent animEvent, ref float speed)
        {
            if (animEvent == null || !Active.TryGetValue(animEvent, out ActiveAttack state)) return;
            Player player = animEvent.m_character as Player;
            if (player == null || player.m_currentAttack != state.Attack ||
                player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Unarmed) return;
            speed *= state.Speed;
        }

        internal static void Tick(CharacterAnimEvent animEvent)
        {
            if (animEvent == null || !Active.TryGetValue(animEvent, out ActiveAttack state)) return;
            Player player = animEvent.m_character as Player;
            if (player == null || (!player.InAttack() && Time.time - state.Started > 0.35f) || player.m_currentAttack != state.Attack ||
                player.GetCurrentWeapon()?.m_shared?.m_skillType != Skills.SkillType.Unarmed)
            { Clear(animEvent); return; }
            // Clip Speed events and FreezeFrame retain control over current motion.
        }
    }

    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
    internal static class Fists35AttackSpeedKeepPatch
    {
        private static void Postfix(CharacterAnimEvent __instance) => Fists35AttackSpeedService.Tick(__instance);
    }

    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
    internal static class Fists35NativeSpeedEventPatch
    {
        private static void Prefix(CharacterAnimEvent __instance, ref float speedScale) =>
            Fists35AttackSpeedService.ScaleEvent(__instance, ref speedScale);
    }

    /// <summary>
    /// Fists 35: successive fist attacks ramp attack speed. Adrenaline gain belongs to the
    /// Unarmed skill passive independently of this perk. This deliberately lives in a separate
    /// patch unit so Codex can validate the Attack.Start signature against the local 1.0 assemblies.
    /// </summary>
    [HarmonyPatch(typeof(Attack), "Start")]
    [HarmonyPriority(Priority.Last)]
    internal static class Fists35AttackSpeedPatch
    {
        private static void Postfix(Attack __instance, bool __result, Humanoid character, CharacterAnimEvent animEvent, ItemDrop.ItemData weapon)
        {
            Player player = character as Player;
            if (!__result || player == null || player != Player.m_localPlayer || animEvent == null)
                return;

            PerkPlayerTransientState state = PerkTransientStateService.For(player);
            bool isUnarmed = weapon?.m_shared?.m_skillType == Skills.SkillType.Unarmed;
            bool isFistAttack = isUnarmed && PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 35);

            if (!isUnarmed)
            {
                Fists35AttackSpeedService.Clear(animEvent);
                if (state.FistComboStacks > 0 || state.FistAttackSerial > 0)
                    NetworkSync.SendClientAbility("fists35_reset");
                state.FistComboStacks = 0;
                state.FistAttackSerial = 0;
                state.LastFistHitSerial = 0;
                return;
            }

            if (!isFistAttack)
            {
                Fists35AttackSpeedService.Track(animEvent, __instance,
                    PerkRuntimeService.IsBareHands(weapon)
                        ? 1f + 0.002f * Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Unarmed), 0f, 100f)
                        : 1f);
                return;
            }

            // Hitboxes are unreliable; only the rhythm timeout ends the combo.
            if (Time.time - state.LastFistHitAt > 3f)
                state.FistComboStacks = 0;

            state.FistAttackSerial++;
            if (ZNet.instance != null && !ZNet.instance.IsServer())
                NetworkSync.SendClientAbility("fists35_start");
            float perStack = PerkRuntimeService.IsBareHands(weapon) ? 0.15f : 0.10f;
            float speedBonus = perStack * Mathf.Clamp(state.FistComboStacks, 0, 3);
            float bareHandPassive = PerkRuntimeService.IsBareHands(weapon)
                ? 0.002f * Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Unarmed), 0f, 100f)
                : 0f;
            float speed = 1f + bareHandPassive + speedBonus;
            Fists35AttackSpeedService.Track(animEvent, __instance, speed);
            if (speedBonus > 0f || bareHandPassive > 0f)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[FistsSpeed] bare=" + PerkRuntimeService.IsBareHands(weapon) +
                        " stacks=" + state.FistComboStacks + " multiplier=" + speed.ToString("0.00"));
            }
        }

        internal static void RecordServerStart(Player player)
        {
            if (player == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 35)) return;
            PerkPlayerTransientState state = PerkTransientStateService.For(player);
            if (Time.time - state.LastFistHitAt > 3f)
                state.FistComboStacks = 0;
            state.FistAttackSerial++;
        }

        internal static void RecordServerReset(Player player)
        {
            if (player == null) return;
            PerkPlayerTransientState state = PerkTransientStateService.For(player);
            state.FistComboStacks = 0;
            state.FistAttackSerial = 0;
            state.LastFistHitSerial = 0;
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class Fists35SuccessfulHitPatch
    {
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance != null ? __instance.GetHealth() : 0f;
        }

        private static void Postfix(Character __instance, HitData hit, float __state)
        {
            if (__instance == null || hit == null || __instance.GetHealth() >= __state - 0.001f)
                return;

            Player attacker = hit.GetAttacker() as Player;
            if (attacker == null || PerkRuntimeService.IsPerkGenerated(hit) || hit.m_ranged ||
                !PerkRuntimeService.HasPerk(attacker, Skills.SkillType.Unarmed, 35))
                return;

            // Only the target owner confirms health loss. Other peers cannot author stacks.
            if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;

            PerkPlayerTransientState state = PerkTransientStateService.For(attacker);
            if (hit.m_skill == Skills.SkillType.Unarmed)
            {
                // Keep the established local-owner/server-owner confirmed-hit path. Remote
                // target owners still require an authoritative remote skill source before
                // they can safely relay this state to a different attacking client.
                if (state.FistAttackSerial == 0) state.FistAttackSerial = 1;
                if (state.LastFistHitSerial != state.FistAttackSerial &&
                    Time.time - state.LastFistHitAt >= 0.12f)
                {
                    state.LastFistHitSerial = state.FistAttackSerial;
                    state.FistComboStacks = Mathf.Clamp(state.FistComboStacks + 1, 0, 3);
                    state.LastFistHitAt = Time.time;
                    PerkVisualService.PlayFistCombo(attacker, state.FistComboStacks);
                }
            }
        }
    }

    [HarmonyPatch(typeof(SEMan), "ModifyAdrenaline")]
    [HarmonyPriority(Priority.Last)]
    internal static class FistsPassiveAdrenalineGainPatch
    {
        private static void Postfix(SEMan __instance, float baseValue, ref float use)
        {
            Player player = __instance?.m_character as Player;
            if (player == null || use <= 0f || baseValue <= 0f || !MasteryPlugin.Settings.Enabled.Value ||
                !FistsAdrenalinePolicy.Eligible(player))
                return;
            float before = use;
            float level = Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Unarmed), 0f, 100f);
            use *= FistsGainRules.PassiveScale(level);
            FistsAdrenalinePolicy.Log(player, "passive-scale", baseValue, player.GetAdrenaline(),
                player.GetAdrenaline(), before, use);
        }
    }

}




