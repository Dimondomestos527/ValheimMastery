using System;
using System.Collections.Generic;
using HarmonyLib;
namespace ValheimMastery
{
    internal static class Ashlands70Combat
    {
        internal sealed class Scope
        {
            internal Scope Previous;
            internal Player Victim;
            internal Character Attacker;
            internal bool Absorbed, Blocked, LostHealth, Own;
            internal Character Body;
            internal float EntryHealth, NestedHealthLoss;
            internal Dictionary<SE_Shield,float> ShieldTotals;
        }
        [ThreadStatic] internal static Scope Current;
        internal static Scope Begin(Character victim, HitData hit, bool onlyIfOutside)
        {
            Magic70Diagnostics.Incoming(victim, hit);
            bool eligible = victim is Player player && Ashlands70World.VictimOwner(player) && Ashlands70World.Orbit(player) != null &&
                hit != null && !PerkRuntimeService.IsPerkGenerated(hit);
            if (eligible && onlyIfOutside && Current?.Victim == victim) return null; // same native event
            if (!eligible && Current == null) return null;
            // A nested generated/ignored event masks the parent's flags until
            // its finalizer restores the prior frame, including exceptions.
            var scope = new Scope { Previous = Current, Body = victim, EntryHealth = victim?.GetHealth() ?? 0f,
                Victim = eligible ? (Player)victim : null, Attacker = eligible ? hit.GetAttacker() : null, Own = eligible };
            Current = scope; return scope;
        }
        internal static void End(Scope scope, Exception error)
        {
            if (scope == null) return;
            Current = scope.Previous; // Always unmask, before native reads or outgoing callbacks.
            float nestedLoss = scope.Body == null ? 0f : Math.Max(0f, scope.EntryHealth - scope.Body.GetHealth());
            for (Scope parent = scope.Previous; parent != null; parent = parent.Previous)
                if (parent.Body == scope.Body) { parent.NestedHealthLoss += nestedLoss; break; }
            if (scope.ShieldTotals != null)
                foreach (var shield in scope.ShieldTotals)
                    for (Scope parent = scope.Previous; parent != null; parent = parent.Previous)
                        if (parent.Body == shield.Key.m_character) { AddShield(parent, shield.Key, shield.Value); break; }
            Magic70Diagnostics.Outcome(scope, error);
            if (scope.Own && error == null && (scope.Absorbed || scope.Blocked || scope.LostHealth)) Ashlands70World.Retaliate(scope.Victim, scope.Attacker);
        }
        internal static float ShieldTotal(Scope frame,SE_Shield shield)
            => frame?.ShieldTotals != null && frame.ShieldTotals.TryGetValue(shield,out float total) ? total : 0f;
        internal static void AddShield(Scope frame,SE_Shield shield,float amount)
        {
            if(frame==null||amount<=0f)return;
            if(frame.ShieldTotals==null)frame.ShieldTotals=new Dictionary<SE_Shield,float>();
            frame.ShieldTotals[shield]=ShieldTotal(frame,shield)+amount;
        }
    }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Ashlands70IncomingScopePatch
    {
        [HarmonyPriority(Priority.First)] private static void Prefix(Character __instance, HitData hit, out Ashlands70Combat.Scope __state)
        { __state = Ashlands70Combat.Begin(__instance, hit, false); }
        private static Exception Finalizer(Ashlands70Combat.Scope __state, Exception __exception)
        { Ashlands70Combat.End(__state, __exception); return __exception; }
    }
    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
    internal static class Ashlands70BarrierReplyPatch
    {
        internal sealed class Absorption { internal float Damage,Nested; internal Ashlands70Combat.Scope Frame; internal bool Finished; }
        private static void Prefix(SE_Shield __instance, out Absorption __state)
        { var frame=Ashlands70Combat.Current;__state=new Absorption{Damage=__instance.m_damage,Frame=frame,Nested=Ashlands70Combat.ShieldTotal(frame,__instance)}; }
        private static void Postfix(SE_Shield __instance, Absorption __state)
        { Complete(__instance,__state,true); }
        private static Exception Finalizer(SE_Shield __instance,Absorption __state,Exception __exception)
        { Complete(__instance,__state,__exception==null);return __exception; }
        private static void Complete(SE_Shield __instance,Absorption __state,bool success)
        {
            var frame=Ashlands70Combat.Current;if(__state==null||__state.Finished||frame!=__state.Frame)return;__state.Finished=true;
            float own=__instance.m_damage-__state.Damage-(Ashlands70Combat.ShieldTotal(frame,__instance)-__state.Nested);
            if(own<=0f)return;Ashlands70Combat.AddShield(frame,__instance,own);
            if(success&&frame?.Own==true&&frame.Victim==__instance.m_character)frame.Absorbed=true;
        }
    }
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Ashlands70BlockReplyPatch
    {
        private static void Prefix(HitData hit, out float __state) { __state = hit.GetTotalBlockableDamage(); }
        private static void Postfix(Humanoid __instance, HitData hit, bool __result, float __state)
        { if (__result && Ashlands70Combat.Current?.Victim == __instance && hit.GetTotalBlockableDamage() < __state) Ashlands70Combat.Current.Blocked = true; }
    }
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class Ashlands70FinalDamagePatch
    {
        internal sealed class Resolution { internal float Health, NestedHealthLoss; internal Ashlands70Combat.Scope Own, Observed; }
        [HarmonyPriority(Priority.Last)] private static void Prefix(Character __instance, ref HitData hit, out Resolution __state)
        {
            __state = new Resolution { Health = __instance.GetHealth(), Own = Ashlands70Combat.Begin(__instance, hit, true) };
            __state.Observed = Ashlands70Combat.Current;
            __state.NestedHealthLoss = __state.Observed?.NestedHealthLoss ?? 0f;
            float factor = Ashlands70World.DamageMultiplier(__instance);
            if (hit != null && factor < 1f) { hit = hit.Clone(); hit.ApplyModifier(factor); }
        }
        private static Exception Finalizer(Character __instance, Resolution __state, Exception __exception)
        {
            if (__state != null && __exception == null && Ashlands70Combat.Current == __state.Observed &&
                __state.Health - __instance.GetHealth() > (Ashlands70Combat.Current?.NestedHealthLoss ?? 0f) - __state.NestedHealthLoss &&
                Ashlands70Combat.Current?.Victim == __instance)
                Ashlands70Combat.Current.LostHealth = true;
            Ashlands70Combat.End(__state?.Own, __exception); return __exception;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
    internal static class Ashlands70ArmorPatch
    { [HarmonyPriority(int.MinValue)] private static void Postfix(Player __instance, ref float __result) { if (Ashlands70World.Orbit(__instance) != null) __result += 20f; } }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
    internal static class Ashlands70HealthRegenPatch
    { private static void Postfix(SEMan __instance, ref float __0) { if (__instance.m_character is Player player && Ashlands70World.InGrove(player)) __0 *= 1.25f; } }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
    internal static class Ashlands70StaminaRegenPatch
    { private static void Postfix(SEMan __instance, ref float __0) { if (__instance.m_character is Player player && Ashlands70World.InGrove(player)) __0 *= 1.25f; } }
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyEitrRegen))]
    internal static class Ashlands70EitrRegenPatch
    {
        [HarmonyPriority(int.MinValue)] private static void Postfix(SEMan __instance, ref float __0)
        {
            if (__instance.m_character is Player player && Ashlands70World.InGrove(player))
                // Native UpdateStats adds equipment AFTER SEMan: compensate that
                // addend so the final native regeneration, including robes, is x1.25.
                __0 = Ashlands70Rules.EitrRegen(__0, player.GetEquipmentEitrRegenModifier());
        }
    }
}
