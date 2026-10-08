using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.RegularExpressions;

internal static class NativeFeedbackChecks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition m, string type, string method) => m.HasBody && m.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == method);
        void Check(bool ok, string name)
        { if (!ok) throw new Exception("Native feedback boundary: " + name); Console.WriteLine("PASS: " + name); }
        Check(!Calls(Method("PerkNativeFeedback", "CreateVisualOnly"), "Object", "DestroyImmediate") &&
            Calls(Method("PerkNativeFeedback", "Sanitize"), "Object", "DestroyImmediate") &&
            Calls(Method("DeferredNativeVfx", "Prepare"), "PerkNativeFeedback", "Sanitize") &&
            Calls(Method("VfxPool", "Spawn"), "PerkNativeFeedback", "CreateVisualOnly"), "pool stages inactive cosmetics and strips native controllers only in safe-frame preparation");
        Check(!Calls(Method("PerkAudioService", "PlayPrefab"), "Object", "Instantiate"), "audio never instantiates mixed VFX/gameplay prefab");
        Check(Convert.ToInt32(Type("VfxPool").Fields.Single(f => f.Name == "ActiveLimit").Constant) == 48,
            "shared visual active budget is bounded at 48");
        Check(Calls(Method("VfxPoolBaseline", "Restore"), "EmissionModule", "SetBursts") &&
            Calls(Method("VfxPoolBaseline", "TuneBurstEmission"), "EmissionModule", "SetBursts"), "particle burst counts restore rather than compound between leases");
        Check(Calls(Method("VfxPoolLease", "OnDisable"), "VfxPoolLease", "ReleaseCount") &&
            Calls(Method("VfxPoolLease", "OnDestroy"), "VfxPoolLease", "ReleaseCount"), "visual lease budget released on hide and destroy");
        Check(Calls(Method("MasteryVfxMaterial", "SpawnPrefab"), "PerkNativeFeedback", "PlayVfx"), "common combat visuals use bounded native helper");
        Check(!Calls(Method("RuntimeAssetAuditService", "Audition"), "Object", "Instantiate"), "audition cannot instantiate live creature/projectile gameplay");
        string manifest = @"C:\ValheimModDev\valheim_Data\StreamingAssets\SoftRef\manifest_extended";
        var assets = File.ReadLines(manifest).Select(line => Regex.Match(line, @"path in bundle: .*/([^/]+)\.prefab"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var owners = new HashSet<string>(StringComparer.Ordinal) {
            "VfxRecipeService", "MasteryVfxMaterial", "AssassinBlinkVisualService", "Knife70ShadowVisualService",
            "Knife70ShadowEchoVisual", "PolearmSpinVisualService", "PolearmSpinVisual", "PolearmBurstVisual",
            "Fists70ClientService", "Fists70ClientMaulVisual", "AxeExecutionVisual", "AxeBossExecutionVisual" };
        var used = module.Types.Where(t => owners.Contains(t.Name)).SelectMany(t => t.Methods)
            .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand)
            .Where(s => Regex.IsMatch(s, @"^(?:vfx|fx|sfx)_[A-Za-z0-9]+(?:_[A-Za-z0-9]+)*$"))
            .Distinct().Order().ToArray();
        var missing = used.Where(s => !assets.Contains(s)).ToArray();
        Check(missing.Length == 0, "all " + used.Length + " chosen native asset names exist in current client manifest: missing=" + string.Join(",", missing));
        Console.WriteLine("STATIC ONLY: actual registry resolution, appearance, audibility and FPS remain LIVE_TEST_REQUIRED.");
        return 0;
    }
}
