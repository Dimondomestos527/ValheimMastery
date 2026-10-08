using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class AutoReplantRequest
    {
        internal Player Player;
        internal string HarvestedName;
        internal Pickable Source;
        internal float ExpiresAt;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal float ReadyAt;
    }

    internal static class FarmingPerkService
    {
        private const string MasterPlantKey = "valheim_mastery.farming70.masterplant";
        private static readonly List<AutoReplantRequest> Pending = new List<AutoReplantRequest>();

        internal static void CaptureHarvest(Pickable pickable, Player player, bool cultivated, bool clusterHarvest)
        {
            if (clusterHarvest || !cultivated || pickable == null || player == null ||
                player != Player.m_localPlayer || !pickable.CanBePicked() || pickable.m_pickedLocal ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Farming, 35) || !HasCultivatorOnHotbar(player)) return;
            Pending.Add(new AutoReplantRequest
            {
                Player = player,
                HarvestedName = pickable.gameObject.name.Replace("(Clone)", string.Empty),
                Source = pickable,
                ExpiresAt = Time.time + 5f,
                Position = pickable.transform.position,
                Rotation = pickable.transform.rotation,
                ReadyAt = Time.time + 0.20f
            });
        }

        internal static void Tick()
        {
            for (int i = Pending.Count - 1; i >= 0; --i)
            {
                AutoReplantRequest request = Pending[i];
                if (request == null || request.Player == null) { Pending.RemoveAt(i); continue; }
                if (Time.time < request.ReadyAt) continue;
                if (request.Source != null && !request.Source.m_picked)
                {
                    if (Time.time >= request.ExpiresAt) Pending.RemoveAt(i);
                    continue;
                }
                Pending.RemoveAt(i);
                TryReplant(request);
            }
        }

        internal static void HarvestWildCluster(Pickable source, Player player, float radius, int maximum)
        {
            if (source == null || player == null || maximum <= 0) return;
            string sourceName = source.gameObject.name.Replace("(Clone)", string.Empty);
            int harvested = 0;
            foreach (Pickable candidate in UnityEngine.Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None))
            {
                if (candidate == null || candidate == source || candidate.m_pickRaiseSkill == Skills.SkillType.Farming ||
                    !candidate.CanBePicked() || Vector3.Distance(source.transform.position, candidate.transform.position) > radius ||
                    !string.Equals(candidate.gameObject.name.Replace("(Clone)", string.Empty), sourceName, StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate.Interact(player, false, false) && ++harvested >= maximum) break;
            }
        }

        private static void TryReplant(AutoReplantRequest request)
        {
            Player player = request.Player;
            if (player == null || player != Player.m_localPlayer || player.IsDead() || !HasCultivatorOnHotbar(player)) return;
            Piece piece = FindPlantPiece(player, request.HarvestedName);
            if (piece == null || !player.HaveRequirements(piece, Player.RequirementMode.CanBuild)) return;

            player.PlacePiece(piece, request.Position, request.Rotation, false, false);
            if (!player.NoCostCheat()) player.ConsumeResources(piece.m_resources, 0, -1, 1);
        }

        internal static bool HasCultivatorOnHotbar(Player player)
        {
            if (player?.GetInventory()?.GetAllItems() == null) return false;
            foreach (ItemDrop.ItemData tool in player.GetInventory().GetAllItems())
                if (tool?.m_gridPos.y == 0 && tool.m_gridPos.x >= 0 && tool.m_gridPos.x < 8 &&
                    tool.m_shared?.m_buildPieces != null && tool.m_shared.m_buildPieces.m_skill == Skills.SkillType.Farming)
                    return true;
            return false;
        }

        private static Piece FindPlantPiece(Player player, string harvestedName)
        {
            PieceTable table = null;
            if (player?.GetInventory()?.GetAllItems() != null)
                foreach (ItemDrop.ItemData tool in player.GetInventory().GetAllItems())
                    if (tool?.m_gridPos.y == 0 && tool.m_gridPos.x >= 0 && tool.m_gridPos.x < 8 &&
                        tool.m_shared?.m_buildPieces?.m_skill == Skills.SkillType.Farming)
                    { table = tool.m_shared.m_buildPieces; break; }
            if (table?.m_pieces == null || string.IsNullOrEmpty(harvestedName)) return null;
            foreach (GameObject prefab in table.m_pieces)
            {
                Plant plant = prefab != null ? prefab.GetComponent<Plant>() : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (plant?.m_grownPrefabs == null || piece == null) continue;
                foreach (GameObject grown in plant.m_grownPrefabs)
                    if (grown != null && string.Equals(grown.name.Replace("(Clone)", string.Empty), harvestedName, StringComparison.OrdinalIgnoreCase))
                        return piece;
            }
            return null;
        }

        internal static void MarkMasterPlant(Piece piece)
        {
            Plant plant = piece != null ? piece.GetComponent<Plant>() : null;
            if (plant == null || piece.m_nview?.GetZDO() == null) return;
            Player creator = Player.GetPlayer(piece.GetCreator());
            if (PerkRuntimeService.HasPerk(creator, Skills.SkillType.Farming, 70))
                piece.m_nview.GetZDO().Set(MasterPlantKey, true);
        }

        internal static bool IsMasterPlant(Plant plant) => plant?.m_nview?.GetZDO()?.GetBool(MasterPlantKey, false) == true;

        internal static bool HasNearbyFarmingMaster(Tameable tameable)
        {
            if (tameable == null) return false;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null && PerkRuntimeService.HasPerk(player, Skills.SkillType.Farming, 70) &&
                    Vector3.Distance(player.transform.position, tameable.transform.position) <= Tameable.m_playerMaxDistance)
                    return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.OnPlaced))]
    internal static class Farming70MasterPlantRecordPatch
    {
        private static void Postfix(Piece __instance) => FarmingPerkService.MarkMasterPlant(__instance);
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.HaveGrowSpace))]
    internal static class Farming70GrowSpacePatch
    {
        private static void Postfix(Plant __instance, ref bool __result)
        {
            if (FarmingPerkService.IsMasterPlant(__instance)) __result = true;
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.HaveRoof))]
    internal static class Farming70RoofPatch
    {
        private static void Postfix(Plant __instance, ref bool __result)
        {
            if (FarmingPerkService.IsMasterPlant(__instance)) __result = false;
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.DecreaseRemainingTime))]
    internal static class Farming70TamingSpeedPatch
    {
        private static void Prefix(Tameable __instance, ref float __0)
        {
            if (__0 > 0f && FarmingPerkService.HasNearbyFarmingMaster(__instance)) __0 *= 1.5f;
        }
    }

    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character) })]
    internal static class Farming70PartialTameFearPatch
    {
        private static void Postfix(BaseAI __instance, Character __0, ref bool __result)
        {
            Player player = __0 as Player;
            Tameable tameable = __instance?.m_tamable;
            if (!__result || player == null || tameable == null || tameable.IsTamed() || tameable.GetTameness() <= 0 ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Farming, 70)) return;
            if (Vector3.Distance(player.transform.position, tameable.transform.position) > 2f) __result = false;
        }
    }
}
