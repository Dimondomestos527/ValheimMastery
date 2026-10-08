using Mono.Cecil;

internal static class CombatInputChecks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody &&
            method.Body.Instructions.Any(i => i.Operand is MethodReference r &&
                r.DeclaringType.Name == type && r.Name == name);
        bool CallsName(MethodDefinition method, string name) => method.HasBody &&
            method.Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == name);
        bool Field(MethodDefinition method, string name) => method.HasBody &&
            method.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == name);
        bool ReturnsTrue(MethodDefinition method) => method.HasBody && method.Body.Instructions
            .Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_1) &&
            method.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ret);
        void Check(bool value, string name)
        {
            if (!value) throw new Exception(name);
            Console.WriteLine("PASS " + name);
        }

        var spearInput = Method("Spear35RecallBlockInputPatch", "Prefix");
        Check(Field(spearInput, "m_localPlayer") &&
            Calls(spearInput, "Humanoid", "GetRightItem") &&
            Calls(spearInput, "Humanoid", "GetLeftItem") &&
            Calls(spearInput, "Spear70HookService", "IsPulling") &&
            Calls(spearInput, "Spear70HookService", "OwnsSecondary"),
            "spear input suppression is local-player and both-hands-empty scoped");
        var emptyHands = Method("Spear70HookService", "EmptyHands");
        Check(Calls(emptyHands, "Humanoid", "GetRightItem") && Calls(emptyHands, "Humanoid", "GetLeftItem") &&
            Calls(Method("Spear70Attachment", "TryUse"), "Spear70HookService", "EmptyHands"),
            "queued spear use rechecks that both hands are empty");

        var fire = Method("FireStaff35Charge", "Input");
        Check(Field(fire, "m_localPlayer") && CallsName(fire, "GetCurrentWeapon") &&
            Calls(fire, "FireStaff35Charge", "IsStaff") && ReturnsTrue(fire),
            "Fire35 forwards PlayerAttackInput for nonlocal or nonmatching weapons");

        foreach (string type in new[] { "Magic70IceStorm", "Magic70Surtling", "Magic70BloodDome" })
        {
            var consumer = module.Types.SingleOrDefault(t => t.Name == type);
            if (consumer == null) continue; // Some Magic70 implementations are build-gated.
            var input = consumer.Methods.Single(m => m.Name == "Input");
            Check(CallsName(input, "GetCurrentWeapon") && ReturnsTrue(input),
                type + " forwards PlayerAttackInput for nonmatching weapons");
        }

        Console.WriteLine("Combat input checks are Cecil shape checks; cross-weapon behavior still needs a live game pass.");
        return 0;
    }
}
