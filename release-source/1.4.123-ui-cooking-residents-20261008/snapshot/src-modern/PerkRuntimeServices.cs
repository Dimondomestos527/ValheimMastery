using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class PerkHitContext
    {
        internal string PerkId = "";
        internal Skills.SkillType SourceSkill = Skills.SkillType.None;
        internal long SourcePlayerId = 0L;
        internal int GenerationDepth = 0;
        internal bool IsPerkGenerated = false;
        internal bool AllowSelfProc = false;
        internal bool AllowOtherPerkProc = true;
        internal bool AllowKnife70InitialProc = true;
        internal bool AllowShadowRecursion = false;
        internal bool IgnoreReflect = false;
        internal bool IgnoreExecution = false;
        internal bool IgnoreOverdrawPayload = false;
        internal float XpMultiplier = 1f;
        internal readonly HashSet<string> AppliedPerks = new HashSet<string>(StringComparer.Ordinal);
    }

    internal static class PerkRuntimeService
    {
        private static readonly ConditionalWeakTable<HitData, PerkHitContext> HitContexts = new ConditionalWeakTable<HitData, PerkHitContext>();
        internal static bool ForceProc { get; set; }

        internal static float GetActualSkillLevel(Player player, Skills.SkillType skill)
        {
            if (player == null)
                return 0f;
            // Native GetSkillList allocates a new List and copies every entry.
            // Perk eligibility is queried in several per-frame paths: read the
            // existing raw level without SE bonuses, flooring or creating a skill.
            Skills skills = player.GetSkills();
            if (skills?.m_skillData == null) return 0f;
            if (skills.m_skillData.TryGetValue(skill, out Skills.Skill found) && found?.m_info?.m_skill == skill)
                return found.m_level;
            // Preserve the former info-based lookup for unusual foreign entries
            // without allocating a temporary list or caching stale levels.
            foreach (var pair in skills.m_skillData)
                if (pair.Value?.m_info != null && pair.Value.m_info.m_skill == skill)
                    return pair.Value.m_level;
            return 0f;
        }

        internal static bool HasPerk(Player player, Skills.SkillType skill, int milestone)
        {
            return GetActualSkillLevel(player, skill) >= milestone;
        }

        internal static bool RollChance(float chance)
        {
            return ForceProc || UnityEngine.Random.value < Mathf.Clamp01(chance);
        }

        internal static PerkHitContext GetHitContext(HitData hit)
        {
            return hit == null ? null : HitContexts.GetOrCreateValue(hit);
        }

        internal static bool TryMarkApplied(HitData hit, string perkId)
        {
            PerkHitContext context = GetHitContext(hit);
            return context != null && context.AppliedPerks.Add(perkId);
        }

        internal static bool IsPerkGenerated(HitData hit)
        {
            return hit != null && ((HitContexts.TryGetValue(hit, out PerkHitContext context) && context.IsPerkGenerated) ||
#if MASTERY_CLUBS35_EXPERIMENT
                hit.m_variant == Mace35CorpseProjectile.GeneratedVariant ||
#endif
#if MASTERY_SHIELD35_EXPERIMENT
                hit.m_variant == Blocking35ProjectileService.ReflectedVariant ||
#endif
                hit.m_variant == 1270 ||
                hit.m_variant == CrossbowTurretProjectile.Variant ||
                MasteryAttackTagService.Has(hit, MasteryAttackTag.Knives70Shadow) ||
                MasteryAttackTagService.Has(hit, MasteryAttackTag.Fists70Maul));
        }

        internal static void RestoreStamina(Player player, float amount)
        {
            if (player != null && amount > 0f)
                player.AddStamina(amount);
        }

        internal static string ItemPrefabName(ItemDrop.ItemData item)
        {
            if (item?.m_dropPrefab != null)
                return item.m_dropPrefab.name.Replace("(Clone)", "").Trim();
            return "";
        }

        // PlayerUnarmed has no stable drop prefab on every 1.0 client.  Any named Unarmed item
        // other than the default fist is a fist weapon and must not receive bare-hand passives.
        internal static bool IsBareHands(ItemDrop.ItemData item)
        {
            if (item?.m_shared?.m_skillType != Skills.SkillType.Unarmed)
                return false;
            string prefab = ItemPrefabName(item);
            return string.IsNullOrEmpty(prefab) ||
                prefab.Equals("PlayerUnarmed", StringComparison.OrdinalIgnoreCase) ||
                prefab.Equals("Fist", StringComparison.OrdinalIgnoreCase) ||
                prefab.Equals("Unarmed", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class PerkCooldownStateService
    {
        private const string Prefix = "valheim_mastery.cooldown.";

        internal static bool TryConsume(Player player, string cooldownId, double seconds)
        {
            if (player == null)
                return false;
            string key = Prefix + cooldownId;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            if (player.m_customData.TryGetValue(key, out string raw) &&
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double readyAt) && readyAt > now)
                return false;
            player.m_customData[key] = (now + seconds).ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        internal static double GetRemainingSeconds(Player player, string cooldownId)
        {
            if (player == null || string.IsNullOrWhiteSpace(cooldownId)) return 0d;
            string key = Prefix + cooldownId;
            if (!player.m_customData.TryGetValue(key, out string raw) ||
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double readyAt)) return 0d;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            return Math.Max(0d, readyAt - now);
        }
        internal static void ReduceRemaining(Player player, string cooldownId, float remainingMultiplier)
        {
            if (player == null || string.IsNullOrWhiteSpace(cooldownId)) return;
            string key = Prefix + cooldownId;
            if (!player.m_customData.TryGetValue(key, out string raw) ||
                !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double readyAt)) return;
            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            if (readyAt <= now) return;
            player.m_customData[key] = (now + (readyAt - now) * Math.Max(0f, remainingMultiplier)).ToString("R", CultureInfo.InvariantCulture);
        }
        internal static void Clear(Player player)
        {
            if (player == null)
                return;
            List<string> keys = new List<string>();
            foreach (string key in player.m_customData.Keys)
                if (key.StartsWith(Prefix, StringComparison.Ordinal))
                    keys.Add(key);
            foreach (string key in keys)
                player.m_customData.Remove(key);
        }
    }

    internal static class PerkProfessionService
    {
        private static readonly HashSet<string> MetalMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CopperOre", "TinOre", "IronScrap", "IronOre", "SilverOre", "BlackMetalScrap", "FlametalOre", "FlametalOreNew",
            "Copper", "Tin", "Bronze", "Iron", "Silver", "BlackMetal", "Flametal", "FlametalNew", "CopperScrap", "BronzeScrap"
        };

        private static readonly HashSet<string> WoodMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wood", "FineWood", "RoundLog", "CoreWood", "ElderBark", "YggdrasilWood", "Blackwood"
        };

        internal static bool IsWoodMaterial(ItemDrop.ItemData item) => WoodMaterials.Contains(PerkRuntimeService.ItemPrefabName(item));
        internal static bool IsWoodMaterial(string prefab) => WoodMaterials.Contains((prefab ?? string.Empty).Replace("(Clone)", string.Empty).Trim());

        internal static float GetMasterFeastWeightDiscount(Player player, Inventory inventory)
        {
            if (player == null || inventory == null || player.GetInventory() != inventory ||
                MasterFeastThemeService.GetTheme(player) != FeastTheme.BlackForest) return 0f;
            bool nonPortable = PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 35);
            bool metal = PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 100);
            bool wood = PerkRuntimeService.HasPerk(player, Skills.SkillType.WoodCutting, 70);
            float discount = 0f;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string prefab = PerkRuntimeService.ItemPrefabName(item);
                if (MetalMaterials.Contains(prefab) || WoodMaterials.Contains(prefab))
                    discount += item.GetWeight(item.m_stack) * (1f - ItemWeightDiscountFraction(item, nonPortable, metal, wood)) * 0.25f;
            }
            return discount;
        }
        internal static float GetInventoryWeightDiscount(Player player, Inventory inventory)
        {
            if (player == null || inventory == null || player.GetInventory() != inventory)
                return 0f;
            bool nonPortablePerk = PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 35);
            bool metalPerk = PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 100);
            bool woodPerk = PerkRuntimeService.HasPerk(player, Skills.SkillType.WoodCutting, 70);
            if (!nonPortablePerk && !metalPerk && !woodPerk)
                return 0f;

            float discount = 0f;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                string prefab = PerkRuntimeService.ItemPrefabName(item);
                float weight = item.GetWeight(item.m_stack);
                discount += weight * ItemWeightDiscountFraction(item, nonPortablePerk, metalPerk, woodPerk);
            }
            return discount;
        }
        private static float ItemWeightDiscountFraction(ItemDrop.ItemData item, bool nonPortable, bool metal, bool wood)
        {
            string prefab = PerkRuntimeService.ItemPrefabName(item);
            float fraction = 0f;
            if (nonPortable && !item.m_shared.m_teleportable && !item.m_shared.m_questItem) fraction = .75f;
            if (metal && MetalMaterials.Contains(prefab)) fraction = Mathf.Max(fraction, .75f);
            if (wood && WoodMaterials.Contains(prefab)) fraction = Mathf.Max(fraction, string.Equals(prefab, "Wood", StringComparison.OrdinalIgnoreCase) ? .90f : .50f);
            return fraction;
        }
    }
}
