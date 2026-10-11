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
            { "swampplant2", "Assets/world/Props/SwampPlant/model/swampplant2.mat" },
            { "bone", "Assets/GameElements/Items/_res/bone/bone.mat" },
            { "Boletus_Yellow", "Assets/3rd party/SkythianCat/Hand_Painted_Nature_Kit_LITE/Materials/Boletus_Yellow.mat" },
            { "Dandelion", "Assets/3rd party/SkythianCat/Hand_Painted_Nature_Kit_LITE/Materials/Dandelion.mat" },
            { "Deer 2", "Assets/3rd party/Malbers Animations/Animals Packs/01 Forest Pack/Deer/Models/Materials/Deer 2.mat" },
            { "Guardstone_OdenGlow_mat", "Assets/GameElements/Pieces/_res/guardstone/model/Guardstone_OdenGlow_mat.mat" },
            { "Guardstone_Oden_mat", "Assets/GameElements/Pieces/_res/guardstone/model/Guardstone_Oden_mat.mat" },
            { "MistlandsMushrooms_item", "Assets/GameElements/Items/_res/MistlandsMushrooms/model/MistlandsMushrooms_item.mat" },
            { "PineTree_01", "Assets/world/Props/PineTree/Materials/PineTree_01.mat" },
            { "Shoot_Stack_mat", "Assets/world/Props/Shoots/materials/Shoot_Stack_mat.mat" },
            { "amber", "Assets/GameElements/Items/materials/_res/amber/amber.mat" },
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
            { "roundlog", "Assets/GameElements/Items/materials/_res/roundlog/roundlog.mat" },
            { "silverbar", "Assets/GameElements/Items/_res/silver/silverbar.mat" },
            { "snow", "Assets/GameElements/Pieces/_res/snow.mat" },
            { "stone", "Assets/GameElements/Items/_res/stone/stone.mat" },
            { "surtlingcore", "Assets/GameElements/Items/materials/_res/surtlingcore/surtlingcore.mat" },
            { "swamptree1_bark", "Assets/world/Props/SwampTree/model/swamptree1_bark.mat" },
            { "tarlump", "Assets/GameElements/Items/materials/_res/tar/tarlump.mat" }
        };
        private static readonly HashSet<MasterIdolArtwork> Live = new HashSet<MasterIdolArtwork>();
        private readonly List<Surface> _surfaces = new List<Surface>();
        private GameObject _root;
        private MeshRenderer _native;
        private Color _color;
        private MasterIdolEnvironment _environment;
        private float _last = float.NaN;
        private Model _model;
        private GameObject _decorRoot;
        private bool _decorReady, _decorActive;
        private float _decorRetryAt;
        private int _decorAttempts;

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
            if (name == "Guardstone_Oden_mat" && native != null)
                foreach (var mat in native.sharedMaterials)
                    if (mat != null && Name(mat) == name) return mat;
            if (MaterialPaths.TryGetValue(name, out var path))
                return NativeSoftVisualAssets.Get<Material>(path); // exact catalog identity; asynchronous, scene-owned
            Material match = null;
            if (!string.IsNullOrEmpty(prefab))
            {
                match = FromPrefab(ObjectDB.instance?.GetItemPrefab(prefab), name) ?? FromPrefab(ZNetScene.instance?.GetPrefab(prefab), name);
                if (match != null) return match;
            }
            // Unknown identities are unavailable, not permission for a global path/material scan.
            return null;
        }
        private static void FreezeStaticDeformation(Material material)
        {
            // Native vegetation/snow shaders deform full trees/terrain. These are fixed idol parts.
            string shader = material.shader != null ? material.shader.name : "";
            if (shader == "Custom/Vegetation")
                foreach (string name in new[] { "_SwayDistance", "_RippleDistance", "_PushDistance", "_CamCull", "_DistanceScale" })
                    if (material.HasProperty(name)) material.SetFloat(name, 0f);
            if (shader == "Valheim/Snow Mesh")
            {
                foreach (string name in new[] { "_OffsetNoiseStrength", "_OffsetNoiseStrengthHigh", "_OffsetNoiseStrengthLow", "_ScaleOutFactor", "_Metallic" })
                    if (material.HasProperty(name)) material.SetFloat(name, 0f);
            }
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
            catch { foreach (var mesh in meshes) if (mesh != null) Cleanup(() => UnityEngine.Object.Destroy(mesh)); throw; }
        }
        internal static MasterIdolArtwork TryCreate(MasterIdolVisual owner, string id, Color color)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || owner.NativeModel == null) return null;
            var art = new MasterIdolArtwork { _native = owner.NativeModel, _color = color };
            try
            {
                var model = Load(id); art._model = model; var sources = new Material[model.Data.Parts.Length];
                bool ready = true; // optional ambient readiness must never hide the approved model
                for (int i = 0; i < sources.Length; i++)
                {
                    var p = model.Data.Parts[i]; if (p.Role == 0) continue; // flora/snow is optional and deferred separately
                    sources[i] = NativeMaterial(p.Material, p.Prefab, owner.NativeModel);
                    if (sources[i] == null) ready = false;
                }
                if (!ready) return null; // warm all dependencies together; caller retries while native loads are pending
                art._root = new GameObject("VM_IdolArtwork_" + id); art._root.SetActive(false); art._root.transform.SetParent(owner.transform, false);
                for (int i = 0; i < sources.Length; i++)
                {
                    var p = model.Data.Parts[i]; if (p.Role == 0) continue;
                    var material = new Material(sources[i]) { name = "VM_" + id + "_" + p.Name };
                    var surface = new Surface { Material = material, Role = p.Role, Emission = Color.black };
                    art._surfaces.Add(surface); // own the clone before any shader/property edit can fail
                    FreezeStaticDeformation(material);
                    surface.Emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                    if (p.Role != 0)
                    {
                        // The authored surface follows the wood; its narrow recess remains dark when disabled.
                        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", Texture2D.whiteTexture);
                        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
                        material.DisableKeyword("_NORMALMAP");
                        if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                        material.SetColor("_Color", p.Role == 1 ? new Color(.19f, .13f, .065f) : new Color(.035f, .022f, .012f));
                        if (p.Role == 1 && !material.HasProperty("_EmissionColor")) throw new InvalidOperationException("Native Ward rune emission property unavailable.");
                        material.EnableKeyword("_EMISSION"); surface.Emission = Color.black;
                    }
                    var obj = new GameObject(p.Name); obj.layer = owner.NativeModel.gameObject.layer; obj.transform.SetParent(art._root.transform, false);
                    obj.AddComponent<MeshFilter>().sharedMesh = model.Meshes[i];
                    var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = p.Role == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    renderer.receiveShadows = p.Role == 0;
                }
                art._environment = MasterIdolEnvironment.Create(art._root.transform, id, color);
                // Keep the intact native frame, including its exact transform, UVs, normals and material.
                // Only the carving overlay and optional surroundings are ours.
                art.Tick(0, false); art._root.SetActive(true); Live.Add(art); return art;
            }
            catch (Exception e)
            {
                art.Dispose(); MasteryPlugin.Log?.LogWarning("[MasterIdols] Approved artwork unavailable; native model retained: " + id + ": " + e.Message); return null;
            }
        }
        private void UpdateDecor(bool active)
        {
            if (active && !_decorReady && Time.unscaledTime >= _decorRetryAt)
            {
                if (_decorAttempts < 61) _decorAttempts++;
                _decorRetryAt = Time.unscaledTime + (_decorAttempts < 60 ? .5f : 10f);
                var sources = new Material[_model.Data.Parts.Length]; bool ready = true;
                for (int i = 0; i < sources.Length; i++)
                {
                    var part = _model.Data.Parts[i]; if (part.Role != 0) continue;
                    sources[i] = NativeMaterial(part.Material, part.Prefab, _native);
                    if (sources[i] == null) ready = false;
                }
                if (ready)
                {
                    int first = _surfaces.Count;
                    try
                    {
                        _decorRoot = new GameObject("VM_IdolBiomeDecor"); _decorRoot.SetActive(false);
                        _decorRoot.transform.SetParent(_root.transform, false);
                        for (int i = 0; i < sources.Length; i++)
                        {
                            var part = _model.Data.Parts[i]; if (part.Role != 0) continue;
                            var material = new Material(sources[i]) { name = "VM_IdolDecor_" + part.Name };
                            var surface = new Surface { Material = material, Role = 0, Emission = Color.black };
                            _surfaces.Add(surface); // register ownership before deformation/property edits
                            FreezeStaticDeformation(material);
                            surface.Emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                            var obj = new GameObject(part.Name); obj.layer = _native.gameObject.layer;
                            obj.transform.SetParent(_decorRoot.transform, false);
                            obj.AddComponent<MeshFilter>().sharedMesh = _model.Meshes[i];
                            var renderer = obj.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                        }
                        _decorReady = true; _last = float.NaN;
                    }
                    catch (Exception e)
                    {
                        if (_decorRoot != null) Cleanup(() => UnityEngine.Object.Destroy(_decorRoot)); _decorRoot = null;
                        for (int i = _surfaces.Count - 1; i >= first; i--)
                        { if (_surfaces[i].Material != null) { var owned = _surfaces[i].Material; Cleanup(() => UnityEngine.Object.Destroy(owned)); } _surfaces.RemoveAt(i); }
                        if (_decorAttempts == 1) MasteryPlugin.Log?.LogWarning("[MasterIdols] Optional biome decor pending; native frame/runes retained: " + e.Message);
                    }
                }
            }
            if (_decorRoot != null && _decorActive != active) { _decorRoot.SetActive(active); _decorActive = active; }
        }
        internal void Tick(float brightness, bool active)
        {
            UpdateDecor(active);
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
        private static void Cleanup(Action action)
        {
            try { action(); } catch (Exception e) { MasteryPlugin.Log?.LogWarning("[MasterIdols] Cosmetic cleanup: " + e.Message); }
        }
        public void Dispose()
        {
            Cleanup(() => _environment?.Dispose()); _environment = null;
            if (_root != null) { Cleanup(() => _root.SetActive(false)); Cleanup(() => UnityEngine.Object.Destroy(_root)); _root = null; }
            foreach (var surface in _surfaces) if (surface.Material != null) Cleanup(() => UnityEngine.Object.Destroy(surface.Material));
            _surfaces.Clear(); _decorRoot = null; _model = null; Live.Remove(this);
        }
        internal static void Shutdown()
        {
            foreach (var art in new List<MasterIdolArtwork>(Live)) Cleanup(art.Dispose);
            foreach (var model in Models.Values) foreach (var mesh in model.Meshes) if (mesh != null) Cleanup(() => UnityEngine.Object.Destroy(mesh));
            Models.Clear(); Live.Clear();
        }
    }
}
