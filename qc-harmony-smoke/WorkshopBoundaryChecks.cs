using System.Reflection;
using Mono.Cecil;

internal static class WorkshopBoundaryChecks
{
    internal static int Run(string plugin, Assembly assembly)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition Type(string name) => module.Types.Single(t => t.Name == name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        IEnumerable<MethodDefinition> AllMethods(TypeDefinition type) => type.Methods.Concat(type.NestedTypes.SelectMany(AllMethods));
        void Check(bool okay, string text) { if (!okay) throw new Exception(text); Console.WriteLine("PASS " + text); }
        var authority = Method("WorkshopHostCrafting", "IsAuthority");
        Check(Calls(authority, "ZNet", "IsServer") && authority.Body.Instructions.Any(i =>
            i.Operand is FieldReference f && f.Name == "m_localPlayer"), "host adapter never treats remote inventory as authoritative");
        var requirements = Method("WorkshopHostCraftRequirementsPatch", "Postfix");
        Check(requirements.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "CraftCallDepth"),
            "preview availability has explicit action-scope boundary");
        Check(Calls(Method("WorkshopHostCraftActionPatch", "Finalizer"), "WorkshopHostCrafting", "Finish"),
            "craft finalizer closes transaction on every exit");
        var finish = Method("WorkshopHostCrafting", "Finish");
        Check(Calls(finish, "CraftingOutcomeSnapshot", "HasSuccessfulOutput") && Calls(finish, "WorkshopAtomicDebit", "Commit") &&
            Calls(finish, "WorkshopAtomicDebit", "Dispose"), "commit requires actual output; failed action has rollback path");
        Check(Calls(Method("WorkshopStorageLockPatch", "Postfix"), "WorkshopAtomicDebit", "IsLocked"),
            "vanilla open/take/stack access honors uncertain-stock quarantine");
        var request = Method("WorkshopRemoteCraft", "Request");
        var pendingType = (GenericInstanceType)Type("WorkshopRemoteCraft").Fields.Single(f => f.Name == "Pending").FieldType;
        Check(pendingType.GenericArguments[0].FullName == "System.Int64" && Calls(request, "WorkshopRequestLedger", "Remember"),
            "pending actions and replay admission follow character across reconnects");
        Check(Calls(request, "ZNet", "GetPeer") && Calls(request, "WorkshopActor", "get_HasCrafting70") &&
            Calls(request, "WorkshopWorldRecords", "HasCapability") && Calls(request, "WorkshopStationProof", "Acquire") &&
            AllMethods(Type("WorkshopRemoteCraft")).Any(m => m.HasBody && Calls(m, "WorkshopChestLease", "Acquire")) &&
            Calls(Method("WorkshopRemoteCraft", "CommitRequest"), "WorkshopAtomicDebit", "Begin"),
            "draft request resolves authenticated peer, skill, station, and real debit");
        Check(Calls(Method("WorkshopRemoteCraft", "SendRequest"), "OwnerSkillAuthority", "SendNow"),
            "remote request refreshes owner levels on the same connection before authorization");
        Check(Calls(request, "WorkshopChestLease", "SamePending"), "duplicate handoff packet does not deny/cancel original client request");
        Check(Calls(request, "WorkshopDurableRequests", "Admit"), "server durably admits request before permitting a debit");
        Check(Calls(Method("WorkshopRemoteCraft", "AllowCraft"), "WorkshopRemoteCraft", "BeginSelectedCraft"),
            "ordinary craft timer routes through server instead of granting preview stock");
        Check(Calls(Method("WorkshopRemoteCraft", "Protocol"), "ZNet", "GetServerRPC") &&
            Calls(Method("WorkshopRemoteCraft", "Preview"), "WorkshopRemoteCraft", "get_Ready"),
            "remote preview requires current server protocol confirmation");
        Check(Calls(Method("WorkshopBuildRequestPatch", "Prefix"), "WorkshopBuildBridge", "BeforePlace") &&
            Calls(Method("WorkshopBuildBridge", "BeforePlace"), "WorkshopRemoteCraft", "BeginBuild"),
            "normal placement cannot bypass the chest request path");
        Check(Calls(request, "WorkshopBuildBridge", "ServerEligible") &&
            Calls(Method("WorkshopRemoteCraft", "CommitRequest"), "WorkshopBuildBridge", "ServerEligible"),
            "building position and network authorization checked before and after handoff");
        var build = Method("WorkshopRemoteCraft", "ExecuteBuild");
        int payment = build.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference r && r.Name == "PayPersonal");
        int placement = build.Body.Instructions.ToList().FindIndex(i => i.Operand is MethodReference r && r.Name == "TryPlacePiece");
        Check(payment >= 0 && placement > payment, "personal materials paid before vanilla creates building");
        Check(Calls(Method("WorkshopBuildAuthorizedPositionPatch", "Prefix"), "WorkshopRemoteCraft", "GuardBuildPlacement") &&
            Method("WorkshopRemoteCraft", "Reply").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "BuildEntered"),
            "placement cannot drift outside authorization and uncertain output is not refunded");
        Check(Calls(Method("WorkshopChestLease", "ReceiveLease"), "ZNet", "GetServerRPC"), "only server can request owner freeze");
        var leaseOwner = Method("WorkshopChestLease", "ReceiveLease");
        Check(Calls(leaseOwner, "Container", "Save") && Calls(leaseOwner, "ZDO", "SetOwner") &&
            Calls(leaseOwner, "ZDOMan", "ForceSendZDO"), "owner saves and relinquishes authority before acknowledgment");
        var commitRequest = Method("WorkshopRemoteCraft", "CommitRequest");
        Check(Calls(commitRequest, "WorkshopActor", "get_HasCrafting70") && Calls(commitRequest, "WorkshopRemoteCraft", "TryCosts") &&
            Calls(commitRequest, "WorkshopWorldRecords", "HasCapability"), "post-handoff debit revalidates skill costs and station");
        Check(module.Types.All(t => t.Name != "WorkshopRecipeCapabilityPatch") &&
            Calls(request, "WorkshopRemoteCraft", "ValidRecipeStation") &&
            Calls(commitRequest, "WorkshopRemoteCraft", "ValidRecipeStation"),
            "connected stations never merge recipe lists; exact station rechecked before debit");
        var exactStation = Method("WorkshopRemoteCraft", "ValidRecipeStation");
        Check(Calls(exactStation, "WorkshopWorldRecords", "Find") && Calls(exactStation, "Vector3", "Distance") &&
            Calls(exactStation, "WorkshopActor", "get_Position") && Calls(exactStation, "WorkshopStationProof", "Usable") &&
            exactStation.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Level") &&
            !Calls(exactStation, "ZNetScene", "FindInstance") &&
            !Calls(request, "Player", "RequiredCraftingStation") && !Calls(commitRequest, "Player", "RequiredCraftingStation"),
            "remote recipe station validated from ZDO identity, not unsynchronized Player.m_currentStation");
        Check(Calls(commitRequest, "WorkshopWorldRecords", "Chests") && Calls(commitRequest, "WorkshopRecordStore", ".ctor") &&
            Calls(commitRequest, "WorkshopRecovery", "MarkRecords") &&
            !Calls(commitRequest, "WorkshopNetworkStorage", "FindServerOwnedEligible"),
            "dedicated debit uses native world records without loaded Container dependency");
        var proof = Method("WorkshopStationProof", "ReceiveProof");
        Check(Calls(proof, "ZNet", "GetPeer") && Calls(proof, "ZDO", "GetOwner") && Calls(proof, "ZDOMan", "GetZDO") &&
            proof.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "OwnerRpc") &&
            proof.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Deadline"),
            "station proof authenticates actual owner, exact record and bounded request lifetime");
        var flushRecord = Method("WorkshopRecordStore", "Flush");
        Check(Calls(flushRecord, "WorkshopRecordStore", "CanWrite") && Calls(flushRecord, "ZDO", "Set") &&
            Calls(flushRecord, "Container", "Load") && Calls(flushRecord, "ZDOMan", "ForceSendZDO"),
            "record debit persists native stock, refreshes loaded cache and replicates it");
        Check(Calls(Method("WorkshopChestLease", "Tick"), "WorkshopLeaseRules", "Evaluate"), "runtime uses tested replicated-snapshot handoff rules");
        Check(Calls(commitRequest, "WorkshopChestLease", "KeepWarm") &&
            Method("WorkshopChestLease", "ReturnSettled").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Warm") &&
            Method("WorkshopChestLease", "PinOwner").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Warm"),
            "warm ownership is retained between atomic transactions and pinned against background reassignment");
        var warmOpen = Method("WorkshopChestLease", "ReceiveWarmOpen");
        Check(Calls(warmOpen, "WorkshopActor", "Resolve") && Calls(warmOpen, "WorkshopAtomicDebit", "IsLocked") &&
            Calls(warmOpen, "WorkshopWorldRecords", "WardAllows") && Calls(warmOpen, "WorkshopChestLease", "ReturnSettled") &&
            Calls(Method("WorkshopChestLease", "Tick"), "Container", "Load") &&
            Calls(Method("WorkshopChestLease", "Tick"), "Container", "Interact"),
            "warm chest opening authenticates proximity/access, refuses unsettled stock and reloads native inventory before interaction");
        Check(Calls(Method("WorkshopStorageLockPatch", "Postfix"), "WorkshopChestLease", "BlocksAccess") &&
            Calls(Method("WorkshopLeaseOwnershipPatch", "Prefix"), "WorkshopChestLease", "PinOwner"),
            "vanilla access and automatic ownership migration respect active lease");
        var ack = Method("WorkshopRemoteCraft", "Acknowledge");
        Check(Calls(ack, "WorkshopRecovery", "SettleRecords") && Calls(ack, "WorkshopChestLease", "ReturnSettled"),
            "world settlement precedes returning chest to live owner");
        var returnOwner = Method("WorkshopChestLease", "ReturnSettled");
        Check(Calls(returnOwner, "WorkshopAtomicDebit", "IsLocked") && Calls(returnOwner, "ZNet", "GetPeer") &&
            Calls(returnOwner, "ZDO", "GetString") && Calls(returnOwner, "ZDO", "GetBool"),
            "pending, locked or quarantined inventory cannot return to client ownership");
        Check(Calls(ack, "WorkshopRemoteCraft", "RequestPlayer") && ack.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "PlayerId"),
            "draft result bound to original peer and character");
        var actorResolve = Method("WorkshopActor", "Resolve");
        var dataResolve = Method("OwnerSkillAuthority", "ResolveCharacterData");
        Check(Calls(Method("WorkshopRemoteCraft", "RequestPlayer"), "WorkshopActor", "Resolve") &&
            Calls(actorResolve, "ZNet", "GetPeer") && Calls(actorResolve, "ZNet", "IsServer") &&
            Calls(actorResolve, "OwnerSkillAuthority", "ResolveCharacterData") &&
            !Calls(actorResolve, "OwnerSkillAuthority", "ResolvePlayer") && !Calls(dataResolve, "ZNetScene", "FindInstance"),
            "dedicated identity uses authenticated character ZDO without a loaded Player instance");
        Check(Calls(dataResolve, "ZDOMan", "GetZDO") && Calls(dataResolve, "ZDO", "GetOwner") &&
            Calls(dataResolve, "ZNetScene", "GetPrefab") && dataResolve.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_characterID"),
            "native character binding validates peer ownership and Player prefab");
        var receiveSkills = Method("OwnerSkillAuthority", "Receive");
        Check(Calls(receiveSkills, "OwnerSkillAuthority", "ResolveCharacterData") && Calls(receiveSkills, "ZDO", "GetLong") &&
            !Calls(receiveSkills, "OwnerSkillAuthority", "ResolvePlayer"), "owner skill receipt does not require a rendered character");
        Check(Calls(Method("WorkshopRemoteCraft", "StationUsable"), "Cover", "GetCoverForPoint") &&
            Method("WorkshopRemoteCraft", "StationUsable").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_haveFire"),
            "dedicated station still enforces native roof cover and fire requirements");
        Check(Calls(Method("ProcessingStationProgressionService", "ResolveSender"), "OwnerSkillAuthority", "ResolvePlayerId"),
            "sap and processing author identity supports ZDO-only dedicated characters");
        Check(Method("WorkshopRemoteCraft", "SendReply").Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.Name == "Enqueue"), "listen-host grant deferred beyond original vanilla timer call");
        Check(Calls(Method("WorkshopHostCrafting", "Begin"), "WorkshopRemoteCraft", "get_Executing"),
            "host grant cannot start a second chest debit in legacy host adapter");
        Check(Calls(Method("WorkshopRemoteCraft", "Reply"), "ZNet", "GetServerRPC"), "client grant accepts only server RPC");
        var craftValidation = Method("WorkshopRemoteCraft", "CraftCancellation");
        Check(Calls(craftValidation, "Player", "RequiredCraftingStation") && Calls(craftValidation, "Inventory", "ContainsItem") &&
            craftValidation.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "StationId") &&
            !craftValidation.Body.Instructions.Any(i => i.Operand is FieldReference f && (f.Name == "m_selectedRecipe" || f.Name == "m_selectedVariant")),
            "immutable recipe completion revalidates exact station and upgrade, not transient UI selection");
        Check(Calls(Method("WorkshopRemoteCraft", "ReceiveReply"), "ZNet", "GetServerRPC") &&
            !Calls(Method("WorkshopRemoteCraft", "ReceiveReply"), "WorkshopRemoteCraft", "Reply") &&
            Calls(Method("WorkshopRemoteCraft", "Tick"), "WorkshopRemoteCraft", "Reply"),
            "authenticated network grant executes from update after native timer unwinds");
        var buildRay = Method("WorkshopBuildRayPatch", "Prefix");
        Check(Calls(buildRay, "Physics", "Raycast") && Calls(buildRay, "Vector3", "Distance") &&
            Calls(buildRay, "Collider", "get_attachedRigidbody") &&
            !buildRay.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_placementStatus"),
            "saved build ray preserves native collision and range checks without forcing placement validity");
        Check(Calls(Method("WorkshopPendingCancelPatch", "Prefix"), "WorkshopRemoteCraft", "CancelPendingCraft") &&
            Calls(Method("WorkshopPendingStartPatch", "Prefix"), "WorkshopRemoteCraft", "get_ClientBusy"),
            "pending craft supports explicit cancel and blocks a second native timer");
        Check(Method("WorkshopRemoteCraft", "BeginSelectedCraft").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_selectedRecipe"),
            "diagnostic request captures idle selected recipe instead of timed craft state");
        var executeSelected = Method("WorkshopRemoteCraft", "ExecuteSelected");
        Check(Calls(executeSelected, "InventoryGui", "DoCrafting") && executeSelected.Body.ExceptionHandlers.Any(h => h.HandlerType == Mono.Cecil.Cil.ExceptionHandlerType.Finally),
            "granted diagnostic executes vanilla crafting with finally-restored GUI context");
        Check(Calls(Method("WorkshopRecovery", "Settled"), "ZNet", "GetServerRPC"), "receipt removal accepts only server settlement");
        Check(Calls(Method("WorkshopRecovery", "Recover"), "ZNet", "GetPeer") &&
            Calls(Method("WorkshopRecovery", "Recover"), "WorkshopReceipt", "TryDecision"), "recovery authenticates character and refuses ambiguous decision");
        Check(ack.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Failed") &&
            ack.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Decision"),
            "failed settlement cannot be retried as successful or reversed");
        var restore = Method("WorkshopInventorySnapshot", "Restore");
        foreach (string name in new[] { "m_stack", "m_quality", "m_durability", "m_customData" })
            Check(restore.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == name), "rollback restores " + name);
        var ownerType = assembly.GetType("ValheimMastery.OwnerSkillAuthority")!;
        var valid = ownerType.GetMethod("Valid", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var value in new[] { 0f, 70f, 100f }) Check((bool)valid.Invoke(null, new object[] { value }), "valid owner level " + value);
        foreach (var value in new[] { -1f, 101f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(!(bool)valid.Invoke(null, new object[] { value }), "invalid owner level rejected " + value);
        bool draftEnabled = (bool)assembly.GetType("ValheimMastery.WorkshopRemoteCraft")!
            .GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Console.WriteLine("Remote Workshop experiment enabled=" + draftEnabled + ". Not a gameplay/multiplayer test.");
        return 0;
    }
}
