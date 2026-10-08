using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class Fishing70HookWindow
    {
        internal Fish Catch;
        internal float Until;
    }

    internal static class Fishing70Service
    {
        internal const float HookWindowSeconds = 3f;
        internal const float StaminaCostMultiplier = 0.30f;
        private static readonly ConditionalWeakTable<FishingFloat, Fishing70HookWindow> Windows =
            new ConditionalWeakTable<FishingFloat, Fishing70HookWindow>();

        private static bool IsLocalOwner(FishingFloat fishingFloat)
        {
            return fishingFloat != null && Player.m_localPlayer != null && fishingFloat.GetOwner() == Player.m_localPlayer;
        }

        internal static void ObserveNewCatch(FishingFloat fishingFloat, Fish previous)
        {
            Fish current = fishingFloat?.GetCatch();
            if (current == null || current == previous) return;
            Player player = Player.m_localPlayer;
            if (player == null || !IsLocalOwner(fishingFloat) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Fishing, 70)) return;
            Fishing70HookWindow state = Windows.GetOrCreateValue(fishingFloat);
            state.Catch = current;
            state.Until = Time.time + HookWindowSeconds;
            PerkVisualService.PlayProc(player, "fishing_70", fishingFloat.transform.position, false, true);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fishing70] Hooked fish; reduced reel stamina for 3 seconds.");
        }

        internal static bool IsActive(FishingFloat fishingFloat)
        {
            Player player = Player.m_localPlayer;
            return player != null && IsLocalOwner(fishingFloat) &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Fishing, 70) &&
                Windows.TryGetValue(fishingFloat, out Fishing70HookWindow state) &&
                state.Until > Time.time && state.Catch != null && state.Catch == fishingFloat.GetCatch();
        }
    }

    [HarmonyPatch(typeof(FishingFloat), "TryToHook")]
    internal static class Fishing70HookPatch
    {
        private static void Prefix(FishingFloat __instance, out Fish __state) =>
            __state = __instance?.GetCatch();

        private static void Postfix(FishingFloat __instance, Fish __state) =>
            Fishing70Service.ObserveNewCatch(__instance, __state);
    }

    [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
    internal static class Fishing70StaminaPatch
    {
        internal sealed class State
        {
            internal float Pull;
            internal float Hooked;
            internal float HookedMaxSkill;
            internal bool Restored;
        }

        private static void Prefix(FishingFloat __instance, out State __state)
        {
            __state = null;
            if (__instance == null || !Fishing70Service.IsActive(__instance)) return;
            __state = new State
            {
                Pull = __instance.m_pullStaminaUse,
                Hooked = __instance.m_hookedStaminaPerSec,
                HookedMaxSkill = __instance.m_hookedStaminaPerSecMaxSkill
            };
            __instance.m_pullStaminaUse *= Fishing70Service.StaminaCostMultiplier;
            __instance.m_hookedStaminaPerSec *= Fishing70Service.StaminaCostMultiplier;
            __instance.m_hookedStaminaPerSecMaxSkill *= Fishing70Service.StaminaCostMultiplier;
        }

        private static void Postfix(FishingFloat __instance, State __state) => Restore(__instance, __state);

        private static Exception Finalizer(FishingFloat __instance, State __state, Exception __exception)
        {
            Restore(__instance, __state);
            return __exception;
        }

        private static void Restore(FishingFloat fishingFloat, State state)
        {
            if (fishingFloat == null || state == null || state.Restored) return;
            fishingFloat.m_pullStaminaUse = state.Pull;
            fishingFloat.m_hookedStaminaPerSec = state.Hooked;
            fishingFloat.m_hookedStaminaPerSecMaxSkill = state.HookedMaxSkill;
            state.Restored = true;
        }
    }
}
