using System;
using System.Diagnostics;
namespace ValheimMastery
{
    internal static class MasterIdolTiming
    {
        internal static long Start() => Stopwatch.GetTimestamp();
        internal static void Finish(string stage,long start)
        {
            if(start==0 || !MasteryPlugin.Settings.UIDebugLogging.Value)return;
            double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;
            if(ms>=8)MasteryPlugin.Log.LogWarning("[MasterIdolsTiming] "+stage+" "+ms.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+"ms");
        }
        internal static bool Measure(string stage, Func<bool> action)
        {
            using var aggregate=new MasterIdolPerf.Scope(stage);long start = Stopwatch.GetTimestamp();
            try { return action(); }
            finally
            {
                Finish(stage,start);
            }
        }
    }
}
