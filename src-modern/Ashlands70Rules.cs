using System;
namespace ValheimMastery
{
    internal enum Ashlands70Kind { None, Madness, Chain, Grove, Orbit }
    internal static class Ashlands70Rules
    {
        internal const int GeneratedVariant = 1270, Shards = 10;
        internal const float MadnessRadius = 15f, ChainLength = 12f, ChainInterval = .75f, GroveRadius = 7f;
        internal static Ashlands70Kind Kind(string prefab) => prefab == "StaffRedTroll" ? Ashlands70Kind.Madness :
            prefab == "StaffLightning" ? Ashlands70Kind.Chain : prefab == "StaffGreenRoots" ? Ashlands70Kind.Grove :
            prefab == "StaffClusterbomb" ? Ashlands70Kind.Orbit : Ashlands70Kind.None;
        internal static string Staff(Ashlands70Kind k) => k == Ashlands70Kind.Madness ? "StaffRedTroll" :
            k == Ashlands70Kind.Chain ? "StaffLightning" : k == Ashlands70Kind.Grove ? "StaffGreenRoots" : "StaffClusterbomb";
        internal static Skills.SkillType Skill(Ashlands70Kind k) => k == Ashlands70Kind.Madness ? Skills.SkillType.BloodMagic : Skills.SkillType.ElementalMagic;
        internal static float Eitr(Ashlands70Kind k) => k == Ashlands70Kind.Chain ? 35f : k == Ashlands70Kind.Grove ? 50f : 40f;
        internal static float Cooldown(Ashlands70Kind k) => k == Ashlands70Kind.Madness ? 60f : k == Ashlands70Kind.Chain ? 20f : 45f;
        internal static double Duration(Ashlands70Kind k, int quality) => k == Ashlands70Kind.Madness ? 30d + 10d * quality :
            k == Ashlands70Kind.Grove ? 20d + 5d * quality : k == Ashlands70Kind.Orbit ? 45d + 30d * quality : 20d;
        internal static bool Quality(int q) => q >= 1 && q <= 100;
        internal static float EitrRegen(float status, float equipment) => status * 1.25f + equipment * .25f;
        internal static bool Fresh(long now, long issued) => now > 0 && issued > 0 && issued <= now + TimeSpan.TicksPerSecond * 3L && now - issued <= TimeSpan.TicksPerSecond * 10L;
        internal static float DamageRoll(float level, float roll, float coefficient)
        {
            float center = .4f + .6f * UnityEngine.Mathf.Clamp01(level / 100f);
            float vanilla = UnityEngine.Mathf.Lerp(UnityEngine.Mathf.Clamp01(center - .15f), UnityEngine.Mathf.Clamp01(center + .15f), roll);
            float zero = UnityEngine.Mathf.Lerp(.25f, .55f, roll);
            return UnityEngine.Mathf.Clamp01(zero + coefficient * (vanilla - zero));
        }
        internal static bool Finite(UnityEngine.Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        internal static long Deadline(long now, double seconds) => checked(now + (long)(seconds * TimeSpan.TicksPerSecond));
        internal static bool Active(long now, long until) => now > 0 && now < until;
        internal static string CooldownId(Ashlands70Kind k) => "ashlands70_" + k;
    }
}
