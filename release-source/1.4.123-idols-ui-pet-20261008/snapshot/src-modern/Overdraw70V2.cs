using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal enum OverdrawPhase { Idle, Drawing, FullDraw, Charging, Ready, Released, Cancelled }
    internal sealed class OverdrawState
    {
        internal float FullDrawSince = -1f;
        internal float ChargedUntil;
        internal bool ReadyFeedbackPlayed;
        internal OverdrawReadyIndicator ReadyIndicator;
        internal OverdrawPhase Phase = OverdrawPhase.Idle;
    }

    internal static class Overdraw70Service
    {
        internal const float HoldAfterFullDraw = 2.5f;
        internal const float BuildupStart = 2f;
        internal const float ReleaseGrace = 0.35f;
        private static int ShotSerial;

        internal static void Update(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            Bow70DebugOverlay.Refresh(player);
            OverdrawState state = MasteryStateStore.GetPlayerState<OverdrawState>(player);
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            bool valid = !player.IsDead() && !player.IsStaggering() && PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 70) && weapon?.m_shared?.m_skillType == Skills.SkillType.Bows;
            if (!valid) { Cancel(state); return; }
            if (player.m_blocking) { Cancel(state); return; }
            if (!player.IsDrawingBow())
            {
                // Projectile.Setup can run after the first non-drawing Player.Update.
                // Keep only a short release window; never carry READY to a later shot.
                if (state.Phase == OverdrawPhase.Ready && state.ChargedUntil >= Time.time) return;
                if (state.Phase != OverdrawPhase.Released) Cancel(state); else ResetCharge(state);
                return;
            }
            state.Phase = OverdrawPhase.Drawing;
            if (player.GetAttackDrawPercentage() < 0.99f) { ResetCharge(state); state.Phase = OverdrawPhase.Drawing; return; }
            if (state.FullDrawSince < 0f) state.FullDrawSince = Time.time;
            float held = Mathf.Max(0f, Time.time - state.FullDrawSince);
            state.Phase = held <= 0.02f ? OverdrawPhase.FullDraw : held < HoldAfterFullDraw ? OverdrawPhase.Charging : OverdrawPhase.Ready;
            if (held >= BuildupStart && MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            {
                if (state.ReadyIndicator == null)
                {
                    GameObject indicator = new GameObject("ValheimMastery_OverdrawArrowTip");
                    state.ReadyIndicator = indicator.AddComponent<OverdrawReadyIndicator>();
                    state.ReadyIndicator.Initialize(player);
                }
            }
            if (held < HoldAfterFullDraw) return;
            state.ChargedUntil = Time.time + ReleaseGrace;
            if (state.ReadyFeedbackPlayed) return;
            state.ReadyFeedbackPlayed = true;
            // Prefer a dry bow-tension cue. Fallback remains bow-family audio, never forge/smelter.
            string readySfx = ZNetScene.instance?.GetPrefab("sfx_bow_draw") != null ? "sfx_bow_draw" : "sfx_bow_fire";
            PerkAudioService.Play("bows70_ready", readySfx, player.GetCenterPoint(), 0.55f);
        }

        internal static bool TryConsume(Player player, Projectile projectile, HitData hit, ItemDrop.ItemData firedAmmo)
        {
            if (player == null || projectile == null || hit == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 70) || hit.m_skill != Skills.SkillType.Bows) return false;
            OverdrawState state = MasteryStateStore.GetPlayerState<OverdrawState>(player);
            if (player.m_blocking) { Cancel(state); return false; }
            if (state.Phase != OverdrawPhase.Ready || state.ChargedUntil < Time.time) return false;
            state.Phase = OverdrawPhase.Released; ResetCharge(state, false);
            projectile.m_vel *= 2f; projectile.m_damage.Modify(1.25f); projectile.m_attackForce *= 2.5f;
            Bow70ArrowProfileService.Apply(player, projectile, firedAmmo);
            hit.m_pushForce *= 2.5f;
            if (projectile.m_originalHitData != null) projectile.m_originalHitData.m_pushForce *= 2.5f;
            int shotToken = NextShotSerial();
            MasteryAttackTagService.Add(hit, MasteryAttackTag.Overdraw); MasteryAttackTagService.SetAttackSerial(hit, shotToken);
            if (projectile.m_originalHitData != null) { MasteryAttackTagService.Add(projectile.m_originalHitData, MasteryAttackTag.Overdraw); MasteryAttackTagService.SetAttackSerial(projectile.m_originalHitData, shotToken); }
            OverdrawProjectileTag tag = projectile.GetComponent<OverdrawProjectileTag>() ?? projectile.gameObject.AddComponent<OverdrawProjectileTag>();
            tag.Initialize(player, shotToken);
            ZDO zdo = projectile.m_nview?.GetZDO();
            if (zdo != null) { zdo.Set(OverdrawProjectileTag.ActiveZdoKey, true); zdo.Set(OverdrawProjectileTag.OwnerZdoKey, player.GetPlayerID()); zdo.Set(OverdrawProjectileTag.TokenZdoKey, shotToken); }
            PerkAudioService.Play("bows70_release", "sfx_bow_fire", projectile.transform.position, 0.58f);
            OverdrawPenetrationService.SendReleaseVisual(player, projectile.transform.position, projectile.m_vel.normalized);
            Bow70DebugState.RecordRelease(projectile, shotToken);
            if (MasteryPlugin.Settings.VerboseLogging.Value) MasteryPlugin.Log.LogInfo("[Bows70] RELEASE projectile=" + projectile.gameObject.name + " velocity=x2 damage=x1.5625 before ammo profile force=x2.5 token=" + shotToken);
            return true;
        }

        private static int NextShotSerial() { ShotSerial = ShotSerial >= 120000 ? 1 : ShotSerial + 1; return ShotSerial; }
        private static void ResetCharge(OverdrawState state, bool setIdle = true)
        {
            state.FullDrawSince = -1f; state.ChargedUntil = 0f; state.ReadyFeedbackPlayed = false;
            if (state.ReadyIndicator != null) Object.Destroy(state.ReadyIndicator.gameObject);
            state.ReadyIndicator = null; if (setIdle) state.Phase = OverdrawPhase.Idle;
        }
        private static void Cancel(OverdrawState state) { if (state == null) return; ResetCharge(state, false); state.Phase = OverdrawPhase.Cancelled; }
    }

    internal sealed class OverdrawProjectileTag : MonoBehaviour
    {
        internal const string ActiveZdoKey = "vm.overdraw.active";
        internal const string OwnerZdoKey = "vm.overdraw.owner";
        internal const string TokenZdoKey = "vm.overdraw.token";
        internal Player Owner; internal int ShotToken; internal bool IsOverdraw;
        internal void Initialize(Player owner, int token) { Owner = owner; ShotToken = token; IsOverdraw = true; if (MasteryPlugin.Settings.EnablePerkProcVFX.Value && GetComponent<OverdrawArrowTrail>() == null) gameObject.AddComponent<OverdrawArrowTrail>(); }
    }

    internal sealed class OverdrawReadyIndicator : MonoBehaviour
    {
        private Player Owner;
        private Light TipLight;
        private bool ReadyPulsePlayed;
        private Transform CachedHeld;
        private Renderer[] CachedRenderers;
        internal void Initialize(Player owner)
        {
            Owner = owner;
            TipLight = gameObject.AddComponent<Light>();
            TipLight.type = LightType.Point;
            TipLight.color = new Color(1f, 0.76f, 0.24f, 1f);
            TipLight.range = 0.35f;
            TipLight.intensity = 0.25f;
            TipLight.shadows = LightShadows.None;
        }
        private void LateUpdate()
        {
            if (Owner == null || Owner != Player.m_localPlayer || !Owner.IsDrawingBow() || Owner.IsDead() || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) { Destroy(gameObject); return; }
            Vector3 eye = Owner.GetEyePoint(), forward = Owner.GetAimDir(eye).normalized, tip = FindArrowTip(eye, forward);
            transform.position = tip;
            OverdrawState state = MasteryStateStore.GetPlayerState<OverdrawState>(Owner);
            float held = state.FullDrawSince < 0f ? 0f : Mathf.Max(0f, Time.time - state.FullDrawSince);
            float tension = Mathf.Clamp01((held - Overdraw70Service.BuildupStart) /
                (Overdraw70Service.HoldAfterFullDraw - Overdraw70Service.BuildupStart));
            TipLight.range = Mathf.Lerp(0.35f, 0.85f, tension);
            TipLight.intensity = Mathf.Lerp(0.25f, 2.3f, tension) + Mathf.PingPong(Time.time * 5f, 0.25f) * tension;
            if (held >= Overdraw70Service.HoldAfterFullDraw && !ReadyPulsePlayed)
            {
                ReadyPulsePlayed = true;
                MasteryVfxMaterial.SpawnPrefab("vfx_arrowhit", tip, 0.18f);
            }
        }
        private Vector3 FindArrowTip(Vector3 eye, Vector3 forward)
        {
            Transform held = Owner.m_visEquipment?.m_leftItemInstance?.transform ?? Owner.m_visEquipment?.m_rightItemInstance?.transform;
            if (held == null) return eye + forward * 0.82f;
            if (CachedHeld != held || CachedRenderers == null)
            {
                CachedHeld = held;
                CachedRenderers = held.GetComponentsInChildren<Renderer>(true);
            }
            Bounds bounds = default;
            bool hasBounds = false;
            foreach (Renderer renderer in CachedRenderers)
            {
                if (renderer == null) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) return held.position + forward * 0.55f;
            float extent = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.y) * bounds.extents.y + Mathf.Abs(forward.z) * bounds.extents.z;
            return bounds.center + forward * (extent + 0.12f);
        }
        private void OnDestroy() { if (Owner != null && MasteryStateStore.TryGetPlayerState<OverdrawState>(Owner, out OverdrawState state) && state.ReadyIndicator == this) state.ReadyIndicator = null; }
    }

    internal sealed class OverdrawArrowTrail : MonoBehaviour
    {
        private Material Material;
        private TrailRenderer Trail;
        private void Awake()
        {
            TrailRenderer trail = gameObject.AddComponent<TrailRenderer>(); Material = MasteryVfxMaterial.CloneFromPrefab("vfx_arrowhit"); trail.material = Material;
            trail.time = 0.20f; trail.minVertexDistance = 0.08f; trail.widthMultiplier = 0.045f; trail.startColor = new Color(1f, 0.80f, 0.38f, 0.78f); trail.endColor = new Color(0.80f, 0.84f, 0.88f, 0f); trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Trail = trail;
            Trail.enabled = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
        }
        private void LateUpdate()
        {
            if (Trail == null) return;
            bool visible = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (Trail.enabled && !visible) Trail.Clear();
            Trail.enabled = visible;
            Trail.emitting = visible;
        }
        private void OnDestroy() { if (Material != null) Destroy(Material); }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Overdraw70UpdatePatch { private static void Postfix(Player __instance) => Overdraw70Service.Update(__instance); }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    [HarmonyPriority(Priority.Last)]
    internal static class Overdraw70ProjectilePatch
    {
        private static void Postfix(Projectile __instance, Character owner, HitData hitData, ItemDrop.ItemData ammo)
        {
            MasteryExtendedEventBus.Publish(new ProjectileLaunchedEvent { Owner = owner as Player, Projectile = __instance, Hit = hitData, Skill = hitData?.m_skill ?? Skills.SkillType.None });
            Overdraw70Service.TryConsume(owner as Player, __instance, hitData, ammo);
        }
    }
}
