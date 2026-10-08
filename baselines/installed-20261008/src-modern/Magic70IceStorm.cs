using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class Magic70VariantVisual
    { private static void Prefix(ref int variant) { if (variant == 1270) variant = 0; } }
    internal static class Magic70IceStorm
    {
        internal const string PrefabName = "VM_FimbulStorm70";
        internal const float EitrCost = 50f;
        internal const float Cooldown = 25f;
        internal static readonly List<Magic70StormZone> Zones = new List<Magic70StormZone>();
        private static GameObject Template;
        private static GameObject Staging;
        internal static float Radius(float skill) => 4f + .04f * Mathf.Clamp(skill, 0f, 100f);
        internal static float DamagePerSecond(float skill) => 4f + .08f * Mathf.Clamp(skill, 0f, 100f);
        internal static float Duration(int quality) => 20f * Mathf.Clamp(quality, 1, 4);
        internal static void Register(ZNetScene scene)
        {
            NativeSoftVisualAssets.GetPrefab("sfx_dverger_ice_aoe_start");
            NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/weather/SnowStorm.prefab");
            if (scene == null || scene.m_namedPrefabs.ContainsKey(PrefabName.GetStableHashCode())) return;
            Staging = new GameObject("VM_InactiveStormTemplate"); Staging.SetActive(false);
            Template = new GameObject(PrefabName); Template.transform.SetParent(Staging.transform, false);
            ZNetView view = Template.AddComponent<ZNetView>(); view.m_persistent = false;
            Template.AddComponent<Magic70StormZone>();
            scene.m_namedPrefabs.Add(PrefabName.GetStableHashCode(), Template);
            scene.m_prefabs.Add(Template);
        }
        internal static bool Input(Player player)
        {
            if (!MagicSkillPassives.OwnerReady(player) || !IceStaff35Trail.IsIceStaff(player.GetCurrentWeapon()) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 70)) return true;
            if (!player.m_secondaryAttack && !player.m_secondaryAttackHold) return true;
            FireStaff35Charge.Cancel();
            player.m_queuedSecondAttackTimer = 0f;
            if (!player.m_secondaryAttack || player.InAttack() || player.IsStaggering() || player.IsTeleporting() ||
                player.m_dodgeInvincible || player.m_blocking || player.InMinorAction()) return false;
            Magic70StormZone existing = null;
            foreach (Magic70StormZone zone in Zones)
                if (zone != null && zone.Active && zone.Author == player.GetPlayerID()) { existing = zone; break; }
            // Recasting relocates the SAME paid zone, rather than stacking damage or
            // destroying the weather appearance already confirmed in live play.
            if (!player.HaveEitr(EitrCost) || (existing == null &&
                PerkCooldownStateService.GetRemainingSeconds(player, "magic70_ice") > 0d)) return false;
            if (existing == null && Zones.Count >= 8) return false;
            Vector3 eye = player.GetEyePoint(); Vector3 direction = player.GetLookDir();
            Vector3 point = eye + direction * 20f;
            int mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
            if (Physics.Raycast(eye, direction, out RaycastHit aim, 20f, mask)) point = aim.point;
            if (!Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out RaycastHit ground, 35f, mask) || ground.normal.y < .45f) return false;
            if (existing != null)
            {
                existing.View.InvokeRPC("VM_StormRelocate", player.m_nview.GetZDO().m_uid,
                    ground.point + Vector3.up * .08f,
                    PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.ElementalMagic), player.GetCurrentWeapon().m_quality);
                player.UseEitr(EitrCost);
                PerkCooldownStateService.ReduceRemaining(player, "magic70_ice", 0f);
                PerkCooldownStateService.TryConsume(player, "magic70_ice", Cooldown);
                Magic70CastAnimation.Play(player);
                PerkAudioService.Play("magic70_ice_cast", "sfx_dverger_ice_aoe_start", player.GetCenterPoint(), .5f, .8f);
                return false;
            }
            GameObject prefab = ZNetScene.instance?.GetPrefab(PrefabName);
            if (prefab == null) return false;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, ground.point + Vector3.up * .08f, Quaternion.identity);
            Magic70StormZone created = instance?.GetComponent<Magic70StormZone>();
            if (created?.View?.IsValid() != true || !created.View.IsOwner())
            { if (instance != null) UnityEngine.Object.Destroy(instance); return false; }
            float skill = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.ElementalMagic);
            ZDO data = created.View.GetZDO(); data.Set("vm.storm.author", player.GetPlayerID()); data.Set("vm.storm.skill", skill);
            data.Set("vm.storm.expires", ZNet.instance.GetTime().AddSeconds(Duration(player.GetCurrentWeapon().m_quality)).Ticks);
            player.UseEitr(EitrCost); PerkCooldownStateService.TryConsume(player, "magic70_ice", Cooldown);
            Magic70CastAnimation.Play(player);
            PerkAudioService.Play("magic70_ice_cast", "sfx_dverger_ice_aoe_start", player.GetCenterPoint(), .5f, .8f);
            return false;
        }
        internal static bool Obscures(Character observer, Character target)
        {
            if (observer == null || target == null || observer.IsPlayer() || observer.IsTamed() ||
                (target.transform.position - observer.transform.position).sqrMagnitude <= 9f) return false;
            foreach (Magic70StormZone zone in Zones)
            {
                Player caster = zone != null ? Player.GetPlayer(zone.Author) : null;
                if (caster != null && !caster.IsDead() && zone.Active && BaseAI.IsEnemy(caster, observer) && zone.Contains(observer.GetCenterPoint())) return true;
            }
            return false;
        }
    }
    internal sealed class Magic70StormZone : MonoBehaviour
    {
        internal ZNetView View;
        internal long Author => View?.GetZDO()?.GetLong("vm.storm.author", 0) ?? 0;
        private float _nextDamage;
        private readonly Collider[] _hits = new Collider[96];
        private readonly HashSet<Character> _seen = new HashSet<Character>();
        private NativeStormWeatherVisual _weather;
        private float _nextVisualAttempt;
        internal bool Active => View?.IsValid() == true && Author != 0 && ZNet.instance != null &&
            MasteryPlugin.Settings.Enabled.Value && ZNet.instance.GetTime().Ticks < View.GetZDO().GetLong("vm.storm.expires", 0L);
        internal bool Contains(Vector3 position)
        {
            float radius = Magic70IceStorm.Radius(View?.GetZDO()?.GetFloat("vm.storm.skill", 0f) ?? 0f);
            Vector3 delta = position - transform.position;
            return Mathf.Abs(delta.y) <= 8f && delta.x * delta.x + delta.z * delta.z <= radius * radius;
        }
        private void Awake()
        {
            View = GetComponent<ZNetView>(); Magic70IceStorm.Zones.Add(this);
            if (View?.IsValid() == true)
                View.Register<ZDOID, Vector3, float, int>("VM_StormRelocate", Relocate);
        }
        private void Relocate(long sender, ZDOID actorId, Vector3 point, float skill, int quality)
        {
            if (!Active || !View.IsOwner() || ZDOMan.instance == null) return;
            ZDO actor = ZDOMan.instance.GetZDO(actorId);
            if (actor == null || actor.GetOwner() != sender || actor.GetLong(ZDOVars.s_playerID, 0L) != Author ||
                float.IsNaN(skill) || float.IsInfinity(skill) ||
                float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z) ||
                (point - actor.GetPosition()).sqrMagnitude > 50f * 50f || quality < 1 || quality > 4) return;
            transform.position = point;
            ZDO data = View.GetZDO(); data.SetPosition(point);
            data.Set("vm.storm.skill", Mathf.Clamp(skill, 0f, 100f));
            data.Set("vm.storm.expires", ZNet.instance.GetTime().AddSeconds(Magic70IceStorm.Duration(quality)).Ticks);
            _nextDamage = Time.time + .5f;
            ClearVisual();
        }
        private void OnDestroy() { Magic70IceStorm.Zones.Remove(this); ClearVisual(); }
        private void OnDisable() { ClearVisual(); }
        private void Update()
        {
            if (View?.IsValid() != true || Author == 0) { ClearVisual(); return; }
            ZDO data = View.GetZDO();
            // This zone has no native ZSyncTransform. Replicas explicitly follow
            // the owner-written ZDO position after a relocation.
            Vector3 networkPosition = data.GetPosition();
            if ((transform.position - networkPosition).sqrMagnitude > .0001f)
            { transform.position = networkPosition; ClearVisual(); }
            if (ZNet.instance.GetTime().Ticks >= data.GetLong("vm.storm.expires", 0L))
            { ClearVisual(); if (View.IsOwner()) ZNetScene.instance.Destroy(gameObject); return; }
            float skill = data.GetFloat("vm.storm.skill", 0f);
            if (Player.m_localPlayer != null && (Player.m_localPlayer.transform.position - transform.position).sqrMagnitude < 80f * 80f)
            {
                Visual(skill);
            }
            else ClearVisual();
            if (!View.IsOwner() || Time.time < _nextDamage || !MasteryPlugin.Settings.Enabled.Value) return;
            _nextDamage = Time.time + .5f;
            Player caster = Player.GetPlayer(Author);
            if (caster == null || caster.IsDead()) return;
            IceStaff35Trail.RegisterEffect(ObjectDB.instance);
            float radius = Magic70IceStorm.Radius(skill);
            int count = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 2f, radius + 2f, _hits,
                LayerMask.GetMask("character", "character_net"));
            _seen.Clear();
            for (int i = 0; i < count; i++)
            {
                Character enemy = _hits[i]?.GetComponentInParent<Character>(); _hits[i] = null;
                if (enemy == null || !_seen.Add(enemy) || enemy.IsDead() || enemy.IsPlayer() ||
                    !Contains(enemy.GetCenterPoint()) || !BaseAI.IsEnemy(caster, enemy)) continue;
                // Use the same owner-routed direct frost mark as the ice staff:
                // flying enemies stay low while repeatedly exposed to the storm.
                enemy.GetSEMan()?.AddStatusEffect(IceStaff35Trail.EffectHash, true, 0, 0f, 1);
                HitData hit = new HitData { m_skill = Skills.SkillType.ElementalMagic, m_point = enemy.GetCenterPoint(),
                    m_blockable = false, m_dodgeable = false, m_staggerMultiplier = 0f, m_variant = 1270, m_skillRaiseAmount = 0f };
                hit.m_damage.m_frost = Magic70IceStorm.DamagePerSecond(skill) * .5f; hit.SetAttacker(caster);
                PerkHitContext context = PerkRuntimeService.GetHitContext(hit); context.IsPerkGenerated = true;
                context.AllowSelfProc = context.AllowOtherPerkProc = false; context.XpMultiplier = 0f; context.PerkId = "magic70_ice";
                enemy.Damage(hit);
            }
            _seen.Clear();
        }
        private void Visual(float skill)
        {
            if (!Active || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) { ClearVisual(); return; }
            if (_weather != null || Time.time < _nextVisualAttempt) return;
            _nextVisualAttempt = Time.time + .5f;
            _weather = NativeStormWeatherVisual.Create(transform, Magic70IceStorm.Radius(skill));
        }
        private void ClearVisual()
        { if (_weather != null) _weather.Stop(); _weather = null; }
    }
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Magic70StormPrefabPatch { private static void Postfix(ZNetScene __instance) => Magic70IceStorm.Register(__instance); }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Magic70StormInputPatch
    { [HarmonyPriority(Priority.First)] private static bool Prefix(Player __instance) => Magic70IceStorm.Input(__instance); }
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), new Type[] { typeof(Character) })]
    internal static class Magic70StormVisionPatch
    {
        private static void Postfix(BaseAI __instance, Character target, ref bool __result)
        { if (__result && Magic70IceStorm.Obscures(__instance.m_character, target)) __result = false; }
    }
}
