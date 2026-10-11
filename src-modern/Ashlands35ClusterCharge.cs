using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal static class Ashlands35ClusterCharge
    {
        internal sealed class Cast { internal float Factor = 1f; internal int Steps; internal bool Prepaid, Prepared; }
        internal static readonly ConditionalWeakTable<Attack, Cast> Casts = new ConditionalWeakTable<Attack, Cast>();
        private static Player Owner;
        private static ItemDrop.ItemData Weapon;
        private static float Held, Pending = 1f;
        private static float Paid, BaseCost;
        private static int LastStep;
        private static bool Charging;
        private static bool CancelledUntilRelease;
        private static bool StartingRelease;
        private static GameObject Ui;
        private static Image Fill;
        private static TextMeshProUGUI Label;
        private static Ashlands35ChargeVisual CoreVisual;
        private static Transform VisualWeapon;
        private static float NextVisualRetry;
        internal static int Steps(float seconds) => Ashlands35Rules.Steps(seconds);
        internal static float Factor(float seconds) => Ashlands35Rules.Damage(Steps(seconds));
        internal static bool IsStaff(ItemDrop.ItemData item) => item?.m_dropPrefab?.name == "StaffClusterbomb";
        private static void Clear(bool released = false)
        {
            if (Owner != null) Owner.GetComponent<Ashlands35ClusterPose>()?.End();
            Charging = false; Held = 0f; Weapon = null; Pending = 1f; Paid = BaseCost = 0f;
            if (Ui != null) Ui.SetActive(false);
            if (CoreVisual != null) CoreVisual.Finish();
            CoreVisual = null; VisualWeapon = null; NextVisualRetry = 0f;
        }
        internal static void Cancel() => Clear();
        internal static void SwitchCleanup(Player player)
        {
            if (player != Owner || !Charging || player.GetCurrentWeapon() == Weapon) return;
            Clear(); CancelledUntilRelease = false;
        }
        internal static void Shutdown() { Clear(); if (Ui != null) UnityEngine.Object.Destroy(Ui); Ui = null; Fill = null; Label = null; Owner = null; Weapon = null; CancelledUntilRelease = false; }
        internal static bool Input(Player player, float dt)
        {
            if (player != Player.m_localPlayer) return true;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (Owner != player) { Clear(); CancelledUntilRelease = false; Owner = player; }
            if (CancelledUntilRelease)
            {
                if (!IsStaff(weapon)) { CancelledUntilRelease = false; return true; }
                if (!player.m_attack && !player.m_attackHold) CancelledUntilRelease = false;
                player.m_queuedAttackTimer = 0f;
                return false;
            }
            if (Charging && player.m_blocking)
            {
                // Cancel, never release a projectile or immediately restart while
                // the original primary button is still held. Already spent Eitr
                // remains spent, just as for the previous interrupted charge.
                Clear(); CancelledUntilRelease = true;
                player.m_queuedAttackTimer = 0f;
                return false;
            }
            if (!MagicSkillPassives.OwnerReady(player) || !IsStaff(weapon) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 35) ||
                player.IsTeleporting() || player.m_dodgeInvincible || player.IsStaggering() || player.m_blocking || player.InMinorAction())
            { Clear(); return true; }
            if (Charging && Weapon != weapon) Clear();
            if (!Charging && !player.m_attack && !player.m_attackHold) return true;
            if (!Charging && player.InAttack()) { Clear(); return true; }
            if (!Charging)
            {
                BaseCost = weapon.m_shared.m_attack.GetAttackEitr(player, weapon);
                if (!player.HaveEitr(BaseCost)) { BaseCost = 0f; return true; }
                Charging = true; Weapon = weapon; Held = 0f; LastStep = 0;
                Paid = BaseCost; player.UseEitr(BaseCost);
                if (player.GetComponent<Ashlands35ClusterPose>() == null) player.gameObject.AddComponent<Ashlands35ClusterPose>();
                ResolvePose(player, weapon);
            }
            player.m_queuedAttackTimer = 0f;
            if (player.m_attackHold)
            {
                float nextHeld = Mathf.Min(3f, Held + Mathf.Max(0f, dt));
                float nextCost = BaseCost * Ashlands35Rules.Cost(nextHeld);
                float extra = Mathf.Max(0f, nextCost - Paid);
                if (player.HaveEitr(extra))
                { player.UseEitr(extra); Paid = nextCost; Held = nextHeld; }
                int step = Steps(Held);
                if (step > LastStep)
                {
                    LastStep = step;
                    Ashlands35Presentation.ClusterCharge(player.GetCenterPoint(), step);
                }
                Show(); return false;
            }
            Pending = Factor(Held);
            bool started = false;
            try
            {
                StartingRelease = true; started = player.StartAttack(null, false);
                if (started) player.GetComponent<Ashlands35ClusterPose>()?.Release(player);
                else { player.AddEitr(Paid); MasteryPlugin.Log.LogWarning("[Ashlands35Cluster] Native release rejected; prepaid Eitr returned."); }
            }
            finally
            {
                StartingRelease = false;
                Clear(started);
            }
            return false;
        }
        private static void ResolvePose(Player player, ItemDrop.ItemData weapon)
        {
            string trigger = weapon.m_shared.m_attack.m_attackAnimation;
            if (weapon.m_shared.m_attack.m_attackChainLevels > 1 || weapon.m_shared.m_attack.m_attackRandomAnimations >= 2)
                trigger += "0";
            // Sample an isolated transform-only rig. NEVER hold the player's
            // full-body Animator: locomotion and native attack admission stay live.
            player.GetComponent<Ashlands35ClusterPose>()?.Begin(player, trigger);
        }
        internal static bool ReleasingPose(Humanoid character) => StartingRelease && character == Owner && Charging;
        internal static bool CosmeticPose(Humanoid character) => character == Owner && Charging && !StartingRelease;
        internal static void SamplePose(Player player)
        {
            if (player != Owner || player.m_animator == null) return;
            if (!Charging) return;
            if (!MagicSkillPassives.OwnerReady(player) || player.GetCurrentWeapon() != Weapon ||
                !IsStaff(player.GetCurrentWeapon()) || player.IsTeleporting() ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 35))
            { Clear(); return; }
            player.GetComponent<Ashlands35ClusterPose>()?.Draw(Held);
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            {
                if (CoreVisual != null) CoreVisual.Finish();
                CoreVisual = null; VisualWeapon = null;
                return;
            }
            Transform weapon = player.m_visEquipment?.m_rightItemInstance?.transform;
            if (weapon != VisualWeapon || (CoreVisual == null && Time.time >= NextVisualRetry))
            {
                if (CoreVisual != null) CoreVisual.Finish();
                VisualWeapon = weapon;
                CoreVisual = Ashlands35ChargeVisual.Create(weapon);
                NextVisualRetry = Time.time + .25f;
            }
            CoreVisual?.Draw(Held);
        }
        internal static void Prepare(Attack attack, Humanoid character, ItemDrop.ItemData weapon, ref float draw)
        {
            if (character != Owner || !IsStaff(weapon) || !Charging) return;
            Cast cast = Casts.GetOrCreateValue(attack);
            if (cast.Prepared) return; cast.Prepared = true;
            cast.Prepaid = true;
            cast.Factor = Pending; cast.Steps = Steps(Held);
            attack.m_damageMultiplier *= cast.Factor;
            // Never apply the bow's near-zero quick-draw damage/velocity branch.
            draw = 1f;
        }
        private static void Show()
        {
            if (Ui == null)
            {
                Ui = new GameObject("VM_Ashlands35ClusterCharge", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                Canvas canvas = Ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2400;
                Ui.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                Ui.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
                GameObject bar = new GameObject("Bar", typeof(RectTransform), typeof(Image)); bar.transform.SetParent(Ui.transform, false);
                RectTransform rect = bar.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .43f); rect.sizeDelta = new Vector2(240f, 8f);
                bar.GetComponent<Image>().color = new Color(.12f, .05f, .02f, .9f);
                GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(bar.transform, false);
                Fill = fill.GetComponent<Image>(); Fill.color = new Color(.45f, .8f, 1f);
                MasteryHudArtwork.Frame(bar.GetComponent<Image>(), "track.png"); MasteryHudArtwork.Fill(Fill);
                GameObject text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(bar.transform, false);
                Label = text.GetComponent<TextMeshProUGUI>(); Label.fontSize = 19f; Label.alignment = TextAlignmentOptions.Center;
                text.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 35f); text.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 24f);
                if (MessageHud.instance?.m_messageCenterText?.font != null) Label.font = MessageHud.instance.m_messageCenterText.font;
            }
            Ui.SetActive(true);
            RectTransform fr = Fill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(Held / 3f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            Label.text = Held >= 3f ? "Розлом — готовий" : "Розлом";
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.InAttack))]
    internal static class Ashlands35ClusterReleaseGate
    { private static void Postfix(Player __instance, ref bool __result) { if (Ashlands35ClusterCharge.ReleasingPose(__instance)) __result = false; } }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class Ashlands35ClusterPoseEventGate
    { private static bool Prefix(Humanoid __instance) => !Ashlands35ClusterCharge.CosmeticPose(__instance); }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Ashlands35ClusterSwitchPatch
    {
        // Void cleanup still runs when the existing Fire35 prefix consumes its input.
        [HarmonyPriority(Priority.First)] private static void Prefix(Player __instance) => Ashlands35ClusterCharge.SwitchCleanup(__instance);
    }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Ashlands35ClusterInputPatch
    { [HarmonyPriority(Priority.Last)] private static bool Prefix(Player __instance, float dt) => Ashlands35ClusterCharge.Input(__instance, dt); }
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class Ashlands35ClusterStartPatch
    { private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon, ref float __8)
        => Ashlands35ClusterCharge.Prepare(__instance, character, weapon, ref __8); }
    [HarmonyPatch(typeof(Attack), "GetAttackEitr", new Type[] { typeof(Character), typeof(ItemDrop.ItemData) })]
    internal static class Ashlands35ClusterPrepaidPatch
    { private static void Postfix(Attack __instance, ref float __result)
        { if (Ashlands35ClusterCharge.Casts.TryGetValue(__instance, out var cast) && cast.Prepaid) __result = 0f; } }
}
