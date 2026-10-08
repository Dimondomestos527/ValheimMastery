using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class MasteryHeldWeaponGlow : MonoBehaviour
    {
        private sealed class GlowNode
        {
            private Light Light;
            private ParticleSystem Embers;
            private Transform AttachedTo;

            internal void Update(Transform target, bool axeReady, bool spearReady)
            {
                if (target == null || (!axeReady && !spearReady)) { SetActive(false); return; }
                if (AttachedTo != target) Attach(target);
                Light.color = axeReady ? new Color(1f, 0.08f, 0.01f) : new Color(0.15f, 0.85f, 1f);
                Light.intensity = axeReady ? 4.2f + Mathf.PingPong(Time.time * 6f, 3.2f) : 1.45f;
                Light.range = axeReady ? 3.2f : 2.1f; Light.enabled = true;
                UpdateEmbers(axeReady);
            }

            private void Attach(Transform target)
            {
                if (Light == null)
                {
                    GameObject glow = new GameObject("ValheimMastery_HeldWeaponGlow"); Light = glow.AddComponent<Light>();
                    Light.type = LightType.Point; Light.shadows = LightShadows.None;
                }
                Light.transform.SetParent(target, false); Light.transform.localPosition = Vector3.zero;
                if (Embers != null) Embers.transform.SetParent(target, false);
                AttachedTo = target;
            }

            private void UpdateEmbers(bool axeReady)
            {
                if (!axeReady) { if (Embers != null && Embers.isPlaying) Embers.Stop(true, ParticleSystemStopBehavior.StopEmitting); return; }
                if (Embers == null)
                {
                    GameObject root = new GameObject("ValheimMastery_AxeKATEmbers"); root.transform.SetParent(AttachedTo, false);
                    Embers = root.AddComponent<ParticleSystem>(); ParticleSystemRenderer renderer = root.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null) MasteryVfxMaterial.AssignOwned(renderer, "vfx_HitSparks");
                    ParticleSystem.MainModule main = Embers.main; main.loop = true; main.startLifetime = 0.50f; main.startSpeed = 1.1f; main.startSize = 0.13f;
                    main.startColor = new Color(1f, 0.06f, 0.01f, 1f); main.simulationSpace = ParticleSystemSimulationSpace.World;
                    ParticleSystem.EmissionModule emission = Embers.emission; emission.rateOverTime = 70f;
                    ParticleSystem.ShapeModule shape = Embers.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.18f;
                }
                if (!Embers.isPlaying) Embers.Play(true);
            }

            internal void SetActive(bool active)
            {
                if (Light != null) Light.enabled = active;
                if (!active && Embers != null && Embers.isPlaying) Embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private Player Player;
        private float AxeGlowUntil;
        private readonly GlowNode Right = new GlowNode();
        private readonly GlowNode Left = new GlowNode();

        internal static void Ensure(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            MasteryHeldWeaponGlow controller = player.GetComponent<MasteryHeldWeaponGlow>();
            if (controller == null) controller = player.gameObject.AddComponent<MasteryHeldWeaponGlow>();
            controller.Player = player;
        }

        internal static void ShowAxeWindup(Player player, float duration)
        {
            if (player == null || player != Player.m_localPlayer) return;
            Ensure(player); MasteryHeldWeaponGlow controller = player.GetComponent<MasteryHeldWeaponGlow>();
            if (controller != null) controller.AxeGlowUntil = Mathf.Max(controller.AxeGlowUntil, Time.time + Mathf.Max(0.1f, duration));
        }

        private void LateUpdate()
        {
            if (Player == null || Player.IsDead() || Player.m_visEquipment == null || !MasteryPlugin.Settings.EnablePerkProcVFX.Value) { Right.SetActive(false); Left.SetActive(false); return; }
            ItemDrop.ItemData weapon = Player.GetCurrentWeapon();
            Skills.SkillType skill = weapon?.m_shared?.m_skillType ?? Skills.SkillType.None;
            // Match Axe35Service.IsAxeHit: harvesting axes also qualify when they deal chop damage.
            bool axeReady = Time.time < AxeGlowUntil && (skill == Skills.SkillType.Axes ||
                (skill == Skills.SkillType.WoodCutting && weapon.GetDamage().m_chop > 0f));
            bool spearReady = false;
            Transform right = Player.m_visEquipment.m_rightItemInstance?.transform;
            Transform left = Player.m_visEquipment.m_leftItemInstance?.transform;
            if ((axeReady || spearReady) && right == null && left == null) right = Player.m_visEquipment.transform;
            Right.Update(right, axeReady, spearReady); Left.Update(left, axeReady, spearReady);
        }
    }

    internal sealed class MasteryWorldSpearGlow : MonoBehaviour
    {
        private Light Light; private ItemDrop Item;
        private float NextCheckAt;
        private void Awake() { Item = GetComponent<ItemDrop>(); }
        private void Update()
        {
            if (Time.time < NextCheckAt) return;
            NextCheckAt = Time.time + 0.25f;
            bool visible = MasteryPlugin.Settings.EnablePerkProcVFX.Value && Player.m_localPlayer != null && PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.Spears, 35) && Item?.m_itemData?.m_shared?.m_skillType == Skills.SkillType.Spears;
            if (!visible) { if (Light != null) Light.enabled = false; return; }
            if (Light == null) { Light = gameObject.AddComponent<Light>(); Light.type = LightType.Point; Light.color = new Color(0.12f, 0.82f, 1f); Light.range = 2.8f; Light.intensity = 1.6f; Light.shadows = LightShadows.None; }
            Light.enabled = true;
        }
    }

    internal static class WeaponGlowService { internal static void Tick() { } }

    [HarmonyPatch(typeof(ItemDrop), "Awake")]
    internal static class MasteryWorldSpearGlowPatch
    {
        private static void Postfix(ItemDrop __instance)
        {
            if (__instance?.m_itemData?.m_shared?.m_skillType == Skills.SkillType.Spears &&
                __instance.GetComponent<MasteryWorldSpearGlow>() == null)
                __instance.gameObject.AddComponent<MasteryWorldSpearGlow>();
        }
    }
}
