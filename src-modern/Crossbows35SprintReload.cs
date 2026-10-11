using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimMastery
{
    // Replace ONLY CheckRun's successful-sprint queue cancellation. Native action
    // progression, completion, reload cost and every other cancellation remain native.
    [HarmonyPatch(typeof(Player), "CheckRun")]
    internal static class Crossbows35SprintReloadPatch
    {
        internal static void ClearForSprint(Humanoid actor)
        {
            if (actor is Player player && Crossbows35ReloadGuard.SprintReload(player)) return;
            actor.ClearActionQueue();
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            var clear = AccessTools.Method(typeof(Humanoid), nameof(Humanoid.ClearActionQueue));
            var replacement = AccessTools.Method(typeof(Crossbows35SprintReloadPatch), nameof(ClearForSprint));
            int matches = 0;
            var code = new List<CodeInstruction>(input);
            foreach (var instruction in code)
                if (instruction.Calls(clear))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Crossbows35 native sprint-clear seam changed; expected exactly one call.");
            return code;
        }
    }

    // Scoped to walking's speed branch, NOT a global InMinorAction override.
    [HarmonyPatch(typeof(Character), "UpdateWalking")]
    internal static class Crossbows35SprintSpeedPatch
    {
        internal static bool SlowForWalking(Character actor)
        {
            if (actor is Player player && player.IsRunning() && Crossbows35ReloadGuard.SprintReload(player)) return false;
            return actor.InMinorActionSlowdown();
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input)
        {
            var original = AccessTools.Method(typeof(Character), nameof(Character.InMinorActionSlowdown));
            var replacement = AccessTools.Method(typeof(Crossbows35SprintSpeedPatch), nameof(SlowForWalking));
            int matches = 0;
            var code = new List<CodeInstruction>(input);
            foreach (var instruction in code)
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    matches++;
                }
            if (matches != 1) throw new InvalidOperationException("Crossbows35 native walk-speed seam changed; expected exactly one call.");
            return code;
        }
    }
}
