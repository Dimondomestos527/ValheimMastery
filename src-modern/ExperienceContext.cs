using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ValheimMastery
{
    internal static class ExperienceContext
    {
        private sealed class PendingContext
        {
            internal int TargetTier;
            internal float ExplicitTierMultiplier;
            internal int ItemTier;
            internal string ItemKey;
            internal string TargetKey;
            internal float DodgeThreat = 1f;
            internal float CreatedAt;
            internal bool FixedHookTraversal;
        }

        private static readonly Dictionary<Skills.SkillType, PendingContext> Pending = new Dictionary<Skills.SkillType, PendingContext>();
        private const float ContextLifetime = 1.25f;

        internal static bool IsManaged(Skills.SkillType skill) => PerkCatalog.Contains(skill);

        internal static void ObserveHit(Component target, HitData hit)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || hit == null || !IsManaged(hit.m_skill)) return;
            Player player = Player.m_localPlayer;
            if (player == null || hit.GetAttacker() != player) return;
            int targetTier = GatheringProgressionService.IsGatheringSkill(hit.m_skill)
                ? GatheringProgressionService.GetResourceTier(target, hit.m_skill)
                : TierDatabase.GetTargetTier(target);
            Set(hit.m_skill, targetTier, TierDatabase.GetItemTier(player.GetCurrentWeapon()), TierDatabase.ItemKey(player.GetCurrentWeapon()), TierDatabase.TargetKey(target), 1f);
        }

        internal static void ObserveBlock(Player player, Character attacker)
        {
            if (player == null || player != Player.m_localPlayer || attacker == null) return;
            ItemDrop.ItemData blocker = player.GetCurrentBlocker();
            Set(Skills.SkillType.Blocking, TierDatabase.GetTargetTier(attacker), TierDatabase.GetItemTier(blocker), TierDatabase.ItemKey(blocker), TierDatabase.TargetKey(attacker), 1f);
        }

        internal static void ObserveDodge(Player player, Character attacker)
        {
            if (player == null || player != Player.m_localPlayer || attacker == null) return;
            Set(Skills.SkillType.Dodge, TierDatabase.GetTargetTier(attacker), 1, "dodge", TierDatabase.TargetKey(attacker), TierDatabase.GetDodgeThreatMultiplier(attacker));
        }

        // Used by cooking, crafting, farming and building. The unique key is the actual recipe, plant or piece.
        internal static void ObserveAction(Player player, Skills.SkillType skill, string actionKey)
        {
            if (player == null || player != Player.m_localPlayer || string.IsNullOrEmpty(actionKey)) return;
            Set(skill, 1, 1, actionKey, "action", 1f);
        }

        internal static void ObserveGatheringAction(Player player, Skills.SkillType skill, string actionKey, int resourceTier)
        {
            if (player == null || player != Player.m_localPlayer || string.IsNullOrEmpty(actionKey)) return;
            Set(skill, Mathf.Max(1, resourceTier), 1, actionKey, actionKey, 1f);
        }

        private sealed class ActionScope : IDisposable
        {
            internal Skills.SkillType Skill;
            internal PendingContext Previous;
            internal bool Changed, HadPrevious, Disposed;
            public void Dispose()
            {
                if (Disposed) return; Disposed = true;
                if (!Changed) return;
                if (HadPrevious) Pending[Skill] = Previous;
                else Pending.Remove(Skill);
            }
        }
        // A traversal award must not borrow a recent bolt's tiers or leave its
        // action key behind for the next ordinary hit. Native RaiseSkill scales once.
        internal static IDisposable BeginAction(Player player, Skills.SkillType skill, string actionKey)
        {
            var scope = new ActionScope { Skill = skill };
            if (player == null || player != Player.m_localPlayer || string.IsNullOrEmpty(actionKey)) return scope;
            scope.Changed = true;
            scope.HadPrevious = Pending.TryGetValue(skill, out scope.Previous);
            ObserveAction(player, skill, actionKey);
            return scope;
        }

        // Fixed hook XP is native Crossbows base × canonical item tier × boss.
        // It must not inherit a recent hit or consume/multiply a discovery key.
        internal static IDisposable BeginFixedHookTraversal(Player player, int itemTier)
        {
            var scope = new ActionScope { Skill = Skills.SkillType.Crossbows };
            if (player == null || player != Player.m_localPlayer || itemTier < 1) return scope;
            scope.Changed = true;
            scope.HadPrevious = Pending.TryGetValue(scope.Skill, out scope.Previous);
            Set(scope.Skill, 1, itemTier, "grapple.travel", "action", 1f);
            Pending[scope.Skill].FixedHookTraversal = true;
            return scope;
        }

        // Blood events retain the historical action/action first-unique receipt.
        // Only their tiers change; a cast/chunk/new creature cannot mint a new 10x key.
        internal static IDisposable BeginBloodCombat(Player player, float targetTier, string staff)
        {
            var scope = new ActionScope { Skill = Skills.SkillType.BloodMagic };
            if (player == null || player != Player.m_localPlayer ||
                !BloodMagicXpRules.Finite(targetTier) || targetTier < 1f || targetTier > 8f ||
                (staff != "StaffSkeleton" && staff != "StaffShield")) return scope;
            scope.Changed = true;
            scope.HadPrevious = Pending.TryGetValue(scope.Skill, out scope.Previous);
            Set(scope.Skill, 1, 1, "action", "action", 1f);
            Pending[scope.Skill].ExplicitTierMultiplier = 1f + MasteryRuntime.TierStep *
                (targetTier - 1f + TierDatabase.GetTargetBloodStaffTier(staff) - 1f);
            return scope;
        }

        private sealed class CookingRecipeContext { internal float Coefficient; internal bool WorldResources; }
        [ThreadStatic] private static CookingRecipeContext ActiveCookingRecipe;
        private sealed class CookingRecipeScope : IDisposable
        {
            internal CookingRecipeContext Previous;
            internal PendingContext PreviousPending;
            internal bool HadPending, ChangedPending, Disposed;
            public void Dispose()
            {
                if (Disposed) return; Disposed = true;
                ActiveCookingRecipe = Previous;
                if (!ChangedPending) return;
                if (HadPending) Pending[Skills.SkillType.Cooking] = PreviousPending;
                else Pending.Remove(Skills.SkillType.Cooking);
            }
        }
        internal static IDisposable BeginCookingRecipe(Player player, float coefficient, bool worldResources, string actionKey = null)
        {
            var scope = new CookingRecipeScope { Previous = ActiveCookingRecipe };
            if (player != null && player == Player.m_localPlayer)
            {
                if (!string.IsNullOrEmpty(actionKey))
                {
                    scope.ChangedPending = true;
                    scope.HadPending = Pending.TryGetValue(Skills.SkillType.Cooking, out scope.PreviousPending);
                    ObserveAction(player, Skills.SkillType.Cooking, actionKey);
                }
                ActiveCookingRecipe = new CookingRecipeContext
                { Coefficient = MeadXpReceiptPolicy.ValidCoefficient(coefficient) ? coefficient : 1f, WorldResources = worldResources };
            }
            return scope;
        }

        internal static float Scale(Skills.SkillType skill, float baseXp)
        {
            if (!MasteryPlugin.Settings.Enabled.Value || Player.m_localPlayer == null) return baseXp;

            Player player = Player.m_localPlayer;
            float bossModifier = WorldProgressionXpService.GetBossXpMultiplier();
            if (!IsManaged(skill)) return Log(skill, baseXp, bossModifier, 1f, 1f, 1f, 1f, baseXp * bossModifier);

            PendingContext context = GetContext(skill);
            float tierMultiplier = GatheringProgressionService.IsGatheringSkill(skill)
                ? GatheringProgressionService.GetTierMultiplier(skill, context.TargetTier, context.ItemTier)
                : (UsesTierFormula(skill)
                    ? 1f + MasteryRuntime.TierStep * (context.TargetTier - 1) + MasteryRuntime.TierStep * (context.ItemTier - 1)
                    : 1f);
            if (skill == Skills.SkillType.BloodMagic && context.ExplicitTierMultiplier > 0f)
                tierMultiplier = context.ExplicitTierMultiplier;
            if (skill == Skills.SkillType.Cooking && ActiveCookingRecipe != null) tierMultiplier *= ActiveCookingRecipe.Coefficient;
            float skillMultiplier = GetSmoothSkillMultiplier(player, skill);
            if (skill == Skills.SkillType.Dodge)
                skillMultiplier *= MasteryRuntime.DodgeXpCoefficient * context.DodgeThreat;

            bool firstUnique = !context.FixedHookTraversal && TryConsumeFirstUnique(player, skill, context.ItemKey, context.TargetKey);
            float firstTimeMultiplier = firstUnique
                ? (skill == Skills.SkillType.Crafting ? Mathf.Min(MasteryRuntime.FirstUniqueMultiplier, 2f) : MasteryRuntime.FirstUniqueMultiplier)
                : 1f;
            // Game.m_resourceRate is the server-synchronized vanilla world modifier used by
            // DropTable and PickableItem. Retain the existing gathering-only policy;
            // the approved exception is the three scoped mead stages, once and last.
            float resourceMultiplier = (UsesWorldResourceRate(skill) || (skill == Skills.SkillType.Cooking && ActiveCookingRecipe?.WorldResources == true)) && Game.m_resourceRate > 0f ? Game.m_resourceRate : 1f;
            float finalXp = baseXp * tierMultiplier * skillMultiplier * firstTimeMultiplier * bossModifier * resourceMultiplier;
            return Log(skill, baseXp, bossModifier, tierMultiplier, skillMultiplier, firstTimeMultiplier, resourceMultiplier, finalXp);
        }

        internal static string ActionKey(Skills.SkillType skill) => GetContext(skill).ItemKey;

        private static PendingContext GetContext(Skills.SkillType skill)
        {
            if (Pending.TryGetValue(skill, out PendingContext context) && (context.FixedHookTraversal || Time.time - context.CreatedAt <= ContextLifetime)) return context;
            return new PendingContext { TargetTier = 1, ItemTier = 1, ItemKey = "action", TargetKey = "action", CreatedAt = Time.time };
        }

        private static void Set(Skills.SkillType skill, int targetTier, int itemTier, string itemKey, string targetKey, float dodgeThreat)
        {
            Pending[skill] = new PendingContext
            {
                TargetTier = Mathf.Max(1, targetTier), ItemTier = Mathf.Max(1, itemTier),
                ItemKey = itemKey ?? "action", TargetKey = targetKey ?? "action",
                DodgeThreat = Mathf.Max(1f, dodgeThreat), CreatedAt = Time.time
            };
        }

        private static bool UsesTierFormula(Skills.SkillType skill)
        {
            switch (skill)
            {
                case Skills.SkillType.Swords: case Skills.SkillType.Axes: case Skills.SkillType.Clubs:
                case Skills.SkillType.Knives: case Skills.SkillType.Spears: case Skills.SkillType.Polearms:
                case Skills.SkillType.Bows: case Skills.SkillType.Crossbows: case Skills.SkillType.Unarmed:
                case Skills.SkillType.Pickaxes: case Skills.SkillType.WoodCutting: case Skills.SkillType.Blocking:
                // Magic intentionally retains its pre-existing formula and data set in this patch.
                case Skills.SkillType.ElementalMagic: case Skills.SkillType.BloodMagic:
                    return true;
                default: return false;
            }
        }

        private static bool UsesWorldResourceRate(Skills.SkillType skill)
        {
            return skill == Skills.SkillType.Farming || skill == Skills.SkillType.WoodCutting || skill == Skills.SkillType.Pickaxes;
        }

        // Linear ramps counter vanilla's steep late-level grind without turning early progression into a free level-100.
        private static float GetSmoothSkillMultiplier(Player player, Skills.SkillType skill)
        {
            float level = Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, skill), 0f, 100f);
            float t = level / 100f;
            switch (skill)
            {
                case Skills.SkillType.Swim: case Skills.SkillType.Fishing: return Mathf.Lerp(2.50f, 3.25f, t);
                case Skills.SkillType.Ride: return Mathf.Lerp(2.35f, 3.00f, t);
                case Skills.SkillType.Sneak: return Mathf.Lerp(1.75f, 2.25f, t);
                case Skills.SkillType.Run: case Skills.SkillType.Jump: return Mathf.Lerp(1.25f, 1.60f, t);
                case Skills.SkillType.Cooking:
                    return level <= 35f ? 2f : level <= 70f ? 2f + (level - 35f) / 35f : 3f + (level - 70f) / 30f;
                default: return 1f;
            }
        }

        private static float Log(Skills.SkillType skill, float baseXp, float boss, float tier, float skillMultiplier, float first, float resource, float finalXp)
        {
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo(skill + " XP: Base=" + F(baseXp) + ", BossMod=" + F(boss) + ", TierMod=" + F(tier) + ", SkillCoef=" + F(skillMultiplier) + ", FirstTime=" + F(first) + ", ResourceMod=" + F(resource) + ", Final=" + F(finalXp));
            return finalXp;
        }

        private static bool TryConsumeFirstUnique(Player player, Skills.SkillType skill, string itemKey, string targetKey)
        {
            if (MasteryRuntime.FirstUniqueMultiplier <= 1f) return false;
            string key = "valheim_mastery.first." + skill + "." + itemKey + "." + targetKey;
            if (player.m_customData.ContainsKey(key)) return false;
            player.m_customData[key] = "1";
            return true;
        }
        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
