using System;
using UnityEngine;

namespace ValheimMastery
{
    // Cosmetic native Draugr Wakeup. Do not replace its animator/controller or
    // send skeleton-only wakeup parameters to this different native controller.
    internal sealed class CarrierBirthVisual : MonoBehaviour
    {
        private float WaitingSince;
        private bool Played;
        private void Awake() { WaitingSince = Time.time; }
        private void Update()
        {
            if (Played || Player.m_localPlayer == null) return;
            Character character = GetComponent<Character>();
            ZDO data = character?.m_nview?.GetZDO();
            if (data == null || ZNet.instance == null) return;
            long birth = data.GetLong(Magic70Carrier.BirthKey, 0L);
            if (birth == 0L) { if (Time.time - WaitingSince > 8f) Played = true; return; }
            float age = (float)((ZNet.instance.GetTime().Ticks - birth) / (double)TimeSpan.TicksPerSecond);
            Played = true;
            if (age < 0f || age > 6f) return; // no replay on old sector/portal loads
            Animator animator = character.GetVisual()?.GetComponentInChildren<Animator>(true);
            int wake = Animator.StringToHash("Base Layer.Wakeup");
            if (animator != null && animator.HasState(0, wake)) animator.Play(wake, 0, 0f);
            PerkNativeFeedback.PlayVfx("fx_summon_skeleton_spawn", character.transform.position, .7f, 3.8f);
            GameObject effect = NativePerkAssetResolver.Resolve("fx_summon_skeleton_spawn");
            if (effect != null)
                foreach (Transform child in effect.GetComponentsInChildren<Transform>(true))
                    if (child.name == "sfx emerge")
                    {
                        PerkAudioService.PlayPrefab("torba_birth", child.gameObject,
                            character.transform.position, .5f, .55f, 1f);
                        break;
                    }
        }
    }
}
