using System;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class MasteryHitContext
    {
        internal string SourcePerk = "";
        internal int GenerationDepth;
        internal bool IsGenerated;
        internal bool AllowSelfProc;
        internal bool IgnoreReflect;
        internal bool IgnoreExecution;
        internal bool IgnoreOverdrawPayload;
        internal bool AllowKnife70InitialProc;
        internal bool AllowShadowRecursion;
    }

    internal sealed class HitResolvedEvent
    {
        internal Player Attacker;
        internal Character Target;
        internal HitData Hit;
        internal Skills.SkillType Skill;
        internal Vector3 HitPoint;
        internal Vector3 Direction;
        internal bool IsPrimary;
        internal bool IsSecondary;
        internal bool IsProjectile;
        internal bool IsBackstab;
        internal bool TargetDied;
        internal MasteryHitContext Mastery;
    }

    internal sealed class AttackStartedEvent
    {
        internal Player Player;
        internal ItemDrop.ItemData Weapon;
        internal bool IsSecondary;
    }

    internal sealed class BlockEvent
    {
        internal Player Defender;
        internal Character Attacker;
        internal ItemDrop.ItemData Blocker;
        internal float BlockedDamage;
        internal bool IsPerfectParry;
        internal bool Succeeded;
    }

    internal static class MasteryEventBus
    {
        internal static event Action<HitResolvedEvent> HitResolved;
        internal static event Action<AttackStartedEvent> AttackStarted;
        internal static event Action<BlockEvent> BlockResolved;

        internal static void Publish(HitResolvedEvent value) => HitResolved?.Invoke(value);
        internal static void Publish(AttackStartedEvent value) => AttackStarted?.Invoke(value);
        internal static void Publish(BlockEvent value) => BlockResolved?.Invoke(value);
    }
}
