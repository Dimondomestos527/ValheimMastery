#if false // Replaced by Overdraw70V2.cs in 1.4.68.
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal enum OverdrawPhase { Idle, Drawing, FullDraw, Charging, Charged, Released, Cancelled }
    internal sealed class OverdrawState
    {
        internal float FullDrawSince = -1f;
        internal float ChargedUntil;
        internal bool ChargingFeedbackPlayed;
        internal bool ReadyFeedbackPlayed;
        internal OverdrawReadyIndicator ReadyIndicator;
        internal OverdrawPhase Phase = OverdrawPhase.Idle;
    }

    internal static class Overdraw70Service
    {
        private const float HoldAfterFullDraw = 2.5f;
        private const float ReleaseGrace = 0.75f;
        private static int PayloadShotSerial;
        internal static void Update(Player player)
        {
            if (player == null || player != Player.m_localPlayer || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 70)) return;
            OverdrawState state = MasteryStateStore.GetPlayerState<OverdrawState>(player);
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_shared?.m_skillType != Skills.SkillType.Bows) { Cancel(state); return; }
            if (!player.IsDrawingBow())
            {
                if (state.ChargedUntil > 0f && Time.time > state.ChargedUntil) Cancel(state);
                else if (state.ChargedUntil <= 0f) { ResetCharge(state); state.Phase = OverdrawPhase.Idle; }
                return;
            }
            state.Phase = OverdrawPhase.Drawing;
            if (player.GetAttackDrawPercentage() < 0.99f) { ResetCharge(state); return; }
            if (state.FullDrawSince < 0f) { state.FullDrawSince = Time.time; state.Phase = OverdrawPhase.FullDraw; }
            float held = Time.time - state.FullDrawSince;
            if (held < HoldAfterFullDraw)
            {
                if (held >= 2f)
                {
                    state.Phase = OverdrawPhase.Charging;
                    if (!state.ChargingFeedbackPlayed)
                    {
                        state.ChargingFeedbackPlayed = true;
                        PerkFeedbackService.Play(player, "bows_70_charge", player.GetCenterPoint() + player.transform.forward * 0.75f, false);
                    }
                }
                return;
            }
            // READY is held until this draw is released or cancelled. The old 0.75 second
            // grace window made the mechanic look randomly broken in live play.
            state.ChargedUntil = float.PositiveInfinity;
            state.Phase = OverdrawPhase.Charged;
            if (state.ReadyIndicator == null)
            {
                GameObject indicator = new GameObject("ValheimMastery_OverdrawReady");
                state.ReadyIndicator = indicator.AddComponent<OverdrawReadyIndicator>();
                state.ReadyIndicator.Initialize(player);
            }
            if (state.ReadyFeedbackPlayed) return;
            state.ReadyFeedbackPlayed = true;
            PerkFeedbackService.Play(player, "bows_70_ready", player.GetCenterPoint() + player.transform.forward * 0.90f, true);
        }

        internal static bool TryConsume(Player player, Projectile projectile, HitData hit)
        {
            if (player == null || projectile == null || hit == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Bows, 70)) return false;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (hit.m_skill != Skills.SkillType.Bows && weapon?.m_shared?.m_skillType != Skills.SkillType.Bows) return false;
            OverdrawState state = MasteryStateStore.GetPlayerState<OverdrawState>(player);
            if (state.ChargedUntil < Time.time || state.Phase != OverdrawPhase.Charged) return false;
            state.Phase = OverdrawPhase.Released;
            ResetCharge(state);
            projectile.m_vel *= 2f;
            projectile.m_damage.Modify(1.25f);
            // Hit noise wakes AI; physical knockback belongs to the source HitData.
            hit.m_pushForce *= 2.5f;
            if (projectile.m_originalHitData != null) projectile.m_originalHitData.m_pushForce *= 2.5f;
            projectile.m_hitNoise *= 2.5f;
            ProjectileOverdrawMarker marker = MasteryStateStore.GetObjectState<ProjectileOverdrawMarker>(projectile);
            marker.IsOverdraw = true;
            marker.Arrow = ResolveArrowClass(player, projectile);
            int payloadToken = (NextPayloadShotSerial() << 4) | ((int)marker.Arrow + 1);
            marker.ShotToken = payloadToken;
            MasteryAttackTagService.Add(hit, MasteryAttackTag.Overdraw);
            MasteryAttackTagService.SetAttackSerial(hit, payloadToken);
            if (projectile.m_originalHitData != null)
            {
                MasteryAttackTagService.Add(projectile.m_originalHitData, MasteryAttackTag.Overdraw);
                MasteryAttackTagService.SetAttackSerial(projectile.m_originalHitData, payloadToken);
            }
            OverdrawProjectileTag tag = projectile.GetComponent<OverdrawProjectileTag>();
            if (tag == null) tag = projectile.gameObject.AddComponent<OverdrawProjectileTag>();
            tag.Owner = player; tag.Arrow = marker.Arrow; tag.IsOverdraw = true; tag.ShotToken = payloadToken;
            ZDO zdo = projectile.m_nview?.GetZDO();
            if (zdo != null)
            {
                zdo.Set(OverdrawProjectileTag.ActiveZdoKey, true);
                zdo.Set(OverdrawProjectileTag.ArrowZdoKey, (int)marker.Arrow);
                zdo.Set(OverdrawProjectileTag.OwnerZdoKey, player.GetPlayerID());
                zdo.Set(OverdrawProjectileTag.TokenZdoKey, payloadToken);
                zdo.Set(OverdrawProjectileTag.ConsumedZdoKey, false);
            }
            PerkFeedbackService.Play(player, "bows_70_release", player.GetCenterPoint(), true);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Bows70] OVERDRAW_PROJECTILE projectile=" + projectile.gameObject.name + " arrow=" + marker.Arrow);
            return true;
        }

        private static int NextPayloadShotSerial()
        {
            PayloadShotSerial = PayloadShotSerial >= 8000 ? 1 : PayloadShotSerial + 1;
            return PayloadShotSerial;
        }

        private static ArrowClass ResolveArrowClass(Player player, Projectile projectile)
        {
            ArrowClass arrow = MasteryClassificationService.GetArrowClass(player.GetAmmoItem());
            if (arrow != ArrowClass.Other) return arrow;
            string name = projectile?.gameObject?.name ?? "";
            foreach (ArrowClass value in System.Enum.GetValues(typeof(ArrowClass)))
                if (value != ArrowClass.Other && name.IndexOf(value.ToString(), System.StringComparison.OrdinalIgnoreCase) >= 0) return value;
            return ArrowClass.Other;
        }

        private static void ResetCharge(OverdrawState state)
        {
            state.FullDrawSince = -1f; state.ChargedUntil = 0f; state.ChargingFeedbackPlayed = false; state.ReadyFeedbackPlayed = false;
            if (state.ReadyIndicator != null) Object.Destroy(state.ReadyIndicator.gameObject);
            state.ReadyIndicator = null;
        }
        private static void Cancel(OverdrawState state) { ResetCharge(state); state.Phase = OverdrawPhase.Cancelled; }
    }

    internal sealed class ProjectileOverdrawMarker { internal bool IsOverdraw; internal bool PayloadConsumed; internal ArrowClass Arrow; internal int ShotToken; }
    internal sealed class OverdrawProjectileTag : MonoBehaviour
    {
        internal const string ActiveZdoKey = "vm.overdraw.active";
        internal const string ArrowZdoKey = "vm.overdraw.arrow";
        internal const string OwnerZdoKey = "vm.overdraw.owner";
        internal const string TokenZdoKey = "vm.overdraw.token";
        internal const string ConsumedZdoKey = "vm.overdraw.consumed";
        internal Player Owner;
        internal ArrowClass Arrow;
        internal bool IsOverdraw;
        internal bool PayloadConsumed;
        internal int ShotToken;
    }

    internal sealed class OverdrawReadyIndicator : MonoBehaviour
    {
        private Player Owner;
        private Light Glow;
        private float NextPulse;

        internal void Initialize(Player owner)
        {
            Owner = owner;
            Color cyan = new Color(0.08f, 0.78f, 1f, 0.96f);
            Glow = gameObject.AddComponent<Light>(); Glow.type = LightType.Point; Glow.color = cyan; Glow.range = 2.2f; Glow.intensity = 2.4f;
        }

        private void LateUpdate()
        {
            if (Owner == null || Owner != Player.m_localPlayer || !Owner.IsDrawingBow()) { Destroy(gameObject); return; }
            Vector3 eye = Owner.GetEyePoint(); Vector3 forward = Owner.GetAimDir(eye).normalized;
            Vector3 tip = FindArrowTip(eye, forward);
            transform.position = tip;
            Glow.transform.position = tip; Glow.intensity = 1.8f + Mathf.PingPong(Time.time * 4f, 2.2f);
            if (Time.time >= NextPulse)
            {
                NextPulse = Time.time + 0.32f;
                MasteryVfxMaterial.SpawnPrefab("vfx_Frost", tip, 0.16f);
                MasteryVfxMaterial.SpawnPrefab("vfx_HitSparks", tip, 0.14f);
            }
        }

        private Vector3 FindArrowTip(Vector3 eye, Vector3 forward)
        {
            Transform held = Owner.m_visEquipment?.m_leftItemInstance?.transform ?? Owner.m_visEquipment?.m_rightItemInstance?.transform;
            if (held == null) return eye + forward * 0.82f;
            Renderer[] renderers = held.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return held.position + forward * 0.55f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i) bounds.Encapsulate(renderers[i].bounds);
            float extent = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.y) * bounds.extents.y + Mathf.Abs(forward.z) * bounds.extents.z;
            return bounds.center + forward * (extent + 0.16f);
        }

        private void OnDestroy()
        {
            if (Owner != null && MasteryStateStore.TryGetPlayerState<OverdrawState>(Owner, out OverdrawState state) && state.ReadyIndicator == this) state.ReadyIndicator = null;
        }
    }
    [HarmonyPatch(typeof(Player), "Update")]
    internal static class Overdraw70UpdatePatch { private static void Postfix(Player __instance) => Overdraw70Service.Update(__instance); }
    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
    [HarmonyPriority(Priority.Last)]
    internal static class Overdraw70ProjectilePatch
    {
        private static void Postfix(Projectile __instance, Character owner, HitData hitData)
        {
            MasteryExtendedEventBus.Publish(new ProjectileLaunchedEvent { Owner = owner as Player, Projectile = __instance, Hit = hitData, Skill = hitData?.m_skill ?? Skills.SkillType.None });
            Overdraw70Service.TryConsume(owner as Player, __instance, hitData);
        }
    }
}
#endif
