using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery {
 // Isolated provider draft. Root wires only after policy/combined/native acceptance.
 internal static class EnchantmentAreaVisual {
  internal enum BeginResult { Started,Pending,Skipped }
  internal readonly struct Cue {
   internal readonly CraftingStation Station;internal readonly ZDO Data;internal readonly ZDOID Uid;
   internal readonly string Marker,Home,Type;internal readonly ZNet Session;internal readonly ZNetScene Scene;
   internal readonly Player Player;internal readonly long World;internal readonly ulong Generation;internal readonly float StartedAt,Deadline;internal readonly long IssuedTicks,PrepareUntilTicks;internal readonly string EventId;
   internal Cue(CraftingStation station,ZDO data,ZDOID uid,string marker,string home,string type,ZNet session,ZNetScene scene,Player player,long world,ulong generation,float startedAt,float deadline){Station=station;Data=data;Uid=uid;Marker=marker;Home=home;Type=type;Session=session;Scene=scene;Player=player;World=world;Generation=generation;StartedAt=startedAt;Deadline=deadline;IssuedTicks=PrepareUntilTicks=0;EventId=null;}
   internal Cue(CraftingStation station,ZDO data,ZDOID uid,string marker,string home,string type,ZNet session,ZNetScene scene,Player player,long world,ulong generation,long issuedTicks,long prepareUntilTicks,string eventId):this(station,data,uid,marker,home,type,session,scene,player,world,generation,0,0){IssuedTicks=issuedTicks;PrepareUntilTicks=prepareUntilTicks;EventId=eventId;}
  }
  // Explicit configuration. The approved singleton is applied by Root lifecycle,
  // never enabled by static initialization or by a per-frame world scan.
  internal sealed class Policy {
   internal readonly int Tiny,Weather,Fog;internal readonly EnchantmentAmbientArea.Policy Ambient;
   internal Policy(int tiny,int weather,int fog,EnchantmentAmbientArea.Policy ambient=null){if(tiny<1||weather<1||fog<1)throw new ArgumentOutOfRangeException();Tiny=tiny;Weather=weather;Fog=fog;Ambient=ambient;}
  }
  internal static readonly Policy ApprovedTestPolicy=new Policy(32768,2000,24,new EnchantmentAmbientArea.Policy(40,80,120,4096,true));
  static Policy Limits;static readonly Dictionary<CraftingStation,Run> Runs=new Dictionary<CraftingStation,Run>();static readonly List<CraftingStation> Remove=new List<CraftingStation>();
  static int TinyUsed,WeatherUsed,FogUsed;
  static readonly Dictionary<string,long> Admitted=new Dictionary<string,long>(StringComparer.Ordinal);static readonly List<string> Expired=new List<string>();static ZNet HistorySession;static long HistoryWorld;static ulong HistoryGeneration;
  static bool Admit(Cue cue){
   if(cue.EventId==null||cue.EventId.Length<1||cue.EventId.Length>128||cue.IssuedTicks<=0||cue.PrepareUntilTicks-cue.IssuedTicks!=TimeSpan.TicksPerSecond*3)return false;
   long now=cue.Session.GetTime().Ticks;if(now<cue.IssuedTicks||now>=cue.PrepareUntilTicks)return false;
   if(!ReferenceEquals(HistorySession,cue.Session)||HistoryWorld!=cue.World||HistoryGeneration!=cue.Generation){Admitted.Clear();HistorySession=cue.Session;HistoryWorld=cue.World;HistoryGeneration=cue.Generation;}
   Expired.Clear();foreach(var pair in Admitted)if(now>=pair.Value)Expired.Add(pair.Key);foreach(var id in Expired)Admitted.Remove(id);
   if(Admitted.ContainsKey(cue.EventId)||Admitted.Count>=512)return false;
   Admitted.Add(cue.EventId,cue.PrepareUntilTicks);return true;
  }
  static int Share(Run owner,int channel){
   var active=new List<Run>();var requests=new List<int>();foreach(var run in Runs.Values)if(!run.Disposed){active.Add(run);requests.Add(run.Desired(channel));}
   int limit=channel==0?Math.Max(0,Limits.Tiny-EnchantmentAmbientArea.ReservedTiny):channel==1?Limits.Weather:Limits.Fog;
   var shares=EnchantmentAreaBudget.Allocate(limit,requests.ToArray());int index=active.IndexOf(owner);return index<0?0:shares[index];
  }
  internal static void Configure(Policy policy){if(ReferenceEquals(Limits,policy))return;Clear();Limits=policy;}
  static bool Graphics=>!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
  internal static BeginResult TryBegin(Cue cue){
   if(Limits==null||!Graphics||!MasteryPlugin.Settings.Enabled.Value||!MasteryPlugin.Settings.EnablePerkProcVFX.Value||!WorkbenchEnchantmentRules.Known(cue.Type)||cue.Station==null||!Finite(cue.StartedAt)||!Finite(cue.Deadline))return BeginResult.Skipped;
   var run=new Run(cue);if(!run.Current()||!Admit(cue)){run.Dispose();return BeginResult.Skipped;}Cancel(cue.Station,"replaced");Runs.Add(cue.Station,run);
   try{run.Tick();return run.Started?BeginResult.Started:BeginResult.Pending;}catch(Exception error){Runs.Remove(cue.Station);run.Dispose();WorkbenchEnchantmentVisualTrace.Write("area-error",cue.Type,error.GetType().Name);return BeginResult.Skipped;}
  }
  static int ForegroundTinyDemand(){long total=0;foreach(var run in Runs.Values)if(!run.Disposed)total+=run.Desired(0);return Limits==null?0:(int)Math.Min(Limits.Tiny,total);}
  internal static void Tick(){
   if(!Graphics){Clear();return;}
   try{EnchantmentAmbientArea.Tick(Limits?.Ambient,Limits!=null?Math.Max(0,Limits.Tiny-Math.Max(TinyUsed,ForegroundTinyDemand())):0);}catch(Exception error){EnchantmentAmbientArea.Clear();WorkbenchEnchantmentVisualTrace.Write("ambient-error","all",error.GetType().Name);}
   Remove.Clear();foreach(var pair in Runs){var run=pair.Value;try{if(!run.Current()){run.Dispose();Remove.Add(pair.Key);continue;}run.Tick();}catch(Exception error){run.Dispose();Remove.Add(pair.Key);WorkbenchEnchantmentVisualTrace.Write("area-error","cue",error.GetType().Name);}}
   foreach(var key in Remove)Runs.Remove(key);
  }
  internal static void Cancel(CraftingStation station,string reason){if(ReferenceEquals(station,null))return;if(Runs.TryGetValue(station,out var run)){Runs.Remove(station);run.Dispose();}}
  internal static void Clear(){foreach(var run in Runs.Values)run.Dispose();Runs.Clear();Remove.Clear();EnchantmentAmbientArea.Clear();TinyUsed=WeatherUsed=FogUsed=0;}
  static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
  sealed class Run:IDisposable {
   readonly Cue Cue;string Marker,Home;internal bool Disposed;bool Reserved,SwampScheduled,BudgetReported;int TinyCap,WeatherCap,FogCap,Serial,BurstMask,GeometrySignature;float Last,NextAsset,NextGeometry,NextBudget,TinyCredit,WeatherCredit,ReadyAt=-1,EndsAt,WeatherRate,TinyScale=1;
   EnchantmentNativeLeaf Tiny,Cloud,Weather,Green;readonly Vector3[] CloudPoints=new Vector3[24];readonly Color[] CloudColors=new Color[24];readonly ParticleSystem.Particle[] CloudParticles=new ParticleSystem.Particle[24];
   MasterIdolEffectZone Zone,AreaZone;MasterIdolEffectCircle[] AreaCircles;double[] CircleWeights;double TotalCircleWeight;float CachedAreaFactor;bool CloudInitialized;internal bool Started;
   internal Run(Cue cue){Cue=cue;Marker=cue.Marker??"";Home=Marker.Length==0?null:cue.Home;Last=Time.unscaledTime;}
   internal bool Current(){
    if(Disposed||!Graphics||!MasteryPlugin.Settings.Enabled.Value||!MasteryPlugin.Settings.EnablePerkProcVFX.Value||Cue.Station==null||!Cue.Station.isActiveAndEnabled||Cue.Session!=ZNet.instance||Cue.Scene!=ZNetScene.instance||Cue.Player!=Player.m_localPlayer||Cue.Player==null||Cue.World==0||Cue.Session?.GetWorld()==null||Cue.World!=Cue.Session.GetWorldUID()||Cue.Generation!=MasterIdolLoadIdentity.Generation||(!Started&&(Cue.EventId==null||Cue.IssuedTicks<=0||Cue.PrepareUntilTicks-Cue.IssuedTicks!=TimeSpan.TicksPerSecond*3||Cue.Session.GetTime().Ticks<Cue.IssuedTicks||Cue.Session.GetTime().Ticks>=Cue.PrepareUntilTicks))||(Started&&(Time.unscaledTime<ReadyAt||Time.unscaledTime>=EndsAt)))return false;
    var view=Cue.Station.GetComponent<ZNetView>();var data=view?.GetZDO();if(data==null||view?.IsValid()!=true||!ReferenceEquals(data,Cue.Data)||data.m_uid!=Cue.Uid||data.GetPrefab()!="piece_workbench".GetStableHashCode())return false;
    string marker=data.GetString(WorkbenchEnchantmentRuntime.StationKey,"");
    if(Marker.Length==0&&Guid.TryParseExact(marker,"N",out _)){Marker=marker;Home=Cue.Uid+"/"+marker;}
    if(marker!=Marker)return false;
    return Marker.Length==0||Guid.TryParseExact(Marker,"N",out _)&&Home==Cue.Uid+"/"+Marker;
   }
   internal void Tick(){
    float now=Time.unscaledTime,t=Started?now-ReadyAt:0,dt=Mathf.Clamp(now-Last,0,.1f);Last=now;
    if(Limits==null||Home==null)return;
    Zone=MasterIdolEffectZones.Find(Home);
    if(Zone==null||Zone.Type!=Cue.Type||(Zone.Home-Cue.Data.GetPosition()).sqrMagnitude>.01f){StopLeaves();return;}
    if(now>=NextGeometry){NextGeometry=now+.25f;int signature=17;unchecked{foreach(var circle in Zone.Circles){signature=signature*31+circle.Point.GetHashCode();signature=signature*31+circle.Radius.GetHashCode();}}if(GeometrySignature!=signature){GeometrySignature=signature;StopLeaves();ReleaseBudget();AreaZone=null;}}
    if(now>=NextBudget){NextBudget=now+.25f;int tiny=Share(this,0),weather=Share(this,1),fog=Share(this,2);if(Reserved&&(tiny!=TinyCap||weather!=WeatherCap||fog!=FogCap)){StopLeaves();ReleaseBudget();}}
    if(!Reserved&&!Reserve())return;
    if(now>=NextAsset){NextAsset=now+.25f;Prepare(t);}
    bool ready=(TinyCap==0||Tiny?.Ready==true)&&(FogCap==0||Cloud?.Ready==true)&&(WeatherCap==0||(Cue.Type=="BlackForest"?Green?.Ready==true:Weather?.Ready==true));
    if(!ready)return;
    if(!Started){if(Cue.Session.GetTime().Ticks>=Cue.PrepareUntilTicks)return;Started=true;ReadyAt=now;EndsAt=now+10;Last=now;dt=0;WorkbenchEnchantmentVisualTrace.Write("area-started",Cue.Type,"duration=10 tiny="+TinyCap+" weather="+WeatherCap+" fog="+FogCap+" factor="+AreaFactor());}
    if(Cue.Type=="Swamp"&&!SwampScheduled){MasterIdolSwampDomeVisual.BeginActivation(Home,ReadyAt,EndsAt);SwampScheduled=true;}
    float stage=Cue.Type=="Meadows"?t*.4f:Cue.Type=="Swamp"?t*.58f:Cue.Type=="Mistlands"?t*.55f:t;
    if(Cue.Type=="BlackForest"){
     for(int i=0;i<3;i++){float at=i*3.75f;if(t>=at&&(BurstMask&(1<<i))==0){BurstMask|=1<<i;if(t-at<.15f)for(int n=0;n<60;n++)Emit(Green,Sample(Serial++,1.5f),Vector3.up*.2f,2.5f,null);}}
    }
    if(Cue.Type=="Mountain"&&t<8.5f)for(int n=Count(WeatherRate,dt,ref WeatherCredit);n>0;n--)Emit(Weather,Sample(Serial++,5+(Serial%13)*.4f),Vector3.down*1.2f,3,null);
    if(Cue.Type=="Swamp"&&stage>=1&&stage<3.6f)for(int n=Count(WeatherRate,dt,ref WeatherCredit);n>0;n--)Emit(Weather,Sample(Serial++,10+(Serial%5)*.4f),Vector3.down*12,1.1f,null);
    if(Cue.Type=="Plains")for(int n=Count(EnchantmentActivationMotion.PlainsRate(t)*AreaFactor()*TinyScale,dt,ref TinyCredit);n>0;n--){
     int id=Serial++;var point=Sample(id,.8f+(id%7)*.17f);var circle=Zone.Circles[id%Zone.Circles.Length];float angle=id*2.39996323f,r=EnchantmentActivationMotion.PlainsRadius(t,circle.Radius);
     var ring=new Vector3(circle.Point.x+Mathf.Cos(angle)*r,point.y,circle.Point.z+Mathf.Sin(angle)*r);point=Vector3.Lerp(point,ring,EnchantmentActivationMotion.PlainsWaveMix(t));
     if(Zone.Contains(point))Emit(Tiny,point,new Vector3(EnchantmentActivationMotion.PlainsSpeed(t),0,.005f),3,new Color(1,.71f,.24f,.32f));
    }
    if(Cue.Type!="Plains"&&Tiny?.Ready==true&&(Cue.Type=="Meadows"||Cue.Type=="Mountain"&&t>=8.5f||Cue.Type=="Swamp"&&stage>=4||Cue.Type=="Mistlands"&&stage>=3.5f)){
     float rate=(Cue.Type=="Meadows"?180:120)*AreaFactor()*TinyScale;for(int n=Count(rate,dt,ref TinyCredit);n>0;n--){int id=Serial++;Color a=Cue.Type=="Meadows"?new Color(.66f,.86f,.34f,.3f):Cue.Type=="Swamp"?new Color(.46f,.74f,.25f,.28f):Cue.Type=="Mountain"?new Color(.6f,.78f,.98f,.28f):Color.Lerp(new Color(.95f,.34f,.7f,.32f),new Color(.58f,.26f,.94f,.32f),(id%11)/10f);if(Cue.Type=="Meadows")a=Color.Lerp(a,new Color(1,.82f,.29f,.3f),(id%11)/10f);Emit(Tiny,Sample(id,.8f+(id%7)*.17f),Cue.Type=="Meadows"?new Vector3(.25f,.04f,.03f):new Vector3(.04f,.025f,.025f),3,a);}
    }
    if(Cloud?.Ready==true)CloudTick(stage);
    Tiny?.FlushMoving();Weather?.FlushMoving();Green?.FlushMoving();
    if(Cue.Type=="Mountain"&&t>=9)Weather?.SetOpacity(Mathf.Clamp01(10-t));
    if(t>=9.4f)Tiny?.SetOpacity(Mathf.Clamp01((10-t)/.6f));
   }
   float AreaFactor(){if(!ReferenceEquals(AreaZone,Zone)||!ReferenceEquals(AreaCircles,Zone.Circles)){AreaZone=Zone;AreaCircles=Zone.Circles;CachedAreaFactor=Mathf.Max(.01f,(float)(EnchantmentUnionArea.Measure(Zone.Circles)/(Math.PI*400)));CircleWeights=new double[Zone.Circles.Length];TotalCircleWeight=0;for(int i=0;i<CircleWeights.Length;i++){TotalCircleWeight+=Zone.Circles[i].Radius*Zone.Circles[i].Radius;CircleWeights[i]=TotalCircleWeight;}}return CachedAreaFactor;}
   internal int Desired(int channel){
    float factor=Zone==null?1:AreaFactor();
    if(channel==2)return Cue.Type=="Meadows"||Cue.Type=="Swamp"||Cue.Type=="Mistlands"?24:0;
    if(channel==1)return Cue.Type=="Mountain"?EnchantmentAreaBudget.Capacity(135*factor,3):Cue.Type=="Swamp"?EnchantmentAreaBudget.Capacity(240*factor,1.1f):Cue.Type=="BlackForest"?180:0;
    return Cue.Type=="Plains"?Mathf.CeilToInt(2000*factor):Cue.Type=="BlackForest"?0:EnchantmentAreaBudget.Capacity((Cue.Type=="Meadows"?180:120)*factor,3);
   }
   bool Reserve(){
    float factor=AreaFactor();TinyCap=Share(this,0);WeatherCap=Share(this,1);FogCap=Share(this,2);
    if(Desired(0)>0&&TinyCap<2||Desired(1)>0&&WeatherCap<2||Desired(2)>0&&FogCap<1||(long)TinyUsed+EnchantmentAmbientArea.ReservedTiny+TinyCap>Limits.Tiny||(long)WeatherUsed+WeatherCap>Limits.Weather||(long)FogUsed+FogCap>Limits.Fog){if(!BudgetReported){BudgetReported=true;WorkbenchEnchantmentVisualTrace.Write("area-budget-pending",Cue.Type,"bounded shared quota preparing whole union");}return false;}
    WeatherRate=Cue.Type=="Mountain"?EnchantmentAreaBudget.Rate(135*factor,3,WeatherCap):Cue.Type=="Swamp"?EnchantmentAreaBudget.Rate(240*factor,1.1f,WeatherCap):0;
    float peak=(Cue.Type=="Plains"?600:Cue.Type=="Meadows"?180:120)*factor;TinyScale=TinyCap>0?EnchantmentAreaBudget.Rate(peak,3,TinyCap)/peak:1;
    TinyUsed+=TinyCap;WeatherUsed+=WeatherCap;FogUsed+=FogCap;Reserved=true;return true;
   }
   void Prepare(float t){
    if(TinyCap>0&&Tiny==null)Tiny=Own("Assets/Systems/_Environment.prefab","FollowPlayer/GlobalWindParticles/particles_pixel",TinyCap);
    if(Cue.Type=="Meadows"&&Cloud==null)Cloud=Own("Assets/Effects/vfx_swamp_mist.prefab",null,FogCap);
    if(Cue.Type=="Mistlands"&&Cloud==null)Cloud=Own("Assets/Effects/vfx_mistlands_mist.prefab",null,FogCap);
    if(Cue.Type=="Swamp"){
     if(Cloud==null)Cloud=Own("Assets/Effects/vfx_swamp_mist.prefab",null,FogCap);
     if(Weather==null)Weather=Own("Assets/Systems/_Environment.prefab","FollowPlayer/LightRain/Rain",WeatherCap);
    }
    if(Cue.Type=="Mountain"&&Weather==null)Weather=Own("Assets/Systems/_Environment.prefab","FollowPlayer/Snow/snow",WeatherCap);
    if(Cue.Type=="BlackForest"&&Green==null)Green=Own("Assets/Characters/GreyDwarf/misc/shaman_heal_aoe.prefab","flames_world",WeatherCap);
   }
   EnchantmentNativeLeaf Own(string path,string child,int cap){var donor=NativeSoftVisualAssets.Get<GameObject>(path);if(donor==null)return null;var source=child==null?donor:donor.transform.Find(child)?.gameObject;if(source==null)return null;return EnchantmentNativeLeaf.Create(source,cap,value=>{if(path=="Assets/Effects/vfx_mistlands_mist.prefab"){var main=value.Particle.main;main.startSizeMultiplier*=1.1f;}},path=="Assets/Effects/vfx_swamp_mist.prefab"&&child==null?"vfx_swamp_mist (1)":null);}
   Vector3 Sample(int id,float height){
    AreaFactor();var circles=Zone.Circles;Vector3 last=circles[0].Point+Vector3.up*height;
    for(int trial=0;trial<64;trial++){
     uint hash=unchecked((uint)(id*2654435761u+trial*12289u));double target=(hash%100000)/100000d*TotalCircleWeight;int index=Array.BinarySearch(CircleWeights,target);if(index<0)index=~index;index=Math.Min(index,circles.Length-1);var circle=circles[index];hash=hash*1664525u+1013904223u;float radius=Mathf.Sqrt((hash%100000)/100000f)*circle.Radius;hash=hash*1664525u+1013904223u;float angle=(hash%100000)*Mathf.PI*2/100000f;last=circle.Point+new Vector3(Mathf.Cos(angle)*radius,height,Mathf.Sin(angle)*radius);
     int covers=0;foreach(var covering in circles)if(covering.Contains(last))covers++;hash=hash*1664525u+1013904223u;if(covers>0&&hash%(uint)covers==0)return last;
    }
    // Bounded cosmetic sampling under extreme overlap; last point remains in union.
    return last;
   }
   void CloudTick(float t){
    var ps=Cloud.Particle;
    if(!CloudInitialized){for(int i=0;i<FogCap;i++){CloudPoints[i]=Sample(i,Cue.Type=="Meadows"?.8f:Cue.Type=="Swamp"?2.2f:2.5f);Emit(Cloud,CloudPoints[i],Vector3.zero,12,null,(uint)(i+1));}int count=ps.GetParticles(CloudParticles);for(int i=0;i<count;i++)CloudColors[(int)CloudParticles[i].randomSeed-1]=CloudParticles[i].startColor;CloudInitialized=true;}
    int n=ps.GetParticles(CloudParticles);for(int i=0;i<n;i++){
     int id=(int)CloudParticles[i].randomSeed-1;var point=CloudPoints[id];var color=CloudColors[id];float alpha=1;
     if(Cue.Type=="Meadows"){point.x+=t*.15f;color=new Color(.4f,.65f,.27f,.045f);alpha=Mathf.Clamp01(Mathf.Min(t/.8f,(4-t)/2));}
     else if(Cue.Type=="Mistlands"){color.a=Mathf.Min(1,color.a*1.2f);float angle=Mathf.Min(t,3)*.55f;var local=point-Zone.Home;float outward=1+Mathf.Max(0,t-3)*.22f;point.x=Zone.Home.x+(local.x*Mathf.Cos(angle)-local.z*Mathf.Sin(angle))*outward;point.z=Zone.Home.z+(local.x*Mathf.Sin(angle)+local.z*Mathf.Cos(angle))*outward;alpha=Mathf.Clamp01((5.5f-t)/2.5f);}
     else if(MasterIdolSwampDomeVisual.TryRead(Home,out var shell)){
      var delta=point-shell.Center;delta.y=0;float distance=delta.magnitude;if(shell.Active&&distance<shell.TargetRadius*shell.Opening01){var dir=delta.sqrMagnitude>0?delta.normalized:Vector3.right;point.x=shell.Center.x+dir.x*(shell.TargetRadius*shell.Opening01+1);point.z=shell.Center.z+dir.z*(shell.TargetRadius*shell.Opening01+1);alpha=Mathf.Clamp01((5.8f-t)/2.8f);}else alpha=Mathf.Min(1,t/.8f);
     }else alpha=Mathf.Clamp01((5.8f-t)/2.8f);
     color.a*=alpha;CloudParticles[i].position=point;CloudParticles[i].startColor=color;
    }ps.SetParticles(CloudParticles,n);
   }
   void StopLeaves(){Tiny?.Dispose();Cloud?.Dispose();Weather?.Dispose();Green?.Dispose();Tiny=Cloud=Weather=Green=null;CloudInitialized=false;}
   void ReleaseBudget(){if(Reserved){TinyUsed-=TinyCap;WeatherUsed-=WeatherCap;FogUsed-=FogCap;Reserved=false;}}
   public void Dispose(){if(Disposed)return;Disposed=true;if(!Started&&Cue.EventId!=null)WorkbenchEnchantmentVisualTrace.Write("area-preparation-skipped",Cue.Type,"no ready display; no replay");StopLeaves();if(SwampScheduled)MasterIdolSwampDomeVisual.CancelActivation(Home);ReleaseBudget();}
  }
  // Emit runs before the engine expires this frame's old particles; reserve one
  // maximum producer step (.1s) and fractional emission credit, not rate*life only.
  static int WeatherCapacity(float rate,float life,float factor)=>Mathf.CeilToInt(rate*life*factor)+Mathf.CeilToInt(rate*.1f*factor)+1;
  static int Count(float rate,float dt,ref float credit){credit+=rate*dt;int count=Mathf.FloorToInt(credit);credit-=count;return count;}
  static void Emit(EnchantmentNativeLeaf leaf,Vector3 point,Vector3 velocity,float life,Color? color,uint seed=0){if(leaf?.Ready!=true)return;if(velocity.sqrMagnitude>0){leaf.QueueMoving(point,velocity,life,color);return;}var value=new ParticleSystem.EmitParams{position=point,velocity=velocity,startLifetime=life};if(seed!=0)value.randomSeed=seed;if(color.HasValue)value.startColor=color.Value;leaf.Particle.Emit(value,1);}
 }
}
