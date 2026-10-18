using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class Ashlands70Grant
    {
        internal string Token;
        internal long World, Author, Issued, Started, Until;
        internal ZDOID Caster;
        internal Ashlands70Kind Kind;
        internal int Quality;
        internal Vector3 Origin, End;
        internal HitData Damage;
        internal readonly List<ZDOID> Targets = new List<ZDOID>();
        internal void Write(ZPackage p)
        {
            p.Write(Token); p.Write(World); p.Write(Author); p.Write(Caster); p.Write((int)Kind); p.Write(Quality);
            p.Write(Issued); p.Write(Started); p.Write(Until); p.Write(Origin); p.Write(End); Damage.Serialize(ref p); p.Write(Targets.Count);
            foreach (ZDOID id in Targets) p.Write(id);
        }
        internal static Ashlands70Grant Read(ZPackage p)
        {
            var g = new Ashlands70Grant { Token = p.ReadString(), World = p.ReadLong(), Author = p.ReadLong(), Caster = p.ReadZDOID(),
                Kind = (Ashlands70Kind)p.ReadInt(), Quality = p.ReadInt(), Issued = p.ReadLong(), Started = p.ReadLong(), Until = p.ReadLong(), Origin = p.ReadVector3(), End = p.ReadVector3(), Damage = new HitData() };
            g.Damage.Deserialize(ref p); int count = p.ReadInt();
            if (count < 0 || count > 4096) throw new ArgumentException("target count");
            for (int i = 0; i < count; i++) g.Targets.Add(p.ReadZDOID());
            if (p.GetPos() != p.Size()) throw new ArgumentException("trailing data");
            return g;
        }
    }
    internal static class Ashlands70Cast
    {
        private const string RequestRpc = "VM_Ashlands70RequestV1", GrantRpc = "VM_Ashlands70GrantV1", ResultRpc = "VM_Ashlands70ResultV1", ReadyRpc = "VM_Ashlands70ReadyV1";
        private sealed class Pending { internal Player Player; internal string Token; internal Ashlands70Kind Kind; internal float Eitr, Health, Started; internal bool Refresh; }
        private sealed class Receipt { internal Ashlands70Grant Grant; internal long Until; }
        private static readonly Dictionary<string, Receipt> Receipts = new Dictionary<string, Receipt>();
        private static readonly Dictionary<string, long> Cooldowns = new Dictionary<string, long>();
        private static readonly HashSet<ZRpc> Registered = new HashSet<ZRpc>();
        private static readonly HashSet<ZRpc> ReadyPeers = new HashSet<ZRpc>();
        private static readonly HashSet<ZRpc> CapablePeers = new HashSet<ZRpc>();
        private static readonly List<string> Expired = new List<string>();
        private static readonly List<ZDO> Nearby = new List<ZDO>();
        private static readonly HashSet<ZoneSystem.SectorIndex> Visited = new HashSet<ZoneSystem.SectorIndex>();
        private static Pending Awaiting;
        private static ZNet Session;
        private static long World;
        private static float NextSnapshot, NextReady;
        private static bool RemoteReady;
        internal static long Now => ZNet.instance != null && ZNet.instance.GetWorld() != null ? ZNet.instance.GetTime().Ticks : 0;
        internal static long CurrentWorld => ZNet.instance != null && ZNet.instance.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
        internal static void Register(ZNetPeer peer)
        {
            CheckSession(); ZRpc rpc = peer?.m_rpc;
            if (rpc == null || !Registered.Add(rpc)) return;
            rpc.Register<ZPackage>(RequestRpc, ReceiveRequest); rpc.Register<ZPackage>(GrantRpc, ReceiveGrant);
            rpc.Register<string, bool>(ResultRpc, ReceiveResult);
            rpc.Register<long>(ReadyRpc, ReceiveReady);
        }
        private static void ReceiveReady(ZRpc rpc, long world)
        {
            CheckSession();
            if (CurrentWorld == 0 || world != CurrentWorld || rpc == null) return;
            if (ZNet.instance.IsServer())
            {
                ZNetPeer peer = ZNet.instance.GetPeer(rpc);
                if (peer?.IsReady() != true || OwnerSkillAuthority.ResolveCharacterData(peer) == null) return;
                CapablePeers.Add(rpc); ReadyPeers.Remove(rpc); rpc.Invoke(ReadyRpc, world);
            }
            else if (rpc == ZNet.instance.GetServerRPC()) RemoteReady = true;
        }
        private static bool CohortReady()
        { foreach (ZNetPeer peer in ZNet.instance.GetPeers()) if (peer?.IsReady() == true && !CapablePeers.Contains(peer.m_rpc)) return false; return true; }
        private static void CheckSession()
        {
            long world = CurrentWorld;
            if (Session == ZNet.instance && (World == world || world == 0)) return;
            // Keep early connection registrations when world metadata becomes ready.
            bool same = Session == ZNet.instance;
            Session = ZNet.instance; World = world; Receipts.Clear(); Cooldowns.Clear(); ReadyPeers.Clear(); CapablePeers.Clear();
            Awaiting = null; NextSnapshot = NextReady = 0f; RemoteReady = false; if (!same) Registered.Clear(); Ashlands70World.Reset();
        }
        internal static void Tick()
        {
            CheckSession(); if (CurrentWorld == 0 || ZNet.instance == null) return;
            if (Awaiting != null && Time.unscaledTime - Awaiting.Started > 12f) Awaiting = null; // uncertain outcome: no automatic refund
            Ashlands70World.Tick();
            if (!ZNet.instance.IsServer() && !RemoteReady && Player.m_localPlayer?.m_nview?.IsValid() == true && Time.unscaledTime >= NextReady)
            { NextReady = Time.unscaledTime + 1f; ZNet.instance.GetServerRPC()?.Invoke(ReadyRpc, CurrentWorld); }
            if (!ZNet.instance.IsServer() || Time.unscaledTime < NextSnapshot) return;
            NextSnapshot = Time.unscaledTime + 1f;
            Registered.RemoveWhere(r => ZNet.instance.GetPeer(r) == null);
            ReadyPeers.RemoveWhere(r => ZNet.instance.GetPeer(r) == null);
            CapablePeers.RemoveWhere(r => ZNet.instance.GetPeer(r) == null);
            Expired.Clear(); foreach (var row in Receipts) if (row.Value.Until <= Now) Expired.Add(row.Key);
            foreach (string key in Expired) Receipts.Remove(key);
            Expired.Clear(); foreach (var row in Cooldowns) if (row.Value <= Now) Expired.Add(row.Key);
            foreach (string key in Expired) Cooldowns.Remove(key);
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer?.IsReady() != true || peer.m_rpc == null || !CapablePeers.Contains(peer.m_rpc) || !ReadyPeers.Add(peer.m_rpc)) continue;
                try
                {
                    foreach (Receipt receipt in Receipts.Values)
                        if (Ashlands70Rules.Active(Now, receipt.Grant.Until)) SendGrant(peer.m_rpc, receipt.Grant);
                }
                catch (Exception error) { ReadyPeers.Remove(peer.m_rpc); MasteryPlugin.Log.LogWarning("[Ashlands70] snapshot retry: " + error.GetType().Name); }
            }
        }
        internal static bool Input(Player player)
        {
            if (player != Player.m_localPlayer || !MagicSkillPassives.OwnerReady(player)) return true;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon(); Ashlands70Kind kind = Ashlands70Rules.Kind(PerkRuntimeService.ItemPrefabName(weapon));
            if (kind == Ashlands70Kind.None || !PerkRuntimeService.HasPerk(player, Ashlands70Rules.Skill(kind), 70) ||
                (!player.m_secondaryAttack && !player.m_secondaryAttackHold)) return true;
            player.m_queuedSecondAttackTimer = 0f;
            bool refresh = Ashlands70World.CanRefresh(player.GetZDOID(), player.GetPlayerID(), kind, out _);
            if (!player.m_secondaryAttack || player.InAttack() || player.InMinorAction() || player.IsTeleporting() ||
                player.IsStaggering() || player.m_dodgeInvincible || player.m_blocking || Awaiting != null ||
                !Ashlands70Rules.Quality(weapon.m_quality) || !player.HaveEitr(Ashlands70Rules.Eitr(kind)) ||
                (PerkCooldownStateService.GetRemainingSeconds(player, Ashlands70Rules.CooldownId(kind)) > 0d && !refresh)) return false;
            float hp = kind == Ashlands70Kind.Madness ? player.GetMaxHealth() * .2f : 0f;
            if (hp > 0f && player.GetHealth() <= hp) return false;
            CheckSession(); if (CurrentWorld == 0) return false;
            Ashlands35ClusterCharge.Cancel();
            Vector3 origin = player.transform.position, end = origin;
            if (kind == Ashlands70Kind.Chain)
            {
                origin = player.GetEyePoint(); Vector3 direction = player.GetAimDir(origin).normalized;
                end = origin + direction * Ashlands70Rules.ChainLength;
                if (Physics.Raycast(origin, direction, out RaycastHit hit, Ashlands70Rules.ChainLength,
                    LayerMask.GetMask("terrain", "static_solid", "piece", "Default"), QueryTriggerInteraction.Ignore)) end = hit.point;
            }
            var grant = new Ashlands70Grant { Token = Guid.NewGuid().ToString("N"), World = CurrentWorld, Author = player.GetPlayerID(),
                Caster = player.GetZDOID(), Kind = kind, Quality = weapon.m_quality, Origin = origin, End = end,
                Issued = Now, Damage = new HitData() };
            bool localServer = ZNet.instance.IsServer(); ZRpc transport = localServer ? null : ZNet.instance.GetServerRPC();
            if (!localServer && (transport == null || !RemoteReady)) return false;
            var package = new ZPackage(); grant.Write(package); // Known serialization failures cannot consume resources.
            Awaiting = new Pending { Player = player, Token = grant.Token, Kind = kind, Eitr = Ashlands70Rules.Eitr(kind), Health = hp, Started = Time.unscaledTime, Refresh = refresh };
            player.UseEitr(Awaiting.Eitr); if (hp > 0f) player.UseHealth(hp);
            bool attempted = false;
            try
            {
                OwnerSkillAuthority.SendNow(); Magic70CastAnimation.Play(player); attempted = true;
                if (localServer) ReceiveRequest(null, package); else transport.Invoke(RequestRpc, package);
            }
            catch (Exception error)
            {
                if (!attempted) ReceiveResult(null, grant.Token, false);
                MasteryPlugin.Log.LogWarning("[Ashlands70] cast send outcome uncertain=" + attempted + " " + error.GetType().Name);
            }
            return false;
        }
        private static HitData Payload(Ashlands70Grant grant, ZRpc rpc)
        {
            var hit = new HitData { m_attacker = grant.Caster, m_skill = Ashlands70Rules.Skill(grant.Kind), m_blockable = true, m_dodgeable = true, m_skillRaiseAmount = 0f };
            if (grant.Kind == Ashlands70Kind.Chain || grant.Kind == Ashlands70Kind.Orbit)
            {
                ItemDrop native = (ObjectDB.instance?.GetItemPrefab(Ashlands70Rules.Staff(grant.Kind)) ?? ZNetScene.instance?.GetPrefab(Ashlands70Rules.Staff(grant.Kind)))?.GetComponent<ItemDrop>();
                if (native?.m_itemData?.m_shared == null) throw new InvalidOperationException("native staff pending");
                ItemDrop.ItemData weapon = native.m_itemData.Clone(); weapon.m_quality = grant.Quality;
                int low = 70, high = 100;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    bool accepted = rpc == null ? PerkRuntimeService.HasPerk(Player.m_localPlayer, hit.m_skill, middle) : OwnerSkillAuthority.Has(rpc, grant.Caster, grant.Author, hit.m_skill, middle);
                    if (accepted) low = middle; else high = middle - 1;
                }
                hit.m_damage = weapon.GetDamage();
                hit.m_damage.Modify(weapon.m_shared.m_attack.m_damageMultiplier * Ashlands70Rules.DamageRoll(low, UnityEngine.Random.value, CombatSkillScalingService.GetWeaponDamageCoefficient(hit.m_skill, weapon)));
            }
            Generated(hit); return hit;
        }
        private static void MadnessTargets(Ashlands70Grant grant, Vector3 origin)
        {
            grant.Targets.Clear(); Nearby.Clear(); Visited.Clear(); Vector2s center = ZoneSystem.GetZone(origin);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
                ZDOMan.instance.FindObjects(new Vector2s(center.x + x, center.y + y), Nearby, Visited);
            foreach (ZDO target in Nearby)
            {
                if ((target.GetPosition() - origin).sqrMagnitude > Ashlands70Rules.MadnessRadius * Ashlands70Rules.MadnessRadius ||
                    target.GetFloat(ZDOVars.s_health, 1f) <= 0f) continue;
                GameObject prefab = ZNetScene.instance?.GetPrefab(target.GetPrefab());
                if (prefab?.GetComponent<Character>() == null || prefab.GetComponent<Player>() != null) continue;
                Character loaded = Ashlands70World.Resolve(target.m_uid); if (loaded != null && loaded.IsDead()) continue;
                grant.Targets.Add(target.m_uid);
                if (grant.Targets.Count > 4096) throw new InvalidOperationException("too many madness targets");
            }
            Nearby.Clear(); Visited.Clear();
        }
        internal static void Generated(HitData hit)
        {
            hit.m_variant = Ashlands70Rules.GeneratedVariant; hit.m_skillRaiseAmount = 0f;
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
            context.PerkId = "ashlands70"; context.AllowSelfProc = context.AllowOtherPerkProc = false; context.XpMultiplier = 0f;
        }
        private static void ReceiveRequest(ZRpc rpc, ZPackage package)
        {
            CheckSession(); if (ZNet.instance?.IsServer() != true || CurrentWorld == 0 || package == null || package.Size() > 131072) return;
            Ashlands70Grant grant = null; bool committed = false;
            try
            {
                package.SetPos(0); grant = Ashlands70Grant.Read(package);
                WorkshopActor actor = WorkshopActor.Resolve(rpc); ZNetPeer peer = rpc == null ? null : ZNet.instance.GetPeer(rpc);
                ZDO data = ZDOMan.instance?.GetZDO(grant.Caster);
                if (!Guid.TryParseExact(grant.Token, "N", out _) || grant.World != CurrentWorld || actor == null || !actor.Available ||
                    actor.CharacterId != grant.Caster || actor.GetPlayerID() != grant.Author || data == null ||
                    (rpc != null && (peer?.IsReady() != true || data.GetOwner() != peer.m_uid))) { Reply(rpc, grant.Token, false); return; }
                string key = grant.Author + ":" + grant.Caster + ":" + grant.Token;
                if (Receipts.TryGetValue(key, out Receipt old)) { SendGrant(rpc, old.Grant); Reply(rpc, grant.Token, true); return; }
                if (!Guid.TryParseExact(grant.Token, "N", out _) || grant.Kind < Ashlands70Kind.Madness || grant.Kind > Ashlands70Kind.Orbit ||
                    !CohortReady() || (rpc != null && !CapablePeers.Contains(rpc)) ||
                    grant.World != CurrentWorld || actor == null || !actor.Available || actor.IsDead() || actor.IsTeleporting() ||
                    actor.CharacterId != grant.Caster || actor.GetPlayerID() != grant.Author || data == null ||
                    (rpc != null && (peer?.IsReady() != true || data.GetOwner() != peer.m_uid)) || !Ashlands70Rules.Quality(grant.Quality) ||
                    data.GetInt(ZDOVars.s_rightItem, 0) != Ashlands70Rules.Staff(grant.Kind).GetStableHashCode() ||
                    data.GetInt(ZDOVars.s_rightItemQuality, 1) != grant.Quality || !Ashlands70Rules.Finite(grant.Origin) || !Ashlands70Rules.Finite(grant.End) ||
                    !Ashlands70Rules.Finite(actor.Position) || Vector3.Distance(actor.Position, grant.Origin) > 3f || Vector3.Distance(grant.Origin, grant.End) > Ashlands70Rules.ChainLength + .05f ||
                    !(rpc == null ? PerkRuntimeService.HasPerk(Player.m_localPlayer, Ashlands70Rules.Skill(grant.Kind), 70) :
                      OwnerSkillAuthority.Has(rpc, grant.Caster, grant.Author, Ashlands70Rules.Skill(grant.Kind), 70)) ||
                    !Ashlands70Rules.Fresh(Now, grant.Issued))
                { Reply(rpc, grant.Token, false); return; }
                string cooldown = grant.Author + ":" + grant.Kind;
                bool refresh = Ashlands70World.CanRefresh(grant.Caster, grant.Author, grant.Kind, out long previousStarted);
                if (Cooldowns.TryGetValue(cooldown, out long until) && Now < until && !refresh) { Reply(rpc, grant.Token, false); return; }
                if (grant.Kind == Ashlands70Kind.Chain)
                {
                    Vector3 offset = grant.Origin - actor.EyePoint;
                    if (offset.sqrMagnitude > .0001f && Physics.Raycast(actor.EyePoint, offset.normalized, out _, offset.magnitude,
                        LayerMask.GetMask("terrain", "static_solid", "piece", "Default"), QueryTriggerInteraction.Ignore))
                    { Reply(rpc, grant.Token, false); return; }
                    Vector3 span = grant.End - grant.Origin;
                    if (span.sqrMagnitude > .0001f && Physics.Raycast(grant.Origin, span.normalized, out RaycastHit solid, span.magnitude,
                        LayerMask.GetMask("terrain", "static_solid", "piece", "Default"), QueryTriggerInteraction.Ignore)) grant.End = solid.point;
                }
                else grant.Origin = grant.End = actor.Position;
                if (grant.Kind == Ashlands70Kind.Madness) MadnessTargets(grant, actor.Position);
                else grant.Targets.Clear();
                grant.Damage = Payload(grant, rpc); // Ignore every caller-supplied damage/status/push field.
                grant.Started = refresh ? Math.Max(Now, previousStarted + 1L) : Now; grant.Until = Ashlands70Rules.Deadline(grant.Started, Ashlands70Rules.Duration(grant.Kind, grant.Quality)); Generated(grant.Damage);
                Receipts[key] = new Receipt { Grant = grant, Until = Math.Max(grant.Until, Ashlands70Rules.Deadline(Now, 120d)) };
                Cooldowns[cooldown] = Ashlands70Rules.Deadline(Now, Ashlands70Rules.Cooldown(grant.Kind));
                committed = true; Ashlands70World.Accept(grant);
                foreach (ZNetPeer receiver in ZNet.instance.GetPeers()) if (receiver?.IsReady() == true && CapablePeers.Contains(receiver.m_rpc))
                    try { SendGrant(receiver.m_rpc, grant); } catch (Exception error) { MasteryPlugin.Log.LogWarning("[Ashlands70] grant delivery pending: " + error.GetType().Name); ReadyPeers.Remove(receiver.m_rpc); }
                Reply(rpc, grant.Token, true);
            }
            catch (Exception error) { if (grant != null) Reply(rpc, grant.Token, committed); MasteryPlugin.Log.LogWarning("[Ashlands70] packet outcome=" + committed + " " + error.GetType().Name); }
        }
        private static void SendGrant(ZRpc rpc, Ashlands70Grant grant)
        {
            if (rpc == null) { Ashlands70World.Accept(grant); return; }
            var package = new ZPackage(); grant.Write(package); rpc.Invoke(GrantRpc, package);
        }
        private static void ReceiveGrant(ZRpc rpc, ZPackage package)
        {
            if (rpc == null || ZNet.instance?.IsServer() != false || rpc != ZNet.instance.GetServerRPC() || package == null || package.Size() > 131072) return;
            try { package.SetPos(0); Ashlands70Grant grant = Ashlands70Grant.Read(package); if (grant.World == CurrentWorld) Ashlands70World.Accept(grant); } catch (Exception) { }
        }
        private static void Reply(ZRpc rpc, string token, bool accepted)
        { if (rpc == null) ReceiveResult(null, token, accepted); else rpc.Invoke(ResultRpc, token, accepted); }
        private static void ReceiveResult(ZRpc rpc, string token, bool accepted)
        {
            if (rpc != null && rpc != ZNet.instance?.GetServerRPC()) return;
            Pending pending = Awaiting; if (pending == null || pending.Token != token) return; Awaiting = null;
            if (pending.Player == null || pending.Player != Player.m_localPlayer || pending.Player.IsDead()) return;
            if (accepted)
            {
                if (pending.Refresh) PerkCooldownStateService.ReduceRemaining(pending.Player, Ashlands70Rules.CooldownId(pending.Kind), 0f);
                PerkCooldownStateService.TryConsume(pending.Player, Ashlands70Rules.CooldownId(pending.Kind), Ashlands70Rules.Cooldown(pending.Kind));
            }
            else { pending.Player.AddEitr(pending.Eitr); if (pending.Health > 0f) pending.Player.Heal(pending.Health, true); }
        }
        internal static void Reset()
        { Session = null; World = 0; Awaiting = null; RemoteReady = false; Registered.Clear(); ReadyPeers.Clear(); CapablePeers.Clear(); Receipts.Clear(); Cooldowns.Clear(); Ashlands70World.Reset(); }
    }
}
