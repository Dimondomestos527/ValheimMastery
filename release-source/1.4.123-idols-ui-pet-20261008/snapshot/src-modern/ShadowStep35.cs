using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class ShadowStepCollisionPair
    {
        internal Collider PlayerCollider;
        internal Collider EnemyCollider;
    }

    internal sealed class ShadowStepState
    {
        internal float ReadyUntil;
        internal bool SpecialDodgeActive;
        internal readonly List<ShadowStepCollisionPair> Ignored = new List<ShadowStepCollisionPair>();
    }

    internal static class ShadowStep35Service
    {
        internal const float ReadyDuration = 5f;

        internal static void ArmFromConfirmedKill(Player player)
        {
            if (player == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                if (player == Player.m_localPlayer) { ArmLocal(player, ReadyDuration); return; }
                NetworkSync.SendProcFeedback(player, "knife35_ready:" + ReadyDuration.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), player.GetCenterPoint());
                return;
            }
            if (player == Player.m_localPlayer) ArmLocal(player, ReadyDuration);
        }

        internal static void ArmLocal(Player player, float duration)
        {
            if (player == null || player != Player.m_localPlayer || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Knives, 35)) return;
            ShadowStepState state = MasteryStateStore.GetPlayerState<ShadowStepState>(player);
            state.ReadyUntil = Time.time + Mathf.Max(0.1f, duration);
            ShadowStepVisualService.ShowReady(player, duration);
            MasteryPlugin.Log.LogInfo("[Knife35] phase=READY source=SERVER_CONFIRMED expires=" + state.ReadyUntil.ToString("0.000"));
        }

        internal static bool IsReady(Player player) => player != null && player == Player.m_localPlayer &&
            MasteryStateStore.TryGetPlayerState<ShadowStepState>(player, out ShadowStepState state) && state.ReadyUntil >= Time.time;

        internal static void ConfirmDodge(Player player)
        {
            if (!IsReady(player)) return;
            ShadowStepState state = MasteryStateStore.GetPlayerState<ShadowStepState>(player);
            state.ReadyUntil = 0f;
            state.SpecialDodgeActive = true;
            RefreshEnemyCollisionPass(player, state);
            ShadowStepVisualService.PlayDodge(player);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Knife35] phase=CONSUMED acceptedDodge=true staminaCost=0 rootMotionMultiplier=1.5");
        }

        internal static bool IsSpecialDodge(Player player) => player != null &&
            MasteryStateStore.TryGetPlayerState<ShadowStepState>(player, out ShadowStepState state) && state.SpecialDodgeActive;

        internal static void Tick(Player player)
        {
            if (player == null || !MasteryStateStore.TryGetPlayerState<ShadowStepState>(player, out ShadowStepState state) || !state.SpecialDodgeActive) return;
            if (!player.InDodge()) { EndDodge(player, state); return; }
            RefreshEnemyCollisionPass(player, state);
        }

        private static void EndDodge(Player player, ShadowStepState state)
        {
            foreach (ShadowStepCollisionPair pair in state.Ignored)
                if (pair.PlayerCollider != null && pair.EnemyCollider != null)
                    Physics.IgnoreCollision(pair.PlayerCollider, pair.EnemyCollider, false);
            state.Ignored.Clear();
            state.SpecialDodgeActive = false;
            ShadowStepVisualService.PlayEnd(player);
        }

        private static void RefreshEnemyCollisionPass(Player player, ShadowStepState state)
        {
            Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target == player || target.IsDead() || !BaseAI.IsEnemy(player, target) ||
                    MasteryClassificationService.GetCreatureClass(target) != CreatureClass.SmallNormal ||
                    (target.GetCenterPoint() - player.GetCenterPoint()).sqrMagnitude > 9f) continue;
                foreach (Collider playerCollider in playerColliders)
                foreach (Collider enemyCollider in target.GetComponentsInChildren<Collider>())
                {
                    if (playerCollider == null || enemyCollider == null || AlreadyIgnored(state, playerCollider, enemyCollider)) continue;
                    Physics.IgnoreCollision(playerCollider, enemyCollider, true);
                    state.Ignored.Add(new ShadowStepCollisionPair { PlayerCollider = playerCollider, EnemyCollider = enemyCollider });
                }
            }
        }

        private static bool AlreadyIgnored(ShadowStepState state, Collider a, Collider b)
        {
            foreach (ShadowStepCollisionPair pair in state.Ignored)
                if (pair.PlayerCollider == a && pair.EnemyCollider == b) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateDodge")]
    internal static class ShadowStep35DodgePatch
    {
        private sealed class Snapshot { internal bool WasInDodge; internal bool Ready; internal bool HadQueuedDodge; }
        private static void Prefix(Player __instance, out Snapshot __state)
        {
            __state = new Snapshot
            {
                WasInDodge = __instance != null && __instance.InDodge(),
                HadQueuedDodge = __instance != null && __instance.m_queuedDodgeTimer > 0f,
                Ready = __instance != null && ShadowStep35Service.IsReady(__instance)
            };
        }
        private static void Postfix(Player __instance, Snapshot __state)
        {
            if (__instance == null || __state == null) return;
            if (__state.Ready && __state.HadQueuedDodge && !__state.WasInDodge && __instance.m_queuedDodgeTimer <= 0f && __instance.m_dodgeInvincible)
                ShadowStep35Service.ConfirmDodge(__instance);
            ShadowStep35Service.Tick(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetDodgeStaminaUse))]
    internal static class ShadowStep35FreeDodgePatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (__instance != null && __instance.m_queuedDodgeTimer > 0f && !__instance.InDodge() && ShadowStep35Service.IsReady(__instance))
                __result = 0f;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddRootMotion))]
    [HarmonyPriority(Priority.First)]
    internal static class ShadowStep35RootMotionPatch
    {
        private static void Prefix(Character __instance, ref Vector3 vel)
        {
            Player player = __instance as Player;
            if (!ShadowStep35Service.IsSpecialDodge(player) || !player.InDodge()) return;
            vel.x *= 1.5f;
            vel.z *= 1.5f;
        }
    }
}