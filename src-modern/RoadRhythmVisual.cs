using UnityEngine;
namespace ValheimMastery
{
    internal sealed class RoadRhythmVisual : MonoBehaviour
    {
        private Player Owner;
        private NativeWindSwirl Wind;
        private RunSpeedWakeVisual Wake;
        internal static void Set(Player player, int stacks)
        {
            bool emergency = Stride70Service.State(player).EmergencyUntil > Time.time;
            if (stacks <= 0 && !emergency) { Clear(player); return; }
            var visual = player.GetComponent<RoadRhythmVisual>() ?? player.gameObject.AddComponent<RoadRhythmVisual>();
            visual.Owner = player;
            if (visual.Wind != null) { Destroy(visual.Wind.gameObject); visual.Wind = null; }
            if (visual.Wake != null) { Destroy(visual.Wake.gameObject); visual.Wake = null; }
            if (visual.Wind != null)
            { visual.Wind.Radius = .45f + stacks * .16f; visual.Wind.Strength = stacks * .7f; visual.Wind.Fade = .6f; }
        }
        internal static void Clear(Player player)
        { var visual = player?.GetComponent<RoadRhythmVisual>(); if (visual != null) Destroy(visual); }
        private void Update()
        {
            if (Owner == null || (Stride70Service.GetStacks(Owner) <= 0 &&
                Stride70Service.State(Owner).EmergencyUntil <= Time.time)) Destroy(this);
        }
        private void OnDestroy()
        {
            if (Wind != null) Destroy(Wind.gameObject);
            if (Wake != null) Destroy(Wake.gameObject);
        }
    }
}
