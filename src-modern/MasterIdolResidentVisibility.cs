using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
 // Camera evidence is client cosmetic input, never authority for damage/work/rewards.
 internal sealed class MasterIdolResidentVisibility:MonoBehaviour
 {
  private const string Rpc="VM_Idol_Observed_v1";
  private sealed class Witness:MasterIdolObservationReceipt {internal ZDO Player;}
  private readonly Dictionary<long,Witness> Witnesses=new Dictionary<long,Witness>();
  private readonly List<long> Expired=new List<long>();
  private ZNetView View,Registered;private Character Actor;private ZDO Data;private ZNet Session;private ZDOMan Manager;private ulong Generation;private long Owner,ContextTicks,World;private ushort Revision;
  private float Next;private bool Reported;private long ReportOwner;private ushort ReportRevision;
  private static readonly int SightMask=LayerMask.GetMask("piece","static_solid","terrain");
  private bool SampleValue;private float SampleUntil;private Camera SampleCamera;private Player SamplePlayer;
  private static int Frame=-1;private float RefreshAt;private static readonly MasterIdolObservationRotation<MasterIdolResidentVisibility> Polling=new MasterIdolObservationRotation<MasterIdolResidentVisibility>();
  private void Awake(){View=GetComponent<ZNetView>();Actor=GetComponent<Character>();}
  private bool Context()
  {
   if(View?.IsValid()!=true||ZNet.instance?.GetWorld()==null||!MasterIdolLoadIdentity.LoadReady)return false;
   if(Registered!=View){View.Register<ZPackage>(Rpc,Receive);Registered=View;}
   var data=View.GetZDO();if(data==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return false;
   if(!ReferenceEquals(Data,data)||Session!=ZNet.instance||Manager!=ZDOMan.instance||Generation!=MasterIdolLoadIdentity.Generation||Owner!=data.GetOwner()||Revision!=data.OwnerRevision||World!=Session.GetWorldUID())
   {ClearSample();Witnesses.Clear();Reported=false;Next=0;Data=data;Session=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;Owner=data.GetOwner();Revision=data.OwnerRevision;World=Session.GetWorldUID();ContextTicks=Session.GetTime().Ticks;}
   return true;
  }
  private void ClearSample(){SampleValue=false;SampleUntil=0;SampleCamera=null;SamplePlayer=null;}
  private static bool LocalContext(out Player player,out Camera camera)
  {
   player=Player.m_localPlayer;camera=GameCamera.instance?.m_camera;
   // Native TakeInput gates Chat, Console, TextInput, StoreGui, InventoryGui,
   // Menu, TextViewer and Minimap. A604 Mastery is hosted by visible InventoryGui.
   return Application.isFocused&&!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null&&
    MasteryPlugin.Settings.EnablePerkProcVFX.Value&&player!=null&&!player.IsDead()&&!player.IsTeleporting()&&!player.InGhostMode()&&!player.IsDebugFlying()&&
    camera!=null&&camera.isActiveAndEnabled&&GameCamera.instance.isActiveAndEnabled&&!InventoryGui.IsVisible()&&player.TakeInput()&&
    !Game.IsPaused()&&!Hud.InRadial()&&!Hud.InBuildUi();
  }
  internal static bool Local(Character actor)
  {
   if(actor==null||!LocalContext(out var player,out var camera))return false;
   var component=actor.GetComponent<MasterIdolResidentVisibility>();
   return component!=null&&component.isActiveAndEnabled&&component.Context()&&component.SampleValue&&
    component.SampleCamera==camera&&component.SamplePlayer==player&&MasterIdolVisibilityRules.Fresh(component.SampleUntil,Time.unscaledTime)&&
    (player.transform.position-actor.transform.position).sqrMagnitude<=MasterIdolVisibilityRules.Radius*MasterIdolVisibilityRules.Radius;
  }
  // Only the four-slot round-robin Poll may sample viewport/LOS. Active gesture
  // and animation updates consume this same short-lived result; they never cast.
  private bool CaptureSample()
  {
   ClearSample();
   if(Actor==null||!LocalContext(out var player,out var camera)||
    (player.transform.position-Actor.transform.position).sqrMagnitude>MasterIdolVisibilityRules.Radius*MasterIdolVisibilityRules.Radius)return false;
   Vector3 point=Actor.transform.position+Vector3.up*.9f;Vector3 screen=camera.WorldToViewportPoint(point);
   SampleCamera=camera;SamplePlayer=player;SampleUntil=Time.unscaledTime+.2f;
   if(!MasterIdolVisibilityRules.Viewport(screen.x,screen.y,screen.z))return false;
   SampleValue=!Physics.Linecast(camera.transform.position,point,out var hit,SightMask,QueryTriggerInteraction.Ignore)||hit.collider?.GetComponentInParent<Character>()==Actor;
   return SampleValue;
  }
  internal static bool Observed(Character actor)
  {
   var component=actor?.GetComponent<MasterIdolResidentVisibility>();return component!=null&&component.Observed();
  }
  private bool Observed()
  {
   if(!Context()||!View.IsOwner()||!MasterIdolResidentPolicy.TryHome(Actor,out var zone)||!zone.Contains(transform.position))return false;
   bool observed=false;Expired.Clear();
   foreach(var pair in Witnesses)
   {
    var witness=pair.Value;var player=witness.Player==null?null:Manager.GetZDO(witness.Player.m_uid);
    if(player==null||!ReferenceEquals(player,witness.Player)||player.GetOwner()!=pair.Key||witness.RetainUntil<=Time.unscaledTime)Expired.Add(pair.Key);else if(MasterIdolVisibilityRules.Fresh(witness.Until,Time.unscaledTime)&&(player.GetPosition()-transform.position).sqrMagnitude<=MasterIdolVisibilityRules.Radius*MasterIdolVisibilityRules.Radius)observed=true;
   }
   foreach(long key in Expired)Witnesses.Remove(key);return observed;
  }
  private void Receive(long sender,ZPackage packet)
  {
   try
   {
    if(packet==null||packet.Size()>96||!Context()||!View.IsOwner())return;
    long world=packet.ReadLong();var actor=MasterIdolResidentWork.ReadUid(packet);int revision=packet.ReadInt();var playerId=MasterIdolResidentWork.ReadUid(packet);long stamp=packet.ReadLong();bool visible=packet.ReadBool();
    if(packet.GetPos()!=packet.Size()||world!=World||actor!=Data.m_uid||revision!=Revision||!MasterIdolVisibilityRules.Stamp(stamp,Session.GetTime().Ticks,ContextTicks))return;
    var playerData=Manager.GetZDO(playerId);if(playerData==null||playerData.GetOwner()!=sender||playerData.GetLong(ZDOVars.s_playerID,0)==0)return;
    bool local=sender==ZNet.GetUID()&&Player.m_localPlayer?.m_nview?.GetZDO()==playerData;
    if(Session.IsServer()&&!local){var peer=Session.GetPeer(sender);if(peer?.IsReady()!=true||peer.m_characterID!=playerId||peer.m_playerID!=playerData.GetLong(ZDOVars.s_playerID,0))return;}
    else if(!Session.IsServer()&&!local&&Session.GetServerPeer()?.IsReady()!=true)return;
    if(!MasterIdolResidentPolicy.TryHome(Actor,out var zone)||!zone.Contains(transform.position)||(playerData.GetPosition()-transform.position).sqrMagnitude>MasterIdolVisibilityRules.Radius*MasterIdolVisibilityRules.Radius||playerData.GetBool(ZDOVars.s_debugFly,false))return;
    Witnesses.TryGetValue(sender,out var old);
    if(old==null){Observed();if(Witnesses.Count>=MasterIdolVisibilityRules.MaxObservers)return;old=new Witness{Player=playerData};Witnesses.Add(sender,old);}
    if(!ReferenceEquals(old.Player,playerData))return;
    old.Accept(stamp,visible,Time.unscaledTime);
   }
   catch(Exception){MasterIdolResidentMetrics.Count("scene.observer-rejected");}
  }
  private void Update()
  {
   if(Time.unscaledTime<RefreshAt)return;RefreshAt=Time.unscaledTime+.5f;
   bool ready=Context(); // Dedicated registers RPCs; it never evaluates a camera or sends witnesses.
   if(Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
   var player=Player.m_localPlayer;
   if(ready&&player!=null&&(player.transform.position-transform.position).sqrMagnitude<=MasterIdolVisibilityRules.Radius*MasterIdolVisibilityRules.Radius&&MasterIdolResidentPolicy.TryHome(Actor,out _))Polling.Add(this);else Polling.Remove(this);
  }
  internal static void Tick()
  {
   if(Application.isBatchMode||SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null||Frame==Time.frameCount)return;Frame=Time.frameCount;
   int count=Math.Min(MasterIdolVisibilityRules.MaxReportsPerFrame,Polling.Count);
   for(int i=0;i<count;i++){var component=Polling.Take();if(component==null){Polling.Remove(component);continue;}try{component.Poll();}catch(Exception){Polling.Remove(component);MasterIdolResidentMetrics.Count("scene.observer-poll-failed");}}
  }
  private void Poll()
  {
   if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.12f;
   if(!Context()||!MasterIdolResidentPolicy.TryHome(Actor,out _)){Polling.Remove(this);return;}
   bool visible=CaptureSample();if(!visible&&!Reported)return;
   if(visible&&Reported&&ReportOwner==Owner&&ReportRevision==Revision&&Time.unscaledTime<SendAt)return;
   var playerData=Player.m_localPlayer?.m_nview?.GetZDO();if(playerData==null)return;
   var packet=new ZPackage();packet.Write(World);MasterIdolResidentWork.WriteUid(packet,Data.m_uid);packet.Write((int)Revision);MasterIdolResidentWork.WriteUid(packet,playerData.m_uid);packet.Write(Session.GetTime().Ticks);packet.Write(visible);
   if(View.IsOwner())Receive(ZNet.GetUID(),new ZPackage(packet.GetArray()));else View.InvokeRPC(Owner,Rpc,packet);
   Reported=visible;ReportOwner=Owner;ReportRevision=Revision;SendAt=Time.unscaledTime+MasterIdolVisibilityRules.ReportCadence;
  }
  private float SendAt;
  private void OnDisable(){ClearSample();Polling.Remove(this);Witnesses.Clear();Reported=false;}
  private void OnDestroy(){ClearSample();Polling.Remove(this);Witnesses.Clear();}
 }
 [HarmonyPatch(typeof(MasteryPlugin),"Update")]
 internal static class MasterIdolVisibilityTickPatch{private static void Postfix()=>MasterIdolResidentVisibility.Tick();}
 [HarmonyPatch(typeof(BaseAI),"Awake")]
 internal static class MasterIdolResidentVisibilityAttachPatch
 {private static void Postfix(BaseAI __instance){if(MasterIdolResidentRoster.Limit(Utils.GetPrefabName(__instance.gameObject))>0&&__instance.GetComponent<MasterIdolResidentVisibility>()==null)__instance.gameObject.AddComponent<MasterIdolResidentVisibility>();}}
}

