using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Native authority adapter only. Direct ZRpc authentication, never routed
    // payload sender. Replicated hand/life state uses native owner-trust model.
    internal static class Combat100NativeObservations
    {
        internal sealed class MusterActor
        {
            internal ZNet Session;
            internal long World, PlayerId, Owner;
            internal ZDOID CharacterId;
            internal int HeldPrefab;
            internal Vector3 Position;
            internal string Character => CharacterId.ToString();
        }
        internal static bool TryMusterActor(ZRpc rpc, long expectedWorld, ZDOID expectedCharacter, int expectedHeld, out MusterActor actor)
        {
            actor = null;
            var net = ZNet.instance;
            if (net == null || !net.IsServer() || net.GetWorld() == null || expectedWorld != net.GetWorldUID() || rpc == null) return false;
            var peer = net.GetPeer(rpc);
            var zdo = OwnerSkillAuthority.ResolveCharacterData(peer);
            if (zdo == null || zdo.m_uid != expectedCharacter || expectedHeld == 0 || zdo.GetInt(ZDOVars.s_rightItem, 0) != expectedHeld ||
                !AliveFriendly(zdo) || !HeldPolearms(expectedHeld)) return false;
            long player = zdo.GetLong(ZDOVars.s_playerID, 0);
            if (player == 0 || !OwnerSkillAuthority.Has(rpc, zdo.m_uid, player, Skills.SkillType.Polearms, 100)) return false;
            Vector3 position = zdo.GetPosition();
            if (!Finite(position) || !ReferenceEquals(net, ZNet.instance) || !ReferenceEquals(peer, net.GetPeer(rpc))) return false;
            actor = new MusterActor { Session = net, World = expectedWorld, PlayerId = player, Owner = peer.m_uid,
                CharacterId = zdo.m_uid, HeldPrefab = expectedHeld, Position = position };
            return true;
        }
        internal static bool TryLocalMusterActor(out MusterActor actor)
        {
            actor = null; var net = ZNet.instance; var player = Player.m_localPlayer;
            if (net == null || !net.IsServer() || net.GetWorld() == null || player?.m_nview?.IsOwner() != true ||
                player.IsDead() || player.IsPVPEnabled() || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Polearms, 100)) return false;
            var zdo = player.m_nview.GetZDO(); var weapon = player.GetCurrentWeapon();
            if (zdo == null || weapon == null || !ReferenceEquals(weapon, player.GetRightItem()) || !weapon.IsWeapon() ||
                weapon.m_shared.m_skillType != Skills.SkillType.Polearms || !AliveFriendly(zdo)) return false;
            int held = zdo.GetInt(ZDOVars.s_rightItem, 0); Vector3 position = zdo.GetPosition();
            if (!HeldPolearms(held) || !Finite(position) || zdo.GetOwner() == 0 || zdo.GetLong(ZDOVars.s_playerID, 0) != player.GetPlayerID()) return false;
            actor = new MusterActor { Session = net, World = net.GetWorldUID(), PlayerId = player.GetPlayerID(), Owner = zdo.GetOwner(),
                CharacterId = zdo.m_uid, HeldPrefab = held, Position = position };
            return true;
        }
        internal static bool SnapshotMuster(MusterActor caster, out Combat100ActionJournal.Recipient[] roster)
        {
            roster = null;
            if (caster == null || !ReferenceEquals(caster.Session, ZNet.instance) || !caster.Session.IsServer() ||
                caster.Session.GetWorld() == null || caster.World != caster.Session.GetWorldUID() || !Finite(caster.Position)) return false;
            var seen = new Dictionary<long, Combat100ActionJournal.Recipient>();
            bool Add(ZDO zdo)
            {
                if (zdo == null || !AliveFriendly(zdo)) return true;
                Vector3 position = zdo.GetPosition();
                if (!Finite(position) || (position - caster.Position).sqrMagnitude > 35f * 35f) return true;
                long id = zdo.GetLong(ZDOVars.s_playerID, 0); long owner = zdo.GetOwner();
                if (id == 0 || owner == 0 || zdo.m_uid == ZDOID.None) return true;
                var recipient = new Combat100ActionJournal.Recipient { PlayerId = id, Owner = owner, Character = zdo.m_uid.ToString() };
                if (seen.TryGetValue(id, out var existing)) return existing.Owner == owner && existing.Character == recipient.Character;
                if (seen.Count >= 64) return false; // No silently truncated paid roster.
                seen.Add(id, recipient); return true;
            }
            foreach (var peer in caster.Session.GetPeers())
                if (!Add(OwnerSkillAuthority.ResolveCharacterData(peer))) return false;
            var local = Player.m_localPlayer;
            if (local?.m_nview?.IsOwner() == true && !Add(local.m_nview.GetZDO())) return false;
            if (!seen.TryGetValue(caster.PlayerId, out var self) || self.Owner != caster.Owner || self.Character != caster.Character) return false;
            if (!ReferenceEquals(caster.Session, ZNet.instance) || caster.World != caster.Session.GetWorldUID()) return false;
            var ordered = new List<Combat100ActionJournal.Recipient>(seen.Values);
            ordered.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId)); roster = ordered.ToArray(); return true;
        }
        private static bool AliveFriendly(ZDO zdo) => !zdo.GetBool(ZDOVars.s_dead, false) &&
            !zdo.GetBool(ZDOVars.s_pvp, false) && float.IsFinite(zdo.GetFloat(ZDOVars.s_health, float.NaN)) &&
            zdo.GetFloat(ZDOVars.s_health, float.NaN) > 0f;
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        private static bool HeldPolearms(int hash)
        {
            if (hash == 0) return false;
            var item = ZNetScene.instance?.GetPrefab(hash)?.GetComponent<ItemDrop>()?.m_itemData;
            return item != null && item.IsWeapon() && item.m_shared?.m_skillType == Skills.SkillType.Polearms;
        }
    }
}
