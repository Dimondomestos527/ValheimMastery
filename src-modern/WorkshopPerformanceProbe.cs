using System;
using System.Diagnostics;
using System.Globalization;
using HarmonyLib;

namespace ValheimMastery
{
    // Observer only. Default-off, fixed storage, no RPC/config/save/world writes.
    internal static class WorkshopPerformanceProbe
    {
        internal struct Span
        {
            internal bool Active;
            internal int Kind;
            internal long Start, Allocated;
        }
        private sealed class Bucket
        {
            internal long Queries, Builds, Aborted, Ticks, MaxTicks, Bytes, AllocationSamples;
            internal long Records, Entries, Stations, ExtensionLoops, GraphLoops, EntryCreates;
            internal void Clear() { Queries = Builds = Aborted = Ticks = MaxTicks = Bytes = AllocationSamples = 0;
                Records = Entries = Stations = ExtensionLoops = GraphLoops = EntryCreates = 0; }
        }
        private static readonly Bucket[] Buckets = { new Bucket(), new Bucket(), new Bucket() };
        private static bool Enabled, Registered, AllocationRequested, AllocationUnavailable;
        private static long Started, Until, Finished;
        private static ZNet Session;
        private static string Role = "not-started", Reason = "not-started", Fault;
        private static Func<long> ReadAllocation;

