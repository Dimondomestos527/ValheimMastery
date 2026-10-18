using UnityEngine;
namespace ValheimMastery {
 // Shared preview/runtime proposal: continuous Plains envelope, no frame-count cadence.
 internal static class EnchantmentActivationMotion {
  internal const float Duration=10;
  static float Smooth(float value)=>Mathf.SmoothStep(0,1,Mathf.Clamp01(value));
  internal static float PlainsRate(float elapsed)=>Smooth(elapsed/.35f)*Mathf.Lerp(600,120,Smooth((elapsed-2)/2));
  internal static float PlainsWaveMix(float elapsed)=>1-Smooth((elapsed-2.5f)/1.5f);
  internal static float PlainsRadius(float elapsed,float radius)=>radius*Smooth(elapsed/3.5f);
  internal static float PlainsSpeed(float elapsed)=>Mathf.Lerp(.022f,.22f,PlainsWaveMix(elapsed));
 }
}
