using UnityEngine;
namespace ValheimMastery
{
    // Cosmetic vanilla Ruby mesh only: never instantiate its item/pickup prefab.
    internal sealed class Ashlands35ChargeVisual : MonoBehaviour
    {
        private const string RubyPath = "Assets/GameElements/Items/valuables/Ruby.prefab";
        private readonly Transform[] Gems = new Transform[Ashlands35VisualRules.RubyCapacity];
        private Vector3 ShaftStart, ShaftEnd;
        private float Angle;
        private int Visible = -1;
        private bool Ending;
        private float NativeSize;
        private Vector3 NativeScale;

        internal static Ashlands35ChargeVisual Create(Transform weapon)
        {
            if (weapon == null || Application.isBatchMode || Player.m_localPlayer == null ||
                !MasteryPlugin.Settings.EnablePerkProcVFX.Value || !ResolveShaft(weapon, out Vector3 start, out Vector3 end)) return null;
            GameObject native = ObjectDB.instance?.GetItemPrefab("Ruby") ?? NativeSoftVisualAssets.Get<GameObject>(RubyPath);
            Transform model = native != null ? native.transform.Find("attach/model") : null;
            MeshFilter source = model != null ? model.GetComponent<MeshFilter>() : null;
            MeshRenderer renderer = model != null ? model.GetComponent<MeshRenderer>() : null;
            if (source?.sharedMesh == null || renderer?.sharedMaterial == null) return null;
            Vector3 scale = model.localScale;
            Vector3 extent = Vector3.Scale(source.sharedMesh.bounds.size, scale);
            float size = Mathf.Max(Mathf.Abs(extent.x), Mathf.Max(Mathf.Abs(extent.y), Mathf.Abs(extent.z)));
            if (!float.IsFinite(size) || size <= .0001f) return null;
            var root = new GameObject("VM_Ashlands35Cluster_RubyOrbit");
            root.SetActive(false); root.layer = weapon.gameObject.layer;
            root.transform.SetParent(weapon, false);
            var visual = root.AddComponent<Ashlands35ChargeVisual>();
            visual.ShaftStart = start; visual.ShaftEnd = end;
            visual.NativeSize = size; visual.NativeScale = scale;
            for (int i = 0; i < visual.Gems.Length; i++)
            {
                var pivot = new GameObject("VM_RubyOrbitPivot"); pivot.layer = root.layer;
                pivot.transform.SetParent(root.transform, false);
                var gem = new GameObject("VM_RubyModel"); gem.layer = root.layer;
                gem.transform.SetParent(pivot.transform, false);
                gem.transform.localRotation = model.localRotation;
                gem.transform.localScale = scale;
                // Native gem mesh is not centered around its origin.
                gem.transform.localPosition = -(model.localRotation * Vector3.Scale(source.sharedMesh.bounds.center, scale));
                gem.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                var copy = gem.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block); copy.SetPropertyBlock(block);
                block.Clear(); renderer.GetPropertyBlock(block, 0); copy.SetPropertyBlock(block, 0);
                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.receiveShadows = false; copy.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                copy.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                visual.Gems[i] = pivot.transform; pivot.SetActive(false);
            }
            root.SetActive(true); return visual;
        }

        private static bool ResolveShaft(Transform weapon, out Vector3 start, out Vector3 end)
        {
            start = end = Vector3.zero;
            MeshFilter[] meshes = weapon.GetComponentsInChildren<MeshFilter>(true);
            Vector3 axis = Vector3.zero, center = Vector3.zero;
            float longest = .01f;
            // Read the longest native mesh direction after its child rotation and
            // scale. Never assume the equipped staff points along world/local up.
            foreach (MeshFilter mesh in meshes)
            {
                if (mesh.sharedMesh == null || mesh.name.StartsWith("VM_", System.StringComparison.Ordinal)) continue;
                Bounds bounds = mesh.sharedMesh.bounds;
                Vector3 localAxis = bounds.extents.y >= bounds.extents.x && bounds.extents.y >= bounds.extents.z ? Vector3.up :
                    bounds.extents.x >= bounds.extents.z ? Vector3.right : Vector3.forward;
                float extent = Vector3.Dot(bounds.extents, localAxis);
                Vector3 a = weapon.InverseTransformPoint(mesh.transform.TransformPoint(bounds.center - localAxis * extent));
                Vector3 b = weapon.InverseTransformPoint(mesh.transform.TransformPoint(bounds.center + localAxis * extent));
                Vector3 span = b - a;
                if (span.sqrMagnitude <= longest) continue;
                longest = span.sqrMagnitude; axis = span.normalized; center = (a + b) * .5f;
            }
            if (axis == Vector3.zero) return false;
            float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
            foreach (MeshFilter mesh in meshes)
            {
                if (mesh.sharedMesh == null || mesh.name.StartsWith("VM_", System.StringComparison.Ordinal)) continue;
                Bounds bounds = mesh.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    Vector3 point = weapon.InverseTransformPoint(mesh.transform.TransformPoint(local));
                    float distance = Vector3.Dot(point - center, axis);
                    minimum = Mathf.Min(minimum, distance); maximum = Mathf.Max(maximum, distance);
                }
            }
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || maximum - minimum < .1f) return false;
            start = center + axis * minimum; end = center + axis * maximum;
            // A named native core gives the direction of energy flow. Otherwise
            // the farther end from the hand attachment is the existing tip fallback.
            Vector3 head = start.sqrMagnitude > end.sqrMagnitude ? start : end;
            foreach (Transform child in weapon.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith("VM_", System.StringComparison.Ordinal)) continue;
                string name = child.name.ToLowerInvariant();
                if (!name.Contains("surtling") && !name.Contains("core") && !name.Contains("gem")) continue;
                head = weapon.InverseTransformPoint(child.position); break;
            }
            if ((head - start).sqrMagnitude < (head - end).sqrMagnitude)
            { Vector3 swap = start; start = end; end = swap; }
            return true;
        }

        internal void Draw(float held)
        {
            if (Ending) return;
            bool enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            int count = enabled ? Ashlands35VisualRules.Rubies(held) : 0;
            if (count != Visible)
            {
                for (int i = 0; i < Gems.Length; i++) Gems[i].gameObject.SetActive(i < count);
                Visible = count;
            }
            if (count == 0) return;
            // Integrate speed rather than time*speed: a charging change cannot jump the orbit.
            Angle = Mathf.Repeat(Angle + Time.deltaTime * Ashlands35VisualRules.OrbitSpeed(held), Mathf.PI * 2f);
            Vector3 axis = (ShaftEnd - ShaftStart).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < .9f ? Vector3.up : Vector3.right;
            Vector3 side = Vector3.Cross(axis, reference).normalized, normal = Vector3.Cross(axis, side);
            float radius = Ashlands35VisualRules.Radius(held);
            float scale = Ashlands35VisualRules.RubySize(held) / NativeSize;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.39996f + ((i & 1) == 0 ? Angle : -Angle);
                Gems[i].localPosition = Vector3.Lerp(ShaftStart, ShaftEnd, Ashlands35VisualRules.Along(i)) +
                    (side * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius;
                Gems[i].localRotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis);
                Gems[i].localScale = Vector3.one * scale;
            }
        }
        internal void Finish()
        {
            if (Ending) return;
            Ending = true; gameObject.SetActive(false); Destroy(gameObject);
        }
    }
}

