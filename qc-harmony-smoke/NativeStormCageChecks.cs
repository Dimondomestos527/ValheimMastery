using Mono.Cecil;

internal static class NativeStormCageChecks
{
    // Structural checks only: no native Unity renderer or network is executed.
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition M(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(string type, string method, string target, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == target && r.Name == name);
        bool Text(string type, string method, string value) => M(type, method).Body.Instructions.Any(i => i.Operand is string s && s == value);
        int Constant(string type, string name) => Convert.ToInt32(Type(type).Fields.Single(f => f.Name == name).Constant);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }

        Check(Constant("NativeStormWeatherVisual", "ParticleBudget") <= 500 &&
            Constant("NativeStormWeatherVisual", "FlakeBudget") + Constant("NativeStormWeatherVisual", "ClusterBudget") == Constant("NativeStormWeatherVisual", "ParticleBudget"),
            "native weather has an aggregate per-zone budget below500");
        Check(Calls("Magic70StormZone", "Visual", "NativeStormWeatherVisual", "Create") &&
            !Calls("Magic70StormZone", "Visual", "NativeSnowVisualAssets", "CreateSnow") &&
            !Calls("Magic70StormZone", "Visual", "ParticleSystem", "SetParticles"),
            "storm replaces manual dot/ring lattice with native weather emitters");
        Check(Calls("NativeStormWeatherVisual", "Create", "Object", "Instantiate") &&
            Calls("NativeStormWeatherVisual", "Create", "NativeStormWeatherVisual", "StripControllers") &&
            !M("NativeStormWeatherVisual", "Create").Body.Instructions.Any(i => i.Operand is GenericInstanceMethod g &&
                g.Name == "AddComponent" && g.GenericArguments.Any(t => t.Name == "ParticleSystem")),
            "weather clones native emitters instead of adding bare particle systems");
        Check(Calls("NativeStormWeatherVisual", "FindEmitter", "Material", "GetTexturePropertyNames") &&
            Calls("NativeStormWeatherVisual", "FindEmitter", "MaterialPropertyBlock", "GetTexture"),
            "snow selection requires actual shader texture bindings, not material-name-only evidence");
        Check(Calls("NativeStormWeatherVisual", "CopyPropertyBlocks", "Renderer", "GetPropertyBlock") &&
            Calls("NativeStormWeatherVisual", "CopyPropertyBlocks", "Renderer", "SetPropertyBlock"),
            "native renderer property blocks survive isolation including per-material bindings");
        Check(Text("NativeStormWeatherVisual", "FindSource", "Assets/Effects/weather/SnowStorm.prefab") &&
            !Calls("NativeStormWeatherVisual", "FindSource", "Resources", "FindObjectsOfTypeAll"),
            "weather uses exact catalog asset and Mountain environment references without a broad resource scan");
        Check(Calls("NativeStormWeatherVisual", "Update", "ParticleSystem", "GetParticles") &&
            !M("NativeStormWeatherVisual", "Update").Body.Instructions.Any(i => i.Operand is MethodReference r &&
                new[] { "set_startColor", "set_startSize", "set_velocity" }.Contains(r.Name)),
            "bounded escape trimming does not overwrite native appearance or motion");
        Check(Calls("Magic70StormZone", "ClearVisual", "NativeStormWeatherVisual", "Stop") &&
            Calls("NativeStormWeatherVisual", "Stop", "Object", "Destroy"),
            "weather cleanup stops emission and destroys the cosmetic tree");

        Check(Calls("Magic70BloodDome", "ReceiveRupture", "ZDO", "GetOwner") &&
            Calls("Magic70BloodDome", "ReceiveRupture", "Guid", "TryParseExact") &&
            Calls("Magic70BloodDome", "ReceiveRupture", "Dictionary`2", "TryGetValue"),
            "rupture presentation contains owner validation, bounded token parsing and duplicate guard");
        var release = M("Magic70BloodCageHold", "Release").Body.Instructions.ToList();
        int rupture = release.FindIndex(i => i.Operand is string s && s == "VM_BloodCageRupture");
        int damage = release.FindIndex(i => i.Operand is MethodReference r && r.DeclaringType.Name == "Character" && r.Name == "Damage");
        Check(rupture >= 0 && damage > rupture, "owner broadcasts native rupture presentation before the release damage call");
        Check(Text("Magic70BloodDome", "BreakFeedback", "fx_StaffShield_Break") &&
            !Text("Magic70BloodCageHold", "Release", "fx_StaffShield_Hit") &&
            M("Magic70BloodDome", "BreakFeedback").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_breakEffects"),
            "rupture uses actual SE_Shield break effects instead of shield-hit placeholder");
        Check(Text("Magic70BloodDome", "CastFeedback", "fx_shield_start") &&
            Calls("Magic70BloodDome", "CastFeedback", "PerkAudioService", "PlayPrefab"),
            "cast orb and independent sound use actual shield staff references");
        Check(!Calls("Magic70BloodCageVisual", "TryCreate", "Material", "SetColor") &&
            !Calls("Magic70BloodCageVisual", "TryCreate", "MainModule", "set_loop") &&
            Calls("Magic70BloodCageVisual", "TryCreate", "Magic70BloodDome", "BoundParticles"),
            "shell preserves native gradients and loop modes with aggregate particle budget");
        Check(!Calls("Magic70BloodDome", "ReceiveRupture", "Character", "Damage") &&
            !Calls("Magic70BloodCageBurst", "TryPlay", "Character", "Damage") &&
            Calls("Magic70BloodCageBurst", "TryPlay", "Magic70BloodDome", "BreakFeedback"),
            "client rupture snapshot is presentation only and survives target destruction");
        Console.WriteLine(count + " native storm/cage STATIC checks passed; visual appearance and dedicated-owner delivery LIVE_TEST_REQUIRED.");
        return 0;
    }
}
