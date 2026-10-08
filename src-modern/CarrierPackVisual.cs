using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Keep the native rig and rigid straw-hat mesh/materials. Native
    // VisEquipment.AttachItem parents a standalone visual with worldPositionStays
    // true, then zeroes local position/rotation; a scaled Draugr joint must not
    // multiply the item's scale a second time.
    internal sealed class CarrierPackVisual : MonoBehaviour
    {
        private float Next;
        private bool HatBuilt;
        private bool UnsafeHat;
        private void Update()
        {
            if (Time.time < Next) return;
            Next = Time.time + 1f;
            Character character = GetComponent<Character>();
            if (!Magic70Carrier.IsCarrier(character)) return;
            character.m_name = "Торба";
            Humanoid humanoid = character as Humanoid;
            if (humanoid != null && character.m_nview.IsOwner())
            {
                RemoveHand(humanoid, humanoid.GetRightItem());
                RemoveHand(humanoid, humanoid.GetLeftItem());
            }
            if (HatBuilt || UnsafeHat || Application.isBatchMode || Player.m_localPlayer == null) return;
            GameObject hat = ObjectDB.instance?.GetItemPrefab("HelmetStrawHat") ??
                NativeSoftVisualAssets.Get<GameObject>("Assets/GameElements/Items/helmets/HelmetStrawHat.prefab");
            Transform attach = hat != null ? hat.transform.Find("attach") : null;
            if (attach == null) return;
            Transform head = null;
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
                if (bone.name == "HelmetAttach") { head = bone; break; }
            if (head == null) return;

            // Cosmetic components only; preserve each source local transform
            // instead of flattening a nested hierarchy through lossyScale ratios.
            GameObject visual = new GameObject("VM_Torba_StrawHat");
            visual.SetActive(false);
            visual.transform.localScale = attach.localScale;
            visual.transform.SetParent(head, true);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            var nodes = new Dictionary<Transform, Transform> { [attach] = visual.transform };
            int copied = 0;
            foreach (MeshFilter filter in attach.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer original = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || original == null || !original.enabled || copied >= 4) continue;
                Transform node = CopyPath(filter.transform, attach, nodes);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                MeshRenderer renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = original.sharedMaterials;
                var block = new MaterialPropertyBlock();
                original.GetPropertyBlock(block); renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = original.shadowCastingMode;
                renderer.receiveShadows = original.receiveShadows;
                copied++;
            }
            if (copied == 0) { Destroy(visual); return; }
            visual.SetActive(true);
            foreach (MeshRenderer renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                Vector3 size = renderer.bounds.size;
                // Refuse obstructing geometry rather than silently replacing the
                // selected asset. This also leaves an exact renderer/scale clue.
                if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || !float.IsFinite(size.z) ||
                    Mathf.Max(size.x, Mathf.Max(size.y, size.z)) > 2f)
                {
                    UnsafeHat = true;
                    visual.SetActive(false);
                    MasteryPlugin.Log?.LogWarning("[TorbaHat] unsafe renderer=" + renderer.name +
                        " mesh=" + renderer.GetComponent<MeshFilter>().sharedMesh.name +
                        " bounds=" + size + " jointScale=" + head.lossyScale +
                        " hatScale=" + visual.transform.lossyScale + "; cosmetic omitted");
                    Destroy(visual);
                    return;
                }
            }
            HatBuilt = true;
            if (MasteryPlugin.Settings?.VerboseLogging.Value == true)
                MasteryPlugin.Log?.LogInfo("[TorbaHat] id=" + character.m_nview.GetZDO().m_uid +
                    " meshes=" + copied + " jointScale=" + head.lossyScale +
                    " hatScale=" + visual.transform.lossyScale);
        }
        private static Transform CopyPath(Transform source, Transform attach,
            Dictionary<Transform, Transform> nodes)
        {
            if (nodes.TryGetValue(source, out Transform existing)) return existing;
            Transform parent = CopyPath(source.parent, attach, nodes);
            Transform node = new GameObject(source.name).transform;
            node.SetParent(parent, false);
            node.localPosition = source.localPosition;
            node.localRotation = source.localRotation;
            node.localScale = source.localScale;
            nodes.Add(source, node);
            return node;
        }
        private static void RemoveHand(Humanoid humanoid, ItemDrop.ItemData item)
        {
            if (item == null) return;
            humanoid.UnequipItem(item, false);
            humanoid.GetInventory().RemoveItem(item);
        }
    }
}
