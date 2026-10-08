using System;
#pragma warning disable CS0649
using UnityEngine;

namespace ValheimMastery
{
    // Normalized event payloads used while legacy Harmony handlers are migrated one at a time.
    // They intentionally contain gameplay data only; feedback and networking remain downstream.
    internal sealed class ProjectileLaunchedEvent { internal Player Owner; internal Projectile Projectile; internal HitData Hit; internal Skills.SkillType Skill; }
    internal sealed class ProjectileImpactEvent { internal Player Owner; internal Projectile Projectile; internal Character Target; internal Vector3 Point; internal HitData Hit; }
    internal sealed class EnemyKilledEvent { internal Player Killer; internal Character Target; internal HitData Hit; internal Skills.SkillType Skill; }
    internal sealed class ParryEvent { internal Player Defender; internal Character Attacker; internal ItemDrop.ItemData Blocker; internal float BlockedDamage; }
    internal sealed class GuardBreakEvent { internal Player Defender; internal Character Attacker; internal ItemDrop.ItemData Blocker; }
    internal sealed class PerfectDodgeEvent { internal Player Player; internal Character Threat; }
    internal sealed class SprintStateEvent { internal Player Player; internal bool IsSprinting; internal float Stamina; }
    internal sealed class JumpEvent { internal Player Player; }
    internal sealed class SwimStateEvent { internal Player Player; internal bool IsSwimming; }
    internal sealed class CraftEvent { internal Player Player; internal ItemDrop.ItemData Item; internal CraftingStation Station; }
    internal sealed class UpgradeEvent { internal Player Player; internal ItemDrop.ItemData Item; internal CraftingStation Station; }
    internal sealed class RepairEvent { internal Player Player; internal ItemDrop.ItemData Item; internal CraftingStation Station; }
    internal sealed class BuildEvent { internal Player Player; internal Piece Piece; }
    internal sealed class HarvestEvent { internal Player Player; internal GameObject Source; internal Vector3 Position; }
    internal sealed class PlantEvent { internal Player Player; internal GameObject Plant; internal Vector3 Position; }
    internal sealed class FoodCreatedEvent { internal Player Cook; internal ItemDrop.ItemData Food; internal CookingStation Station; }
    internal sealed class FoodConsumedEvent { internal Player Player; internal ItemDrop.ItemData Food; }
    internal sealed class SkillXpEvent { internal Player Player; internal Skills.SkillType Skill; internal float BaseXp; internal float FinalXp; }

    internal static class MasteryExtendedEventBus
    {
        internal static event Action<ProjectileLaunchedEvent> ProjectileLaunched;
        internal static event Action<ProjectileImpactEvent> ProjectileImpact;
        internal static event Action<EnemyKilledEvent> EnemyKilled;
        internal static event Action<ParryEvent> Parry;
        internal static event Action<GuardBreakEvent> GuardBreak;
        internal static event Action<PerfectDodgeEvent> PerfectDodge;
        internal static event Action<SprintStateEvent> SprintState;
        internal static event Action<JumpEvent> Jump;
        internal static event Action<SwimStateEvent> SwimState;
        internal static event Action<CraftEvent> Craft;
        internal static event Action<UpgradeEvent> Upgrade;
        internal static event Action<RepairEvent> Repair;
        internal static event Action<BuildEvent> Build;
        internal static event Action<HarvestEvent> Harvest;
        internal static event Action<PlantEvent> Plant;
        internal static event Action<FoodCreatedEvent> FoodCreated;
        internal static event Action<FoodConsumedEvent> FoodConsumed;
        internal static event Action<SkillXpEvent> SkillXp;

        internal static void Publish(ProjectileLaunchedEvent value) => ProjectileLaunched?.Invoke(value);
        internal static void Publish(ProjectileImpactEvent value) => ProjectileImpact?.Invoke(value);
        internal static void Publish(EnemyKilledEvent value) => EnemyKilled?.Invoke(value);
        internal static void Publish(ParryEvent value) => Parry?.Invoke(value);
        internal static void Publish(GuardBreakEvent value) => GuardBreak?.Invoke(value);
        internal static void Publish(PerfectDodgeEvent value) => PerfectDodge?.Invoke(value);
        internal static void Publish(SprintStateEvent value) => SprintState?.Invoke(value);
        internal static void Publish(JumpEvent value) => Jump?.Invoke(value);
        internal static void Publish(SwimStateEvent value) => SwimState?.Invoke(value);
        internal static void Publish(CraftEvent value) => Craft?.Invoke(value);
        internal static void Publish(UpgradeEvent value) => Upgrade?.Invoke(value);
        internal static void Publish(RepairEvent value) => Repair?.Invoke(value);
        internal static void Publish(BuildEvent value) => Build?.Invoke(value);
        internal static void Publish(HarvestEvent value) => Harvest?.Invoke(value);
        internal static void Publish(PlantEvent value) => Plant?.Invoke(value);
        internal static void Publish(FoodCreatedEvent value) => FoodCreated?.Invoke(value);
        internal static void Publish(FoodConsumedEvent value) => FoodConsumed?.Invoke(value);
        internal static void Publish(SkillXpEvent value) => SkillXp?.Invoke(value);
    }
}