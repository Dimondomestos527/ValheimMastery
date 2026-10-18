using System;
using UnityEngine;
namespace ValheimMastery {
 // Local scene-instance color lease only. Caller owns native EnvMan binding/context validation.
 // No prefab/material, emission, size, wind, world-state, or particle-list mutation.
 internal sealed class NativeTinyPaletteLease {
  internal readonly struct Context {
   internal readonly object Session,Scene;internal readonly long World;internal readonly ulong Generation;
   internal Context(object session,object scene,long world,ulong generation){Session=session;Scene=scene;World=world;Generation=generation;}
   internal bool Valid=>Session!=null&&Scene!=null&&World!=0;
   internal bool Same(Context other)=>ReferenceEquals(Session,other.Session)&&ReferenceEquals(Scene,other.Scene)&&World==other.World&&Generation==other.Generation;
  }
  ParticleSystem Particle;Context Captured;ParticleSystem.MinMaxGradient Original,Expected;bool Owns,Foreign;
  internal bool HasLease=>Owns;
  internal bool ForeignWriteObserved=>Foreign;
  internal static bool VerifiedShape(ParticleSystem ps)=>ps!=null&&ps.gameObject.scene.IsValid()&&ps.name=="particles_pixel"&&ps.transform.parent?.name=="GlobalWindParticles"&&ps.transform.parent.parent?.name=="FollowPlayer";
  internal bool Apply(ParticleSystem particle,Context context,ParticleSystem.MinMaxGradient palette){
   if(!context.Valid||!VerifiedShape(particle))return false;
   if(!ReferenceEquals(Particle,particle)||!Captured.Same(context)){
    Release(context);Particle=particle;Captured=context;Foreign=false;var module=particle.main;Original=Copy(module.startColor);Expected=Copy(Original);Owns=true;
   }
   if(Foreign)return false;
   var main=particle.main;
   if(!Same(main.startColor,Expected)){Owns=false;Foreign=true;return false;}
   if(Same(Expected,palette))return true;
   main.startColor=Copy(palette);Expected=Copy(main.startColor);return true;
  }
  internal bool Release(Context current){
   bool restored=false;
   if(Owns&&Particle!=null&&Captured.Same(current)&&VerifiedShape(Particle)){
    var main=Particle.main;if(Same(main.startColor,Expected)){main.startColor=Copy(Original);restored=true;}
   }
   Particle=null;Owns=false;Foreign=false;Captured=default;return restored;
  }
  internal static ParticleSystem.MinMaxGradient Copy(ParticleSystem.MinMaxGradient source){
   switch(source.mode){
    case ParticleSystemGradientMode.Color:return new ParticleSystem.MinMaxGradient(source.color);
    case ParticleSystemGradientMode.TwoColors:return new ParticleSystem.MinMaxGradient(source.colorMin,source.colorMax);
    case ParticleSystemGradientMode.Gradient:return new ParticleSystem.MinMaxGradient(CopyGradient(source.gradient));
    case ParticleSystemGradientMode.TwoGradients:return new ParticleSystem.MinMaxGradient(CopyGradient(source.gradientMin),CopyGradient(source.gradientMax));
    case ParticleSystemGradientMode.RandomColor:var random=new ParticleSystem.MinMaxGradient(CopyGradient(source.gradient));random.mode=ParticleSystemGradientMode.RandomColor;return random;
    default:throw new InvalidOperationException("Unsupported native gradient mode");
   }
  }
  static Gradient CopyGradient(Gradient source){if(source==null)return null;var copy=new Gradient();copy.mode=source.mode;copy.SetKeys(source.colorKeys,source.alphaKeys);return copy;}
  internal static bool Same(ParticleSystem.MinMaxGradient a,ParticleSystem.MinMaxGradient b){
   if(a.mode!=b.mode)return false;
   switch(a.mode){
    case ParticleSystemGradientMode.Color:return a.color.Equals(b.color);
    case ParticleSystemGradientMode.TwoColors:return a.colorMin.Equals(b.colorMin)&&a.colorMax.Equals(b.colorMax);
    case ParticleSystemGradientMode.Gradient:case ParticleSystemGradientMode.RandomColor:return SameGradient(a.gradient,b.gradient);
    case ParticleSystemGradientMode.TwoGradients:return SameGradient(a.gradientMin,b.gradientMin)&&SameGradient(a.gradientMax,b.gradientMax);
    default:return false;
   }
  }
  static bool SameGradient(Gradient a,Gradient b){
   if(a==null||b==null)return a==b;if(a.mode!=b.mode)return false;
   var ac=a.colorKeys;var bc=b.colorKeys;var aa=a.alphaKeys;var ba=b.alphaKeys;
   if(ac.Length!=bc.Length||aa.Length!=ba.Length)return false;
   for(int i=0;i<ac.Length;i++)if(!ac[i].time.Equals(bc[i].time)||!ac[i].color.Equals(bc[i].color))return false;
   for(int i=0;i<aa.Length;i++)if(!aa[i].time.Equals(ba[i].time)||!aa[i].alpha.Equals(ba[i].alpha))return false;
   return true;
  }
 }
}
