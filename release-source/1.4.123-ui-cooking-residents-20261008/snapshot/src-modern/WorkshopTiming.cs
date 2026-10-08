using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ValheimMastery
{
    // Observation only. No gameplay waits, ownership changes or packet-format changes.
    // Each side uses its own monotonic clock; never subtract timestamps across hosts.
    internal static class WorkshopTiming
    {
        private sealed class Trace
        {
            internal long Started;
            internal string Kind, Mode;
            internal readonly Dictionary<string, double> Marks = new Dictionary<string, double>();
            internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        }
        private static readonly Dictionary<string, Trace> Traces = new Dictionary<string, Trace>();
        private static bool Enabled => MasteryPlugin.Settings?.WorkshopTiming?.Value == true;
        private static double Age(long started) => (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        private static string Key(string id, bool server) => (server ? "S:" : "C:") + id;
        internal static void Start(string id, bool server, string kind, string mode)
        {
            if (!Enabled || !Guid.TryParseExact(id, "N", out _)) return;
            var stale = new List<string>();
            foreach (var pair in Traces) if (Age(pair.Value.Started) > 45000d) stale.Add(pair.Key);
            foreach (string key in stale) Traces.Remove(key);
            string own = Key(id, server);
            if (Traces.ContainsKey(own) || Traces.Count >= 128) return;
            var trace = new Trace { Started = Stopwatch.GetTimestamp(), Kind = kind, Mode = mode };
            trace.Marks[server ? "S0.received" : "T0.request"] = 0d;
            Traces.Add(own, trace);
        }
        internal static void Mark(string id, bool server, string stage)
        {
            if (Enabled && Traces.TryGetValue(Key(id, server), out var trace) && !trace.Marks.ContainsKey(stage))
                trace.Marks[stage] = Age(trace.Started);
        }
        internal static void Count(string id, bool server, string name, int value)
        { if (Enabled && Traces.TryGetValue(Key(id, server), out var trace)) trace.Counts[name] = value; }
        internal static void Probe(string id)
        {
            var net = ZNet.instance;
            if (!Enabled || net == null || net.IsServer()) return;
            Mark(id, false, "Cprobe.sent");
            net.GetServerRPC()?.Invoke("VM_WS_TimingEcho", id);
        }
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<string>("VM_WS_TimingEcho", (source, id) => {
                if (!Guid.TryParseExact(id, "N", out _) || ZNet.instance == null) return;
                if (ZNet.instance.IsServer())
                { if (ZNet.instance.GetPeer(source)?.IsReady() == true) source.Invoke("VM_WS_TimingEcho", id); }
                else if (source == ZNet.instance.GetServerRPC()) Mark(id, false, "Cprobe.returned");
            });
        }
        internal static void Report(string id, bool server, string result, bool finish = false)
        {
            string key = Key(id, server);
            if (!Enabled || !Traces.TryGetValue(key, out var trace)) return;
            var output = new StringBuilder("[WorkshopTiming] id=").Append(id)
                .Append(" side=").Append(server ? "server" : "client").Append(" kind=").Append(trace.Kind)
                .Append(" mode=").Append(trace.Mode).Append(" result=").Append(result);
            foreach (var mark in trace.Marks)
                output.Append(' ').Append(mark.Key).Append('=').Append(mark.Value.ToString("F3", CultureInfo.InvariantCulture));
            foreach (var count in trace.Counts) output.Append(' ').Append(count.Key).Append('=').Append(count.Value);
            if (trace.Marks.TryGetValue("Cprobe.sent", out double sent) && trace.Marks.TryGetValue("Cprobe.returned", out double returned))
                output.Append(" echoRttMs=").Append((returned - sent).ToString("F3", CultureInfo.InvariantCulture));
            // Buffer marks and log only at grant/outcome/settlement: per-stage disk logging would bias measurements.
            MasteryPlugin.Log.LogInfo(output.ToString());
            if (finish) Traces.Remove(key);
        }
    }
}
