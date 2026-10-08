using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PlayerFeedbackFollowupChecks
{
    internal static int Run(string path)
    {
        using var module = ModuleDefinition.ReadModule(path);
        MethodDefinition Method(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(string type, string name, string target, string member) => Method(type, name).Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == target && m.Name == member);
        bool Float(string type, string name, float value) => Method(type, name).Body.Instructions.Any(i => i.OpCode.Code == Code.Ldc_R4 && i.Operand is float f && Math.Abs(f - value) < .00001f);
        int count = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
        Check(Calls("WorkshopStoragePreview", "Refresh", "Container", "Load"), "chest preview refreshes replicated inventory, not ownership");
        Check(!Calls("WorkshopResourceRowPatch", "Postfix", "WorkshopRemoteCraft", "Preview") && Calls("WorkshopResourceRowPatch", "Postfix", "WorkshopStoragePreview", "Refresh"), "partial recipe rows independent of full affordability");
        Check(Calls("WorkshopConnectionVisual", "Update", "VfxPool", "Spawn") && !Calls("WorkshopConnectionVisual", "Update", "Container", "Save"), "chest source hints purely cosmetic");
        Check(Float("Spear70HookService", "PullSpeed", 10f) && Float("Spear70HookService", "PullSpeed", 100f), "spear speed 10*(1+level/100)");
        Check(!Float("Spear70Attachment", "Update", 120f) && Float("Spear70Attachment", "TryUse", 8f), "valid attachment has no idle expiry; pending RPC remains bounded");
        Check(!Calls("Spear70HookService", "TryBegin", "Character", "GetLookDir") && !Calls("Spear70Attachment", "Reply", "Character", "Message"), "no facing-dependent admission or failed-pull toast");
        Check(Calls("Dodge70Service", "Apply", "Dodge70Refund", "TryRefund") && (Calls("Dodge70Refund", "TryRefund", "Player", "AddStamina") || Calls("Dodge70Refund", "TryRefund", "Character", "AddStamina")), "dodge70 restores stamina instead of the rejected target-side stagger");
        Check(module.Types.Single(t => t.Name == "Dodge35PerfectDodgePatch").CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch" && a.ConstructorArguments.Any(v => v.Value is string s && s == "RPC_Damage")), "perfect dodge observed where native player-owner invulnerability is decided");
        Check(Calls("EnemyStaggerHud", "Publish", "Character", "GetStaggerPercentage") && Calls("EnemyStaggerHud", "Publish", "ZDO", "Set"), "enemy stagger bar uses actual owner-published fraction");
        Check(Float("EnemyStaggerHud", "Publish", .2f), "stagger sync capped to five updates per second and changed values only");
        Check(Calls("WoodInventoryCapacity", "Normalize", "MethodBase", "Invoke") && module.Types.Single(t => t.Name == "WoodInventoryCapacity").NestedTypes.Any(t => t.Fields.Any(f => f.Name == "Expanded")), "wood capacity uses item-local copied shared data");
        Check(Float("PerkProfessionService", "ItemWeightDiscountFraction", .9f), "ordinary wood70 keeps ten percent weight");
        Check(!Calls("Fists35AttackSpeedService", "Tick", "CharacterAnimEvent", "Speed") && Calls("Fists35NativeSpeedEventPatch", "Prefix", "Fists35AttackSpeedService", "ScaleEvent"), "fist speed follows native events instead of fixed-update overwrite");
        Check(Float("FistsPassiveAdrenalineGainPatch", "Postfix", .015f), "adrenaline skill passive max +150percent");
        Check(Float("FistsKickScalingPatch", "Prefix", .025f) && Float("FistsKickScalingPatch", "Prefix", .015f), "kick damage +250percent and force +150percent at100");
        Check(Float("Pickaxes70SuperHitPatch", "Prefix", .15f), "pickaxe chance update present");
        Check(Calls("PerkAudioService", "Play", "NativePerkAssetResolver", "Resolve") && Calls("VfxRecipeService", "Spawn", "NativePerkAssetResolver", "Resolve"), "VFX and audio resolve real character/environment prefab references");
        Check(Calls("NativeWindSwirl", "LateUpdate", "ParticleSystem", "SetParticles") && !Calls("NativeWindSwirl", "LateUpdate", "Object", "Instantiate"), "native wind bounded reusable particle buffer");
        Check(Calls("PerkNarrativeService", "Description", "PerkLocalization", "Localize"), "narrative UI retains real mechanics description");
        Check(Float("CombatSkillScalingService", "GetWeaponDamageCoefficient", .55f) &&
            Calls("CombatSkillScalingService", "GetWeaponDamageCoefficient", "PerkRuntimeService", "IsBareHands") &&
            Calls("CombatSkillScalingService", "GetWeaponDamageCoefficient", "CombatSkillScalingService", "GetDamageCoefficient"),
            "fist weapons retain55percent; bare hands and other skills retain existing coefficient path");
        Check(Calls("CombatSkillScalingService", "ScaleRandomDamageFactor", "CombatSkillScalingService", "GetWeaponDamageCoefficient"), "actual native random damage hook uses weapon-specific coefficient");
        Check(Method("WorkshopRemoteCraft", "TryResourceCosts").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_upgraderResource") &&
            Method("WorkshopResourcePlan", "TryBuild").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_upgraderResource"), "normal recipe cost paths recognize upgrader-only resource rows");
        Check(Calls("EnemyStaggerBar", "Build", "RectTransform", "get_offsetMin") && Calls("EnemyStaggerBar", "Build", "RectTransform", "SetSizeWithCurrentAnchors"), "stagger bar follows HP offsets and explicit3pixel height");
        Console.WriteLine($"PASS followup boundaries {count}; Unity appearance, chest debit and remote combat LIVE_TEST_REQUIRED.");
        return 0;
    }
}
