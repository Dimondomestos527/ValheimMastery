#if !MASTERY_RELEASE_SAFE || MASTERY_CLUB_PASSIVES
using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal enum ClubWeaponClass { UnknownClub, Mace, SledgeHammer }

    internal static class ClubWeaponClassService
    {
        internal static ClubWeaponClass Classify(ItemDrop.ItemData item)
        {
            if (item?.m_shared?.m_skillType != Skills.SkillType.Clubs) return ClubWeaponClass.UnknownClub;
            return ClassifyPrefab(PerkRuntimeService.ItemPrefabName(item));
        }

        internal static ClubWeaponClass ClassifyPrefab(string raw)
        {
            string name = (raw ?? string.Empty).Replace("(Clone)", string.Empty).Trim();
            if (name.StartsWith("Sledge", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Stagbreaker", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Demolisher", StringComparison.OrdinalIgnoreCase))
                return ClubWeaponClass.SledgeHammer;
            if (name.StartsWith("Mace", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Club", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Frostner", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Porcupine", StringComparison.OrdinalIgnoreCase))
                return ClubWeaponClass.Mace;
            return ClubWeaponClass.UnknownClub;
        }
    }

}
#endif
