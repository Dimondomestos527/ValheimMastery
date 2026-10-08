using Mono.Cecil;

internal static class Magic70Checks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition Method(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.Name == type && r.Name == name);
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
        Check(Calls(Method("PerkRuntimeService", "GetActualSkillLevel"), "Dictionary`2", "TryGetValue") &&
            !Calls(Method("PerkRuntimeService", "GetActualSkillLevel"), "Skills", "GetSkillList") &&
            !Calls(Method("PerkRuntimeService", "GetActualSkillLevel"), "Skills", "GetSkillLevel"),
            "raw perk levels avoid allocating native skill lists and do not inherit status bonuses");
        Check(Calls(Method("Magic70StormZone", "Update"), "ZNetView", "IsOwner"), "storm damage scheduled only by object owner");
        Check(Calls(Method("Magic70StormZone", "Update"), "Physics", "OverlapSphereNonAlloc"), "storm bounded nonalloc query");
        Check(Calls(Method("Magic70StormZone", "Update"), "Character", "IsPlayer") && Calls(Method("Magic70StormZone", "Update"), "BaseAI", "IsEnemy"), "storm excludes players and friends");
        Check(Calls(Method("Magic70StormZone", "Update"), "ZNet", "GetTime") && Calls(Method("Magic70StormZone", "Update"), "ZDO", "GetLong"), "network-time expiry survives ownership changes");
        Check(Calls(Method("Magic70IceStorm", "Obscures"), "Character", "IsPlayer") && Calls(Method("Magic70IceStorm", "Obscures"), "Character", "IsTamed"), "vision penalty excludes players and friendly minions");
        Check(Calls(Method("Magic70IceStorm", "Input"), "PerkCooldownStateService", "TryConsume"), "storm has persisted cooldown");
        Check(Calls(Method("Magic70Surtling", "Receive"), "ZNet", "GetPeer"), "summon request authenticates native peer");
        Check(Calls(Method("Magic70Surtling", "Receive"), "WorkshopActor", "Resolve") &&
            !Method("Magic70Surtling", "Receive").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_playerID"),
            "summon admission resolves authenticated character ZDO, not unreliable peer playerID");
        Check(Calls(Method("Magic70Surtling", "Admit"), "OwnerSkillAuthority", "Has") &&
            Calls(Method("Magic70Surtling", "Admit"), "ZDO", "GetInt"),
            "summon admission verifies owner skill snapshot and replicated equipped staff/quality");
        Check(Calls(Method("OwnerSkillAuthority", "ReceiveMagic"), "OwnerSkillAuthority", "ResolveCharacterData") &&
            Calls(Method("OwnerSkillAuthority", "ReceiveMagic"), "OwnerSkillAuthority", "Valid"),
            "magic snapshot has authenticated character binding and bounded levels");
        Check(!Method("Magic70Surtling", "Admit").Body.Instructions.Any(i => i.Operand is MethodReference r && r.Name == "Clear"),
            "summon replay admission cannot silently evict prior identities");
        Check(Calls(Method("Magic70Surtling", "Tick"), "ZDOMan", "GetAllZDOsWithPrefabIterative"), "single-summon admission includes unloaded world identities, incrementally");
        Check(Calls(Method("Magic70Surtling", "Tick"), "Character", "SetLevel") && Calls(Method("Magic70Surtling", "Tick"), "Tameable", "Command"), "summon native stars and saved follow target");
        Check(Calls(Method("Magic70Surtling", "Tick"), "Magic70Surtling", "NativeLevelForQuality"), "summon uses explicit quality 1/2/3+ to native level 1/2/4 mapping");
        Check(Calls(Method("Magic70SurtlingLootPatch", "Prefix"), "Magic70Surtling", "HasSummonAuthor"), "authored summon drops suppressed in the shared direct/ragdoll generation path");
        Check(Calls(Method("Magic70Surtling", "Input"), "PerkCooldownStateService", "GetRemainingSeconds") &&
            Method("Magic70Surtling", "Finish").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "PendingCost"),
            "death surcharge uses persistent expiry and exact paid-cost refunds");
        Check(Calls(Method("Magic70SurtlingDeathNoticePatch", "Prefix"), "Magic70Surtling", "BeforeNativeDeath") &&
            Calls(Method("Magic70Surtling", "BeforeNativeDeath"), "ZNetView", "IsOwner") &&
            Calls(Method("Magic70Surtling", "ProcessDeathReceipts"), "ZDO", "GetOwner"),
            "summon death notice precedes native destruction and validates source ownership");
        Check(Calls(Method("Magic70SurtlingHealthPatch", "Postfix"), "Character", "GetMaxHealthBase"),
            "summon max health derives from native base rather than compounding prior buffs");
        Check(Method("Magic70Surtling", "RegisterPrefab").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_dropsEnabled" && i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld), "summon drops disabled on template before activation");
        Check(Calls(Method("Magic70Surtling", "Result"), "ZNet", "GetServerRPC"), "refund/grant replies accepted only from server");
        Check(Calls(Method("Magic70SurtlingLife", "Explode"), "ZDO", "GetBool") && Calls(Method("Magic70SurtlingLife", "Explode"), "Character", "IsPlayer"), "one-shot explosion excludes players");
        Check(Calls(Method("Magic70SurtlingLife", "Update"), "BaseAI", "MoveTo"), "low-health suicide uses native movement, not teleport");
        if (module.Types.Any(t => t.Name == "Magic70DomePending"))
        {
            Check(!Calls(Method("Magic70DomeProjectilePatch", "Prefix"), "ZNetScene", "Destroy"), "dome contact never destroys projectile before authority decision");
            Check(Calls(Method("Magic70DomePending", "Hold"), "ZNetScene", "Destroy"), "accepted dome absorption finishes on projectile owner");
            Check(Calls(Method("Magic70DomeZone", "Hit"), "ZDO", "GetOwner") && Calls(Method("Magic70DomeZone", "Hit"), "ZDO", "GetBool"), "dome debit authenticates projectile owner and persists replay identities");
        }
        if (module.Types.Any(t => t.Name == "Clubs70Reservation"))
        {
            Check(Calls(Method("Clubs70Reservation", "Tick"), "Character", "UseStamina"), "clubs charge reserves actual stamina");
            Check(Calls(Method("Clubs70Reservation", "Commit"), "Clubs70Reservation", "Release"), "committed clubs attack consumes reserve");
            Check(Calls(Method("Clubs70Reservation", "Release"), "Character", "AddStamina") || Calls(Method("Clubs70Reservation", "Release"), "Player", "AddStamina"), "clubs expiry/cancellation restores stamina reserve");
            Check(Method("Clubs70Service", "TryShockwave").Body.Instructions.Count <= 2, "charged clubs replace rather than stack legacy shockwave");
            Check(Calls(Method("Clubs70Reservation", "Commit"), "MagicSkillPassives", "OwnerReady") &&
                Calls(Method("Clubs70Reservation", "Commit"), "Clubs70Reservation", "SameSession") &&
                Calls(Method("Clubs70Reservation", "Commit"), "Humanoid", "GetCurrentWeapon"),
                "clubs commit is owner/session/current-weapon bound");
            Check(Calls(Method("Clubs70ReservationSave", "Prefix"), "Clubs70Reservation", "BeforeSave") &&
                Calls(Method("Clubs70ReservationSave", "Finalizer"), "Clubs70Reservation", "AfterSave"),
                "native save projects refundable reserve and restores live values on exceptions");
            Check(!Calls(Method("Clubs70Reservation", "BeforeSave"), "Clubs70Reservation", "Release"),
                "autosave does not cancel or consume a live clubs charge");
            Check(Calls(Method("Clubs70ReservationTeardown", "Prefix"), "Clubs70Reservation", "Cancel") &&
                Calls(Method("Clubs70ReservationLogout", "Prefix"), "Clubs70Reservation", "Cancel"),
                "clubs logout/teardown releases the ephemeral charge");
        }
        if (module.Types.Any(t => t.Name == "Hammer35Epicenter"))
        {
            Check(Calls(Method("Hammer35Epicenter", "Apply"), "Attack", "GetAttackOrigin") &&
                Calls(Method("Hammer35Epicenter", "Apply"), "ClubWeaponClassService", "Classify") &&
                Calls(Method("Hammer35Epicenter", "Apply"), "PerkRuntimeService", "IsPerkGenerated"),
                "hammer inner zone uses native area origin, classified weapon and real hits only");
            Check(Method("Hammer35AttackScope", "Finalizer").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "Current"),
                "hammer native attack context restores even after an exception");
            Check(!module.Types.Any(t => t.Name == "Hammer35FractureDamagePatch"), "new TZ removes obsolete hammer fracture multiplier");
            Check(Calls(Method("Hammer35Epicenter", "Apply"), "Clubs70EchoService", "CaptureHammer") &&
                Calls(Method("Hammer35EchoCompletion", "Postfix"), "Clubs70EchoService", "CompleteHammer"),
                "new TZ captures real native hammer hits and queues delayed echo after area attack");
        }
        if (module.Types.Any(t => t.Name == "Mace35CorpseProjectile"))
        {
            Check(Calls(Method("Mace35CorpseProjectile", "Mark"), "AttackIntentService", "IsSecondary") && Calls(Method("Mace35CorpseProjectile", "Mark"), "PerkRuntimeService", "IsPerkGenerated"), "mace launch requires real secondary and rejects generated hits");
            Check(Calls(Method("Mace35CorpseProjectile", "Launch"), "ZNetView", "IsOwner") && !Calls(Method("Mace35CorpseProjectile", "Launch"), "Object", "Instantiate"), "mace launch uses existing owner-authoritative native corpse");
            Check(Calls(Method("Mace35CorpseFlight", "FixedUpdate"), "Physics", "SphereCastNonAlloc"), "corpse gameplay uses bounded swept collision");
            Check(Method("PerkRuntimeService", "IsPerkGenerated").Body.Instructions.Any(i => Equals(i.Operand, 1201)), "corpse anti-recursion marker survives native damage serialization");
        }
        if (module.Types.Any(t => t.Name == "ShieldRush70"))
        {
            Check(Calls(Method("ShieldRush70", "TryStart"), "Character", "UseStamina") &&
                Calls(Method("ShieldRush70", "TryStart"), "PerkCooldownStateService", "TryConsume"),
                "shield rush pays once on committed start with persisted cooldown");
            Check(Calls(Method("ShieldRush70", "Tick"), "Physics", "CapsuleCastNonAlloc") &&
                Calls(Method("ShieldRush70", "Tick"), "Physics", "OverlapCapsuleNonAlloc") &&
                Calls(Method("ShieldRush70", "ImpactArea"), "Physics", "OverlapSphereNonAlloc"),
                "shield rush motion and impacts use bounded collision buffers");
            Check(!Calls(Method("ShieldRush70", "Tick"), "Character", "Damage") &&
                !Calls(Method("ShieldRush70", "Tick"), "Player", "Dodge") &&
                !Calls(Method("ShieldRush70", "Tick"), "Player", "TeleportTo"),
                "shield rush neither teleports nor uses damaging/dodge payloads");
            Check(Calls(Method("ShieldRush70InputPatch", "Prefix"), "ShieldRush70", "IsActive"),
                "repeated dodge input cannot start native i-frame roll during rush");
            Check(!Calls(Method("Blocking70ReflectionService", "HandleBlock"), "Blocking70ReflectionService", "DealDamage") &&
                !Calls(Method("Blocking70ReflectionService", "HandleBlock"), "Blocking70ReflectionService", "DealParrySplash"),
                "new shield70 excludes legacy passive reflection dispatch");
        }
        if (module.Types.Any(t => t.Name == "Blocking35ProjectileService"))
        {
            Check(!Calls(Method("Blocking35ProjectileService", "Route"), "Humanoid", "BlockAttack") &&
                Calls(Method("Blocking35ProjectileService", "Receive"), "Character", "RPC_Damage"),
                "shield35 routes one native owner damage resolution instead of probing BlockAttack");
            Check(Calls(Method("Blocking35ProjectileService", "Receive"), "ZDO", "GetOwner") &&
                Calls(Method("Blocking35ProjectileService", "Receive"), "ZNetView", "IsOwner"),
                "shield35 authenticates source authority and victim ownership");
            Check(Calls(Method("Blocking35ProjectileService", "AfterBlock"), "Character", "HaveStamina") &&
                Calls(Method("Blocking35ProjectileService", "AfterBlock"), "Character", "IsStaggering"),
                "shield35 requires an unbroken native block before negation/reflection");
            Check(!Calls(Method("Blocking35ProjectileService", "Return"), "DamageTypes", "Modify") &&
                Calls(Method("Blocking35ProjectileService", "Return"), "Projectile", "Setup"),
                "reflection preserves original damage and uses a native owner-created projectile");
            Check(Calls(Method("Blocking35ExplosionCapture", "Postfix"), "Blocking35ProjectileService", "CaptureExplosion") &&
                Calls(Method("Blocking35ExplosionScope", "Prefix"), "Blocking35ProjectileService", "Explosion"),
                "short spawned impact retains original projectile identity; unrelated AoE has no correlation");
            Check(Method("PerkRuntimeService", "IsPerkGenerated").Body.Instructions.Any(i => Equals(i.Operand, 1235)),
                "reflection anti-recursion marker survives native hit serialization");
        }
        Check(Calls(Method("FireStaff35Charge", "ResolvePose"), "Fire35PoseDriver", "Begin") &&
            Calls(Method("FireStaff35Charge", "SamplePose"), "Fire35PoseDriver", "Draw") &&
            !Calls(Method("FireStaff35Charge", "SamplePose"), "Animator", "Play") &&
            !Calls(Method("FireStaff35Charge", "SamplePose"), "AnimationClip", "SampleAnimation"),
            "fire windup samples isolated native Mecanim without holding gameplay animator");
        foreach (string spell in new[] { "Magic70IceStorm", "Magic70BloodDome", "Magic70Surtling" })
            Check(Calls(Method(spell, "Input"), "Magic70CastAnimation", "Play") &&
                !Calls(Method(spell, "Input"), "ZSyncAnimation", "SetTrigger"),
                spell + " never starts the staff's unmanaged primary loop");
        Check(Method("Magic70CastAnimation", "Play").Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "m_loopingAttack") &&
            Method("Magic70CastAnimation", "Finish").Body.Instructions.Any(i => Equals(i.Operand, "attack_abort")),
            "cosmetic cast rejects native loops and always has a replicated exit");
        Check(Calls(Method("Magic70CosmeticCastEventPatch", "Prefix"), "Magic70CastAnimation", "Casting"),
            "cosmetic cast events cannot fire a stale native Attack");
        Check(Calls(Method("Magic70BloodDome", "ReceiveRequest"), "WorkshopActor", "Resolve") &&
            Calls(Method("Magic70BloodDome", "ReceiveRequest"), "OwnerSkillAuthority", "Has") &&
            Calls(Method("Magic70BloodDome", "ReceiveRequest"), "ZRoutedRpc", "InvokeRoutedRPC"),
            "enemy cage authenticates caster and routes to actual target owner without server scene dependency");
        Check(Calls(Method("Magic70BloodDome", "ValidEnemy"), "Character", "IsPlayer") &&
            Calls(Method("Magic70BloodDome", "ValidEnemy"), "Character", "IsTamed") &&
            Calls(Method("Magic70BloodDome", "ValidEnemy"), "Character", "IsBoss"),
            "enemy cage excludes players, friendly summons and bosses");
        Check(Calls(Method("Magic70BloodCageHold", "Release"), "ZNetView", "IsOwner") &&
            Calls(Method("Magic70BloodCageHold", "Release"), "Character", "Damage") &&
            Method("Magic70BloodCageHold", "Release").Body.Instructions.Any(i => Equals(i.Operand, .75f)),
            "enemy cage expiry is an owner-only direct hit based on native shield capacity");
        Check(Calls(Method("Magic70BloodCageVisual", "TryCreate"), "PerkNativeFeedback", "CreateVisualOnly") &&
            !module.Types.Any(t => t.Name == "Magic70DomeZone"),
            "persistent native shield shell replaces legacy friendly dome gameplay");
        Console.WriteLine(checks + " magic70 structural checks passed. LIVE_TEST_REQUIRED; not proof of visuals or multiplayer.");
        return 0;
    }
}
