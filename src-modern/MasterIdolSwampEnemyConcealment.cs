using HarmonyLib;

namespace ValheimMastery
{
    // NPC-local perception only. Never change player visibility, combat or damage state.
    internal static class MasterIdolSwampEnemyConcealment
    {
        internal static bool Hidden(BaseAI observer, Character target)
        {
            if (!(target is Player) || observer == null ||
                observer.m_nview == null || !observer.m_nview.IsValid() || !observer.m_nview.IsOwner())
                return false;
            var creature = observer.m_character;
            if (creature == null || creature is Player || creature.IsDead() || creature.IsTamed() ||
                !BaseAI.IsEnemy(creature, target)) return false;
            // This provider already enforces current projection/session readiness and exact dome geometry.
            return MasterIdolSwamp.Contains(target.transform.position);
        }

        internal static bool Perceive(BaseAI observer, Character target, ref bool result)
        {
            if (!Hidden(observer, target)) return true;
            result = false;
            return false;
        }

        internal static void DropCurrent(BaseAI observer)
        {
            if (observer is MonsterAI monster)
            {
                if (!Hidden(monster, monster.m_targetCreature)) return;
                monster.m_targetCreature = null;
                // Forget this player's pursuit point, but retain alert and any independent static target.
                monster.m_lastKnownTargetPos = monster.transform.position;
                monster.m_beenAtLastPos = true;
                monster.m_updateTargetTimer = 0;
                monster.SetTargetInfo(ZDOID.None);
            }
            else if (observer is AnimalAI animal)
            {
                if (!Hidden(animal, animal.m_target)) return;
                animal.m_target = null;
                animal.m_updateTargetTimer = 0;
                animal.SetTargetInfo(ZDOID.None);
            }
        }
    }

    [HarmonyPatch(typeof(BaseAI), "CanSenseTarget", new[] { typeof(Character), typeof(bool) })]
    internal static class MasterIdolSwampSensePatch
    {
        private static bool Prefix(BaseAI __instance, Character __0, ref bool __result) =>
            MasterIdolSwampEnemyConcealment.Perceive(__instance, __0, ref __result);
    }

    [HarmonyPatch(typeof(BaseAI), "CanSeeTarget", new[] { typeof(Character) })]
    internal static class MasterIdolSwampSeePatch
    {
        private static bool Prefix(BaseAI __instance, Character __0, ref bool __result) =>
            MasterIdolSwampEnemyConcealment.Perceive(__instance, __0, ref __result);
    }

    [HarmonyPatch(typeof(BaseAI), "CanHearTarget", new[] { typeof(Character) })]
    internal static class MasterIdolSwampHearPatch
    {
        private static bool Prefix(BaseAI __instance, Character __0, ref bool __result) =>
            MasterIdolSwampEnemyConcealment.Perceive(__instance, __0, ref __result);
    }

    [HarmonyPatch(typeof(BaseAI), "FindEnemy")]
    internal static class MasterIdolSwampFindEnemyPatch
    {
        // HuntPlayer's native closest-player fallback bypasses vision/hearing. No second search.
        private static void Postfix(BaseAI __instance, ref Character __result)
        {
            if (MasterIdolSwampEnemyConcealment.Hidden(__instance, __result)) __result = null;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), "SetTarget", new[] { typeof(Character) })]
    internal static class MasterIdolSwampSetTargetPatch
    {
        // OnDamaged still runs normally; only its concealed retaliation target is inadmissible.
        private static bool Prefix(MonsterAI __instance, Character __0) =>
            !MasterIdolSwampEnemyConcealment.Hidden(__instance, __0);
    }

    [HarmonyPatch(typeof(BaseAI), "UpdateAI", new[] { typeof(float) })]
    internal static class MasterIdolSwampCurrentTargetPatch
    {
        private static void Prefix(BaseAI __instance) => MasterIdolSwampEnemyConcealment.DropCurrent(__instance);
    }

    [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
    internal static class MasterIdolSwampUpdateTargetPatch
    {
        private static void Prefix(MonsterAI __instance) => MasterIdolSwampEnemyConcealment.DropCurrent(__instance);
    }
}
