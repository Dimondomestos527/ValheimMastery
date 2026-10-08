using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Prewarm the exact selected donor during ordinary client scene loading, not first damage.
    // Existing shared SoftRefs own the held dependency and release it on scene destruction.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Knife70VisualWarmupPatch
    {
        private static void Postfix(ZNetScene __instance)
        {
            if (!Application.isBatchMode && __instance.GetComponent<Knife70VisualWarmup>() == null)
                __instance.gameObject.AddComponent<Knife70VisualWarmup>();
        }
    }
    internal sealed class Knife70VisualWarmup : MonoBehaviour
    {
        private float NextCheck, Started;
        private bool Warned;
        private void Awake() { Started = Time.time; }
        private void Update()
        {
            if (Time.time < NextCheck) return;
            NextCheck = Time.time + .25f;
            if (Application.isBatchMode || ZNetScene.instance == null) return;
            GameObject donor = NativeSoftVisualAssets.Get<GameObject>("Assets/Characters/Wraith/Wraith.prefab");
            GameObject visual = donor?.transform.Find("Visual")?.gameObject;
            if (visual != null)
            {
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogInfo("[Knife70VFX] exact Wraith donor ready; renderers=" +
                        visual.GetComponentsInChildren<Renderer>(true).Length);
                enabled = false;
            }
            else if (!Warned && Time.time - Started > 8f)
            {
                Warned = true;
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogWarning("[Knife70VFX] exact Wraith donor still pending/unavailable after8s; native asset catalog diagnostics distinguish missing.");
                // Cold load may still finish; bounded1Hz retries, no expired cue replay.
                NextCheck = Time.time + 1f;
            }
            else if (Warned) NextCheck = Time.time + 1f;
        }
    }
}
