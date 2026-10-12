using System;

namespace ValheimMastery
{
    // Exact native identities; no substring match or current-held-weapon inference.
    internal static class StaffMagicXpRules
    {
        internal const int Spirit = 3, Roots = 4, Troll = 5, Frost = 6, AhriReturn = 7;
        internal static readonly string[] Staves = { "StaffFireball", "StaffIceShards", "StaffShield", "StaffSkeleton",
            "StaffLightning", "StaffClusterbomb", "StaffGreenRoots", "StaffRedTroll",
            "StaffFrostOrbs", "StaffOrbofAhri", "StaffSpiritCaller", "StaffThunderBlood" };
        internal static string Name(string name) => (name ?? "").Replace("(Clone)", "").Trim();
        internal static int Index(string staff) => Array.IndexOf(Staves, Name(staff));
        internal static int Tier(string staff) { int n = Index(staff); return n < 0 ? 1 : n < 4 ? 6 : n < 8 ? 7 : 8; }
        internal static bool Magic(Skills.SkillType skill) => skill == Skills.SkillType.BloodMagic || skill == Skills.SkillType.ElementalMagic;
        internal static string SummonStaff(string prefab)
        {
            switch (Name(prefab))
            {
                case "Skeleton_Friendly": case "Skeleton_Friendly_Archer": return "StaffSkeleton";
                case "Boar_spiritcaller": case "Wolf_spiritcaller": case "Bjorn_spiritcaller": return "StaffSpiritCaller";
                case "staff_greenroots_tentaroot": return "StaffGreenRoots";
                case "Troll_Summoned": return "StaffRedTroll";
                default: return "";
            }
        }
        internal static string Staff(int kind) => kind == 1 ? "StaffSkeleton" : kind == 2 ? "StaffShield" :
            kind == Spirit ? "StaffSpiritCaller" : kind == Roots ? "StaffGreenRoots" : kind == Troll ? "StaffRedTroll" :
            kind == Frost ? "StaffFrostOrbs" : kind == AhriReturn ? "StaffOrbofAhri" :
            kind >= 100 && kind < 112 ? Staves[kind - 100] : kind >= 200 && kind < 212 ? Staves[kind - 200] : "";
        internal static Skills.SkillType Skill(int kind) => kind == Roots || (kind >= 100 && kind < 112)
            ? Skills.SkillType.ElementalMagic : Skills.SkillType.BloodMagic;
        internal static int DirectKind(string staff, Skills.SkillType skill)
        {
            int n = Index(staff);
            if (skill == Skills.SkillType.BloodMagic) return n == 9 ? 209 : 0;
            return skill == Skills.SkillType.ElementalMagic && (n == 0 || n == 1 || n == 4 || n == 5 || n == 6 || n == 9 || n == 11) ? 100 + n : 0;
        }
        internal static float RootBase(int nativeCap) => nativeCap >= 2 && nativeCap <= 6 ? 1f / nativeCap : 0f;
        internal static float FrostBase(float damage, float capacity) => BloodMagicXpRules.Finite(damage) && damage > 0f &&
            BloodMagicXpRules.Finite(capacity) && capacity > 0f ? Math.Min(1f, damage / capacity) : 0f;
        internal static bool Valid(int kind, float raw, float tier) => Staff(kind).Length != 0 &&
            (kind < 100 || DirectKind(Staff(kind), Skill(kind)) == kind) &&
            BloodMagicXpRules.Finite(raw) && raw > 0f && raw <= (kind == 1 || kind == Spirit || kind >= 100 ? 1.5f : 1.000001f) &&
            BloodMagicXpRules.Finite(tier) && tier >= 1f && tier <= 8f &&
            (kind != Troll || Math.Abs(raw - .5f) < .000001f) && (kind != Roots || raw <= .500001f);
        internal static bool Target(string key) => key != null && key.Length > 0 && key.Length <= 96 &&
            key.IndexOf('\n') < 0 && key.IndexOf('\r') < 0 && key.IndexOf('\0') < 0;
    }
}
