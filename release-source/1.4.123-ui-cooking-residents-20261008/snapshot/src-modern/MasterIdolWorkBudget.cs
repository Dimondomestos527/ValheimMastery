using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolWorkBudget
    {
        private static int Frame=-1,Paths;internal const int PathsPerFrame=4;
        internal static bool Path(){if(Frame!=Time.frameCount){Frame=Time.frameCount;Paths=0;}if(Paths>=PathsPerFrame){MasterIdolPerf.Count("path.deferred");return false;}Paths++;MasterIdolPerf.Count("path.accepted");return true;}
    }
}
