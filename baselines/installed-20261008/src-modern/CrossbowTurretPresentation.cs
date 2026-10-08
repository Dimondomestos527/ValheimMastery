using UnityEngine;

namespace ValheimMastery
{
    // Renderer-only native composition. Never instantiate a Turret controller for decoration.
    internal sealed class CrossbowTurretPresentation : MonoBehaviour
    {
        private Container Chest;
        private GameObject Model, WeaponRoot;
        private Transform Body;
        private Renderer[] Original;
        private bool[] OriginalEnabled;
        private float NextRefresh;
        private int Shot, Revision = -1;
        private byte[] Cargo;
        private bool Initialized;
        private string WeaponPrefab;
        private CrossbowTurretState LastState;
        private GameObject Loaded, Unloaded;
        private Turret Definition;
        private void Awake() { Chest = GetComponent<Container>(); }
        private void Update()
        {
            if (Application.isBatchMode || Chest == null) return;
            ZDO record = Chest.m_nview?.GetZDO(); if (!CrossbowTurretCustody.Marked(record)) return;
            // Smooth cosmetics every rendered frame; keep cargo/effect polling bounded.
            if (Body != null)
            {
                Vector3 aim = record.GetVec3(CrossbowTurretRuntime.AimKey, transform.forward);
                if (aim.sqrMagnitude > .01f)
                    Body.rotation = Quaternion.Slerp(Body.rotation, Quaternion.LookRotation(aim),
                        1f - Mathf.Exp(-12f * Time.deltaTime));
            }
            if (Time.time < NextRefresh) return;
            NextRefresh = Time.time + .1f;
            if (Definition == null) Definition = ZNetScene.instance?.GetPrefab("piece_turret")?.GetComponent<Turret>();
            if (Model == null && Definition != null) BuildBase();
            int currentShot = record.GetInt(CrossbowTurretRuntime.ShotKey, 0);
            CrossbowTurretState state = CrossbowTurretCustody.State(record);
            if (!Initialized) { Shot = currentShot; LastState = state; Initialized = true; }
            else
            {
                if (currentShot != Shot)
                {
                    Shot = currentShot;
                    Cue(Definition?.m_shootEffect, transform.position + Vector3.up * 1.35f + record.GetVec3(CrossbowTurretRuntime.AimKey, transform.forward) * 1.15f);
                }
                if (state != LastState)
                {
                    if (state == CrossbowTurretState.Active) Cue(Definition?.m_addAmmoEffect, transform.position + Vector3.up);
                    else if (LastState == CrossbowTurretState.Active) Cue(Definition?.m_setTargetEffect, transform.position);
                    LastState = state;
                }
            }
            int revision = record.GetInt(CrossbowTurretCustody.RevisionKey, 0);
            byte[] bytes = record.GetByteArray(ZDOVars.s_items, null);
            // Aim updates also bump DataRevision; only decode actual cargo changes.
            if (Revision != revision || !System.Linq.Enumerable.SequenceEqual(Cargo ?? System.Array.Empty<byte>(), bytes ?? System.Array.Empty<byte>()))
            {
                Revision = revision; Cargo = bytes == null ? null : (byte[])bytes.Clone();
                if (CrossbowTurretStock.TryRead(bytes, Chest, out var stock, out _))
                {
                    string name = stock.Weapon.m_dropPrefab.name;
                    if (WeaponRoot == null || name != WeaponPrefab) BuildWeapon(stock.Weapon);
                    bool ready = stock.Bolts > 0 && state == CrossbowTurretState.Active;
                    Loaded?.SetActive(ready); Unloaded?.SetActive(!ready);
                }
                else if (state != CrossbowTurretState.Active && WeaponRoot != null)
                { Destroy(WeaponRoot); WeaponRoot = null; Loaded = Unloaded = null; WeaponPrefab = null; }
            }
        }
        private void BuildBase()
        {
            Original = GetComponentsInChildren<Renderer>(true); OriginalEnabled = new bool[Original.Length];
            for (int i = 0; i < Original.Length; i++) { OriginalEnabled[i] = Original[i].enabled; Original[i].enabled = false; }
            Model = new GameObject("Mastery_FieldTurret_NativeBase"); Model.transform.SetParent(transform, false);
            Model.transform.localRotation = Definition.transform.localRotation; Model.transform.localScale = Vector3.one * .65f;
            Transform source = Definition.transform.Find("New/Base"); int budget = 128;
            if (source != null) CloneMeshes(source, Model.transform, ref budget);
            source = Definition.transform.Find("New/NeckRotation");
            if (source != null) CloneMeshes(source, Model.transform, ref budget);
            var body = new GameObject("InstalledCrossbowPivot"); Body = body.transform; Body.SetParent(Model.transform, false);
            Transform donorBody = Definition.m_turretBody?.transform;
            Body.localPosition = donorBody != null ? donorBody.localPosition : Vector3.up * 1.7444f;
            Revision = -1; // Retry weapon composition if the native base became available late.
        }
        private void BuildWeapon(ItemDrop.ItemData weapon)
        {
            if (Body == null) return;
            Transform attach = weapon.m_dropPrefab.transform.Find("attach");
            if (attach == null) return;
            if (WeaponRoot != null) Destroy(WeaponRoot);
            WeaponRoot = new GameObject("Installed_" + weapon.m_dropPrefab.name); WeaponRoot.transform.SetParent(Body, false);
            WeaponRoot.transform.localPosition = new Vector3(0f, .15f, .6f);
            // Native Loaded/Unloaded mesh/child transforms already orient the weapon along +Z.
            // A second yaw180 made the visible crossbow face opposite the server's Aim.
            WeaponRoot.transform.localRotation = Quaternion.identity; WeaponRoot.transform.localScale = Vector3.one * 1.5f;
            int budget = 128;
            Transform loaded = attach.Find("Loaded"), unloaded = attach.Find("Unloaded");
            Loaded = loaded != null ? CloneMeshes(loaded, WeaponRoot.transform, ref budget) : null;
            Unloaded = unloaded != null ? CloneMeshes(unloaded, WeaponRoot.transform, ref budget) : null;
            WeaponPrefab = weapon.m_dropPrefab.name;
        }
        private static GameObject CloneMeshes(Transform source, Transform parent, ref int budget)
        {
            if (budget-- <= 0) return null;
            GameObject clone = new GameObject(source.name); clone.layer = parent.gameObject.layer;
            Transform node = clone.transform; node.SetParent(parent, false);
            node.localPosition = source.localPosition; node.localRotation = source.localRotation; node.localScale = source.localScale;
            MeshFilter mesh = source.GetComponent<MeshFilter>(); MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            if (mesh != null && renderer != null)
            {
                clone.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                MeshRenderer copy = clone.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
                copy.shadowCastingMode = renderer.shadowCastingMode; copy.receiveShadows = renderer.receiveShadows;
            }
            foreach (Transform child in source)
            {
                // Only static mesh transform trees; never attach scripts/particles/colliders/audio/lights.
                if (child.name == "Collider" || child.name == "UpgraderGlow" || child.GetComponent<ParticleSystem>() != null || child.GetComponent<Light>() != null) continue;
                CloneMeshes(child, node, ref budget);
            }
            return clone;
        }
        private static void Cue(EffectList list, Vector3 position)
        {
            if (Player.m_localPlayer == null || list?.m_effectPrefabs == null) return;
            int budget = 4;
            foreach (var effect in list.m_effectPrefabs)
            {
                if (budget-- <= 0) break;
                GameObject prefab = effect.m_prefab; if (!effect.m_enabled || prefab == null) continue;
                if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
                {
                    GameObject instance = VfxPool.Spawn(prefab, position, Quaternion.identity, 2f);
                    if (instance != null)
                    {
                        instance.transform.localScale *= .65f; instance.SetActive(true);
                        NativeVfxSafeFrame.AfterReady(instance, () => instance.GetComponent<VfxPoolBaseline>()?.RestartParticles());
                    }
                }
                PerkAudioService.PlayPrefab("crossbow_turret_native", prefab, position, .5f, .8f, 1f);
            }
        }
        private void OnDestroy()
        {
            if (Original != null) for (int i = 0; i < Original.Length; i++) if (Original[i] != null) Original[i].enabled = OriginalEnabled[i];
            if (Model != null) Destroy(Model);
        }
    }
}
