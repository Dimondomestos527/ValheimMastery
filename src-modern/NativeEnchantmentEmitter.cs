using System;using System.Collections.Generic;using UnityEngine;
namespace ValheimMastery {
 // Temporary authored moving particles only. Native allocation initializes all
 // particle state; this avoids the observed EmitParams/nonzero-velocity blowup.
 internal sealed class NativeEnchantmentEmitter {
  readonly struct Spawn {internal readonly Vector3 Position,Velocity;internal readonly float Life;internal readonly Color? Color;internal Spawn(Vector3 p,Vector3 v,float life,Color? color){Position=p;Velocity=v;Life=life;Color=color;}}
  readonly List<Spawn> Pending=new List<Spawn>(128);readonly HashSet<uint> Existing=new HashSet<uint>();ParticleSystem.Particle[] Buffer=Array.Empty<ParticleSystem.Particle>();
  internal void Queue(Vector3 point,Vector3 velocity,float life,Color? color){Pending.Add(new Spawn(point,velocity,life,color));}
  internal void Clear(){Pending.Clear();Existing.Clear();}
  internal void Flush(ParticleSystem ps){
   if(Pending.Count==0)return;
   try{
    int cap=ps.main.maxParticles;if(Buffer.Length<cap)Buffer=new ParticleSystem.Particle[cap];
    int before=ps.GetParticles(Buffer);if((long)before+Pending.Count>cap)throw new InvalidOperationException("Moving emitter capacity exceeded");
    Existing.Clear();for(int i=0;i<before;i++)if(!Existing.Add(Buffer[i].randomSeed))throw new InvalidOperationException("Existing particle seed identity is ambiguous");
    ps.Emit(Pending.Count);int after=ps.GetParticles(Buffer),next=0;
    for(int i=0;i<after;i++){
     if(Existing.Contains(Buffer[i].randomSeed))continue;
     if(next>=Pending.Count)throw new InvalidOperationException("Unexpected native particle allocation");
     var spawn=Pending[next++];Buffer[i]=Configure(Buffer[i],spawn.Position,spawn.Velocity,spawn.Life,spawn.Color);
    }
    if(next!=Pending.Count||after-before!=Pending.Count)throw new InvalidOperationException("Native allocation identity mismatch");
    ps.SetParticles(Buffer,after);
   }finally{Pending.Clear();Existing.Clear();}
  }
  internal static ParticleSystem.Particle Configure(ParticleSystem.Particle particle,Vector3 position,Vector3 velocity,float life,Color? color){particle.position=position;particle.velocity=velocity;particle.startLifetime=life;particle.remainingLifetime=life;if(color.HasValue)particle.startColor=color.Value;return particle;}
 }
}
