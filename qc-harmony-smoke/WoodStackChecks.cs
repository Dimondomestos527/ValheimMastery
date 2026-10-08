using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class WoodStackChecks
{
    internal static int Run(string plugin, string managed, Assembly assembly)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        var clamp = assembly.GetType("ValheimMastery.WoodInventoryCapacity")!.GetMethod("ClampWoodStack", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var entry in new[] {
            (50,50,true,50), (51,50,true,51), (100,50,true,100), (101,50,true,100),
            (100,50,false,50), (100,1,true,1), (100,200,true,100), (200,200,true,200), (0,50,true,0) })
            Check((int)clamp.Invoke(null, new object[] { entry.Item1, entry.Item2, entry.Item3 })! == entry.Item4,
                $"preserve bounded saved wood quantity {entry}");
        using var native = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
        var load = native.Types.Single(t => t.Name == "Inventory").Methods.Single(m => m.Name == "AddItem" &&
            m.Parameters.Count == 14 && m.Parameters[0].ParameterType.FullName == "System.Int32" && m.Parameters[1].ParameterType.FullName == "System.Int32");
        var il = load.Body.Instructions;
        Check(il.Where((i,n) => n > 0 && i.Operand is MethodReference m && m.DeclaringType.Name == "Mathf" && m.Name == "Min" &&
            il[n-1].Operand is FieldReference f && f.Name == "m_maxStackSize").Count() == 1, "native serialized-load clamp has exactly one supported replacement");
        using var mod = ModuleDefinition.ReadModule(plugin);
        var capacity = mod.Types.Single(t => t.Name == "WoodInventoryCapacity");
        Check(capacity.Methods.Single(m => m.Name == "ExpandedOwner").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "PersistenceReady"), "stack expansion disabled unless preservation patch installed");
        var quick = mod.Types.Single(t => t.Name == "WoodInventoryQuickMovePatch").Methods.Single(m => m.Name == "Prefix");
        Check(quick.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Clone") && quick.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "RemoveItem" && m.Parameters.Count == 2), "quick move copies50 and debits only moved quantity");
        Check(mod.Types.Single(t => t.Name == "WoodInventoryDragMovePatch").Methods.Single(m => m.Name == "Prefix").Parameters.Any(p => p.Name == "amount" && p.ParameterType.IsByReference), "drag move caps transfer amount rather than source stack");
        Console.WriteLine($"PASS wood-stack boundaries {checks}; save/reload, drag, shift-click and tombstone LIVE_TEST_REQUIRED.");
        return 0;
    }
}
