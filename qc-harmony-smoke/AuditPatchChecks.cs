using Mono.Cecil;

// Compiled-code boundary checks, not substitutes for gameplay or audio tests.
internal static class AuditPatchChecks
{
    internal static int Run(string pluginPath)
    {
        using var module = ModuleDefinition.ReadModule(pluginPath);
        int passed = 0;
        MethodDefinition Method(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference called && called.DeclaringType.Name == type && called.Name == name);
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Audit boundary regression: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }

        Check(!module.Types.Any(t => t.Name is "Crafting70RepairAllPatch" or "Crafting70StationLevelPatch"), "removed Crafting70 effects stay absent");
        Check(!module.Types.Any(t => t.Name is "Cooking70FoodStatePatch" or "Cooking70FeastCarryPatch"), "dead Feast hooks stay absent");
        Check(Calls(Method("CraftingXpPatch", "Postfix"), "CraftingOutcomeSnapshot", "HasSuccessfulOutput"), "craft XP checks successful output");
        Check(Calls(Method("Crafting35TransactionPatch", "Postfix"), "CraftingOutcomeSnapshot", "HasSuccessfulOutput"), "upgrade feedback checks successful output");
        MethodDefinition mine = Method("Pickaxes70SuperHitPatch", "Postfix");
        Check(!mine.Parameters.Any(p => p.Name == "__result") && mine.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "HealthBefore"), "superhit not gated by destroyed-only result");
        Check(Calls(mine, "Collider", "ClosestPoint") && Calls(mine, "MineRock5", "DamageArea"), "same-vein hit areas use collider geometry and vanilla damage");
        Check(!Calls(Method("ProcessingStationOutputPatch", "Prefix"), "NetworkSync", "SendStationXp") &&
            Calls(Method("ProcessingStationOutputPatch", "Postfix"), "NetworkSync", "SendStationXp"), "processing XP follows output inspection");
        Check(!Calls(Method("AxeTargetVisualService", "ApplyExecution"), "AxeTargetVisualService", "ShowExecutionStatus") &&
            Calls(Method("AxeTargetVisualService", "SendExecution"), "NetworkSync", "SendProcFeedback"), "execution broadcast does not grant observer HUD buff");
        Check(Calls(Method("MasteryOwnedVfxMaterials", "OnDestroy"), "Object", "Destroy"), "owned material cleanup retained");
        Check(Calls(Method("MasterFeastConsumePatch", "Prefix"), "Cooking70FeastServingPatch", "GetServing") &&
            Calls(Method("Cooking70FeastServingPatch", "Prefix"), "ItemData", "Clone"), "placed Feast isolates serving metadata");
        Console.WriteLine($"PASS: audit compiled-code boundaries {passed}/10. LIVE_TEST_REQUIRED.");
        return 0;
    }
}
