using Mono.Cecil;
using System.Reflection;
internal static class CookingTransferChecks
{
    internal static int Runtime(Assembly plugin, string path)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var safety = plugin.GetType("ValheimMastery.PotentialForgeSafetyPatch", true);
        var protects = safety.GetMethod("ProtectsTarget", flags);
        foreach (var row in new[] { (1,4,true), (2,4,true), (3,4,true), (4,4,false), (5,4,false), (1,2,true), (2,2,false), (5,6,true), (6,6,false), (0,4,false) })
            if ((bool)protects.Invoke(null, new object[] { row.Item1, row.Item2 }) != row.Item3) throw new Exception("Wrong forge quality boundary.");
        var stateType = plugin.GetType("ValheimMastery.PotentialForgeSafetyState", true);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var sharedType = stateType.GetField("Shared", instance).FieldType;
        var shared = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(sharedType);
        var state = Activator.CreateInstance(stateType, true);
        var chance = sharedType.GetField("m_breakChance", instance);
        var successChance = sharedType.GetField("m_upgradeChance", instance);
        successChance.SetValue(shared, .90f);
        chance.SetValue(shared, -1f);
        stateType.GetField("Shared", instance).SetValue(state, shared);
        stateType.GetField("OriginalBreakChance", instance).SetValue(state, .35f);
        stateType.GetField("OriginalUpgradeChance", instance).SetValue(state, .65f);
        stateType.GetField("Applied", instance).SetValue(state, true);
        safety.GetMethod("Restore", flags).Invoke(null, new[] { state });
        safety.GetMethod("Restore", flags).Invoke(null, new[] { state });
        if ((float)chance.GetValue(shared) != .35f || (bool)stateType.GetField("Applied", instance).GetValue(state)) throw new Exception("Forge restore is not idempotent.");
        if ((float)successChance.GetValue(shared) != .65f) throw new Exception("Forge success chance leaked outside protected upgrade.");
        // Use the actual native ItemData.Clone: this is how portable item tags
        // survive inventory cloning, not a fake dictionary-only implementation.
        var itemType = plugin.GetType("ValheimMastery.MeadDurationState", true).Assembly.GetReferencedAssemblies();
        var game = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "assembly_valheim");
        var dataType = game.GetType("ItemDrop+ItemData", true);
        var item = Activator.CreateInstance(dataType);
        var customField = dataType.GetField("m_customData", instance);
        var custom = new Dictionary<string, string> { ["valheim_mastery.cooking_tier"] = "35" };
        customField.SetValue(item, custom);
        var copy = dataType.GetMethod("Clone", instance).Invoke(item, null);
        var copied = (Dictionary<string,string>)customField.GetValue(copy);
        if (ReferenceEquals(custom, copied) || copied["valheim_mastery.cooking_tier"] != "35") throw new Exception("Food mastery tag fails native Clone.");
        using var module = ModuleDefinition.ReadModule(path);
        bool Calls(string type, string method, string calledType, string calledMethod) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method).Body.Instructions.Any(i =>
            i.Operand is MethodReference mr && mr.DeclaringType.Name == calledType && mr.Name == calledMethod);
        if (!Calls("Cooking35FoodDecayPatch", "Postfix", "Cooking35Service", "RecomputeSlowDecay") ||
            !Calls("Cooking35Service", "RecomputeSlowDecay", "CookingFoodPersistence", "Has") ||
            !Calls("Cooking35EatFeedbackPatch", "Postfix", "CookingFoodPersistence", "Record")) throw new Exception("Persisted food bonus not wired to eating/decay.");
        if (!Calls("FermenterBonusService", "End", "CookingAuthorNetwork", "StampNew") ||
            !Calls("CookingMasteryFoodStampPatch", "Postfix", "CookingAuthorNetwork", "StampNew")) throw new Exception("Actual food/mead output not stamped.");
        var mead = module.Types.Single(t => t.Name == "Cooking35MeadDurationPatch");
        if (mead.Methods.Single(m => m.Name == "Prefix").Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference f && f.Name == "m_ttl")) throw new Exception("Mead still mutates shared prefab.");
        if (!Calls("Cooking35MeadDurationPatch", "Postfix", "SEMan", "GetStatusEffect")) throw new Exception("Mead refresh does not modify actual live effect.");
        Console.WriteLine("PASS: 10 forge cap cases/idempotent restoration; native food tag Clone; persisted eaten-food wiring; actual mead/food stamps; live-effect refresh hook. LIVE_TEST_REQUIRED.");
        return 0;
    }
    internal static int Forge(string path)
    {
        using var module = ModuleDefinition.ReadModule(path);
        var type = module.Types.Single(x => x.Name == "PotentialForgeSafetyPatch");
        var prefix = type.Methods.Single(x => x.Name == "Prefix");
        var il = prefix.Body.Instructions;
        foreach (var field in new[] { "m_upgrader", "m_quality", "m_maxQuality", "m_breakChance" })
            if (!il.Any(i => i.Operand is FieldReference f && f.Name == field)) throw new Exception("Forge boundary missing: " + field);
        if (!il.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_R4 && (float)i.Operand == -1f)) throw new Exception("Zero-risk sentinel absent.");
        if (!il.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_R4 && (float)i.Operand == .90f)) throw new Exception("Protected upgrade must have 90 percent success.");
        // Vanilla reads breakChance from the first upgrader resource, NOT from
        // the upgraded weapon. Presence of a safety class alone proves nothing.
        if (!il.Any(i => i.Operand is FieldReference f && f.Name == "m_upgraderResource"))
            throw new Exception("BROKEN: safety changes upgraded weapon data, but vanilla reads breakChance from recipe's upgrader resource.");
        if (type.Methods.Any(x => x.Name == "Prepare")) throw new Exception("Unexpected conditional registration requires review.");
        if (!type.Methods.Any(x => x.Name == "Finalizer") || !type.Methods.Any(x => x.Name == "Restore")) throw new Exception("Shared-data restore absent.");
        Console.WriteLine("PASS: DLL includes active Forge safety hook: upgrader only, quality below item's maxQuality, break sentinel -1, restoration. STATIC ONLY, not a live upgrade test. " + path);
        return 0;
    }
}
