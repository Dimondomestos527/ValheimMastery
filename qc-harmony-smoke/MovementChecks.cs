using System.Reflection;
using Mono.Cecil;

internal static class MovementChecks
{
    internal static int Run(string path, Assembly assembly)
    {
        int count = 0;
        void Check(bool pass, string label) { if (!pass) throw new Exception("Movement: " + label); count++; }
        var tier = assembly.GetType("ValheimMastery.Stride70Service")!.GetMethod("RhythmTier", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var sample in new[] { (0f, 0), (2.99f, 0), (3f, 1), (4.99f, 1), (5f, 2), (6.99f, 2), (7f, 3), (60f, 3) })
            Check((int)tier.Invoke(null, new object[] { sample.Item1 })! == sample.Item2, "rhythm boundary " + sample.Item1);
        using var module = ModuleDefinition.ReadModule(path);
        TypeDefinition Type(string n) => module.Types.Single(t => t.Name == n);
        MethodDefinition Method(string t, string n) => Type(t).Methods.Single(m => m.Name == n);
        bool Calls(string t, string n, string callee) => Method(t, n).Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == callee);
        Check(Calls("Jump70AirJumpPatch", "Prefix", "ForceJump"), "air jump must use native jump path");
        Check(Calls("Jump70AirJumpPatch", "Prefix", "ApplyStatusEffectJumpMods"), "air jump respects equipment/status jump force");
        var airJump = Type("Jump70AirJumpPatch");
        Check((float)airJump.Fields.Single(f => f.Name == "AirJumpForceScale").Constant == .8f, "air jump uses 80% vanilla vertical force");
        Check((int)Type("NativeMovementPulse").Fields.Single(f => f.Name == "ParticleBudget").Constant == 81 &&
            Method("NativeMovementPulse", "Schedule").Body.Instructions.Any(i => i.Operand is float f && f == .55f),
            "air jump uses finite native one-shot particle budget and shortened streak lifetime");
        Check(Calls("Jump70AirJumpPatch", "Prefix", "PlayAirJump") && !Calls("Jump70AirJumpPatch", "Prefix", "Proc"), "air jump shows local native burst without perk toast");
        Check(Method("VfxRecipeService", "AddMovement").Body.Instructions.Any(i => i.Operand is string prefab && prefab == "fx_perfectdodge") &&
            Method("NativeMovementPulse", "Schedule").Body.Instructions.Any(i => i.Operand is string material && material == "ring_gradient"),
            "air jump helper uses actual native dodge ring/renderer instead of low-contrast landing dust");
        Check(Calls("Swim70RestPatch", "Postfix", "ModifyStaminaRegen"), "rest uses native stamina modifiers");
        Check(Calls("Stride70Service", "WaterMotion", "UpdateWalking") && Calls("Stride70Service", "WaterMotion", "AddForce"), "physical water support plus native locomotion");
        Check(!Calls("Stride70Service", "WaterMotion", "set_position") && !Calls("Stride70Service", "WaterMotion", "set_useGravity"), "no water teleport or gravity switch");
        Check(Method("Stride70Service", "WaterMotion").Body.ExceptionHandlers.Any(h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally), "water support fields restored");
        Check(Calls("Stride70Service", "Update", "HasSolidGround") && Calls("Run70SurfaceSupportPatch", "Postfix", "HasSolidGround"), "both water exit and surface routing use real ground support, not coyote time");
        Check(Calls("Stride70Service", "HasSolidGround", "Raycast"), "solid ground support verified by bounded ray on last ground collider without world scan");
        foreach (var name in new[] { "Stride70StaminaPatch", "Swim35DrainPatch", "SwimSpeedPatch", "Sneak35DrainPatch", "MovementWalkingFieldsPatch" })
            Check(Type(name).Methods.Any(m => m.Name == "Finalizer"), name + " field restoration");
        Check(module.Types.All(t => t.Name != "Swimming35RunSpeedPatch" && t.Name != "Swimming35JogSpeedPatch" && t.Name != "Sneak70MovementPatch"), "legacy movement bonuses removed");
        Check(Calls("CombatPerks35OutgoingHitPatch", "Prefix", "GetActualSkillLevel"), "knife passive uses actual skill");
        Check(Calls("VeiledService", "Update", "Play") && !Calls("VeiledService", "Update", "PlayAtWorldPosition"), "veil feedback stays local");
        Check(Type("Dodge70UseStaminaPatch").CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch" &&
            a.ConstructorArguments.Any(v => v.Value is TypeReference t && t.Name == "Player")),
            "roll cost capture targets Player's real virtual stamina override, not the empty Character base");
        Check(Calls("Dodge70RollCapturePatch", "Finalizer", "AbortUpdate"), "roll capture restores nested scope after exceptions");
        Check(Calls("Dodge70Refund", "FinishUseStamina", "GetStamina") && Calls("Dodge70Refund", "TryRefund", "AddStamina") &&
            Calls("Dodge70Refund", "TryRefund", "TryConsume"), "refund measures actual debit and uses bounded restoration with persisted cooldown");
        Check(Method("Dodge70Refund", "TryRefund").Body.Instructions.Any(i => i.Operand is float f && f == .5f), "refund is half the admitted roll cost");
        Check(Calls("Dodge70Service", "Apply", "TryRefund") && module.Types.All(t => t.Name != "PerfectDodgeStaggerRegistration"),
            "validated perfect dodge uses refund, with no old target-side stagger RPC");
        var narratives = (System.Collections.Generic.Dictionary<string, string>)assembly.GetType("ValheimMastery.PerkNarrativeService")!
            .GetField("Ua", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Check(narratives.Count == 48 && narratives.Values.All(v => !v.Any(char.IsDigit)), "all current35/70 narrative entries are nonnumeric");
        Check(Calls("SkillsTooltipPatch", "BuildText", "Description"), "hover descriptions use the same compact narrative as panel");
        Console.WriteLine($"PASS: {count} focused movement formula/code-boundary checks; not Unity physics, AI or audiovisual verification.");
        return 0;
    }
}
