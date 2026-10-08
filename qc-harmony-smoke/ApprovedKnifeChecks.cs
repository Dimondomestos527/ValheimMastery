using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class ApprovedKnifeChecks
{
    internal static int Run(string path)
    {
        using var module = ModuleDefinition.ReadModule(path);
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        void Constant(string type, string name, float expected)
        {
            float actual = Convert.ToSingle(Type(type).Fields.Single(f => f.Name == name).Constant);
            if (MathF.Abs(actual - expected) > .00001f) throw new Exception("Knife balance changed: " + name);
        }
        Constant("AssassinBlink70Service", "Range", 20f);
        Constant("AssassinBlink70Service", "Cooldown", 12f);
        Constant("Knife70ShadowStrikeService", "DefaultChance", .35f);
        Constant("Knife70ShadowStrikeService", "SearchRadius", 10f);

        var update = Method("ShadowStrikeGuarantee", "Update");
        var instructions = update.Body.Instructions;
        int resolve = instructions.ToList().FindIndex(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "_resolved");
        int damage = instructions.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "Damage");
        int visual = instructions.ToList().FindIndex(i => i.Operand is MethodReference m && m.Name == "PlayImpact");
        if (resolve < 0 || damage <= resolve || visual <= damage ||
            !update.Body.ExceptionHandlers.Any(h => h.HandlerType == ExceptionHandlerType.Finally))
            throw new Exception("ShadowStrike no longer resolves once, cleans up on failure, and applies gameplay before visual feedback.");
        if (instructions.Any(i => i.Operand is MethodReference m && m.Name == "GetCurrentWeapon") ||
            !instructions.Any(i => i.Operand is FieldReference f && f.Name == "_backstab"))
            throw new Exception("ShadowStrike must use the original knife's backstab snapshot.");

        var echo = Method("Knife70ShadowEchoVisual", "Update");
        if (!echo.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "PlayImpact") ||
            !Method("Knife70ShadowEchoVisual", "EnsureVisual").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "EnablePerkProcVFX"))
            throw new Exception("Knife echo lost separate visual preference and delayed impact feedback paths.");
        Console.WriteLine("PASS: approved knife balance and compiled lifetime/feedback boundaries. Gameplay, visual quality and audio still require Valheim.");
        return 0;
    }
}
