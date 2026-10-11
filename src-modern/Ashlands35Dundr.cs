using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace ValheimMastery
{
    internal static class Ashlands35Dundr
    {
        private const string Rpc = "VM_Ashlands35DundrAcceptedV1";
        private const string NextKey = "vm.ash35.dundr.next", HeadKey = "vm.ash35.dundr.head", BitsKey = "vm.ash35.dundr.bits";
        internal sealed class Candidate { internal ZDOID Id; internal int Pellet; internal long Owner; internal Character Body; internal Vector3 Point; }
        internal sealed class Volley
        {
            internal Player Caster; internal long Serial, World; internal int Pellets; internal float Stamp;
            internal bool Spent; internal int Bound; internal float Speed; internal GameObject Prefab; internal ItemDrop.ItemData Weapon;
            internal readonly HitData[] Hits = new HitData[12];
            internal readonly Dictionary<ZDOID, ushort> Accepted = new Dictionary<ZDOID, ushort>();
            internal readonly List<Candidate> Candidates = new List<Candidate>(64);
        }
        internal sealed class Shot { internal Volley Volley; internal int Pellet; }
        private sealed class Extra { internal ZDOID Origin; }
        private static readonly ConditionalWeakTable<Projectile, Extra> Extras = new ConditionalWeakTable<Projectile, Extra>();
        internal sealed class Acceptance { internal Character Target; internal Player Caster; internal HitData Hit; internal int Token; internal bool Applied; }
        [ThreadStatic] internal static Volley CurrentBurst;
        [ThreadStatic] internal static Shot CurrentHit;
        [ThreadStatic] internal static Acceptance CurrentAcceptance;
        [ThreadStatic] internal static Player ExtraCaster;
        private const string EmitterKey = "vm.ash35.dundr.emitter", ExtraWorldKey = "vm.ash35.dundr.world";
        private static readonly ConditionalWeakTable<Projectile, Shot> Shots = new ConditionalWeakTable<Projectile, Shot>();
        private static readonly ConditionalWeakTable<Player, List<Volley>> Pending = new ConditionalWeakTable<Player, List<Volley>>();
        internal static bool Wire(int variant) => variant >= 10000 && variant < 26384;
        private static int Token(long serial, int pellet) => 10000 + (int)(serial % 1024) * 16 + pellet;
        internal static void Register(Player caster)
        {
            if (caster?.m_nview?.IsValid() == true)
                caster.m_nview.Register<ZPackage>(Rpc, (sender, packet) => Receive(caster, sender, packet));
        }
        internal static Volley Begin(Attack attack)
        {
            Player caster = attack?.m_character as Player;
            if (!Ashlands35Summons.Ready(caster, Skills.SkillType.ElementalMagic) || attack.m_weapon?.m_dropPrefab?.name != "StaffLightning") return null;
            ZDO data = caster.m_nview.GetZDO(); long next = data.GetLong(NextKey, 0);
            if (next < 0 || next == long.MaxValue) return null;
            data.Set(NextKey, ++next);
            var volley = new Volley { Caster = caster, Serial = next, World = Ashlands35Summons.World,
                Stamp = Time.unscaledTime, Pellets = Math.Clamp(attack.m_projectiles, 1, 12), Prefab = attack.m_attackProjectile, Weapon = attack.m_weapon };
            List<Volley> list = Pending.GetOrCreateValue(caster);
            list.RemoveAll(v => v.Spent || v.World != volley.World || Time.unscaledTime - v.Stamp > 3f);
            if (list.Count >= 64) list.RemoveAt(0);
            list.Add(volley); return volley;
        }
        internal static void Bind(Projectile projectile, ItemDrop.ItemData item, HitData hit)
        {
            if (CurrentBurst == null || item?.m_dropPrefab?.name != "StaffLightning" || hit == null ||
                projectile?.m_owner != CurrentBurst.Caster || projectile.m_nview?.IsOwner() != true ||
                PerkRuntimeService.IsPerkGenerated(hit) || CurrentBurst.Bound >= CurrentBurst.Pellets) return;
            int pellet = CurrentBurst.Bound++; CurrentBurst.Hits[pellet] = hit.Clone();
            CurrentBurst.Speed = Mathf.Max(CurrentBurst.Speed, projectile.GetVelocity().magnitude);
            Shots.Remove(projectile); Shots.Add(projectile, new Shot { Volley = CurrentBurst, Pellet = pellet });
        }
        internal static Shot For(Projectile projectile) => projectile != null && !projectile.m_didHit &&
            projectile.m_nview?.IsOwner() == true && Shots.TryGetValue(projectile, out Shot shot) ? shot : null;
        internal static void Transport(Character first, ref HitData hit)
        {
            Shot shot = CurrentHit; Volley volley = shot?.Volley; Player caster = volley?.Caster;
            if (volley == null || volley.Spent || caster?.m_nview?.IsOwner() != true || hit == null ||
                hit.GetAttacker() != caster || PerkRuntimeService.IsPerkGenerated(hit) || !Enemy(caster, first) ||
                first.m_nview?.IsValid() != true || volley.Candidates.Count >= 64) return;
            ZDOID id = first.GetZDOID();
            if (!volley.Candidates.Exists(c => c.Id == id && c.Pellet == shot.Pellet)) volley.Candidates.Add(new Candidate
                { Id = id, Pellet = shot.Pellet, Owner = first.m_nview.GetZDO().GetOwner(), Body = first, Point = first.GetCenterPoint() });
            // Only the transported clone carries the token; native projectile impact VFX remain unchanged.
            hit = hit.Clone(); hit.m_variant = (short)Token(volley.Serial, shot.Pellet);
        }
        internal static Acceptance Capture(Character target, ref HitData hit)
        {
            if (hit == null || !Wire(hit.m_variant)) return null;
            int token = hit.m_variant; hit = hit.Clone(); hit.m_variant = -1;
            Player caster = hit.GetAttacker() as Player;
            return target?.m_nview?.IsValid() == true && target.m_nview.IsOwner() && caster != null && Enemy(caster, target)
                ? new Acceptance { Target = target, Caster = caster, Hit = hit, Token = token } : null;
        }
        internal static Acceptance Applying(Character target, HitData hit)
        {
            Acceptance scope = CurrentAcceptance;
            return scope != null && scope.Target == target && ReferenceEquals(scope.Hit, hit) &&
                !target.IsDebugFlying() && !target.IsDead() && !target.IsTeleporting() && !target.InCutscene() &&
                !CinematicsManager.IsPlaying() ? scope : null;
        }
        internal static void Confirm(Acceptance scope)
        {
            if (scope == null || !scope.Applied || scope.Target?.m_nview?.IsValid() != true || !scope.Target.m_nview.IsOwner() ||
                scope.Caster?.m_nview?.IsValid() != true) return;
            var packet = new ZPackage(); packet.Write(Ashlands35Summons.World); packet.Write(scope.Token); packet.Write(scope.Target.GetZDOID());
            scope.Caster.m_nview.InvokeRPC(Rpc, packet);
        }
        private static void Receive(Player caster, long sender, ZPackage packet)
        {
            if (!Ashlands35Summons.Ready(caster, Skills.SkillType.ElementalMagic) || packet == null || packet.Size() != 24 ||
                !Pending.TryGetValue(caster, out List<Volley> list)) return;
            try
            {
                packet.SetPos(0); long world = packet.ReadLong(); int token = packet.ReadInt(); ZDOID id = packet.ReadZDOID();
                if (world != Ashlands35Summons.World || !Wire(token)) return;
                int pellet = (token - 10000) % 16;
                Volley volley = list.Find(v => !v.Spent && v.World == world && Token(v.Serial, pellet) == token && pellet < v.Pellets && Time.unscaledTime - v.Stamp <= 3f);
                Candidate first = volley?.Candidates.Find(c => c.Id == id && c.Pellet == pellet && c.Owner == sender);
                if (first == null || volley.Bound == 0) return;
                volley.Accepted.TryGetValue(id, out ushort mask);
                if (!Ashlands35Rules.AcceptPellet(ref mask, pellet, volley.Pellets)) return;
                volley.Accepted[id] = mask;
                if (Ashlands35Rules.Majority(mask, volley.Pellets)) Chain(volley, first);
            }
            catch (Exception ex) { MasteryPlugin.Log.LogWarning("[Ashlands35] rejected Dundr confirmation: " + ex.GetType().Name); }
        }
        private static void Chain(Volley volley, Candidate first)
        {
            Player caster = volley.Caster; ZDO data = caster.m_nview.GetZDO();
            long head = data.GetLong(HeadKey, 0); ulong bits = unchecked((ulong)data.GetLong(BitsKey, 0));
            if (!Ashlands35Rules.Claim(ref head, ref bits, volley.Serial)) { volley.Spent = true; return; }
            data.Set(HeadKey, head); data.Set(BitsKey, unchecked((long)bits)); volley.Spent = true;
            Vector3 from = first.Point, b = from, c = from;
            Character firstTarget = Nearest(caster, first.Body, null, first.Body, from);
            Character secondTarget = Nearest(caster, firstTarget, first.Body, first.Body, from);
            int count = 0;
            if (firstTarget != null) { Fire(volley, first, firstTarget); b = firstTarget.GetCenterPoint(); count++; }
            if (secondTarget != null) { Fire(volley, first, secondTarget); c = secondTarget.GetCenterPoint(); count++; }
            if (count > 0) Ashlands35Presentation.Arc(caster, from, b, c, count);
        }
        private static ZDOID Emitter(Projectile shot)
        {
            if (shot == null) return ZDOID.None;
            if (Extras.TryGetValue(shot, out Extra extra)) return extra.Origin;
            ZDO data = shot.m_nview?.IsValid() == true ? shot.m_nview.GetZDO() : null;
            return data != null && data.GetLong(ExtraWorldKey, 0) == Ashlands35Summons.World ? data.GetZDOID(EmitterKey) : ZDOID.None;
        }
        internal static Player BeginExtra(Projectile shot)
        {
            if (Emitter(shot) == ZDOID.None) return null;
            // Retain generated/zero-XP identity when this native projectile changes owner.
            shot.m_hitVariant = 1270; shot.m_skill = Skills.SkillType.ElementalMagic;
            shot.m_raiseSkillAmount = shot.m_adrenaline = 0f;
            return shot.m_owner as Player;
        }
        internal static bool IsEmitter(Projectile shot, IDestructible target) => target is Character body &&
            Emitter(shot) != ZDOID.None && body.GetZDOID() == Emitter(shot);
        internal static bool ObserveXp(HitData hit) => !(ExtraCaster != null && hit != null &&
            hit.GetAttacker() == ExtraCaster && hit.m_skill == Skills.SkillType.ElementalMagic && hit.m_variant == 1270);
        internal static bool RaiseXp(Player caster, Skills.SkillType skill, float amount) =>
            !(caster != null && caster == ExtraCaster && skill == Skills.SkillType.ElementalMagic && amount == 0f);
        private static void Fire(Volley volley, Candidate first, Character target)
        {
            if (volley.Prefab == null || volley.Speed <= 0f) return;
            Vector3 point = target.GetCenterPoint(), direction = (point - first.Point).normalized;
            Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
            if (side.sqrMagnitude < .01f) side = Vector3.right;
            int amount = Ashlands35Rules.ExtraPellets(volley.Pellets);
            for (int i = 0; i < amount; i++)
            {
                // Native networked lightning pellets, originating at the struck enemy.
                Vector3 origin = first.Point + side * ((i - (amount-1)*.5f) * .04f);
                GameObject instance = UnityEngine.Object.Instantiate(volley.Prefab, origin, Quaternion.LookRotation(direction));
                Projectile shot = instance?.GetComponent<Projectile>();
                if (shot?.m_nview?.IsValid() != true || !shot.m_nview.IsOwner())
                { if (instance != null) ZNetScene.instance.Destroy(instance); continue; }
                Extras.Add(shot, new Extra { Origin = first.Id });
                shot.m_nview.GetZDO().Set(EmitterKey, first.Id); shot.m_nview.GetZDO().Set(ExtraWorldKey, volley.World);
                HitData hit = volley.Hits[i % volley.Bound].Clone(); hit.m_point = origin;
                hit.m_variant = 1270; hit.m_skillRaiseAmount = 0f;
                var context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
                context.PerkId = "ashlands35_dundr"; context.AllowSelfProc = context.AllowOtherPerkProc = false; context.XpMultiplier = 0f;
                shot.Setup(volley.Caster, (point-origin).normalized * volley.Speed, 0f, hit, volley.Weapon, null);
                shot.m_raiseSkillAmount = 0f; shot.m_adrenaline = 0f; shot.m_spawnItem = null; shot.m_respawnItemOnHit = false;
            }
        }
        private static bool Enemy(Player caster, Character enemy) => enemy != null && !enemy.IsDead() &&
            !enemy.IsPlayer() && !enemy.IsTamed() && BaseAI.IsEnemy(caster, enemy);
        private static Character Nearest(Player caster, Character previous, Character used, Character first, Vector3 point)
        {
            Character best = null; float distance = Ashlands35Rules.ChainRadius * Ashlands35Rules.ChainRadius;
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            foreach (Character enemy in Character.GetAllCharacters())
            {
                if (enemy == previous || enemy == used || enemy == first || !Enemy(caster, enemy)) continue;
                Vector3 delta = enemy.GetCenterPoint() - point; float squared = delta.sqrMagnitude;
                if (squared > distance || Physics.Raycast(point, delta.normalized, delta.magnitude, mask, QueryTriggerInteraction.Ignore)) continue;
                distance = squared; best = enemy;
            }
            return best;
        }
    }
}
