using System.Collections.Generic;using UnityEngine;
namespace ValheimMastery {
 // Geometry-change setup only. Native engine owns ordinary lifetime looping.
 internal static class NativeEnchantmentRing {
  internal static long PointKey(Vector3 point){int row=Mathf.RoundToInt(point.z/.47631398f),col=Mathf.RoundToInt(point.x/.55f-((row&1)==0?0:.5f));return ((long)row<<32)^(uint)col;}
  static uint Seed(long key){unchecked{uint value=(uint)key^(uint)(key>>32)*0x9e3779b9u;value^=value>>16;value*=0x7feb352du;value^=value>>15;value*=0x846ca68bu;value^=value>>16;return value==0?1:value;}}
  internal static void Update(ParticleSystem ps,Vector3[] points){
   var old=new ParticleSystem.Particle[ps.particleCount];int oldCount=ps.GetParticles(old);var indexed=new Dictionary<uint,ParticleSystem.Particle>();for(int i=0;i<oldCount;i++)indexed[old[i].randomSeed]=old[i];
   ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.maxParticles=points.Length;main.ringBufferMode=ParticleSystemRingBufferMode.LoopUntilReplaced;main.ringBufferLoopRange=new Vector2(0,1);
   var chosen=new HashSet<uint>();for(int i=0;i<points.Length;i++){uint seed=Seed(PointKey(points[i]));while(!chosen.Add(seed))unchecked{seed++;if(seed==0)seed=1;}ps.Emit(new ParticleSystem.EmitParams{position=points[i],velocity=Vector3.zero,startLifetime=10,randomSeed=seed},1);}
   var particles=new ParticleSystem.Particle[points.Length];int n=ps.GetParticles(particles);for(int i=0;i<n;i++){
    // Native movement changes x/z; stable seeds retain state without snapping
    // an existing firefly back onto its old lattice anchor during a rebuild.
    if(indexed.TryGetValue(particles[i].randomSeed,out var previous))particles[i]=previous;
    else{uint seed=particles[i].randomSeed;particles[i].remainingLifetime=particles[i].startLifetime*(.05f+.9f*((seed*773u)%1000)/1000f);}
   }ps.SetParticles(particles,n);ps.Play(false);
  }
 }
}
