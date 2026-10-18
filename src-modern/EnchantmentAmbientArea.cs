using System;using System.Collections.Generic;using UnityEngine;
namespace ValheimMastery {
 internal static class EnchantmentAmbientArea {
  internal sealed class Policy {
   internal readonly float Full,Half,Quarter;internal readonly int CellsPerFrame;internal readonly bool MixedPalettes;
   internal Policy(float full,float half,float quarter,int cellsPerFrame,bool mixedPalettes){if(full<=0||half<full||quarter<half||cellsPerFrame<=0)throw new ArgumentOutOfRangeException();Full=full;Half=half;Quarter=quarter;CellsPerFrame=cellsPerFrame;MixedPalettes=mixedPalettes;}
  }
  static readonly string[] Types={"Meadows","BlackForest","Swamp","Mountain","Plains","Mistlands"};
  static readonly Color[] Colors={new Color(.66f,.86f,.34f),new Color(.25f,.48f,1),new Color(.46f,.74f,.25f),new Color(.6f,.78f,.98f),new Color(1,.71f,.24f),new Color(.95f,.34f,.7f)};
  static readonly NativeTinyPaletteLease ColorLease=new NativeTinyPaletteLease();
  static readonly NativeTinyBinding Binding=new NativeTinyBinding();
  static EnvMan Environment;static ParticleSystem NativeTiny;static ParticleSystem.MinMaxGradient BaseColor;static NativeTinyPaletteLease.Context Context;
  static readonly List<MasterIdolEffectZone> Nearby=new List<MasterIdolEffectZone>();static readonly List<Retiring> Old=new List<Retiring>();
  static MasterIdolEffectGeometry Projection;static Build Builder;static EnchantmentNativeLeaf Blue;static int BlueCount,Signature,LastCapacity=-1;static float Next;static bool BudgetReported;static Policy LastPolicy;
  sealed class Retiring {internal EnchantmentNativeLeaf Leaf;internal int Count;internal float At;}
  internal static int ReservedTiny {get{int total=BlueCount;foreach(var old in Old)total+=old.Count;return total;}}
  static NativeTinyPaletteLease.Context Current(){var net=ZNet.instance;return new NativeTinyPaletteLease.Context(net,ZNetScene.instance,net?.GetWorld()!=null?net.GetWorldUID():0,MasterIdolLoadIdentity.Generation);}
  internal static void Clear(){ColorLease.Release(ColorLease.HasLease?Current():default);Binding.Clear();Blue?.Dispose();Blue=null;foreach(var old in Old)old.Leaf.Dispose();Old.Clear();BlueCount=Signature=0;LastCapacity=-1;Builder=null;Environment=null;NativeTiny=null;Projection=null;LastPolicy=null;Next=0;Context=default;}
  static void Retire(){if(Blue==null)return;Old.Add(new Retiring{Leaf=Blue,Count=BlueCount,At=Time.unscaledTime});Blue=null;BlueCount=0;}
  internal static void Tick(Policy policy,int capacity){
   if(policy==null||Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){Clear();return;}
   var current=Current();var player=Player.m_localPlayer;var snapshot=MasterIdolEffectZones.Snapshot;
   if(policy==null||!current.Valid||player==null||!MasteryPlugin.Settings.Enabled.Value||!MasteryPlugin.Settings.EnablePerkProcVFX.Value){Clear();return;}
   if(!Context.Same(current)){Clear();Context=current;}
   float now=Time.unscaledTime;if(Blue!=null&&BlueCount>capacity){Retire();Builder=null;Signature=0;LastCapacity=-1;}for(int i=Old.Count-1;i>=0;i--){float age=now-Old[i].At;if(age>=.6f){Old[i].Leaf.Dispose();Old.RemoveAt(i);}else Old[i].Leaf.SetOpacity(1-age/.6f);}
   if(snapshot==null){ColorLease.Release(current);Retire();Builder=null;Projection=null;return;}
   if(now>=Next){Next=now+.25f;
    var selected=new List<Color>();int mask=0;
    for(int i=0;i<Types.Length;i++)if(snapshot.At(Types[i],player.transform.position)!=null){mask|=1<<i;selected.Add(Colors[i]);}
    if(selected.Count>0&&(selected.Count==1||policy.MixedPalettes)){BindNative();if(NativeTiny!=null){if(!ColorLease.HasLease&&!ColorLease.ForeignWriteObserved)BaseColor=NativeTinyPaletteLease.Copy(NativeTiny.main.startColor);ColorLease.Apply(NativeTiny,current,Palette(BaseColor,selected,mask));}else ColorLease.Release(current);}else ColorLease.Release(current);
    var camera=Utils.GetMainCamera();Vector3 observer=camera!=null?camera.transform.position:player.transform.position;
    snapshot.Near("BlackForest",observer,policy.Quarter,Nearby);Nearby.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
    int signature=17;unchecked{foreach(var zone in Nearby){signature=signature*31+zone.Id.GetHashCode();foreach(var circle in zone.Circles){signature=signature*31+circle.Point.GetHashCode();signature=signature*31+circle.Radius.GetHashCode();}}signature=signature*31+Mathf.FloorToInt(observer.x/4);signature=signature*31+Mathf.FloorToInt(observer.z/4);}
    if(!ReferenceEquals(LastPolicy,policy)){Retire();Builder=null;Signature=0;LastPolicy=policy;}Projection=snapshot;
    if(Builder==null&&(signature!=Signature||LastCapacity!=capacity)){Signature=signature;LastCapacity=capacity;BudgetReported=false;Builder=new Build(Nearby,observer,policy);}
   }
   if(Builder==null)return;
   if(Builder.Exceeded&&capacity>0)Builder.ReduceDensity();
   Builder.Step(policy.CellsPerFrame,capacity);if(!Builder.Done)return;
   if(Builder.Exceeded){if(!BudgetReported){BudgetReported=true;WorkbenchEnchantmentVisualTrace.Write("bf-area-budget-pending","BlackForest","preparing lower whole-union density within shared quota");}if(capacity==0)Retire();return;}
   if(Builder.Points.Count==0){Retire();Builder=null;return;}
   int count=Builder.Points.Count;
   if(Blue?.Ready==true){int retiring=ReservedTiny-BlueCount;if((long)retiring+count>capacity)return;NativeEnchantmentRing.Update(Blue.Particle,Builder.Points.ToArray());BlueCount=count;Builder=null;return;}
   if(Blue!=null)return;if((long)ReservedTiny+count>capacity)return;
   var donor=NativeSoftVisualAssets.Get<GameObject>("Assets/Effects/FireFlies.prefab")?.transform.Find("Particle System")?.gameObject;if(donor==null)return;
   var points=Builder.Points.ToArray();var leaf=EnchantmentNativeLeaf.Create(donor,count,value=>NativeEnchantmentRing.Update(value.Particle,points),null,true);if(leaf==null)return;Blue=leaf;BlueCount=count;Builder=null;
  }
  static void BindNative(){
   var env=EnvMan.instance;var candidate=Binding.Resolve(env!=null?env.transform:null,Time.unscaledTime);if(ReferenceEquals(env,Environment)&&ReferenceEquals(candidate,NativeTiny))return;
   ColorLease.Release(Context);Environment=env;NativeTiny=candidate;if(candidate!=null)BaseColor=NativeTinyPaletteLease.Copy(candidate.main.startColor);
  }
  static ParticleSystem.MinMaxGradient Palette(ParticleSystem.MinMaxGradient native,List<Color> colors,int mask){
   if(colors.Count==1){int index=0;while((mask&(1<<index))==0)index++;Color second=colors[0];if(index==0)second=new Color(1,.82f,.29f);if(index==5)second=new Color(.58f,.26f,.94f);colors.Add(second);}
   bool mixed=(mask&(mask-1))!=0;
   Func<Gradient,Gradient> tint=source=>{var gradient=new Gradient{mode=mixed?GradientMode.Fixed:GradientMode.Blend};var keys=new GradientColorKey[mixed?colors.Count+1:2];for(int i=0;i<keys.Length;i++)keys[i]=new GradientColorKey(colors[Math.Min(i,colors.Count-1)],mixed?(float)i/colors.Count:i);gradient.SetKeys(keys,source.alphaKeys);return gradient;};
   Func<float,Gradient> constantAlpha=alpha=>{var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(alpha,0),new GradientAlphaKey(alpha,1)});return tint(gradient);};
   switch(native.mode){
    case ParticleSystemGradientMode.Color:if(!mixed){var a=colors[0];var b=colors[1];a.a=b.a=native.color.a;return new ParticleSystem.MinMaxGradient(a,b);}var many=new ParticleSystem.MinMaxGradient(constantAlpha(native.color.a));many.mode=ParticleSystemGradientMode.RandomColor;return many;
    case ParticleSystemGradientMode.TwoColors:if(!mixed){var a=colors[0];var b=colors[1];a.a=native.colorMin.a;b.a=native.colorMax.a;return new ParticleSystem.MinMaxGradient(a,b);}return new ParticleSystem.MinMaxGradient(constantAlpha(native.colorMin.a),constantAlpha(native.colorMax.a));
    case ParticleSystemGradientMode.Gradient:return new ParticleSystem.MinMaxGradient(tint(native.gradient));
    case ParticleSystemGradientMode.RandomColor:var random=new ParticleSystem.MinMaxGradient(tint(native.gradient));random.mode=ParticleSystemGradientMode.RandomColor;return random;
    case ParticleSystemGradientMode.TwoGradients:return new ParticleSystem.MinMaxGradient(tint(native.gradientMin),tint(native.gradientMax));
    default:throw new InvalidOperationException("Unsupported native ambient color mode");
   }
  }
  sealed class Build {
   const float StepX=.55f,StepZ=.47631398f;readonly List<MasterIdolEffectCircle> Circles=new List<MasterIdolEffectCircle>();readonly HashSet<long> Seen=new HashSet<long>();readonly Vector3 Observer;readonly Policy Policy;
   internal readonly List<Vector3> Points=new List<Vector3>();internal bool Done,Exceeded;internal int FailedCapacity;int Circle,Row,Column,MinColumn,MaxColumn,MaxRow,DensityStride=1;bool Prepared;
   internal Build(List<MasterIdolEffectZone> zones,Vector3 observer,Policy policy){Observer=observer;Policy=policy;foreach(var zone in zones)Circles.AddRange(zone.Circles);}
   internal void ReduceDensity(){if(DensityStride<int.MaxValue/2)DensityStride*=2;Restart();}
   internal void Restart(){Points.Clear();Seen.Clear();Done=Exceeded=Prepared=false;Circle=Row=Column=0;}
   internal void Step(int budget,int capacity){
    while(budget-->0&&!Done){
     if(Circle>=Circles.Count){Done=true;break;}var circle=Circles[Circle];
     if(!Prepared){Row=Mathf.FloorToInt(Mathf.Max(circle.Point.z-circle.Radius,Observer.z-Policy.Quarter)/StepZ);MaxRow=Mathf.CeilToInt(Mathf.Min(circle.Point.z+circle.Radius,Observer.z+Policy.Quarter)/StepZ);MinColumn=Mathf.FloorToInt(Mathf.Max(circle.Point.x-circle.Radius,Observer.x-Policy.Quarter)/StepX)-1;MaxColumn=Mathf.CeilToInt(Mathf.Min(circle.Point.x+circle.Radius,Observer.x+Policy.Quarter)/StepX);Column=MinColumn;Prepared=true;}
     if(Row>MaxRow){Circle++;Prepared=false;continue;}int row=Row,col=Column++;if(Column>MaxColumn){Column=MinColumn;Row++;}
     var point=new Vector3((col+((row&1)==0?0:.5f))*StepX,circle.Point.y+.7f,row*StepZ);if(!circle.Contains(point))continue;
     float distance=(point-Observer).magnitude;if(distance>Policy.Quarter)continue;uint hash=unchecked((uint)(row*73856093)^(uint)(col*19349663));
     // .55m lattice at five-sixths occupancy matches approved ~4000/R20 density.
     uint baseHash=Mix(hash);if(baseHash%6==0)continue;int stride=distance<=Policy.Full?1:distance<=Policy.Half?2:4;if(Mix(hash^0x9e3779b9u)%(uint)stride!=0||Mix(hash^0x85ebca6bu)%(uint)DensityStride!=0)continue;
     long key=((long)row<<32)^(uint)col;if(!Seen.Add(key))continue;point.y+=(hash%17)/40f;
     if(Points.Count>=capacity){FailedCapacity=capacity;Exceeded=Done=true;return;}Points.Add(point);
    }
   }
   static uint Mix(uint value){unchecked{value^=value>>16;value*=0x7feb352du;value^=value>>15;value*=0x846ca68bu;return value^(value>>16);}}
  }
 }
}
