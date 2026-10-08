using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
namespace ValheimMastery
{
    // Fixed stage names, no per-frame output. Read-only aggregates are diagnostic evidence, not FPS attribution.
    internal static class MasterIdolPerf
    {
        private sealed class Sample{internal long Calls,Ticks,Bytes,Count,Maximum;}
        private static readonly Dictionary<string,Sample> Samples=new Dictionary<string,Sample>(StringComparer.Ordinal);
        private static bool Enabled=>MasteryPlugin.Settings?.UIDebugLogging?.Value==true;
        private static Sample Get(string stage){if(!Samples.TryGetValue(stage,out var value)&&Samples.Count<64){value=new Sample();Samples.Add(stage,value);}return value;}
        internal static void Count(string stage,int amount=1){if(!Enabled)return;var value=Get(stage);if(value!=null)value.Count+=amount;}
        internal readonly struct Scope:IDisposable
        {
            private readonly Sample Value;private readonly long Start,Bytes;
            internal Scope(string stage){Value=Enabled?Get(stage):null;Start=Value==null?0:Stopwatch.GetTimestamp();Bytes=Value==null?0:GC.GetAllocatedBytesForCurrentThread();}
            public void Dispose(){if(Value==null)return;long ticks=Stopwatch.GetTimestamp()-Start;Value.Calls++;Value.Ticks+=ticks;Value.Maximum=Math.Max(Value.Maximum,ticks);Value.Bytes+=Math.Max(0,GC.GetAllocatedBytesForCurrentThread()-Bytes);}
        }
        internal static string Describe(){var lines=new List<string>();lines.Add("Idol aggregates debug="+Enabled+" stages="+Samples.Count+"; cumulative CPU/managed allocations, not FPS/GPU");foreach(var pair in Samples){var s=pair.Value;lines.Add(pair.Key+" calls="+s.Calls+" count="+s.Count+" totalMs="+(s.Ticks*1000d/Stopwatch.Frequency).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" maxMs="+(s.Maximum*1000d/Stopwatch.Frequency).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" bytes="+s.Bytes);}return string.Join("\n",lines);}
    }
    [HarmonyPatch(typeof(PerkDebugService),nameof(PerkDebugService.RegisterCommands))]
    internal static class MasterIdolPerfCommandPatch
    {private static void Postfix()=>new Terminal.ConsoleCommand("vm_idol_perf","Показати накопичені витрати ідолів; лише читання, збір за UIDebugLogging.",args=>args.Context.AddString(MasterIdolPerf.Describe()),false);}
}
