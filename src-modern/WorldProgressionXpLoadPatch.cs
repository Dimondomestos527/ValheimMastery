using System.IO;
using HarmonyLib;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Load), new[] { typeof(BinaryReader), typeof(Version.World) })]
    internal static class WorldXpWorldLoadPatch
    {
        private static void Postfix() => WorldProgressionXpService.Refresh(true);
    }
}