        internal static void Query(int kind)
        {
            if (!Enabled) return;
            try
            {
                if (Session != ZNet.instance) { Finish("session-changed"); return; }
                if (Stopwatch.GetTimestamp() >= Until) { Finish("expired"); return; }
                Buckets[kind].Queries++;
            }
            catch (Exception error) { Fail(error); }
        }
        internal static Span Begin(int kind)
        {
            if (!Enabled) return default;
            try { return new Span { Active = true, Kind = kind, Allocated = Allocation(), Start = Stopwatch.GetTimestamp() }; }
            catch (Exception error) { Fail(error); return default; }
        }
        internal static void End(Span span, bool completed, int records, int entries, int stations,
            long extensionLoops, long graphLoops, int entryCreates)
        {
            if (!span.Active) return;
            try
            {
                long elapsed = Stopwatch.GetTimestamp() - span.Start;
                long allocation = span.Allocated < 0 ? -1 : Allocation();
                Bucket bucket = Buckets[span.Kind];
                bucket.Builds++; if (!completed) bucket.Aborted++;
                bucket.Ticks += elapsed; bucket.MaxTicks = Math.Max(bucket.MaxTicks, elapsed);
                if (allocation >= span.Allocated && span.Allocated >= 0)
                { bucket.Bytes += allocation - span.Allocated; bucket.AllocationSamples++; }
                bucket.Records = Math.Max(bucket.Records, records); bucket.Entries = Math.Max(bucket.Entries, entries);
                bucket.Stations = Math.Max(bucket.Stations, stations);
                bucket.ExtensionLoops += extensionLoops; bucket.GraphLoops += graphLoops; bucket.EntryCreates += entryCreates;
            }
            catch (Exception error) { Fail(error); }
        }
        private static long Allocation()
        {
            if (!AllocationRequested || ReadAllocation == null) return -1;
            try { return ReadAllocation(); }
            catch { ReadAllocation = null; AllocationUnavailable = true; return -1; }
        }
        private static void Fail(Exception error) { Enabled = false; Finished = Stopwatch.GetTimestamp(); Fault = error.GetType().Name; Reason = "probe-fault"; }
        private static void Start(int seconds, bool allocation)
        {
            foreach (Bucket bucket in Buckets) bucket.Clear();
            Session = ZNet.instance;
            Role = Session?.IsServer() == true ? (Player.m_localPlayer == null ? "server-headless" : "host-local") : "client-non-authoritative";
            Started = Stopwatch.GetTimestamp(); Until = Started + Math.Max(15, Math.Min(120, seconds)) * Stopwatch.Frequency;
            Finished = 0; Fault = null; Reason = "sampling"; AllocationRequested = allocation; AllocationUnavailable = false;
            if (allocation && ReadAllocation == null)
            {
                try
                {
                    var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
                    if (method != null) ReadAllocation = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
                }
                catch { ReadAllocation = null; }
                AllocationUnavailable = ReadAllocation == null;
            }
            Enabled = true;
        }
        private static string Header()
        {
            long now = Finished != 0 ? Finished : Stopwatch.GetTimestamp();
            double seconds = Started == 0 ? 0 : (Math.Min(now, Until) - Started) / (double)Stopwatch.Frequency;
            return "[WorkshopPerf] role=" + Role + " active=" + Enabled + " reason=" + Reason + " seconds=" + seconds.ToString("F2", CultureInfo.InvariantCulture) +
                " alloc=" + (!AllocationRequested ? "off" : AllocationUnavailable ? "unavailable" : "thread") + " fault=" + (Fault ?? "none");
        }
        private static string Summary(int kind)
        {
            Bucket b = Buckets[kind]; double factor = 1000d / Stopwatch.Frequency;
            return "[WorkshopPerf] " + (kind == 0 ? "world" : "local") + " queries=" + b.Queries + " rebuilds=" + b.Builds + " incomplete=" + b.Aborted +
                " totalMs=" + (b.Ticks * factor).ToString("F3", CultureInfo.InvariantCulture) + " maxMs=" + (b.MaxTicks * factor).ToString("F3", CultureInfo.InvariantCulture) +
                " allocBytes=" + (b.AllocationSamples == 0 ? "not-sampled" : b.Bytes.ToString(CultureInfo.InvariantCulture)) + " allocSamples=" + b.AllocationSamples +
                " maxRecords=" + b.Records + " maxEntries=" + b.Entries + " maxStations=" + b.Stations +
                " extensionLoopOpportunities=" + b.ExtensionLoops + " graphLoopOpportunities=" + b.GraphLoops + " entryCreates=" + b.EntryCreates;
        }
        private static void Finish(string reason)
        {
            Enabled = false; Finished = Stopwatch.GetTimestamp(); Reason = reason;
            try { MasteryPlugin.Log?.LogInfo(Header()); MasteryPlugin.Log?.LogInfo(Summary(0)); MasteryPlugin.Log?.LogInfo(Summary(1));
                MasteryPlugin.Log?.LogInfo("[WorkshopPerf] localDirtyCalls=" + Buckets[2].Queries); }
            catch { /* Diagnostics never replace a gameplay exception. */ }
        }
        internal static void Register()
        {
            if (Registered) return;
            try
            {
                new Terminal.ConsoleCommand("vm_workshopperf", "vm_workshopperf start [15-120 seconds] [alloc] | status | stop (local process only)", Command, false);
                Registered = true;
                // Explicit operator opt-in permits a headless sample without remote control/config edits.
                if (int.TryParse(Environment.GetEnvironmentVariable("VM_WORKSHOP_PERF_SECONDS"), out int seconds))
                    Start(seconds, Environment.GetEnvironmentVariable("VM_WORKSHOP_PERF_ALLOC") == "1");
            }
            catch (Exception error) { Fail(error); }
        }
        private static void Command(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string action = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "status";
                if (action == "start")
                {
                    int seconds = 60;
                    if (args.Args.Length > 2 && !int.TryParse(args.Args[2], out seconds)) { args.Context.AddString("Seconds must be15-120."); return; }
                    if (seconds < 15 || seconds > 120) { args.Context.AddString("Seconds must be15-120."); return; }
                    Start(seconds, args.Args.Length > 3 && args.Args[3] == "alloc");
                }
                else if (action == "stop") { if (Enabled) Finish("manual"); }
                else if (action != "status") { args.Context.AddString("vm_workshopperf start [15-120] [alloc] | status | stop"); return; }
                if (Enabled && Stopwatch.GetTimestamp() >= Until) Finish("expired");
                args.Context.AddString(Header()); args.Context.AddString(Summary(0)); args.Context.AddString(Summary(1));
                args.Context.AddString("[WorkshopPerf] localDirtyCalls=" + Buckets[2].Queries);
            }
            catch (Exception error) { Fail(error); }
        }
    }

    // PatchAll happens before this existing startup method, including its debug-disabled return.
    [HarmonyPatch(typeof(PerkDebugService), nameof(PerkDebugService.RegisterCommands))]
    internal static class WorkshopPerformanceCommandPatch
    {
        private static void Postfix() => WorkshopPerformanceProbe.Register();
    }
}
