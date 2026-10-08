using Mono.Cecil;

internal static class RemainingVisualBoundaryChecks
{
    internal static int Run(string pluginPath)
    {
        using var module = ModuleDefinition.ReadModule(pluginPath);
        int passed = 0;
        MethodDefinition Method(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i =>
            i.Operand is MethodReference called && called.DeclaringType.Name == type && called.Name == name);
        bool UsesField(MethodDefinition method, string name) => method.Body.Instructions.Any(i =>
            i.Operand is FieldReference field && field.Name == name);
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Visual boundary regression: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }
        var shockwave = Method("Bow70ShockwaveVisual", "Spawn");
        var instructions = shockwave.Body.Instructions.ToList();
        int sound = instructions.FindIndex(i => i.Operand is MethodReference call && call.DeclaringType.Name == "PerkAudioService" && call.Name == "Play");
        int visualGate = instructions.FindIndex(i => i.Operand is FieldReference field && field.Name == "EnablePerkProcVFX");
        Check(sound >= 0 && visualGate > sound &&
            UsesField(Method("Bow70ShockwaveVisual", "Update"), "EnablePerkProcVFX"),
            "bow shockwave hides visuals without suppressing its sound");
        Check(UsesField(Method("OverdrawRemoteTrailPatch", "Postfix"), "EnablePerkProcVFX") &&
            UsesField(Method("OverdrawRemoteTrailPatch", "Postfix"), "m_localPlayer") &&
            UsesField(Method("OverdrawArrowTrail", "Awake"), "EnablePerkProcVFX") &&
            UsesField(Method("OverdrawArrowTrail", "LateUpdate"), "EnablePerkProcVFX") &&
            Calls(Method("OverdrawArrowTrail", "LateUpdate"), "TrailRenderer", "set_emitting"),
            "arrow trails respect VFX changes and do not allocate on dedicated server");
        Check(UsesField(Method("Fists70TargetLockMarker", "Initialize"), "EnablePerkProcVFX") &&
            UsesField(Method("Fists70TargetLockMarker", "LateUpdate"), "EnablePerkProcVFX") &&
            Calls(Method("Fists70TargetLockMarker", "LateUpdate"), "Fists70TargetLockService", "IsCandidate") &&
            !UsesField(Method("Fists70TargetLockService", "Refresh"), "EnablePerkProcVFX"),
            "Maul decorative marker gate cannot disable functional target selection");
        Check(Calls(Method("AxeGeometryDebugVisual", "NewLine"), "MasteryVfxMaterial", "AssignOwned") &&
            !Calls(Method("AxeGeometryDebugVisual", "NewLine"), "MasteryVfxMaterial", "CloneFromPrefab"),
            "short-lived axe debug geometry disposes every cloned material");
        Console.WriteLine($"PASS: remaining visual boundaries {passed}/4. LIVE_TEST_REQUIRED.");
        return 0;
    }
}
