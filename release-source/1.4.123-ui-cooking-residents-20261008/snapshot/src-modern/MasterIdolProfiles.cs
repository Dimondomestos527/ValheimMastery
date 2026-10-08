using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    // Phase B: explicit basic-resource allowlists. No biome powers or Favor cost.
    internal sealed class MasterIdolProfile
    {
        internal readonly string Id, Prefab, Ukrainian, English;
        internal readonly IReadOnlyList<MasterIdolResource> Resources;
        internal MasterIdolProfile(string id, string ukrainian, string english, params MasterIdolResource[] resources)
        {
            Id = id; Prefab = "VM_MasterIdol_" + id; Ukrainian = ukrainian; English = english;
            Resources = Array.AsReadOnly(resources);
        }
    }
    internal readonly struct MasterIdolResource
    {
        internal readonly string Prefab;
        internal readonly int Amount;
        internal MasterIdolResource(string prefab, int amount) { Prefab = prefab; Amount = amount; }
    }
    internal static class MasterIdolProfiles
    {
        // Compact recipes; native-weight budget may lower these ceilings, never raise them.
        internal static readonly IReadOnlyList<MasterIdolProfile> All = Array.AsReadOnly(new[] {
            new MasterIdolProfile("Meadows", "Ідол Лугів", "Meadows Idol",
                R("Wood",8), R("Stone",6), R("FineWood",4), R("DeerHide",2), R("Resin",4), R("TrophyDeer",1), R("Dandelion",3)),
            new MasterIdolProfile("BlackForest", "Ідол Чорного лісу", "Black Forest Idol",
                R("RoundLog",8), R("Bronze",2), R("GreydwarfEye",2), R("SurtlingCore",1), R("MushroomYellow",3)),
            new MasterIdolProfile("Swamp", "Ідол Боліт", "Swamp Idol",
                R("Wood",8), R("Stone",6), R("ElderBark",8), R("Iron",4), R("Guck",3)),
            new MasterIdolProfile("Mountain", "Ідол Гір", "Mountain Idol",
                R("Wood",8), R("Stone",8), R("Obsidian",6), R("Silver",2)),
            new MasterIdolProfile("Plains", "Ідол Рівнин", "Plains Idol",
                R("FineWood",8), R("BlackMetal",4), R("Flax",6), R("Barley",6), R("LoxPelt",2), R("Tar",3)),
            new MasterIdolProfile("Mistlands", "Ідол Імлистих земель", "Mistlands Idol",
                R("BlackMarble",8), R("YggdrasilWood",8), R("Eitr",3), R("Bronze",2), R("MushroomMagecap",3), R("MushroomJotunPuffs",3))
        });
        private static MasterIdolResource R(string prefab, int amount) => new MasterIdolResource(prefab, amount);
        internal static MasterIdolProfile Find(string id)
        {
            foreach (var profile in All) if (string.Equals(profile.Id, id, StringComparison.Ordinal)) return profile;
            return null;
        }
        internal static MasterIdolProfile FromPrefab(string name)
        {
            foreach (var profile in All) if (string.Equals(profile.Prefab, name, StringComparison.Ordinal)) return profile;
            return null;
        }
    }
}
