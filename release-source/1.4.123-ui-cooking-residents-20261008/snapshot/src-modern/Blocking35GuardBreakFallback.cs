using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // AddStaggerDamage returns true on the precise frame the stagger bar overflows.
    // This is a safer guard-break signal than waiting until the later animation call.
    [HarmonyPatch(typeof(Character), nameof(Character.AddStaggerDamage))]
    internal static class Blocking35GuardBreakOverflowPatch
    {
        // Superseded by Stored Pressure; retained only as migration history and never patched live.
        private static bool Prepare() => false;
        private sealed class State
        {
            internal Player Player;
            internal ItemDrop.ItemData Blocker;
            internal bool WasBlocking;
        }

        private static void Prefix(Character __instance, out State __state)
        {
            Player player = __instance as Player;
            __state = new State
            {
                Player = player,
                WasBlocking = player != null && (player.IsBlocking() || player.m_blocking),
                Blocker = player?.GetCurrentBlocker()
            };
        }

        // Valheim 1.0.x signature: (float damage, Vector3 forceDirection, HitData hit).
        private static void Postfix(float damage, Vector3 forceDirection, HitData hit, bool __result, State __state)
        {
            if (!__result || __state == null || !__state.WasBlocking || __state.Player == null)
                return;
            Vector3 point = hit != null ? hit.m_point : __state.Player.GetCenterPoint();
            Blocking35Service.TriggerGuardBreak(__state.Player, __state.Blocker, hit?.GetAttacker() as Character, point);
        }
    }
}