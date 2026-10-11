using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class WorkbenchEnchantmentNativeIdentity
    {
        internal sealed class Batch
        {
            internal ZNet Session; internal ZDOMan Manager; internal long World;
            internal ulong FinalGeneration;
            internal int InnerDepth; internal bool SawInner, Failed, Completed, Original, Result;
            internal bool Valid => Completed && Original && Result && SawInner && !Failed;
        }
        internal sealed class Inner
        { internal Batch Batch; internal bool Original; }
        sealed class Proof
        {
            internal Batch Batch;
            internal ZNet Session; internal ZDOMan Manager; internal long World, Creator;
            internal ZDOID Native; internal string Station; internal Vector3 Position;
        }
        static ConditionalWeakTable<ZDO, Proof> Loaded = new ConditionalWeakTable<ZDO, Proof>();
        static Batch Active;
        internal static void Reset() => Loaded = new ConditionalWeakTable<ZDO, Proof>();
        internal static void Abort() { if (Active != null) Active.Failed = true; Active = null; Reset(); }
        internal static Batch Begin(ZNet net)
        {
            if (Active != null) Active.Failed = true;
            // A skipped or failed reload may never reach ResetBeforeLoad.
            // Revoke older completed proofs before the new outer call starts.
            Reset();
            var batch = new Batch { Session = net, Manager = ZDOMan.instance,
                World = net?.GetWorld() == null ? 0 : net.GetWorldUID() };
            batch.Failed = net?.IsServer() != true || batch.Manager == null || batch.World == 0;
            Active = batch; return batch;
        }
        static bool Context(Batch batch) => batch != null && !batch.Failed &&
            ReferenceEquals(batch.Session, ZNet.instance) && ReferenceEquals(batch.Manager, ZDOMan.instance) &&
            batch.Session?.IsServer() == true && batch.Session.GetWorld() != null && batch.World == batch.Session.GetWorldUID();
        internal static Inner BeginInner(ZDOMan manager)
        {
            var batch = Active;
            if (!Context(batch) || !ReferenceEquals(manager, batch.Manager)) return null;
            batch.InnerDepth++; batch.SawInner = true;
            return new Inner { Batch = batch };
        }
        internal static void EndInner(Inner state, Exception error)
        {
            if (state == null) return;
            var batch = state.Batch;
            if (error != null || !state.Original || !Context(batch) || !ReferenceEquals(Active, batch)) batch.Failed = true;
            batch.InnerDepth = Math.Max(0, batch.InnerDepth - 1);
        }
        internal static void End(Batch batch, Exception error)
        {
            if (batch == null) return;
            if (error != null || !Context(batch) || !ReferenceEquals(Active, batch) || batch.InnerDepth != 0 || ZNet.m_loadError) batch.Failed = true;
            // Shared legacy-profile Context can initialize during this batch and
            // advance its generation after an earlier workbench was observed.
            // Seal the batch after all native loads; ResetBeforeLoad separately
            // clears the proof table, so reset survivors are never admitted.
            batch.FinalGeneration = MasterIdolLoadIdentity.Generation;
            batch.Completed = true;
            if (ReferenceEquals(Active, batch)) Active = null;
        }
        internal static void Filename(ChunkSaveMapping mapping, string filename)
        {
            var batch = Active;
            if (batch != null && batch.InnerDepth > 0 && ReferenceEquals(mapping, batch.Manager?.m_chunkSaveMapping) && string.IsNullOrEmpty(filename)) batch.Failed = true;
        }
        internal static void Invalidate(ZDO data) { if (data != null) Loaded.Remove(data); }
        internal static void Observe(ZDO data)
        {
            var batch = Active;
            if (!Context(batch) || batch.InnerDepth <= 0 || data?.GetPrefab() != "piece_workbench".GetStableHashCode()) return;
            string id = data.GetString(WorkbenchEnchantmentRuntime.StationKey, "");
            if (!Guid.TryParseExact(id, "N", out _)) return;
            Loaded.Remove(data);
            Loaded.Add(data, new Proof { Batch = batch,
                Session = batch.Session, Manager = batch.Manager, World = batch.World, Native = data.m_uid,
                Station = id, Creator = data.GetLong(ZDOVars.s_creator, 0), Position = data.GetPosition() });
        }
        internal static bool Matches(ZDO data, string station)
        {
            return data != null && MasterIdolLoadIdentity.LoadReady && Loaded.TryGetValue(data, out var p) &&
                p.Batch.Valid && Context(p.Batch) && p.Batch.FinalGeneration == MasterIdolLoadIdentity.Generation &&
                p.Native == data.m_uid && p.Session == ZNet.instance && p.Manager == ZDOMan.instance &&
                p.World == ZNet.instance.GetWorldUID() && ReferenceEquals(p.Manager.GetZDO(data.m_uid), data) &&
                p.Station == station && data.GetString(WorkbenchEnchantmentRuntime.StationKey, "") == station &&
                p.Creator == data.GetLong(ZDOVars.s_creator, 0) && p.Position == data.GetPosition();
        }
    }
    [HarmonyPatch(typeof(ZDO), "Load")]
    internal static class WorkbenchEnchantmentNativeLoadedPatch
    {
        static void Prefix(ZDO __instance) => WorkbenchEnchantmentNativeIdentity.Invalidate(__instance);
        static void Postfix(ZDO __instance, bool __runOriginal) { if (__runOriginal) WorkbenchEnchantmentNativeIdentity.Observe(__instance); }
    }
    [HarmonyPatch(typeof(ZNet), "LoadWorld")]
    internal static class WorkbenchEnchantmentAuthoritativeLoadPatch
    {
        static void Prefix(ZNet __instance, out WorkbenchEnchantmentNativeIdentity.Batch __state) => __state = WorkbenchEnchantmentNativeIdentity.Begin(__instance);
        static void Postfix(bool __runOriginal, bool __result, WorkbenchEnchantmentNativeIdentity.Batch __state)
        { if (__state != null) { __state.Original = __runOriginal; __state.Result = __result; } }
        static Exception Finalizer(Exception __exception, WorkbenchEnchantmentNativeIdentity.Batch __state)
        { WorkbenchEnchantmentNativeIdentity.End(__state, __exception); return __exception; }
    }
    [HarmonyPatch(typeof(ZNet), "LoadOldWorld")]
    internal static class WorkbenchEnchantmentLegacyAuthoritativeLoadPatch
    {
        static void Prefix(ZNet __instance, out WorkbenchEnchantmentNativeIdentity.Batch __state) => __state = WorkbenchEnchantmentNativeIdentity.Begin(__instance);
        static void Postfix(bool __runOriginal, WorkbenchEnchantmentNativeIdentity.Batch __state)
        {
            // Legacy loading is a sibling of LoadWorld, not nested in it. The
            // void outer method alone proves nothing: a completed native inner
            // load and final no-error checks are required before proof admission.
            if (__state != null) { __state.Original = __runOriginal; __state.Result = __runOriginal && __state.SawInner; }
        }
        static Exception Finalizer(Exception __exception, WorkbenchEnchantmentNativeIdentity.Batch __state)
        { WorkbenchEnchantmentNativeIdentity.End(__state, __exception); return __exception; }
    }
    [HarmonyPatch]
    internal static class WorkbenchEnchantmentInnerLoadPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        { foreach (var method in AccessTools.GetDeclaredMethods(typeof(ZDOMan))) if (method.Name == "LoadChunks" || method.Name == "Load") yield return method; }
        static void Prefix(ZDOMan __instance, out WorkbenchEnchantmentNativeIdentity.Inner __state) => __state = WorkbenchEnchantmentNativeIdentity.BeginInner(__instance);
        static void Postfix(bool __runOriginal, WorkbenchEnchantmentNativeIdentity.Inner __state) { if (__state != null) __state.Original = __runOriginal; }
        static Exception Finalizer(Exception __exception, WorkbenchEnchantmentNativeIdentity.Inner __state)
        { WorkbenchEnchantmentNativeIdentity.EndInner(__state, __exception); return __exception; }
    }
    [HarmonyPatch(typeof(ChunkSaveMapping), "GetChunkFilename")]
    internal static class WorkbenchEnchantmentLoadFilenamePatch
    { static void Postfix(ChunkSaveMapping __instance, string __result) => WorkbenchEnchantmentNativeIdentity.Filename(__instance, __result); }
    [HarmonyPatch]
    internal static class WorkbenchEnchantmentNativeResetPatch
    {
        static IEnumerable<MethodBase> TargetMethods()
        { foreach (var method in AccessTools.GetDeclaredMethods(typeof(ZDOMan))) if (method.Name == "ResetBeforeLoad" || method.Name == "ShutDown") yield return method; }
        static void Prefix(MethodBase __originalMethod)
        { if (__originalMethod.Name == "ShutDown") WorkbenchEnchantmentNativeIdentity.Abort(); else WorkbenchEnchantmentNativeIdentity.Reset(); }
    }
}
