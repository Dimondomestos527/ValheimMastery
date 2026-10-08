using Mono.Cecil;
using Mono.Cecil.Cil;

// Compiled boundaries only: no claim of live appearance, audibility or measured FPS.
internal static class PolearmVisualPolishChecks
{
    internal static int Run(string pluginPath)
    {
        using var module = ModuleDefinition.ReadModule(pluginPath);
        int passed = 0;
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference called && called.DeclaringType.Name == type && called.Name == name);
        bool HasFloat(MethodDefinition method, float value) => method.Body.Instructions.Any(i =>
            i.OpCode.Code == Code.Ldc_R4 && i.Operand is float number && Math.Abs(number - value) < 0.00001f);
        bool UsesField(MethodDefinition method, string name) => method.Body.Instructions.Any(i =>
            i.Operand is FieldReference field && field.Name == name);
        bool HasString(MethodDefinition method, string value) => method.Body.Instructions.Any(i =>
            i.OpCode.Code == Code.Ldstr && i.Operand is string text && text == value);
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Polearm visual regression: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }

        MethodDefinition update = Method("PolearmSpinVisual", "Update");
        Check(Calls(update, "Transform", "set_localRotation") && !Calls(update, "Mathf", "Sin") &&
            !Calls(update, "Mathf", "Cos") && !Calls(update, "LineRenderer", "SetPosition") &&
            !Calls(update, "LineRenderer", "SetPositions") && !Calls(update, "PerkNativeFeedback", "PlayVfx") &&
            !Calls(update, "Object", "Instantiate"), "spin frame loop rotates cached geometry without spawning");
        MethodDefinition stacks = Method("PolearmSpinVisual", "SetStacks");
        Check(HasFloat(stacks, 0.20f) && Calls(stacks, "Mathf", "Clamp") &&
            Calls(stacks, "PolearmSpinVisual", "SetRingRadius") && Calls(stacks, "LineRenderer", "SetPositions"),
            "spin perimeter keeps original stack-scaled damage radius");
        MethodDefinition pulse = Method("PolearmSpinVisual", "Pulse");
        Check(UsesField(pulse, "NextWindAt") && UsesField(pulse, "EnablePerkProcVFX") &&
            HasFloat(pulse, 0.90f) && HasFloat(pulse, 0.65f) &&
            Calls(pulse, "PolearmSpinVisual", "ReleaseOwnedWind") && Calls(pulse, "PerkNativeFeedback", "PlayVfx"),
            "native spin accent is VFX-gated and throttled beyond its lifetime");
        Check(HasString(pulse, "fx_fallenfalkyrie_spin") &&
            !HasString(Method("PolearmBurstVisualService", "Play"), "fx_fallenfalkyrie_spin") &&
            !HasString(pulse, "fallenvalkyrie_spin"), "spin uses cosmetic native prefab, never gameplay attack prefab");
        MethodDefinition release = Method("PolearmSpinVisual", "ReleaseOwnedWind");
        Check(Calls(release, "Transform", "get_parent") && Calls(release, "Object", "op_Equality") &&
            Calls(release, "Object", "Destroy"), "expired/reused pooled wind lease is protected by parent ownership");
        Check(UsesField(Method("PolearmSpinVisual", "Initialize"), "EnablePerkProcVFX") &&
            Calls(update, "PolearmSpinVisual", "ReleaseOwnedWind"), "VFX disabled hides initial geometry and releases owned accent");
        MethodDefinition soundPulse = Method("PolearmSpinVisualService", "Pulse");
        Check(Calls(soundPulse, "PerkAudioService", "Play") && !UsesField(soundPulse, "EnablePerkProcVFX"),
            "polearm audio remains independent of the VFX switch");
        MethodDefinition cleanup = Method("PolearmSpinVisual", "OnDestroy");
        Check(UsesField(cleanup, "OuterMaterial") && UsesField(cleanup, "SweepMaterial") &&
            Calls(cleanup, "Object", "Destroy"), "spin owns and disposes its two cloned line materials");
        MethodDefinition burstUpdate = Method("PolearmBurstVisual", "Update");
        MethodDefinition burstRing = Method("PolearmBurstVisual", "UpdateRing");
        Check(HasFloat(burstUpdate, 0.38f) && Calls(burstUpdate, "PolearmBurstVisual", "UpdateRing") &&
            Calls(burstRing, "LineRenderer", "SetPositions") && !Calls(burstRing, "Mathf", "Sin") &&
            !Calls(burstRing, "Mathf", "Cos") && !Calls(burstUpdate, "PerkNativeFeedback", "PlayVfx"),
            "phantom arc retains original duration with cached geometry and no frame spawning");
        Check(Math.Abs(Convert.ToDouble(Type("Polearms70ContinuousSpinService").Fields.Single(f =>
            f.Name == "BasePeriod").Constant) - 0.92d) < 0.00001d &&
            HasFloat(Method("Polearms70ContinuousSpinService", "DoAreaPulse"), 0.35f) &&
            HasFloat(Method("Polearms70ContinuousSpinService", "DoAreaPulse"), 0.20f) &&
            HasFloat(Method("Polearms70ContinuousSpinService", "Tick"), 0.30f) &&
            HasFloat(Method("Polearms70ContinuousSpinService", "Tick"), 0.40f),
            "existing spin cadence, damage, stagger, radius, stamina and speed values unchanged");
        Console.WriteLine($"PASS: polearm visual boundaries {passed}/10. LIVE_TEST_REQUIRED.");
        return 0;
    }
}
