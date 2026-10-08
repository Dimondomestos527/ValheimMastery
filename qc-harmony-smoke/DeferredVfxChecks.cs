using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class DeferredVfxChecks
{
    // IL contract checks only. These cannot execute Unity activation/rendering.
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition T(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition M(string type, string name) => T(type).Methods.Single(m => m.Name == name);
        IEnumerable<TypeDefinition> Tree(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Tree));
        IEnumerable<MethodDefinition> OwnerMethods(string type) => Tree(T(type)).SelectMany(t => t.Methods);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        bool Call(string type, string method, string target, string name) => Calls(M(type, method), target, name);
        bool CallbackCall(string type, string target, string name) => OwnerMethods(type).Any(m => Calls(m, target, name));
        bool Field(MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == name);
        bool Stores(MethodDefinition method, string name) => method.Body.Instructions.Any(i =>
            i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == name);
        int Index(MethodDefinition method, string type, string name) => method.Body.Instructions.ToList().FindIndex(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int Constant(string type, string name) => Convert.ToInt32(T(type).Fields.Single(f => f.Name == name).Constant);
        bool Float(string type, string method, float value) => M(type, method).Body.Instructions.Any(i => i.Operand is float f && f == value);
        bool FalseArgument(MethodDefinition method, string target, string name) => Calls(method, target, name) &&
            method.Body.Instructions.Where(i => i.Operand is MethodReference r && r.DeclaringType.Name == target && r.Name == name).All(i =>
                i.Previous?.OpCode == OpCodes.Ldc_I4_0);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }

        var create = M("PerkNativeFeedback", "CreateVisualOnly");
        Check(!Calls(create, "Object", "DestroyImmediate") && Calls(create, "DeferredNativeVfx", "Initialize") &&
            Call("VfxPool", "Spawn", "PerkNativeFeedback", "CreateVisualOnly"),
            "proc callbacks stage tunable cosmetic wrappers without immediate destruction");
        var instantiate = create.Body.Instructions.Single(i => i.Operand is MethodReference r &&
            r.DeclaringType.Name == "Object" && r.Name == "Instantiate");
        var deactivate = create.Body.Instructions.Where(i => i.Operand is MethodReference r &&
            r.DeclaringType.Name == "GameObject" && r.Name == "SetActive").ToArray();
        var contentDeactivate = deactivate.First(i => i.Offset > instantiate.Offset);
        Check(((MethodReference)instantiate.Operand).Parameters.Count == 3 &&
            deactivate.Count(i => i.Offset < instantiate.Offset) >= 2 &&
            contentDeactivate.Previous?.Previous?.OpCode == OpCodes.Ldloc_1 &&
            deactivate.All(i => i.Previous?.OpCode == OpCodes.Ldc_I4_0) &&
            Index(create, "DeferredNativeVfx", "Initialize") > create.Body.Instructions.IndexOf(contentDeactivate),
            "native clone is instantiated below an inactive wrapper and explicitly kept inactive before staging");

        var prepare = M("DeferredNativeVfx", "Prepare");
        int strip = Index(prepare, "PerkNativeFeedback", "Sanitize"), enable = Index(prepare, "GameObject", "SetActive");
        int prepared = prepare.Body.Instructions.ToList().FindIndex(i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.Name == "Prepared");
        Check(Call("DeferredNativeVfx", "Prepare", "NativeVfxSafeFrame", "get_IsDraining") &&
            strip >= 0 && prepared > strip && enable > prepared &&
            !Call("DeferredNativeVfx", "OnEnable", "PerkNativeFeedback", "Sanitize") &&
            Field(M("DeferredNativeVfx", "OnEnable"), "Prepared"),
            "wrapper activation cannot awaken native content before safe-frame sanitation succeeds");
        Check(Call("NativeVfxFrameDriver", "LateUpdate", "NativeVfxSafeFrame", "Pump") &&
            Call("NativeVfxSafeFrame", "Pump", "DeferredNativeVfx", "Prepare") &&
            M("NativeVfxSafeFrame", "Pump").Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally) &&
            module.Types.SelectMany(t => t.Methods).Where(m => Calls(m, "DeferredNativeVfx", "Prepare")).All(m =>
                m.DeclaringType.Name == "NativeVfxSafeFrame" && m.Name == "Pump"),
            "native preparation has one bounded LateUpdate drain and clears its context guard in finally");
        Check(Call("PerkNativeFeedback", "Sanitize", "NativeVfxSafeFrame", "get_IsDraining") &&
            Call("PerkNativeFeedback", "Sanitize", "GameObject", "get_activeInHierarchy") &&
            Call("PerkNativeFeedback", "Sanitize", "Object", "DestroyImmediate") &&
            module.Types.SelectMany(t => t.Methods).Where(m => Calls(m, "PerkNativeFeedback", "Sanitize")).All(m =>
                m.DeclaringType.Name == "DeferredNativeVfx" && m.Name == "Prepare"),
            "immediate stripping refuses active content and is unreachable from proc callbacks");
        var strips = M("PerkNativeFeedback", "Sanitize").Body.Instructions.Where(i => i.Operand is GenericInstanceMethod g &&
            g.Name == "GetComponentsInChildren").Select(i => ((GenericInstanceMethod)i.Operand).GenericArguments[0].Name).ToArray();
        Check(new[] { "MonoBehaviour", "ZNetView", "AudioSource", "Collider", "Rigidbody" }.All(strips.Contains) &&
            strips.Count(s => s == "MonoBehaviour") >= 2 && strips.Count(s => s == "AudioSource") >= 2,
            "sanitation verifies native scripts, audio and physics components are actually gone before activation");
        Check(Call("DeferredNativeVfx", "Prepare", "PerkNativeFeedback", "Fail") &&
            Call("DeferredNativeVfx", "Prepare", "DeferredNativeVfx", "Abort") &&
            Call("DeferredNativeVfx", "Abort", "Object", "Destroy") &&
            !Call("DeferredNativeVfx", "Abort", "Object", "DestroyImmediate") &&
            Field(M("DeferredNativeVfx", "Abort"), "Aborted") &&
            Call("DeferredNativeVfx", "Abort", "VfxPoolLease", "Cancel"),
            "failed sanitation hides and normally destroys the wrapper, with idempotent never-enabled lease cleanup");
        Check(Field(create, "FailedPrefabs") && Call("PerkNativeFeedback", "Fail", "HashSet`1", "Add") &&
            Call("PerkNativeFeedback", "Fail", "Object", "GetInstanceID"),
            "failed source cache prevents recurring inactive orphan allocations in the same scene");
        Check(Constant("NativeVfxSafeFrame", "PendingLimit") == 64 && Constant("NativeVfxSafeFrame", "PerFrameLimit") == 12 &&
            Constant("VfxPool", "ActiveLimit") == 48 && Constant("VfxPool", "PerPrefabLimit") == 12 &&
            Float("NativeVfxSafeFrame", "Request", 1.5f) && Float("NativeVfxSafeFrame", "Pump", .1f),
            "staging, per-frame work, active leases, cached instances and asynchronous asset retries are finite");
        Check(Call("PerkNativeFeedback", "PlayVfx", "NativeVfxSafeFrame", "Request") &&
            Call("VfxRecipeService", "Spawn", "NativeVfxSafeFrame", "Request") &&
            !Call("NativeVfxSafeFrame", "Pump", "NativeSoftVisualAssets", "Release"),
            "pending SoftRef procs get bounded retries without releasing live native asset references");
        var bindings = M("PerkNativeFeedback", "CopyRendererBindings");
        Check(new[] { 1, 2 }.All(n => bindings.Body.Instructions.Any(i => i.Operand is MethodReference r &&
            r.DeclaringType.Name == "Renderer" && r.Name == "GetPropertyBlock" && r.Parameters.Count == n)) &&
            Calls(bindings, "Renderer", "SetPropertyBlock"),
            "native renderer property blocks survive staging including per-material texture/atlas bindings");
        Check(Call("PerkNativeFeedback", "CreateVisualOnly", "PerkNativeFeedback", "ConfigureLights") &&
            Call("VfxPool", "Spawn", "VfxPoolBaseline", "Capture"),
            "bounded native light values are configured before the reusable baseline is captured");

        Check(Call("VfxPoolBaseline", "RestartParticles", "DeferredNativeVfx", "get_IsPrepared") &&
            Stores(M("VfxPoolBaseline", "RestartParticles"), "_restartPending") &&
            Call("VfxPoolBaseline", "FinishPendingRestart", "VfxPoolBaseline", "RestartParticles") &&
            Call("VfxPoolBaseline", "StopParticles", "MainModule", "set_playOnAwake") &&
            !Call("VfxPoolBaseline", "StopParticles", "ParticleSystem", "Play"),
            "first-use Stop/Restart intent is retained without native autoplay racing deferred playback");
        var restart = M("VfxPoolBaseline", "RestartParticles");
        Check(FalseArgument(restart, "ParticleSystem", "Clear") && FalseArgument(restart, "ParticleSystem", "Play") &&
            Index(M("DeferredNativeVfx", "BeginPlayback"), "VfxPoolBaseline", "FinishPendingRestart") <
            Index(M("DeferredNativeVfx", "BeginPlayback"), "Action", "Invoke"),
            "queued native bursts restart once per system before custom emission instead of clearing it afterwards");
        Check(!Stores(M("VfxPoolLease", "Arm"), "_returnAt") && Stores(M("VfxPoolLease", "NotifyReady"), "_returnAt") &&
            Call("VfxPoolLease", "NotifyReady", "GameObject", "get_activeInHierarchy") &&
            Call("VfxPoolLease", "NotifyReady", "DeferredNativeVfx", "get_IsPrepared") &&
            Call("VfxPoolLease", "Cancel", "VfxPoolLease", "ReleaseCount"),
            "pooled lifetime starts at usable activation, while cancellation releases a never-enabled lease");
        Check(Call("RoadRhythmPhaseVisual", "EmitPhase", "NativeVfxSafeFrame", "AfterReady") &&
            !Call("RoadRhythmPhaseVisual", "EmitPhase", "ParticleSystem", "Emit") &&
            Call("RoadRhythmPhaseVisual", "EmitPhase", "NativeLandingBurst", "Schedule") &&
            !Call("RoadRhythmPhaseVisual", "EmitReady", "ParticleSystem", "Emit"),
            "native road scheduler follows safe readiness without manually synthesized dust");

        Check(Call("GoldForgeCosmetics", "Burst", "NativeVfxSafeFrame", "AfterReady") &&
            Call("GoldForgeImpactRing", "Play", "NativeVfxSafeFrame", "AfterReady") &&
            Call("GoldForgeRisingEmbers", "Play", "NativeVfxSafeFrame", "AfterReady") &&
            CallbackCall("GoldForgeCosmetics", "ParticleSystem", "Play"),
            "first-use native forge bursts and custom particles only play after staged content is usable");
        Check(Call("GoldForgeImpactRing", "LateUpdate", "DeferredNativeVfx", "get_IsPrepared") &&
            Call("GoldForgeRisingEmbers", "LateUpdate", "DeferredNativeVfx", "get_IsPrepared") &&
            Call("GoldForgeCosmeticLifetime", "Arm", "NativeVfxSafeFrame", "AfterReady") &&
            !Stores(M("GoldForgeCosmeticLifetime", "Arm"), "_until") &&
            OwnerMethods("GoldForgeCosmeticLifetime").Any(m => m.HasBody && Stores(m, "_until")),
            "custom SetParticles is gated on readiness and its visible lifetime clock starts in the ready callback");
        Check(Call("GoldForgeCosmetics", "Cancel", "GoldForgeCosmeticLifetime", "Release") &&
            Call("GoldForgeCosmeticLifetime", "OnDestroy", "GoldForgeCosmeticLifetime", "Release") &&
            Call("GoldForgeImpactRing", "Play", "GoldForgeCosmetics", "Cancel") &&
            Call("GoldForgeRisingEmbers", "Play", "GoldForgeCosmetics", "Cancel"),
            "failed custom-particle initialization releases cosmetic capacity even before first activation");
        Check(Call("IceTrailVisual", "PulseShards", "NativeVfxSafeFrame", "AfterReady") &&
            !Call("IceTrailVisual", "PulseShards", "ParticleSystem", "Play") &&
            CallbackCall("IceTrailVisual", "ParticleSystem", "Play") &&
            Constant("IceTrailVisual", "ShardVisualCapacity") == 6 &&
            Constant("GoldForgeImpactRing", "Segments") == 96 && Constant("GoldForgeRisingEmbers", "Capacity") == 128,
            "first-use ice shards defer playback while existing shard/forge visual budgets stay unchanged");

        Console.WriteLine(count + " deferred VFX STATIC checks passed; Unity callback safety, actual visible bursts and cleanup LIVE_TEST_REQUIRED.");
        return 0;
    }
}
