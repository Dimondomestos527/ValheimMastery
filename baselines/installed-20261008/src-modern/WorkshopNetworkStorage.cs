using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    // Eligibility only. This class must never make HaveRequirements true or remove
    // resources until the server transaction protocol can commit the action.
    internal static class WorkshopNetworkStorage
    {
        private static float _nextScan;
        private static ZNet _session;
        private static Container[] _frameContainers;
        internal static List<Container> FindServerOwnedEligible(Player player, Vector3 actionPoint)
            => FindEligible(player, actionPoint, true);
        internal static List<Container> FindServerOwnedEligible(WorkshopActor player, Vector3 actionPoint)
            => FindEligible(player, actionPoint, true);
        internal static List<Container> FindEligible(WorkshopActor player, Vector3 actionPoint, bool serverOwned)
        {
            if (ZNet.instance?.IsServer() != true || player == null || !player.HasCrafting70 || player.IsDead() || player.IsTeleporting())
                return new List<Container>();
            return FindEligibleCore(player.GetPlayerID(), actionPoint, serverOwned);
        }
        internal static List<Container> FindEligible(Player player, Vector3 actionPoint, bool serverOwned, bool preview = false)
        {
            List<Container> result = new List<Container>();
            if (ZNet.instance == null || player == null) return result;
            bool authority = ZNet.instance.IsServer();
            if (authority ? !OwnerSkillAuthority.Has(player, Skills.SkillType.Crafting, 70) :
                serverOwned || !WorkshopRemoteCraft.Enabled || player != Player.m_localPlayer ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70)) return result;
            return FindEligibleCore(player.GetPlayerID(), actionPoint, serverOwned, preview);
        }
        private static List<Container> FindEligibleCore(long playerId, Vector3 actionPoint, bool serverOwned, bool preview = false)
        {
            var result = new List<Container>();
            int component = WorkshopNetwork.CoveredComponent(actionPoint);
            if (component < 0) return result;
            if (_session != ZNet.instance || Time.time >= _nextScan || _frameContainers == null)
            {
                _session = ZNet.instance; _nextScan = Time.time + 0.5f;
                _frameContainers = UnityEngine.Object.FindObjectsOfType<Container>();
            }
            foreach (Container container in _frameContainers)
            {
                if (container == null || container.m_piece == null ||
                    container.GetComponentInParent<Vagon>() != null ||
                    container.m_nview == null || !container.m_nview.IsValid() ||
                    (serverOwned && !container.m_nview.IsOwner()) || container.IsInUse() || WorkshopAtomicDebit.IsLocked(container.GetInventory()) ||
                    !IsPlayerChest(container) || !(preview ? CanObserve(container, playerId) : container.CheckAccess(playerId)) ||
                    !WardAllows(playerId, container.transform.position) ||
                    WorkshopNetwork.CoveredComponent(container.transform.position) != component) continue;
                result.Add(container);
            }
            result.Sort((a, b) =>
            {
                float da = (a.transform.position - actionPoint).sqrMagnitude;
                float db = (b.transform.position - actionPoint).sqrMagnitude;
                int distance = da.CompareTo(db);
                if (distance != 0) return distance;
                ZDO az = a.m_nview.GetZDO(), bz = b.m_nview.GetZDO();
                return string.CompareOrdinal(az?.m_uid.ToString(), bz?.m_uid.ToString());
            });
            return result;
        }

        internal static bool IsPlayerChest(Container container)
        {
            // Installed weapon/ammunition belongs to the turret, including supply/return custody.
            if (CrossbowTurretCustody.Marked(container.m_nview?.GetZDO())) return false;
            // Explicit vanilla chest family; no carts, ships or scripted loot.
            string name = container.gameObject.name;
            return name.StartsWith("piece_chest", StringComparison.OrdinalIgnoreCase);
        }
        private static bool CanObserve(Container chest, long playerId)
        {
            // UI privacy policy excludes transient freeze/escrow gates. Final
            // debit still uses ordinary access checks and current-byte CAS.
            ZDO record = chest.m_nview.GetZDO();
            if (record == null || record.GetBool(WorkshopChestLease.QuarantineKey, false)) return false;
            return chest.m_privacy == Container.PrivacySetting.Public ||
                (chest.m_privacy == Container.PrivacySetting.Private && record.GetLong(ZDOVars.s_creator, 0) == playerId);
        }

        internal static bool WardAllows(long playerId, Vector3 point)
        {
            // PrivateArea.CheckAccess uses Player.m_localPlayer and is invalid on a
            // dedicated server. Mirror its all-overlapping-wards policy with the
            // authenticated peer's player ID instead.
            if (PrivateArea.m_allAreas == null) return true;
            foreach (PrivateArea ward in PrivateArea.m_allAreas)
            {
                if (ward == null || !ward.IsEnabled() || !ward.IsInside(point, 0f)) continue;
                if (ward.m_piece == null ||
                    (ward.m_piece.GetCreator() != playerId && !ward.IsPermitted(playerId))) return false;
            }
            return true;
        }
    }
}
