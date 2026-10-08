using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class CanonicalBonusFeedbackChecks
{
    internal static int Run(string pluginPath, string gamePath, Assembly plugin)
    {
        using var module = ModuleDefinition.ReadModule(pluginPath);
        using var vanilla = ModuleDefinition.ReadModule(gamePath);
        int passed = 0;
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference called && called.DeclaringType.Name == type && called.Name == name);
        bool Field(MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == name);
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Canonical bonus feedback regression: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }

        foreach ((string type, string methodName) in new[] { ("InventoryGui", "DoCrafting"), ("CookingStation", "OnInteract") })
        {
            var method = vanilla.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == methodName);
            var code = method.Body.Instructions.ToList();
            int bonus = code.FindIndex(i => i.Operand is FieldReference field && field.Name == "m_craftBonusEffect");
            Check(bonus > 0 && code.Take(bonus).Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "DamageText" && m.Name == "ShowText") &&
                code.Skip(bonus).Take(16).Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "EffectList" && m.Name == "Create"),
                "actual " + type + "." + methodName + " uses native craft-bonus EffectList after orange text");
        }

        Assembly game = Assembly.LoadFrom(gamePath);
        MethodInfo original = game.GetType("Pickable", true)!.GetMethod("Interact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var actual = vanilla.Types.Single(t => t.Name == "Pickable").Methods.Single(m => m.Name == "Interact");
        // Harmony 2's old MethodInvoker/AccessTools runtime shim does not initialize on
        // this net8 host. Read the REAL game IL using Cecil, resolve each metadata token
        // in that same game module, then run the shipped transpiler over those actual
        // instructions. This tests the transformation, not Harmony's detour engine.
        var input = ReadInstructions(actual, original);
        var snapshot = input.Select(i => new CodeInstruction(i)).ToList();
        MethodInfo transpiler = plugin.GetType("ValheimMastery.PickableCanonicalBonusFeedbackPatch", true)
            .GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { input })!).ToList();
        Check(snapshot.Count == result.Count, "Pickable transpiler preserves every vanilla instruction slot");
        int changes = 0;
        for (int i = 0; i < snapshot.Count; ++i)
        {
            if (!snapshot[i].labels.SequenceEqual(result[i].labels) || !snapshot[i].blocks.SequenceEqual(result[i].blocks))
                throw new Exception("Pickable branch labels/exception blocks changed at instruction " + i);
            if (snapshot[i].opcode == result[i].opcode && Equals(snapshot[i].operand, result[i].operand)) continue;
            changes++;
            MethodInfo oldCall = snapshot[i].operand as MethodInfo ?? throw new Exception("Changed instruction was not a method call");
            MethodInfo newCall = result[i].operand as MethodInfo ?? throw new Exception("Replacement instruction is not a method call");
            Check(oldCall.DeclaringType?.Name == "EffectList" && oldCall.Name == "Create" && !oldCall.IsStatic &&
                newCall.DeclaringType?.Name == "VanillaBonusFeedback" && newCall.Name == "PlayPickableBonus" && newCall.IsStatic &&
                oldCall.ReturnType == newCall.ReturnType && oldCall.GetParameters().Length + 1 == newCall.GetParameters().Length &&
                newCall.GetParameters()[0].ParameterType == oldCall.DeclaringType &&
                oldCall.GetParameters().Select(p => p.ParameterType).SequenceEqual(newCall.GetParameters().Skip(1).Select(p => p.ParameterType)) &&
                result[i + 1].opcode == System.Reflection.Emit.OpCodes.Pop,
                "Pickable replacement preserves instance/argument/return stack and discarded effect-array result");
        }
        Check(changes == 1, "exactly one confirmed Pickable bonus effect replaced; roll/RPC/quantity untouched");
        Check(input.Any(i => i.labels.Count > 0) && snapshot.Where(i =>
            i.opcode.FlowControl == System.Reflection.Emit.FlowControl.Branch ||
            i.opcode.FlowControl == System.Reflection.Emit.FlowControl.Cond_Branch).Count() ==
            actual.Body.Instructions.Count(i => i.OpCode.FlowControl == FlowControl.Branch || i.OpCode.FlowControl == FlowControl.Cond_Branch),
            "actual vanilla branch targets reconstructed and preserved, not a synthetic bonus-only fixture");
        Check(Calls(Method("VanillaBonusFeedback", "Show"), "DamageText", "ShowText") && Field(Method("VanillaBonusFeedback", "Play"), "m_craftBonusEffect"),
            "custom successful drops share actual vanilla Bonus text and craft-bonus runtime reference");
        foreach (string name in new[] { "PlayPickaxeResourceProcLocal", "PlayWoodResourceProcLocal", "PlayGenericResourceBonusLocal" })
            Check(Calls(Method("PerkVisualService", name), "VanillaBonusFeedback", "Show"), name + " uses unified cue");
        Check(Calls(Method("VanillaBonusFeedback", "Play"), "VfxPool", "Spawn") && Calls(Method("VanillaBonusFeedback", "Play"), "PerkAudioService", "PlayPrefab") &&
            Field(Method("VanillaBonusFeedback", "Play"), "EnablePerkProcVFX") && Field(Method("PerkAudioService", "PlayPrefab"), "EnablePerkSFX"),
            "visual/audio paths and preferences separated");
        Check(!Type("PerkAudioService").Methods.Any(m => Calls(m, "Object", "Instantiate")) &&
            !Type("PerkAudioService").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
                .Any(i => i.OpCode.Code == Code.Ldstr && Equals(i.Operand, "sfx_gui_craftitem_forge")),
            "sound helper never clones mixed effect prefabs or guesses unrelated forge fallback");
        Check(!Type("VanillaBonusFeedback").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i =>
            i.Operand is MethodReference m && new[] { "AddItem", "RemoveItem", "RaiseSkill", "Damage", "InvokeRPC" }.Contains(m.Name)),
            "bonus presentation cannot award resources/XP or dispatch gameplay RPCs");
        Console.WriteLine($"PASS: canonical bonus feedback {passed} checks; runtime asset binding/audibility and remote delivery LIVE_TEST_REQUIRED.");
        return 0;
    }

    private static List<CodeInstruction> ReadInstructions(MethodDefinition actual, MethodInfo original)
    {
        var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
            .ToDictionary(op => op.Value);
        var dynamicMethod = new System.Reflection.Emit.DynamicMethod("BonusFeedback_IlLabels", typeof(void), System.Type.EmptyTypes);
        var generator = dynamicMethod.GetILGenerator(); // labels/locals only; never emitted, invoked or detoured
        var locals = original.GetMethodBody()!.LocalVariables.Select(v => generator.DeclareLocal(v.LocalType, v.IsPinned)).ToArray();
        var labels = new Dictionary<Instruction, System.Reflection.Emit.Label>();
        System.Reflection.Emit.Label Label(Instruction target)
        {
            if (!labels.TryGetValue(target, out var label)) labels[target] = label = generator.DefineLabel();
            return label;
        }
        object Operand(object operand) => operand switch
        {
            Instruction target => Label(target),
            Instruction[] targets => targets.Select(Label).ToArray(),
            VariableDefinition local => locals[local.Index],
            ParameterDefinition parameter => parameter.Index + (actual.HasThis ? 1 : 0),
            FieldReference field => original.Module.ResolveField(field.MetadataToken.ToInt32())!,
            MethodReference method => original.Module.ResolveMethod(method.MetadataToken.ToInt32())!,
            TypeReference type => original.Module.ResolveType(type.MetadataToken.ToInt32())!,
            _ => operand
        };
        var result = actual.Body.Instructions.Select(i => new CodeInstruction(opcodes[i.OpCode.Value], Operand(i.Operand))).ToList();
        var indexes = actual.Body.Instructions.Select((instruction, index) => (instruction, index)).ToDictionary(p => p.instruction, p => p.index);
        foreach (var pair in labels) result[indexes[pair.Key]].labels.Add(pair.Value);
        // Preserve actual EH markers too. Interact currently has none; retain real
        // metadata support rather than silently fabricating or dropping those blocks.
        foreach (var group in actual.Body.ExceptionHandlers.GroupBy(h => (h.TryStart, h.TryEnd)))
        {
            result[indexes[group.Key.TryStart]].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            foreach (var handler in group)
            {
                ExceptionBlockType kind = handler.HandlerType switch
                {
                    ExceptionHandlerType.Catch => ExceptionBlockType.BeginCatchBlock,
                    ExceptionHandlerType.Finally => ExceptionBlockType.BeginFinallyBlock,
                    ExceptionHandlerType.Fault => ExceptionBlockType.BeginFaultBlock,
                    _ => throw new NotSupportedException("Bonus feedback QC requires explicit support for filter EH")
                };
                var catchType = handler.CatchType != null ? original.Module.ResolveType(handler.CatchType.MetadataToken.ToInt32()) : null;
                result[indexes[handler.HandlerStart]].blocks.Add(new ExceptionBlock(kind, catchType));
            }
            Instruction end = group.OrderBy(h => h.HandlerEnd?.Offset ?? int.MaxValue).Last().HandlerEnd;
            int endIndex = end != null ? indexes[end] - 1 : result.Count - 1;
            result[endIndex].blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        }
        return result;
    }
}
