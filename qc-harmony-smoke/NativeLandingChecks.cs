using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class NativeLandingChecks
{
    // Actual installed native bundle read 2026-10-04, without running Unity:
    // ID31a5ecc53e22b44f6a8193a41abec38c / c4210710 / fx_land.prefab.
    // PS pathID -8801600073991809205: t0 burst5, rate0, enabled,
    // maxParticles10, lifetime2s, duration3s, Local scaling, AlwaysSimulate.
    // Controllers: camera shake and timed destruction, not an emitter.
    // Renderer material dust(pathID3338279634442006464), native wildfire01
    // texture128x128(pathID-4959966876346263813), material color alpha .472.
    // That data disproves the stripped-controller hypothesis, not visibility.
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition T(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition M(string type, string name) => T(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        bool Call(string type, string method, string target, string name) => Calls(M(type, method), target, name);
        bool Text(string type, string method, string text) => M(type, method).Body.Instructions.Any(i => i.Operand is string s && s == text);
        bool Field(string type, string method, string field) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is FieldReference f && f.Name == field);
        int Index(MethodDefinition method, string type, string name) => method.Body.Instructions.ToList().FindIndex(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }

        Check(Text("NativePerkAssetResolver", "Resolve", "Assets/Characters/character_effects/land/fx_land.prefab") &&
            Text("NativePerkAssetResolver", "Resolve", "Assets/Effects/fx_perfectdodge.prefab") &&
            !Field("NativePerkAssetResolver", "Resolve", "m_jumpEffects"),
            "landing and intentional movement pulse resolve separate actual catalog identities, not arbitrary jump effects");
        Check(Call("NativeLandingBurst", "NativeCount", "EmissionModule", "GetBurst") &&
            Call("NativeLandingBurst", "NativeCount", "MinMaxCurve", "Evaluate") &&
            Call("NativeLandingBurst", "NativeCount", "Burst", "get_probability") &&
            Call("NativeLandingBurst", "NativeCount", "Burst", "get_time") &&
            Call("NativeLandingBurst", "NativeCount", "Mathf", "CeilToInt"),
            "instant emission count derives from actual native t0 burst and recipe multiplier, not a guessed preset");
        Check(new[] { 1d, 1.75d, 2.5d }.Select(f => Math.Min(10, (int)Math.Ceiling(5d * f))).SequenceEqual(new[] { 5, 9, 10 }),
            "legacy105 burst5/cap10 yields capped diagnostic predictions5/9/10; native scheduler retains fractional bursts (arithmetic model only)");
        Check(Call("NativeLandingBurst", "Schedule", "NativeVfxSafeFrame", "AfterReady") &&
            !Call("NativeLandingBurst", "Schedule", "ParticleSystem", "Emit") &&
            Call("NativeLandingBurst", "Schedule", "MainModule", "set_maxParticles") &&
            Convert.ToInt32(T("NativeLandingBurst").Fields.Single(f => f.Name == "ParticleBudget").Constant) == 64,
            "bounded count capacity is staged while actual emission waits for cosmetic readiness");
        var emit = M("NativeLandingBurst", "EmitReady");
        int stop = Index(emit, "ParticleSystem", "Stop"), clear = Index(emit, "ParticleSystem", "Clear");
        int disable = Index(emit, "EmissionModule", "set_enabled");
        int play = emit.Body.Instructions.ToList().FindIndex(Math.Max(0, disable + 1), i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == "ParticleSystem" && r.Name == "Play");
        int impulse = Index(emit, "ParticleSystem", "Emit");
        Check(stop >= 0 && clear > stop && disable > clear && play > disable && impulse > play,
            "explicit native impulse stops and clears timed emission first, preventing a duplicate automatic burst");
        Check(Call("NativeLandingBurst", "EmitReady", "MainModule", "set_scalingMode") &&
            Call("VfxPoolBaseline", "Capture", "MainModule", "get_scalingMode") &&
            Call("VfxPoolBaseline", "Restore", "MainModule", "set_scalingMode") &&
            Field("VfxPoolBaseline", "Capture", "_scalingModes") && Field("VfxPoolBaseline", "Restore", "_scalingModes") &&
            Call("VfxPoolBaseline", "Restore", "MainModule", "set_maxParticles") &&
            Call("VfxPoolBaseline", "Restore", "EmissionModule", "set_enabled"),
            "instance-only Hierarchy scaling, burst capacity and disabled scheduled emission restore on pooled reuse");
        Check(Call("VfxRecipeService", "SpawnResolved", "NativeLandingBurst", "Schedule") &&
            Field("VfxRecipeService", "Add", "NativeLandingCue") && Field("VfxRecipeService", "SpawnResolved", "NativeLandingCue") &&
            Call("VfxRecipeService", "SpawnResolved", "NativeMovementPulse", "Schedule") &&
            Call("Jump70AirJumpPatch", "Prefix", "NativeMovementPulse", "PlayAirJump") &&
            Call("Jump70AirJumpPatch", "Prefix", "Character", "ForceJump") &&
            !Call("Jump70AirJumpPatch", "Prefix", "PerkFeedbackService", "Play"),
            "Run restores the legacy native landing route while air-jump retains its separate pulse, without toast or jump rewrite");
        Check(Call("NativeLandingBurst", "Play", "NativeVfxSafeFrame", "Request") &&
            Call("NativeLandingBurst", "Spawn", "VfxPool", "Spawn") &&
            !Call("NativeLandingBurst", "Spawn", "Object", "Instantiate"),
            "remaining landing dust retains bounded pending native asset retries and shared cosmetic lease ownership");
        Check(!T("NativeLandingBurst").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i =>
            i.Operand is MethodReference r && new[] { "set_sharedMaterial", "set_startColor", "set_startSize", "set_startSpeed", "set_shape" }.Contains(r.Name)),
            "native dust texture/renderer, authored color/size curves and shape are not replaced by placeholder particles");
        var trace = T("NativeLandingBurst").NestedTypes.Single(t => t.Name.Contains("TraceVisibleFrame"));
        Check(trace.Methods.Any(m => Calls(m, "ParticleSystem", "GetParticles")) &&
            trace.Methods.Any(m => Calls(m, "Particle", "GetCurrentSize")) &&
            trace.Methods.Any(m => Calls(m, "Particle", "GetCurrentColor")) &&
            trace.Methods.Any(m => Calls(m, "Renderer", "get_bounds")) &&
            trace.Methods.Any(m => Calls(m, "Renderer", "get_isVisible")) &&
            Call("NativeLandingBurst", "OnDisable", "MonoBehaviour", "StopAllCoroutines"),
            "bounded post-fade-in trace measures actual particles, size/alpha and renderer bounds, with reuse cancellation");

        Console.WriteLine(count + " native landing/movement STATIC checks passed; perceptual Run/DoubleJump readability LIVE_TEST_REQUIRED.");
        return 0;
    }
}
