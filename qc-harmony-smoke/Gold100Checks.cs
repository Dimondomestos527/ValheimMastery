using Mono.Cecil;

internal static class Gold100Checks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        TypeDefinition Type(string name) => Find(module.Types, name) ?? throw new Exception("Missing type " + name);
        MethodDefinition Method(string type, string name) => Type(type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int CallAt(MethodDefinition method, string type, string name) => method.Body.Instructions
            .Select((i, n) => (i, n)).Where(x => x.i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name)
            .Select(x => x.n).DefaultIfEmpty(-1).First();
        bool HasField(MethodDefinition method, string name) => method.HasBody && method.Body.Instructions.Any(i =>
            i.Operand is FieldReference f && f.Name == name);
        bool HasString(MethodDefinition method, string text) => method.HasBody && method.Body.Instructions.Any(i =>
            i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr && String.Equals(i.Operand as string, text, StringComparison.Ordinal));
        bool HasInt(MethodDefinition method, int value) => method.HasBody && method.Body.Instructions.Any(i =>
            (i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4 && Convert.ToInt32(i.Operand) == value) ||
            i.OpCode.Code == (Mono.Cecil.Cil.Code)((int)Mono.Cecil.Cil.Code.Ldc_I4_0 + value));
        IEnumerable<MethodDefinition> AllMethods(TypeDefinition type) => type.Methods.Concat(type.NestedTypes.SelectMany(AllMethods));
        void Check(bool okay, string label)
        {
            if (!okay) throw new Exception("FAIL Gold100: " + label);
            Console.WriteLine("PASS Gold100: " + label);
        }
        static TypeDefinition Find(IEnumerable<TypeDefinition> types, string name)
        {
            foreach (var type in types)
            {
                if (type.Name == name || type.FullName == name) return type;
                var nested = Find(type.NestedTypes, name);
                if (nested != null) return nested;
            }
            return null;
        }

        var prefixType = Type("GoldDivineCraftPatch");
        var prefix = Method("GoldDivineCraftPatch", "Prefix");
        var priority = prefix.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "HarmonyPriority");
        Check(prefix.ReturnType.FullName == "System.Boolean" && priority != null &&
            priority.ConstructorArguments.Count == 1 && Convert.ToInt32(priority.ConstructorArguments[0].Value) == 900,
            "DoCrafting Harmony prefix is bool and Priority.First+100");
        var patchTarget = prefixType.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch").ToArray();
        Check(patchTarget.Length > 0 && patchTarget.Any(a => a.ConstructorArguments.Any(v => v.Value is TypeReference t && t.Name == "InventoryGui")),
            "divine interception is attached to InventoryGui");

        var intercept = Method("GoldDivineTransactions", "Intercept");
        Check(Calls(intercept, "WorkshopRemoteCraft", "get_ClientBusy") && Calls(intercept, "WorkshopRecovery", "get_Outstanding") &&
            !intercept.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.Name.Contains("Debit", StringComparison.OrdinalIgnoreCase)),
            "Gold transaction blocks on competing Workshop work and has no separate Workshop debit path");
        Check(CallAt(intercept, "GoldDivineTransactions", "Receipt") >= 0 &&
            CallAt(intercept, "GoldDivineTransactions", "Receipt") < CallAt(intercept, "GoldDivineTransactions", "Send"),
            "owner journal Request is durable before sending server request");
        var admission = Method("GoldCraftingService", "CanStart");
        Check(Calls(intercept, "GoldDivineTransactions", "get_Outstanding") &&
            Calls(intercept, "GoldCraftingService", "CanStart") &&
            Calls(admission, "GoldCraftingService", "get_DivineActionsReady") &&
            Calls(admission, "GoldCraftingService", "get_AvailableFavor") &&
            Calls(admission, "GoldCraftingService", "CooldownAllows"),
            "new action uses pending receipt, server readiness, held-aware Favor and patron cooldown");

        var receive = Method("GoldDivineTransactions", "Receive");
        var transactionMethods = AllMethods(Type("GoldDivineTransactions")).Where(m => m.HasBody).ToArray();
        Check(transactionMethods.Any(m => CallAt(m, "GoldCraftingLedger", "Reserve") >= 0 &&
            CallAt(m, "GoldCraftingLedger", "Reserve") < CallAt(m, "GoldDivineTransactions", "Reply")) &&
            transactionMethods.Any(m => CallAt(m, "GoldCraftingLedger", "Commit") >= 0 &&
                m.Body.Instructions.Select((i, n) => (i, n)).Any(x => x.n > CallAt(m, "GoldCraftingLedger", "Commit") &&
                    x.i.Operand is MethodReference r && r.DeclaringType.Name == "GoldDivineTransactions" && r.Name == "Reply")),
            "server reserves pending Favor before grant and commits pending token before settlement reply");
        Check(Calls(receive, "GoldCraftingLedger", "get_IsAvailable") && Calls(receive, "GoldDivineTransactions", "ServerActionAllowed") &&
            Calls(Method("GoldDivineTransactions", "ServerActionAllowed"), "GoldCraftingService", "HasGold"), "server requires available ledger, authenticated Gold owner, and kind-specific action authority");
        Check(Calls(receive, "GoldCraftingLedger", "Commit") && Calls(receive, "GoldCraftingService", "ClearArm") &&
            Calls(receive, "GoldCraftingService", "NotifyFavor"), "successful pending settlement commits Favor, clears arm, and syncs state");

        var grant = Method("GoldDivineTransactions", "Grant");
        var grantClosures = transactionMethods.Where(m => m.Name.Contains("Grant>b__", StringComparison.Ordinal)).ToArray();
        Check(Calls(grant, "ZNet", "GetServerRPC") && Calls(grant, "GoldAction", "Decode") &&
            grantClosures.Any(m => Calls(m, "GoldDivineTransactions", "ReadReceipt") && Calls(m, "GoldAction", "Encode")),
            "grant accepts only decoded matching durable owner receipt from server path");
        Check(Calls(grant, "Queue`1", "Enqueue") && HasField(grant, "m_localPlayer"),
            "returned grants are deferred and bound to current local player");
        Check(grantClosures.Any(m => Calls(m, "GoldDivineTransactions", "Receipt") &&
            Calls(m, "GoldDivineTransactions", "Send") && HasInt(m, 0) && HasInt(m, 1) && HasInt(m, 2)),
            "unknown/rejected/applied/ack outcomes have explicit guarded paths");

        var apply = Method("GoldDivineTransactions", "Apply");
        Check(Calls(apply, "GoldDivineTransactions", "Target") && HasField(apply, "m_quality") &&
            Calls(apply, "ItemData", "GetMaxDurability"), "upgrade mutates the located existing ItemData and restores its durability cap");
        Check(Calls(apply, "Player", "GetFirstRequiredItem") && Calls(apply, "Inventory", "RemoveItem") && HasInt(apply, 1),
            "upgrade consumes the recipe-selected idol through native GetFirstRequiredItem and one-item removal");
        Check(!Calls(intercept, "InventoryGui", "DoCrafting") && Calls(prefix, "GoldDivineTransactions", "Intercept") &&
            intercept.Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_0),
            "custom craft prefix suppresses vanilla DoCrafting when it owns the request");
        Check(HasInt(apply, 5) && HasString(apply, "VM_Masterwork") && HasString(apply, "VM_MasterworkPatron") &&
            Calls(apply, "Dictionary`2", "Remove") && !HasString(apply, "Volundr"), "quality-5 creation removes inherited visible stamp metadata");

        var service = Method("GoldCraftingService", "get_DivineActionsReady");
        Check(Calls(service, "GoldCraftingService", "get_CraftingEnabled") && Calls(service, "GoldCraftingService", "get_CoreReady"),
            "crafting action readiness requires both crafting enable and current shared core readiness");
        Check(Calls(Method("GoldCraftingService", "get_Enabled"), "GoldCraftingService", "get_CraftingEnabled") &&
            Calls(Method("GoldCraftingService", "get_CraftingEnabled"), "GoldCraftingService", "get_CoreEnabled"),
            "legacy Enabled remains crafting-only and includes the shared core gate");
        var recoveryAccess = Method("GoldCraftingService", "TryGetRecoveryLedger");
        Check(Calls(recoveryAccess, "ZNet", "IsServer") && Calls(recoveryAccess, "ZNet", "GetWorldUID") &&
            HasField(recoveryAccess, "SessionWorld") && HasField(recoveryAccess, "LoadedWorld") &&
            !Calls(recoveryAccess, "GoldCraftingService", "get_CoreEnabled"),
            "recovery storage binds current loaded server world independently of enable flags");
        Check(Calls(Method("GoldCraftingService", "TryGetServerLedger"), "GoldCraftingService", "get_CoreEnabled") &&
            Calls(Method("GoldCraftingService", "TryGetServerLedger"), "GoldCraftingService", "TryGetRecoveryLedger"),
            "fresh storage access adds core enable to current-world recovery binding");
        Check(grantClosures.Any(m => Calls(m, "GoldDivineTransactions", "Apply") &&
            Calls(m, "GoldCraftingService", "get_DivineActionsReady")),
            "deferred native application requires authoritative fresh crafting readiness");
        var hold = Method("GoldCraftingService", "HoldsHammer");
        Check(Calls(hold, "Humanoid", "GetRightItem") && HasField(hold, "m_hiddenRightItem") &&
            Calls(hold, "Player", "GetCurrentCraftingStation"), "Hammer eligibility accounts for native hidden hand item at a station");

        var persist = Method("GoldCharacterSave", "Persist");
        Check(Calls(persist, "Game", "get_instance") && Calls(persist, "PlayerProfile", "SavePlayerData") &&
            Calls(persist, "PlayerProfile", "Save") && HasField(persist, "SawWriter") &&
            HasField(persist, "Failed"), "character persistence requires profile save and observed writer success");
        var observe = Method("GoldCharacterSave", "Observe");
        Check(Calls(observe, "FileWriter", "get_Status") && HasInt(observe, 2),
            "FileWriter.Finish status guard requires WriterStatus completed=2");

        var processing = Type("ProcessingCompletePatch");
        var post = processing.Methods.Single(m => m.Name == "Postfix");
        Check(Calls(post, "ZDO", "Set") && HasString(post, "vm.gold.processing.proof.") &&
            HasString(post, "vm.gold.processing.sequence.v1") && Calls(post, "GoldFavorService", "SendProcessing"),
            "completed processing proof ring persists receipt/sequence on station ZDO before sending");
        var proof = Method("GoldFavorService", "ProcessProcessingReceipt");
        Check(Calls(proof, "ZDO", "GetString") && HasString(proof, "vm.gold.processing.proof.") &&
            Calls(proof, "GoldCraftingLedger", "GainBackground") && Calls(proof, "GoldFavorService", "ReplyProcessing"),
            "server verifies persistent station proof and idempotently awards background Favor before ack");

        var begin = Method("GoldDivineTransactions", "BeginMasterwork");
        var canStart = Method("GoldDivineTransactions", "CanStartMasterwork");
        Check(HasField(begin, "m_craftTimer") && HasField(begin, "m_craftRecipe") && HasField(begin, "m_craftVariant") &&
            !Calls(begin, "InventoryGui", "OnCraftPressed") && !Calls(begin, "InventoryGui", "DoCrafting") &&
            !Calls(canStart, "GoldCraftingService", "HoldsHammer") && !Calls(canStart, "GoldCraftingService", "get_Armed"),
            "explicit creation button owns the native timer without Hammer or global arm");
        var cancel = Method("GoldDivineTransactions", "CancelMasterwork");
        Check(HasField(cancel, "m_craftTimer") && !Calls(cancel, "GoldDivineTransactions", "Receipt") &&
            Calls(Method("GoldDivineTransactions", "Tick"), "GoldDivineTransactions", "CancelMasterwork") &&
            Calls(Method("GoldMasterworkClosePatch", "Prefix"), "GoldDivineTransactions", "CancelMasterwork") &&
            Calls(Method("GoldMasterworkCancelPatch", "Postfix"), "GoldDivineTransactions", "CancelMasterwork"),
            "close/cancel stop the owned timer without mutating durable receipts");
        var localValid = Method("GoldDivineTransactions", "LocalValid");
        var stationValid = Method("GoldDivineTransactions", "MasterworkStationValid");
        Check(Calls(localValid, "GoldMasterworkRules", "StationTypeMatches") &&
            Calls(stationValid, "GoldMasterworkRules", "StationTypeMatches") &&
            Calls(stationValid, "WorkshopWorldRecords", "WardAllows") && Calls(stationValid, "WorkshopStationProof", "Usable") &&
            !Calls(localValid, "CraftingStation", "GetLevel") && !Calls(stationValid, "CraftingStation", "GetLevel") &&
            !Calls(localValid, "Recipe", "GetRequiredStationLevel") && !Calls(stationValid, "Recipe", "GetRequiredStationLevel"),
            "scoped creation validation retains station type/ward/environment without station level");
        Check(Calls(Method("GoldDivineTransactions", "ServerActionAllowed"), "GoldMasterworkRules", "ActionAllowed") &&
            transactionMethods.Any(m => Calls(m, "WorkshopActor", "Resolve") && Calls(m, "GoldCraftingLedger", "Reserve") &&
                Calls(m, "GoldDivineTransactions", "ServerActionAllowed") && HasField(m, "admittedSession") &&
                Calls(m, "GoldCraftingService", "IsServerLedgerCurrent") && HasField(m, "requestWorld")) &&
            transactionMethods.Any(m => Calls(m, "GoldDivineTransactions", "Reply") && HasString(m, "station") &&
                HasField(m, "admittedSession") && HasField(m, "ledger") &&
                Calls(m, "GoldCraftingService", "IsServerLedgerCurrent") && HasField(m, "requestWorld") &&
                CallAt(m, "GoldCraftingService", "IsServerLedgerCurrent") < CallAt(m, "HashSet`1", "Remove") &&
                m.Body.Instructions.Take(CallAt(m, "HashSet`1", "Remove")).Any(i =>
                    i.OpCode.Code == Mono.Cecil.Cil.Code.Brtrue || i.OpCode.Code == Mono.Cecil.Cil.Code.Brtrue_S ||
                    i.OpCode.Code == Mono.Cecil.Cil.Code.Brfalse || i.OpCode.Code == Mono.Cecil.Cil.Code.Brfalse_S)),
            "async proof success and failure are session-bound; success re-resolves actor and kind authority");
        var cueInstruction = apply.Body.Instructions.First(i => i.Operand is MethodReference r &&
            r.DeclaringType.Name == "GoldMasterworkPresentation" && r.Name == "Play");
        Check(CallAt(apply, "GoldDivineTransactions", "Receipt") < CallAt(apply, "GoldMasterworkPresentation", "Play") &&
            !apply.Body.ExceptionHandlers.Any(h => cueInstruction.Offset >= h.TryStart.Offset &&
                (h.TryEnd == null || cueInstruction.Offset < h.TryEnd.Offset)),
            "completion cue follows durable receipt and is outside inventory rollback try");
        var buttonMethods = AllMethods(Type("GoldMasterworkButton")).ToArray();
        Check(Calls(Method("GoldMasterworkButton", "Ensure"), "Button", "set_onClick") &&
            buttonMethods.Any(m => Calls(m, "GoldDivineTransactions", "BeginMasterwork")) &&
            buttonMethods.All(m => !Calls(m, "InventoryGui", "OnCraftPressed")) &&
            !Type("GoldMasterworkTooltip").CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch"),
            "native button clone has independent listeners and legacy visible stamp patch is disabled");

        Check(CallAt(begin, "GoldDivineTransactions", "CanStartMasterwork") <
            CallAt(begin, "GoldMasterworkPresentation", "Begin") &&
            begin.Body.Instructions.Select((i, n) => (i, n)).Any(x => x.i.Operand is FieldReference f &&
                f.Name == "m_craftTimer" && x.i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld &&
                x.n < CallAt(begin, "GoldMasterworkPresentation", "Begin")),
            "working cue begins only after accepted intent/native timer");
        var presentation = Type("GoldMasterworkPresentation");
        var working = Method("GoldMasterworkWorkingCue", "Update");
        Check(Calls(working, "GoldMasterworkPresentation", "Local") && HasField(working, "_session") &&
            HasField(working, "_currentIntent") && HasField(working, "_pulses") && HasInt(working, 4) &&
            working.Body.ExceptionHandlers.Count > 0,
            "working pulses are bounded and scoped to exact local intent/session with cosmetic catch");
        Check(Calls(Method("GoldMasterworkPresentation", "Local"), "Application", "get_isBatchMode") &&
            Calls(Method("GoldMasterworkPresentation", "Local"), "GoldCraftingService", "get_Enabled") &&
            Method("GoldMasterworkPresentation", "Begin").Body.ExceptionHandlers.Count > 0 &&
            Method("GoldMasterworkPresentation", "Play").Body.ExceptionHandlers.Count > 0 &&
            Calls(Method("GoldMasterworkPresentation", "PlayAudio"), "PerkAudioService", "PlayPrefab") &&
            Calls(Method("GoldMasterworkPresentation", "PlayAudio"), "PerkAudioService", "Play") &&
            Calls(Method("GoldMasterworkPresentation", "SpawnVisual"), "VfxPool", "Spawn"),
            "headless/cosmetic errors isolated; native clip playback separate from pooled visual");
        Check(HasString(Method("GoldMasterworkPresentation", "WorkingPulse"), "fx_forge_hammer") &&
            HasString(Method("GoldMasterworkPresentation", "Play"), "fx_invupgrade") &&
            AllMethods(presentation).All(m => !Calls(m, "EffectList", "Create")),
            "selected native sparks/upgrade donors do not instantiate gameplay EffectList");
        var ui = Type("GoldPantheonUiController");
        Check(AllMethods(ui).All(m => !Calls(m, "GoldCraftingService", "ToggleArm") &&
                !HasString(m, "DivineInspiration")) &&
            !ui.Methods.Any(m => m.Name == "ToggleArm"),
            "Pantheon no longer builds or activates Prepare Inspiration");
        Check(Calls(Method("GoldPantheonUiController", "RefreshWalletCards"), "GoldCraftingService", "get_PatronWallets") &&
            HasField(Method("GoldPantheonUiController", "RefreshWalletCards"), "Held") &&
            Calls(Method("GoldPantheonUiController", "BuildWalletCard"), "GoldPantheonNativeStyle", "Frame") &&
            Calls(Method("GoldPantheonUiController", "BuildWalletCard"), "GoldPantheonNativeStyle", "Bar") &&
            Calls(Method("GoldPantheonUiController", "BuildWalletCard"), "GoldPantheonUiController", "EarningMethods"),
            "passive native cards retain authoritative wallets/holds and implemented earning descriptions");
        var copy = Method("GoldPantheonNativeStyle", "Copy");
        Check(Calls(copy, "Image", "set_sprite") && Calls(copy, "Graphic", "set_material") &&
            !Calls(copy, "Object", "Instantiate") &&
            AllMethods(ui).All(m => !Calls(m, "GoldCraftingLedger", "Gain") &&
                !Calls(m, "GoldCraftingLedger", "Reserve") && !Calls(m, "GoldCraftingLedger", "Commit")),
            "native styling copies only Image resources; cards never mutate financial state");
        Console.WriteLine("PASS Gold100: static Cecil shape checks only; Unity runtime behavior is not exercised.");
        return 0;
    }
}
