using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal static class Ashlands70Projectiles
    {
        private static GameObject Stage;
        internal static bool Fire(Player player, Character target, Ashlands70Grant grant, int slot)
        {
            GameObject prefab = NativePerkAssetResolver.Resolve("staff_clusterbombstaff_splinter_projectile");
            if (prefab == null || prefab.GetComponent<Projectile>() == null) { Magic70Diagnostics.Event("orbit-projectile", player, target, prefab == null ? "prefab-pending" : "no-native-projectile"); return false; }
            if (Stage == null) { Stage = new GameObject("VM_Ashlands70_ProjectileStage"); Stage.SetActive(false); }
            GameObject body = null;
            try
            {
            body = UnityEngine.Object.Instantiate(prefab, Stage.transform, false); body.SetActive(false);
            Projectile projectile = body.GetComponent<Projectile>();
            // A real native child, never the parent or another12-fragment cascade.
            projectile.m_bounce = false; projectile.m_gravity = 0f; projectile.m_spawnOnHit = null; projectile.m_randomSpawnOnHit = new List<GameObject>();
            projectile.m_spawnOnTtl = false; projectile.m_respawnItemOnHit = false;
            projectile.m_raiseSkillAmount = 0f; projectile.m_hitVariant = Ashlands70Rules.GeneratedVariant;
            body.transform.SetParent(null, false); body.transform.position = Ashlands70Presentation.OrbitPoint(player, slot);
            HitData hit = grant.Damage.Clone(); Ashlands70Cast.Generated(hit);
            Vector3 direction = (target.GetCenterPoint() - body.transform.position).normalized;
            projectile.Setup(player, direction * 24f, -1f, hit, null, null);
            projectile.m_originalHitData = hit; projectile.m_hitVariant = Ashlands70Rules.GeneratedVariant;
            projectile.m_raiseSkillAmount = 0f;
            body.SetActive(true); return true;
            }
            catch (Exception error)
            {
                if (body != null)
                {
                    ZNetView view = body.GetComponent<ZNetView>();
                    if (view?.IsValid() == true && view.IsOwner()) ZNetScene.instance.Destroy(body); else UnityEngine.Object.Destroy(body);
                }
                Magic70Diagnostics.Event("orbit-projectile", player, target, "setup-exception:" + error.GetType().Name);
                MasteryPlugin.Log.LogWarning("[Ashlands70] splinter spawn failed: " + error.GetType().Name); return false;
            }
        }
        internal static void Reset() { if (Stage != null) UnityEngine.Object.Destroy(Stage); Stage = null; }
    }
}
