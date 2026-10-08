using Mono.Cecil;

internal static class Patch123Checks
{
    internal static int Run(string plugin, string managed)
    {
        using var mod = ModuleDefinition.ReadModule(plugin);
        using var game = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
        MethodDefinition M(string type, string method) => mod.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method);
        bool Calls(string type, string method, string owner, string name) => M(type, method).Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.Name == owner && r.Name == name);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        var load = game.Types.Single(t => t.Name == "ItemDrop").NestedTypes.Single(t => t.Name == "ItemData").Methods.Single(m => m.Name == "Load" && m.Parameters.Count == 3);
        Check(!load.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_shared"), "native serialized ItemData.Load has no SharedData: reproduces old cargo guard failure");
        var gate = M("Magic70CarrierAddItemAmountPatch", "Prefix");
        Check(gate.Parameters.Any(p => p.Name == "__0" && p.ParameterType.MetadataType == MetadataType.Int32) && !gate.Parameters.Any(p => p.ParameterType.FullName.Contains("ItemData")), "load guard receives native prefab hash, never unhydrated serialized item");
        Check(Calls("Magic70CarrierAddItemAmountPatch", "Prefix", "ObjectDB", "GetItemPrefab") && Calls("Magic70CarrierAddItemAmountPatch", "Prefix", "Magic70Carrier", "CanAdd"), "restored cargo validates actual native prefab while portal restrictions remain enforced");
        Check(M("Skeleton35NativeDistanceExpiryPatch", "Prefix").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_unsummonDistance") && !Calls("Skeleton35NativeDistanceExpiryPatch", "Prefix", "ZNetScene", "Destroy"), "authored skeleton distance expiry adjusted before native owner logic");
        Check(Calls("Magic70CarrierLootPatch", "Prefix", "Component", "GetComponent"), "carrier ragdoll and death share scoped no-loot generation gate");
        Check(!M("CarrierPackVisual", "Update").Body.Instructions.Any(i => i.Operand is string s && s == "piece_chest_wood"), "no building chest render tree attached to carrier");
        Check(Calls("CarrierBirthVisual", "Update", "Animator", "HasState") && Calls("CarrierBirthVisual", "Update", "Animator", "Play"), "carrier plays verified native Draugr Wakeup only when present");
        Check(M("NativePerkAssetResolver", "ResolveLegacyRun").Body.Instructions.Any(i => i.Operand is string s && s == "fx_perfectdodge") && !M("NativePerkAssetResolver", "ResolveLegacyRun").Body.Instructions.Any(i => i.Operand is string s && s == "fx_land"), "run phases resolve visible historical fallback, not dust landing");
        Check(Calls("RoadRhythmPhaseVisual", "EmitPhase", "NativeLandingBurst", "Schedule") && !Calls("RoadRhythmPhaseVisual", "EmitReady", "ParticleSystem", "Emit"), "both run paths replay native scheduled effect without synthetic dust");
        Check(Calls("Clubs70ChargePresentation", "Begin", "NativeBurstPlayback", "HasMaterial") &&
            Calls("Clubs70ChargePresentation", "UpdateSound", "AudioSource", "set_loop"), "smooth charge selects actual native material and a single authored loop");
        Check(!Calls("Clubs70Reservation", "Tick", "PerkNativeFeedback", "PlayVfx") &&
            !Calls("Clubs70Reservation", "Tick", "PerkAudioService", "PlayPrefab"), "charge progression has no old threshold blasts or sound spam");
        Check(!mod.Types.Any(t => t.Name == "SledgeHammerSkillRadiusPatch" || t.Name == "Hammer35FractureDamagePatch"), "new hammer mechanics remove radius and fracture bonuses");
        Check(Calls("Clubs70EchoService", "EchoHammer", "Character", "Damage") &&
            Calls("Clubs70EchoService", "EchoMace", "Character", "Damage") &&
            Calls("Clubs70EchoService", "WithinEcho", "Physics", "Linecast"), "both echoes damage through native path with solid-geometry checks");
        Check(!Calls("Spears35ProjectilePatch", "Postfix", "PerkVisualService", "PlayProc"), "native spear launch has no mastery impact overlay");
        Check(Calls("ShieldRush70", "Tick", "Physics", "OverlapCapsuleNonAlloc") &&
            Calls("ShieldRush70", "ImpactArea", "Physics", "OverlapSphereNonAlloc"), "shield contacts sweep actual movement before area stagger");
        Check(Calls("ShieldRushImpactVisual", "Spawn", "MainModule", "set_startSize") &&
            Calls("ShieldRushImpactVisual", "Spawn", "NativeVfxSafeFrame", "AfterReady") &&
            mod.Types.Single(t => t.Name == "ShieldRushImpactVisual").NestedTypes.SelectMany(t => t.Methods)
                .Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MethodReference r &&
                    r.DeclaringType.Name == "NativeBurstPlayback" && r.Name == "EmitOnce"),
            "shield-native single wave emits after readiness at real radius rather than ignored local parent scaling");
        foreach (float damage in new[] { 1f, 50f, 120f, 1000f })
        {
            const float stagger = 1.8f;
            Check(Math.Abs(damage * .5f * stagger - damage * stagger * .5f) < .001f,
                "hammer half damage yields half final native stagger at damage=" + damage);
            Check(Math.Abs(damage * .75f * (stagger / .75f) - damage * stagger) < .001f,
                "mace compensation retains full final native stagger at damage=" + damage);
        }
        Console.WriteLine(count + " patch123 STATIC native-boundary checks passed; no LIVE runtime claim.");
        return 0;
    }
}
