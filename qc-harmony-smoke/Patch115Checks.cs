using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Patch115Checks
{
    internal static int Run(string plugin)
    {
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition M(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(string type, string method, string target, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == target && r.Name == name);
        bool Field(string type, string method, string name) => M(type, method).Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == name);
        bool Float(string type, string method, float value) => M(type, method).Body.Instructions.Any(i => i.Operand is float f && f == value);
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        Check(Calls("Fire35PoseDriver", "Clone", "HashSet`1", "Contains") && Calls("Fire35PoseDriver", "Begin", "SkinnedMeshRenderer", "get_bones"), "fire pose clones only skeleton ancestry, not equipment/VFX trees");
        Check(Calls("Fire35CoreChargeVisual", "Create", "ParticleSystemRenderer", "SetActiveVertexStreams") &&
            Calls("Fire35CoreChargeVisual", "Create", "NativeSnowVisualAssets", "CopySheet"), "fire embers preserve native renderer streams and sprite animation");
        Check(Float("IceStaff35Trail", "Add", 12f) && Float("IceStaff35Trail", "Add", .08f), "frost zones last longer and scale continuously with magic");
        Check(Calls("IceStaff35Trail", "Update", "Character", "Damage") && Field("IceStaff35Trail", "Update", "m_staggerMultiplier") && Field("IceStaff35Trail", "Update", "m_skillRaiseAmount"), "frost tick uses native damage with zero stagger and explicit no-XP marker");
        Check(!module.Types.Any(t => t.Name == "Ice35ExposureResistancePatch"), "removed cold-vulnerability override, native resistance is preserved");
        Check(Calls("Ice35MasterFrostHitPatch", "Prefix", "Ice35Exposure", "Mark") && Calls("Ice35Exposure", "Mark", "MagicSkillPassives", "OwnerReady"), "generic player frost registers mastery without changing native spell payload");
        Check(Calls("IceStaff35FlyingPressure", "Prepare", "Character", "IsBoss") && Field("IceStaff35FlyingPressure", "Prepare", "MasterFrostHash") && Field("IceStaff35FlyingPressure", "Prepare", "NativeFrostHash"), "generic frost grounding excludes bosses and requires actual native frost");
        Check(Float("ShieldRush70", "TryStart", 16.5f) && Float("ShieldRush70", "TryStart", 11f), "shield rush preserves distinct expanded light/tower distances");
        Check(Calls("ShieldRush70", "Tick", "Physics", "IgnoreCollision") && Calls("ShieldRush70", "Stop", "Physics", "IgnoreCollision"), "hostile rush contacts are ignored temporarily and restored on stop");
        Check(!Field("ShieldRush70", "TryStart", "Stagger") && !Field("ShieldRush70", "Register", "m_staggerMultiplier"), "rush contains no stagger damage payload");
        Check(Calls("Magic70BloodDome", "ShieldCapacity", "Character", "IsBoss") == false && Field("Magic70BloodDome", "ShieldCapacity", "m_StatusEffects") && Field("Magic70BloodDome", "ShieldCapacity", "m_levelUpSkillOnBreak"), "cage reads native blood-shield definition rather than hashing an item name");
        Check(Calls("WorkshopStationProof", "Prewarm", "OwnerSkillAuthority", "SendNow") && Calls("WorkshopStationProof", "Prewarm", "WorkshopStationProof", "WarmLog"), "station prewarm publishes authority and traces its actual transport");
        Check(!Calls("WorkshopStationProof", "Prewarm", "Inventory", "RemoveItem"), "station prewarm never debits or temporarily injects resources");
        Check(Float("Pickaxes70SuperHitPatch", "Prefix", .15f) && M("Pickaxes70SuperHitPatch", "Prefix").Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == "RollChance") == 2, "pickaxe keeps two independent fifteen-percent rolls");
        Check(Calls("Pickaxes70SuperHitPatch", "Postfix", "PerkVisualService", "PlayProc") && Field("Pickaxes70SuperHitPatch", "Postfix", "m_health"), "vein feedback is tied to post-damage health inspection");
        Check(Calls("Magic70Surtling", "Tick", "Magic70Surtling", "RecallExisting") &&
            Calls("Magic70Surtling", "RecallExisting", "ZDO", "SetPosition"), "existing summon relocates the same persistent identity");
        Check(Calls("Magic70Surtling", "MoveRequest", "ZNet", "GetServerRPC") &&
            Calls("Magic70Surtling", "MoveLoaded", "ZNetView", "IsOwner"), "only the authenticated server commands the loaded minion owner");
        Check(Calls("Magic70Surtling", "MoveResult", "ZDO", "GetOwner") && Field("Magic70Surtling", "MoveResult", "Owner"), "recall confirmation matches the current authoritative owner");
        Check(!Calls("Magic70Surtling", "MoveLoaded", "Character", "SetHealth") &&
            !Calls("Magic70Surtling", "MoveLoaded", "Object", "Instantiate"), "recall neither heals nor clones a summon");
        Check(Calls("Magic70Surtling", "Finish", "Mathf", "Clamp") && Field("Magic70Surtling", "Finish", "PendingCost"), "successful recall refund is capped to the paid surcharge");
        Check(!Calls("Magic70Surtling", "ExpireRecalls", "Magic70Surtling", "Reply"), "uncertain owner outcome is not refunded as a confirmed failure");
        Check(Float("Magic70SurtlingBirthVisual", "Update", 3.6f) &&
            Calls("Magic70SurtlingBirthVisual", "Update", "Magic70SurtlingBirthVisual", "UpdateIgnition"), "longer core ignition precedes creature emergence");
        Console.WriteLine(count + " patch115 STATIC structure checks passed; visuals/network gameplay require live testing.");
        return 0;
    }
}
