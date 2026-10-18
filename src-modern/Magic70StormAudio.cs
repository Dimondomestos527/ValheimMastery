using UnityEngine;
namespace ValheimMastery
{
    // Accepted local cast feedback only. No costs, zone lifetime, RPC or ZDO
    // mutation; mirrors the existing owner-local sound with bounded cold retry.
    internal sealed class Magic70StormAudio : MonoBehaviour
    {
        private Player Owner;
        private ZNet Session;
        private long World;
        private Vector3 Point;
        private float Deadline, NextAttempt;
        private bool Finished;
        internal static void Play(Player player)
        {
            if (player == null || player != Player.m_localPlayer || Application.isBatchMode ||
                MasteryPlugin.Settings?.Enabled?.Value != true || MasteryPlugin.Settings.EnablePerkSFX.Value != true) return;
            Magic70StormAudio cue = player.GetComponent<Magic70StormAudio>();
            if (cue == null || cue.Finished) cue = player.gameObject.AddComponent<Magic70StormAudio>();
            cue.Owner = player; cue.Session = ZNet.instance; cue.World = cue.Session?.GetWorld() != null ? cue.Session.GetWorldUID() : 0L; cue.Point = player.GetCenterPoint();
            cue.Deadline = Time.unscaledTime + 2f; cue.NextAttempt = 0f; cue.TryPlay();
        }
        private void Update() => TryPlay();
        private void TryPlay()
        {
            if (Finished) return;
            if (Owner == null || Owner != Player.m_localPlayer || Owner.IsDead() || Owner.IsTeleporting() ||
                Owner.m_nview?.IsValid() != true || !Owner.m_nview.IsOwner() ||
                !ReferenceEquals(Session, ZNet.instance) || Session == null || Session.GetWorld() == null || World == 0L || Session.GetWorldUID() != World || Application.isBatchMode ||
                MasteryPlugin.Settings?.Enabled?.Value != true || MasteryPlugin.Settings.EnablePerkSFX.Value != true ||
                Time.unscaledTime > Deadline)
            { Finish(); return; }
            if (Time.unscaledTime < NextAttempt) return;
            NextAttempt = Time.unscaledTime + .15f;
            GameObject native = NativePerkAssetResolver.Resolve("sfx_dverger_ice_aoe_start");
            if (native != null && PerkAudioService.PlayPrefab("magic70_ice_cast", native, Point, .5f, .8f)) Finish();
        }
        private void Finish() { Finished = true; Destroy(this); }
    }
}
