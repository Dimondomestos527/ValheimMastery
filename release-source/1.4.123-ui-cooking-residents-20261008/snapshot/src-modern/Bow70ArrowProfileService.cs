using UnityEngine;

namespace ValheimMastery
{
    /// <summary>
    /// Fired-projectile-only mastery. Never changes the ammo stack, ItemDrop prefab,
    /// ObjectDB template, or shared Projectile prefab. A missing native damage kind
    /// stays missing; this service does not synthesize elemental payload attacks.
    /// </summary>
    internal static class Bow70ArrowProfileService
    {
        internal static string Apply(Player owner, Projectile projectile, ItemDrop.ItemData firedAmmo)
        {
            if (owner == null || projectile == null) return "none";
            // Projectile.Setup receives the ammo used by this exact shot. GetAmmoItem()
            // can already point to a different stack (or null) after the last arrow is spent.
            ArrowClass arrow = MasteryClassificationService.GetArrowClass(firedAmmo);
            if (arrow == ArrowClass.Other)
                arrow = MasteryClassificationService.GetArrowClass(owner.GetAmmoItem());
            if (arrow == ArrowClass.Other)
                arrow = MasteryClassificationService.GetArrowClass(projectile.gameObject.name);
            HitData.DamageTypes damage = projectile.m_damage;
            string profile = arrow.ToString();
            switch (arrow)
            {
                case ArrowClass.Wood:
                    projectile.m_attackForce *= 1.15f;
                    break;
                case ArrowClass.Flint:
                    if (damage.m_pierce > 0f) damage.m_pierce *= 1.15f;
                    break;
                case ArrowClass.Bronze:
                    projectile.m_attackForce *= 1.20f;
                    break;
                case ArrowClass.Iron:
                    // No fabricated pickaxe explosion or unverified tool damage.
                    projectile.m_attackForce *= 1.25f;
                    if (damage.m_pierce > 0f) damage.m_pierce *= 1.10f;
                    break;
                case ArrowClass.Obsidian:
                    if (damage.m_pierce > 0f) damage.m_pierce *= 1.20f;
                    break;
                case ArrowClass.Fire:
                    if (damage.m_fire > 0f) damage.m_fire *= 1.35f;
                    break;
                case ArrowClass.Frost:
                    if (damage.m_frost > 0f) damage.m_frost *= 1.35f;
                    break;
                case ArrowClass.Poison:
                    if (damage.m_poison > 0f) damage.m_poison *= 1.35f;
                    break;
                case ArrowClass.Silver:
                    if (damage.m_spirit > 0f) damage.m_spirit *= 1.35f;
                    break;
                case ArrowClass.Needle:
                    if (damage.m_pierce > 0f) damage.m_pierce *= 1.15f;
                    projectile.m_vel *= 1.10f;
                    break;
                case ArrowClass.Carapace:
                    projectile.m_attackForce *= 1.25f;
                    break;
                case ArrowClass.Charred:
                    // Preserve the installed game's actual damage identity.
                    if (damage.m_pierce > 0f) damage.m_pierce *= 1.12f;
                    if (damage.m_fire > 0f) damage.m_fire *= 1.12f;
                    if (damage.m_spirit > 0f) damage.m_spirit *= 1.12f;
                    break;
                default:
                    profile = "other/native";
                    break;
            }
            // Overdraw70Service applies the first 1.25x charge bonus before this
            // profile. Add another 25% to that complete charged-shot payload.
            damage.Modify(1.25f);
            projectile.m_damage = damage;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Bows70] Arrow-native profile=" + profile + " velocity=" + projectile.m_vel.magnitude.ToString("0.##") + " force=" + projectile.m_attackForce.ToString("0.##"));
            return profile;
        }
    }
}
