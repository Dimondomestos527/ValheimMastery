using System;
using UnityEngine;
namespace ValheimMastery {
 // Uses the existing inactive factory and real safe LateUpdate coordinator.
 internal sealed class EnchantmentNativeLeaf:IDisposable {
  internal GameObject Root;internal ParticleSystem Particle;GameObject Template;bool Disposed;ParticleSystem.MinMaxGradient BaseLifetimeColor;
  readonly NativeEnchantmentEmitter Moving=new NativeEnchantmentEmitter();
  internal void QueueMoving(Vector3 point,Vector3 velocity,float life,Color? color){if(Ready)Moving.Queue(point,velocity,life,color);}
  internal void FlushMoving(){if(Ready)Moving.Flush(Particle);else Moving.Clear();}
  internal bool Ready=>!Disposed&&Root!=null&&Particle!=null&&Root.GetComponent<DeferredNativeVfx>()?.IsPrepared==true;
  internal static EnchantmentNativeLeaf Create(GameObject source,int cap,Action<EnchantmentNativeLeaf> configure,string excludedChild=null,bool nativeMotion=false){
   if(source==null||source.GetComponentInChildren<ParticleSystem>(true)==null||cap<=0)return null;
   var primarySource=source.GetComponent<ParticleSystem>();var fallbackSource=excludedChild==null?null:source.transform.Find(excludedChild)?.GetComponent<ParticleSystem>();
   if(excludedChild!=null&&(primarySource==null||fallbackSource==null||fallbackSource.transform.parent!=source.transform||fallbackSource.GetComponentsInChildren<ParticleSystem>(true).Length!=1||source.GetComponentsInChildren<ParticleSystem>(true).Length!=2))return null;
   var primaryMaterial=primarySource?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;var fallbackMaterial=fallbackSource?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
   var result=new EnchantmentNativeLeaf();
   try{
    result.Template=new GameObject("vfx_VM_EnchantmentLeaf");result.Template.SetActive(false);
    var native=UnityEngine.Object.Instantiate(source,result.Template.transform,false);native.SetActive(true);
    result.Root=PerkNativeFeedback.CreateVisualOnly(result.Template);
    if(result.Root==null){result.Dispose();return null;}
    result.Root.GetComponent<DeferredNativeVfx>().AfterReady(()=>{
     if(result.Disposed||result.Root==null)return;
     foreach(var light in result.Root.GetComponentsInChildren<Light>(true)){light.enabled=false;UnityEngine.Object.Destroy(light);}
     var systems=result.Root.GetComponentsInChildren<ParticleSystem>(true);
     foreach(var ps in systems){ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var e=ps.emission;e.enabled=false;}
     if(excludedChild!=null){
      // The verified swamp donor has one modern root PS and one legacy child
      // fallback. Remove that exact instance child only during safe LateUpdate.
      ParticleSystem primary=null;Transform fallback=null;
      foreach(var ps in systems){var child=ps.transform.Find(excludedChild);var legacy=child?.GetComponent<ParticleSystem>();if(legacy!=null&&child.parent==ps.transform&&ps.GetComponent<ParticleSystemRenderer>()?.sharedMaterial==primaryMaterial&&legacy.GetComponent<ParticleSystemRenderer>()?.sharedMaterial==fallbackMaterial){primary=ps;fallback=child;break;}}
      if(systems.Length!=2||primary==null||fallback==null){result.Dispose();return;}
      UnityEngine.Object.DestroyImmediate(fallback.gameObject);systems=result.Root.GetComponentsInChildren<ParticleSystem>(true);
     }
     if(systems.Length!=1){result.Dispose();return;}
     result.Particle=systems[0];var main=result.Particle.main;main.maxParticles=cap;main.simulationSpace=ParticleSystemSimulationSpace.World;
     if(!nativeMotion){var noise=result.Particle.noise;noise.enabled=false;var velocity=result.Particle.velocityOverLifetime;velocity.enabled=false;var force=result.Particle.forceOverLifetime;force.enabled=false;}
     var lifetimeColor=result.Particle.colorOverLifetime;result.BaseLifetimeColor=lifetimeColor.enabled?NativeTinyPaletteLease.Copy(lifetimeColor.color):new ParticleSystem.MinMaxGradient(Color.white);
     configure?.Invoke(result);if(!result.Disposed)result.Particle.Play(false);
    });
    result.Root.SetActive(true);return result;
   }catch{result.Dispose();throw;}
  }
  // Scale from a detached immutable profile, never the previous frame's gradient.
  internal void SetOpacity(float alpha){if(!Ready)return;var color=Particle.colorOverLifetime;color.enabled=true;color.color=Scale(BaseLifetimeColor,Mathf.Clamp01(alpha));}
  static Color Scale(Color c,float a){c.a*=a;return c;}
  static Gradient Scale(Gradient source,float alpha){var colors=source.colorKeys;var alphas=source.alphaKeys;for(int i=0;i<alphas.Length;i++)alphas[i].alpha*=alpha;var value=new Gradient{mode=source.mode};value.SetKeys(colors,alphas);return value;}
  static ParticleSystem.MinMaxGradient Scale(ParticleSystem.MinMaxGradient value,float alpha){switch(value.mode){
   case ParticleSystemGradientMode.Color:return new ParticleSystem.MinMaxGradient(Scale(value.color,alpha));
   case ParticleSystemGradientMode.TwoColors:return new ParticleSystem.MinMaxGradient(Scale(value.colorMin,alpha),Scale(value.colorMax,alpha));
   case ParticleSystemGradientMode.Gradient:return new ParticleSystem.MinMaxGradient(Scale(value.gradient,alpha));
   case ParticleSystemGradientMode.TwoGradients:return new ParticleSystem.MinMaxGradient(Scale(value.gradientMin,alpha),Scale(value.gradientMax,alpha));
   case ParticleSystemGradientMode.RandomColor:var random=new ParticleSystem.MinMaxGradient(Scale(value.gradient,alpha));random.mode=ParticleSystemGradientMode.RandomColor;return random;
   default:throw new InvalidOperationException("Unsupported lifetime gradient mode");}}
  public void Dispose(){if(Disposed)return;Disposed=true;Moving.Clear();if(Root!=null){Root.SetActive(false);UnityEngine.Object.Destroy(Root);}if(Template!=null)UnityEngine.Object.Destroy(Template);Root=Template=null;Particle=null;}
 }
}
