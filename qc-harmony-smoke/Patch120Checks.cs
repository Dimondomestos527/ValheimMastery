using Mono.Cecil;
using ValheimMastery;

internal static class Patch120Checks
{
    internal static int Run(string path)
    {
        using var module = ModuleDefinition.ReadModule(path);
        MethodDefinition M(string type, string name) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == name);
        bool Calls(string type, string method, string owner, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == owner && r.Name == name);
        bool Field(string type, string method, string name) => M(type, method).Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == name);
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        foreach (int level in new[] { 35, 70, 100 })
        {
            Check(MilestoneCrossingRule.Crossed(level - 1, level, level), "repeat crossing " + level + " is eligible without unlock state");
            Check(!MilestoneCrossingRule.Crossed(level, level + .2f, level), "ordinary XP above " + level + " cannot replay presentation");
            Check(!MilestoneCrossingRule.Crossed(level + 1, level - 1, level), "lowering through " + level + " cannot play level-up");
        }
        Check(!MilestoneCrossingRule.Crossed(float.NaN, 100, 35) &&
            !MilestoneCrossingRule.Crossed(34, float.PositiveInfinity, 35), "invalid level inputs cannot unlock repeat presentation");
        Check(Calls("RaiseSkillPatch", "Prefix", "PerkRuntimeService", "GetActualSkillLevel") &&
            Field("RaiseSkillPatch", "Postfix", "PreviousLevel") &&
            Calls("Milestones", "Check", "MilestoneCrossingRule", "Crossed"), "native XP compares actual before and after level");
        Check(Calls("Milestones", "Check", "PerkStateService", "WasEverUnlocked") &&
            Calls("Milestones", "Check", "PerkStateService", "MarkEverUnlocked"), "repeat milestone keeps durable unlock and first Gold acknowledgement separate");
        Check(Calls("Magic70IceStorm", "Input", "ZNetView", "InvokeRPC") &&
            Calls("Magic70StormZone", "Relocate", "ZDO", "GetOwner") &&
            Calls("Magic70StormZone", "Relocate", "ZDO", "SetPosition") &&
            Calls("Magic70StormZone", "Relocate", "Magic70StormZone", "ClearVisual"), "storm relocation moves one authenticated native zone");
        Check(Field("FireStaff35Charge", "Input", "CancelledUntilRelease") &&
            Field("FireStaff35Charge", "Input", "m_blocking") &&
            Field("Overdraw70Service", "TryConsume", "m_blocking"), "RMB cancellation suppresses fire restart and charged bow payload");
        Check(!Calls("Clubs70Reservation", "Tick", "ZInput", "GetButtonDown") &&
            Field("Clubs70Reservation", "Release", "SuppressUntilBlockRelease"), "new TZ: block resumes partial charge rather than canceling it");
        Check(Calls("SummonRosterCommands", "OnFocusAcknowledged", "Character", "GetZDOID") &&
            Field("SummonRosterCommands", "Tick", "FocusAcknowledged") &&
            Field("SummonRosterCommands", "Receive", "Token"), "dismiss fill waits for matching server focus acknowledgement");
        Console.WriteLine(count + " patch120 checks passed: pure threshold tests and structural checks; renderer/network remain LIVE_TEST_REQUIRED.");
        return 0;
    }
}
