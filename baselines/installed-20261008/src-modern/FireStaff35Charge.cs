using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimMastery
{
    internal static class FireStaff35Charge
    {
        internal sealed class Cast { internal float Factor = 1f; internal int Steps; internal bool Prepaid; }
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
        private static Fire35CoreChargeVisual CoreVisual;
        private static Transform VisualWeapon;
        private static float NextVisualRetry;
        [ThreadStatic] internal static float ProjectileFactor;
        [ThreadStatic] internal static float BurnFactor;
        [ThreadStatic] internal static float SpawnFactor;
        [ThreadStatic] internal static EffectList ImpactEffects;
        [ThreadStatic] internal static float ImpactScale;
        internal static int Steps(float seconds) => Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(3f, seconds) / .5f), 0, 6);
        internal static float Factor(float seconds) => 1f + .25f * Steps(seconds);
        internal static bool IsStaff(ItemDrop.ItemData item) => item?.m_dropPrefab?.name == "StaffFireball";
        private static void Clear(bool released = false)
        {
            if (Owner != null) Owner.GetComponent<Fire35PoseDriver>()?.End();
            Charging = false; Held = 0f; Weapon = null; Pending = 1f; Paid = BaseCost = 0f;
            if (Ui != null) Ui.SetActive(false);
            if (CoreVisual != null) CoreVisual.Finish();
            CoreVisual = null; VisualWeapon = null; NextVisualRetry = 0f;
        }
        internal static void Cancel() => Clear();
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
                if (player.GetComponent<Fire35PoseDriver>() == null) player.gameObject.AddComponent<Fire35PoseDriver>();
                ResolvePose(player, weapon);
            }
            player.m_queuedAttackTimer = 0f;
            if (player.m_attackHold)
            {
                float nextHeld = Mathf.Min(3f, Held + Mathf.Max(0f, dt));
                float nextCost = BaseCost * (1f + .5f * nextHeld / 3f);
                float extra = Mathf.Max(0f, nextCost - Paid);
                if (player.HaveEitr(extra))
                { player.UseEitr(extra); Paid = nextCost; Held = nextHeld; }
                int step = Steps(Held);
                if (step > LastStep)
                {
                    LastStep = step;
                    if (step == 6) PerkAudioService.Play("fire35_ready", "sfx_imp_fireball_explode", player.GetCenterPoint(), .45f);
                }
                Show(); return false;
            }
            Pending = Factor(Held);
            bool started = false;
            try
            {
                StartingRelease = true; started = player.StartAttack(null, false);
                if (started) player.GetComponent<Fire35PoseDriver>()?.Release(player);
                else { player.AddEitr(Paid); MasteryPlugin.Log.LogWarning("[Fire35] Native release rejected; prepaid Eitr returned."); }
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
            player.GetComponent<Fire35PoseDriver>()?.Begin(player, trigger);
        }
        internal static bool ReleasingPose(Humanoid character) => StartingRelease && character == Owner && Charging;
        internal static bool CosmeticPose(Humanoid character) => character == Owner && Charging && !StartingRelease;
        internal static void SamplePose(Player player)
        {
            if (player != Owner || player.m_animator == null) return;
            if (!Charging) return;
            Transform weapon = player.m_visEquipment?.m_rightItemInstance?.transform;
            if (weapon != VisualWeapon || (CoreVisual == null && Time.time >= NextVisualRetry))
            {
                if (CoreVisual != null) CoreVisual.Finish();
                VisualWeapon = weapon;
                CoreVisual = Fire35CoreChargeVisual.Create(weapon);
                NextVisualRetry = Time.time + .25f;
            }
            player.GetComponent<Fire35PoseDriver>()?.Draw(Held);
            CoreVisual?.Draw(Held);
        }
        internal static void Prepare(Attack attack, Humanoid character, ItemDrop.ItemData weapon, ref float draw)
        {
            if (character != Owner || !IsStaff(weapon) || !Charging) return;
            Cast cast = Casts.GetOrCreateValue(attack);
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
                Ui = new GameObject("VM_Fire35Charge", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                Canvas canvas = Ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 2400;
                Ui.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                Ui.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
                GameObject bar = new GameObject("Bar", typeof(RectTransform), typeof(Image)); bar.transform.SetParent(Ui.transform, false);
                RectTransform rect = bar.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .43f); rect.sizeDelta = new Vector2(240f, 8f);
                bar.GetComponent<Image>().color = new Color(.12f, .05f, .02f, .9f);
                GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image)); fill.transform.SetParent(bar.transform, false);
                Fill = fill.GetComponent<Image>(); Fill.color = new Color(1f, .42f, .08f);
                GameObject text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(bar.transform, false);
                Label = text.GetComponent<TextMeshProUGUI>(); Label.fontSize = 19f; Label.alignment = TextAlignmentOptions.Center;
                text.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 35f); text.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 24f);
                if (MessageHud.instance?.m_messageCenterText?.font != null) Label.font = MessageHud.instance.m_messageCenterText.font;
            }
            Ui.SetActive(true);
            RectTransform fr = Fill.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(Held / 3f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;
            Label.text = Held >= 3f ? "Жар Сурта — готовий" : "Жар Сурта";
        }
    }
    [HarmonyPatch(typeof(Player), nameof(Player.InAttack))]
    internal static class Fire35ReleasePoseGate
    { private static void Postfix(Player __instance, ref bool __result) { if (FireStaff35Charge.ReleasingPose(__instance)) __result = false; } }
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class Fire35CosmeticPoseEventGate
    { private static bool Prefix(Humanoid __instance) => !FireStaff35Charge.CosmeticPose(__instance); }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Fire35InputPatch { private static bool Prefix(Player __instance, float dt) => FireStaff35Charge.Input(__instance, dt); }
    [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
    internal static class Fire35StartPatch
    {
        private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon, ref float attackDrawPercentage) => FireStaff35Charge.Prepare(__instance, character, weapon, ref attackDrawPercentage);
    }
    [HarmonyPatch(typeof(Attack), "GetAttackEitr", new Type[] { typeof(Character), typeof(ItemDrop.ItemData) })]
    internal static class Fire35CostPatch
    {
        private static void Postfix(Attack __instance, ref float __result)
        { if (FireStaff35Charge.Casts.TryGetValue(__instance, out var cast) && cast.Prepaid) __result = 0f; }
    }
    [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
    internal static class Fire35BurstPatch
    {
        private static void Prefix(Attack __instance, out float __state)
        { __state = FireStaff35Charge.ProjectileFactor; FireStaff35Charge.ProjectileFactor = FireStaff35Charge.Casts.TryGetValue(__instance, out var cast) ? cast.Factor : 1f; }
        private static Exception Finalizer(float __state, Exception __exception) { FireStaff35Charge.ProjectileFactor = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    internal static class Fire35ProjectilePatch
    {
        private static void Postfix(Projectile __instance, ItemDrop.ItemData item)
        {
            float factor = FireStaff35Charge.ProjectileFactor;
            if (!FireStaff35Charge.IsStaff(item) || factor <= 1f) return;
            __instance.m_aoe *= Mathf.Sqrt(factor);
            __instance.m_hitVariant = (short)(600 + Mathf.RoundToInt((factor - 1f) * 4f));
            __instance.transform.localScale *= Mathf.Sqrt(factor);
        }
    }
    [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
    internal static class Fire35SpawnContextPatch
    {
        private static void Prefix(Projectile __instance, out float __state)
        {
            __state = FireStaff35Charge.SpawnFactor;
            FireStaff35Charge.SpawnFactor = FireStaff35Charge.IsStaff(__instance.m_weapon) && __instance.m_hitVariant >= 601 && __instance.m_hitVariant <= 606
                ? 1f + .25f * (__instance.m_hitVariant - 600) : 1f;
        }
        private static Exception Finalizer(float __state, Exception __exception) { FireStaff35Charge.SpawnFactor = __state; return __exception; }
    }
    [HarmonyPatch(typeof(Projectile), "OnHit")]
    internal static class Fire35ImpactVisualContextPatch
    {
        internal sealed class State { internal EffectList Effects; internal float Scale; }
        private static void Prefix(Projectile __instance, out State __state)
        {
            __state = new State { Effects = FireStaff35Charge.ImpactEffects, Scale = FireStaff35Charge.ImpactScale };
            FireStaff35Charge.ImpactEffects = null; FireStaff35Charge.ImpactScale = 1f;
            if (FireStaff35Charge.IsStaff(__instance.m_weapon) && __instance.m_hitVariant >= 601 && __instance.m_hitVariant <= 606)
            {
                FireStaff35Charge.ImpactEffects = __instance.m_hitEffects;
                FireStaff35Charge.ImpactScale = Mathf.Sqrt(1f + .25f * (__instance.m_hitVariant - 600));
            }
        }
        private static Exception Finalizer(State __state, Exception __exception)
        {
            if (__state != null) { FireStaff35Charge.ImpactEffects = __state.Effects; FireStaff35Charge.ImpactScale = __state.Scale; }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class Fire35ImpactVisualScalePatch
    {
        private static void Postfix(EffectList __instance, GameObject[] __result)
        {
            if (!ReferenceEquals(__instance, FireStaff35Charge.ImpactEffects) || FireStaff35Charge.ImpactScale <= 1f || __result == null) return;
            foreach (GameObject visual in __result)
            {
                if (visual == null || visual.GetComponent<Aoe>() != null) continue;
                // Native Projectile.OnHit always requests scale=1. Scale only
                // its returned VFX, never the projectile collider or gameplay AoE.
                visual.transform.localScale *= FireStaff35Charge.ImpactScale;
                foreach (ParticleSystem particles in visual.GetComponentsInChildren<ParticleSystem>(true))
                { var main = particles.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            }
        }
    }
    [HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
    internal static class Fire35ExplosionPatch
    {
        private static void Postfix(Aoe __instance, HitData hitData)
        {
            float factor = FireStaff35Charge.SpawnFactor;
            if (factor <= 1f) return;
            __instance.m_radius *= Mathf.Sqrt(factor);
            // SpawnOnHit uses an AoE prefab; scaling only EffectList misses its
            // embedded explosion visuals. Do not scale its gameplay transform.
            foreach (ParticleSystem particles in __instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startSizeMultiplier *= Mathf.Sqrt(factor);
                main.startSpeedMultiplier *= Mathf.Sqrt(factor);
                var shape = particles.shape;
                if (shape.enabled) shape.radius *= Mathf.Sqrt(factor);
            }
            // An inherited hit already contains the scaled attack damage.
            if (hitData == null || !__instance.m_useAttackSettings) __instance.m_damage.Modify(factor);
            __instance.m_hitVariant = (short)(600 + Mathf.RoundToInt((factor - 1f) * 4f));
            __instance.m_skill = Skills.SkillType.ElementalMagic;
        }
    }
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Fire35BurnContextPatch
    {
        private static void Prefix(HitData hit, out float __state)
        {
            __state = FireStaff35Charge.BurnFactor; FireStaff35Charge.BurnFactor = 1f;
            if (hit != null && hit.m_skill == Skills.SkillType.ElementalMagic && hit.m_variant >= 601 && hit.m_variant <= 606)
            { FireStaff35Charge.BurnFactor = 1f + .25f * (hit.m_variant - 600); hit.m_variant = 0; }
        }
        private static Exception Finalizer(float __state, Exception __exception) { FireStaff35Charge.BurnFactor = __state; return __exception; }
    }
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class Fire35VisualVariantPatch
    {
        // Reserved hit variants carry burn duration through the vanilla damage RPC.
        // They must never select a nonexistent fireball/shield visual variant.
        private static void Prefix(ref int variant)
        { if (variant >= 601 && variant <= 606) variant = 0; }
    }
    [HarmonyPatch(typeof(SE_Burning), nameof(SE_Burning.AddFireDamage))]
    internal static class Fire35BurnDurationPatch
    {
        private sealed class BaseDuration { internal float Value; }
        private static readonly ConditionalWeakTable<SE_Burning, BaseDuration> Durations = new ConditionalWeakTable<SE_Burning, BaseDuration>();
        private static void Prefix(SE_Burning __instance)
        {
            BaseDuration duration = Durations.GetValue(__instance, effect => new BaseDuration { Value = effect.m_ttl });
            __instance.m_ttl = duration.Value * Mathf.Max(1f, FireStaff35Charge.BurnFactor);
            // Native AddFireDamage divides TOTAL damage over the new lifetime: no double damage multiplier.
        }
    }
}
