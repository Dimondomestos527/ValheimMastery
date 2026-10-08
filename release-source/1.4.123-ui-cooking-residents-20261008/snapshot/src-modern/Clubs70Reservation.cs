#if MASTERY_CLUBS70_EXPERIMENT
using System.Runtime.CompilerServices;
using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Reserve actual stamina, never regenerate the unavailable portion. Native
    // food/stat maximum updates remain the source of the unreserved maximum.
    internal static class Clubs70Reservation
    {
        internal sealed class State
        {
            internal ItemDrop.ItemData Weapon;
            internal float Reserved, Arming, Expires;
            internal bool Charging, SuppressUntilBlockRelease;
            internal Attack PendingAttack;
            internal int Scene;
            internal ZDOID Character;
        }
        private static readonly ConditionalWeakTable<Player, State> States = new ConditionalWeakTable<Player, State>();
        private sealed class Charge { internal float Value; }
        private static readonly ConditionalWeakTable<Attack, Charge> Attacks = new ConditionalWeakTable<Attack, Charge>();
        internal sealed class SaveState { internal float Maximum, Current; }
        private static bool SameSession(Player player, State state) => state.Scene == (ZNetScene.instance?.GetInstanceID() ?? 0) &&
            state.Character == player.GetZDOID();
        internal static void Cancel(Player player)
        {
            if (player == null || !States.TryGetValue(player, out State state)) return;
            Release(player, state, true);
        }
        internal static SaveState BeforeSave(Player player)
        {
            if (player != Player.m_localPlayer || !States.TryGetValue(player, out State state) || state.Reserved <= 0f) return null;
            // Charge is deliberately ephemeral. Native saves retain the natural
            // maximum and refundable stamina, not a permanently reduced maximum.
            // Autosave must not cancel a live charge or change its current HUD.
            var saved = new SaveState { Maximum = player.m_maxStamina, Current = player.m_stamina };
            player.m_maxStamina += state.Reserved;
            if (!player.IsDead()) player.m_stamina = Mathf.Min(player.m_maxStamina, player.m_stamina + state.Reserved);
            return saved;
        }
        internal static void AfterSave(Player player, SaveState state)
        {
            if (player == null || state == null) return;
            player.m_maxStamina = state.Maximum; player.m_stamina = state.Current;
        }
        internal static float ChargeFraction(Player player)
        { return player != null && States.TryGetValue(player, out var state)
            ? ClubsReserveRules.Fraction(state.Reserved, player.m_maxStamina + state.Reserved,
                ClubWeaponClassService.Classify(state.Weapon) == ClubWeaponClass.Mace) : 0f; }
        internal static bool IsCharging(Player player)
        { return player != null && States.TryGetValue(player, out var state) && state.Charging; }
        internal static float ChargeFraction(Attack attack) => attack != null && Attacks.TryGetValue(attack, out var charge) ? charge.Value : 0f;
        internal static float Factor(Attack attack) => ChargeFraction(attack);
        internal static float InnerFactor(Attack attack) => 1f;
        internal static void Maximum(Player player, ref float maximum)
        {
            if (!States.TryGetValue(player, out var state) || state.Reserved <= 0f) return;
            // Native food values decay every update. A full reserve would exceed
            // the slightly smaller half-cap and previously cancel on the next tick.
            // Trim/refund ONLY the excess; retain ready state and its expiry.
            float fitted = ClubsReserveRules.Fit(state.Reserved, maximum,
                ClubWeaponClassService.Classify(state.Weapon) == ClubWeaponClass.Mace);
            float excess = state.Reserved - fitted;
            if (excess > 0f)
            {
                state.Reserved = fitted;
                player.m_maxStamina += excess;
                if (!player.IsDead()) player.AddStamina(excess);
            }
            maximum = Mathf.Max(0f, maximum - state.Reserved);
        }
        private static void Release(Player player, State state, bool refund)
        {
            float reserved = state.Reserved;
            if (reserved > 0f && MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[ClubsCharge] release actor=" + player.GetZDOID() +
                    " attack=" + (state.PendingAttack != null ? RuntimeHelpers.GetHashCode(state.PendingAttack) : 0) +
                    " reserve=" + reserved.ToString("F2") + " refund=" + refund);
            state.Reserved = state.Arming = state.Expires = 0f; state.Charging = false; state.Weapon = null; state.PendingAttack = null;
            state.SuppressUntilBlockRelease = true;
            player.m_maxStamina += reserved;
            if (refund && !player.IsDead()) player.AddStamina(reserved);
        }
        private static void Lock(Player player, State state)
        {
            if (state.Charging)
            {
                state.Charging = false; state.Expires = Time.time + 15f;
            }
            state.Arming = 0f;
        }
        internal static void Tick(Player player)
        {
            if (player != Player.m_localPlayer || player?.m_nview?.IsOwner() != true) return;
            State state = States.GetOrCreateValue(player);
            if (!SameSession(player, state))
            {
                Release(player, state, true);
                state.Scene = ZNetScene.instance?.GetInstanceID() ?? 0; state.Character = player.GetZDOID();
            }
            bool blockHeld = ZInput.GetButton("Block");
            if (!blockHeld)
            {
                state.SuppressUntilBlockRelease = false;
            }
            Clubs70ChargeHud.Show(player, state.Reserved, ClubsReserveRules.Capacity(player.m_maxStamina + state.Reserved,
                ClubWeaponClassService.Classify(state.Weapon) == ClubWeaponClass.Mace), state.Charging, state.Expires);
            ItemDrop.ItemData equipped = player.GetCurrentWeapon();
            bool bare = equipped == null || PerkRuntimeService.IsBareHands(equipped);
            if (!MagicSkillPassives.OwnerReady(player) || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70) ||
                (state.Weapon != null && !bare && equipped != state.Weapon))
            { if (state.Reserved > 0f) Release(player, state, true); else state.Arming = 0f; return; }
            ClubWeaponClass kind = ClubWeaponClassService.Classify(equipped);
            bool eligible = kind == ClubWeaponClass.Mace || kind == ClubWeaponClass.SledgeHammer;
            if (state.Reserved > 0f && !state.Charging && Time.time >= state.Expires)
            { Release(player, state, true); return; }
            float normalMax = player.m_maxStamina + state.Reserved;
            float capacity = ClubsReserveRules.Capacity(normalMax, kind == ClubWeaponClass.Mace);
            if (state.Reserved > 0f && state.Reserved >= capacity - .001f)
            {
                // A full reserve stays ready until it is used or expires. Do not
                // restart an empty charge loop while block remains held.
                Lock(player, state); state.SuppressUntilBlockRelease = true; return;
            }
            bool canCharge = eligible && player.IsBlocking() && !state.SuppressUntilBlockRelease &&
                !player.IsStaggering() && !player.InAttack() && !player.InDodge();
            if (!canCharge)
            { Lock(player, state); return; }
            // A partial reserve pauses while block is released and resumes
            // immediately on re-hold, retaining the existing 15-second expiry.
            if (state.Reserved > 0f && !state.Charging)
            { state.Charging = true; state.Expires = 0f; state.Arming = 0f; }
            float dt = Mathf.Clamp(Time.deltaTime, 0f, .1f);
            state.Arming += dt;
            if (state.Reserved <= 0f && state.Arming < 1f) return;
            state.Charging = true; state.Weapon = equipped;
            float chargeRate = ClubsReserveRules.Rate(kind == ClubWeaponClass.Mace);
            float amount = Mathf.Min(chargeRate * dt, Mathf.Max(0f, capacity - state.Reserved), Mathf.Max(0f, player.GetStamina()));
            if (amount > 0f)
            {
                // Native world stamina rates may differ from1. Only reserve
                // what was actually removed, without overshooting its weapon cap.
                float rate = Mathf.Max(0f, Game.m_staminaRate);
                if (rate <= 0f) { Lock(player, state); state.SuppressUntilBlockRelease = true; return; }
                float before = player.GetStamina();
                player.UseStamina(amount / rate);
                float spent = Mathf.Clamp(before - player.GetStamina(), 0f, capacity - state.Reserved);
                state.Reserved += spent;
                player.m_maxStamina = Mathf.Max(0f, normalMax - state.Reserved);
                player.m_stamina = Mathf.Min(player.m_stamina, player.m_maxStamina);
            }
            if (state.Reserved >= capacity - .001f || player.GetStamina() <= .001f)
            { Lock(player, state); state.SuppressUntilBlockRelease = true; }
        }
        internal static void Commit(Attack attack, Humanoid character, ItemDrop.ItemData weapon, bool started)
        {
            Player player = character as Player;
            if (!started || !MagicSkillPassives.OwnerReady(player) || !States.TryGetValue(player, out var state) || state.Reserved <= 0f ||
                !SameSession(player, state) || state.Weapon != weapon || weapon != player.GetCurrentWeapon() ||
                attack?.m_character != player || attack.m_weapon != weapon || ChargeFraction(attack) > 0f ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70)) return;
            if (!state.Charging && Time.time >= state.Expires) { Release(player, state, true); return; }
            float fraction = ChargeFraction(player);
            bool hammer = ClubWeaponClassService.Classify(weapon) == ClubWeaponClass.SledgeHammer;
            Attacks.GetOrCreateValue(attack).Value = fraction;
            // Attack.Start operates on the concrete native attack clone, not shared
            // item definitions. Keep this payload on that one attack instance.
            bool maceSecondary = !hammer && AttackIntentService.IsSecondary(attack, player);
            if (hammer || maceSecondary) attack.m_damageMultiplier *= 1f + (hammer ? .75f : .50f) * fraction;
            attack.m_staggerMultiplier *= 1f + (hammer ? 1f : .75f) * fraction;
            if (hammer || maceSecondary) attack.m_forceMultiplier *= 1f + fraction;
            // Keep the reservation through a whiff; the first real native hit
            // consumes it. The existing expiry bounds how long it can persist.
            state.PendingAttack = attack; state.Charging = false; state.Arming = 0f;
            if (state.Expires <= Time.time) state.Expires = Time.time + 15f;
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[ClubsCharge] commit actor=" + player.GetZDOID() + " attack=" + RuntimeHelpers.GetHashCode(attack) +
                    " weapon=" + PerkRuntimeService.ItemPrefabName(weapon) + " fraction=" + fraction.ToString("F3") +
                    " reserve=" + state.Reserved.ToString("F2") + " secondary=" + AttackIntentService.IsSecondary(attack, player));
        }

        internal static void ConsumeOnHit(Attack attack, Character victim, HitData hit)
        {
            Player player = attack?.m_character as Player;
            if (player == null || victim == null || hit == null || hit.GetAttacker() != player ||
                hit.m_skill != Skills.SkillType.Clubs || PerkRuntimeService.IsPerkGenerated(hit) ||
                !BaseAI.IsEnemy(player, victim) || !States.TryGetValue(player, out var state) ||
                state.PendingAttack != attack || state.Reserved <= 0f || state.Weapon != attack.m_weapon ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70)) return;
            Release(player, state, false);
        }
    }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Clubs70ReservationTick
    { private static void Postfix(Player __instance) => Clubs70Reservation.Tick(__instance); }
    // Block resilience is scoped to the native block call. Do not increase the
    // player's general damage stagger threshold or mutate shared weapon data.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]
    internal static class Clubs70ChargingBlock
    {
        private sealed class Scope { internal Player Player; internal float Factor; }
        private static void Prefix(Humanoid __instance, out Scope __state)
        {
            __state = null;
            if (!(__instance is Player player) || !MagicSkillPassives.OwnerReady(player) ||
                !MasteryPlugin.Settings.Enabled.Value || !player.IsBlocking() ||
                !Clubs70Reservation.IsCharging(player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Clubs, 70)) return;
            ClubWeaponClass kind = ClubWeaponClassService.Classify(player.GetCurrentWeapon());
            if (kind != ClubWeaponClass.Mace && kind != ClubWeaponClass.SledgeHammer) return;
            __state = new Scope { Player = player, Factor = player.m_staggerDamageFactor };
            player.m_staggerDamageFactor *= 1.5f;
        }
        private static Exception Finalizer(Scope __state, Exception __exception)
        {
            if (__state?.Player != null) __state.Player.m_staggerDamageFactor = __state.Factor;
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.SetMaxStamina))]
    internal static class Clubs70ReservedMaximum
    { private static void Prefix(Player __instance, ref float stamina) => Clubs70Reservation.Maximum(__instance, ref stamina); }
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class Clubs70ReservationCommit
    {
        private static void Postfix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon, bool __result)
            => Clubs70Reservation.Commit(__instance, character, weapon, __result);
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
    internal static class Clubs70ChargeMeleeScope
    {
        [ThreadStatic] private static Attack Current;
        private static void Prefix(Attack __instance, out Attack __state) { __state = Current; Current = __instance; }
        private static Exception Finalizer(Attack __state, Exception __exception) { Current = __state; return __exception; }
        internal static Attack Active => Current;
    }
    [HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
    internal static class Clubs70ChargeAreaScope
    {
        [ThreadStatic] private static Attack Current;
        private static void Prefix(Attack __instance, out Attack __state) { __state = Current; Current = __instance; }
        private static Exception Finalizer(Attack __state, Exception __exception) { Current = __state; return __exception; }
        internal static Attack Active => Current;
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class Clubs70ChargeHit
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            Attack attack = Clubs70ChargeAreaScope.Active ?? Clubs70ChargeMeleeScope.Active;
            Clubs70Reservation.ConsumeOnHit(attack, __instance, hit);
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.Save))]
    internal static class Clubs70ReservationSave
    {
        private static void Prefix(Player __instance, out Clubs70Reservation.SaveState __state)
            => __state = Clubs70Reservation.BeforeSave(__instance);
        private static Exception Finalizer(Player __instance, Clubs70Reservation.SaveState __state, Exception __exception)
        { Clubs70Reservation.AfterSave(__instance, __state); return __exception; }
    }
    [HarmonyPatch(typeof(Player), "OnDisable")]
    internal static class Clubs70ReservationTeardown
    { private static void Prefix(Player __instance) => Clubs70Reservation.Cancel(__instance); }
    [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
    internal static class Clubs70ReservationLogout
    { private static void Prefix() => Clubs70Reservation.Cancel(Player.m_localPlayer); }
}
#endif
