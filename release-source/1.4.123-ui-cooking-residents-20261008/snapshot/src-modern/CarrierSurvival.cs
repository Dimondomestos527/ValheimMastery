using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Per-live-body state only. No cargo, roster, RPC or durable record mutation.
    internal sealed class CarrierSurvival : MonoBehaviour
    {
        private bool WasOwner;
        private float LastCombat;
        private float NextThreatScan;
        private Character Threat;
        private void Awake() { LastCombat = Time.time; }
        internal void MarkCombat() { LastCombat = Time.time; }
        private void Update()
        {
            Character character = GetComponent<Character>();
            if (!Magic70Carrier.IsCarrier(character)) return;
            if (!character.m_nview.IsOwner()) { WasOwner = false; return; }
            if (!WasOwner)
            {
                WasOwner = true;
                LastCombat = Time.time;
                NextThreatScan = 0f;
                Threat = null;
            }
            if (character.IsDead() || character.GetHealth() <= 0f) return;
            float desired = character.GetMaxHealthBase() * character.GetLevel();
            if (float.IsFinite(desired) && desired > 0f &&
                Mathf.Abs(character.GetMaxHealth() - desired) > .01f)
                character.SetupMaxHealth(); // scoped patch preserves the old live fraction

            MonsterAI ai = character.GetComponent<MonsterAI>();
            if (Time.time >= NextThreatScan)
            {
                NextThreatScan = Time.time + .5f;
                Threat = NearestThreat(character);
                if (ai != null)
                {
                    if (Threat != null)
                    {
                        // Native SetTarget cannot replace a nonnull target.
                        if (ai.GetTargetCreature() != Threat) ai.m_targetCreature = null;
                        ai.SetTarget(Threat);
                        ai.SetAlerted(true);
                    }
                    else
                    {
                        Character target = ai.GetTargetCreature();
                        if (target != null && (target.IsDead() ||
                            !BaseAI.IsEnemy(character, target) ||
                            Vector3.Distance(character.transform.position, target.transform.position) > CarrierSurvivalRules.FleeRange))
                            ClearThreat(ai);
                    }
                }
            }
            Character activeTarget = ai != null ? ai.GetTargetCreature() : null;
            bool fighting = IsThreatWithin(character, Threat, CarrierSurvivalRules.ThreatRadius) ||
                IsThreatWithin(character, activeTarget, CarrierSurvivalRules.FleeRange) || character.InAttack();
            if (fighting) { MarkCombat(); return; }
            float heal = CarrierSurvivalRules.RegenAmount(character.GetHealth(), character.GetMaxHealth(),
                Time.time - LastCombat, Time.deltaTime);
            if (heal > 0f) character.Heal(heal, false);
        }
        private static void ClearThreat(MonsterAI ai)
        {
            // Match native lost-target cleanup; keep m_follow for ordinary follow.
            // SetTarget(null) is a no-op in the installed native implementation.
            ai.m_targetCreature = null;
            ai.m_targetStatic = null;
            ai.m_timeSinceSensedTargetCreature = 0f;
            ai.m_timeSinceAttacking = 0f;
            ai.m_updateTargetTimer = 0f;
            ai.SetAlerted(false);
            ai.SetTargetInfo(ZDOID.None);
        }
        private static Character NearestThreat(Character carrier)
        {
            Character nearest = null;
            float best = CarrierSurvivalRules.ThreatRadius * CarrierSurvivalRules.ThreatRadius;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate.IsDead() || !BaseAI.IsEnemy(carrier, candidate)) continue;
                BaseAI ai = candidate.GetBaseAI();
                if (ai != null && ai.IsSleeping()) continue;
                float distance = (candidate.transform.position - carrier.transform.position).sqrMagnitude;
                if (distance <= best) { nearest = candidate; best = distance; }
            }
            return nearest;
        }
        private static bool IsThreatWithin(Character carrier, Character threat, float radius) =>
            threat != null && !threat.IsDead() && BaseAI.IsEnemy(carrier, threat) &&
            (threat.transform.position - carrier.transform.position).sqrMagnitude <= radius * radius;
    }

    // Native Awake may resize an existing full-HP body before our first Update.
    // Capture before SetupMaxHealth; do not multiply saved maxHP or heal portals.
    [HarmonyPatch(typeof(Character), "SetupMaxHealth")]
    internal static class CarrierMaxHealthPatch
    {
        internal struct State { internal bool Apply; internal float Health, Max; }
        private static void Prefix(Character __instance, out State __state)
        {
            __state = default;
            if (!Magic70Carrier.IsCarrier(__instance) || !__instance.m_nview.IsOwner()) return;
            __state.Health = __instance.GetHealth();
            __state.Max = __instance.GetMaxHealth();
            __state.Apply = __state.Health > 0f && __state.Max > 0f;
        }
        private static void Postfix(Character __instance, State __state)
        {
            if (!__state.Apply || !Magic70Carrier.IsCarrier(__instance) || !__instance.m_nview.IsOwner()) return;
            float max = __instance.GetMaxHealth();
            if (Mathf.Abs(max - __state.Max) <= .01f || __instance.GetHealth() <= 0f || __instance.IsDead()) return;
            __instance.SetHealth(CarrierSurvivalRules.ResizedHealth(__state.Health, __state.Max, max));
        }
    }

    [HarmonyPatch(typeof(BaseAI), "UpdateRegeneration")]
    internal static class CarrierNativeRegenPatch
    {
        private static bool Prefix(BaseAI __instance) =>
            !Magic70Carrier.IsCarrier(__instance.GetComponent<Character>());
    }

    [HarmonyPatch(typeof(BaseAI), "OnDamaged")]
    internal static class CarrierCombatDamagePatch
    {
        private static void Postfix(BaseAI __instance, float damage)
        {
            if (damage <= 0f) return;
            Character character = __instance.GetComponent<Character>();
            if (Magic70Carrier.IsCarrier(character) && character.m_nview.IsOwner())
                character.GetComponent<CarrierSurvival>()?.MarkCombat();
        }
    }

    // Change ranking only when the native valid result is Torba. Keep native
    // faction/sensing/sleep/cinematic filters and still target Torba when alone.
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
    internal static class CarrierEnemyPreferencePatch
    {
        private static void Postfix(BaseAI __instance, ref Character __result)
        {
            if (!Magic70Carrier.IsCarrier(__result)) return;
            Character searcher = __instance.GetComponent<Character>();
            if (searcher == null || !BaseAI.IsEnemy(searcher, __result)) return;
            float carrierDistance = Vector3.Distance(searcher.transform.position, __result.transform.position);
            Character alternative = null;
            float best = carrierDistance * CarrierSurvivalRules.TargetDistancePenalty;
            foreach (Character candidate in Character.GetAllCharacters())
            {
                if (candidate == null || candidate.IsDead() || candidate.m_aiSkipTarget ||
                    Magic70Carrier.IsCarrier(candidate) || !BaseAI.IsEnemy(searcher, candidate) ||
                    (candidate == Player.m_localPlayer && CinematicsManager.IsPlaying())) continue;
                BaseAI ai = candidate.GetBaseAI();
                if (ai != null && ai.IsSleeping()) continue;
                float distance = Vector3.Distance(searcher.transform.position, candidate.transform.position);
                if (distance >= best || !__instance.CanSenseTarget(candidate)) continue;
                alternative = candidate; best = distance;
            }
            if (alternative != null && CarrierSurvivalRules.PreferAlternate(carrierDistance, best)) __result = alternative;
        }
    }
}

