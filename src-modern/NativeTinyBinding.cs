using UnityEngine;
namespace ValheimMastery {
 // Cheap exact hierarchy lookup at bounded cadence only while consumer is eligible.
 internal sealed class NativeTinyBinding {
  Transform Root;ParticleSystem Cached;float NextTry;
  internal void Clear(){Root=null;Cached=null;NextTry=0;}
  internal ParticleSystem Resolve(Transform root,float now){
   if(root==null){Clear();return null;}
   if(!ReferenceEquals(Root,root)){Root=root;Cached=null;NextTry=0;}
   if(Cached!=null&&NativeTinyPaletteLease.VerifiedShape(Cached)&&Cached.transform.IsChildOf(root))return Cached;
   if(now<NextTry)return null;NextTry=now+.25f;
   var candidate=root.Find("FollowPlayer/GlobalWindParticles/particles_pixel")?.GetComponent<ParticleSystem>();
   Cached=NativeTinyPaletteLease.VerifiedShape(candidate)?candidate:null;return Cached;
  }
 }
}
