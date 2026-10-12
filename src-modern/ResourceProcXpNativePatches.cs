using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class ResourceProcAttackPatch
    {
        private static void Prefix(Attack __instance, out FullResourceProcExperience.AttackFrame __state)
        {
            __state = new FullResourceProcExperience.AttackFrame { Previous = FullResourceProcExperience.CurrentAttack };
            try { FullResourceProcExperience.BeginAttack(__instance, out __state); }
            catch (Exception failure) { ResourceProcXpTransport.Fault(failure); FullResourceProcExperience.CurrentAttack = null; }
        }
        private static Exception Finalizer(FullResourceProcExperience.AttackFrame __state, Exception __exception)
        { FullResourceProcExperience.FinishAttack(__state, __exception); return __exception; }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo native = AccessTools.Method(typeof(Character), nameof(Character.RaiseSkill), new[] { typeof(Skills.SkillType), typeof(float) });
            MethodInfo wrapper = AccessTools.Method(typeof(FullResourceProcExperience), nameof(FullResourceProcExperience.RaiseOrdinary));
            int count = 0;
            foreach (var instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) && Equals(instruction.operand, native))
                { count++; instruction.opcode = OpCodes.Call; instruction.operand = wrapper; }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Resource proc XP expected one native melee award, got " + count);
        }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    internal static class ResourceProcIncomingPatch
    {
        private static void Prefix(ZRpc __0, out ZRpc __state) { __state = ResourceProcXpTransport.Incoming; ResourceProcXpTransport.Incoming = __0; }
        private static Exception Finalizer(ZRpc __state, Exception __exception) { ResourceProcXpTransport.Incoming = __state; return __exception; }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "HandleRoutedRPC")]
    internal static class ResourceProcHandlePatch
    {
        private static bool Prefix(ZRoutedRpc.RoutedRPCData __0, out ResourceProcXpTransport.NativeState __state)
        { ResourceProcXpTransport.BeginHandle(__0, out __state); return ResourceProcXpTransport.AllowManaged(__0); }
        private static Exception Finalizer(ResourceProcXpTransport.NativeState __state, Exception __exception)
        { ResourceProcXpTransport.FinishHandle(__state); return __exception; }
    }
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class ResourceProcRoutePatch
    { private static bool Prefix(ZRoutedRpc.RoutedRPCData __0) => ResourceProcXpTransport.BeforeRoute(__0); }
    [HarmonyPatch(typeof(NetworkSync), nameof(NetworkSync.Register))]
    internal static class ResourceProcRegistrationPatch
    { private static void Postfix() => ResourceProcXpTransport.Register(); }
    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class ResourceProcDeathPatch
    { private static void Prefix(Player __instance) => FullResourceProcExperience.ForgetActor(__instance); }
}
