using HarmonyLib;
using System;
using UnityEngine;

namespace ValheimMastery
{
    // Temporarily add to vanilla's timed-block multiplier for the exact BlockAttack call.
    // Restoring the shared item data is mandatory because SharedData is reused by item instances.
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    [HarmonyPriority(Priority.First)]
    internal static class Fists70ParryMultiplierPatch
    {
        private sealed class State { internal ItemDrop.ItemData Blocker; internal float Vanilla; internal Player Player; internal Character Attacker; internal bool Perfect; }

        private static void Prefix(Humanoid __instance, Character attacker, out State __state)
        {
            __state = null;
            Player player = __instance as Player;
            ItemDrop.ItemData blocker = player?.GetCurrentBlocker();
            if (player == null || blocker?.m_shared?.m_skillType != Skills.SkillType.Unarmed ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 70)) return;
            __state = new State
            {
                Blocker = blocker, Vanilla = blocker.m_shared.m_timedBlockBonus, Player = player, Attacker = attacker,
                Perfect = player.m_blockTimer >= 0f && player.m_blockTimer <= Humanoid.m_perfectBlockInterval
            };
            blocker.m_shared.m_timedBlockBonus = __state.Vanilla + (PerkRuntimeService.IsBareHands(blocker) ? 10f : 6f);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fists70] ParryMultiplier vanilla=" + __state.Vanilla.ToString("0.##") + " final=" + blocker.m_shared.m_timedBlockBonus.ToString("0.##"));
        }

        private static void Postfix(bool __result, State __state)
        {
            if (__result && __state?.Perfect == true) Fists70MaulService.MarkPerfectParry(__state.Player, __state.Attacker);
            Restore(__state);
        }
        private static Exception Finalizer(Exception __exception, State __state) { Restore(__state); return __exception; }
        private static void Restore(State state) { if (state?.Blocker?.m_shared != null) state.Blocker.m_shared.m_timedBlockBonus = state.Vanilla; }
    }
}
