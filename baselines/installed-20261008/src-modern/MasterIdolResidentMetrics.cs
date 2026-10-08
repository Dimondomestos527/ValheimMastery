using System;using System.Collections.Generic;
namespace ValheimMastery{
internal static class MasterIdolResidentMetrics{
 private static readonly Dictionary<string,long> Counts=new Dictionary<string,long>(StringComparer.Ordinal);
 internal static void Count(string name){if(Counts.TryGetValue(name,out var n))Counts[name]=n+1;else if(Counts.Count<64)Counts[name]=1;}
 internal static string Describe(){var rows=new List<string>{"resident counters: cumulative, no per-frame log"};foreach(var pair in Counts)rows.Add(pair.Key+"="+pair.Value);return string.Join("\n",rows);}
}}
