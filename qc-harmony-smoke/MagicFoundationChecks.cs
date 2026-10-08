using Mono.Cecil;

internal static class MagicFoundationChecks
{
    internal static int Run(string plugin, string managed)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        using var game = ModuleDefinition.ReadModule(Path.Combine(managed, "assembly_valheim.dll"));
        MethodDefinition Method(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int checks = 0;
        void Check(bool ok, string label)
        { if (!ok) throw new Exception("Magic foundation: " + label); checks++; Console.WriteLine("PASS " + label); }
        Check(Calls(Method("ElementalBaseEitrRegenPatch", "Finalizer"), "Nullable`1", "get_Value"), "base eitr field restored by finalizer");
        Check(Calls(Method("BloodNativeHealingTickPatch", "Postfix"), "Character", "Heal") &&
            !Calls(Method("BloodNativeHealingTickPatch", "Postfix"), "SEMan", "ModifyHealthRegen"), "flat healing not multiplied by Rested/feast again");
        Check(Calls(Method("MagicShieldCasterLevelPatch", "Postfix"), "MagicShield35Service", "SetCasterLevel"), "native caster skill captured on shield receiver");
        Check(!Calls(Method("MagicShield35Service", "OnDamage"), "Character", "ApplyPushback") &&
            !Calls(Method("MagicShield35Service", "OnDamage"), "Character", "Damage") &&
            !Calls(Method("MagicShield35Service", "OnDamage"), "ZNetView", "InvokeRPC"), "failed shield break pressure removed without extra damage/XP");
        Check(Calls(Method("Shield35Renewal", "Pulse"), "Physics", "OverlapSphereNonAlloc"), "renewal uses bounded once-per-pulse query");
        Check(Calls(Method("Shield35Renewal", "Input"), "Character", "UseHealth") &&
            !Calls(Method("Shield35Renewal", "Input"), "Character", "UseEitr") &&
            !Calls(Method("Shield35Renewal", "Input"), "Character", "UseStamina"), "renewal spends only actual health");
        Check(Calls(Method("Shield35Renewal", "Receive"), "ZDO", "GetOwner") &&
            Calls(Method("Shield35Renewal", "Receive"), "ZNetView", "IsOwner") &&
            Calls(Method("Shield35Renewal", "Receive"), "ZDO", "GetPosition") &&
            Calls(Method("Shield35Renewal", "Receive"), "Shield35Renewal", "IntactShield") &&
            !Calls(Method("Shield35Renewal", "Receive"), "SEMan", "AddStatusEffect"),
            "renewal validates caster authority/range and recipient owner; absent shields are not recreated");
        Check(Calls(Method("Shield35ChannelNoCastPatch", "Prefix"), "Shield35Renewal", "Channeling"),
            "renewal animation cannot fire an old native attack/cast event");
        Check(!Calls(Method("MagicShield35Service", "OnDamage"), "BloodMagic35Service", "Nova"), "old damaging nova not reused");
        var rpc = game.Types.Single(t => t.Name == "Character").Methods.Single(m => m.Name == "RPC_Damage");
        Check(Calls(rpc, "Character", "ApplyPushback"), "native damage RPC supports pushback (runtime effect still needs live test)");
        var food = game.Types.Single(t => t.Name == "Player").Methods.Single(m => m.Name == "UpdateFood");
        Check(food.Body.Instructions.Any(i => i.Operand is float f && f == 10f), "actual food healing cadence is ten seconds");
        Check(Method("BloodMagic35BarrierNovaPatch", "Prepare").Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_0),
            "legacy shield nova patch disabled");
        Check(Method("IceStaff35Trail", "RegisterEffect").Body.Instructions.Any(i =>
            i.Operand is FieldReference f && f.Name == "m_nameHash" && i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld),
            "cloned frost template has its own cached hash");
        Check(Calls(Method("IceStaff35Trail", "Update"), "Physics", "OverlapSphereNonAlloc") &&
            Calls(Method("IceStaff35Trail", "Update"), "SEMan", "AddStatusEffect"), "bounded ground zones use native status owner routing");
        Check(Calls(Method("IceStaff35Trail", "Update"), "Character", "Damage") &&
            Method("IceStaff35Trail", "Update").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_skillRaiseAmount") &&
            Method("IceStaff35Trail", "Update").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_variant"),
            "ground damage is explicitly marked generated with no skill XP");
        Check(Calls(Method("IceTrailNoDoubleSlowPatch", "Prefix"), "SEMan", "HaveStatusEffect"), "trail does not stack speed reduction with native Frost");
        Check(Calls(Method("IceStaff35FlyingPressure", "Prepare"), "Character", "IsFlying") &&
            Calls(Method("IceStaff35FlyingPressure", "Prepare"), "SEMan", "HaveStatusEffect") &&
            Calls(Method("IceStaff35FlyingPressure", "Prepare"), "ZNetView", "IsOwner"),
            "frost flying descent applies only to tagged, owner-controlled flyers");
        Check(Calls(Method("BloodSkeleton35Service", "Update"), "IProjectile", "Setup"),
            "free skeleton uses native SpawnAbility projectile setup");
        Check(!Calls(Method("BloodSkeleton35Service", "Update"), "BloodSkeleton35Service", "CatchUpLoadedFollowers") &&
            Calls(Method("Skeleton35Travel", "Apply"), "ZNetView", "IsOwner") &&
            Calls(Method("Skeleton35Travel", "Apply"), "ZSyncTransform", "SyncNow"),
            "single skeleton travel controller moves only the network owner");
        foreach (string legacy in new[] { "ElementalMagic35FreeCastPatch", "BloodMagic35StaffBlockPatch", "BloodMagic35BarrierNovaPatch", "BloodMagic35SummonDeathNovaPatch" })
            Check(Method(legacy, "Prepare").Body.Instructions.Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldc_I4_0),
                "retired magic hook disabled: " + legacy);
        Check(Calls(Method("Fire35InputPatch", "Prefix"), "FireStaff35Charge", "Input"), "fire charge integrated into native primary input");
        Check(Calls(Method("FireStaff35Charge", "Prepare"), "ConditionalWeakTable`2", "GetOrCreateValue"), "charge multiplier belongs to attack instance, not shared staff prefab");
        Check(Calls(Method("Fire35ProjectilePatch", "Postfix"), "Mathf", "Sqrt") && Calls(Method("Fire35ExplosionPatch", "Postfix"), "Mathf", "Sqrt"), "area growth converted to radius for projectile and spawned explosion");
        Check(Calls(Method("Fire35BurnDurationPatch", "Prefix"), "ConditionalWeakTable`2", "GetValue"), "burn lifetime based on original duration, not compounding");
        Check(Method("Fire35BurstPatch", "Finalizer").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "ProjectileFactor" && i.OpCode.Code == Mono.Cecil.Cil.Code.Stsfld), "projectile context restored on exceptions");
        Check(Calls(Method("Skeleton35Travel", "Valid"), "ZDO", "GetLong") && Calls(Method("Skeleton35Travel", "Valid"), "GameObject", "GetComponent"), "travel validates persistent author and native skeleton prefab");
        Check(Calls(Method("Skeleton35Travel", "Apply"), "ZDO", "SetPosition") && !Calls(Method("Skeleton35Travel", "Apply"), "Object", "Instantiate"), "unloaded travel moves the same identity without cloning");
        Check(Calls(Method("Skeleton35Travel", "MoveReply"), "ZNet", "GetServerRPC"), "loaded movement reply restricted to server");
        Check(Calls(Method("OwnerSkillAuthority", "ResolvePlayer"), "ZNetScene", "FindInstance") &&
            Method("OwnerSkillAuthority", "ResolvePlayer").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_characterID"),
            "authenticated native CharacterID resolves remote player");
        Check(Calls(Method("WorkshopRemoteCraft", "RequestPlayer"), "WorkshopActor", "Resolve") &&
            Calls(Method("WorkshopActor", "Resolve"), "OwnerSkillAuthority", "ResolveCharacterData") &&
            Calls(Method("WorkshopRecovery", "Recover"), "OwnerSkillAuthority", "ResolvePlayerId") &&
            Calls(Method("OwnerSkillAuthority", "ResolvePlayerId"), "OwnerSkillAuthority", "ResolveCharacterData"),
            "craft and recovery share authenticated ZDO-only remote identity binding");
        Check(Calls(Method("Skeleton35Travel", "Tick"), "BaseAI", "HavePath") &&
            Calls(Method("Skeleton35PortalPatch", "Postfix"), "Skeleton35Travel", "Portal"),
            "travel observes path failure and successful player teleport");
        Check(Calls(Method("FireStaff35Charge", "Input"), "Player", "UseEitr") || Calls(Method("FireStaff35Charge", "Input"), "Character", "UseEitr"),
            "charge pays eitr through native resource API");
        Check(Method("Fire35CostPatch", "Postfix").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Prepaid"),
            "prepaid cast cannot charge eitr again at launch");
        Check(Calls(Method("MagicShield35Service", "HealSummon"), "Character", "Heal") &&
            Calls(Method("MagicShield35Service", "HealSummon"), "ZNetView", "IsOwner"),
            "summon shield healing belongs to creature network owner");
        Console.WriteLine(checks + " static magic-foundation checks passed; not a gameplay/VFX verification.");
        return 0;
    }
}
