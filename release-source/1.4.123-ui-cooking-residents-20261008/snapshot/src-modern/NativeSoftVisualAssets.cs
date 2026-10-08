using System;
using System.Collections.Generic;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;

namespace ValheimMastery
{
    // Load real game visuals by catalogued asset path. Unlike Resources-only
    // lookup this also works before visiting the corresponding biome/trader.
    // No prefab is instantiated and no synchronous bundle wait occurs in play.
    internal static class NativeSoftVisualAssets
    {
        private interface IReference { void Release(); }
        private sealed class Reference<T> : IReference where T : UnityEngine.Object
        {
            internal SoftReference<T> Asset;
            internal Reference(AssetID id) { Asset = new SoftReference<T>(id); Asset.LoadAsync(); }
            public void Release() => Asset.Release();
        }
        private static readonly Dictionary<string, IReference> Held = new Dictionary<string, IReference>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, AssetID> Paths;
        private static readonly Dictionary<string, string> PrefabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static ZNetScene Scene;
        internal static GameObject GetPrefab(string name)
        {
            // Build one name index of the native catalog, not a Resources/world scan per proc.
            if (Paths == null) Get<GameObject>("Assets/Effects/weather/SnowStorm.prefab");
            return PrefabPaths.TryGetValue(name, out string path) ? Get<GameObject>(path) : null;
        }
        internal static T Get<T>(string path) where T : UnityEngine.Object
        {
            if (Application.isBatchMode || ZNetScene.instance == null) return null;
            if (Scene != ZNetScene.instance)
            {
                Release(); Scene = ZNetScene.instance;
            }
            try
            {
                if (!Held.TryGetValue(path, out IReference held))
                {
                    if (Paths == null)
                    {
                        Paths = new Dictionary<string, AssetID>(Runtime.GetAllAssetPathsInBundleMappedToAssetID(), StringComparer.OrdinalIgnoreCase);
                        foreach (string nativePath in Paths.Keys)
                            if (nativePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                            {
                                string nativeName = System.IO.Path.GetFileNameWithoutExtension(nativePath);
                                if (!PrefabPaths.ContainsKey(nativeName)) PrefabPaths.Add(nativeName, nativePath);
                            }
                    }
                    if (!Paths.TryGetValue(path, out AssetID id))
                    {
                        if (Missing.Add(path)) MasteryPlugin.Log.LogWarning("[NativeVisual] Asset absent from active catalog: " + path + "; entries=" + Paths.Count + ". Extended catalog must be enabled at plugin startup.");
                        return null;
                    }
                    held = new Reference<T>(id); Held.Add(path, held);
                }
                var typed = held as Reference<T>;
                return typed != null && typed.Asset.IsLoaded ? typed.Asset.Asset : null;
            }
            catch (Exception error)
            {
                // Asset failures must never cancel a spell or lock an action.
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogWarning("[NativeVisual] " + path + ": " + error.GetType().Name);
                return null;
            }
        }
        internal static void Release()
        {
            foreach (IReference held in Held.Values)
                try { held.Release(); } catch (Exception) { /* Native loader may already be shutting down. */ }
            Held.Clear(); Missing.Clear(); PrefabPaths.Clear(); Paths = null; Scene = null;
        }
    }
    [HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
    internal static class NativeSoftVisualReleasePatch
    { private static void Postfix() => NativeSoftVisualAssets.Release(); }
}
