using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Only the original placement frame may consume this one-shot accepted intent.
    internal static class MasterIdolAutoPlacement
    {
        [ThreadStatic] private static Player Player;
        [ThreadStatic] private static bool Injected;
        [ThreadStatic] private static float PreviousPress;
        internal static void Begin(Player player, bool takeInput)
        {
            Player = takeInput && Eligible(player) ? player : null;
            Injected = false;
            if (Player != null) PreviousPress = Player.m_placePressedTime;
        }
        private static bool Eligible(Player player) => player != null && MasterIdolPlacement.AutoReady(player) &&
            Time.time - player.m_lastToolUseTime > player.m_placeDelay &&
            !ZInput.GetButton("JoyAltKeys") && !Hud.InRadial();
        internal static void End()
        {
            // Native clears a consumed press before attempting resources/stamina/placement.
            // A queued synthetic press must never escape into another selection/frame.
            if (Injected && Player != null && Player.m_placePressedTime > -9990f)
                Player.m_placePressedTime = PreviousPress;
            Player = null; Injected = false;
        }
        internal static bool Consume(string button)
        {
            if (Player == null || Injected || button != "Attack" || !Eligible(Player) || !MasterIdolPlacement.ConsumeAuto()) return false;
            Injected = true; return true;
        }
    }
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class MasterIdolAutoFramePatch
    {
        private static void Prefix(Player __instance, bool takeInput) => MasterIdolAutoPlacement.Begin(__instance, takeInput);
        private static Exception Finalizer(Exception __exception) { MasterIdolAutoPlacement.End(); return __exception; }
    }
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), new[] { typeof(string) })]
    internal static class MasterIdolAutoInputPatch
    {
        private static bool Prefix(string __0, ref bool __result)
        { if (!MasterIdolAutoPlacement.Consume(__0)) return true; __result = true; return false; }
    }
}
