using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    internal static class Ashlands35BurstPatch
    {
        internal struct Frame { internal bool Captured; internal Ashlands35Dundr.Volley Volley; internal int Steps; }
        [ThreadStatic] internal static int ClusterSteps;
        private static void Prefix(Attack __instance, out Frame __state)
        {
            __state = new Frame { Captured = true, Volley = Ashlands35Dundr.CurrentBurst, Steps = ClusterSteps };
            Ashlands35Dundr.CurrentBurst = Ashlands35Dundr.Begin(__instance);
            ClusterSteps = Ashlands35ClusterCharge.Casts.TryGetValue(__instance, out var cast) ? cast.Steps : 0;
        }
        private static Exception Finalizer(Frame __state, Exception __exception)
        { if (__state.Captured) { Ashlands35Dundr.CurrentBurst = __state.Volley; ClusterSteps = __state.Steps; } return __exception; }
    }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class Ashlands35ProjectileSetupPatch
    {
        private static void Postfix(Projectile __instance, HitData hitData, ItemDrop.ItemData item)
        {
            Ashlands35Dundr.Bind(__instance, item, hitData);
            if (item?.m_dropPrefab?.name != "StaffClusterbomb") return;
            int steps = Ashlands35BurstPatch.ClusterSteps;
            if (__instance.name.StartsWith("staff_clusterbombstaff_projectile", StringComparison.Ordinal))
            {
                if (steps > 0) { __instance.m_spawnCount = Ashlands35Rules.Fragments(steps); __instance.m_hitVariant = (short)(7030 + steps);
                    if (__instance.m_originalHitData != null) __instance.m_originalHitData.m_variant = __instance.m_hitVariant; }
            }
            else if (__instance.name.StartsWith("staff_clusterbombstaff_splinter_projectile", StringComparison.Ordinal) &&
                hitData?.m_variant >= 7031 && hitData.m_variant <= 7036)
            { __instance.m_aoe *= Ashlands35Rules.Radius(hitData.m_variant - 7030); __instance.m_hitVariant = hitData.m_variant; }
        }
    }
    [HarmonyPatch(typeof(Projectile), "IsValidTarget")]
    internal static class Ashlands35DundrEmitterPatch
    {
        private static bool Prefix(Projectile __instance, IDestructible destr, ref bool __result)
        { if (!Ashlands35Dundr.IsEmitter(__instance, destr)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(Projectile), "OnHit")]
    internal static class Ashlands35ProjectileHitPatch
    {
        internal struct Frame { internal bool Captured; internal Ashlands35Dundr.Shot Previous; internal Player Extra; }
        private static void Prefix(Projectile __instance, out Frame __state)
        { __state = new Frame { Captured = true, Previous = Ashlands35Dundr.CurrentHit, Extra = Ashlands35Dundr.ExtraCaster };
            Ashlands35Dundr.CurrentHit = Ashlands35Dundr.For(__instance); Ashlands35Dundr.ExtraCaster = Ashlands35Dundr.BeginExtra(__instance); }
        private static Exception Finalizer(Frame __state, Exception __exception)
        { if (__state.Captured) { Ashlands35Dundr.CurrentHit = __state.Previous; Ashlands35Dundr.ExtraCaster = __state.Extra; } return __exception; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
    internal static class Ashlands35DundrZeroXpPatch
    {
        [HarmonyPriority(Priority.First)] private static bool Prefix(Player __instance, Skills.SkillType __0, float __1)
            => Ashlands35Dundr.RaiseXp(__instance, __0, __1);
    }
    [HarmonyPatch(typeof(ExperienceContext), nameof(ExperienceContext.ObserveHit))]
    internal static class Ashlands35DundrObserveXpPatch
    {
        [HarmonyPriority(Priority.First)] private static bool Prefix(HitData hit) => Ashlands35Dundr.ObserveXp(hit);
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Ashlands35DundrHitPatch
    { [HarmonyPriority(Priority.First)] private static void Prefix(Character __instance, ref HitData hit) => Ashlands35Dundr.Transport(__instance, ref hit); }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Ashlands35AcceptedDamagePatch
    {
        internal struct Frame { internal bool Captured; internal Ashlands35Dundr.Acceptance Previous, Current; }
        [HarmonyPriority(Priority.First)] private static void Prefix(Character __instance, ref HitData hit, out Frame __state)
        {
            __state = new Frame { Captured = true, Previous = Ashlands35Dundr.CurrentAcceptance };
            Ashlands35Summons.NormalizeBlast(__instance, ref hit);
            __state.Current = Ashlands35Dundr.Capture(__instance, ref hit);
            Ashlands35Dundr.CurrentAcceptance = __state.Current;
        }
        private static void Postfix(Frame __state, bool __runOriginal)
        { if (__runOriginal) Ashlands35Dundr.Confirm(__state.Current); }
        private static Exception Finalizer(Frame __state, Exception __exception)
        { if (__state.Captured) Ashlands35Dundr.CurrentAcceptance = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class Ashlands35AcceptedApplyPatch
    {
        [HarmonyPriority(Priority.First)] private static void Prefix(Character __instance, HitData hit, out Ashlands35Dundr.Acceptance __state)
            => __state = Ashlands35Dundr.Applying(__instance, hit);
        private static void Postfix(Ashlands35Dundr.Acceptance __state, bool __runOriginal)
        { if (__runOriginal && __state != null) __state.Applied = true; }
    }
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class Ashlands35VisualVariantPatch
    { private static void Prefix(ref int variant) { if (variant >= 7031 && variant <= 7036) variant = -1; } }
    [HarmonyPatch(typeof(SpawnAbility), "SetupAoe")]
    internal static class Ashlands35NativeBirthPatch
    { private static void Prefix(SpawnAbility __instance, Character __0) => Ashlands35Summons.Birth(__instance, __0); }
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class Ashlands35CharacterAwakePatch
    { private static void Postfix(Character __instance) { Ashlands35Summons.Attach(__instance); Ashlands35Presentation.Register(__instance); if (__instance is Player player) Ashlands35Dundr.Register(player); } }
    [HarmonyPatch(typeof(Character), nameof(Character.GetFaction))]
    internal static class Ashlands35TrollFactionPatch
    { private static void Postfix(Character __instance, ref Character.Faction __result)
        { if (Ashlands35Summons.Friendly(__instance)) __result = Character.Faction.Players; } }
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class Ashlands35TrollDamagePatch
    { private static void Prefix(Character __instance, ref HitData hit) => Ashlands35Summons.Damage(__instance, ref hit); }
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class Ashlands35SummonDeathPatch
    { private static void Prefix(Character __instance) => Ashlands35Summons.Death(__instance); }
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class Ashlands35RootAttackPatch
    { private static void Prefix(Attack __instance, Humanoid character) => Ashlands35Summons.Boost(__instance, character); }
    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class Ashlands35DeathBlastSetupPatch
    { [HarmonyPriority(Priority.Last)] private static void Postfix(Aoe __instance) => Ashlands35Summons.RestoreBlast(__instance); }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class Ashlands35SceneExitPatch
    { private static void Postfix() { Ashlands35ClusterCharge.Shutdown(); Ashlands35Summons.Shutdown(); } }
}
