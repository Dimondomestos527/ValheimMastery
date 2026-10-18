using HarmonyLib;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Ashlands70InputPatch
    { [HarmonyPriority(Priority.First)] private static bool Prefix(Player __instance) => Ashlands70Cast.Input(__instance); }
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    internal static class Ashlands70RegisterPatch
    { private static void Postfix(ZNetPeer peer) => Ashlands70Cast.Register(peer); }
    [HarmonyPatch(typeof(MasteryPlugin), "Update")]
    internal static class Ashlands70TickPatch
    { private static void Postfix() => Ashlands70Cast.Tick(); }
    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    internal static class Ashlands70ShutdownPatch
    { private static void Prefix() => Ashlands70Cast.Reset(); }
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class Ashlands70CharacterPatch
    { private static void Postfix(Character __instance) => Ashlands70Madness.Track(__instance); }
}
