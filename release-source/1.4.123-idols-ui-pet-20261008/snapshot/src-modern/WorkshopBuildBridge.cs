using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Normal hammer UI -> asynchronous server debit -> vanilla placement.
    // Preview is not credit. TryPlacePiece is intercepted before any object exists.
    internal static class WorkshopBuildBridge
    {
        [ThreadStatic] internal static int PlacementDepth;
        internal static bool ClientEligible(Player player, Piece piece)
        {
            if (!WorkshopRemoteCraft.Ready || ZNet.instance == null ||
                player == null || player != Player.m_localPlayer || player.IsDead() || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 70) || piece == null ||
                !piece.m_enabled || piece.m_repairPiece || piece.m_removePiece || player.NoCostCheat() ||
                player.m_noPlacementCost || ZoneSystem.instance?.GetGlobalKey(piece.FreeBuildKey()) == true ||
                player.m_buildPieces == null || !player.m_buildPieces.m_pieces.Contains(piece.gameObject) ||
                player.GetRightItem()?.m_shared?.m_buildPieces != player.m_buildPieces) return false;
            // Preserve vanilla DLC and station checks; never use chest stock to
            // unlock unavailable content or bypass required station capability.
            if (!string.IsNullOrEmpty(piece.m_dlc) && DLCMan.instance?.IsDLCInstalled(piece.m_dlc) != true) return false;
            return piece.m_craftingStation == null ||
                WorkshopNetwork.FindCapability(piece.m_craftingStation.m_name, player.transform.position, 1) != null;
        }
        internal static bool ServerEligible(WorkshopActor player, Piece piece, Vector3 point)
        {
            if (player == null || piece == null || !piece.m_enabled || piece.m_repairPiece || piece.m_removePiece ||
                !Finite(point.x) || !Finite(point.y) || !Finite(point.z) ||
                Vector3.Distance(player.EyePoint, point) > player.PlaceDistance + 1f ||
                ZoneSystem.instance?.GetGlobalKey(piece.FreeBuildKey()) == true ||
                !WorkshopWorldRecords.WardAllows(player.GetPlayerID(), point)) return false;
            int component = WorkshopWorldRecords.Covered(player.Position);
            return component >= 0 && WorkshopWorldRecords.Covered(point) == component &&
                (piece.m_craftingStation == null || WorkshopWorldRecords.HasCapability(piece.m_craftingStation.m_name, point, 1));
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Preview(Player player, Piece piece)
        {
            if (!ClientEligible(player, piece)) return false;
            return WorkshopStoragePreview.CanPay(player, piece.m_resources, 0, 1);
        }
        internal static bool BeforePlace(Player player, Piece piece)
        {
            if (WorkshopRemoteCraft.HasBuildCredit(player, piece)) return true;
            if (!ClientEligible(player, piece)) return true;
            if (WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding) return false;
            if (WorkshopResourcePlan.TryBuild(player.GetInventory(), new List<Container>(), piece.m_resources, 0, out _)) return true;
            WorkshopRemoteCraft.BeginBuild(player, piece);
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    internal static class WorkshopBuildRequirementsPatch
    {
        private static void Postfix(Player __instance, Piece piece, Player.RequirementMode mode, ref bool __result)
        {
            if (!__result && (int)mode == 0)
                __result = WorkshopRemoteCraft.HasBuildCredit(__instance, piece) ||
                    (WorkshopBuildBridge.PlacementDepth > 0 && (WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding) &&
                     WorkshopBuildBridge.ClientEligible(__instance, piece) &&
                     WorkshopRemoteCraft.TryResourceCosts(piece.m_resources, 0, 1, out _)) || WorkshopBuildBridge.Preview(__instance, piece);
        }
    }
    // The native click checks stock before TryPlacePiece. Pass only supported
    // local workshop input to the async bridge. Its false placement result still
    // prevents native consumption, stamina, durability and recent-piece effects.
    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class WorkshopBuildAdmissionScopePatch
    {
        private static void Prefix(Player __instance, out bool __state)
        { __state = __instance == Player.m_localPlayer; if (__state) WorkshopBuildBridge.PlacementDepth++; }
        private static Exception Finalizer(Exception __exception, bool __state)
        { if (__state) WorkshopBuildBridge.PlacementDepth--; return __exception; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class WorkshopBuildRequestPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (WorkshopBuildBridge.BeforePlace(__instance, piece)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class WorkshopBuildAuthorizedPositionPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance, Piece piece, Vector3 pos, Quaternion rot) =>
            WorkshopRemoteCraft.GuardBuildPlacement(__instance, piece, pos, rot);
    }
}
