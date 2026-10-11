using System;
using UnityEngine;
namespace ValheimMastery
{
    internal static class Ashlands35Presentation
    {
        private const string Rpc = "VM_Ashlands35FxV1";
        private static float NextCharge;
        internal static void ClusterCharge(Vector3 point, int step)
        {
            if (Time.unscaledTime < NextCharge) return; NextCharge = Time.unscaledTime + .45f;
            GameObject native = NativePerkAssetResolver.Resolve(step == 6 ? "fx_clusterbombstaff_hit" : "fx_clusterbombstaff_splinter_hit");
            PerkAudioService.PlayPrefab("ashlands35_cluster_charge", native, point, .4f, .25f, .65f + .06f * step);
        }
        internal static void Register(Character body)
        {
            if (body?.m_nview?.IsValid() != true) return;
            if (!(body is Player) && !Ashlands35Summons.Prefab(body, Ashlands35Summons.TrollName) &&
                !Ashlands35Summons.Prefab(body, Ashlands35Summons.RootName)) return;
            body.m_nview.Register<ZPackage>(Rpc, (sender, data) => Receive(body, sender, data));
        }
        internal static void Summon(Character body, int kind, Vector3 point) => Send(body, kind, point, point, point, 0);
        internal static void Arc(Player caster, Vector3 a, Vector3 b, Vector3 c, int count) => Send(caster, 3, a, b, c, count);
        private static void Send(Character body, int kind, Vector3 a, Vector3 b, Vector3 c, int count)
        {
            if (body?.m_nview?.IsValid() != true || !body.m_nview.IsOwner() || Ashlands35Summons.World == 0) return;
            var data = new ZPackage(); data.Write(Ashlands35Summons.World); data.Write(kind); data.Write(a); data.Write(b); data.Write(c); data.Write(count);
            body.m_nview.InvokeRPC(ZNetView.Everybody, Rpc, data);
        }
        private static void TrollExplosion(Vector3 point)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            GameObject prefab = NativePerkAssetResolver.Resolve("vfx_troll_summoned_prespawn");
            if (prefab == null) { NativeVfxSafeFrame.Request("vfx_troll_summoned_prespawn", loaded => TrollExplosionResolved(loaded, point)); return; }
            TrollExplosionResolved(prefab, point);
        }
        private static bool TrollExplosionResolved(GameObject prefab, Vector3 point)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value || Player.m_localPlayer == null) return true;
            GameObject root = PerkNativeFeedback.CreateVisualOnly(prefab);
            if (root == null) return false;
            root.transform.position = point; root.SetActive(true);
            NativeVfxSafeFrame.AfterReady(root, () =>
            {
                if (root == null) return;
                Transform explosion = null;
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Explosion") { explosion = child; break; }
                if (explosion == null) { UnityEngine.Object.Destroy(root); return; }
                // Native birth uses these six systems as death subemitters of a
                // 2.5-second prespawn timer. Replay the explosion immediately,
                // on an unpooled sanitized clone, without its summon timer.
                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var sub = ps.subEmitters; sub.enabled = false;
                    var main = ps.main; main.loop = false; main.playOnAwake = false;
                    if (ps.transform.IsChildOf(explosion) || ps.transform == explosion)
                    { main.startDelay = 0f; ps.Play(false); }
                }
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (!renderer.transform.IsChildOf(explosion) && renderer.transform != explosion) renderer.enabled = false;
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                    if (!light.transform.IsChildOf(explosion) && light.transform != explosion) light.enabled = false;
                UnityEngine.Object.Destroy(root, 5f);
            });
            return true;
        }
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        private static void Receive(Character body, long sender, ZPackage data)
        {
            if (Player.m_localPlayer == null || Application.isBatchMode || !MasteryPlugin.Settings.Enabled.Value ||
                body?.m_nview?.IsValid() != true || body.m_nview.GetZDO().GetOwner() != sender || data == null || data.Size() > 80) return;
            try
            {
                data.SetPos(0); long world = data.ReadLong(); int kind = data.ReadInt(); Vector3 a = data.ReadVector3(), b = data.ReadVector3(), c = data.ReadVector3(); int count = data.ReadInt();
                if (data.GetPos() != data.Size() || world != Ashlands35Summons.World || !Finite(a) || !Finite(b) || !Finite(c) ||
                    (Player.m_localPlayer.transform.position - a).sqrMagnitude > 80f * 80f) return;
                if (kind == 1 && Ashlands35Summons.Prefab(body, Ashlands35Summons.TrollName))
                {
                    TrollExplosion(a);
                    PerkAudioService.Play("ashlands35_troll_death", "sfx_staff_trollstav_troll_impact", a, .15f);
                    PerkAudioService.Play("ashlands35_troll_roar", "sfx_trollfire_roar", a, .15f);
                }
                else if (kind == 2 && Ashlands35Summons.Prefab(body, Ashlands35Summons.RootName))
                {
                    SpawnAbility ability = NativePerkAssetResolver.Resolve("staff_greenroots_spawn")?.GetComponent<SpawnAbility>();
                    foreach (var effect in ability?.m_spawnEffects?.m_effectPrefabs ?? Array.Empty<EffectList.EffectData>())
                    {
                        if (!effect.m_enabled || effect.m_prefab == null) continue;
                        PerkNativeFeedback.PlayVfx(effect.m_prefab.name, a, 1f, 2f);
                        PerkAudioService.PlayPrefab("ashlands35_root_regrow", effect.m_prefab, a, .15f);
                    }
                }
                else if (kind == 3 && body is Player && count >= 1 && count <= 2 &&
                    (body.transform.position - a).sqrMagnitude <= 210f * 210f && (b-a).sqrMagnitude <= 25.01f && (count < 2 || (c-a).sqrMagnitude <= 25.01f))
                {
                    PerkAudioService.Play("ashlands35_dundr_arc", "sfx_staff_lightning_fire", a, .12f, .5f);
                }
            }
            catch (Exception) { /* Cosmetic malformed packet never changes gameplay. */ }
        }
    }
}
