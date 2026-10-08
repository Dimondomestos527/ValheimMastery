using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Carrier119Checks
{
    internal static int Run(string pluginPath, string managed)
    {
        using var plugin = ModuleDefinition.ReadModule(pluginPath);
        using var game = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
        TypeDefinition T(string name) => plugin.Types.Single(t => t.Name == name);
        MethodDefinition M(string type, string method) => T(type).Methods.Single(m => m.Name == method);
        bool Calls(string type, string method, string owner, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == owner && r.Name == name);
        int count = 0;
        void Check(bool yes, string text) { if (!yes) throw new Exception(text); Console.WriteLine("PASS " + text); count++; }
        Check(Calls("Magic70Carrier", "Register", "ZRpc", "Register") &&
            Calls("Magic70Carrier", "ProcessCast", "OwnerSkillAuthority", "Has"), "carrier RPC and verified mastery admission enabled");
        Check(Calls("Magic70Carrier", "EnqueueCast", "Magic70Carrier", "BeginFreshWorldIndex") &&
            M("Magic70Carrier", "StepWorldIndex").Body.Instructions.Count(i => i.Operand is MethodReference r &&
                r.Name == "GetAllZDOsWithPrefabIterative") == 4, "each cast refreshes canonical carrier/cargo/two skeleton indexes incrementally");
        Check(!Calls("Magic70Carrier", "ProcessCast", "ZNetScene", "FindInstance") &&
            Calls("Magic70Carrier", "ProcessCast", "BloodSkeleton35Service", "CanAdmitCarrier"), "dedicated admission does not require a loaded Player instance");
        Check(Calls("Magic70Carrier", "CountCanonicalOwnedSkeletons", "Magic70Carrier", "IsCurrent") &&
            Calls("Magic70Carrier", "CountCanonicalOwnedSkeletons", "ZDO", "GetLong") &&
            Calls("Magic70Carrier", "CountCanonicalOwnedSkeletons", "ZDO", "GetFloat"), "slot count uses current authored living canonical skeleton records");
        Check(Calls("BloodSkeleton35Service", "CanAdmitCarrier", "Mathf", "Clamp") &&
            Calls("BloodSkeleton35Service", "CanAdmitCarrier", "BloodSkeleton35Service", "Limit"), "slot capacity derives from native staff and bounded replicated quality");
        Check(Calls("Magic70CarrierNativeSkeletonLimitPatch", "Prefix", "Magic70Carrier", "OwnedSlotCount") &&
            Calls("Magic70CarrierNativeSkeletonLimitPatch", "Prefix", "Mathf", "Max"), "native post-summon replacement also reserves one carrier slot");
        Check(Calls("Magic70Carrier", "ProcessCast", "Magic70Carrier", "AddByAuthor"), "new carrier and cargo immediately enter author index");
        Check(Calls("Magic70Carrier", "AllowCargoRpc", "OwnerSkillAuthority", "ResolveCharacterData") &&
            Calls("Magic70Carrier", "AllowCargoRpc", "Magic70Carrier", "FindCarrierForCargo"), "native cargo RPC checks actor identity and linked body range");
        Check(Calls("Magic70Carrier", "AllowCargoRpc", "ZNet", "GetUID") &&
            !Calls("Magic70Carrier", "AllowCargoRpc", "ZNet", "IsServer"), "native client-owner self RPC is supported after inventory ownership transfer");
        Check(Calls("Magic70Carrier", "ReceiveClose", "WorkshopActor", "Resolve") &&
            Calls("Magic70Carrier", "ReleaseCargo", "Magic70Carrier", "RevisionReached"), "close handoff waits for final native inventory revision");
        Check(Calls("Magic70Carrier", "FollowClosedCargo", "Magic70Carrier", "FindCarrierForCargo") &&
            !Calls("Magic70Carrier", "FollowClosedCargo", "Character", "GetAllCharacters"), "closed cargo follows indexed canonical body even outside loaded server scene");
        Check(Calls("Magic70CarrierInputPatch", "Prefix", "Magic70Carrier", "Input") &&
            Calls("Magic70Carrier", "Input", "Skeleton35Travel", "get_PortalRecoveryPending"), "secondary attack is scoped and waits for portal roster recovery");
        var attack = plugin.Types.Single(t => t.Name.Contains("Carrier") && t.Name.Contains("Attack") &&
            t.Methods.Any(m => m.Name == "Prefix"));
        Check(attack.Methods.Single(m => m.Name == "Prefix").Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == "Magic70Carrier" && r.Name == "IsCarrier"), "noncombat guard recognizes only the carrier");
        Check(M("Magic70Carrier", "IsAllowedCargo").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_teleportable") &&
            M("Magic70Carrier", "IsAllowedCargo").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_questItem"), "cargo excludes nonportal and quest items regardless of world portal override");
        foreach (string gate in new[] { "Magic70CarrierAddItemPatch", "Magic70CarrierAddItemGridPatch", "Magic70CarrierAddItemPositionPatch",
            "Magic70CarrierAddGameObjectPatch", "Magic70CarrierAddItemAmountPatch", "Magic70CarrierCanAddItemPatch", "Magic70CarrierCanAddGameObjectPatch" })
            Check(Calls(gate, "Prefix", "Magic70Carrier", "CanAdd"), gate + " rejects before destination mutation");
        Check(!T("Magic70Carrier").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == "ZDOMan" && r.Name == "DestroyZDO"), "carrier lifecycle never destroys persistent cargo ZDO");
        var spawn = game.Types.Single(t => t.Name == "SpawnAbility").NestedTypes.Single(t => t.Name.Contains("<Spawn>"));
        var move = spawn.Methods.Single(m => m.Name == "MoveNext");
        var instantiate = move.Body.Instructions.Select((instruction, index) => (instruction, index)).Single(x =>
            x.instruction.Operand is GenericInstanceMethod generic && generic.Name == "Instantiate" &&
            generic.DeclaringType.Name == "Object" && generic.GenericArguments.Any(a => a.Name == "GameObject"));
        var instantiateStore = move.Body.Instructions[instantiate.index + 1];
        Check(move.Body.Variables.Count > 2 && move.Body.Variables[1].VariableType.Name == "SpawnAbility" &&
            move.Body.Variables[2].VariableType.Name == "GameObject" &&
            (instantiateStore.OpCode.Code == Code.Stloc_2 ||
             (instantiateStore.OpCode.Code == Code.Stloc && instantiateStore.Operand is VariableDefinition local && local.Index == 2)) &&
            M("Skeleton35SpawnAuthorPatch", "Transpiler").Body.Instructions.Any(i =>
                i.Operand is FieldReference f && f.DeclaringType.Name == "OpCodes" && f.Name == "Ldloc_2") &&
            M("Skeleton35SpawnAuthorPatch", "Transpiler").Body.Instructions.Any(i =>
                i.Operand is string s && s == "TagSpawnedSummon") &&
            M("Skeleton35SpawnAuthorPatch", "Transpiler").Body.Instructions.Any(i =>
                i.Operand is MethodReference r && r.DeclaringType.Name == "AccessTools" && r.Name == "Method") &&
            M("Skeleton35SpawnAuthorPatch", "Transpiler").Body.Instructions.Any(i =>
                i.Operand is FieldReference f && f.DeclaringType.Name == "OpCodes" && f.Name == "Call"),
            "friendly summon author hook binds exactly once from SpawnAbility local1 and GameObject local2 after native Instantiate");
        Check(move.Body.Variables.Count > 1 && move.Body.Variables[1].VariableType.Name == "SpawnAbility" &&
            move.Body.Instructions.Count(i => i.Operand is MethodReference r && r.DeclaringType.Name == "SpawnSystem" && r.Name == "GetNrOfInstances") == 1 &&
            M("Magic70CarrierSpawnCapPatch", "Transpiler").Body.Instructions.Any(i =>
                i.Operand is MethodReference r && r.Name == "Calls") &&
            M("Magic70CarrierSpawnCapPatch", "Transpiler").Body.Instructions.Any(i =>
                i.Operand is FieldReference f && f.DeclaringType.Name == "OpCodes" && f.Name == "Ldloc_1"), "native spawn-cap hook still matches exact owner local and single count site");
        var inventory = game.Types.Single(t => t.Name == "Inventory");
        foreach (string transfer in new[] { "MoveAll", "StackAll" })
        {
            var method = inventory.Methods.Single(m => m.Name == transfer);
            var code = method.Body.Instructions;
            int add = code.ToList().FindIndex(i => i.Operand is MethodReference r && r.DeclaringType.Name == "Inventory" && r.Name == "AddItem");
            int remove = code.ToList().FindIndex(i => i.Operand is MethodReference r && r.DeclaringType.Name == "Inventory" && r.Name == "RemoveItem");
            Check(add >= 0 && remove > add && code.Skip(add + 1).Take(remove - add - 1).Any(i =>
                i.OpCode.Code == Code.Brfalse || i.OpCode.Code == Code.Brfalse_S), transfer + " native transfer branches on destination success before source removal");
        }
        Console.WriteLine(count + " carrier119 STATIC checks passed; live inventory/portal persistence is NOT proven.");
        return 0;
    }
}
