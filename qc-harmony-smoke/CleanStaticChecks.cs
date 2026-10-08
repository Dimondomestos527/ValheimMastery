using System.Collections;
using System.Reflection;
using HarmonyLib;

// QC-only reflection/catalog inspection. Never instantiates the plugin or game objects.
internal static class CleanStaticChecks
{
    internal static int Run(Assembly assembly)
    {
        var types = assembly.GetTypes();
        int errors = 0, explicitTargets = 0, dynamicClasses = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var categories = new Dictionary<string, int>();
        foreach (var type in types)
        {
            var attributes = type.GetCustomAttributes<HarmonyPatch>().Select(a => a.info).ToArray();
            if (attributes.Length == 0) continue;
            var targetType = attributes.Select(a => a.declaringType).LastOrDefault(t => t != null);
            var name = attributes.Select(a => a.methodName).LastOrDefault(n => n != null);
            var parameters = attributes.Select(a => a.argumentTypes).LastOrDefault(p => p != null);
            if (targetType == null || name == null) { dynamicClasses++; continue; }
            var originals = targetType.GetMethods(flags).Where(m => m.Name == name &&
                (parameters == null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))).ToArray();
            if (originals.Length != 1) { Console.WriteLine($"ERROR target {type.FullName} matches={originals.Length}"); errors++; continue; }
            explicitTargets++;
            categories.TryGetValue(targetType.Name, out int count); categories[targetType.Name] = count + 1;
            var original = originals[0];
            foreach (var patch in type.GetMethods(flags).Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer"))
            foreach (var p in patch.GetParameters())
            {
                var accepted = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
                if (p.Name == "__result")
                {
                    if (original.ReturnType == typeof(void) || !accepted.IsAssignableFrom(original.ReturnType))
                    { Console.WriteLine($"ERROR result injection {type.Name}.{patch.Name}: {accepted} vs {original.ReturnType}"); errors++; }
                }
                else if (p.Name?.StartsWith("___") == true)
                {
                    FieldInfo field = null;
                    for (var search = targetType; search != null && field == null; search = search.BaseType)
                        field = search.GetField(p.Name.Substring(3), flags);
                    if (field == null || !accepted.IsAssignableFrom(field.FieldType))
                    { Console.WriteLine($"ERROR field injection {type.Name}.{patch.Name}: {p.Name}"); errors++; }
                }
                else if (p.Name != null && !p.Name.StartsWith("__"))
                {
                    var arg = original.GetParameters().FirstOrDefault(a => a.Name == p.Name);
                    var actual = arg?.ParameterType;
                    if (actual?.IsByRef == true) actual = actual.GetElementType();
                    if (actual == null || !accepted.IsAssignableFrom(actual))
                    { Console.WriteLine($"ERROR argument injection {type.Name}.{patch.Name}: {p.Name} {accepted} vs {actual}"); errors++; }
                }
            }
        }
        Console.WriteLine($"Static targets={explicitTargets}, dynamic/special classes={dynamicClasses}, signature errors={errors}");
        foreach (var c in categories.OrderBy(c => c.Key)) Console.WriteLine($"CATEGORY {c.Key}: {c.Value}");
        var catalog = assembly.GetType("ValheimMastery.PerkCatalog", true)!;
        var entries = (IDictionary)catalog.GetField("BySkill", flags)!.GetValue(null)!;
        // Do not initialize PerkLocalization: its initializer invokes Harmony's
        // host-sensitive AccessTools. Inspect compiled registration strings instead.
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(assembly.Location);
        var loc = module.Types.Single(t => t.Name == "PerkLocalization");
        var words = loc.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr).Select(i => (string)i.Operand)
            .ToHashSet(StringComparer.Ordinal);
        int perks = 0, missing = 0;
        foreach (DictionaryEntry entry in entries)
        foreach (var perk in (IEnumerable)entry.Value!)
        {
            perks++;
            var id = (string)perk.GetType().GetField("Id", flags)!.GetValue(perk)!;
            foreach (var suffix in new[] { "name", "desc", "flavor" })
            {
                var key = "vm_perk_" + id + "_" + suffix;
                if (!words.Contains(key))
                { Console.WriteLine("ERROR localization missing " + key); missing++; }
            }
        }
        Console.WriteLine($"Catalog skills={entries.Count}, perks={perks}, compiled localization keys={words.Count(s => s.StartsWith("vm_"))}, missing={missing}; text rendering/registration not executed.");
        return errors + missing == 0 ? 0 : 1;
    }

    internal static void PrintRegistrations(string owner)
    {
        var keys = new List<string>(); int targets = 0;
        foreach (var original in Harmony.GetAllPatchedMethods())
        {
            var info = Harmony.GetPatchInfo(original);
            if (info == null || !info.Owners.Contains(owner)) continue;
            targets++;
            foreach (var pair in new[] { ("prefix", info.Prefixes), ("postfix", info.Postfixes), ("transpiler", info.Transpilers), ("finalizer", info.Finalizers) })
            foreach (var patch in pair.Item2.Where(p => p.owner == owner))
                keys.Add($"{original.DeclaringType?.FullName}:{original}:{pair.Item1}:{patch.PatchMethod.DeclaringType?.FullName}:{patch.PatchMethod}");
        }
        var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).ToArray();
        Console.WriteLine($"REGISTRATION targets={targets}, patches={keys.Count}, duplicate registrations={duplicates.Length}");
        foreach (var duplicate in duplicates) Console.WriteLine("DUPLICATE " + duplicate.Key);
    }
}
