using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class BloodMagicCombatXp
    {
        private const string Rpc = "VM_BloodCombatXpV2", SequenceKey = "vm.bloodxp.seq",
            ShieldAuthorKey = "vm.bloodxp.shield.author", ShieldCasterKey = "vm.bloodxp.shield.caster",
            CapacityKey = "vm.bloodxp.shield.capacity", FractionKey = "vm.bloodxp.shield.fraction",
            WeightedKey = "vm.bloodxp.shield.weight", ReceiptKey = "valheim_mastery.bloodxp.receipts.v1";
        private const string SkeletonAuthorKey = "vm.skeleton.master", SkeletonCasterKey = "vm.bloodxp.skeleton.caster",
            IssuerKey = "vm.bloodxp.issuer", PreviousIssuerKey = "vm.bloodxp.previousissuer", PreviousLastKey = "vm.bloodxp.previouslast", PreviousLowerKey = "vm.bloodxp.previouslower";
        private static readonly List<Delivery> Outgoing = new List<Delivery>();
        private static ZNet Session;
        private static long SessionWorld;
        private static float NextOutgoing, ProofWindow;
        private static readonly HashSet<ZDOID> RequestedProofs = new HashSet<ZDOID>();
        internal sealed class AttackScope
        {
            internal Character Source, Target;
            internal int TargetTier;

        }
        [ThreadStatic] internal static AttackScope CurrentAttack;
        private sealed class ShieldState
        {
            internal long Author;
            internal ZDOID Caster;
            internal BloodMagicXpRules.Absorption Credit;
        }
        private sealed class Delivery
        {
            internal long Sender, World, Author, Serial, Stamp;
            internal ZDOID Source, Caster;
            internal int Kind;
            internal string Target = "action";
            internal float Raw, Tier, Expires;
        }
        private sealed class RecipientState
        {
            internal BloodMagicXpRules.Receipts Receipts;
            internal float NextCheck;
            internal readonly List<Delivery> Pending = new List<Delivery>();
        }
        private static readonly ConditionalWeakTable<SE_Shield, ShieldState> Shields = new ConditionalWeakTable<SE_Shield, ShieldState>();
        private static readonly ConditionalWeakTable<Player, RecipientState> Recipients = new ConditionalWeakTable<Player, RecipientState>();
        private static bool Enabled => MasteryPlugin.Settings.Enabled.Value;
        private static bool IsSkeleton(Character source) => source?.m_nview?.IsValid() == true && source.IsTamed() &&
            (source.m_nview.GetZDO().GetPrefab() == "Skeleton_Friendly".GetStableHashCode() ||
             source.m_nview.GetZDO().GetPrefab() == "Skeleton_Friendly_Archer".GetStableHashCode());
        internal static bool Enemy(Character source, Character target) => source != null && target != null &&
            target != source && !target.IsDead() && !target.IsPlayer() && !target.IsTamed() && BaseAI.IsEnemy(source, target);
        internal struct AttackFrame { internal bool Captured; internal AttackScope Previous; }
        internal static void BeginAttack(Character source, out AttackFrame frame)
        {
            frame = new AttackFrame { Captured = true, Previous = CurrentAttack };
            CurrentAttack = Enabled && IsSkeleton(source) && source.m_nview.IsOwner()
                ? new AttackScope { Source = source } : null;
        }
        internal static void ObserveTarget(Character target, HitData hit)
        {
            AttackScope scope = CurrentAttack;
            if (scope?.Source == null || scope.Target != null || hit == null || hit.GetAttacker() != scope.Source ||
                PerkRuntimeService.IsPerkGenerated(hit) || !Enemy(scope.Source, target)) return;
            scope.Target = target; scope.TargetTier = TierDatabase.GetTargetTier(target);
        }
        internal static bool RaiseSkeleton(Character source, float factor)
        {
            if (!Enabled || !IsSkeleton(source)) return true;
            long author = source.m_nview.GetZDO().GetLong(SkeletonAuthorKey, 0);
            if (author == 0) return true; // Legacy/unmanaged native summon: retain its original grant.
            // Suppress only this native creature-to-follow-target grant; its author
            // receives exactly one replacement when native confirms a skill event.
            AttackScope scope = CurrentAttack;
            if (scope?.Source != source || scope.Target == null || !source.m_nview.IsOwner()) return false;
            Tameable tameable = source.GetComponent<Tameable>();
            if (tameable == null || tameable.m_levelUpOwnerSkill != Skills.SkillType.BloodMagic) return true;
            ZDOID caster = source.m_nview.GetZDO().GetZDOID(SkeletonCasterKey);
            ZDOID current = FindAuthor(author);
            if (current != ZDOID.None && current != caster)
            { caster = current; source.m_nview.GetZDO().Set(SkeletonCasterKey, caster); }
            if (caster != ZDOID.None) Send(source, caster, author, 1, factor * tameable.m_levelUpFactor, scope.TargetTier);
            return false;
        }
        internal static ZDOID FindAuthor(long author)
        {
            if (author == 0) return ZDOID.None;
            if (Player.m_localPlayer?.GetPlayerID() == author) return Player.m_localPlayer.GetZDOID();
            foreach (Player player in Player.GetAllPlayers()) if (player != null && player.GetPlayerID() == author) return player.GetZDOID();
            if (ZNet.instance != null)
                foreach (ZNet.PlayerInfo player in ZNet.instance.GetPlayerList())
                    if (ZDOMan.instance?.GetZDO(player.m_characterID)?.GetLong(ZDOVars.s_playerID, 0) == author) return player.m_characterID;
            return ZDOID.None;
        }
        internal static void BindSkeleton(MonsterAI ai, GameObject follow)
        {
            Player player = follow?.GetComponent<Player>(); Character source = ai?.m_character;
            if (player != null && IsSkeleton(source) && source.m_nview.IsOwner() &&
                source.m_nview.GetZDO().GetLong(SkeletonAuthorKey, 0) == player.GetPlayerID())
                source.m_nview.GetZDO().Set(SkeletonCasterKey, player.GetZDOID());
        }
        internal static void BindSkeletonBirth(SpawnAbility ability, GameObject spawned)
        {
            Player player = ability?.m_owner as Player; Character source = spawned?.GetComponent<Character>();
            if (player != null && IsSkeleton(source) && source.m_nview.IsOwner() &&
                source.m_nview.GetZDO().GetLong(SkeletonAuthorKey, 0) == player.GetPlayerID())
                source.m_nview.GetZDO().Set(SkeletonCasterKey, player.GetZDOID());
        }
        internal static void BindShield(StatusEffect effect, Character attacker)
        {
            if (!Enabled || !(effect is SE_Shield shield) || shield.m_levelUpSkillOnBreak != Skills.SkillType.BloodMagic ||
                !(attacker is Player caster) || caster.IsDead() || caster.m_nview?.IsValid() != true ||
                shield.m_character?.m_nview?.IsOwner() != true || BaseAI.IsEnemy(caster, shield.m_character) ||
                !BloodMagicXpRules.Finite(shield.m_totalAbsorbDamage) || shield.m_totalAbsorbDamage <= 0f) return;
            Shields.Remove(shield);
            var state = new ShieldState { Author = caster.GetPlayerID(), Caster = caster.GetZDOID(),
                Credit = new BloodMagicXpRules.Absorption(shield.m_totalAbsorbDamage) };
            if (state.Author == 0) return;
            Shields.Add(shield, state);
            ZDO zdo = shield.m_character.m_nview.GetZDO();
            zdo.Set(ShieldAuthorKey, state.Author); zdo.Set(ShieldCasterKey, state.Caster);
            zdo.Set(CapacityKey, state.Credit.Capacity); PersistCredit(zdo, state);
        }
        private static ShieldState Shield(SE_Shield shield)
        {
            if (shield == null || shield.m_levelUpSkillOnBreak != Skills.SkillType.BloodMagic) return null;
            if (Shields.TryGetValue(shield, out ShieldState state)) return state;
            ZDO zdo = shield.m_character?.m_nview?.GetZDO();
            if (zdo == null) return null;
            float capacity = zdo.GetFloat(CapacityKey, 0f), fraction = zdo.GetFloat(FractionKey, 0f), weight = zdo.GetFloat(WeightedKey, 0f);
            long author = zdo.GetLong(ShieldAuthorKey, 0); ZDOID caster = zdo.GetZDOID(ShieldCasterKey);
            if (author == 0 || caster == ZDOID.None || !BloodMagicXpRules.Finite(capacity) || capacity <= 0f ||
                !BloodMagicXpRules.Finite(fraction) || fraction < 0f || fraction >= .1f ||
                !BloodMagicXpRules.Finite(weight) || weight < fraction || weight > fraction * 8f + 1e-6f) return null;
            state = new ShieldState { Author = author, Caster = caster,
                Credit = new BloodMagicXpRules.Absorption(capacity) { Fraction = fraction, WeightedTier = weight } };
            Shields.Add(shield, state); return state;
        }
        private static void PersistCredit(ZDO zdo, ShieldState state)
        { zdo.Set(FractionKey, (float)state.Credit.Fraction); zdo.Set(WeightedKey, (float)state.Credit.WeightedTier); }
        internal static bool ManagedShield(SE_Shield shield) => Enabled && Shield(shield) != null;
        internal static void Absorb(SE_Shield shield, HitData hit, float incoming, float remaining)
        {
            if (!Enabled || shield?.m_character?.m_nview?.IsOwner() != true || hit == null ||
                PerkRuntimeService.IsPerkGenerated(hit) || !Enemy(shield.m_character, hit.GetAttacker())) return;
            ShieldState state = Shield(shield);
            if (state == null) return;
            ZDOID current = FindAuthor(state.Author);
            if (current != ZDOID.None && current != state.Caster)
            { state.Caster = current; shield.m_character.m_nview.GetZDO().Set(ShieldCasterKey, current); }
            BloodMagicXpRules.Credit credit = state.Credit.Add(incoming, remaining, TierDatabase.GetTargetTier(hit.GetAttacker()));
            PersistCredit(shield.m_character.m_nview.GetZDO(), state);
            if (credit.Raw > 0f) Send(shield.m_character, state.Caster, state.Author, 2, credit.Raw, credit.TargetTier);
        }
        internal static void Register(Character character)
        {
            if (!(character is Player player) || player.m_nview?.IsValid() != true) return;
            player.m_nview.Register<ZPackage>(Rpc, (sender, package) => Receive(player, sender, package));
        }
        private static void EnsureSession()
        {
            long world = ZNet.instance != null && ZNet.instance.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
            if (Session == ZNet.instance && SessionWorld == world) return;
            Session = ZNet.instance; SessionWorld = world; Outgoing.Clear(); NextOutgoing = ProofWindow = 0f; RequestedProofs.Clear();
        }
        private static void RequestProof(ZDOID id)
        {
            if (Time.time >= ProofWindow) { RequestedProofs.Clear(); ProofWindow = Time.time + .25f; }
            if (id == ZDOID.None || RequestedProofs.Count >= 8 || !RequestedProofs.Add(id)) return;
            ZDOMan.instance?.RequestZDO(id); // Global maximum32 unique requests/s, shared by both queues.
        }
        private static void Send(Character source, ZDOID caster, long author, int kind, float raw, float tier) => SendStaff(source, caster, author, kind, raw, tier, "action");
        internal static bool SendStaff(Character source, ZDOID caster, long author, int kind, float raw, float tier, string target)
        {
            if (source?.m_nview?.IsOwner() != true || caster == ZDOID.None || author == 0 || ZNet.instance == null || ZNet.instance.GetWorld() == null ||
                !ValidValues(kind, raw, tier) || !StaffMagicXpRules.Target(target)) return false;
            EnsureSession();
            ZDO zdo = source.m_nview.GetZDO(); long serial = zdo.GetLong(SequenceKey, 0), issuer = zdo.GetOwner();
            if (serial < 0 || serial == long.MaxValue) return false;
            // Keep one previous owner interval so its already-issued reliable RPCs
            // remain verifiable after transfer. Neither issuer can mint future serials.
            if (zdo.GetLong(IssuerKey, 0) != issuer)
            {
                zdo.Set(PreviousIssuerKey, zdo.GetLong(IssuerKey, 0));
                zdo.Set(PreviousLowerKey, zdo.GetLong(PreviousLastKey, 0));
                zdo.Set(PreviousLastKey, serial); zdo.Set(IssuerKey, issuer);
            }
            zdo.Set(SequenceKey, ++serial);
            var value = new Delivery { Sender = issuer, World = SessionWorld, Source = source.GetZDOID(), Caster = caster,
                Author = author, Serial = serial, Stamp = ZNet.instance.GetTime().Ticks, Kind = kind, Raw = raw, Tier = tier, Target = target, Expires = Time.time + 10f };
            if (Dispatch(value)) return true;
            if (Outgoing.Count >= 64) return false;
            Outgoing.Add(value); return true;
        }
        private static bool Dispatch(Delivery value)
        {
            var data = new ZPackage(); data.Write(value.World); data.Write(value.Source); data.Write(value.Caster);
            data.Write(value.Author); data.Write(value.Serial); data.Write(value.Stamp); data.Write(value.Kind); data.Write(value.Raw); data.Write(value.Tier); data.Write(value.Target);
            Player local = Player.m_localPlayer;
            if (local != null && local.GetZDOID() == value.Caster && local.m_nview?.IsOwner() == true)
            { Receive(local, value.Sender, data); return true; }
            ZDO destination = ZDOMan.instance?.GetZDO(value.Caster);
            if (destination == null) { RequestProof(value.Caster); return false; }
            if (destination.GetLong(ZDOVars.s_playerID, 0) != value.Author || destination.GetOwner() == 0 || ZRoutedRpc.instance == null)
            { RequestProof(value.Caster); return false; }
            ZRoutedRpc.instance.InvokeRoutedRPC(destination.GetOwner(), value.Caster, Rpc, data); return true;
        }
        internal static void TickOutgoing()
        {
            EnsureSession();
            if (!Enabled || ZNet.instance == null || Outgoing.Count == 0 || Time.time < NextOutgoing) return;
            NextOutgoing = Time.time + .25f;
            for (int i = Outgoing.Count - 1; i >= 0; --i)
            {
                Delivery value = Outgoing[i];
                if (Time.time > value.Expires || value.World != SessionWorld || Dispatch(value)) Outgoing.RemoveAt(i);
            }
        }
        private static bool ValidValues(int kind, float raw, float tier) => StaffMagicXpRules.Valid(kind, raw, tier);
        private static RecipientState Recipient(Player player) => Recipients.GetValue(player, p => new RecipientState
        { Receipts = BloodMagicXpRules.Receipts.Import(p.m_customData.TryGetValue(ReceiptKey, out string saved) ? saved : "") });
        private static void Receive(Player player, long sender, ZPackage data)
        {
            if (!Enabled || player != Player.m_localPlayer || player.m_nview?.IsOwner() != true || data == null || data.Size() > 512 || ZNet.instance == null || ZNet.instance.GetWorld() == null) return;
            try
            {
                data.SetPos(0);
                var value = new Delivery { Sender = sender, World = data.ReadLong(), Source = data.ReadZDOID(),
                    Caster = data.ReadZDOID(), Author = data.ReadLong(), Serial = data.ReadLong(), Stamp = data.ReadLong(),
                    Kind = data.ReadInt(), Raw = data.ReadSingle(), Tier = data.ReadSingle(), Target = data.ReadString(), Expires = Time.time + 10f };
                if (data.GetPos() != data.Size() || !Basic(player, value)) return;
                RecipientState state = Recipient(player);
                if (Ready(value)) Apply(player, state, value);
                else if (state.Pending.Count < 64)
                {
                    foreach (Delivery pending in state.Pending) if (pending.Source == value.Source && pending.Serial == value.Serial) return;
                    state.Pending.Add(value); // Native ZDO author metadata can arrive after its RPC.
                    RequestProof(value.Source);
                }
            }
            catch (Exception error) { if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogWarning("[BloodXP] rejected " + error.GetType().Name); }
        }
        private static bool Basic(Player player, Delivery value)
        {
            long now = ZNet.instance.GetTime().Ticks;
            return value.World == ZNet.instance.GetWorldUID() && value.Caster == player.GetZDOID() &&
                value.Author != 0 && value.Author == player.GetPlayerID() && value.Source != ZDOID.None && value.Serial > 0 &&
                value.Stamp >= now - TimeSpan.TicksPerSecond * 10L && value.Stamp <= now + TimeSpan.TicksPerSecond &&
                ValidValues(value.Kind, value.Raw, value.Tier) && StaffMagicXpRules.Target(value.Target);
        }
        private static bool Ready(Delivery value)
        {
            ZDO source = ZDOMan.instance?.GetZDO(value.Source);
            if (source == null || value.Serial > source.GetLong(SequenceKey, 0)) return false;
            long boundary = source.GetLong(PreviousLastKey, 0);
            bool issuer = source.GetLong(IssuerKey, 0) == value.Sender && value.Serial > boundary;
            bool previous = source.GetLong(PreviousIssuerKey, 0) != 0 && source.GetLong(PreviousIssuerKey, 0) == value.Sender &&
                value.Serial > source.GetLong(PreviousLowerKey, 0) && value.Serial <= boundary;
            if (!issuer && !previous) return false;
            if (value.Kind == 1) return (source.GetPrefab() == "Skeleton_Friendly".GetStableHashCode() ||
                source.GetPrefab() == "Skeleton_Friendly_Archer".GetStableHashCode()) &&
                source.GetLong(SkeletonAuthorKey, 0) == value.Author;
            if (value.Kind == 2) return source.GetLong(ShieldAuthorKey, 0) == value.Author && source.GetZDOID(ShieldCasterKey) == value.Caster;
            return StaffMagicXpContext.Proof(source, value.Kind, value.Author, value.Caster, value.World) &&
                (value.Kind != StaffMagicXpRules.Roots || StaffMagicXpContext.RootProof(source, value.Raw));
        }
        private static void Apply(Player player, RecipientState state, Delivery value)
        {
            if (!Enabled || player != Player.m_localPlayer || player.m_nview?.IsValid() != true || !player.m_nview.IsOwner()) return;
            string key = value.World.ToString(CultureInfo.InvariantCulture) + "_" + value.Source.ToString();
            if (!state.Receipts.Accept(key, value.Serial)) return;
            // Native direct Skills semantics: no additional Player-level rested/food
            // multiplier; the shared RaiseSkill hook and world skill rate run once.
            if (value.Kind <= 2)
            {
                using (ExperienceContext.BeginBloodCombat(player, value.Tier, value.Kind == 1 ? "StaffSkeleton" : "StaffShield"))
                    player.GetSkills().RaiseSkill(Skills.SkillType.BloodMagic, value.Raw);
                return;
            }
            Skills.SkillType skill = StaffMagicXpRules.Skill(value.Kind);
            if (PerkRuntimeService.GetActualSkillLevel(player, skill) >= 100f) return;
            using (ExperienceContext.BeginStaffCombat(player, skill, StaffMagicXpRules.Staff(value.Kind), value.Tier, value.Target, value.Kind == StaffMagicXpRules.Spirit))
            {
                // Native direct AoE/return calls use Player modifiers; summoned/reaction
                // credits use the established direct-Skills transport semantics.
                StaffMagicXpContext.Frame previous = StaffMagicXpContext.Current;
                try
                {
                    StaffMagicXpContext.Current = null;
                    if (value.Kind >= 100 || value.Kind == StaffMagicXpRules.AhriReturn) player.RaiseSkill(skill, value.Raw);
                    else player.GetSkills().RaiseSkill(skill, value.Raw);
                }
                finally { StaffMagicXpContext.Current = previous; }
            }
        }
        internal static void Tick(Player player)
        {
            if (!Enabled || player != Player.m_localPlayer || ZNet.instance == null || ZNet.instance.GetWorld() == null || !Recipients.TryGetValue(player, out RecipientState state)) return;
            if (player.m_nview?.IsValid() != true || !player.m_nview.IsOwner()) { state.Pending.Clear(); return; }
            if (state.Pending.Count == 0 || Time.time < state.NextCheck) return;
            state.NextCheck = Time.time + .1f;
            for (int i = state.Pending.Count - 1; i >= 0; --i)
            {
                Delivery value = state.Pending[i];
                if (!Basic(player, value) || Time.time > value.Expires) { state.Pending.RemoveAt(i); continue; }
                if (Ready(value)) { state.Pending.RemoveAt(i); Apply(player, state, value); }
                else RequestProof(value.Source);
            }
        }
        internal static void Save(Player player)
        {
            if (player == Player.m_localPlayer && Recipients.TryGetValue(player, out RecipientState state))
                player.m_customData[ReceiptKey] = state.Receipts.Export();
        }
    }
    [HarmonyPatch(typeof(Skeleton35Travel), nameof(Skeleton35Travel.Tag))]
    internal static class BloodSkeletonXpCasterPatch
    { private static void Postfix(MonsterAI ai, GameObject follow) => BloodMagicCombatXp.BindSkeleton(ai, follow); }
    [HarmonyPatch(typeof(Skeleton35Travel), nameof(Skeleton35Travel.TagSpawnedSummon))]
    internal static class BloodSkeletonXpBirthCasterPatch
    { private static void Postfix(SpawnAbility ability, GameObject spawned) => BloodMagicCombatXp.BindSkeletonBirth(ability, spawned); }
    [HarmonyPatch(typeof(ZNet), "Update")]
    internal static class BloodMagicXpOutgoingPatch
    { private static void Postfix() => BloodMagicCombatXp.TickOutgoing(); }
    [HarmonyPatch(typeof(StatusEffect), nameof(StatusEffect.SetAttacker))]
    internal static class BloodShieldXpCasterPatch
    { private static void Postfix(StatusEffect __instance, Character attacker) => BloodMagicCombatXp.BindShield(__instance, attacker); }
    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
    internal static class BloodShieldXpAbsorptionPatch
    {
        internal struct State { internal float Incoming, Remaining; }
        private static void Prefix(SE_Shield __instance, HitData hit, out State __state) => __state = new State
        { Incoming = hit?.GetTotalDamage() ?? 0f, Remaining = __instance.m_totalAbsorbDamage - __instance.m_damage };
        private static void Postfix(SE_Shield __instance, HitData hit, State __state) =>
            BloodMagicCombatXp.Absorb(__instance, hit, __state.Incoming, __state.Remaining);
    }
    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.IsDone))]
    internal static class BloodShieldXpBreakPatch
    {
        internal struct State { internal bool Changed; internal Skills.SkillType Skill; }
        private static void Prefix(SE_Shield __instance, out State __state)
        {
            __state = new State { Changed = BloodMagicCombatXp.ManagedShield(__instance), Skill = __instance.m_levelUpSkillOnBreak };
            if (__state.Changed) __instance.m_levelUpSkillOnBreak = Skills.SkillType.None;
        }
        private static Exception Finalizer(SE_Shield __instance, State __state, Exception __exception)
        { if (__state.Changed) __instance.m_levelUpSkillOnBreak = __state.Skill; return __exception; }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class BloodSkeletonXpMeleePatch
    {
        private static void Prefix(Attack __instance, out BloodMagicCombatXp.AttackFrame __state) => BloodMagicCombatXp.BeginAttack(__instance.m_character, out __state);
        private static Exception Finalizer(BloodMagicCombatXp.AttackFrame __state, Exception __exception)
        { if (__state.Captured) BloodMagicCombatXp.CurrentAttack = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class BloodSkeletonXpAreaPatch
    {
        private static void Prefix(Attack __instance, out BloodMagicCombatXp.AttackFrame __state) => BloodMagicCombatXp.BeginAttack(__instance.m_character, out __state);
        private static Exception Finalizer(BloodMagicCombatXp.AttackFrame __state, Exception __exception)
        { if (__state.Captured) BloodMagicCombatXp.CurrentAttack = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
    internal static class BloodSkeletonXpProjectilePatch
    {
        private static void Prefix(Projectile __instance, out BloodMagicCombatXp.AttackFrame __state) => BloodMagicCombatXp.BeginAttack(__instance.m_owner, out __state);
        private static Exception Finalizer(BloodMagicCombatXp.AttackFrame __state, Exception __exception)
        { if (__state.Captured) BloodMagicCombatXp.CurrentAttack = __state.Previous; return __exception; }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class BloodSkeletonXpTargetPatch
    { private static void Prefix(Character __instance, HitData hit) => BloodMagicCombatXp.ObserveTarget(__instance, hit); }
    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class BloodSkeletonXpRaisePatch
    { private static bool Prefix(Character __instance, float __1) => BloodMagicCombatXp.RaiseSkeleton(__instance, __1); }
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class BloodMagicXpRegisterPatch
    { private static void Postfix(Character __instance) => BloodMagicCombatXp.Register(__instance); }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class BloodMagicXpTickPatch
    { private static void Postfix(Player __instance) => BloodMagicCombatXp.Tick(__instance); }
    [HarmonyPatch(typeof(Player), "Save")]
    internal static class BloodMagicXpSavePatch
    { private static void Prefix(Player __instance) => BloodMagicCombatXp.Save(__instance); }
}
