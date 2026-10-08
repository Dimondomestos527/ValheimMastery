using Mono.Cecil;
using ValheimMastery;
internal static class Patch121Checks
{
    internal static int Run(string plugin)
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        Check(Math.Abs(ClubsReserveRules.Fit(50f, 99.9f) - 49.95f) < .0001f, "small food decay trims full reserve, never zeroes it");
        Check(ClubsReserveRules.Fit(50f, 60f) == 30f, "food expiration retains valid half-cap reserve");
        Check(ClubsReserveRules.Fit(20f, 99f) == 20f && ClubsReserveRules.Fit(50f, 120f) == 50f, "unchanged or increasing food maximum never spends reserve");
        float reserve = 50f, refund = 0f;
        for (int i = 1; i <= 100; i++)
        {
            float next = ClubsReserveRules.Fit(reserve, 100f - i * .1f);
            refund += reserve - next; reserve = next;
            if (reserve <= 0f || reserve > (100f - i * .1f) * .5f + .0001f) throw new Exception("decay step " + i);
        }
        Check(Math.Abs(reserve - 45f) < .001f && Math.Abs(refund - 5f) < .001f, "100 food decay updates refund only aggregate excess once");
        using var module = ModuleDefinition.ReadModule(plugin);
        MethodDefinition M(string type, string method) => module.Types.Single(t => t.Name == type).Methods.Single(m => m.Name == method);
        bool Calls(string type, string method, string owner, string name) => M(type, method).Body.Instructions.Any(i =>
            i.Operand is MethodReference r && r.DeclaringType.Name == owner && r.Name == name);
        bool Text(string type, string method, string text) => M(type, method).Body.Instructions.Any(i => i.Operand is string s && s == text);
        Check(Calls("Clubs70Reservation", "Maximum", "ClubsReserveRules", "Fit") &&
            !Calls("Clubs70Reservation", "Maximum", "Clubs70Reservation", "Release"), "native max-stamina updates cannot cancel ready charge");
        Check(!Text("Clubs70Reservation", "Tick", "fx_sledge_demolisher_hit") &&
            !Text("Clubs70Reservation", "Lock", "fx_sledge_demolisher_hit") &&
            !Text("Clubs70Reservation", "Tick", "vfx_HitSparks"), "new TZ: charge never emits threshold blasts or sparks bursts");
        Check(Calls("NativeGroundPressure", "Spawn", "MainModule", "set_scalingMode") &&
            Calls("NativeGroundPressure", "Spawn", "MainModule", "get_startSize") &&
            !Calls("NativeGroundPressure", "Spawn", "ParticleSystemRenderer", "get_mesh"), "native horizontal billboard calibrated from start size, not its unused mesh");
        bool Field(string type, string method, string field) => M(type, method).Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == field);
        Check(Field("Magic70Carrier", "HasEquippedStaff", "s_leftItem") && Field("Magic70Carrier", "HasEquippedStaff", "s_rightItem") &&
            Field("Magic70Carrier", "GetEquippedStaffQuality", "s_leftItemQuality") && Field("Magic70Carrier", "GetEquippedStaffQuality", "s_rightItemQuality"), "remote staff authority reads both native equipped slots and their qualities");
        Check(Calls("Magic70Carrier", "ProcessCast", "Humanoid", "GetCurrentWeapon") && Calls("Magic70Carrier", "ProcessCast", "PerkRuntimeService", "HasPerk"), "local host checks actual held staff and native mastery");
        Check(Calls("SummonRosterCommands", "Tick", "SummonRosterCommands", "FocusTargetStillAimed") &&
            Calls("SummonRosterCommands", "FocusTargetStillAimed", "Physics", "Raycast"), "locked dismiss focus keeps aim and line of sight cancellation");
        Check(Calls("SummonRosterCommands", "OnRejected", "Skeleton35Travel", "PortalRejected"), "negative server reply retires stale portal attempts");
        Console.WriteLine(count + " patch121 tests passed; live appearance and summon behavior not proven.");
        return 0;
    }
}
