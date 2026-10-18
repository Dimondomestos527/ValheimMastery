using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Visual-only native donor hierarchies; readiness never gates gameplay.
    internal static class Ashlands70Presentation
    {
        private sealed class Visual
        {
            internal GameObject Root;
            internal Ashlands70State State;
            internal ParticleSystem Zaps;
            internal readonly List<Transform> Pieces = new List<Transform>();
            internal readonly List<ParticleSystem> Fragments = new List<ParticleSystem>();
            internal float NextEmission, NextArcUpdate;
            internal LineRenderer ChainArc, ChainCore;
            internal readonly Vector3[] ArcPoints = new Vector3[49];
            internal readonly System.Random ArcRandom = new System.Random(7070);
        }
        private static readonly Dictionary<string, Visual> Spells = new Dictionary<string, Visual>();
        private static readonly Dictionary<ZDOID, Ashlands70Eyes> Eyes = new Dictionary<ZDOID, Ashlands70Eyes>();
        private static readonly List<string> Remove = new List<string>();
        private static readonly List<ZDOID> RemoveEyes = new List<ZDOID>();
        private static readonly HashSet<ZDOID> FailedEyes = new HashSet<ZDOID>();
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static readonly HashSet<string> Failed = new HashSet<string>();
        private static float NextDiscover;
        internal static Vector3 OrbitPoint(Player owner, int slot)
        {
            float angle = Time.time * 1.8f + slot * Mathf.PI * 2f / Ashlands70Rules.Shards;
            return owner.GetCenterPoint() + new Vector3(Mathf.Cos(angle) * 1.15f, .15f + .22f * Mathf.Sin(angle * 2f), Mathf.Sin(angle) * 1.15f);
        }
        internal static bool Visible => !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null &&
            Player.m_localPlayer != null && MasteryPlugin.Settings.Enabled.Value && MasteryPlugin.Settings.EnablePerkProcVFX.Value;
        internal static void Tick()
        {
            Ashlands70Audio.Tick();
            if (!Visible) { Clear(); return; }
            long now = Ashlands70Cast.Now;
            if (Time.unscaledTime >= NextDiscover)
            {
                NextDiscover = Time.unscaledTime + .5f; Seen.Clear();
                foreach (Ashlands70State state in Ashlands70World.ActiveStates)
                {
                    Ashlands70Grant g = state.Grant;
                    if (g.Kind == Ashlands70Kind.Madness || !Ashlands70Rules.Active(now, g.Until)) continue;
                    Character caster = Ashlands70World.Resolve(g.Caster);
                    Vector3 point = g.Kind == Ashlands70Kind.Orbit && caster != null ? caster.transform.position : g.Origin;
                    if ((point - Player.m_localPlayer.transform.position).sqrMagnitude > 6400f ||
                        (g.Kind == Ashlands70Kind.Orbit && (caster == null || caster.IsDead()))) continue;
                    Seen.Add(g.Token);
                    if (!Spells.ContainsKey(g.Token) && !Failed.Contains(g.Token)) { Visual v = Create(state); if (v != null) Spells.Add(g.Token, v); }
                }
                Remove.Clear(); foreach (var row in Spells) if (!Seen.Contains(row.Key)) Remove.Add(row.Key);
                foreach (string key in Remove) { UnityEngine.Object.Destroy(Spells[key].Root); Spells.Remove(key); }
                Failed.RemoveWhere(token => !Seen.Contains(token));
                RemoveEyes.Clear();
                foreach (ZDOID id in FailedEyes) if (!Ashlands70Madness.Active(Ashlands70World.Resolve(id))) RemoveEyes.Add(id);
                foreach (ZDOID id in RemoveEyes) FailedEyes.Remove(id);
                foreach (ZDOID id in Ashlands70Madness.Targets)
                {
                    Character body = Ashlands70World.Resolve(id);
                    if (!Ashlands70Madness.Active(body) || Eyes.ContainsKey(id) || FailedEyes.Contains(id) ||
                        (body.transform.position - Player.m_localPlayer.transform.position).sqrMagnitude > 3600f) continue;
                    try { Ashlands70Eyes eye = Ashlands70Eyes.Create(body); if (eye != null) Eyes.Add(id, eye); }
                    catch (Exception error)
                    {
                        FailedEyes.Add(id);
                        MasteryPlugin.Log.LogWarning("[Ashlands70] madness visual disabled for this target: " + error.GetType().Name);
                    }
                }
                RemoveEyes.Clear();
                foreach (var row in Eyes) if (row.Value == null || !Ashlands70Madness.Active(row.Value.Body) ||
                    (row.Value.Body.transform.position - Player.m_localPlayer.transform.position).sqrMagnitude > 4225f) RemoveEyes.Add(row.Key);
                foreach (ZDOID id in RemoveEyes) { if (Eyes[id] != null) UnityEngine.Object.Destroy(Eyes[id].gameObject); Eyes.Remove(id); }
            }
            foreach (Visual v in Spells.Values)
            {
                Ashlands70Grant g = v.State.Grant; bool active = Ashlands70Rules.Active(now, g.Until);
                if (v.Root != null) v.Root.SetActive(active); if (!active) continue;
                if (g.Kind == Ashlands70Kind.Orbit)
                {
                    Player caster = Ashlands70World.Resolve(g.Caster) as Player;
                    if (caster == null || caster.IsDead()) { v.Root.SetActive(false); continue; }
                    ZDO data = caster.m_nview.GetZDO();
                    int remaining = data.GetLong(Ashlands70World.OrbitUntilKey, 0) == g.Until ?
                        Mathf.Clamp(data.GetInt(Ashlands70World.OrbitCountKey, 0), 0, Ashlands70Rules.Shards) : 0;
                    for (int i = 0; i < v.Pieces.Count; i++)
                    {
                        v.Pieces[i].gameObject.SetActive(i < remaining);
                        if (i < remaining) { v.Pieces[i].position = OrbitPoint(caster, i); v.Pieces[i].Rotate(35f * Time.deltaTime, 65f * Time.deltaTime, 28f * Time.deltaTime); }
                    }
                }
                if (g.Kind == Ashlands70Kind.Chain && Time.unscaledTime >= v.NextArcUpdate)
                { v.NextArcUpdate = Time.unscaledTime + 1f / 12f; UpdateChainArcs(v); }
                if (v.Zaps != null && Time.unscaledTime >= v.NextEmission)
                {
                    v.NextEmission = Time.unscaledTime + .12f;
                    int count = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(g.Origin, g.End)), 1, 12);
                    for (int i = 0; i <= count; i++) Emit(v.Zaps, Vector3.Lerp(g.Origin, g.End, i / (float)count), .25f);
                }
            }
        }
        private static Visual Create(Ashlands70State state)
        {
            var root = new GameObject("VM_Ashlands70_" + state.Grant.Kind); root.SetActive(false);
            var v = new Visual { Root = root, State = state }; bool ready = false;
            try
            {
                if (state.Grant.Kind == Ashlands70Kind.Chain) ready = Chain(v);
                else if (state.Grant.Kind == Ashlands70Kind.Grove) ready = Ashlands70GroveVisual.Build(root.transform, state.Grant);
                else if (state.Grant.Kind == Ashlands70Kind.Orbit) ready = Orbit(v);
            }
            catch (Exception error) { Failed.Add(state.Grant.Token); MasteryPlugin.Log.LogWarning("[Ashlands70Visual] " + state.Grant.Kind + ": " + error.GetType().Name); }
            if (!ready) { UnityEngine.Object.Destroy(root); return null; }
            root.SetActive(true);
            foreach (ParticleSystem fragment in v.Fragments) { fragment.Play(); fragment.Emit(1); }
            return v;
        }
        private static bool Chain(Visual v)
        {
            GameObject native = ObjectDB.instance?.GetItemPrefab("Chain") ?? NativeSoftVisualAssets.Get<GameObject>("Assets/GameElements/Items/materials/Chain.prefab");
            Transform source = native != null ? native.transform.Find("model") : null;
            MeshFilter mesh = source != null ? source.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = source != null ? source.GetComponent<MeshRenderer>() : null;
            if (mesh?.sharedMesh == null || renderer?.sharedMaterial == null) return false;
            v.Zaps = RedZaps(v.Root.transform, 96); if (v.Zaps == null) return false;
            Material lightning = v.Zaps.GetComponent<ParticleSystemRenderer>().trailMaterial;
            if (lightning == null) return false;
            v.ChainArc = NativeLine(v.Root.transform, "VM_DundrRedLightning", lightning, .09f, new Color(1f, .025f, .045f, 1f));
            v.ChainCore = NativeLine(v.Root.transform, "VM_DundrRedCore", lightning, .027f, new Color(1f, .38f, .32f, 1f));
            Vector3 a = v.State.Grant.Origin, delta = v.State.Grant.End - a;
            float distance = delta.magnitude, nativeLength = mesh.sharedMesh.bounds.size.y;
            if (nativeLength < .01f) return false;
            Quaternion rotation = distance > .01f ? Quaternion.FromToRotation(Vector3.up, delta / distance) : Quaternion.identity;
            int links = Mathf.Clamp(Mathf.CeilToInt(distance / .85f), 1, 15);
            for (int i = 0; i < links; i++)
            {
                Transform part = CopyMesh(mesh, renderer, v.Root.transform);
                part.position = a + delta * ((i + .5f) / links);
                part.rotation = rotation * Quaternion.AngleAxis((i & 1) * 90f, Vector3.up);
                part.localScale = new Vector3(.65f, Mathf.Max(.1f, distance / links / nativeLength), .65f);
                part.position -= part.rotation * Vector3.Scale(mesh.sharedMesh.bounds.center, part.localScale);
            }
            Transform anchor = CopyMesh(mesh, renderer, v.Root.transform);
            anchor.position = a; anchor.rotation = Quaternion.Euler(90f, 0f, 0f); anchor.localScale = Vector3.one * .7f;
            UpdateChainArcs(v); v.NextArcUpdate = Time.unscaledTime + 1f / 12f;
            return true;
        }
        private static LineRenderer NativeLine(Transform parent, string name, Material material, float width, Color color)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>(); line.transform.SetParent(parent, false);
            line.sharedMaterial = material; line.positionCount = 49; line.useWorldSpace = true; line.widthMultiplier = width;
            line.startColor = color; line.endColor = color; line.numCornerVertices = 0; line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Stretch; line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; return line;
        }
        private static void UpdateChainArcs(Visual v)
        {
            // Persistent buffer/RNG: two bounded native-material layers, 12Hz.
            // Owned child components die with Root; no shared material mutation.
            for (int i = 0; i < v.ArcPoints.Length; i++)
            {
                float u = i / 48f, envelope = Mathf.Sin(u * Mathf.PI);
                v.ArcPoints[i] = Vector3.Lerp(v.State.Grant.Origin, v.State.Grant.End, u) +
                    new Vector3(((float)v.ArcRandom.NextDouble() - .5f) * .48f * envelope,
                        ((float)v.ArcRandom.NextDouble() - .5f) * .48f * envelope, 0f);
            }
            v.ChainArc.SetPositions(v.ArcPoints); v.ChainCore.SetPositions(v.ArcPoints);
        }
        private static bool Orbit(Visual v)
        {
            GameObject native = NativePerkAssetResolver.Resolve("staff_clusterbombstaff_splinter_projectile");
            Transform stone = native != null ? native.transform.Find("splinter") : null;
            Transform flame = native != null ? native.transform.Find("flames") : null;
            ParticleSystem stoneParticle = stone != null ? stone.GetComponent<ParticleSystem>() : null;
            ParticleSystemRenderer donor = stone != null ? stone.GetComponent<ParticleSystemRenderer>() : null;
            if (stoneParticle == null || donor == null || donor.renderMode != ParticleSystemRenderMode.Mesh ||
                donor.mesh == null || donor.sharedMaterial == null || !donor.sharedMaterial.HasProperty("_Color") ||
                flame == null || flame.childCount != 0 || flame.GetComponent<ParticleSystem>() == null) return false;
            foreach (Component component in flame.GetComponents<Component>())
                if (!(component is Transform) && !(component is ParticleSystem) && !(component is ParticleSystemRenderer)) return false;
            // Sphere is inactive in the native projectile. Copy the actual stone
            // mesh and native tint, not that sphere or an orange recolor.
            float size = stoneParticle.main.startSize.constant;
            var tint = new MaterialPropertyBlock(); tint.SetColor("_Color", stoneParticle.main.startColor.color);
            for (int i = 0; i < Ashlands70Rules.Shards; i++)
            {
                var piece = new GameObject("VM_NativeOrbitStone"); piece.transform.SetParent(v.Root.transform, false);
                piece.transform.localRotation = Quaternion.Euler(23f, 0f, 17f);
                var model = new GameObject("VM_NativeSplinterMesh"); model.transform.SetParent(piece.transform, false);
                model.transform.localScale = Vector3.one * size; model.transform.localPosition = -donor.mesh.bounds.center * size;
                model.AddComponent<MeshFilter>().sharedMesh = donor.mesh;
                var renderer = model.AddComponent<MeshRenderer>(); renderer.sharedMaterials = donor.sharedMaterials; renderer.SetPropertyBlock(tint);
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObject body = UnityEngine.Object.Instantiate(flame.gameObject, piece.transform, false); body.SetActive(false);
                body.transform.localPosition = Vector3.zero; body.transform.localRotation = Quaternion.identity; body.transform.localScale = Vector3.one;
                ParticleSystem ps = body.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false; main.loop = true; main.maxParticles = 8;
                main.simulationSpace = ParticleSystemSimulationSpace.Local; main.startLifetime = .4f; main.stopAction = ParticleSystemStopAction.None;
                var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 6f; emission.rateOverDistance = 0f;
                emission.SetBursts(new ParticleSystem.Burst[0]);
                var shape = ps.shape; shape.enabled = false; var collision = ps.collision; collision.enabled = false;
                var trigger = ps.trigger; trigger.enabled = false; var sub = ps.subEmitters; sub.enabled = false;
                body.SetActive(true); v.Pieces.Add(piece.transform); v.Fragments.Add(ps);
            }
            return true;
        }
        internal static Transform CopyMesh(MeshFilter mesh, MeshRenderer renderer, Transform parent)
        {
            var go = new GameObject("VM_NativeVisualMesh"); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            MeshRenderer copy = go.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); copy.SetPropertyBlock(block);
            for (int i = 0; i < copy.sharedMaterials.Length; i++) { block.Clear(); renderer.GetPropertyBlock(block, i); copy.SetPropertyBlock(block, i); }
            copy.shadowCastingMode = ShadowCastingMode.Off; copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off; copy.reflectionProbeUsage = ReflectionProbeUsage.Off; return go.transform;
        }
        internal static ParticleSystem RedZaps(Transform parent, int capacity)
        {
            GameObject native = NativePerkAssetResolver.Resolve("staff_lightning_projectile");
            Transform leaf = native != null ? native.transform.Find("zaps") : null;
            if (leaf == null || leaf.childCount != 0 || leaf.GetComponent<ParticleSystem>() == null) return null;
            // This exact scriptless leaf renders trails with renderMode=None.
            // Clone keeps trail streams, sheet, colors and material bindings intact.
            foreach (Component component in leaf.GetComponents<Component>())
                if (!(component is Transform) && !(component is ParticleSystem) && !(component is ParticleSystemRenderer)) return null;
            GameObject copy = UnityEngine.Object.Instantiate(leaf.gameObject, parent, false); copy.SetActive(false);
            copy.name = "VM_NativeDundrRedZaps"; copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity; copy.transform.localScale = Vector3.one;
            ParticleSystem ps = copy.GetComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.maxParticles = capacity; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = .2f; main.startLifetime = .3f; main.stopAction = ParticleSystemStopAction.None;
            var emission = ps.emission; emission.enabled = false; var shape = ps.shape; shape.enabled = false;
            // The donor attracts toward its moving projectile at radial -10.
            // Manual world-space chain/head samples have no projectile centre.
            var velocity = ps.velocityOverLifetime; velocity.enabled = false;
            var collision = ps.collision; collision.enabled = false; var trigger = ps.trigger; trigger.enabled = false;
            var sub = ps.subEmitters; sub.enabled = false; var trails = ps.trails; trails.dieWithParticles = true;
            ParticleSystemRenderer renderer = copy.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            copy.SetActive(true); ps.Play(); return ps;
        }
        internal static void Emit(ParticleSystem ps, Vector3 point, float size)
        {
            var emit = new ParticleSystem.EmitParams { position = point, velocity = UnityEngine.Random.onUnitSphere * .35f, startSize = size, startLifetime = .3f };
            ps.Emit(emit, 1);
        }
        private static void Clear()
        {
            foreach (Visual v in Spells.Values) if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
            foreach (Ashlands70Eyes eye in Eyes.Values) if (eye != null) UnityEngine.Object.Destroy(eye.gameObject);
            Spells.Clear(); Eyes.Clear(); FailedEyes.Clear(); Failed.Clear(); NextDiscover = 0f;
        }
        internal static void Reset() { Clear(); Ashlands70Audio.Reset(); Ashlands70Projectiles.Reset(); }
    }
}
