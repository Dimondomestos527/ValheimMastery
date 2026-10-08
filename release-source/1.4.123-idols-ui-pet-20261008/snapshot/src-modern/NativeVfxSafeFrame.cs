using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // All component destruction is drained from a plain Unity LateUpdate, never
    // from animation, physics or render callbacks. Work and retries are bounded.
    internal static class NativeVfxSafeFrame
    {
        private const int PendingLimit = 64, PerFrameLimit = 12;
        private static readonly Queue<DeferredNativeVfx> Pending = new Queue<DeferredNativeVfx>();
        private sealed class NamedRequest { internal string Name; internal Func<GameObject, bool> Play; internal float Until, Retry; }
        private static readonly List<NamedRequest> Requests = new List<NamedRequest>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static NativeVfxFrameDriver Driver;
        private static ZNetScene Scene;
        private static bool Draining;
        internal static bool CanStage => Pending.Count < PendingLimit;
        internal static bool IsDraining => Draining;
        private static void Ensure()
        {
            if (Scene != ZNetScene.instance)
            { Clear(); Scene = ZNetScene.instance; }
            if (Driver == null) Driver = new GameObject("ValheimMastery_CosmeticVfxCoordinator").AddComponent<NativeVfxFrameDriver>();
        }
        internal static void Stage(DeferredNativeVfx visual)
        { Ensure(); Pending.Enqueue(visual); }
        internal static void AfterReady(GameObject root, Action play)
        {
            if (root == null || play == null) return;
            DeferredNativeVfx pending = root.GetComponent<DeferredNativeVfx>();
            if (pending == null || (pending.IsPrepared && root.activeInHierarchy)) play(); else pending.AfterReady(play);
        }
        internal static void Request(string name, Func<GameObject, bool> play)
        {
            Ensure();
            if (Requests.Count >= PendingLimit) { Warn(name, "pending asset request budget reached"); return; }
            Requests.Add(new NamedRequest { Name = name, Play = play, Until = Time.time + 1.5f, Retry = Time.time });
        }
        internal static void Pump()
        {
            if (Scene != ZNetScene.instance || Player.m_localPlayer == null) { Clear(); Scene = ZNetScene.instance; return; }
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) Requests.Clear();
            for (int i = Requests.Count - 1; i >= 0; i--)
            {
                NamedRequest request = Requests[i];
                if (Time.time >= request.Until)
                { Warn(request.Name, "asset/budget unavailable within 1.5s"); Requests.RemoveAt(i); continue; }
                if (Time.time < request.Retry) continue;
                request.Retry = Time.time + .1f;
                try
                {
                    GameObject prefab = NativePerkAssetResolver.Resolve(request.Name);
                    if (prefab != null && request.Play(prefab)) Requests.RemoveAt(i);
                }
                catch (Exception error)
                { Warn(request.Name, error.GetType().Name); Requests.RemoveAt(i); }
            }
            Draining = true;
            try
            {
                int count = Mathf.Min(Pending.Count, PerFrameLimit);
                for (int i = 0; i < count; i++)
                {
                    DeferredNativeVfx visual = Pending.Dequeue();
                    if (visual != null) visual.Prepare();
                }
            }
            finally { Draining = false; }
        }
        internal static void Warn(string name, string reason)
        {
            if (Warned.Add(name + ":" + reason))
                MasteryPlugin.Log.LogWarning("[NativeVFX] " + name + " skipped: " + reason);
        }
        internal static void Clear()
        {
            while (Pending.Count > 0) { DeferredNativeVfx visual = Pending.Dequeue(); if (visual != null) visual.Abort(); }
            Requests.Clear(); Warned.Clear();
        }
        internal static void DriverDestroyed(NativeVfxFrameDriver driver)
        { if (ReferenceEquals(Driver, driver)) { Clear(); Driver = null; Scene = null; } }
    }

    internal sealed class NativeVfxFrameDriver : MonoBehaviour
    {
        private void LateUpdate() => NativeVfxSafeFrame.Pump();
        private void OnDestroy() => NativeVfxSafeFrame.DriverDestroyed(this);
    }

    internal sealed class DeferredNativeVfx : MonoBehaviour
    {
        private GameObject Content, Source;
        private bool Prepared, Aborted;
        private Action ReadyAction;
        internal bool IsPrepared => Prepared;
        internal void Initialize(GameObject content, GameObject source)
        { Content = content; Source = source; NativeVfxSafeFrame.Stage(this); }
        internal void AfterReady(Action play) { if (!Aborted) ReadyAction += play; }
        internal void Prepare()
        {
            if (Prepared || Aborted || !NativeVfxSafeFrame.IsDraining) return;
            if (Content == null) { Abort(); return; }
            try
            {
                if (!PerkNativeFeedback.Sanitize(Content))
                { PerkNativeFeedback.Fail(Source, "native controllers could not be stripped safely"); Abort(); return; }
                Prepared = true;
                Content.SetActive(true);
                if (gameObject.activeInHierarchy)
                    BeginPlayback();
            }
            catch (Exception error)
            { PerkNativeFeedback.Fail(Source, error.GetType().Name); Abort(); }
        }
        private void OnEnable()
        {
            if (!Prepared) return;
            BeginPlayback();
        }
        private void BeginPlayback()
        {
            if (Aborted) return;
            try
            {
                GetComponent<VfxPoolBaseline>()?.FinishPendingRestart(); GetComponent<VfxPoolLease>()?.NotifyReady();
                Action play = ReadyAction; ReadyAction = null; play?.Invoke();
            }
            catch (Exception error)
            {
                // A caller's stale presentation callback is not evidence that the
                // shared native prefab is unsafe. Clean this instance only.
                NativeVfxSafeFrame.Warn(gameObject.name, "playback " + error.GetType().Name); Abort();
            }
        }
        internal void Abort()
        {
            if (Aborted) return;
            Aborted = true;
            // OnDestroy is not guaranteed for a wrapper that was never enabled.
            GetComponent<VfxPoolLease>()?.Cancel();
            if (Content != null) Content.SetActive(false);
            ReadyAction = null;
            gameObject.SetActive(false); Destroy(gameObject);
        }
    }
}
