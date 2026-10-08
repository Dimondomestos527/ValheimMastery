using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace ValheimMastery
{
    internal static class CrossbowTurretContainerGuards
    {
        internal static bool Locked(ZDO record) => CrossbowTurretCustody.Marked(record) &&
            (CrossbowTurretRules.LocksStock(CrossbowTurretCustody.State(record)) ||
                record.GetBool(CrossbowTurretCustody.QuarantineKey, false) ||
                CrossbowTurretHandoff.LocallyFrozen(record) || CrossbowTurretHandoff.IsPending(record));
        internal static bool AllowNativeRequest(Container chest, long sender, long playerId)
        {
            ZDO record = chest?.m_nview?.GetZDO();
            if (!CrossbowTurretCustody.Marked(record)) return true;
            if (Locked(record) || record.GetLong(CrossbowTurretCustody.OwnerKey, 0) != playerId || playerId == 0) return false;
            ZNet net = ZNet.instance;
            if (net == null) return false;
            if (!net.IsServer())
            {
                // Native return/supply custody belongs to the owner client until its next handoff.
                Player local = Player.m_localPlayer;
                return sender == ZNet.GetUID() && chest.m_nview.IsOwner() && local != null &&
                    local.GetPlayerID() == playerId && !local.IsDead() &&
                    (local.transform.position - record.GetPosition()).sqrMagnitude <= 36f;
            }
            ZDO character = sender == ZNet.GetUID() ? Player.m_localPlayer?.m_nview?.GetZDO() :
                OwnerSkillAuthority.ResolveCharacterData(net.GetPeer(sender));
            return character != null && character.GetLong(ZDOVars.s_playerID, 0) == playerId &&
                !character.GetBool(ZDOVars.s_dead, false) &&
                (character.GetPosition() - record.GetPosition()).sqrMagnitude <= 36f;
        }
    }
    [HarmonyPatch(typeof(Container), nameof(Container.CheckAccess))]
    internal static class CrossbowTurretContainerAccessPatch
    {
        private static void Postfix(Container __instance, long __0, ref bool __result)
        {
            ZDO record = __instance.m_nview?.GetZDO();
            if (CrossbowTurretCustody.Marked(record) && (CrossbowTurretContainerGuards.Locked(record) ||
                record.GetLong(CrossbowTurretCustody.OwnerKey, 0) != __0)) __result = false;
        }
    }
    [HarmonyPatch(typeof(Container), nameof(Container.Save))]
    internal static class CrossbowTurretContainerSavePatch
    {
        private static bool Prefix(Container __instance)
        {
            ZDO record = __instance.m_nview?.GetZDO();
            return !CrossbowTurretCustody.Marked(record) ||
                (__instance.m_nview.IsOwner() &&
                    (CrossbowTurretHandoff.SavingForHandoff(__instance) || !CrossbowTurretContainerGuards.Locked(record)));
        }
    }
    [HarmonyPatch]
    internal static class CrossbowTurretContainerRequestPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Container), "RPC_RequestOpen");
            yield return AccessTools.Method(typeof(Container), "RPC_RequestStack");
            yield return AccessTools.Method(typeof(Container), "RPC_RequestTakeAll");
        }
        private static bool Prefix(Container __instance, long __0, long __1)
            => CrossbowTurretContainerGuards.AllowNativeRequest(__instance, __0, __1);
    }
    [HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
    internal static class CrossbowTurretOwnerPinPatch
    {
        private static bool Prefix(ZDO __instance, long __0)
        {
            if (!CrossbowTurretCustody.Marked(__instance)) return true;
            ZNet net = ZNet.instance;
            long serverUid = net?.IsServer() == true ? ZNet.GetUID() : net?.GetServerPeer()?.m_uid ?? 0;
            if (serverUid == 0) return false;
            if (__0 == serverUid) return true;
            if (CrossbowTurretContainerGuards.Locked(__instance)) return false;
            long personalOwner = __instance.GetLong(CrossbowTurretCustody.OwnerKey, 0);
            return net.IsServer() ? OwnerSkillAuthority.ResolvePlayerId(net.GetPeer(__0)) == personalOwner :
                Player.m_localPlayer?.GetPlayerID() == personalOwner && __0 == ZNet.GetUID();
        }
    }
}
