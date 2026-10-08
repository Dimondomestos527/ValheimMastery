using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class VeiledState
    {
        internal float Since = -1f, NextCheck, BlockUntil;
        internal bool Active;
    }
    internal static class VeiledService
    {
        private static readonly int VeilKey = "vm_movement_veil_until".GetStableHashCode();
        internal static bool CheckingSight;
        internal static bool Active(Player p)
        {
            if (p == null || p.IsDead() || !p.IsCrouching() || p.InAttack() || p.IsRunning()) return false;
            if (p == Player.m_localPlayer) return MasteryStateStore.TryGetPlayerState<VeiledState>(p, out var s) && s.Active;
            // Owner-authored, short lease: same vanilla trust as the agreed skill model.
            return ZNet.instance != null && (p.m_nview?.GetZDO()?.GetLong(VeilKey, 0L) ?? 0L) > (long)(ZNet.instance.GetTimeSeconds() * 1000d);
        }
        internal static void Break(Player p)
        {
            if (p == null || p != Player.m_localPlayer) return;
            var s = MasteryStateStore.GetPlayerState<VeiledState>(p);
            s.Since = -1f; s.BlockUntil = Time.time + .25f;
            if (s.Active) { s.Active = false; p.m_nview?.GetZDO()?.Set(VeilKey, 0L); }
        }
        internal static void Update(Player p)
        {
            if (p == null || p != Player.m_localPlayer) return;
            var s = MasteryStateStore.GetPlayerState<VeiledState>(p);
            if (!MovementPerkService.Valid(p) || !p.IsCrouching() || p.m_run || p.InAttack() || p.InDodge() || p.IsSwimming() || !PerkRuntimeService.HasPerk(p, Skills.SkillType.Sneak, 70)) { Break(p); return; }
            if (Time.time < s.NextCheck || Time.time < s.BlockUntil) return;
            s.NextCheck = Time.time + .2f;
            bool observed = false;
            // Five bounded-frequency scans per second, cheap distance/enemy gates before LOS.
            foreach (BaseAI ai in BaseAI.GetAllInstances())
            {
                if (ai == null || ai.m_character == null || ai.m_character.IsDead() || !ai.IsEnemy(p)) continue;
                float distance = (ai.transform.position - p.transform.position).sqrMagnitude;
                if (distance <= 9f) { Break(p); return; }
                if (s.Active || distance > ai.m_viewRange * ai.m_viewRange) continue;
                try { CheckingSight = true; observed = ai.CanSeeTarget(p); }
                finally { CheckingSight = false; }
                if (observed) break;
            }
            if (observed) { s.Since = -1f; return; }
            if (!s.Active)
            {
                if (s.Since < 0f) s.Since = Time.time;
                if (Time.time - s.Since < 1.8f) return;
                s.Active = true;
                // Strictly local: never advertise this player's veil to nearby enemies/clients.
                VfxRecipeService.Play("sneak_70_veil", p, p.transform.position);
                PerkProcHudService.Show(p, "sneak_70", Skills.SkillType.Sneak, 70);
            }
            if (ZNet.instance != null) p.m_nview?.GetZDO()?.Set(VeilKey, (long)((ZNet.instance.GetTimeSeconds() + .8d) * 1000d));
        }
        internal static bool Hidden(Transform observer, Character target) => !CheckingSight && target is Player p && Active(p) && observer != null && (observer.position - p.transform.position).sqrMagnitude > 9f;
    }
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(bool), typeof(Character) })]
    internal static class VeiledSightPatch
    {
        private static bool Prefix(Transform me, Character target, ref bool __result)
        {
            if (!VeiledService.Hidden(me, target)) return true;
            // Do not erase an alerted observer's directly visible current target. Veiled
            // helps reacquisition after broken LOS, not vanishing in front of an attacker.
            var observer = me.GetComponent<MonsterAI>();
            if (observer != null && observer.IsAlerted() && observer.m_targetCreature == target) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), new[] { typeof(Transform), typeof(float), typeof(Character) })]
    internal static class VeiledHearingPatch
    {
        private static bool Prefix(Transform me, Character target, ref bool __result)
        { if (!VeiledService.Hidden(me, target)) return true; __result = false; return false; }
    }
    [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
    internal static class VeiledRetentionPatch
    {
        private static void Prefix(MonsterAI __instance, float dt)
        {
            // Accelerate only this observer's naturally unsensed target timeout. No global aggro reset.
            var p = __instance.m_targetCreature as Player;
            if (p == null || !VeiledService.Hidden(__instance.transform, p) || __instance.m_nview == null || !__instance.m_nview.IsOwner()) return;
            bool visible;
            try { VeiledService.CheckingSight = true; visible = __instance.CanSeeTarget(p); }
            finally { VeiledService.CheckingSight = false; }
            if (!visible) __instance.m_timeSinceSensedTargetCreature += dt * 5f;
        }
    }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class VeiledAttackBreakPatch
    { private static void Prefix(Humanoid __instance) { if (__instance is Player p) VeiledService.Break(p); } }
    [HarmonyPatch(typeof(Player), nameof(Player.OnDamaged))]
    internal static class VeiledDamageBreakPatch
    { private static void Postfix(Player __instance, HitData hit) { if (MovementPerkService.DirectAttack(__instance, hit)) VeiledService.Break(__instance); } }
}
