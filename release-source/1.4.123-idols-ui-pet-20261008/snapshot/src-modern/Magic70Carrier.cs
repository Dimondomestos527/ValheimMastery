using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Shared identity and prefab foundation for the server-owned Torbjorn roster
    // command. Cargo is deliberately a separate native Container ZDO; the carrier
    // ZDO stores only CargoKey, so roster serialization can preserve the link.
    internal static partial class Magic70Carrier
    {
        internal const string PrefabName = "VM_Torbjorn70";
        internal const string AuthorKey = "vm.skeleton.master";
        internal const string CargoKey = "vm.torbjorn.cargo";
        internal const string CargoPrefabName = "VM_TorbjornCargo70";
        internal const string CargoAuthorKey = "vm.torbjorn.cargo.author";
        internal const string CargoCarrierKey = "vm.torbjorn.cargo.carrier";
        internal const string ActiveCarrierKey = "vm.torbjorn.active";
        internal const string BirthKey = "vm.torbjorn.birth";

        // These are native VisEquipment ZDO slots (confirmed in the installed
        // assembly). Read only the current left/right identities; never trust a
        // cached/backpack item or a client-supplied staff name/quality.
        internal static bool HasEquippedStaff(ZDO character, int prefabHash) => character != null &&
            (character.GetInt(ZDOVars.s_rightItem, 0) == prefabHash || character.GetInt(ZDOVars.s_leftItem, 0) == prefabHash);
        internal static int GetEquippedStaffQuality(ZDO character, int prefabHash)
        {
            if (character == null) return 0;
            if (character.GetInt(ZDOVars.s_rightItem, 0) == prefabHash) return character.GetInt(ZDOVars.s_rightItemQuality, 0);
            if (character.GetInt(ZDOVars.s_leftItem, 0) == prefabHash) return character.GetInt(ZDOVars.s_leftItemQuality, 0);
            return 0;
        }

        internal static void RegisterPrefab(ZNetScene scene)
        {
            if (scene == null) return;
            RegisterCarrier(scene);
            RegisterCargo(scene);
        }

        private static void RegisterCarrier(ZNetScene scene)
        {
            int hash = PrefabName.GetStableHashCode();
            if (scene.m_namedPrefabs.ContainsKey(hash)) return;
            GameObject native = scene.GetPrefab("Draugr");
            if (native == null) return;
            GameObject staging = new GameObject("VM_InactiveTorbjornTemplate");
            staging.SetActive(false);
            GameObject prefab = UnityEngine.Object.Instantiate(native, staging.transform, false);
            prefab.name = PrefabName;
            Character character = prefab.GetComponent<Character>();
            if (character == null) { UnityEngine.Object.Destroy(staging); return; }
            character.m_name = "Торба";
            Humanoid humanoid = prefab.GetComponent<Humanoid>();
            if (humanoid != null)
            {
                humanoid.m_defaultItems = Array.Empty<GameObject>();
                humanoid.m_randomWeapon = Array.Empty<GameObject>();
                humanoid.m_randomShield = Array.Empty<GameObject>();
                humanoid.m_randomSets = Array.Empty<Humanoid.ItemSet>();
                humanoid.m_randomItems = Array.Empty<Humanoid.RandomItem>();
            }
            character.m_faction = Character.Faction.Players;
            character.m_health *= CarrierSurvivalRules.HealthMultiplier;
            CharacterDrop drops = prefab.GetComponent<CharacterDrop>();
            if (drops != null)
            {
                drops.m_dropsEnabled = false;
                drops.m_drops.Clear();
            }
            Tameable tame = prefab.GetComponent<Tameable>() ?? prefab.AddComponent<Tameable>();
            tame.m_startsTamed = true;
            tame.m_commandable = true;
            tame.m_unsummonDistance = 0f;
            tame.m_unsummonOnOwnerLogoutSeconds = 0f;
            ZNetView view = prefab.GetComponent<ZNetView>();
            if (view != null) view.m_persistent = true;
            MonsterAI ai = prefab.GetComponent<MonsterAI>();
            if (ai != null)
            {
                // Native flee branch stays active at full HP and after being hurt.
                // It preserves native sleep, sensing, navigation and follow fallback.
                ai.m_fleeIfLowHealth = 1.01f;
                ai.m_fleeTimeSinceHurt = float.MaxValue;
                ai.m_fleeRange = CarrierSurvivalRules.FleeRange;
                ai.m_viewRange = CarrierSurvivalRules.ThreatRadius;
                ai.m_hearRange = CarrierSurvivalRules.ThreatRadius;
                ai.m_attackPlayerObjects = false;
                ai.m_enableHuntPlayer = false;
                // Native owner-relative tether must not erase a nearby threat before flee.
                // CarrierSurvival bounds acquisition/retention to 12m/18m instead.
                ai.m_alertRange = float.MaxValue;
                ai.m_wakeupRange = 0f;
                ai.m_noiseWakeup = false;
                ai.m_maxNoiseWakeupRange = 0f;
                ai.m_consumeRange = 0f;
                ai.m_consumeSearchRange = 0f;
            }
            prefab.AddComponent<Magic70CarrierMarker>();
            prefab.AddComponent<CarrierSurvival>();
            prefab.AddComponent<CarrierPackVisual>();
            prefab.AddComponent<CarrierBirthVisual>();
            scene.m_namedPrefabs.Add(hash, prefab);
            scene.m_prefabs.Add(prefab);
        }

        private static void RegisterCargo(ZNetScene scene)
        {
            int hash = CargoPrefabName.GetStableHashCode();
            if (scene.m_namedPrefabs.ContainsKey(hash)) return;
            GameObject native = scene.GetPrefab("piece_chest_wood");
            if (native == null) return;
            GameObject staging = new GameObject("VM_InactiveTorbjornCargoTemplate");
            staging.SetActive(false);
            GameObject prefab = UnityEngine.Object.Instantiate(native, staging.transform, false);
            prefab.name = CargoPrefabName;
            ZNetView view = prefab.GetComponent<ZNetView>();
            Container container = prefab.GetComponent<Container>();
            if (view == null || container == null)
            {
                UnityEngine.Object.Destroy(staging);
                return;
            }
            view.m_persistent = true;
            container.m_name = "Шлунок Торби";
            container.m_privacy = Container.PrivacySetting.Private;
            container.m_checkGuardStone = false;
            container.m_autoDestroyEmpty = false;
            WearNTear wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                // Native IL shows these true values enable damage when roof/support
                // is absent. The hidden floating proxy must not accumulate either.
                wear.m_noRoofWear = false;
                wear.m_noSupportWear = false;
                wear.m_burnable = false;
                wear.m_snowDamageImmune = true;
                wear.m_ashDamageImmune = true;
            }
            foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in prefab.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            // Keep the native Piece component: native Container ownership/open RPCs
            // rely on its component graph. Harmony guards below block removal/damage.
            prefab.AddComponent<Magic70CarrierCargoMarker>();
            scene.m_namedPrefabs.Add(hash, prefab);
            scene.m_prefabs.Add(prefab);
        }

        internal static bool IsCarrier(Character character)
        {
            return character != null && character.m_nview?.IsValid() == true &&
                character.m_nview.GetZDO().GetPrefab() == PrefabName.GetStableHashCode();
        }

        // One living, server-authored Torbjorn consumes one ordinary StaffSkeleton
        // slot, regardless of whether the native Draugr follows through MonsterAI.
        internal static int OwnedSlotCount(Player player)
        {
            if (player == null) return 0;
            long author = player.GetPlayerID();
            if (author == 0) return 0;
            ZDO playerData = player.m_nview?.IsValid() == true ? player.m_nview.GetZDO() : null;
            ZDOID activeId = playerData?.GetZDOID(ActiveCarrierKey) ?? ZDOID.None;
            ZDO active = activeId.IsNone() ? null : ZDOMan.instance?.GetZDO(activeId);
            if (active != null && active.GetPrefab() == PrefabName.GetStableHashCode() &&
                active.GetLong(AuthorKey, 0L) == author && active.GetFloat("health", 1f) > 0f)
                return 1;
            foreach (Character character in Character.GetAllCharacters())
                if (character != null && !character.IsDead() && IsCarrier(character) &&
                    character.m_nview.GetZDO().GetLong(AuthorKey, 0L) == author)
                    return 1;
            return 0;
        }

        // The same vanilla portal restriction applies to every cargo insertion
        // path, independent of the world's portal override.
        internal static bool IsAllowedCargo(ItemDrop.ItemData item)
        {
            return item?.m_shared != null && !item.m_shared.m_questItem && item.m_shared.m_teleportable;
        }

        internal static void RecordOwnerLink(Player player, ZDOID carrierId)
        {
            if (player?.m_nview?.IsValid() == true && player.m_nview.IsOwner() && !carrierId.IsNone())
                player.m_nview.GetZDO()?.Set(ActiveCarrierKey, carrierId);
        }
    }

    internal sealed class Magic70CarrierMarker : MonoBehaviour { }
    internal sealed class Magic70CarrierCargoMarker : MonoBehaviour { }

    // Native ragdoll creation can generate drops even when OnDeath drops are
    // disabled. Guard the shared generation path, not just the prefab flag.
    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class Magic70CarrierLootPatch
    {
        private static bool Prefix(CharacterDrop __instance,
            ref System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GameObject, int>> __result)
        {
            if (__instance == null || __instance.GetComponent<Magic70CarrierMarker>() == null) return true;
            __result = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<GameObject, int>>();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Magic70CarrierPrefabPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            // Do not register an unfinished, publicly accessible cargo prefab in
            // the normal build. Enable only after access/insertion gates exist.
            Magic70Carrier.RegisterPrefab(__instance);
#endif
        }
    }
}


