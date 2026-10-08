using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Dodge70RollState
    {
        internal float PendingCost;
        internal float PendingUntil;
    }

    // Measures only stamina actually removed by UseStamina while native
    // UpdateDodge is admitting a queued roll. The dodge hit callback later
    // decides whether that roll truly avoided an enemy attack.
    internal static class Dodge70Refund
    {
        private const string CooldownId = "dodge_70";
        private const double CooldownSeconds = 8d;
        private static readonly ConditionalWeakTable<Player, Dodge70RollState> States = new ConditionalWeakTable<Player, Dodge70RollState>();
        [ThreadStatic] private static Player _capturingPlayer;
        [ThreadStatic] private static float _capturedSpend;

        internal sealed class UpdateSnapshot
        {
            internal bool Capturing;
            internal bool HadQueuedDodge;
            internal bool WasInDodge;
            internal Player PreviousPlayer;
            internal float PreviousSpend;
        }

        internal static void BeginUpdate(Player player, out UpdateSnapshot state)
        {
            state = new UpdateSnapshot
            {
                HadQueuedDodge = player != null && player.m_queuedDodgeTimer > 0f,
                WasInDodge = player != null && player.InDodge(),
                PreviousPlayer = _capturingPlayer,
                PreviousSpend = _capturedSpend
            };
            if (!MagicSkillPassives.OwnerReady(player) || !state.HadQueuedDodge || state.WasInDodge ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Dodge, 70)) return;
            _capturingPlayer = player;
            _capturedSpend = 0f;
            state.Capturing = true;
        }

        internal static void CompleteUpdate(Player player, UpdateSnapshot state)
        {
            if (player == null || state == null || !state.Capturing) return;
            bool admitted = state.HadQueuedDodge && !state.WasInDodge &&
                player.m_queuedDodgeTimer <= 0f && player.m_dodgeInvincible;
            float spend = _capturedSpend;
            if (admitted && spend > 0f && !float.IsNaN(spend) && !float.IsInfinity(spend))
            {
                Dodge70RollState roll = States.GetOrCreateValue(player);
                roll.PendingCost = spend;
                roll.PendingUntil = Time.time + 3f;
            }
            _capturingPlayer = state.PreviousPlayer;
            _capturedSpend = state.PreviousSpend;
            state.Capturing = false;
        }

        internal static void AbortUpdate(UpdateSnapshot state)
        {
            if (state == null || !state.Capturing) return;
            _capturingPlayer = state.PreviousPlayer;
            _capturedSpend = state.PreviousSpend;
            state.Capturing = false;
        }

        internal static bool TryRefund(Player player)
        {
            if (!MagicSkillPassives.OwnerReady(player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Dodge, 70) ||
                !States.TryGetValue(player, out Dodge70RollState roll) || roll.PendingCost <= 0f) return false;

            float cost = roll.PendingCost;
            roll.PendingCost = 0f; // a validated avoided-hit event can consume this roll only once
            if (Time.time > roll.PendingUntil) return false;

            float before = player.GetStamina();
            float refund = cost * 0.5f;
            float after = Mathf.Min(player.GetMaxStamina(), before + refund);
            float actualRefund = Mathf.Max(0f, after - before);
            if (actualRefund <= 0f ||
                !PerkCooldownStateService.TryConsume(player, CooldownId, CooldownSeconds)) return false;
            player.AddStamina(actualRefund);
            return true;
        }

        internal static float BeforeUseStamina(Player player) => ReferenceEquals(player, _capturingPlayer) ? player.GetStamina() : float.NaN;

        internal static void FinishUseStamina(Character character, float before)
        {
            if (!ReferenceEquals(character, _capturingPlayer) || float.IsNaN(before)) return;
            float spent = Mathf.Max(0f, before - ((Player)character).GetStamina());
            if (spent > 0f && !float.IsNaN(spent) && !float.IsInfinity(spent)) _capturedSpend += spent;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateDodge")]
    internal static class Dodge70RollCapturePatch
    {
        private static void Prefix(Player __instance, out Dodge70Refund.UpdateSnapshot __state) =>
            Dodge70Refund.BeginUpdate(__instance, out __state);
        private static void Postfix(Player __instance, Dodge70Refund.UpdateSnapshot __state) =>
            Dodge70Refund.CompleteUpdate(__instance, __state);
        private static Exception Finalizer(Exception __exception, Dodge70Refund.UpdateSnapshot __state)
        {
            Dodge70Refund.AbortUpdate(__state);
            return __exception;
        }
    }

    // Native UpdateDodge uses virtual Character.UseStamina: it dispatches to
    // Player's override. Patching the empty base method would capture nothing.
    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina), new[] { typeof(float) })]
    internal static class Dodge70UseStaminaPatch
    {
        private static void Prefix(Player __instance, out float __state) => __state = Dodge70Refund.BeforeUseStamina(__instance);
        private static void Postfix(Player __instance, float __state) => Dodge70Refund.FinishUseStamina(__instance, __state);
    }
}
