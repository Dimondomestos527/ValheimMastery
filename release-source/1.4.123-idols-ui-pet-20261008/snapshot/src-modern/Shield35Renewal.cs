using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Shield35Renewal
    {
        internal const float Range = 10f, CapacityPerPulse = .08f, TimePerPulse = 2f;
        private static Player Owner;
        private static ItemDrop.ItemData Weapon;
        private static bool Pending, Active, WaitForRelease;
        private static float Held, Clock, Cost;
        private static int RenewalPulses;
        private static float LastRenewal = float.NegativeInfinity;
        private static readonly Collider[] Hits = new Collider[96];
        private static readonly HashSet<Character> Seen = new HashSet<Character>();
        private sealed class Receipts
        {
            internal readonly Dictionary<ZDOID, long> Last = new Dictionary<ZDOID, long>();
            internal readonly Queue<ZDOID> Order = new Queue<ZDOID>();
        }
        private static readonly ConditionalWeakTable<Character, Receipts> History = new ConditionalWeakTable<Character, Receipts>();
        internal static bool Channeling(Humanoid player) => Active && player == Owner;
        internal static bool IsStaff(ItemDrop.ItemData item) => PerkRuntimeService.ItemPrefabName(item) == "StaffShield";
        internal static SE_Shield IntactShield(Character character)
        {
            var effects = character?.GetSEMan()?.GetStatusEffects();
            if (effects == null || character.IsDead()) return null;
            foreach (StatusEffect effect in effects)
                if (effect is SE_Shield shield && shield.m_levelUpSkillOnBreak == Skills.SkillType.BloodMagic &&
                    shield.m_totalAbsorbDamage > 0f && float.IsFinite(shield.m_damage) &&
                    shield.m_damage <= shield.m_totalAbsorbDamage &&
                    (shield.m_ttl <= 0f || shield.m_time < shield.m_ttl)) return shield;
            return null;
        }
        internal static float HealthCost(Player player, ItemDrop.ItemData weapon)
        {
            if (ShieldRenewalCostRules.ResetDue(LastRenewal, Time.time)) RenewalPulses = 0;
            return ShieldRenewalCostRules.Cost(player.GetMaxHealth(), RenewalPulses);
        }

        internal static void CancelForSecondary(Player player)
        { if (Owner == player && (Pending || Active)) Stop(player.m_attackHold || player.m_attack); }
        private static void Stop(bool wait = false)
        {
            if (Active && Owner != null) Owner.GetComponent<Shield35ChannelPose>()?.Finish();
            Pending = Active = false; Held = Clock = Cost = 0f; Weapon = null;
            WaitForRelease = wait;
        }
        internal static bool Input(Player player, float dt)
        {
            if (player != Player.m_localPlayer) return true;
            if (Owner != player) { Stop(); Owner = player; RenewalPulses = 0; LastRenewal = float.NegativeInfinity; }
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            // Mandatory nonmatching-weapon pass-through; no global input lock.
            if (!MagicSkillPassives.OwnerReady(player) || !IsStaff(weapon) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.BloodMagic, 35))
            { Stop(); return true; }
            bool held = player.m_attackHold || player.m_attack;
            if (WaitForRelease)
            { if (!held) WaitForRelease = false; return false; }
            if (player.IsTeleporting() || player.IsStaggering() || player.InDodge() || player.m_blocking ||
                player.InMinorAction() || player.m_secondaryAttack || player.m_secondaryAttackHold ||
                (Weapon != null && Weapon != weapon))
            { Stop(held); return true; }
            SE_Shield own = IntactShield(player);
            if (own == null)
            {
                bool wasChannel = Active || Pending;
                Stop(wasChannel && held);
                return !wasChannel; // Losing a shield cannot fall through into a paid recast.
            }
            if (!Active && !Pending)
            {
                if (!held || player.InAttack()) return true;
                Pending = true; Weapon = weapon;
            }
            player.m_queuedAttackTimer = 0f;
            if (!held)
            {
                bool tap = Pending && !Active;
                Stop();
                if (tap) player.StartAttack(null, false);
                return false;
            }
            Held += Mathf.Clamp(dt, 0f, .1f);
            if (!Active)
            {
                if (Held < .3f) return false;
                Cost = HealthCost(player, weapon);
                if (player.GetHealth() <= Cost + 1f) { Stop(true); return false; }
                Active = true; Pending = false; Clock = 0f;
                Shield35ChannelPose pose = player.GetComponent<Shield35ChannelPose>();
                if (pose == null) pose = player.gameObject.AddComponent<Shield35ChannelPose>();
                pose.Begin(weapon.m_shared.m_attack.m_attackAnimation);
            }
            Clock += Mathf.Clamp(dt, 0f, .1f);
            if (Clock >= 1f)
            {
                Clock -= 1f;
                Cost = HealthCost(player, weapon);
                if (player.GetHealth() <= Cost + 1f || IntactShield(player) == null) { Stop(true); return false; }
                // UseHealth bypasses damage/shield/proc hooks. Never drain eitr or
                // stamina, and never start a native Attack for a renewal pulse.
                player.UseHealth(Cost);
                if (RenewalPulses < int.MaxValue) RenewalPulses++;
                LastRenewal = Time.time;
                Pulse(player);
            }
            return false;
        }
        private static void Pulse(Player caster)
        {
            float level = PerkRuntimeService.GetActualSkillLevel(caster, Skills.SkillType.BloodMagic);
            long stamp = ZNet.instance.GetTime().Ticks;
            ZPackage packet = new ZPackage(); packet.Write(caster.GetZDOID()); packet.Write(level); packet.Write(stamp);
            int count = Physics.OverlapSphereNonAlloc(caster.GetCenterPoint(), Range, Hits,
                LayerMask.GetMask("character", "character_net"));
            Seen.Clear();
            // Include self even if an unusual collision-layer setting hides it.
            Deliver(caster, caster, packet); Seen.Add(caster);
            int sent = 1;
            for (int i = 0; i < count; i++)
            {
                Character target = Hits[i]?.GetComponentInParent<Character>(); Hits[i] = null;
                if (target == null || !Seen.Add(target) || target.IsDead() || sent >= 32 ||
                    (!target.IsPlayer() && !target.IsTamed()) || BaseAI.IsEnemy(caster, target) ||
                    (target.GetCenterPoint() - caster.GetCenterPoint()).sqrMagnitude > Range * Range) continue;
                Deliver(caster, target, packet); sent++;
            }
            Seen.Clear();
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Shield35Renew] paidHP=" + Cost.ToString("0.0") + " recipients=" + sent);
        }
        private static void Deliver(Player caster, Character target, ZPackage packet)
        {
            if (target.m_nview?.IsValid() != true) return;
            if (target.m_nview.IsOwner()) Receive(target, caster.m_nview.GetZDO().GetOwner(), new ZPackage(packet.GetArray()));
            else target.m_nview.InvokeRPC("VM_Shield35Renew", packet);
        }
        internal static void Register(Character target)
        {
            if (target.m_nview?.IsValid() != true) return;
            target.m_nview.Register<ZPackage>("VM_Shield35Renew", (sender, packet) => Receive(target, sender, packet));
            target.m_nview.Register<bool>("VM_Shield35RenewVisual", (sender, castSound) =>
            {
                if (target.m_nview.IsValid() && target.m_nview.GetZDO().GetOwner() == sender && !target.IsDead())
                {
                    Shield35HealingVisual.Pulse(target);
                    if (castSound) PlayChannelSound(target.GetCenterPoint());
                }
            });
        }
        private static void PlayChannelSound(Vector3 point)
        {
            var item = NativePerkAssetResolver.Resolve("StaffShield")?.GetComponent<ItemDrop>()?.m_itemData;
            if (item?.m_shared == null) return;
            if (PlaySoundList(item.m_shared.m_attack.m_triggerEffect, point)) return;
            PlaySoundList(item.m_shared.m_triggerEffect, point);
        }
        private static bool PlaySoundList(EffectList effects, Vector3 point)
        {
            if (effects?.m_effectPrefabs == null) return false;
            foreach (var effect in effects.m_effectPrefabs)
                if (effect.m_enabled && effect.m_prefab != null &&
                    PerkAudioService.PlayPrefab("shield35_channel", effect.m_prefab, point, .95f, .25f, .95f)) return true;
            return false;
        }
        private static void Receive(Character target, long sender, ZPackage packet)
        {
            if (MasteryPlugin.Settings.Enabled.Value != true || target.m_nview?.IsOwner() != true ||
                packet == null || packet.Size() > 64 || target.IsDead() ||
                (!target.IsPlayer() && !target.IsTamed()) || ZNet.instance == null) return;
            try
            {
                ZDOID sourceId = packet.ReadZDOID(); float level = packet.ReadSingle(); long stamp = packet.ReadLong();
                ZDO source = ZDOMan.instance?.GetZDO(sourceId);
                Player caster = ZNetScene.instance?.FindInstance(sourceId)?.GetComponent<Player>();
                long now = ZNet.instance.GetTime().Ticks;
                if (source == null || source.GetOwner() != sender || caster == null || caster.IsDead() ||
                    !OwnerSkillAuthority.Valid(level) || level < 35f || stamp < now - TimeSpan.TicksPerSecond * 3L ||
                    stamp > now + TimeSpan.TicksPerSecond ||
                    source.GetInt(ZDOVars.s_rightItem, 0) != "StaffShield".GetStableHashCode() ||
                    (target.GetCenterPoint() - source.GetPosition()).sqrMagnitude > (Range + 1f) * (Range + 1f) ||
                    BaseAI.IsEnemy(caster, target)) return;
                SE_Shield shield = IntactShield(target);
                if (shield == null) return; // Never add/reset a status or revive a broken shield.
                Receipts receipts = History.GetOrCreateValue(target);
                bool known = receipts.Last.TryGetValue(sourceId, out long previous);
                if (known && stamp <= previous) return;
                receipts.Last[sourceId] = stamp;
                if (!known) receipts.Order.Enqueue(sourceId);
                while (receipts.Order.Count > 64) receipts.Last.Remove(receipts.Order.Dequeue());
                shield.m_damage = Mathf.Max(0f, shield.m_damage - shield.m_totalAbsorbDamage * CapacityPerPulse);
                if (shield.m_ttl > 0f) shield.m_time = Mathf.Max(0f, shield.m_time - TimePerPulse);
                // Recipient owner broadcasts only accepted renewal cosmetics.
                target.m_nview.InvokeRPC(ZNetView.Everybody, "VM_Shield35RenewVisual", sourceId == target.GetZDOID());
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Shield35Renew] accepted=" + target.gameObject.name +
                        " remaining=" + (shield.m_totalAbsorbDamage - shield.m_damage).ToString("0.0") +
                        " seconds=" + (shield.m_ttl - shield.m_time).ToString("0.0"));
            }
            catch (Exception error) { MasteryPlugin.Log.LogWarning("[Shield35Renew] Invalid packet: " + error.GetType().Name); }
        }
    }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Shield35RenewInputPatch
    { private static bool Prefix(Player __instance, float dt) => Shield35Renewal.Input(__instance, dt); }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class Shield35ChannelNoCastPatch
    { private static bool Prefix(Humanoid __instance) => !Shield35Renewal.Channeling(__instance); }

    // Native shield wind-up, held using the real retargeted Mecanim state. No
    // shared prefab edits, SampleAnimation, global animator-speed change or Attack.
    internal sealed class Shield35ChannelPose : MonoBehaviour
    {
        private Player Player;
        private string Trigger;
        private int Rest, State;
        private float Started, Length, StopAt;
        private bool Holding;
        private readonly List<AnimatorClipInfo> Clips = new List<AnimatorClipInfo>(4);
        private void Awake() => Player = GetComponent<Player>();
        internal void Begin(string trigger)
        {
            Trigger = trigger; State = 0; Started = Time.time; Holding = true;
            Rest = Player.m_animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            Player.m_zanim.SetTrigger(trigger);
        }
        internal void Finish()
        {
            if (!Holding) return;
            Holding = false;
            if (Player?.m_animator != null)
            {
                Player.m_animator.ResetTrigger(Trigger);
                if (Rest != 0) Player.m_animator.CrossFade(Rest, .15f, 0);
            }
        }
        private void LateUpdate()
        {
            if (!Holding) return;
            if (!Shield35Renewal.Channeling(Player) || Player.IsDead() || Player.IsTeleporting()) { Finish(); return; }
            Animator animator = Player.m_animator;
            if (animator == null) return;
            AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0), next = animator.GetNextAnimatorStateInfo(0);
            bool transition = animator.IsInTransition(0) && next.tagHash == Humanoid.s_animatorTagAttack;
            if (State == 0 && (transition || current.tagHash == Humanoid.s_animatorTagAttack))
            {
                State = transition ? next.fullPathHash : current.fullPathHash;
                Clips.Clear();
                if (transition) animator.GetNextAnimatorClipInfo(0, Clips); else animator.GetCurrentAnimatorClipInfo(0, Clips);
                AnimationClip clip = Clips.Count > 0 ? Clips[0].clip : null;
                Length = clip != null ? Mathf.Max(.05f, clip.length) : 1f;
                StopAt = Length * .45f;
                if (clip != null)
                    foreach (AnimationEvent evt in clip.events)
                        if (evt.functionName == "OnAttackTrigger" && evt.time > .02f) StopAt = Mathf.Min(StopAt, evt.time * .9f);
            }
            if (State == 0) return;
            float position = Mathf.Min(StopAt, (Time.time - Started) * 1.5f) / Length;
            animator.Play(State, 0, position); animator.Update(0f);
        }
        private void OnDisable() => Finish();
    }
}
