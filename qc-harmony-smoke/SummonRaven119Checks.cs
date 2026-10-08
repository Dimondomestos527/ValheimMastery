using Mono.Cecil;

internal static class SummonRaven119Checks
{
    internal static int Run(string path)
    {
        using var module = ModuleDefinition.ReadModule(path);
        MethodDefinition M(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(string type, string method, string owner, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == owner && r.Name == name);
        int count = 0;
        void Check(bool yes, string text) { if (!yes) throw new Exception(text); Console.WriteLine("PASS " + text); count++; }
        Check(Calls("SummonRosterCommands", "Receive", "WorkshopActor", "Resolve") &&
            Calls("SummonRosterCommands", "HasMastery", "OwnerSkillAuthority", "Has"), "roster authenticates character and mastery");
        Check(Calls("SummonRosterCommands", "Receive", "ZDO", "Serialize") &&
            Calls("SummonRosterCommands", "Receive", "ZDO", "Deserialize") &&
            Calls("SummonRosterCommands", "Receive", "ZDOMan", "CreateNewZDO"), "server copies full native state rather than naked summon or client payload");
        Check(!Calls("SummonRosterCommands", "Receive", "Character", "SetHealth") &&
            !Calls("SummonRosterCommands", "Retire", "Character", "OnDeath"), "replacement and dismissal do not heal or trigger creature death");
        Check(Calls("SummonRosterCommands", "Retire", "ZDO", "SetOwner") &&
            Calls("SummonRosterCommands", "Retire", "ZDOMan", "DestroyZDO") &&
            Calls("SummonRosterCommands", "Retire", "ZDOMan", "SendDestroyed"), "server native retirement flushes tombstone before replacement publication");
        Check(Calls("SummonRosterCommands", "Receive", "Dictionary`2", "TryGetValue") &&
            Calls("SummonRosterCommands", "Finish", "Dictionary`2", "TryGetValue"), "server replay receipts and client pending identity checks exist");
        Check(Calls("Skeleton35Travel", "Portal", "Character", "GetAllCharacters") &&
            Calls("Skeleton35Travel", "Tick", "SummonRosterCommands", "PortalRequest"), "departure roster captured before unloading and recovered after arrival");
        Check(Calls("BloodSkeleton35Service", "Update", "Skeleton35Travel", "get_PortalRecoveryPending"), "free skeleton waits for in-flight portal roster");
        Check(Calls("SummonRosterCommands", "Tick", "Physics", "Raycast") &&
            Calls("SummonRosterCommands", "Tick", "SummonDismissHud", "Show"), "dismissal requires focused line of sight and has progress feedback");
        Check(Calls("MasteryRavenTutorials", "Tick", "Tutorial", "SpawnRaven") &&
            Calls("MasteryRavenTutorials", "Tick", "Player", "HaveSeenTutorial") &&
            !Calls("MasteryRavenTutorials", "Tick", "Player", "SetSeenTutorial"), "native raven dialogue controls completion, never arrival alone");
        Check(Calls("PerkVisualService", "PlayMilestone", "MasteryRavenTutorials", "Unlock") &&
            Calls("MasteryRavenTutorials", "Queue", "Dictionary`2", "set_Item"), "one-shot lessons are queued in native-saved player customData");
        Check(Calls("Magic70CarrierPrefabPatch", "Postfix", "Magic70Carrier", "RegisterPrefab") &&
            Calls("NetworkSync", "Register", "Magic70Carrier", "Register") &&
            Calls("MasteryPlugin", "Update", "Magic70Carrier", "Tick"), "approved carrier prefab and lifecycle are wired in candidate119");
        Check(Calls("Stride70Service", "Update", "PerkFeedbackService", "Play") &&
            !Calls("Stride70Service", "Update", "RoadRhythmPhaseVisual", "Set") &&
            !Calls("Stride70Service", "Update", "PerkAudioService", "Play"), "run phases restore105 native recipe route with one audio owner");
        Check(Calls("Hammer35Epicenter", "Impact", "WeaponImpactVisualQueue", "QueueHammerImpact") &&
            !Calls("Hammer35Epicenter", "Impact", "PerkNativeFeedback", "CreateVisualOnly") &&
            Calls("WeaponImpactVisualQueuePatch", "Postfix", "WeaponImpactVisualQueue", "Flush"), "hammer cosmetic creation is outside animation callback");
        Check(Calls("VfxPoolBaseline", "Restore", "MainModule", "set_maxParticles") &&
            Calls("VfxPoolBaseline", "Restore", "EmissionModule", "set_enabled"), "cage particle budgets cannot contaminate later pool leases");
        Console.WriteLine(count + " summon/raven119 STATIC checks passed; Unity/network/persistence remain LIVE_TEST_REQUIRED.");
        return 0;
    }
}
