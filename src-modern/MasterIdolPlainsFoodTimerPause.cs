using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimMastery
{
    // Pause the native lifetime debit only. The native tick, nutrition calculation,
    // expired-food removal and regeneration continue with their original inputs.
    [HarmonyPatch(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) })]
    internal static class MasterIdolPlainsFoodTimerPause
    {
        internal static float Remaining(float remaining, float elapsed, Player player)
        {
            if (remaining > 0f && !float.IsInfinity(remaining) && elapsed > 0f &&
                !float.IsInfinity(elapsed) && MasteryPlugin.Settings.Enabled.Value &&
                MasterIdolPlains.Owned(player) && !player.IsDead() &&
                MasterIdolEffectZones.At("Plains", player.transform.position) != null)
                return remaining;
            return remaining - elapsed;
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo time = AccessTools.Field(typeof(Player.Food), nameof(Player.Food.m_time));
            MethodInfo helper = AccessTools.Method(typeof(MasterIdolPlainsFoodTimerPause), nameof(Remaining));
            if (time == null || helper == null) return code;
            int found = -1, count = 0;
            for (int i = 2; i + 1 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Sub || !code[i - 2].LoadsField(time) ||
                    !LoadLocal(code[i - 1]) || code[i + 1].opcode != OpCodes.Stfld ||
                    !Equals(code[i + 1].operand, time)) continue;
                found = i; count++;
            }
            if (count != 1)
            {
                MasteryPlugin.Log?.LogWarning("[PlainsFoodPause] Native lifetime debit not uniquely matched; food update left unchanged.");
                return code;
            }
            // Move branch/exception entry to the additional stack argument.
            var player = new CodeInstruction(OpCodes.Ldarg_0);
            player.labels.AddRange(code[found].labels); code[found].labels.Clear();
            player.blocks.AddRange(code[found].blocks); code[found].blocks.Clear();
            code.Insert(found, player);
            code[found + 1].opcode = OpCodes.Call;
            code[found + 1].operand = helper;
            return code;
        }

        private static bool LoadLocal(CodeInstruction code) => code.opcode == OpCodes.Ldloc_0 ||
            code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Ldloc_2 ||
            code.opcode == OpCodes.Ldloc_3 || code.opcode == OpCodes.Ldloc || code.opcode == OpCodes.Ldloc_S;
    }
}
