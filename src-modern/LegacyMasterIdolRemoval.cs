using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Direct human retirement of all six discontinued CUSTOM physical prefabs.
    // No purchase, material, admission, ledger or entitlement writes belong here.
    internal static class LegacyMasterIdolRemoval
    {
        internal static bool LegacyActionsDisabled => true;
        internal static void Initialize() => Shutdown();
        internal static void Shutdown()
        { Requests.Clear(); Session = null; Manager = null; World = 0; Generation = 0; Cursor = Completed = UnobservedAbsence = 0; Next = 0; }
        private static readonly HashSet<int> Prefabs = new HashSet<int> {
            "VM_MasterIdol_Meadows".GetStableHashCode(), "VM_MasterIdol_BlackForest".GetStableHashCode(),
            "VM_MasterIdol_Swamp".GetStableHashCode(), "VM_MasterIdol_Mountain".GetStableHashCode(),
            "VM_MasterIdol_Plains".GetStableHashCode(), "VM_MasterIdol_Mistlands".GetStableHashCode()
        };
        internal static bool IsLegacyPrefab(int prefab) => prefab != "guard_stone".GetStableHashCode() && Prefabs.Contains(prefab);
        // Root old action/destruction hooks MUST consult this before financial settlement.
        internal static bool IsLegacy(ZDO data) => data != null && IsLegacyPrefab(data.GetPrefab());
        internal sealed class Observation
        {
            internal readonly ZDOID Uid;
            internal readonly ZNet Session;
            internal readonly ZDOMan Manager;
            internal readonly long World;
            internal readonly ulong Generation;
            internal Observation(ZDOID uid)
            { Uid = uid; Session = LegacyMasterIdolRemoval.Session; Manager = LegacyMasterIdolRemoval.Manager; World = LegacyMasterIdolRemoval.World; Generation = LegacyMasterIdolRemoval.Generation; }
        }
        private sealed class Pending
        {
            internal ZDO Native;
            internal int Prefab;
            internal float Retry;
        }
        private static readonly Dictionary<ZDOID, Pending> Requests = new Dictionary<ZDOID, Pending>();
        private static ZNet Session;
        private static ZDOMan Manager;
        private static long World;
        private static ulong Generation;
        private static int Cursor, Completed, UnobservedAbsence;
        private static float Next;
        private static bool Context()
        {
            var net = ZNet.instance; var manager = ZDOMan.instance;
            long world = net?.GetWorld() != null ? net.GetWorldUID() : 0;
            ulong generation = MasterIdolLoadIdentity.Generation;
            if (!ReferenceEquals(Session, net) || !ReferenceEquals(Manager, manager) || World != world || Generation != generation)
            {
                Requests.Clear(); Session = net; Manager = manager; World = world; Generation = generation;
                Cursor = Completed = UnobservedAbsence = 0; Next = 0;
            }
            return net?.IsServer() == true && world != 0 && manager != null && ZNetScene.instance != null && MasterIdolLoadIdentity.LoadReady;
        }
        private static bool Exact(ZDOID uid, Pending p) => p != null && uid != ZDOID.None &&
            ReferenceEquals(Manager.GetZDO(uid), p.Native) && p.Native.m_uid == uid &&
            p.Native.GetPrefab() == p.Prefab && IsLegacyPrefab(p.Prefab);
        internal static void Tick()
        {
            if (!Context() || Time.unscaledTime < Next) return; Next = Time.unscaledTime + .5f;
            if (!MasterIdolWorldIndex.Ensure() || !MasterIdolWorldIndex.Ready) return;
            // This is the existing complete six-prefab index, never an additional world scan.
            var keys = new List<ZDOID>(MasterIdolWorldIndex.Idols.Keys);
            // Existing index postfix can remove a locator when another patch skipped
            // the native original. Already-qualified pending objects remain retryable.
            var seen = new HashSet<ZDOID>(keys);
            foreach(var uid in Requests.Keys)if(seen.Add(uid))keys.Add(uid);
            int count = Math.Min(8, keys.Count);
            for (int i = 0; i < count; i++)
            {
                var uid = keys[(Cursor + i) % keys.Count]; var data = Manager.GetZDO(uid);
                if (!IsLegacy(data) || data.m_uid != uid)
                { if(data == null){if(Requests.Remove(uid))UnobservedAbsence++;MasterIdolWorldIndex.Removed(uid);}else Requests.Remove(uid);continue; }
                if (!Requests.TryGetValue(uid, out var pending))
                {
                    if (Requests.Count >= 128) continue; // Bounded native outstanding window.
                    Requests.Add(uid, pending = new Pending { Native = data, Prefab = data.GetPrefab() });
                }
                else if (!Exact(uid, pending))
                { Requests.Remove(uid); continue; } // Never act on a replaced/pool-reused native object.
                if (Time.unscaledTime >= pending.Retry) Queue(uid, pending);
            }
            Cursor = keys.Count == 0 ? 0 : (Cursor + count) % keys.Count;
            // Missing completion observations are NOT acknowledged. Release only transient
            // tracking capacity; the next complete world/load scan still finds restored shells.
            foreach (var uid in new List<ZDOID>(Requests.Keys))
                if (Manager.GetZDO(uid) == null)
                { Requests.Remove(uid); UnobservedAbsence++; MasterIdolWorldIndex.Removed(uid); }
        }
        private static void Queue(ZDOID uid, Pending pending)
        {
            pending.Retry = Time.unscaledTime + 2f;
            if (!Context() || !Requests.TryGetValue(uid,out var active) || !ReferenceEquals(active,pending) || !Exact(uid, pending)) return;
            var frozen = new Observation(uid);
            // Native DestroyZDO appends a List, so retries must not append duplicates.
            if (Manager.m_destroySendList.Contains(uid)) return;
            var scene = ZNetScene.instance; var instance = scene.FindInstance(uid);
            var view = instance != null ? instance.GetComponent<ZNetView>() : null;
            if (instance != null && (view?.IsValid() != true || !ReferenceEquals(view.GetZDO(), pending.Native))) return;
            try
            {
                if (view != null) view.ClaimOwnership(); else pending.Native.SetOwner(ZDOMan.GetSessionID());
                if (!Context() || !ReferenceEquals(frozen.Session,Session) || !ReferenceEquals(frozen.Manager,Manager) ||
                    frozen.World != World || frozen.Generation != Generation || !Requests.TryGetValue(uid,out active) || !ReferenceEquals(active,pending) ||
                    !Exact(uid, pending) || pending.Native.GetOwner() != ZDOMan.GetSessionID() ||
                    view != null && (!view.IsOwner() || !ReferenceEquals(view.GetZDO(), pending.Native))) return;
                // No WearNTear.Remove/RPC_Remove/Piece.DropResources/legacy settlement.
                if (view != null) scene.Destroy(instance.gameObject); else Manager.DestroyZDO(pending.Native);
            }
            catch (Exception e)
            { MasteryPlugin.Log?.LogWarning("[LegacyIdolRemoval] Native queue deferred: " + e.Message); }
        }
        internal static Observation BeforeNativeRemoval(ZDOMan manager, ZDOID uid)
        {
            try
            {
                return Context() && ReferenceEquals(manager, Manager) && Requests.TryGetValue(uid, out var p) && Exact(uid, p)
                    ? new Observation(uid) : null;
            }
            catch { return null; } // Observation must not break native destruction.
        }
        internal static void AfterNativeRemoval(ZDOMan manager, ZDOID uid, Observation observed, bool ranOriginal)
        {
            try
            {
                if (!ranOriginal || observed == null || observed.Uid != uid || !Context() ||
                    !ReferenceEquals(observed.Session, Session) || !ReferenceEquals(observed.Manager, Manager) ||
                    !ReferenceEquals(manager, Manager) || observed.World != World || observed.Generation != Generation ||
                    Manager.GetZDO(uid) != null || !Requests.Remove(uid)) return;
                Completed++; MasterIdolWorldIndex.Removed(uid); // Frozen locator only; pooled Native is never read.
            }
            catch { } // Root diagnostics/QC verify completion separately from a queued request.
        }
        internal static string Diagnostics() => "world=" + World + " pending=" + Requests.Count +
            " observedNativeRemovals=" + Completed + " unobservedAbsence=" + UnobservedAbsence;
    }
    [HarmonyPatch(typeof(ZDOMan), "HandleDestroyedZDO")]
    internal static class LegacyMasterIdolNativeRemovalPatch
    {
        private static void Prefix(ZDOMan __instance, ZDOID uid, out LegacyMasterIdolRemoval.Observation __state)
            => __state = LegacyMasterIdolRemoval.BeforeNativeRemoval(__instance, uid);
        private static void Postfix(ZDOMan __instance, ZDOID uid, LegacyMasterIdolRemoval.Observation __state, bool __runOriginal)
            => LegacyMasterIdolRemoval.AfterNativeRemoval(__instance, uid, __state, __runOriginal);
    }
}
