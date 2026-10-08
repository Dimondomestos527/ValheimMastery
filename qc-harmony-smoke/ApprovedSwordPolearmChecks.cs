using System.Reflection;
using System.Runtime.CompilerServices;

internal static class ApprovedSwordPolearmChecks
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static int Run(Assembly plugin)
    {
        Type balance = plugin.GetType("ValheimMastery.SwordQuickSecondaryBalance", true)!;
        Type parry = plugin.GetType("ValheimMastery.SwordOffensiveParryService", true)!;
        Constant(balance, "AnimationSpeed", 1.20f);
        Constant(balance, "DamageMultiplier", 1.35f);
        Constant(balance, "StaminaMultiplier", 1f);
        Constant(balance, "StaggerMultiplier", 1.75f);
        Constant(parry, "TimingWindow", 0.35f);
        var scale = plugin.GetType("ValheimMastery.Swords35SecondaryAnimationSpeedPatch", true)!
            .GetMethod("ScaleNativeSpeed", Static)!;
        foreach (float native in new[] { 0f, .5f, 1f, 1.5f })
            if (MathF.Abs((float)scale.Invoke(null, new object[] { native })! - native * 1.20f) > .00001f)
                throw new Exception("Sword35 overwrites native animation-event speed instead of scaling it.");

        // Exercise the actual cancellation method with managed state. Do not call
        // Unity methods or construct scene objects in this offline regression test.
        Type spinState = plugin.GetType("ValheimMastery.Polearm35SpinState", true)!;
        object state = Activator.CreateInstance(spinState, true)!;
        spinState.GetField("Pending", Instance)!.SetValue(state, true);
        spinState.GetField("WasTargetStaggeredByParry", Instance)!.SetValue(state, true);
        FieldInfo weapon = spinState.GetField("TriggerWeapon", Instance)!;
        weapon.SetValue(state, RuntimeHelpers.GetUninitializedObject(weapon.FieldType));
        plugin.GetType("ValheimMastery.Polearm35AutoSpinService", true)!
            .GetMethod("ClearPending", Static)!.Invoke(null, new[] { state });
        if ((bool)spinState.GetField("Pending", Instance)!.GetValue(state)! ||
            (bool)spinState.GetField("WasTargetStaggeredByParry", Instance)!.GetValue(state)! ||
            weapon.GetValue(state) != null || spinState.GetField("Attacker", Instance)!.GetValue(state) != null)
            throw new Exception("Polearm35 cancellation retained a pending counterattack or its weapon/target.");

        Type resolution = plugin.GetType("ValheimMastery.Polearm35ParryResolutionService", true)!;
        FieldInfo active = resolution.GetField("Active", Static)!;
        object original = active.GetValue(null)!;
        MethodInfo cleanup = plugin.GetType("ValheimMastery.Polearm35ResolutionFailureCleanupPatch", true)!
            .GetMethod("Finalizer", Static)!;
        try
        {
            object context = Activator.CreateInstance(active.FieldType, true)!;
            active.SetValue(null, context);
            if (cleanup.Invoke(null, new object[] { null! }) != null || !ReferenceEquals(active.GetValue(null), context))
                throw new Exception("Successful block cleanup corrupted the current parry scope.");
            var error = new InvalidOperationException("synthetic block failure");
            if (!ReferenceEquals(cleanup.Invoke(null, new object[] { error }), error) || active.GetValue(null) != null)
                throw new Exception("Failed block retained polearm35 state or swallowed the original exception.");
        }
        finally { active.SetValue(null, original); }

        Console.WriteLine("PASS: approved Sword35/70 balance (5 constants) and Polearm35 cancellation/exception cleanup (3 cases). Animation/VFX remain LIVE_TEST_REQUIRED.");
        return 0;
    }

    private static void Constant(Type type, string name, float expected)
    {
        float actual = (float)type.GetField(name, Static)!.GetRawConstantValue()!;
        if (MathF.Abs(actual - expected) > 0.00001f)
            throw new Exception($"Approved sword balance changed: {name}={actual}; expected {expected}.");
    }
}
