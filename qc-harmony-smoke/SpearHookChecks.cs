using System.Reflection;
using Mono.Cecil;

internal static class SpearHookChecks
{
    internal static int Run(string plugin, Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        bool Field(MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == name);
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
        var request = Method("Spear70HookService", "Request");
        Check(Calls(request, "ZNet", "IsServer") && Calls(request, "ZNet", "GetPeer"), "request bound to server and real peer");
        Check(Calls(request, "OwnerSkillAuthority", "Has"), "approved owner-skill trust used for authorization");
        Check(Calls(request, "ZDO", "GetOwner") && Calls(request, "ZDOID", "get_UserID"), "drop requires peer ownership and creator identity");
        Check(Calls(request, "BaseAI", "IsEnemy") && Calls(request, "Character", "IsPlayer"), "hostile non-player target gate");
        Check(Field(request, "Cooldowns") && Field(request, "Active") && Field(request, "MoverUid"), "server cooldown and one authorized motion owner");
        Check(Calls(Method("Spear70HookService", "Command"), "ZNet", "GetServerRPC"), "clients accept only their server commands");
        Check(Calls(Method("Spear70HookService", "Protocol"), "ZNet", "GetServerRPC"), "server capability confirmation authenticated");
        Check(Calls(Method("Spear70HookService", "Track"), "Spear70HookService", "get_Ready"),
            "attachment cannot immobilize spear before compatible server confirmation");
        Check(Field(request, "MotionPeers"), "remote creature owner must support motion protocol before dispatch");
        Check(Calls(Method("Spear70HookService", "Heavy"), "Character", "IsBoss"), "boss chooses grapple instead of target displacement");
        var step = Method("SpearHookMotion", "Step");
        Check(Calls(step, "ZNetView", "IsOwner") && Calls(step, "Character", "IsTeleporting"), "movement aborts on ownership loss and teleport");
        Check(Calls(step, "Physics", "CapsuleCastNonAlloc") && Calls(step, "Rigidbody", "MovePosition"), "bounded collision-swept Rigidbody motion");
        Check(!Calls(step, "Transform", "set_position"), "character motion does not teleport transform");
        Check(Calls(Method("SpearHookOwnerMotionPatch", "Postfix"), "SpearHookMotion", "StepFor") &&
            Calls(Method("SpearHookMotion", "StepFor"), "SpearHookMotion", "Step"), "motion runs after vanilla Character fixed update");
        Check(Calls(Method("Spear70Attachment", "Return"), "SpearThrowLifecycleService", "TryRecall"), "completion uses original-item recall route");
        Check(!Calls(Method("SpearThrowLifecycleService", "RecallOnBlockPress"), "Spear70HookService", "TryBegin"),
            "block recalls only; no implicit hook-to-recall fallback");
        var input = Method("Spear35RecallBlockInputPatch", "Prefix");
        Check(input.Parameters.Any(p => p.Name == "secondaryAttack" && p.ParameterType.IsByReference) &&
            Calls(input, "Spear70HookService", "TryBegin"), "secondary input is consumed exclusively by hook");
        Check(Field(Method("Spear70Attachment", "TryUse"), "_queued") &&
            Calls(Method("Spear70Attachment", "Reply"), "Spear70Attachment", "TryUse"),
            "early secondary queues until server confirms attachment");
        Check(Calls(Method("SpearLandedRecallController", "FixedUpdate"), "Humanoid", "Pickup"), "only native Pickup inserts returned item");
        var generated = Type("Spear70Attachment").NestedTypes.SelectMany(t => t.Methods).Where(m => m.HasBody);
        Check(generated.Any(m => Calls(m, "Character", "StartAttack") && Calls(m, "Humanoid", "GetRightItem") &&
            (Calls(m, "Object", "ReferenceEquals") || m.Body.Instructions.Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ceq ||
                i.OpCode == Mono.Cecil.Cil.OpCodes.Bne_Un || i.OpCode == Mono.Cecil.Cil.OpCodes.Bne_Un_S))),
            "arrival thrust requires equipped exact item and actual StartAttack");
        Check(!Type("Spear70HookService").Methods.Concat(Type("SpearHookMotion").Methods).Where(m => m.HasBody)
            .Any(m => Calls(m, "Character", "Damage")), "server hook does not invent direct damage packets");
        var retry = Method("Spear70HookService", "Retry");
        Check(!Field(retry, "Hooks") && Calls(retry, "Spear70HookService", "Reply"), "retry preserves server attachment instead of deleting it");
        Check(Field(request, "MotionToken") && Calls(request, "Guid", "NewGuid"), "each movement attempt has fresh token; stale completion cannot terminate a retry");
        Check(Calls(input, "Spear70HookService", "OwnsSecondary"), "whole valid tether consumes secondary input, not only active motion");
        Check(Calls(Method("Spear70Attachment", "Reply"), "PerkCooldownStateService", "ReduceRemaining"), "authenticated failed motion refunds only spear cooldown");
        Check(Calls(Method("Spear70HookService", "Tick"), "Spear70HookService", "ValidAttachment"), "server sweeps terminal attachment conditions instead of idle expiry");
        bool enabled = (bool)assembly.GetType("ValheimMastery.Spear70HookService")!
            .GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Console.WriteLine("Spear70 experiment enabled=" + enabled + ". Static boundaries only; movement/VFX/multiplayer LIVE_TEST_REQUIRED.");
        return 0;
    }
}
