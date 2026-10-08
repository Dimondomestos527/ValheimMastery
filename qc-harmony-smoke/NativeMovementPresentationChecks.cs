using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NativeMovementPresentationChecks
{
    // Source/IL contracts, never proof of readable pixels in a live Unity scene.
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition T(string type) => module.Types.Single(t => t.Name == type);
        MethodDefinition M(string type, string method) => T(type).Methods.Single(m => m.Name == method);
        IEnumerable<TypeDefinition> Tree(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Tree));
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        bool Call(string type, string method, string target, string name) => Calls(M(type, method), target, name);
        bool AnyCall(string type, string target, string name) => Tree(T(type)).SelectMany(t => t.Methods).Any(m => Calls(m, target, name));
        bool Text(string type, string method, string text) => M(type, method).Body.Instructions.Any(i => i.Operand is string s && s == text);
        int Constant(string type, string field) => Convert.ToInt32(T(type).Fields.Single(f => f.Name == field).Constant);
        int Index(MethodDefinition method, string type, string name) => method.Body.Instructions.ToList().FindIndex(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        bool LegacyRecipe(string id, params float[] values)
        {
            var ops = M("VfxRecipeService", ".cctor").Body.Instructions;
            for (int i = 0; i < ops.Count - 5; i++)
                if (ops[i].Operand is string text && text == id && values.Select((v, n) =>
                    ops[i + n + 1].Operand is float actual && actual == v).All(ok => ok) &&
                    ops[i + 5].Operand is MethodReference method && method.Name == "AddLegacyRun") return true;
            return false;
        }

        Check(Call("VfxRecipeService", "Play", "RoadRhythmPhaseVisual", "Set") &&
            Text("RoadRhythmPhaseVisual", "EmitPhase", "jump_70_air") &&
            Call("RoadRhythmPhaseVisual", "EmitPhase", "NativeMovementPulse", "Schedule"),
            "Natural Run phase feedback reaches the selected air-jump reference-size selective scheduler");
        Check(Text("RoadRhythmPhaseVisual", "ResolveLandingPrefab", "fx_perfectdodge") &&
            Text("NativeMovementPulse", "Schedule", "run_70_stack1") && Text("NativeMovementPulse", "Schedule", "run_70_stack3"),
            "Run donor identity and separate phase ring/streak policy are explicit");
        Check(Call("NativeLandingBurst", "Schedule", "MainModule", "get_scalingMode") &&
            Call("NativeLandingBurst", "Schedule", "MainModule", "set_scalingMode") &&
            Call("NativeLandingBurst", "Schedule", "MainModule", "get_maxParticles") &&
            M("NativeLandingBurst", "EmitReady").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "PreserveNativeSettings") &&
            !AnyCall("NativeLandingBurst", "MainModule", "set_startColor") &&
            !AnyCall("RoadRhythmVisual", "RunSpeedWakeVisual", "Create"),
            "Legacy landing retains native settings and Run keeps sustained wind removed");
        Check(Call("VfxRecipeService", "Tune", "VfxPoolBaseline", "get_NativeRoot") &&
            M("VfxPoolBaseline", "Capture").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_nativeScale") &&
            M("VfxPoolBaseline", "Restore").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "_nativeScale") &&
            Call("NativeLandingBurst", "EmitReady", "ParticleSystem", "Play"),
            "legacy root-scale acts on original native content transform and restores, while native delayed/rate/burst emission replays after readiness");

        Check(Text("VfxRecipeService", "AddMovement", "fx_perfectdodge") &&
            Text("NativeMovementPulse", "Schedule", "ring_gradient") && Text("NativeMovementPulse", "Schedule", "pixel_additive") &&
            Call("NativeMovementPulse", "Schedule", "ParticleSystemRenderer", "get_renderMode"),
            "air-jump selects audited native textured ring and stretched streak renderer, not placeholder cards");
        Check(Constant("NativeMovementPulse", "ParticleBudget") == 81 && Constant("NativeContactSparks", "ParticleBudget") == 32 &&
            Call("NativeBurstPlayback", "NativeCount", "EmissionModule", "GetBurst") &&
            Call("NativeBurstPlayback", "NativeCount", "MinMaxCurve", "Evaluate"),
            "air-jump/contact counts derive native t0 bursts within81/32 hard budgets");
        Check(Call("NativeMovementPulse", "Schedule", "NativeBurstPlayback", "Mute") &&
            Call("NativeMovementPulse", "Schedule", "NativeVfxSafeFrame", "AfterReady") &&
            !Call("NativeMovementPulse", "Schedule", "ParticleSystem", "Emit") &&
            Call("NativeMovementPulse", "EmitReady", "NativeBurstPlayback", "EmitOnce"),
            "first-use air-jump mutes automatic emission before deferred activation and emits only after readiness");
        var emit = M("NativeBurstPlayback", "EmitOnce");
        Check(Index(emit, "ParticleSystem", "Stop") < Index(emit, "ParticleSystem", "Clear") &&
            Index(emit, "ParticleSystem", "Clear") < Index(emit, "EmissionModule", "set_enabled") &&
            Index(emit, "EmissionModule", "set_enabled") < Index(emit, "ParticleSystem", "Play") &&
            Index(emit, "ParticleSystem", "Play") < Index(emit, "ParticleSystem", "Emit"),
            "one-shot replay cannot double the native automatic burst");
        Check(Call("NativeBurstPlayback", "Mute", "Renderer", "set_enabled") &&
            Call("NativeBurstPlayback", "Mute", "Light", "set_intensity") &&
            Call("NativeBurstPlayback", "Mute", "MainModule", "set_maxParticles"),
            "nonselected native renderers, lights and particle buffers stay finite and muted");
        Check(Call("Jump70AirJumpPatch", "Prefix", "NativeMovementPulse", "PlayAirJump") &&
            Call("Jump70AirJumpPatch", "Prefix", "Character", "ForceJump") &&
            !Call("Jump70AirJumpPatch", "Prefix", "PerkFeedbackService", "Play"),
            "air jump changes presentation only and retains native jump path with no repeated HUD toast");
        Check(Call("NativeGroundPressure", "Play", "Physics", "Raycast") &&
            Call("NativeGroundPressure", "Spawn", "NativeBurstPlayback", "HasMaterial") &&
            Text("NativeGroundPressure", "Spawn", "block_wave") &&
            Call("NativeGroundPressure", "Spawn", "MainModule", "get_startSize") &&
            !Call("NativeGroundPressure", "Spawn", "Mesh", "get_bounds"),
            "ground pressure uses a real nearby surface and native horizontal-billboard start-size diameter");
        Check(Call("NativeGroundPressure", "Spawn", "NativeBurstPlayback", "Mute") &&
            Call("NativeGroundPressure", "Spawn", "NativeVfxSafeFrame", "AfterReady") &&
            AnyCall("NativeGroundPressure", "NativeBurstPlayback", "EmitOnce") &&
            Call("NativeGroundPressure", "OnDisable", "MonoBehaviour", "StopAllCoroutines"),
            "outer/core native wave emits after readiness with bounded lifetime and reuse cancellation");
        Check(M("WeaponImpactVisualQueue", "QueueHammerImpact").Body.Instructions.Count(i =>
            i.Operand is MethodReference r && r.Name == "Enqueue") == 1 &&
            M("WeaponImpactVisualQueue", "Flush").Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld &&
                i.Operand is FieldReference f && f.Name == "InnerRadius") &&
            !AnyCall("WeaponImpactVisualQueue", "VfxPool", "Spawn"),
            "one hammer attack owns outer+inner geometry instead of a victim-body full demolition duplicate");
        Check(!AnyCall("Hammer35DustRing", "ParticleSystem", "SetParticles") &&
            !AnyCall("NativeGroundPressure", "ParticleSystem", "SetParticles") &&
            !AnyCall("NativeGroundPressure", "Renderer", "set_sharedMaterial"),
            "no synthetic spark circle, replacement material, or full demolition composition");
        foreach (string property in new[] { "startColor", "alignment", "enabled" })
        {
            string owner = property == "startColor" ? "MainModule" : property == "alignment" ? "ParticleSystemRenderer" : "Renderer";
            Check(Call("VfxPoolBaseline", "Capture", owner, "get_" + property) &&
                Call("VfxPoolBaseline", "Restore", owner, "set_" + property),
                "pooled native " + property + " restores after movement/wave instance-only tuning");
        }
        Console.WriteLine(count + " native movement/impact presentation STATIC checks passed; readable pixels LIVE_TEST_REQUIRED.");
        return 0;
    }
}
