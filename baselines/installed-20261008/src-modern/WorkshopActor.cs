using UnityEngine;

namespace ValheimMastery
{
    // Dedicated servers need the authenticated character ZDO, not its rendered Player.
    internal sealed class WorkshopActor
    {
        internal readonly ZRpc Rpc;
        internal readonly ZDOID CharacterId;
        private readonly long _playerId;
        private readonly Player _local;
        private WorkshopActor(ZRpc rpc, ZDO zdo, Player local)
        { Rpc = rpc; CharacterId = zdo.m_uid; _playerId = zdo.GetLong(ZDOVars.s_playerID, 0); _local = local; }
        internal static WorkshopActor Resolve(ZRpc rpc)
        {
            if (ZNet.instance?.IsServer() != true) return null;
            Player local = rpc == null ? Player.m_localPlayer : null;
            ZDO zdo = rpc == null ? local?.m_nview?.GetZDO() : OwnerSkillAuthority.ResolveCharacterData(ZNet.instance.GetPeer(rpc));
            return zdo == null || zdo.GetLong(ZDOVars.s_playerID, 0) == 0 ? null : new WorkshopActor(rpc, zdo, local);
        }
        private ZDO Current => Rpc == null ? _local?.m_nview?.GetZDO() :
            OwnerSkillAuthority.ResolveCharacterData(ZNet.instance?.GetPeer(Rpc));
        internal bool Available => Current is ZDO zdo && zdo.m_uid == CharacterId && zdo.GetLong(ZDOVars.s_playerID, 0) == _playerId;
        internal Vector3 Position => Current?.GetPosition() ?? Vector3.zero;
        internal long GetPlayerID() => _playerId;
        internal bool IsDead() => !Available || (_local != null ? _local.IsDead() : Current.GetBool(ZDOVars.s_dead, false));
        internal bool IsTeleporting() => !Available || (_local != null ? _local.IsTeleporting() : Current.GetBool("vm.workshop.teleporting", false));
        internal bool HasCrafting70 => Available && (_local != null ? OwnerSkillAuthority.Has(_local, Skills.SkillType.Crafting, 70) :
            OwnerSkillAuthority.Has(Rpc, CharacterId, _playerId, Skills.SkillType.Crafting, 70));
        internal Vector3 EyePoint => _local != null ? _local.GetEyePoint() : Position + Vector3.up * 1.6f;
        internal float PlaceDistance => _local != null ? _local.m_maxPlaceDistance :
            ZNetScene.instance.GetPrefab(Current.GetPrefab()).GetComponent<Player>().m_maxPlaceDistance;
    }
}
