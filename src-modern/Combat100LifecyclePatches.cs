using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.LoadPlayerData))]
    internal static class Combat100MusterProfileLoadPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __0, out bool __state) => __state = Combat100MusterOwner.BeforeLoad(__0);
        private static void Postfix(Player __0, bool __state)
        { if (__state) Combat100MusterOwner.Loaded(__0); }
        // If original throws, postfix never clears hydration quarantine. Empty
        // profiles also receive cleanup; no fabricated receipt restoration occurs.
    }
    [HarmonyPatch(typeof(Player), "UpdateFood")]
    internal static class Combat100MusterFoodCachePatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var code = new List<CodeInstruction>(source);
            int seam = -1, count = 0;
            for (int i = 0; i + 2 < code.Count; ++i)
                if ((code[i].opcode == OpCodes.Call || code[i].opcode == OpCodes.Callvirt) &&
                    code[i].operand is MethodInfo method && method.Name == "MoveNext" &&
                    method.DeclaringType == typeof(List<Player.Food>.Enumerator) &&
                    (code[i + 1].opcode == OpCodes.Brtrue || code[i + 1].opcode == OpCodes.Brtrue_S) &&
                    (code[i + 2].opcode == OpCodes.Leave || code[i + 2].opcode == OpCodes.Leave_S))
                {
                    // Regeneration has a second MoveNext/leave. Only the cache
                    // loop exits to the exact native three-byref food aggregate.
                    if (!(code[i + 2].operand is Label target)) continue;
                    int destination = code.FindIndex(x => x.labels.Contains(target));
                    if (destination < 0 || destination + 4 >= code.Count || code[destination].opcode != OpCodes.Ldarg_0) continue;
                    bool addresses = true;
                    for (int j = 1; j <= 3; ++j) addresses &= code[destination + j].opcode == OpCodes.Ldloca || code[destination + j].opcode == OpCodes.Ldloca_S;
                    if (!addresses || !code[destination + 4].Calls(AccessTools.Method(typeof(Player), nameof(Player.GetTotalFoodValue)))) continue;
                    seam = i + 2; ++count;
                }
            if (count != 1) throw new InvalidOperationException("[Combat100] Native complete food-cache exit is not unique.");
            var first = new CodeInstruction(OpCodes.Ldarg_0);
            first.labels.AddRange(code[seam].labels); code[seam].labels.Clear();
            first.blocks.AddRange(code[seam].blocks); code[seam].blocks.Clear();
            code.InsertRange(seam, new[] { first, new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(Combat100MusterOwner), nameof(Combat100MusterOwner.AfterNativeFoodCaches))) });
            // Early food-removal leave bypasses this callback. No forced food
            // update, timer decrement, regeneration or new native maximum formula.
            return code;
        }
    }
}

