using HarmonyLib;

namespace ValheimMastery
{
    internal static class RidingPerkAuthority
    {
        internal const string LevelKey = "vm.rider.level";
        internal static bool Has(Player rider, int level)
        {
            if (rider == null || !MasteryPlugin.Settings.Enabled.Value) return false;
            if (rider == Player.m_localPlayer || ZNet.instance?.IsServer() == true)
                return OwnerSkillAuthority.Has(rider, Skills.SkillType.Ride, level);
            // Owner-authored character ZDO for a mount currently controlled by
            // another client. Eligibility follows the current saddle user only.
            float value = rider.m_nview?.GetZDO()?.GetFloat(LevelKey, 0f) ?? 0f;
            return OwnerSkillAuthority.Valid(value) && value >= level;
        }
    }
    [HarmonyPatch(typeof(Character), "ApplyPushback", new[] { typeof(UnityEngine.Vector3), typeof(float) })]
    internal static class Riding70PushbackPatch
    {
        private static void Prefix(Character __instance, ref float pushForce)
        {
            Sadle saddle = __instance?.GetComponent<Sadle>();
            Player rider = saddle != null ? Player.GetPlayer(saddle.GetUser()) : null;
            if (RidingPerkAuthority.Has(rider, 70)) pushForce *= .25f;
        }
    }
}
