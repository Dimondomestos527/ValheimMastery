using UnityEngine;
namespace ValheimMastery{internal static class MasterIdolResidentBudget{
 private static int Frame=-1,Paths,Scans;
 private static void Reset(){if(Frame==Time.frameCount)return;Frame=Time.frameCount;Paths=Scans=0;}
 internal static bool Path(){Reset();if(Paths>=4){MasterIdolResidentMetrics.Count("path.deferred");return false;}Paths++;MasterIdolResidentMetrics.Count("path.accepted");return true;}
 internal static bool Scan(){Reset();if(Scans>=8){MasterIdolResidentMetrics.Count("scan.deferred");return false;}Scans++;return true;}
}}
