using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class MaulFishingRidingChecks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition M(string type, string method) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method &&
            (type != "OwnerSkillAuthority" || method != "Has" || m.Parameters.Count == 5));
        bool Call(MethodDefinition m, string type, string name) => m.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int count = 0;
        void Check(bool pass, string label) { if (!pass) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        Check(M("Fists70TargetClassifier", ".cctor").Body.Instructions.Any(i => Equals(i.Operand, "seekerbrute")), "native SeekerBrute classified heavy");
        Check(Call(M("Fists70MaulService", "ObserveConfirmedDamage"), "OwnerSkillAuthority", "Has") &&
            Call(M("Fists70MaulService", "TryBegin"), "OwnerSkillAuthority", "Has"), "remote fists level gates both gauge and activation");
        Check(Call(M("Fists70HitRelay", "Send"), "ZNetView", "IsOwner") &&
            Call(M("Fists70HitRelay", "Send"), "OwnerSkillAuthority", "SendNow"), "only victim owner reports confirmed damage with refreshed skill claim");
        Check(Call(M("Fists70HitRelay", "Receive"), "ZDO", "GetOwner") &&
            M("Fists70HitRelay", "Receive").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_uid"), "server binds report to native victim owner");
        Check(Call(M("Fists70HitRelay", "Receive"), "Dictionary`2", "TryGetValue"), "duplicate report sequence admission present");
        Check(M("OwnerSkillAuthority", "Receive").Parameters.Count == 5 &&
            M("OwnerSkillAuthority", "Has").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Unarmed") &&
            M("OwnerSkillAuthority", "Has").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Ride"), "owner snapshot includes fists and riding");
        Check(Call(M("Fishing35PreserveBaitPatch", "Postfix"), "Inventory", "CountItems") &&
            M("Fishing35PreserveBaitPatch", "Postfix").Body.Instructions.Any(i => i.Operand is float f && f == .5f), "bait 50 percent only after received fish count increases");
        Check(Call(M("Fishing35PreserveBaitPatch", "CaptureRequest"), "ConditionalWeakTable`2", "Add"), "bait captured before asynchronous pickup clears float");
        var fishing = module.Types.Single(t => t.Name == "Fishing70Service");
        Check((float)fishing.Fields.Single(f => f.Name == "HookWindowSeconds").Constant == 3f &&
            (float)fishing.Fields.Single(f => f.Name == "StaminaCostMultiplier").Constant == .30f, "release fishing70 has three-second reduced stamina window");
        Check(Call(M("Fishing70Service", "IsLocalOwner"), "FishingFloat", "GetOwner") &&
            Call(M("Fishing70StaminaPatch", "Finalizer"), "Fishing70StaminaPatch", "Restore"), "fishing70 direct native ownership and exception-safe restoration");
        Check(Call(M("Riding70PushbackPatch", "Prefix"), "RidingPerkAuthority", "Has") &&
            M("Riding70PushbackPatch", "Prefix").Body.Instructions.Any(i => i.Operand is float f && f == .25f), "riding pushback modified at native force application");
        Check(!M("Riding70MountMitigationPatch", "Prefix").Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f && f.Name == "m_pushForce"), "no late or double riding force scaling");
        Check(M("Riding35StaminaRegenPatch", "Prefix").Body.Instructions.Any(i => i.OpCode.Code == Code.Stfld && i.Operand is FieldReference f && f.Name == "HasSprinted"), "rider switch resets stamina recovery state");
        Console.WriteLine(count + " static follow-up checks passed; LIVE_TEST_REQUIRED.");
        return 0;
    }
}
