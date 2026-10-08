using System.Runtime.CompilerServices;
using HarmonyLib;

namespace ValheimMastery
{
    // Valheim clears m_currentAttackIsSecondary before some hit callbacks. Keep the intent on
    // the concrete Attack instance from Player.StartAttack through DoMelee/DoArea resolution.
    internal static class AttackIntentService
    {
        private sealed class Intent { internal bool Secondary; }
        private static readonly ConditionalWeakTable<Attack, Intent> Intents = new ConditionalWeakTable<Attack, Intent>();
        [System.ThreadStatic] private static int _scopeDepth;
        [System.ThreadStatic] private static bool _scopeSecondary;

        internal static void BeginPlayerAttack(bool secondary) { _scopeDepth++; _scopeSecondary = secondary; }
        internal static void EndPlayerAttack() { _scopeDepth = System.Math.Max(0, _scopeDepth - 1); if (_scopeDepth == 0) _scopeSecondary = false; }
        internal static void Capture(Attack attack) { if (attack != null && _scopeDepth > 0) Intents.GetOrCreateValue(attack).Secondary = _scopeSecondary; }
        internal static bool IsSecondary(Attack attack, Player player) =>
            (attack != null && Intents.TryGetValue(attack, out Intent intent) && intent.Secondary) ||
            (player != null && player.m_currentAttack == attack && player.m_currentAttackIsSecondary);
    }

    // StartAttack is declared by Humanoid in Valheim 1.0.x; targeting Player silently fails.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    [HarmonyPriority(Priority.First)]
    internal static class MasteryPlayerAttackIntentScopePatch
    {
        private static void Prefix(Humanoid __instance, bool secondaryAttack, out bool __state)
        {
            __state = __instance is Player;
            if (__state) AttackIntentService.BeginPlayerAttack(secondaryAttack);
        }
        private static void Postfix(bool __state) { if (__state) AttackIntentService.EndPlayerAttack(); }
        private static System.Exception Finalizer(bool __state, System.Exception __exception) { if (__state) AttackIntentService.EndPlayerAttack(); return __exception; }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    [HarmonyPriority(Priority.First)]
    internal static class MasteryAttackIntentCapturePatch
    {
        private static void Prefix(Attack __instance) => AttackIntentService.Capture(__instance);
    }
}