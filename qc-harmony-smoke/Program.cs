using System.Reflection;
using HarmonyLib;
using Mono.Cecil;

var root = AppContext.BaseDirectory;
if (args.Length > 0 && args[0] == "workshop-atomic") return WorkshopAtomicChecks.Run();
if (args.Length > 0 && args[0] == "workshop-planner-experiment") return WorkshopPlannerExperiment.Run();
if (args.Length > 0 && args[0] == "processing-batches") return ProcessingBatchChecks.Run();
var managed = @"C:\ValheimModDev\valheim_Data\Managed";
if (args.Contains("--server")) managed = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed";
string inspectedAssembly = Path.Combine(managed, "assembly_valheim.dll");
int assemblyArgument = Array.IndexOf(args, "--assembly");
if (assemblyArgument >= 0 && assemblyArgument + 1 < args.Length) inspectedAssembly = Path.GetFullPath(args[assemblyArgument + 1]);
var core = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\core";
var plugin = string.Empty;
int pluginArgument = Array.IndexOf(args, "--plugin");
if (pluginArgument >= 0 && pluginArgument + 1 < args.Length) plugin = Path.GetFullPath(args[pluginArgument + 1]);
if (string.IsNullOrEmpty(plugin) || !File.Exists(plugin))
    throw new ArgumentException("QC requires explicit --plugin pointing to an existing rebuilt DLL; no default or stale fallback is permitted.");
