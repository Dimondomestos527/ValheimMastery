using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal struct IceFlyingState { internal bool Active; }
    internal static class IceStaff35FlyingPressure
    {
        internal static bool Prepare(BaseAI ai, out IceFlyingState state)
        {
            state = new IceFlyingState();
            Character creature = ai?.m_character;
            if (creature == null || !MasteryPlugin.Settings.Enabled.Value || creature.IsDead() ||
                creature.IsBoss() || creature.IsPlayer() || creature.IsTamed() ||
                (!creature.IsFlying() && !ai.m_randomFly) || creature.m_nview?.IsValid() != true ||
                !creature.m_nview.IsOwner()) return false;
            SEMan effects = creature.GetSEMan();
            bool trail = effects?.HaveStatusEffect(IceStaff35Trail.EffectHash) == true &&
                effects.GetStatusEffect(IceStaff35Trail.EffectHash)?.IsDone() == false;
            bool frost = effects?.HaveStatusEffect(Ice35Exposure.MasterFrostHash) == true &&
                effects.GetStatusEffect(Ice35Exposure.MasterFrostHash)?.IsDone() == false &&
                effects.HaveStatusEffect(IceStaff35Trail.NativeFrostHash) &&
                effects.GetStatusEffect(IceStaff35Trail.NativeFrostHash)?.IsDone() == false;
            if (!trail && !frost) return false;
            // Native SE_Frost does not slow these resistance categories. Merely
            // receiving a trail marker must never circumvent frost resistance.
            HitData.DamageModifier resistance = creature.GetDamageModifiers().m_frost;
            if (resistance == HitData.DamageModifier.Immune || resistance == HitData.DamageModifier.Ignore ||
                resistance == HitData.DamageModifier.Resistant || resistance == HitData.DamageModifier.VeryResistant ||
                resistance == HitData.DamageModifier.SlightlyResistant) return false;
            state.Active = true;
            return true;
        }
        internal static void ApplyFlying(Character creature, float dt)
        {
            BaseAI ai = creature?.m_baseAI;
            if (creature?.m_body == null || !Prepare(ai, out _) || !creature.IsFlying()) return;
            Ice35FlightControl control = creature.GetComponent<Ice35FlightControl>();
            if (control == null) control = creature.gameObject.AddComponent<Ice35FlightControl>();
            control.Apply(dt);
        }
    }

    // Local, per-live-owner controller. No saved state or status-effect extension.
    internal sealed class Ice35FlightControl : MonoBehaviour
    {
        private Character Creature;
        private bool ForcedLanding;
        private void Awake() => Creature = GetComponent<Character>();
        private void Update()
        {
            if (Creature?.m_nview?.IsValid() != true || !Creature.m_nview.IsOwner() || Creature.IsDead())
            { ForcedLanding = false; return; }
            if (IceStaff35FlyingPressure.Prepare(Creature.m_baseAI, out _)) return;
            // Resume only a body this controller explicitly landed; a naturally
            // grounded frozen creature is never forced into flight.
            if (ForcedLanding && !Creature.InAttack() && !Creature.IsStaggering())
            {
                ForcedLanding = false;
                if (!Creature.IsFlying()) Creature.TakeOff();
            }
        }
        internal void Apply(float dt)
        {
            if (Creature?.m_body == null || Creature.m_collider == null || dt <= 0f || !float.IsFinite(dt)) return;
            Vector3 position = Creature.m_body.position;
            Bounds bounds = Creature.m_collider.bounds;
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            Vector3 origin = new Vector3(position.x, bounds.center.y, position.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit ground, 256f, mask, QueryTriggerInteraction.Ignore)) return;
            float floor = ground.point.y;
            float liquid = SampleLiquid(ground.point);
            bool currentWet = liquid > floor + .05f || Creature.AboveOrInLava();
            Vector3 predicted = origin + new Vector3(Creature.m_currentVel.x, 0f, Creature.m_currentVel.z) * Mathf.Min(dt, .1f);
            if (Physics.Raycast(predicted, Vector3.down, out RaycastHit ahead, 256f, mask, QueryTriggerInteraction.Ignore))
            { floor = Mathf.Max(floor, ahead.point.y); liquid = Mathf.Max(liquid, SampleLiquid(ahead.point)); }
            bool wet = liquid > floor + .05f || Creature.AboveOrInLava();
            if (wet) floor = Mathf.Max(floor, liquid + .2f);
            float bottomOffset = bounds.min.y - position.y;
            float desired = floor - bottomOffset + .15f;
            Creature.m_currentVel.y = MagicPresentationRules.VerticalSpeed(position.y, desired, dt);
            BaseAI ai = Creature.m_baseAI;
            float supportGap = bounds.min.y - ground.point.y;
            if (!currentWet && !wet && ground.normal.y >= .75f && ai?.m_randomFly == true &&
                supportGap >= -.05f && supportGap <= .35f && Mathf.Abs(position.y - desired) <= .2f && !Creature.InAttack() && !Creature.IsStaggering())
            {
                ForcedLanding = true;
                Creature.Land();
            }
        }
        private float SampleLiquid(Vector3 ground)
        {
            // Sample the volume at the floor before entering its trigger. The
            // character's cached liquid level alone is unknown while airborne.
            float sampled = Floating.GetLiquidLevel(ground + Vector3.up * .1f, 1f, LiquidType.All);
            if (sampled <= -9999f && ZoneSystem.instance != null)
                sampled = ZoneSystem.instance.m_waterLevel; // conservative ocean plane
            return Mathf.Max(sampled, Creature.GetLiquidLevel());
        }
    }

    [HarmonyPatch(typeof(Character), "UpdateFlying")]
    internal static class IceFlyingPhysicsPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            var anchor = AccessTools.Method(typeof(Character), "AddPushbackForce");
            int count = code.Count(i => i.Calls(anchor));
            if (count != 1)
            {
                MasteryPlugin.Log?.LogWarning("[Ice35Flying] unexpected native flying physics; pressure skipped");
                return code;
            }
            int index = code.FindIndex(i => i.Calls(anchor)) + 1;
            CodeInstruction receiver = new CodeInstruction(OpCodes.Ldarg_0);
            if (index < code.Count) { receiver.labels.AddRange(code[index].labels); code[index].labels.Clear(); }
            code.InsertRange(index, new[] { receiver, new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(IceStaff35FlyingPressure), nameof(IceStaff35FlyingPressure.ApplyFlying))) });
            return code;
        }
    }
    [HarmonyPatch(typeof(BaseAI), "UpdateTakeoffLanding")]
    internal static class IceFlyingTakeoffPatch
    {
        private static bool Prefix(BaseAI __instance) => !__instance.m_randomFly ||
            !IceStaff35FlyingPressure.Prepare(__instance, out _);
    }
}
