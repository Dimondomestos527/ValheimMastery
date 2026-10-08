using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class CrossbowTurretProjectile
    {
        internal const short Variant = 2070;
        internal static ItemDrop.ItemData FirstBolt(CrossbowTurretStock stock)
        {
            foreach (var item in stock.Inventory.GetAllItems())
                if (item != stock.Weapon && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && item.m_stack > 0) return item;
            return null;
        }
        private static GameObject Prefab(ItemDrop.ItemData weapon, ItemDrop.ItemData bolt)
            => bolt?.m_shared?.m_attack?.m_attackProjectile ?? weapon?.m_shared?.m_attack?.m_attackProjectile;
        internal static bool CanSpawn(CrossbowTurretStock stock)
        {
            GameObject prefab = Prefab(stock.Weapon, FirstBolt(stock));
            return prefab?.GetComponent<Projectile>() != null && ZNetScene.instance.GetPrefab(prefab.name) != null;
        }
        internal static Vector3 AimPoint(CrossbowTurretStock stock, Character target, Vector3 origin, float prediction)
        {
            var bolt = FirstBolt(stock);
            float speed = Mathf.Max(1f, stock.Weapon.m_shared.m_attack.m_projectileVel + bolt.m_shared.m_attack.m_projectileVel);
            float gravity = Mathf.Max(0f, Prefab(stock.Weapon, bolt).GetComponent<Projectile>().m_gravity);
            Vector3 point = target.GetCenterPoint();
            float flight = Vector3.Distance(origin, point) / speed;
            // Same native velocity lead, with the actual bolt's drop rather than Ballista-ammo assumptions.
            point += target.GetVelocity() * flight * Mathf.Clamp(prediction, 0f, 2f);
            flight = Vector3.Distance(origin, point) / speed;
            return point + Vector3.up * (.5f * gravity * flight * flight);
        }
        internal static void Restore(HitData hit)
        {
            if (hit == null || hit.m_variant != Variant) return;
            hit.m_skillRaiseAmount = 0f;
            var context = PerkRuntimeService.GetHitContext(hit);
            context.PerkId = "crossbows_70_turret"; context.SourceSkill = Skills.SkillType.Crossbows;
            context.SourcePlayerId = (hit.GetAttacker() as Player)?.GetPlayerID() ?? 0;
            context.IsPerkGenerated = true; context.GenerationDepth = 1;
            context.AllowSelfProc = context.AllowOtherPerkProc = context.AllowKnife70InitialProc = context.AllowShadowRecursion = false;
            context.IgnoreReflect = context.IgnoreExecution = context.IgnoreOverdrawPayload = true; context.XpMultiplier = 0f;
        }
        internal static void Fire(Player owner, ItemDrop.ItemData weapon, ItemDrop.ItemData bolt, Vector3 origin, Vector3 direction, float noise)
        {
            GameObject prefab = Prefab(weapon, bolt);
            Attack attack = weapon.m_shared.m_attack;
            float level = OwnerSkillAuthority.Level(owner, Skills.SkillType.Crossbows);
            float factor = CrossbowTurretRules.DamageFactor(level, Random.value);
            var hit = new HitData { m_variant = Variant, m_skill = Skills.SkillType.Crossbows, m_skillLevel = level,
                m_skillRaiseAmount = 0f, m_ranged = true, m_point = origin, m_dir = direction,
                m_pushForce = (weapon.m_shared.m_attackForce * attack.m_forceMultiplier + bolt.m_shared.m_attackForce),
                m_staggerMultiplier = attack.m_staggerMultiplier, m_backstabBonus = 1f,
                m_blockable = weapon.m_shared.m_blockable, m_dodgeable = weapon.m_shared.m_dodgeable,
                m_itemLevel = (short)weapon.m_quality, m_itemWorldLevel = (byte)weapon.m_worldLevel };
            hit.m_damage = weapon.GetDamage(); hit.m_damage.Add(bolt.GetDamage());
            hit.m_damage.Modify(factor * attack.m_damageMultiplier);
            hit.m_statusEffectHash = bolt.m_shared.m_attackStatusEffect != null ? bolt.m_shared.m_attackStatusEffect.NameHash() :
                weapon.m_shared.m_attackStatusEffect != null ? weapon.m_shared.m_attackStatusEffect.NameHash() : 0;
            hit.SetAttacker(owner); Restore(hit);
            GameObject instance = Object.Instantiate(prefab, origin, Quaternion.LookRotation(direction));
            Projectile projectile = instance.GetComponent<Projectile>();
            float speed = Mathf.Max(1f, attack.m_projectileVel + bolt.m_shared.m_attack.m_projectileVel);
            // Attribution remains the player; the first ray must originate at the turret, not the distant player.
            projectile.m_doOwnerRaytest = false;
            projectile.Setup(owner, direction * speed, noise, hit, weapon, bolt);
            projectile.m_startPoint = origin; projectile.m_haveStartPoint = false;
            projectile.m_hitVariant = Variant; projectile.m_raiseSkillAmount = 0f;
            projectile.m_hitOwner = projectile.m_hitFriendly = false; projectile.m_noDamageFriendly = true;
            projectile.m_respawnItemOnHit = false; projectile.m_spawnItem = null;
            // Server owns this native network projectile, not the remote player's simulation.
            projectile.m_nview?.GetZDO()?.SetOwner(ZNet.GetUID());
        }
    }
    [HarmonyPatch(typeof(Projectile), "IsValidTarget")]
    internal static class CrossbowTurretProjectileTargetPatch
    {
        private static void Postfix(Projectile __instance, IDestructible __0, ref bool __result)
        {
            if (__instance.m_hitVariant != CrossbowTurretProjectile.Variant || !(__0 is Character target)) return;
            __result &= CrossbowTurretRuntime.Enemy(__instance.m_owner as Player, target);
        }
    }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    [HarmonyPriority(Priority.First)]
    internal static class CrossbowTurretGeneratedHitPatch
    {
        private static bool Prefix(Character __instance, HitData __1)
        {
            if (__1?.m_variant != CrossbowTurretProjectile.Variant) return true;
            CrossbowTurretProjectile.Restore(__1);
            return CrossbowTurretRuntime.Enemy(__1.GetAttacker() as Player, __instance);
        }
    }
}
