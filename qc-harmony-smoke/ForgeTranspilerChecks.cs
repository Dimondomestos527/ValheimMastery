using System.Reflection;
using Mono.Cecil;

internal static class ForgeTranspilerChecks
{
    internal static int Run(Assembly plugin, string managed)
    {
        using var module = ModuleDefinition.ReadModule(plugin.Location);
        var safety = module.Types.Single(t => t.Name == "PotentialForgeSafetyPatch");
        if (safety.Methods.Any(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyTranspiler")))
            throw new Exception("Forge must not rewrite vanilla failure quality; startup-breaking transpiler returned.");
        if (safety.Methods.Any(m => m.Name == "PreserveProtectedQuality"))
            throw new Exception("Removed forge transformation is still present.");
        using var game = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
        var code = game.Types.Single(t => t.Name == "InventoryGui").Methods.Single(m => m.Name == "DoCrafting").Body.Instructions;
        int reductions = code.Where((i, n) => n + 1 < code.Count && i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_2 && code[n + 1].OpCode.Code == Mono.Cecil.Cil.Code.Sub).Count();
        if (reductions != 2) throw new Exception("Native failure-quality/message behavior changed.");
        Console.WriteLine("PASS: forge safety has NO transpiler; native downgrade branches untouched. STATIC ONLY, not an in-game startup test.");
        return 0;
    }
}
