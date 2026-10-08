using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimMastery
{
    // Selection is pure equipped-context routing. Admission, receipts, recovery and
    // effects belong to the selected coordinator; false/throw never implies refund.
    internal static class PantheonActiveInput
    {
        internal enum DispatchResult { NoAction, Ambiguous, Rejected, Accepted, OutcomeUnknown }
        private sealed class ManualAction
        {
            internal string Id;
            internal Skills.SkillType WeaponSkill;
            internal Func<Player, bool> Selected;
            internal Func<Player, bool> TryStart;
        }
        private static readonly SortedDictionary<string, ManualAction> Actions =
            new SortedDictionary<string, ManualAction>(StringComparer.Ordinal);
        private static ConfigFile Configuration;
        private static ConfigEntry<KeyCode> ActiveKey;
        private static ManualAction[] FrozenActions;
        private static int LastDispatchFrame = -1;
        private static bool Dispatching;
        private static bool ShutdownPending;

        internal static void Initialize(ConfigFile config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            RequireIdle();
            if (Configuration != null) throw new InvalidOperationException("Pantheon input is already initialized.");
            ActiveKey = config.Bind("Pantheon", "ActiveAbilityKey", KeyCode.P,
                "Shared key for manual Pantheon abilities. The held weapon selects the action. F7/F8 are retired and resolve to P. " +
                "Reactive abilities activate automatically. Check vanilla remaps and other mods before rebinding.");
            ActiveKey.SettingChanged += NormalizeKey;
            NormalizeKey(null, EventArgs.Empty);
            Configuration = config;
            LastDispatchFrame = -1;
        }

        internal static void Shutdown()
        {
            RequireIdle();
            Actions.Clear();
            FrozenActions = null;
            if (ActiveKey != null) ActiveKey.SettingChanged -= NormalizeKey;
            ActiveKey = null;
            Configuration = null;
            LastDispatchFrame = -1;
            ShutdownPending = false;
        }

        // Plugin teardown may be requested by a callback. Stop admission now,
        // but release the immutable callback snapshot only after dispatch exits.
        internal static void RequestShutdown()
        {
            if (Dispatching) { ShutdownPending = true; return; }
            Shutdown();
        }

        // Register once after Initialize. Never modify registration from callbacks.
        // Selection must not spend/reserve Favor or choose another action because
        // the contextual action is unaffordable/on cooldown. TryStart rechecks authority.
        internal static void RegisterManual(string id, Skills.SkillType weaponSkill, Func<Player, bool> selected, Func<Player, bool> tryStart)
        {
            RequireIdle();
            if (Configuration == null) throw new InvalidOperationException("Initialize Pantheon input before registration.");
            if (FrozenActions != null) throw new InvalidOperationException("Pantheon registration is frozen.");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || selected == null || tryStart == null)
                throw new ArgumentException("Manual Pantheon action registration is invalid.");
            if (Actions.ContainsKey(id)) throw new InvalidOperationException("Duplicate Pantheon action: " + id);
            if (Actions.Count >= 32) throw new InvalidOperationException("Pantheon action registry is full.");
            Actions.Add(id, new ManualAction { Id = id, WeaponSkill = weaponSkill, Selected = selected, TryStart = tryStart });
        }

        private static void RequireIdle()
        {
            if (Dispatching) throw new InvalidOperationException("Pantheon input registration/lifecycle cannot change during dispatch.");
        }

        internal static void FreezeRegistration()
        {
            RequireIdle();
            if (Configuration == null || FrozenActions != null)
                throw new InvalidOperationException("Invalid Pantheon registration lifecycle.");
            FrozenActions = new ManualAction[Actions.Count];
            Actions.Values.CopyTo(FrozenActions, 0);
        }

        private static void NormalizeKey(object sender, EventArgs args)
        {
            // Update the visible module-owned setting as well as the effective key.
            // None and all other remaps survive; retired F7/F8 cannot reactivate later.
            if (ActiveKey != null && (ActiveKey.Value == KeyCode.F7 || ActiveKey.Value == KeyCode.F8))
                ActiveKey.Value = KeyCode.P;
        }

        private static ItemDrop.ItemData HeldWeapon(Player player)
        {
            var weapon = player.GetCurrentWeapon();
            // Native GetCurrentWeapon falls back to an unarmed prefab when weapons are hidden.
            // Manual routing uses visible hands only; reactive/unarmed actions remain independent.
            return weapon?.m_shared != null && weapon.IsWeapon() &&
                (ReferenceEquals(weapon, player.GetRightItem()) || ReferenceEquals(weapon, player.GetLeftItem())) ? weapon : null;
        }

        private static bool CanDispatch(Player player)
        {
            return !ShutdownPending && FrozenActions != null && ActiveKey != null && ActiveKey.Value != KeyCode.None &&
                MasteryPlugin.Settings?.Enabled.Value == true && !Application.isBatchMode &&
                player != null && player == Player.m_localPlayer && player.m_nview?.IsOwner() == true &&
                !player.IsDead() && !player.IsTeleporting() && Hud.instance != null && Hud.instance.m_buildUi != null &&
                !Game.IsPaused() && !Hud.InRadial() && !Hud.InBuildUi() && player.TakeInput();
        }

        internal static void Tick()
        {
            var player = Player.m_localPlayer;
            if (Dispatching || !CanDispatch(player) || LastDispatchFrame == Time.frameCount ||
                !ZInput.GetKeyDown(ActiveKey.Value, true)) return;
            LastDispatchFrame = Time.frameCount;
            DispatchResult result = Dispatch(player);
            if (result == DispatchResult.NoAction && CanDispatch(player))
                player.Message(MessageHud.MessageType.Center, GoldUiLocalization.Text(
                    "This weapon has no special ability.",
                    "Ця зброя не має особливої здібності."));
            else if (result == DispatchResult.Ambiguous)
                player.Message(MessageHud.MessageType.Center, GoldUiLocalization.Text(
                    "Multiple special abilities match this weapon.",
                    "Цій зброї відповідає кілька особливих здібностей."));
        }

        internal static DispatchResult Dispatch(Player player)
        {
            if (Dispatching) return DispatchResult.Rejected;
            if (!CanDispatch(player)) return DispatchResult.NoAction;
            Dispatching = true;
            try
            {
                var weapon = HeldWeapon(player);
                if (weapon == null) return DispatchResult.NoAction;
                var skill = weapon.m_shared.m_skillType;
                ManualAction chosen = null;
                foreach (var action in FrozenActions)
                {
                    if (action.WeaponSkill != skill) continue;
                    bool selected;
                    try { selected = action.Selected(player); }
                    catch (Exception error)
                    {
                        MasteryPlugin.Log?.LogError("[Pantheon] Selection failed for " + action.Id + ": " + error.Message);
                        return DispatchResult.Rejected;
                    }
                    if (!selected) continue;
                    if (chosen != null) return DispatchResult.Ambiguous;
                    chosen = action;
                }
                if (chosen == null) return DispatchResult.NoAction;
                // A callback may invalidate the local session while selecting.
                if (!CanDispatch(player) || !ReferenceEquals(HeldWeapon(player), weapon) ||
                    weapon.m_shared.m_skillType != skill) return DispatchResult.Rejected;
                try { return chosen.TryStart(player) ? DispatchResult.Accepted : DispatchResult.Rejected; }
                catch (Exception error)
                {
                    MasteryPlugin.Log?.LogError("[Pantheon] Activation failed for " + chosen.Id + ": " + error.Message);
                    return DispatchResult.OutcomeUnknown;
                }
            }
            finally
            {
                Dispatching = false;
                if (ShutdownPending) Shutdown();
            }
        }
    }
}