if (args.Length > 2 && args[0] == "inspect-plugin-method")
{
    using var module = ModuleDefinition.ReadModule(plugin);
    var type = module.Types.First(t => t.Name == args[1]);
    foreach (var method in type.Methods.Where(m => args.Skip(2).Contains(m.Name)))
    {
        Console.WriteLine(method.FullName);
        if (method.HasBody) foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
    }
    return 0;
}
if (args.Length > 0 && args[0] == "forge-safety") return CookingTransferChecks.Forge(args.Length > 1 ? args[1] : plugin);
if (args.Length > 0 && args[0] == "magic70") return Magic70Checks.Run(plugin);
if (args.Length > 0 && args[0] == "current123-tooling") return Current123ToolingContracts.Run(plugin);
if (args.Length > 0 && args[0] == "native-storm-cage") return NativeStormCageChecks.Run(plugin);
if (args.Length > 0 && args[0] == "summon-raven119") return SummonRaven119Checks.Run(plugin);
if (args.Length > 0 && args[0] == "carrier119") return Carrier119Checks.Run(plugin, managed);
if (args.Length > 0 && args[0] == "patch120") return Patch120Checks.Run(plugin);
if (args.Length > 0 && args[0] == "patch121") return Patch121Checks.Run(plugin);
if (args.Length > 0 && args[0] == "patch122") return Patch122Checks.Run(plugin);
if (args.Length > 0 && args[0] == "patch123") return Patch123Checks.Run(plugin, managed);
if (args.Length > 0 && args[0] == "native-landing") return NativeLandingChecks.Run(plugin);
if (args.Length > 0 && args[0] == "native-movement-presentation") return NativeMovementPresentationChecks.Run(plugin);
if (args.Length > 0 && args[0] == "deferred-vfx") return DeferredVfxChecks.Run(plugin);
if (args.Length > 0 && args[0] == "patch113") return Patch113Checks.Run(plugin);
if (args.Length > 0 && args[0] == "patch114") return Patch114Checks.Run(plugin);
if (args.Length > 0 && args[0] == "patch115") return Patch115Checks.Run(plugin);
if (args.Length > 0 && args[0] == "patch116") return Patch116Checks.Run(plugin);
if (args.Length > 0 && args[0] == "gold100") return Gold100Checks.Run(plugin);
if (args.Length > 0 && args[0] == "combat-input") return CombatInputChecks.Run(plugin);
if (args.Length > 0 && args[0] == "maul-fishing-riding") return MaulFishingRidingChecks.Run(plugin);
if (args.Length > 1 && args[0] == "inspect-find")
{
    using var module = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
    foreach (var type in module.Types)
        foreach (var field in type.Fields)
            if ((type.Name + " " + field.Name).Contains(args[1], StringComparison.OrdinalIgnoreCase)) Console.WriteLine($"{type.Name}: {field.FieldType.FullName} {field.Name}");
    return 0;
}
if (args.Length > 1 && args[0] == "inspect-field-global")
{
    using var module = ModuleDefinition.ReadModule(inspectedAssembly);
    foreach (var type in module.Types.Concat(module.Types.SelectMany(t => t.NestedTypes)))
        foreach (var method in type.Methods.Where(m => m.HasBody))
            if (method.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == args[1]))
                Console.WriteLine(method.FullName);
    return 0;
}
if (args.Length > 1 && args[0] == "inspect-type")
{
    using var module = ModuleDefinition.ReadModule(inspectedAssembly);
    var type = module.Types.Concat(module.Types.SelectMany(t => t.NestedTypes)).First(t => t.Name == args[1] || t.FullName == args[1]);
    Console.WriteLine($"TYPE {type.FullName} valueType={type.IsValueType}");
    foreach (var field in type.Fields) Console.WriteLine($"FIELD {field.FieldType.FullName} {field.Name}");
    foreach (var method in type.Methods) Console.WriteLine($"METHOD {method.FullName}");
    return 0;
}
if (args.Length > 0 && args[0] == "inspect-method")
{
    using var module = ModuleDefinition.ReadModule(inspectedAssembly);
    var type = module.Types.Concat(module.Types.SelectMany(t => t.NestedTypes)).First(t => t.Name == args[1] || t.FullName == args[1]);
    foreach (var method in type.Methods.Where(m => args.Skip(2).Contains(m.Name)))
    {
        Console.WriteLine(method.FullName);
        Console.WriteLine(string.Join(", ", method.Parameters.Select(p => p.Name + ":" + p.ParameterType)));
        if (method.HasBody) foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
    }
    return 0;
}
if (args.Length > 0 && args[0] == "inspect-field-refs")
{
    using var module = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
    var type = module.Types.First(t => t.Name == args[1]);
    foreach (var method in type.Methods.Where(m => m.HasBody))
        if (method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f &&
            f.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine(method.FullName);
    return 0;
}
if (args.Length > 0 && args[0] == "inspect-method-refs")
{
    using var module = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
    foreach (var type in module.Types)
        foreach (var method in type.Methods.Where(m => m.HasBody))
            if (method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference called &&
                called.DeclaringType.Name == args[1] && called.Name == args[2]))
                Console.WriteLine(method.FullName);
    return 0;
}
if (args.Length > 0 && args[0] == "inspect-projectile")
{
    using var module = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
    var projectile = module.Types.First(type => type.Name == "Projectile");
    Console.WriteLine("FIELDS");
    foreach (var field in projectile.Fields) Console.WriteLine($"{field.FieldType.FullName} {field.Name}");
    foreach (var methodName in new[] { "Setup", "OnHit", "FixedUpdate", "LateUpdate", "Update" })
    {
        foreach (var method in projectile.Methods.Where(method => method.Name == methodName))
        {
            Console.WriteLine($"METHOD {method.FullName}");
            if (!method.HasBody) continue;
            foreach (var instruction in method.Body.Instructions)
                Console.WriteLine($"  {instruction.Offset:X4}: {instruction.OpCode} {instruction.Operand}");
        }
    }
    return 0;
}
AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var name = new AssemblyName(args.Name).Name + ".dll";
    foreach (var folder in new[] { managed, core, Path.GetDirectoryName(plugin)! })
    {
        var path = Path.Combine(folder, name);
        if (File.Exists(path)) return Assembly.LoadFrom(path);
    }
    return null;
};
try
{
    var assembly = Assembly.LoadFrom(plugin);
    if (args.Length > 0 && args[0] == "clean-static") return CleanStaticChecks.Run(assembly);
    if (args.Length > 0 && args[0] == "forge-transpiler") return ForgeTranspilerChecks.Run(assembly, managed);
    if (args.Length > 0 && args[0] == "cooking-forge") return CookingTransferChecks.Runtime(assembly, plugin);
    if (args.Length > 0 && args[0] == "network-manifest") return NetworkManifestChecks.Run(assembly);
    if (args.Length > 0 && args[0] == "magic-foundation") return MagicFoundationChecks.Run(plugin, managed);
    if (args.Length > 0 && args[0] == "wood-stack") return WoodStackChecks.Run(plugin, managed, assembly);
    if (args.Length > 0 && args[0] == "movement") return MovementChecks.Run(plugin, assembly);
    if (args.Length > 0 && args[0] == "workshop-boundaries")
        return WorkshopBoundaryChecks.Run(plugin, assembly);
    if (args.Length > 0 && args[0] == "spear70-boundaries")
        return SpearHookChecks.Run(plugin, assembly);
    if (args.Length > 0 && args[0] == "feast-persistence")
        return MasterFeastPersistenceChecks.Run(assembly);
    if (args.Length > 0 && args[0] == "audit-static")
        return AuditPatchChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "approved-sword-polearm")
        return ApprovedSwordPolearmChecks.Run(assembly);
    if (args.Length > 0 && args[0] == "approved-knives")
        return ApprovedKnifeChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "approved-axe-bow")
        return ApprovedAxeBowVisualChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "polearm-visual-polish")
        return PolearmVisualPolishChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "player-feedback-followup")
        return PlayerFeedbackFollowupChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "native-feedback")
        return NativeFeedbackChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "canonical-bonus")
        return CanonicalBonusFeedbackChecks.Run(plugin, Path.Combine(managed, "assembly_valheim.dll"), assembly);
    if (args.Length > 0 && args[0] == "remaining-visual-boundaries")
        return RemainingVisualBoundaryChecks.Run(plugin);
    if (args.Length > 0 && args[0] == "club-release-selection")
    {
        if (assembly.GetType("ValheimMastery.ClubWeaponClassService") == null ||
            assembly.GetType("ValheimMastery.SledgeHammerSkillRadiusPatch") == null ||
            assembly.GetType("ValheimMastery.SpearsThrowPassiveBalance") != null ||
            assembly.GetType("ValheimMastery.Fishing70Service") != null ||
            assembly.GetType("ValheimMastery.Crossbows70Service") != null ||
            assembly.GetType("ValheimMastery.ShieldWeaponClassService") != null ||
            assembly.GetType("ValheimMastery.Spear70HookDiagnostics") != null ||
            assembly.GetType("ValheimMastery.SpearPinned70Service") == null)
            throw new Exception("Club-only release contains an unfinished branch or omits a required legacy spear path.");
        if (assembly.GetType("ValheimMastery.NetworkSync")?.GetMethod("SendSpearSkillAudit", BindingFlags.Static | BindingFlags.NonPublic) != null)
            throw new Exception("Experimental spear RPC leaked into release-safe networking.");
        Console.WriteLine("PASS: club-only 1.4.79 release selection.");
        return 0;
    }
    if (args.Length > 0 && args[0] == "spear35-static")
    {
        foreach (var name in new[]
        {
            "SpearThrowLifecycleService", "SpearThrowSetupTrackingPatch",
            "SpearThrowDropContextPatch", "SpearThrowDroppedItemTrackingPatch",
            "SpearThrowImpactTrackingPatch", "Spear35RecallBlockInputPatch",
            "SpearLandedRecallController", "SpearTetherVisualController"
        })
            if (assembly.GetType("ValheimMastery." + name) == null)
                throw new Exception("Spear35 experimental route is missing " + name);
        if (assembly.GetType("ValheimMastery.SpearPinned70Service") != null)
            throw new Exception("Legacy spear pin is active alongside experimental recall.");
        var debug = assembly.GetType("ValheimMastery.SpearThrowLifecycleService")!;
        if (debug.GetMethod("DebugSummary", BindingFlags.Static | BindingFlags.NonPublic) == null)
            throw new Exception("Spear trace is unavailable for live identity verification.");
        var recall = debug.GetMethod("RecallOnBlockPress", BindingFlags.Static | BindingFlags.NonPublic);
        if (recall?.GetParameters().Length != 1 || recall.GetParameters()[0].ParameterType.Name != "Player")
            throw new Exception("Spear block recall is not scoped to the local player.");
        Console.WriteLine("PASS: Spear35 block recall hooks present; legacy Spear70 pin absent. Live item/ownership tests still required.");
        return 0;
    }
    if (args.Length > 0 && args[0] == "contracts")
    {
        int checkedCount = 0, errors = 0;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var type in assembly.GetTypes())
        {
            var attributes = type.GetCustomAttributes<HarmonyPatch>().Select(a => a.info).ToArray();
            if (attributes.Length == 0) continue;
            var targetType = attributes.Select(a => a.declaringType).LastOrDefault(t => t != null);
            var name = attributes.Select(a => a.methodName).LastOrDefault(n => n != null);
            var parameters = attributes.Select(a => a.argumentTypes).LastOrDefault(p => p != null);
            if (targetType == null || name == null) continue;
            var methods = targetType.GetMethods(flags).Where(m => m.Name == name && (parameters == null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters))).ToArray();
            if (methods.Length != 1) { Console.WriteLine($"ERROR {type.Name}: target {targetType.Name}.{name} matches={methods.Length}"); errors++; continue; }
            var original = methods[0]; checkedCount++;
            foreach (var patch in type.GetMethods(flags).Where(m => new[] { "Prefix", "Postfix", "Finalizer" }.Contains(m.Name)))
                foreach (var parameter in patch.GetParameters())
                {
                    if (parameter.Name == "__instance")
                    {
                        var acceptedType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
                        if (original.IsStatic || !acceptedType.IsAssignableFrom(original.DeclaringType!))
                        {
                            Console.WriteLine($"ERROR {type.Name}.{patch.Name}: __instance {acceptedType.Name} cannot safely receive every {original.DeclaringType!.Name} instance");
                            errors++;
                        }
                        continue;
                    }
                    if (parameter.Name.StartsWith("__")) continue;
                    var argument = original.GetParameters().FirstOrDefault(p => p.Name == parameter.Name);
                    if (argument == null) { Console.WriteLine($"ERROR {type.Name}.{patch.Name}: missing argument {parameter.Name} in {original}"); errors++; }
                }
        }
        Console.WriteLine($"Contracts checked={checkedCount}, errors={errors}");
        return errors == 0 ? 0 : 1;
    }
    if (args.Length > 0 && args[0] == "bow70-static")
    {
        var service = assembly.GetType("ValheimMastery.OverdrawPenetrationService", true)!;
        var multiplier = service.GetMethod("DamageMultiplierForIndex", BindingFlags.NonPublic | BindingFlags.Static)!;
        var expected = new[] { 1f, 0.80f, 0.65f, 0.50f, 0.40f, 0.35f };
        for (var i = 0; i < expected.Length; ++i)
        {
            var actual = (float)multiplier.Invoke(null, new object[] { i })!;
            if (Math.Abs(actual - expected[i]) > 0.0001f) throw new Exception($"Decay mismatch index={i} actual={actual}");
        }
        if (assembly.GetType("ValheimMastery.OverdrawPayloadService") != null) throw new Exception("Legacy ammo payload type is still compiled.");
        if (assembly.GetType("ValheimMastery.OverdrawPenetrationState") == null || assembly.GetType("ValheimMastery.Bow70ShockwaveVisual") == null) throw new Exception("Bow70 replacement types missing.");
        var phase = assembly.GetType("ValheimMastery.OverdrawPhase", true)!;
        var names = Enum.GetNames(phase);
        foreach (var required in new[] { "Idle", "Drawing", "FullDraw", "Charging", "Ready", "Released", "Cancelled" })
            if (!names.Contains(required)) throw new Exception("Missing state " + required);
        Console.WriteLine("PASS: Bow70 static invariants; decay=1/.8/.65/.5/.4/.35, legacy payload absent, explicit state machine present.");
        return 0;
    }
    if (args.Length > 0 && args[0] == "controlled-static")
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var bulk = assembly.GetType("ValheimMastery.StationBulkLoadService", true)!.GetMethod("ExtraToHalfCapacity", flags)!;
        int Batch(int capacity, int occupied) => (int)bulk.Invoke(null, new object[] { capacity, occupied })!;
        if (Batch(10, 0) != 4 || Batch(10, 5) != 4 || Batch(10, 9) != 0 || Batch(5, 0) != 2)
            throw new Exception("Crafting35 station loading must add half capacity per click, capped by remaining space.");
        var clubType = assembly.GetType("ValheimMastery.ClubWeaponClassService");
        if (clubType != null)
        {
            var classify = clubType.GetMethod("ClassifyPrefab", flags)!;
            string Club(string name) => classify.Invoke(null, new object[] { name })!.ToString()!;
            if (Club("MaceEldner") != "Mace" || Club("Porcupine") != "Mace" ||
                Club("SledgeDemolisher") != "SledgeHammer" || Club("unknown") != "UnknownClub")
                throw new Exception("Club weapon family classification is incorrect.");
        }
        var spearBalance = assembly.GetType("ValheimMastery.SpearsThrowPassiveBalance");
        if (spearBalance != null)
        {
            float Scale(string method, float level) => Convert.ToSingle(spearBalance.GetMethod(method, flags)!.Invoke(null, new object[] { level }));
            if (Math.Abs(Scale("VelocityFactor", 0f) - 1f) > .0001f ||
                Math.Abs(Scale("VelocityFactor", 100f) - 1.30f) > .0001f ||
                Math.Abs(Scale("ForceFactor", 100f) - 1.40f) > .0001f ||
                Math.Abs(Scale("ForceFactor", 200f) - 1.40f) > .0001f)
                throw new Exception("Spear throw passive scaling is incorrect or exceeds the level-100 cap.");
        }
        var sledgeBalance = assembly.GetType("ValheimMastery.SledgeHammerPassiveBalance");
        if (sledgeBalance != null)
        {
            float radius = Convert.ToSingle(sledgeBalance.GetMethod("RadiusFactor", flags)!.Invoke(null, new object[] { 100f }));
            if (Math.Abs(radius - 1.50f) > .0001f) throw new Exception("Sledge radius scaling is incorrect.");
        }
        var shieldType = assembly.GetType("ValheimMastery.ShieldWeaponClassService");
        if (shieldType != null)
        {
            var classify = shieldType.GetMethod("ClassifyTimedBlockBonus", flags)!;
            string Shield(float bonus) => classify.Invoke(null, new object[] { bonus })!.ToString()!;
            if (Shield(1f) != "Heavy" || Shield(1.01f) != "ParryCapable")
                throw new Exception("Shield classification diverges from Valheim's timed-parry boundary.");
        }
        var sword = assembly.GetType("ValheimMastery.SwordQuickSecondaryBalance", true)!;
        float Value(string name) => Convert.ToSingle(sword.GetField(name, flags)!.GetRawConstantValue());
        // The user's later manual correction slowed Sword35 and strengthened it.
        // Preserve current approved mechanics, not the superseded 1.4.75 draft.
        if (Math.Abs(Value("AnimationSpeed") - 1.20f) > .0001f ||
            Math.Abs(Value("DamageMultiplier") - 1.35f) > .0001f ||
            Math.Abs(Value("StaminaMultiplier") - 1f) > .0001f ||
            Math.Abs(Value("StaggerMultiplier") - 1.75f) > .0001f)
            throw new Exception("Sword35 constants do not match the current slower/stronger correction.");
        if (assembly.GetType("ValheimMastery.Bow70ArrowProfileService") == null ||
            assembly.GetType("ValheimMastery.RuntimeAssetAuditService") == null)
            throw new Exception("Bow70 projectile profile or runtime audition tool is absent.");
        if (assembly.GetType("ValheimMastery.OverdrawPayloadService") != null)
            throw new Exception("Legacy Bow70 payload is still compiled.");
        var overdraw = assembly.GetType("ValheimMastery.Overdraw70Service", true)!;
        var grace = overdraw.GetField("ReleaseGrace", flags)!;
        if (Math.Abs(Convert.ToSingle(grace.GetRawConstantValue()) - 0.35f) > .0001f)
            throw new Exception("Bow70 release grace changed unexpectedly.");
        var consume = overdraw.GetMethod("TryConsume", flags)!;
        if (consume.GetParameters().Last().ParameterType.FullName != "ItemDrop+ItemData")
            throw new Exception("Bow70 no longer receives the exact fired ammo at launch.");
        var profile = assembly.GetType("ValheimMastery.Bow70ArrowProfileService", true)!.GetMethod("Apply", flags)!;
        if (profile.GetParameters().Last().ParameterType.FullName != "ItemDrop+ItemData")
            throw new Exception("Arrow profile no longer classifies the exact fired ammo.");
        var fishing = assembly.GetType("ValheimMastery.Fishing70Service");
        if (fishing != null &&
            (Math.Abs(Convert.ToSingle(fishing.GetField("HookWindowSeconds", flags)!.GetRawConstantValue()) - 3f) > .0001f ||
            Math.Abs(Convert.ToSingle(fishing.GetField("StaminaCostMultiplier", flags)!.GetRawConstantValue()) - .30f) > .0001f))
            throw new Exception("Fishing70 hook window or stamina reduction changed unexpectedly.");
        var crossbow = assembly.GetType("ValheimMastery.Crossbows70Service");
        if (crossbow != null && Math.Abs(Convert.ToSingle(crossbow.GetField("SecondTargetDamageMultiplier", flags)!.GetRawConstantValue()) - .75f) > .0001f)
            throw new Exception("Crossbows70 second-target damage is not 75%.");
        Console.WriteLine("PASS: Sword35, fired-ammo profile, Bow70 grace, station batches, optional spear/club/shield combat draft constants.");
        return 0;
    }
    var harmony = new Harmony("valheimmastery.qc.smoke");
    harmony.PatchAll(assembly);
    var patched = Harmony.GetAllPatchedMethods().Count(method => Harmony.GetPatchInfo(method)?.Owners.Contains("valheimmastery.qc.smoke") == true);
    CleanStaticChecks.PrintRegistrations("valheimmastery.qc.smoke");
    Console.WriteLine($"PASS: Harmony PatchAll applied. PatchedMethods={patched}");
    return patched >= 25 ? 0 : 2;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    CleanStaticChecks.PrintRegistrations("valheimmastery.qc.smoke");
    return 1;
}
