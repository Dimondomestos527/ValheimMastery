using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ValheimMastery
{
    internal static class Crossbows35ParryStagger
    {
        internal const float TimedBlockCoefficient = 5f;
        internal const float StaggerDamageMultiplier = 1.75f;

        internal static float TimedBlockBonus(float native, Humanoid actor)
        {
            Player player = actor as Player;
            ItemDrop.ItemData blocker = player?.GetCurrentBlocker();
            if (MasteryPlugin.Settings?.Enabled.Value != true || player == null || player.IsDead() ||
                player.m_nview?.IsOwner() != true || blocker?.m_shared == null ||
                blocker.m_shared.m_skillType != Skills.SkillType.Crossbows || Crossbows35ReloadGuard.IsTraversalHook(blocker) ||
                !ReferenceEquals(blocker, player.GetCurrentWeapon()) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crossbows, 35)) return native;
            float raw = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crossbows);
            if (float.IsNaN(raw) || float.IsInfinity(raw) || raw < 35f || raw > 100f) return native;
            return TimedBlockCoefficient;
        }

        // Only invoked in the unchanged target-owner non-player stagger branch.
        internal static void ApplyStaggerModifier(HitData hit, float native, Character target)
        {
            hit.ApplyModifier(native);
            if (Crossbows35ShotMetadata.Consume(target, hit))
                hit.ApplyModifier(StaggerDamageMultiplier);
        }
    }

    [HarmonyPatch(typeof(Projectile), "OnHit")]
    internal static class Crossbows35ImpactPatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo damage = typeof(IDestructible).GetMethod(nameof(IDestructible.Damage), new[] { typeof(HitData) });
            MethodInfo wrapper = typeof(Crossbows35ShotMetadata).GetMethod(nameof(Crossbows35ShotMetadata.Deliver), BindingFlags.Static | BindingFlags.NonPublic);
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(damage))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    instruction.opcode = OpCodes.Call; instruction.operand = wrapper; count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Crossbows35 expected one direct native projectile damage call, got " + count);
        }
    }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Crossbows35StaggerPatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            MethodInfo apply = typeof(HitData).GetMethod(nameof(HitData.ApplyModifier), new[] { typeof(float) });
            MethodInfo stagger = typeof(Character).GetMethod(nameof(Character.IsStaggering));
            MethodInfo wrapper = typeof(Crossbows35ParryStagger).GetMethod(nameof(Crossbows35ParryStagger.ApplyStaggerModifier), BindingFlags.Static | BindingFlags.NonPublic);
            int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                if (i >= 2 && instruction.Calls(apply) && code[i - 2].opcode == OpCodes.Ldarg_2 &&
                    code[i - 1].opcode == OpCodes.Ldc_R4 && Equals(code[i - 1].operand, 2f) &&
                    code.Skip(Math.Max(0, i - 12)).Take(Math.Min(12, i)).Any(candidate => candidate.Calls(stagger)))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    load.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    yield return load;
                    instruction.opcode = OpCodes.Call; instruction.operand = wrapper; count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Crossbows35 expected one native stagger-critical modifier, got " + count);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Crossbows35ParryPatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo field = typeof(ItemDrop.ItemData.SharedData).GetField("m_timedBlockBonus");
            MethodInfo wrapper = typeof(Crossbows35ParryStagger).GetMethod(nameof(Crossbows35ParryStagger.TimedBlockBonus), BindingFlags.Static | BindingFlags.NonPublic);
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                {
                    count++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, wrapper);
                }
            }
            if (count != 2) throw new InvalidOperationException("Crossbows35 expected two native timed-block reads, got " + count);
        }
    }

}
