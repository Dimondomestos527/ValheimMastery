using Mono.Cecil;
using Mono.Cecil.Cil;

// Boundaries for the approved Axe/Bow35 visual-only maintenance pass.
// These checks do not assert live visibility, audible sound, or measured FPS.
internal static class ApprovedAxeBowVisualChecks
{
    internal static int Run(string pluginPath)
    {
        using var module = ModuleDefinition.ReadModule(pluginPath);
        int passed = 0;
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference called && called.DeclaringType.Name == type && called.Name == name);
        bool HostWorldGuard(string methodName, string applyName)
        {
            var instructions = Method("AxeTargetVisualService", methodName).Body.Instructions;
            int applyIndex = instructions.ToList().FindIndex(i => i.Operand is MethodReference called && called.Name == applyName);
            return Enumerable.Range(0, Math.Max(0, applyIndex - 2)).Any(i =>
                instructions[i].Operand is FieldReference f && f.Name == "m_localPlayer" &&
                instructions[i + 1].OpCode.Code == Code.Ldnull &&
                instructions[i + 2].Operand is MethodReference comparison && comparison.Name == "op_Inequality");
        }
        bool StatusRequiresLocalOwner()
        {
            var instructions = Method("AxeTargetVisualService", "SendExecution").Body.Instructions;
            int statusIndex = instructions.ToList().FindIndex(i => i.Operand is MethodReference called && called.Name == "ShowExecutionStatus");
            return statusIndex >= 4 && Enumerable.Range(2, statusIndex - 2).Any(i =>
                instructions[i - 2].OpCode.Code == Code.Ldarg_0 &&
                instructions[i - 1].Operand is FieldReference f && f.Name == "m_localPlayer" &&
                instructions[i].Operand is MethodReference comparison && comparison.Name == "op_Equality" &&
                instructions[i + 1].OpCode.FlowControl == FlowControl.Cond_Branch &&
                instructions[i + 1].Operand is Instruction skip && skip.Offset > instructions[statusIndex].Offset);
        }
        double Constant(string type, string name) => Convert.ToDouble(Type(type).Fields.Single(f => f.Name == name).Constant);
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception("Approved combat visual regression: " + name);
            Console.WriteLine("PASS: " + name);
            passed++;
        }

        Check(Constant("Axe35Service", "Duration") == 180d && Constant("Axe35Service", "MaxStacks") == 3d &&
            Math.Abs(Constant("Axe35Service", "VulnerabilityPerStack") - 0.11d) < 0.000001d &&
            Constant("Axes70Service", "ReadyDuration") == 7d && Constant("Axes70Service", "ExecutionChance") == 0.25d,
            "approved axe stack and execution balance unchanged");
        Check(Constant("Bows70WeakPointService", "MarkDuration") == 5d &&
            Math.Abs(Constant("Bows70WeakPointService", "HeavyHitRadius") - 0.95d) < 0.000001d &&
            Constant("Bows70WeakPointService", "MaxAimAngle") == 12d,
            "Bow35 mark lifetime and aiming/hit geometry unchanged");
        MethodDefinition markerUpdate = Method("BowWeakPointMarker", "Update");
        Check(!Calls(markerUpdate, "Player", "IsDrawingBow") && !Calls(markerUpdate, "Humanoid", "GetCurrentWeapon") &&
            Calls(markerUpdate, "BowWeakPointMarker", "RefreshSurfacePoint"),
            "released-arrow mark continues tracking target surface");
        MethodDefinition ring = Method("BowWeakPointMarker", "RefreshRing");
        Check(Calls(ring, "LineRenderer", "SetPositions") && !Calls(ring, "Mathf", "Sin") && !Calls(ring, "Mathf", "Cos"),
            "Bow35 ring uses cached circle geometry and one renderer upload");
        Check(Type("BowWeakPointMarker").Methods.Where(m => m.Name == "Initialize" && m.Parameters.Count >= 4)
            .All(m => Calls(m, "BowWeakPointMarker", "RefreshRing")),
            "Bow35 ring initialized before first render");
        Check(!Calls(Method("AxeTargetVisualService", "ApplyExecution"), "AxeTargetVisualService", "ShowExecutionStatus") &&
            Calls(Method("AxeTargetVisualService", "SendExecution"), "NetworkSync", "SendProcFeedback") && StatusRequiresLocalOwner(),
            "shared axe execution visuals retain owner-only status feedback");
        Check(HostWorldGuard("SendStacks", "ApplyStacks") && HostWorldGuard("SendExecutionReady", "ApplyReady") &&
            HostWorldGuard("SendExecution", "ApplyExecution"), "host displays remote-player shared axe visuals");
        int surfaceIndex = markerUpdate.Body.Instructions.ToList().FindIndex(i =>
            i.Operand is MethodReference called && called.Name == "RefreshSurfacePoint");
        int visualIndex = markerUpdate.Body.Instructions.ToList().FindIndex(i =>
            i.Operand is MethodReference called && called.Name == "RefreshRing");
        Check(surfaceIndex >= 0 && visualIndex > surfaceIndex &&
            !markerUpdate.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "EnablePerkProcVFX") &&
            ring.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "EnablePerkProcVFX"),
            "VFX setting cannot bypass Bow35 surface tracking");
        Console.WriteLine($"PASS: approved Axe/Bow35 visual boundaries {passed}/8. LIVE_TEST_REQUIRED.");
        return 0;
    }
}
