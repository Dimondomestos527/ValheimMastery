using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class StaffMagicXpContext
    {
        private const string Prefix = "vm.staffxp.", AuthorKey = Prefix + "author", CasterKey = Prefix + "caster",
            StaffKey = Prefix + "staff", WorldKey = Prefix + "world", CapKey = Prefix + "cap", RawKey = Prefix + "raw",
            KindKey = Prefix + "kind", SpentKey = Prefix + "spent";
        internal sealed class Budget
        {
            internal bool Spent, NoTargetNative;
            internal readonly List<WeakReference<Projectile>> Returns = new List<WeakReference<Projectile>>();
            internal void Spend()
            {
                Spent = true;
                foreach (var weak in Returns) if (weak.TryGetTarget(out Projectile p) && p != null && p.m_nview?.IsOwner() == true)
                    p.m_nview.GetZDO().Set(SpentKey, 1);
            }
        }
        internal sealed class Origin
        {
            internal string Staff;
            internal long Author, World;
            internal ZDOID Caster;
            internal Character Source;
            internal int Cap, Kind;
            internal float Raw;
            internal Budget Budget = new Budget();
        }
        internal sealed class Frame
        {
            internal Origin Origin;
            internal Character Actor, Target;
            internal Skills.SkillType Skill;
            internal bool NativeAward, Blocked;
            internal Aoe Aoe;
            internal Projectile Projectile;
        }
        internal struct Scope { internal bool Captured; internal Frame Previous, Current; }
        [ThreadStatic] internal static Frame Current;
        private sealed class Reaction { internal float Level; internal Origin Origin; }
        private static readonly ConditionalWeakTable<Projectile, Origin> Projectiles = new ConditionalWeakTable<Projectile, Origin>();
        private static readonly ConditionalWeakTable<Aoe, Origin> Areas = new ConditionalWeakTable<Aoe, Origin>();
        private static readonly ConditionalWeakTable<Aoe, Budget> AreaCredits = new ConditionalWeakTable<Aoe, Budget>();
        private static readonly ConditionalWeakTable<SE_React, Reaction> Reactions = new ConditionalWeakTable<SE_React, Reaction>();
        private static bool Enabled => MasteryPlugin.Settings.Enabled.Value;
        private static long World => ZNet.instance != null && ZNet.instance.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
        private static ZDO Owned(Character character) => character?.m_nview?.IsValid() == true && character.m_nview.IsOwner() ? character.m_nview.GetZDO() : null;
        private static bool Usable(Origin o) => o != null && o.Author != 0 && o.Caster != ZDOID.None && o.World != 0 && o.World == World && StaffMagicXpRules.Index(o.Staff) >= 0;
        private static Origin Copy(Origin o) => new Origin { Staff = o.Staff, Author = o.Author, Caster = o.Caster,
            World = o.World, Source = o.Source, Cap = o.Cap, Kind = o.Kind, Raw = o.Raw, Budget = o.Budget };
        private static ZNetView AreaView(Aoe area)
        {
            ZNetView view = area?.GetComponentInParent<ZNetView>();
            // Attached effects must never overwrite a character's immutable birth metadata.
            return view?.GetComponent<Character>() != null ? null : view;
        }
        private static string AreaKey(Aoe area) => Prefix + "area.spent." + (int)area.m_skill + "." + StaffMagicXpRules.Name(area.gameObject.name).GetStableHashCode();
        private static Budget AreaCredit(Aoe area) => AreaCredits.GetValue(area, a => new Budget
        { Spent = AreaView(a)?.GetZDO()?.GetInt(AreaKey(a), 0) != 0 && AreaView(a)?.GetZDO() != null });
        private static void SpendArea(Aoe area, Budget credit)
        {
            credit.Spend(); ZNetView view = AreaView(area);
            if (view?.IsValid() == true && view.IsOwner()) view.GetZDO().Set(AreaKey(area), 1);
        }
        private static Origin Cast(Character owner, ItemDrop.ItemData item)
        {
            string staff = StaffMagicXpRules.Name(item?.m_dropPrefab?.name);
            if (!(owner is Player player) || StaffMagicXpRules.Index(staff) < 0 || player.GetPlayerID() == 0 || World == 0) return null;
            return new Origin { Staff = staff, Author = player.GetPlayerID(), Caster = player.GetZDOID(), World = World, Source = player };
        }
        private static Origin SetupOrigin(Character owner, ItemDrop.ItemData item)
        {
            Origin cast = Cast(owner, item), parent = Current?.Origin;
            if (!Usable(parent) || Current.Blocked) return cast;
            if (item != null && cast == null && StaffMagicXpRules.SummonStaff(owner?.gameObject.name) != parent.Staff) return null;
            if (cast != null && (cast.Staff != parent.Staff || cast.Author != parent.Author || cast.Caster != parent.Caster || parent.Kind == StaffMagicXpRules.Frost)) return cast;
            return Copy(parent);
        }
        private static bool Zero(HitData hit) => hit != null && (hit.m_variant == 7300 || PerkRuntimeService.IsPerkGenerated(hit) || PerkRuntimeService.GetHitContext(hit)?.XpMultiplier <= 0f);
        private static void Track(Origin o, Projectile p)
        {
            if (o.Staff != "StaffOrbofAhri" && (o.Kind != StaffMagicXpRules.Frost || StaffMagicXpRules.Name(p.gameObject.name) != "staff_FrostOrbs_projectile")) return;
            foreach (var weak in o.Budget.Returns) if (weak.TryGetTarget(out Projectile previous) && previous == p) return;
            if (o.Budget.Returns.Count < 8) o.Budget.Returns.Add(new WeakReference<Projectile>(p));
        }
        private static bool SpecialProjectile(Frame f, string prefab) => f.Projectile != null && f.Aoe == null &&
            StaffMagicXpRules.Name(f.Projectile.gameObject.name) == prefab && f.Projectile.m_nview?.IsValid() == true && f.Projectile.m_nview.IsOwner();
        private static void Persist(ZNetView view, Origin o)
        {
            if (view?.IsValid() != true || !view.IsOwner() || !Usable(o)) return;
            ZDO z = view.GetZDO(); z.Set(AuthorKey, o.Author); z.Set(CasterKey, o.Caster); z.Set(StaffKey, o.Staff);
            z.Set(WorldKey, o.World); z.Set(CapKey, o.Cap); z.Set(KindKey, o.Kind); z.Set(RawKey, o.Raw); z.Set(SpentKey, o.Budget.Spent ? 1 : 0);
        }
        private static Origin Restore(ZNetView view, Character source = null)
        {
            ZDO z = view?.IsValid() == true ? view.GetZDO() : null;
            if (z == null) return null;
            var o = new Origin { Staff = z.GetString(StaffKey, ""), Author = z.GetLong(AuthorKey, 0), Caster = z.GetZDOID(CasterKey),
                World = z.GetLong(WorldKey, 0), Cap = z.GetInt(CapKey, 0), Kind = z.GetInt(KindKey, 0), Raw = z.GetFloat(RawKey, 0f), Source = source };
            o.Budget.Spent = z.GetInt(SpentKey, 0) != 0;
            return Usable(o) ? o : null;
        }
        // Immutable birth proof, with native cap captured once; no follow-target inference.
        internal static void Birth(SpawnAbility ability, Character source)
        {
            if (!Enabled || Owned(source) == null) return;
            Origin o = Cast(ability?.m_owner, ability?.m_weapon);
            if (!Usable(o) || StaffMagicXpRules.SummonStaff(source.gameObject.name) != o.Staff) return;
            ZDO z = source.m_nview.GetZDO();
            if (z.GetLong(AuthorKey, 0) != 0) return;
            o.Source = source; o.Cap = z.GetInt(ZDOVars.s_maxInstances, 0);
            Persist(source.m_nview, o);
            if (o.Staff == "StaffSkeleton") z.Set("vm.bloodxp.skeleton.caster", o.Caster);
        }
        internal static Origin Summon(Character source)
        {
            string staff = StaffMagicXpRules.SummonStaff(source?.gameObject.name);
            if (staff.Length == 0 || source?.m_nview?.IsValid() != true) return null;
            Origin o = Restore(source.m_nview, source);
            if (o != null)
            {
                if (o.Staff != staff) return null;
                Rebind(o); return o;
            }
            // Magic35 regrowth creates a new body without calling SpawnAbility.
            ZDO z = Owned(source);
            if (staff != "StaffGreenRoots" || z == null || z.GetLong("vm.ash35.world", 0) != World) return null;
            o = new Origin { Staff = staff, Author = z.GetLong("vm.ash35.author", 0), Caster = z.GetZDOID("vm.ash35.caster"),
                World = World, Source = source, Cap = z.GetInt(ZDOVars.s_maxInstances, 0) };
            if (!Usable(o) || StaffMagicXpRules.RootBase(o.Cap) <= 0f) return null;
            Persist(source.m_nview, o); return o;
        }
        private static void Rebind(Origin o)
        {
            if (!Usable(o) || Owned(o.Source) == null) return;
            ZDOID current = BloodMagicCombatXp.FindAuthor(o.Author);
            if (current == ZDOID.None || current == o.Caster) return;
            // Character ZDO IDs change on reconnect; permanent profile author does not.
            o.Caster = current;
            if (StaffMagicXpRules.SummonStaff(o.Source.gameObject.name) == o.Staff) Persist(o.Source.m_nview, o);
            else if (o.Kind == StaffMagicXpRules.Frost) ReactionProof(o.Source.m_nview.GetZDO(), o);
        }
        internal static bool Proof(ZDO z, int kind, long author, ZDOID caster, long world)
        {
            if (z == null || world == 0) return false;
            string staff = StaffMagicXpRules.Staff(kind);
            if (kind == StaffMagicXpRules.Frost)
            {
                for (int i = 0; i < 8; i++) if (z.GetLong(Prefix + "react.world." + i, 0) == world &&
                    z.GetLong(Prefix + "react.author." + i, 0) == author && z.GetZDOID(Prefix + "react.caster." + i) == caster) return true;
                return false;
            }
            if (kind >= 100 || kind == StaffMagicXpRules.AhriReturn)
                return z.GetLong(ZDOVars.s_playerID, 0) == author && z.m_uid == caster &&
                    z.GetLong(Prefix + "direct.world." + staff, 0) == world;
            if (z.GetLong(AuthorKey, 0) != author || z.GetZDOID(CasterKey) != caster || z.GetString(StaffKey, "") != staff || z.GetLong(WorldKey, 0) != world) return false;
            foreach (string name in new[] { "Boar_spiritcaller", "Wolf_spiritcaller", "Bjorn_spiritcaller", "staff_greenroots_tentaroot", "Troll_Summoned" })
                if (z.GetPrefab() == name.GetStableHashCode() && StaffMagicXpRules.SummonStaff(name) == staff) return true;
            return false;
        }
        internal static bool RootProof(ZDO z, float raw) => z != null &&
            Math.Abs(raw - StaffMagicXpRules.RootBase(z.GetInt(CapKey, 0))) < .000001f && raw > 0f;
        internal static bool Grant(Origin o, int kind, float raw, Character target)
        {
            if (!Enabled || !Usable(o) || raw <= 0f || (o.Source == null || target == null || target == o.Source || target.IsPlayer() || target.IsTamed() || !BaseAI.IsEnemy(o.Source, target))) return false;
            Rebind(o);
            if (kind >= 100 || kind == StaffMagicXpRules.AhriReturn)
            {
                ZDO z = Owned(o.Source);
                if (z == null || !(o.Source is Player p) || p.GetPlayerID() != o.Author || p.GetZDOID() != o.Caster) return false;
                z.Set(Prefix + "direct.world." + o.Staff, o.World);
            }
            return BloodMagicCombatXp.SendStaff(o.Source, o.Caster, o.Author, kind, raw, TierDatabase.GetTargetTier(target), TierDatabase.TargetKey(target));
        }
        internal static void Setup(Projectile p, HitData hit, ItemDrop.ItemData item)
        {
            if (!Enabled || p == null || Zero(hit) || p.m_hitVariant == 1270 || p.m_hitVariant == 7300 || p.m_raiseSkillAmount <= 0f) return;
            Origin o = SetupOrigin(p.m_owner, item);
            if (!Usable(o)) return;
            if (StaffMagicXpRules.Name(p.gameObject.name) == "staff_OrbofAhri_projectile_return" && o.Staff == "StaffOrbofAhri")
            {
                o.Kind = StaffMagicXpRules.AhriReturn;
            }
            Track(o, p);
            Projectiles.Remove(p); Projectiles.Add(p, o); Persist(p.m_nview, o);
        }
        internal static void Setup(Aoe a, ItemDrop.ItemData item)
        {
            if (!Enabled || a == null || a.m_hitVariant == 7300 || a.m_hitVariant == 1270) return;
            Origin o = SetupOrigin(a.m_owner, item);
            if (!Usable(o)) return;
            Areas.Remove(a); Areas.Add(a, o); Persist(AreaView(a), o);
        }
        // Only freshly instantiated native children of the current impact are bound.
        internal static void CaptureSpawn(GameObject spawned)
        {
            if (!Enabled || spawned == null || !Usable(Current?.Origin) || Current.Blocked) return;
            foreach (Projectile p in spawned.GetComponentsInChildren<Projectile>(true))
            {
                Origin o = Copy(Current.Origin);
                if (StaffMagicXpRules.Name(p.gameObject.name) == "staff_OrbofAhri_projectile_return" && o.Staff == "StaffOrbofAhri")
                { o.Kind = StaffMagicXpRules.AhriReturn; }
                Track(o, p);
                Projectiles.Remove(p); Projectiles.Add(p, o); Persist(p.m_nview, o);
            }
            foreach (Aoe a in spawned.GetComponentsInChildren<Aoe>(true))
            {
                if (a.m_hitVariant == 7300 || a.m_hitVariant == 1270) continue;
                Areas.Remove(a); Origin o = Copy(Current.Origin); Areas.Add(a, o); Persist(AreaView(a), o);
            }
        }
        internal static void Begin(Projectile p, out Scope state)
        {
            Origin o = null;
            if (Enabled && p != null && !Projectiles.TryGetValue(p, out o)) o = Restore(p.m_nview, p.m_owner);
            if (o == null && Enabled) o = Cast(p?.m_owner, p?.m_weapon);
            if (o == null && Enabled) o = Summon(p?.m_owner);
            if (o != null && o.Source == null) o.Source = p.m_owner;
            if (Usable(o)) Track(o, p); // restored orb must persist its first successful spend too
            state = new Scope { Captured = true, Previous = Current, Current = Usable(o) ? new Frame { Origin = o, Actor = p.m_owner, Skill = p.m_skill,
                Projectile = p, Blocked = p.m_hitVariant == 1270 || p.m_hitVariant == 7300 || p.m_raiseSkillAmount <= 0f || Zero(p.m_originalHitData) } : null };
            Current = state.Current;
        }
        internal static void Begin(Aoe a, out Scope state)
        {
            Origin o = null;
            if (Enabled && a != null && !Areas.TryGetValue(a, out o)) o = Restore(AreaView(a), a.m_owner);
            if (o == null && Enabled) o = Cast(a?.m_owner, a?.m_itemData) ?? Summon(a?.m_owner);
            if (o != null && o.Source == null) o.Source = a.m_owner;
            state = new Scope { Captured = true, Previous = Current, Current = Usable(o) ? new Frame { Origin = o, Actor = a.m_owner ?? o.Source,
                Skill = a.m_skill, Aoe = a, Blocked = a.m_hitVariant == 7300 || a.m_hitVariant == 1270 } : null };
            Current = state.Current;
        }
        internal static void Begin(Attack attack, out Scope state)
        {
            Origin o = Enabled ? Summon(attack?.m_character) : null;
            state = new Scope { Captured = true, Previous = Current, Current = Usable(o) ? new Frame { Origin = o, Actor = attack.m_character } : null };
            Current = state.Current;
        }
        // ObserveHit keeps its existing public seam. HitData.m_skill is intentionally
        // absent on native Aoe, so the callback's declared skill is authoritative.
        internal static bool Observe(Component target, HitData hit)
        {
            Frame f = Current;
            if (!Enabled || f == null) return false;
            if (f.Blocked || hit == null || Zero(hit)) return true;
            if (!(target is Character c) || hit.GetAttacker() != (f.Aoe != null ? f.Aoe.m_owner : f.Actor) || !BloodMagicCombatXp.Enemy(f.Actor, c)) return true;
            if (f.Target == null) f.Target = c;
            return true; // context installed precisely around RaiseSkill, never stale pending state
        }
        internal struct RaiseScope
        {
            internal IDisposable Context;
            internal Frame Frame;
            internal Player Player;
            internal Skills.SkillType Skill;
            internal float Level, Accumulator;
        }
        private static void Snapshot(Player player, Skills.SkillType skill, out float level, out float accumulator)
        {
            level = accumulator = 0f;
            if (player?.GetSkills()?.m_skillData != null && player.GetSkills().m_skillData.TryGetValue(skill, out Skills.Skill value) && value != null)
            { level = value.m_level; accumulator = value.m_accumulator; }
        }
        internal static void Raised(RaiseScope state, bool ran)
        {
            if (!ran || state.Frame == null || state.Player == null) return;
            Snapshot(state.Player, state.Skill, out float level, out float accumulator);
            if (level <= state.Level && (level != state.Level || accumulator <= state.Accumulator)) return;
            state.Frame.NativeAward = true;
            if (state.Frame.Projectile != null && state.Frame.Origin.Staff == "StaffOrbofAhri" && state.Skill == Skills.SkillType.BloodMagic) state.Frame.Origin.Budget.Spend();
        }
        internal static bool Raise(Player player, Skills.SkillType skill, float raw, out RaiseScope state)
        {
            state = default;
            Frame f = Current;
            if (!Enabled || f == null || !StaffMagicXpRules.Magic(skill)) return true;
            if (raw <= 0f || f.Blocked) return false;
            if (f.Target == null)
            {
                if (f.Aoe != null) AreaCredit(f.Aoe).NoTargetNative = true;
                return false;
            }
            if (f.Aoe != null && AreaCredit(f.Aoe).Spent) return false;
            Origin o = f.Origin;
            if (f.Projectile != null && (o.Kind == StaffMagicXpRules.Frost || o.Kind == StaffMagicXpRules.AhriReturn ||
                (o.Staff == "StaffOrbofAhri" && skill == Skills.SkillType.BloodMagic && o.Budget.Spent))) return false; // projectile replacement/shared budget; native AoE rewards remain separate
            if (player.GetPlayerID() != o.Author || player.GetZDOID() != o.Caster || player != Player.m_localPlayer)
            {
                f.NativeAward = Grant(o, StaffMagicXpRules.DirectKind(o.Staff, skill), raw, f.Target);
                if (f.NativeAward && f.Projectile != null && o.Staff == "StaffOrbofAhri" && skill == Skills.SkillType.BloodMagic) o.Budget.Spend();
                return false;
            }
            if (PerkRuntimeService.GetActualSkillLevel(player, skill) >= 100f) return false;
            state.Frame = f; state.Player = player; state.Skill = skill;
            Snapshot(player, skill, out state.Level, out state.Accumulator);
            state.Context = ExperienceContext.BeginStaffCombat(player, skill, o.Staff, TierDatabase.GetTargetTier(f.Target), TierDatabase.TargetKey(f.Target));
            return true;
        }
        internal static bool RaiseCreature(Character source, float raw)
        {
            if (!Enabled || source is Player) return true;
            Origin o = Summon(source);
            if (o?.Staff != "StaffSpiritCaller") return true;
            Frame f = Current; Tameable tame = source.GetComponent<Tameable>();
            if (tame == null || tame.m_levelUpOwnerSkill != Skills.SkillType.BloodMagic) return true;
            if (f?.Actor == source && f.Target != null && !f.Blocked && !f.NativeAward)
                f.NativeAward = Grant(o, StaffMagicXpRules.Spirit, raw * tame.m_levelUpFactor, f.Target);
            return false; // managed author replaces mutable native follow target
        }
        internal static void Complete(Scope state, bool ran)
        {
            Frame f = state.Current;
            if (!ran || f == null || f.Blocked || f.Target == null || !Usable(f.Origin)) return;
            Origin o = f.Origin;
            if (o.Kind == StaffMagicXpRules.Frost && f.Projectile != null)
            {
                if (SpecialProjectile(f, "staff_FrostOrbs_projectile") && !o.Budget.Spent && Grant(o, StaffMagicXpRules.Frost, o.Raw, f.Target)) o.Budget.Spend();
                return;
            }
            if (o.Kind == StaffMagicXpRules.AhriReturn && f.Projectile != null)
            {
                if (SpecialProjectile(f, "staff_OrbofAhri_projectile_return") && !o.Budget.Spent && Grant(o, StaffMagicXpRules.AhriReturn, 1f, f.Target)) o.Budget.Spend();
                return;
            }
            // Only native attacks/projectiles, never spawn/death/passive AoE, earn new summon XP.
            if (f.Aoe == null && !o.Budget.Spent && (o.Staff == "StaffGreenRoots" || o.Staff == "StaffRedTroll"))
            {
                int kind = o.Staff == "StaffGreenRoots" ? StaffMagicXpRules.Roots : StaffMagicXpRules.Troll;
                if (Grant(o, kind, kind == StaffMagicXpRules.Roots ? StaffMagicXpRules.RootBase(o.Cap) : .5f, f.Target)) o.Budget.Spend();
                return;
            }
            if (f.Aoe != null && f.Aoe.m_canRaiseSkill && StaffMagicXpRules.Magic(f.Skill))
            {
                Budget credit = AreaCredit(f.Aoe);
                if (f.NativeAward) SpendArea(f.Aoe, credit);
                else if ((f.Aoe.m_owner == null || (credit.NoTargetNative && f.Aoe.m_gaveSkill)) && !credit.Spent &&
                    Grant(o, StaffMagicXpRules.DirectKind(o.Staff, f.Skill), 1f, f.Target)) SpendArea(f.Aoe, credit);
            }
        }
        internal static void ReactionLevel(SE_React effect, float level)
        {
            if (effect?.m_spawnObj == null || effect.m_spawnObj.name != "staff_FrostOrbs_projectile") return;
            Reactions.GetValue(effect, _ => new Reaction()).Level = level;
        }
        internal static void ReactionCaster(StatusEffect effect, Character caster)
        {
            if (!Enabled || !(effect is SE_React react) || react.m_spawnObj?.name != "staff_FrostOrbs_projectile" || !(caster is Player p) ||
                Owned(react.m_character) == null || BaseAI.IsEnemy(caster, react.m_character)) return;
            Origin o = new Origin { Staff = "StaffFrostOrbs", Author = p.GetPlayerID(), Caster = p.GetZDOID(), World = World,
                Source = react.m_character, Kind = StaffMagicXpRules.Frost };
            if (!Usable(o)) return;
            Reactions.GetValue(react, _ => new Reaction()).Origin = o;
            ReactionProof(react.m_character.m_nview.GetZDO(), o);
        }
        private static void ReactionProof(ZDO z, Origin o)
        {
            int slot = -1;
            for (int i = 0; i < 8; i++) if (z.GetLong(Prefix + "react.author." + i, 0) == o.Author && z.GetZDOID(Prefix + "react.caster." + i) == o.Caster) { slot = i; break; }
            if (slot < 0) { slot = Math.Abs(z.GetInt(Prefix + "react.next", 0) % 8); z.Set(Prefix + "react.next", (slot + 1) % 8); }
            z.Set(Prefix + "react.author." + slot, o.Author); z.Set(Prefix + "react.caster." + slot, o.Caster); z.Set(Prefix + "react.world." + slot, o.World);
        }
        internal static void Begin(SE_React effect, HitData hit, out Scope state)
        {
            Origin o = null;
            if (Enabled && effect != null && Reactions.TryGetValue(effect, out Reaction r) && Usable(r.Origin) && !Zero(hit))
            {
                SE_Shield reference = ObjectDB.instance?.GetStatusEffect("Staff_shield".GetStableHashCode()) as SE_Shield;
                if (reference != null)
                {
                    float capacity = reference.m_absorbDamage + reference.m_absorbDamagePerSkillLevel * r.Level +
                        (Game.m_worldLevel > 0 ? reference.m_absorbDamageWorldLevel * Game.m_worldLevel : 0f);
                    o = Copy(r.Origin); o.Raw = StaffMagicXpRules.FrostBase(hit?.GetTotalDamage() ?? 0f, capacity); o.Budget = new Budget();
                }
            }
            state = new Scope { Captured = true, Previous = Current, Current = Usable(o) ? new Frame { Origin = o, Actor = effect.m_character } : null };
            Current = state.Current;
        }
    }
    [HarmonyPatch(typeof(SpawnAbility), "SetupAoe")]
    internal static class StaffXpBirthPatch { private static void Prefix(SpawnAbility __instance, Character __0) => StaffMagicXpContext.Birth(__instance, __0); }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class StaffXpProjectileSetupPatch
    { [HarmonyPriority(Priority.Last)] private static void Postfix(Projectile __instance, HitData hitData, ItemDrop.ItemData item) => StaffMagicXpContext.Setup(__instance, hitData, item); }
    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class StaffXpAoeSetupPatch
    { private static void Postfix(Aoe __instance, ItemDrop.ItemData item) => StaffMagicXpContext.Setup(__instance, item); }
    [HarmonyPatch(typeof(Projectile), "OnHit")]
    internal static class StaffXpProjectileHitPatch
    {
        private static void Prefix(Projectile __instance, out StaffMagicXpContext.Scope __state) => StaffMagicXpContext.Begin(__instance, out __state);
        private static void Postfix(StaffMagicXpContext.Scope __state, bool __runOriginal) => StaffMagicXpContext.Complete(__state, __runOriginal);
        private static Exception Finalizer(StaffMagicXpContext.Scope __state, Exception __exception)
        { if (__state.Captured) StaffMagicXpContext.Current = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Aoe), "OnHit")]
    internal static class StaffXpAoeHitPatch
    {
        private static void Prefix(Aoe __instance, out StaffMagicXpContext.Scope __state) => StaffMagicXpContext.Begin(__instance, out __state);
        private static void Postfix(StaffMagicXpContext.Scope __state, bool __runOriginal) => StaffMagicXpContext.Complete(__state, __runOriginal);
        private static Exception Finalizer(StaffMagicXpContext.Scope __state, Exception __exception)
        { if (__state.Captured) StaffMagicXpContext.Current = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    internal static class StaffXpMeleePatch
    {
        private static void Prefix(Attack __instance, out StaffMagicXpContext.Scope __state) => StaffMagicXpContext.Begin(__instance, out __state);
        private static void Postfix(StaffMagicXpContext.Scope __state, bool __runOriginal) => StaffMagicXpContext.Complete(__state, __runOriginal);
        private static Exception Finalizer(StaffMagicXpContext.Scope __state, Exception __exception)
        { if (__state.Captured) StaffMagicXpContext.Current = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
    internal static class StaffXpAreaAttackPatch
    {
        private static void Prefix(Attack __instance, out StaffMagicXpContext.Scope __state) => StaffMagicXpContext.Begin(__instance, out __state);
        private static void Postfix(StaffMagicXpContext.Scope __state, bool __runOriginal) => StaffMagicXpContext.Complete(__state, __runOriginal);
        private static Exception Finalizer(StaffMagicXpContext.Scope __state, Exception __exception)
        { if (__state.Captured) StaffMagicXpContext.Current = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
    internal static class StaffXpNativeRaisePatch
    {
        [HarmonyPriority(Priority.Last)] private static bool Prefix(Player __instance, Skills.SkillType __0, float __1, out StaffMagicXpContext.RaiseScope __state) => StaffMagicXpContext.Raise(__instance, __0, __1, out __state);
        private static void Postfix(StaffMagicXpContext.RaiseScope __state, bool __runOriginal) => StaffMagicXpContext.Raised(__state, __runOriginal);
        private static Exception Finalizer(StaffMagicXpContext.RaiseScope __state, Exception __exception) { __state.Context?.Dispose(); return __exception; }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class StaffXpCreatureRaisePatch { private static bool Prefix(Character __instance, float __1) => StaffMagicXpContext.RaiseCreature(__instance, __1); }
    [HarmonyPatch(typeof(SE_React), nameof(SE_React.SetLevel))]
    internal static class StaffXpReactLevelPatch { private static void Postfix(SE_React __instance, float __1) => StaffMagicXpContext.ReactionLevel(__instance, __1); }
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.SetAttacker))]
    internal static class StaffXpReactCasterPatch { private static void Postfix(StatusEffect __instance, Character attacker) => StaffMagicXpContext.ReactionCaster(__instance, attacker); }
    [HarmonyPatch(typeof(SE_React), nameof(SE_React.OnDamaged))]
    internal static class StaffXpReactDamagePatch
    {
        private static void Prefix(SE_React __instance, HitData hit, out StaffMagicXpContext.Scope __state) => StaffMagicXpContext.Begin(__instance, hit, out __state);
        private static Exception Finalizer(StaffMagicXpContext.Scope __state, Exception __exception)
        { if (__state.Captured) StaffMagicXpContext.Current = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
    internal static class StaffXpSpawnChildrenPatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo method &&
                    method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate" && method.IsGenericMethod &&
                    method.GetGenericArguments()[0] == typeof(GameObject))
                { yield return new CodeInstruction(OpCodes.Dup); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StaffMagicXpContext), nameof(StaffMagicXpContext.CaptureSpawn))); }
            }
        }
    }
    [HarmonyPatch(typeof(Aoe), "CustomFixedUpdate")]
    internal static class StaffXpAoeChainPatch
    {
        private static void Prefix(Aoe __instance, out StaffMagicXpContext.Scope __state) => StaffMagicXpContext.Begin(__instance, out __state);
        private static Exception Finalizer(StaffMagicXpContext.Scope __state, Exception __exception)
        { if (__state.Captured) StaffMagicXpContext.Current = __state.Previous; return __exception; }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => StaffXpSpawnChildrenPatch.Transpiler(instructions);
    }
}
