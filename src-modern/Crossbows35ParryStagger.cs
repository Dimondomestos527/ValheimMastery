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
        // Legacy type/file names retained; these two bonuses are skill progression,
        // not level-35 perks. Both gameplay and presentation consume these formulas.
        internal static float NormalizeLevel(float raw)
            => float.IsNaN(raw) || float.IsInfinity(raw) ? 0f : Math.Max(0f, Math.Min(100f, raw));
        internal static float LearnedLevel(Player player)
            => MasteryPlugin.Settings?.Enabled.Value == true && player != null
                ? NormalizeLevel(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Crossbows)) : 0f;
        internal static float ParryAddition(float level) => .04f * NormalizeLevel(level);
        internal static float StaggerTargetMultiplier(float level) => 1f + .01f * NormalizeLevel(level);
        internal static bool IsActualCrossbow(ItemDrop.ItemData item)
            => item?.m_shared?.m_skillType == Skills.SkillType.Crossbows && item.IsWeapon() &&
                item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Tool && !Crossbows35ReloadGuard.IsTraversalHook(item);

        internal static float TimedBlockBonus(float native, Humanoid actor)
        {
            Player player = actor as Player;
            ItemDrop.ItemData blocker = player?.GetCurrentBlocker();
            if (MasteryPlugin.Settings?.Enabled.Value != true || player == null || player.IsDead() ||
                player.m_nview?.IsOwner() != true || !IsActualCrossbow(blocker) ||
                !ReferenceEquals(blocker, player.GetCurrentWeapon())) return native;
            return native + ParryAddition(LearnedLevel(player));
        }

        // Only invoked in the unchanged target-owner non-player stagger branch.
        internal static void ApplyStaggerModifier(HitData hit, float native, Character target)
        {
            hit.ApplyModifier(native);
            if (Crossbows35ShotMetadata.TryConsume(target, hit, out float level) && level > 0f)
                hit.ApplyModifier(StaggerTargetMultiplier(level));
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
