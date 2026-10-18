using HarmonyLib;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(NetworkSync), nameof(NetworkSync.Register))]
    internal static class ResidentDanceRegisterPatch
    {
        private static void Postfix(ZNetPeer peer) => MasterIdolResidentDance.Register(peer);
    }
}
