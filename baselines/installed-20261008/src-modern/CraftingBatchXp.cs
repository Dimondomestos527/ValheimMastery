using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class CraftingBatchXp
    {
        // Called only inside vanilla's successful-output branch after native payment.
        // resolvedRecipes is vanilla local3, used by GetAmount/HaveRequirements/statistics,
        // not the current UI request or the number of output/bonus items.
        internal static void RecordCompleted(InventoryGui gui, Player player, int resolvedRecipes,
            ItemDrop.ItemData mainOutput)
        {
            CraftingXpState state = CraftingXpState.Current;
            if (state == null || state.CompletedRecipes != 0 || state.Gui != gui || state.Player != player ||
                player == null || player != Player.m_localPlayer || state.Recipe != gui.m_craftRecipe ||
                resolvedRecipes <= 0 || mainOutput == null || mainOutput.m_quality != state.Quality ||
                !string.Equals(TierDatabase.ItemKey(mainOutput),
                    GatheringProgressionService.Normalize(state.Recipe.m_item.gameObject.name), StringComparison.Ordinal) ||
                state.Outcome?.HasSuccessfulOutput() != true) return;
            state.CompletedRecipes = state.Upgrade ? 1 : resolvedRecipes;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class CraftingBatchCompletionPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            var panel = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel", new[] { typeof(bool) });
            int anchor = -1;
            for (int i = 2; i < code.Count; i++)
                if (code[i].Calls(panel) && code[i - 2].opcode == OpCodes.Ldarg_0 &&
                    code[i - 1].opcode == OpCodes.Ldc_I4_0)
                {
                    if (anchor != -1) throw new InvalidOperationException("Ambiguous DoCrafting completion gate.");
                    anchor = i - 2;
                }
            var locals = __originalMethod.GetMethodBody()?.LocalVariables;
            // Fail installation of this patch on native drift instead of guessing a count.
            if (anchor < 0 || locals == null || locals.Count <= 15 || locals[3].LocalType != typeof(int) ||
                locals[15].LocalType != typeof(ItemDrop.ItemData))
                throw new InvalidOperationException("Unsupported DoCrafting completed-batch layout.");
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[anchor].labels);
            first.blocks.AddRange(code[anchor].blocks);
            code[anchor].labels.Clear();
            code[anchor].blocks.Clear();
            code.InsertRange(anchor, new[] {
                first,
                new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldloc_3),
                new CodeInstruction(OpCodes.Ldloc_S, (byte)15),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftingBatchXp), nameof(CraftingBatchXp.RecordCompleted)))
            });
            return code;
        }
    }
}

