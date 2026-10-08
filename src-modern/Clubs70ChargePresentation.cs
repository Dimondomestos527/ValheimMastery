#if MASTERY_CLUBS70_EXPERIMENT
using UnityEngine;

namespace ValheimMastery
{
    // One native gnista emitter; no fog child, stage blasts or damage prefab.
    internal sealed class Clubs70ChargePresentation : MonoBehaviour
    {
        private Player Owner;
        private GameObject Root;
        private ParticleSystem Sparks;
        private float Displayed;
        private float Retry;
        private AudioSource Sound;
        private GameObject SoundRoot;
        internal static void Tick(Player player)
        {
            if (player != Player.m_localPlayer) return;
            var effect = player.GetComponent<Clubs70ChargePresentation>();
            if (effect == null && Clubs70Reservation.IsCharging(player))
            { effect = player.gameObject.AddComponent<Clubs70ChargePresentation>(); effect.Owner = player; }
        }
        private void Update()
        {
            if (Owner == null) { Destroy(this); return; }
            if (!Clubs70Reservation.IsCharging(Owner))
            {
                if (Sparks != null) { var ending = Sparks.emission; ending.rateOverTime = 0f; }
                if (Sound != null) Sound.volume = Mathf.MoveTowards(Sound.volume, 0f, Time.deltaTime * .8f);
                if (Sound == null || Sound.volume <= .001f) Destroy(this);
                return;
            }
            float charge = Clubs70Reservation.ChargeFraction(Owner);
            Displayed = Mathf.MoveTowards(Displayed, charge, Time.deltaTime * 2f);
            UpdateSound();
            Transform weapon = Owner.m_visEquipment?.m_rightItemInstance?.transform;
            if (weapon == null) return;
            Vector3 point = weapon.position;
            MeshRenderer mesh = weapon.GetComponentInChildren<MeshRenderer>();
            if (mesh != null) point = mesh.bounds.center;
            if (Root != null) Root.transform.position = point;
            if (Sparks != null)
            {
                var emission = Sparks.emission;
                emission.rateOverTime = MasteryPlugin.Settings.EnablePerkProcVFX.Value ? Mathf.Lerp(5f, 38f, Displayed) : 0f;
                var main = Sparks.main;
                main.startSizeMultiplier = Mathf.Lerp(.018f, .045f, Displayed);
                main.startSpeedMultiplier = Mathf.Lerp(.25f, .8f, Displayed);
            }
            else if (Root == null && Time.time >= Retry && MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            {
                Retry = Time.time + .5f;
                GameObject native = NativePerkAssetResolver.Resolve("vfx_HitSparks");
                if (native == null) return;
                Root = PerkNativeFeedback.CreateVisualOnly(native);
                if (Root == null) return;
                Root.transform.position = point;
                NativeVfxSafeFrame.AfterReady(Root, Begin);
                Root.SetActive(true);
            }
        }
        private void Begin()
        {
            if (Root == null) return;
            NativeBurstPlayback.Mute(Root);
            foreach (ParticleSystem system in Root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (!NativeBurstPlayback.HasMaterial(system, "gnista") ||
                    system.GetComponent<ParticleSystemRenderer>()?.renderMode != ParticleSystemRenderMode.Stretch) continue;
                Sparks = system;
                var main = system.main; main.loop = true; main.maxParticles = 48;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = .4f;
                var emission = system.emission; emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
                emission.enabled = true; emission.rateOverTime = 5f;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null) renderer.enabled = true;
                for (Transform node = system.transform; node != null && node != Root.transform; node = node.parent)
                    node.gameObject.SetActive(true);
                system.Play(false);
                break;
            }
        }
        private void UpdateSound()
        {
            if (Sound == null && MasteryPlugin.Settings.EnablePerkSFX.Value)
            {
                GameObject native = NativePerkAssetResolver.Resolve("sfx_gui_craftitem_forge");
                if (native == null) return;
                AudioSource template = native.GetComponentInChildren<AudioSource>(true);
                ZSFX settings = template != null ? template.GetComponent<ZSFX>() : null;
                AudioClip clip = settings?.m_audioClips?.Length > 0 ? settings.m_audioClips[0] : template?.clip;
                if (clip == null) return;
                SoundRoot = new GameObject("VM_ClubCharge_ForgeLoop");
                SoundRoot.transform.SetParent(Owner.transform, false);
                Sound = SoundRoot.AddComponent<AudioSource>();
                Sound.playOnAwake = false; Sound.loop = true; Sound.clip = clip;
                Sound.outputAudioMixerGroup = template.outputAudioMixerGroup;
                Sound.spatialBlend = 0f; Sound.volume = 0f; Sound.Play();
            }
            if (Sound == null) return;
            float target = MasteryPlugin.Settings.EnablePerkSFX.Value ? Mathf.Lerp(.04f, .16f, Displayed) : 0f;
            Sound.volume = Mathf.MoveTowards(Sound.volume, target, Time.deltaTime * .8f);
            Sound.pitch = Mathf.Lerp(.9f, 1.1f, Displayed);
        }
        private void OnDestroy()
        {
            if (Root != null) Destroy(Root);
            if (SoundRoot != null) Destroy(SoundRoot);
        }
    }
}
#endif
