using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimMastery
{
    internal sealed class MasterIdolArtwork : IDisposable
    {
        private sealed class Model { internal MasterIdolArtworkData Data; internal Mesh[] Meshes; }
        private sealed class Surface { internal Material Material; internal byte Role; internal Color Emission; }
        private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>();
        private static readonly Dictionary<string, string> MaterialPaths = new Dictionary<string, string>
        {
            { "Boletus_Yellow", "Assets/3rd party/SkythianCat/Hand_Painted_Nature_Kit_LITE/Materials/Boletus_Yellow.mat" },
            { "Dandelion", "Assets/3rd party/SkythianCat/Hand_Painted_Nature_Kit_LITE/Materials/Dandelion.mat" },
            { "Deer 2", "Assets/3rd party/Malbers Animations/Animals Packs/01 Forest Pack/Deer/Models/Materials/Deer 2.mat" },
            { "Guardstone_Oden_mat", "Assets/GameElements/Pieces/_res/guardstone/model/Guardstone_Oden_mat.mat" },
            { "MistlandsMushrooms_item", "Assets/GameElements/Items/_res/MistlandsMushrooms/model/MistlandsMushrooms_item.mat" },
            { "PineTree_01", "Assets/world/Props/PineTree/Materials/PineTree_01.mat" },
            { "barley_ripe", "Assets/GameElements/Pieces/_res/barley/materials/barley_ripe.mat" },
            { "blackmarble_movable", "Assets/GameElements/Pieces/_res/Marble/material/blackmarble_movable.mat" },
            { "blackmetal", "Assets/GameElements/Items/_res/blackmetal/blackmetal.mat" },
            { "bronze", "Assets/GameElements/Items/_res/copper/bronze.mat" },
            { "deerhide", "Assets/GameElements/Items/_res/Hide/deerhide.mat" },
            { "eitr", "Assets/GameElements/Items/materials/_res/eitr/eitr.mat" },
            { "finewood_item", "Assets/world/Props/wood/finewood_item.mat" },
            { "flax_item", "Assets/GameElements/Pieces/_res/flax/materials/flax_item.mat" },
            { "grasscross_heath", "Assets/world/Props/ground_clutter/models/materials/grasscross_heath.mat" },
            { "greydwarfeyemat", "Assets/GameElements/Items/_res/greydwarfeye/greydwarfeyemat.mat" },
            { "guck", "Assets/GameElements/Items/_res/resin/guck.mat" },
            { "ice", "Assets/world/Props/Ice/ice.mat" },
            { "iron", "Assets/GameElements/Items/_res/iron/iron.mat" },
            { "loxpelt", "Assets/GameElements/Items/_res/Hide/loxpelt.mat" },
            { "obsidian_nosnow", "Assets/GameElements/Items/materials/_res/obsidian/obsidian_nosnow.mat" },
            { "resin", "Assets/GameElements/Items/_res/resin/resin.mat" },
            { "silverbar", "Assets/GameElements/Items/_res/silver/silverbar.mat" },
            { "snow", "Assets/GameElements/Pieces/_res/snow.mat" },
            { "stone", "Assets/GameElements/Items/_res/stone/stone.mat" },
            { "surtlingcore", "Assets/GameElements/Items/materials/_res/surtlingcore/surtlingcore.mat" },
            { "swamptree1_bark", "Assets/world/Props/SwampTree/model/swamptree1_bark.mat" },
            { "tarlump", "Assets/GameElements/Items/materials/_res/tar/tarlump.mat" },
            { "woodpole", "Assets/GameElements/Pieces/_res/materials/woodpole.mat" }
        };
        private static readonly HashSet<MasterIdolArtwork> Live = new HashSet<MasterIdolArtwork>();
        private readonly List<Surface> _surfaces = new List<Surface>();
        private GameObject _root;
        private MeshRenderer _native;
        private bool _nativeEnabled;
        private Color _color;
        private MasterIdolEnvironment _environment;
        private float _last = float.NaN;

        private static string Name(Material m) => m.name.Replace(" (Instance)", "");
        internal static Material FromPrefab(GameObject prefab, string name)
        {
            if (prefab == null) return null;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in renderer.sharedMaterials)
                    if (mat != null && Name(mat) == name) return mat;
            return null;
        }
        internal static Material NativeMaterial(string name, string prefab, MeshRenderer native = null)
        {
            if (native != null)
                foreach (var material in native.sharedMaterials)
                    if (material != null && Name(material) == name) return material;
            if (MaterialPaths.TryGetValue(name, out var path))
                return NativeSoftVisualAssets.Get<Material>(path); // exact catalog identity; asynchronous, scene-owned
            Material match = null;
            if (!string.IsNullOrEmpty(prefab))
            {
                match = FromPrefab(ObjectDB.instance?.GetItemPrefab(prefab), name) ?? FromPrefab(ZNetScene.instance?.GetPrefab(prefab), name);
                if (match != null) return match;
            }
            // Only exact native names; never substitute a guessed Standard/particle shader.
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                if (material != null && !material.name.StartsWith("VM_") && Name(material) == name) return material;
            return null;
        }
        private static Model Load(string id)
        {
            if (Models.TryGetValue(id, out var found)) return found;
            MasterIdolArtworkData data;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ValheimMastery.Totems." + id + ".vmt"))
                data = MasterIdolArtworkData.Read(stream);
            var meshes = new Mesh[data.Parts.Length];
            try
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    var part = data.Parts[i]; int count = part.Vertices.Length / 8;
                    var vertices = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count]; var triangles = new int[count];
                    for (int j = 0; j < count; j++)
                    {
                        int k = j * 8; var v = part.Vertices;
                        vertices[j] = new Vector3(v[k], v[k+1], v[k+2]); normals[j] = new Vector3(v[k+3], v[k+4], v[k+5]);
                        uv[j] = new Vector2(v[k+6], v[k+7]); triangles[j] = j;
                    }
                    var mesh = new Mesh { name = "VM_" + id + "_" + i, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    meshes[i] = mesh;
                    mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateTangents(); mesh.RecalculateBounds();
                }
                found = new Model { Data = data, Meshes = meshes }; Models.Add(id, found); return found;
            }
            catch { foreach (var mesh in meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh); throw; }
        }
        internal static MasterIdolArtwork TryCreate(MasterIdolVisual owner, string id, Color color)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || owner.NativeModel == null) return null;
            var art = new MasterIdolArtwork { _native = owner.NativeModel, _nativeEnabled = owner.NativeModel.enabled, _color = color };
            try
            {
                var model = Load(id); var sources = new Material[model.Data.Parts.Length];
                bool ready = MasterIdolEnvironment.Ready(id);
                for (int i = 0; i < sources.Length; i++)
                {
                    var p = model.Data.Parts[i]; sources[i] = NativeMaterial(p.Material, p.Prefab, owner.NativeModel);
                    if (sources[i] == null) ready = false;
                }
                if (!ready) return null; // warm all dependencies together; caller retries while native loads are pending
                art._root = new GameObject("VM_IdolArtwork_" + id); art._root.SetActive(false); art._root.transform.SetParent(owner.transform, false);
                for (int i = 0; i < sources.Length; i++)
                {
                    var p = model.Data.Parts[i]; var material = new Material(sources[i]) { name = "VM_" + id + "_" + p.Name };
                    var surface = new Surface { Material = material, Role = p.Role, Emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black };
                    art._surfaces.Add(surface);
                    if (p.Role != 0)
                    {
                        // The authored surface follows the wood; its narrow recess remains dark when disabled.
                        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", Texture2D.whiteTexture);
                        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
                        if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                        material.SetColor("_Color", p.Role == 1 ? new Color(.19f, .13f, .065f) : new Color(.035f, .022f, .012f));
                        material.EnableKeyword("_EMISSION"); surface.Emission = Color.black;
                    }
                    var obj = new GameObject(p.Name); obj.layer = owner.NativeModel.gameObject.layer; obj.transform.SetParent(art._root.transform, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = model.Meshes[i];
                    var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = p.Role == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = p.Role == 0;
                }
                art._environment = MasterIdolEnvironment.Create(art._root.transform, id, color);
                art._native.enabled = false; art.Tick(0, false); art._root.SetActive(true); Live.Add(art); return art;
            }
            catch (Exception e)
            {
                art.Dispose(); MasteryPlugin.Log?.LogWarning("[MasterIdols] Approved artwork unavailable; native model retained: " + id + ": " + e.Message); return null;
            }
        }
        internal void Tick(float brightness, bool active)
        {
            brightness = Mathf.Clamp(brightness, 0, 1.75f);
            if (_last != brightness)
            {
                foreach (var surface in _surfaces)
                {
                    var m = surface.Material;
                    if (m == null) continue;
                    if (surface.Role == 1)
                    {
                        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", _color * brightness * 2.1f);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.Lerp(new Color(.19f,.13f,.065f), _color * .65f, Mathf.Clamp01(brightness)));
                    }
                    else if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", surface.Emission * (.12f + .88f * Mathf.Clamp01(brightness)));
                }
                _last = brightness;
            }
            _environment?.Tick(active);
        }
        public void Dispose()
        {
            _environment?.Dispose(); _environment = null;
            if (_native != null) _native.enabled = _nativeEnabled;
            if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); _root = null; }
            foreach (var surface in _surfaces) if (surface.Material != null) UnityEngine.Object.Destroy(surface.Material);
            _surfaces.Clear(); Live.Remove(this);
        }
        internal static void Shutdown()
        {
            foreach (var art in new List<MasterIdolArtwork>(Live)) art.Dispose();
            foreach (var model in Models.Values) foreach (var mesh in model.Meshes) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            Models.Clear(); Live.Clear();
        }
    }
}
