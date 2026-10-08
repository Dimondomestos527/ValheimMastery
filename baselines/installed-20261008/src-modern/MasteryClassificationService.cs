using System;
using UnityEngine;

namespace ValheimMastery
{
    internal enum CreatureClass { SmallNormal, Heavy, Boss }
    internal enum SurfaceType { Terrain, Stone, Wood, Metal, GenericStatic }
    internal enum WeaponClass { None, Axe, Spear, Polearm, Bow, Crossbow, Club, Knife, Sword, Fist, Tool, Magic }
    internal enum ArrowClass { Wood, Flint, Bronze, Fire, Iron, Poison, Obsidian, Frost, Silver, Needle, Carapace, Charred, Other }

    internal static class MasteryClassificationService
    {
        internal static CreatureClass GetCreatureClass(Character character)
        {
            if (character == null) return CreatureClass.SmallNormal;
            if (character.IsBoss()) return CreatureClass.Boss;
            return character.GetMaxHealth() >= 1000f ? CreatureClass.Heavy : CreatureClass.SmallNormal;
        }

        internal static SurfaceType GetSurfaceType(Collider collider)
        {
            string text = collider == null ? "" : (collider.gameObject.name + " " + collider.tag);
            if (text.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0 || collider?.GetComponentInParent<Heightmap>() != null) return SurfaceType.Terrain;
            if (text.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0) return SurfaceType.Wood;
            if (text.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("iron", StringComparison.OrdinalIgnoreCase) >= 0) return SurfaceType.Metal;
            if (text.IndexOf("stone", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0) return SurfaceType.Stone;
            return SurfaceType.GenericStatic;
        }

        internal static WeaponClass GetWeaponClass(ItemDrop.ItemData item)
        {
            switch (item?.m_shared?.m_skillType ?? Skills.SkillType.None)
            {
                case Skills.SkillType.Axes: return WeaponClass.Axe;
                case Skills.SkillType.Spears: return WeaponClass.Spear;
                case Skills.SkillType.Polearms: return WeaponClass.Polearm;
                case Skills.SkillType.Bows: return WeaponClass.Bow;
                case Skills.SkillType.Crossbows: return WeaponClass.Crossbow;
                case Skills.SkillType.Clubs: return WeaponClass.Club;
                case Skills.SkillType.Knives: return WeaponClass.Knife;
                case Skills.SkillType.Swords: return WeaponClass.Sword;
                case Skills.SkillType.Unarmed: return WeaponClass.Fist;
                case Skills.SkillType.Pickaxes:
                case Skills.SkillType.WoodCutting: return WeaponClass.Tool;
                case Skills.SkillType.ElementalMagic:
                case Skills.SkillType.BloodMagic: return WeaponClass.Magic;
                default: return WeaponClass.None;
            }
        }

        internal static ArrowClass GetArrowClass(ItemDrop.ItemData ammo)
        {
            return GetArrowClass(PerkRuntimeService.ItemPrefabName(ammo));
        }

        internal static ArrowClass GetArrowClass(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return ArrowClass.Other;
            if (prefab.IndexOf("Carapace", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Carapace;
            if (prefab.IndexOf("Charred", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Charred;
            if (prefab.IndexOf("Needle", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Needle;
            if (prefab.IndexOf("Obsidian", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Obsidian;
            if (prefab.IndexOf("Silver", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Silver;
            if (prefab.IndexOf("Frost", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Frost;
            if (prefab.IndexOf("Poison", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Poison;
            if (prefab.IndexOf("Iron", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Iron;
            if (prefab.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Fire;
            if (prefab.IndexOf("Bronze", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Bronze;
            if (prefab.IndexOf("Flint", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Flint;
            if (prefab.IndexOf("Wood", StringComparison.OrdinalIgnoreCase) >= 0) return ArrowClass.Wood;
            return ArrowClass.Other;
        }
    }
}
