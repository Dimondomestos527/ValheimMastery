using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class BloodSkeleton35Service
    {
        private const float SummonInterval = 45f;
        private static Player Owner;
        private static int SceneEpoch;
        private static float NextSummon;
        private static float NextCatchUp;
        private static bool WarnedMissingSpawner;
        private static float PendingUntil;
        private static int PendingCount;
        private static SpawnAbility PendingSpawner;
        private static float NextMarker;

        private static void ResetFor(Player player)
        {
            int epoch = ZNetScene.instance != null ? ZNetScene.instance.GetInstanceID() : 0;
            if (Owner == player && SceneEpoch == epoch) return;
            Owner = player;
            SceneEpoch = epoch;
            NextSummon = Time.time + SummonInterval;
            NextCatchUp = Time.time + 2f;
            WarnedMissingSpawner = false;
            PendingSpawner = null;
            NextMarker = 0f;
        }

        private static void CatchUpLoadedFollowers(Player player)
        {
            if (Time.time < NextCatchUp) return;
            NextCatchUp = Time.time + 2f;
            if (player.InLiquidSwimDepth()) return;
            Vector3 behind = player.transform.position - player.transform.forward * 2.3f;
            int groundMask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            int moved = 0;
            foreach (Character creature in Character.GetAllCharacters())
            {
                if (creature == null || creature.IsDead() ||
                    !creature.name.StartsWith("Skeleton", StringComparison.Ordinal) ||
                    (creature.transform.position - player.transform.position).sqrMagnitude < 35f * 35f)
                    continue;
                MonsterAI ai = creature.GetComponent<MonsterAI>();
                ZNetView view = creature.m_nview;
                ZSyncTransform sync = creature.GetComponent<ZSyncTransform>();
                if (ai?.GetFollowTarget() != player.gameObject || view == null || !view.IsValid() ||
                    !view.IsOwner() || creature.m_body == null || sync == null) continue;
                Vector3 candidate = behind + player.transform.right * (moved % 3 - 1) * 1.1f;
                if (!Physics.Raycast(candidate + Vector3.up * 3f, Vector3.down,
                    out RaycastHit ground, 8f, groundMask) || ground.normal.y < .65f) continue;
                Vector3 destination = ground.point + Vector3.up * .08f;
                // Move the existing network-owned creature, never a duplicate summon.
                // ZSyncTransform writes the new position to its ZDO for other peers.
                creature.m_body.linearVelocity = Vector3.zero;
                creature.m_body.position = destination;
                creature.transform.position = destination;
                sync.SyncNow();
                moved++;
            }
        }

        private static ItemDrop.ItemData GetHotbarStaff(Player player)
        {
            if (player?.GetInventory()?.GetAllItems() == null) return null;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                if (item?.m_gridPos.y == 0 && item.m_gridPos.x >= 0 && item.m_gridPos.x < 8 &&
                    item.m_dropPrefab != null && item.m_dropPrefab.name == "StaffSkeleton") return item;
            return null;
        }

        private static SpawnAbility GetSpawner(ItemDrop.ItemData staff)
        {
            Attack attack = staff?.m_shared?.m_attack;
            // Skeleton staff uses the projectile launch path, not spawnOnTrigger.
            return attack?.m_attackProjectile?.GetComponent<SpawnAbility>() ??
                attack?.m_spawnOnTrigger?.GetComponent<SpawnAbility>();
        }

        private static void ShowCooldown(Player player, float seconds)
        {
            if (player.m_seman == null || Time.time < NextMarker) return;
            NextMarker = Time.time + 1f;
            const string name = "Поклик кісток";
            StatusEffect active = player.m_seman.GetStatusEffect(name.GetStableHashCode());
            if (active != null) { active.m_ttl = active.m_time + Mathf.Max(.1f, seconds); return; }
            SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>();
            marker.name = name; marker.m_name = name;
            marker.m_tooltip = "Безкоштовний виклик скелета відновлюється. Посох має бути в хотбарі; потрібен вільний слот.";
            marker.m_icon = GetHotbarStaff(player)?.GetIcon() ?? player.m_textIcon;
            marker.m_ttl = Mathf.Max(.1f, seconds); marker.m_flashIcon = false;
            player.m_seman.AddStatusEffect(marker, false, 0, 0f, 0);
        }

        private static int Limit(SpawnAbility spawner, ItemDrop.ItemData staff, float skill)
            => Limit(spawner, staff, skill, staff != null ? staff.m_quality : 1);

        private static int Limit(SpawnAbility spawner, ItemDrop.ItemData staff, float skill, int quality)
        {
            // The staff's own SpawnAbility drives the ordinary skeleton count; do not
            // invent a separate cap that would replace one of the player's live minions.
            int limit = spawner.m_maxSpawned;
            if (spawner.m_levelUpSettings != null)
                foreach (SpawnAbility.LevelUpSettings setting in spawner.m_levelUpSettings)
                    if (setting != null && skill >= setting.m_skillLevel && setting.m_maxSpawns > 0)
                        limit = spawner.m_setMaxInstancesFromWeaponLevel
                            ? Mathf.Max(1, quality) : setting.m_maxSpawns;
            return limit > 0 ? limit : 0;
        }

        // Server admission for the carrier consumes a real StaffSkeleton slot.
        // Use the owner's synchronized right-hand quality and only the minimum
        // verified mastery level (70), so this check cannot overstate capacity.
        internal static bool CanAdmitCarrier(long author, int synchronizedQuality, int canonicalOwnedCount)
        {
            if (author == 0 || canonicalOwnedCount < 0 || ObjectDB.instance == null) return false;
            ItemDrop.ItemData staff = ObjectDB.instance.GetItemPrefab("StaffSkeleton")?.GetComponent<ItemDrop>()?.m_itemData;
            SpawnAbility spawner = GetSpawner(staff);
            if (staff?.m_shared == null || spawner == null) return false;
            int quality = Mathf.Clamp(synchronizedQuality, 1, Mathf.Max(1, staff.m_shared.m_maxQuality));
            int limit = Limit(spawner, staff, 70f, quality);
            return limit > 0 && canonicalOwnedCount < limit;
        }

        private static int OwnedCount(Player player, SpawnAbility spawner)
        {
            int count = 0;
            foreach (Character creature in Character.GetAllCharacters())
            {
                if (creature == null || creature.IsDead()) continue;
                MonsterAI ai = creature.GetComponent<MonsterAI>();
                if (ai?.GetFollowTarget() != player.gameObject) continue;
                foreach (GameObject prefab in spawner.m_spawnPrefab)
                    if (prefab != null && creature.name.StartsWith(prefab.name, StringComparison.Ordinal))
                    { count++; break; }
            }
            return count + Magic70Carrier.OwnedSlotCount(player);
        }

        internal static void Update(Player player)
        {
            if (player != Player.m_localPlayer) return;
            ResetFor(player);
            Skeleton35Travel.Tick(player);
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.BloodMagic, 35)) return;
            // Travel is handled once by Skeleton35Travel, including portal recovery.
            ItemDrop.ItemData staff = GetHotbarStaff(player);
            if (staff == null) { NextSummon = Time.time + SummonInterval; return; }
            if (PendingSpawner != null)
            {
                if (OwnedCount(player, PendingSpawner) > PendingCount)
                {
                    PerkVisualService.PlayProc(player, "bloodmagic_35", player.GetCenterPoint(), true, false);
                    PendingSpawner = null;
                }
                else if (Time.time >= PendingUntil) PendingSpawner = null;
            }
            if (NextSummon > Time.time) ShowCooldown(player, NextSummon - Time.time);
            // The destination may temporarily contain zero loaded skeletons.
            // Do not mint a free one while the departure roster is in flight.
            if (Skeleton35Travel.PortalRecoveryPending) return;
            if (Time.time < NextSummon) return;
            // Do not spin every frame if a full slot or an unresolved prefab blocks casting.
            NextSummon = Time.time + SummonInterval;
            SpawnAbility spawner = GetSpawner(staff);
            if (spawner?.m_spawnPrefab == null || spawner.m_spawnPrefab.Length == 0)
            {
                if (!WarnedMissingSpawner && MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogWarning("[Blood35] StaffSkeleton has no native SpawnAbility trigger; auto summon skipped.");
                WarnedMissingSpawner = true;
                return;
            }
            float skill = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.BloodMagic);
            int limit = Limit(spawner, staff, skill);
            if (limit <= 0 || OwnedCount(player, spawner) >= limit) return;

            // Exactly the native attack spawn path: IProjectile.Setup starts SpawnAbility's
            // coroutine, command/follow setup and normal summon FX, without player animation
            // or eitr expenditure. Native spawner still applies its own count checks.
            GameObject instance = UnityEngine.Object.Instantiate(spawner.gameObject,
                player.transform.position + player.transform.forward, Quaternion.identity);
            IProjectile ability = instance != null ? instance.GetComponent<IProjectile>() : null;
            SpawnAbility autoSpawner = instance != null ? instance.GetComponent<SpawnAbility>() : null;
            if (ability == null || autoSpawner == null)
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                return;
            }
            // The native staff can roll a variable count. The mastery grant is
            // precisely ONE free skeleton, without altering the shared prefab.
            autoSpawner.m_minToSpawn = 1;
            autoSpawner.m_maxToSpawn = 2; // Unity int Random.Range uses an exclusive upper bound.
            autoSpawner.m_commandOnSpawn = true;
            PendingCount = OwnedCount(player, spawner);
            PendingSpawner = spawner; PendingUntil = Time.time + 8f;
            ability.Setup(player, player.transform.forward, -1f, null, staff, null);
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class BloodSkeleton35UpdatePatch
    {
        private static void Postfix(Player __instance) => BloodSkeleton35Service.Update(__instance);
    }
}
