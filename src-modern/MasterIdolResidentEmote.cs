using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // One player-owner publisher; the selected native NPC owner alone admits a reaction.
    internal sealed class MasterIdolResidentEmote:MonoBehaviour
    {
        private const float EventSeconds=3f,QueueSeconds=1f;
        private sealed class Observation
        {internal ZDO Data;internal ZNet Net;internal ZDOMan Manager;internal ulong Generation;internal long World,Owner,EdgeAt;internal ushort Revision;internal int Id;}
        private sealed class Target
        {internal Character Actor;internal ZDO Data;internal long Owner;internal ushort Revision;internal float Distance;}
        private sealed class Publication
        {internal Player Player;internal Observation Context;internal int Id,Attempt;internal string Edge,Home;internal long Stamp;internal float Until;internal Target Target;internal readonly List<Target> Targets=new List<Target>(4);}
        private sealed class Receipt
        {internal ZDO PlayerData,ActorData;internal long PlayerOwner,ActorOwner,World;internal ushort PlayerRevision,ActorRevision;internal ZNet Net;internal ZDOMan Manager;internal ulong Generation;internal int Id;internal string Edge;internal byte Verdict;}
        private static readonly ConditionalWeakTable<Player,Observation> Observations=new ConditionalWeakTable<Player,Observation>();
        private readonly ConditionalWeakTable<Player,Receipt> Receipts=new ConditionalWeakTable<Player,Receipt>();
        private static Publication Pending;
        private static ZNet QueueNet;private static ZDOMan QueueManager;private static ulong QueueGeneration;private static int QueueCount;private static long QueueEpoch;
        private Character Actor;private ZNetView View;private MasterIdolSocial Social;private Player Player;private ZDO ActorData,PlayerData;
        private long ActorOwner,PlayerOwner,Stamp,Epoch,World;private ushort ActorRevision,PlayerRevision;private int Id;private string Home;
        private ZNet Net;private ZDOMan Manager;private ulong Generation;private bool Queued;private float Until,Next,NextCleanup;
        private void Awake(){Actor=GetComponent<Character>();View=GetComponent<ZNetView>();Social=GetComponent<MasterIdolSocial>();}
        private static void QueueContext()
        {
            if(QueueNet==ZNet.instance&&QueueManager==ZDOMan.instance&&QueueGeneration==MasterIdolLoadIdentity.Generation)return;
            QueueNet=ZNet.instance;QueueManager=ZDOMan.instance;QueueGeneration=MasterIdolLoadIdentity.Generation;QueueCount=0;QueueEpoch++;Pending=null;
        }
        internal static bool Current(Player player,ZDO data,ushort revision,int id)
        {
            return data!=null&&player!=null&&!player.IsDead()&&!player.IsTeleporting()&&!player.InGhostMode()&&!player.IsDebugFlying()&&player.GetPlayerID()>0&&
                player.m_nview?.IsValid()==true&&ReferenceEquals(player.m_nview.GetZDO(),data)&&ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data)&&
                data.OwnerRevision==revision&&data.GetOwner()!=0&&data.GetInt(ZDOVars.s_emoteID,0)==id&&
                !string.IsNullOrEmpty(data.GetString(ZDOVars.s_emote,""))&&data.GetString(ZDOVars.s_emote,"").Length<=32;
        }
        private static bool Same(Observation value,Player player)
        {
            var data=player?.m_nview?.GetZDO();return data!=null&&ReferenceEquals(value.Data,data)&&value.Net==ZNet.instance&&value.Manager==ZDOMan.instance&&
                value.Generation==MasterIdolLoadIdentity.Generation&&value.World==ZNet.instance.GetWorldUID()&&value.Owner==data.GetOwner()&&value.Revision==data.OwnerRevision;
        }
        internal static void Before(Player player)
        {
            if(player?.m_nview?.IsValid()!=true||ZNet.instance?.GetWorld()==null||!MasterIdolLoadIdentity.LoadReady)return;
            var data=player.m_nview.GetZDO();if(data==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid),data))return;
            QueueContext();var value=Observations.GetValue(player,_=>new Observation());if(Same(value,player))return;
            value.Data=data;value.Net=ZNet.instance;value.Manager=ZDOMan.instance;value.Generation=MasterIdolLoadIdentity.Generation;
            value.World=ZNet.instance.GetWorldUID();value.Owner=data.GetOwner();value.Revision=data.OwnerRevision;value.Id=data.GetInt(ZDOVars.s_emoteID,0);value.EdgeAt=0;
        }
        internal static void After(Player player,bool started)
        {
            if(!Observations.TryGetValue(player,out var value)||!Same(value,player))return;
            int id=value.Data.GetInt(ZDOVars.s_emoteID,0);if(id==value.Id||!started&&player.m_emoteID!=id)return;
            value.Id=id;value.EdgeAt=Current(player,value.Data,value.Revision,id)?value.Net.GetTime().Ticks:0;
            if(value.EdgeAt==0)return;
            if(ReferenceEquals(player,global::Player.m_localPlayer)&&player.m_nview.IsOwner()&&value.Owner==ZNet.GetUID())Publish(player,value,id);
        }
        private static bool Fresh(Player player,int id,long stamp)
        {
            if(!Observations.TryGetValue(player,out var value)||!Same(value,player)||value.Id!=id||value.EdgeAt<=0)return false;
            long now=ZNet.instance.GetTime().Ticks;return stamp>=value.EdgeAt-3*TimeSpan.TicksPerSecond&&stamp<=value.EdgeAt+3*TimeSpan.TicksPerSecond&&now>=value.EdgeAt&&now-value.EdgeAt<=3*TimeSpan.TicksPerSecond;
        }
        private static bool Alive(Publication value)
        {
            return value!=null&&Time.unscaledTime<value.Until&&Same(value.Context,value.Player)&&ReferenceEquals(value.Player,global::Player.m_localPlayer)&&
                value.Player.m_nview.IsOwner()&&value.Context.Owner==ZNet.GetUID()&&Current(value.Player,value.Context.Data,value.Context.Revision,value.Id)&&
                MasterIdolEffectZones.At("BlackForest",value.Player.transform.position)?.Id==value.Home;
        }
        private static void Publish(Player player,Observation context,int id)
        {
            QueueContext();Pending=null;var home=MasterIdolEffectZones.At("BlackForest",player.transform.position);
            if(home==null||home.Residents.Count>4)return;
            var snapshot=new Observation{Data=context.Data,Net=context.Net,Manager=context.Manager,Generation=context.Generation,World=context.World,Owner=context.Owner,Revision=context.Revision,Id=context.Id,EdgeAt=context.EdgeAt};
            var value=new Publication{Player=player,Context=snapshot,Id=id,Edge=Guid.NewGuid().ToString("N"),Home=home.Id,Stamp=context.EdgeAt,Until=Time.unscaledTime+EventSeconds};
            foreach(var member in home.Residents)
            {
                if(!MasterIdolNetworkQuota.TryUid(member.Key,out long user,out uint number))continue;
                var data=ZDOMan.instance?.GetZDO(new ZDOID(user,number));var actor=ZNetScene.instance?.FindInstance(new ZDOID(user,number))?.GetComponent<Character>();
                if(data==null||actor==null||actor.m_nview?.IsValid()!=true||!ReferenceEquals(actor.m_nview.GetZDO(),data)||!MasterIdolForestGuardState.ModuleBorn(data)||
                    !MasterIdolResidentPolicy.TryHome(actor,out var assigned)||assigned.Id!=home.Id||!MasterIdolResidentPolicy.Friendly(actor,player)||
                    !home.Contains(data.GetPosition())||!float.IsFinite(data.GetFloat(ZDOVars.s_health,0))||data.GetFloat(ZDOVars.s_health,0)<=0)continue;
                float distance=(data.GetPosition()-player.transform.position).sqrMagnitude;if(!float.IsFinite(distance)||distance>64)continue;
                value.Targets.Add(new Target{Actor=actor,Data=data,Owner=data.GetOwner(),Revision=data.OwnerRevision,Distance=distance});
            }
            value.Targets.Sort((a,b)=>{int distance=a.Distance.CompareTo(b.Distance);return distance!=0?distance:string.CompareOrdinal(a.Data.m_uid.ToString(),b.Data.m_uid.ToString());});
            Pending=value;NextTarget(value);
        }
        private static void Write(ZPackage packet,byte kind,Publication value,byte verdict=0)
        {
            packet.Write(kind);MasterIdolResidentWork.WriteUid(packet,value.Context.Data.m_uid);packet.Write((int)value.Context.Revision);packet.Write(value.Id);packet.Write(value.Edge);if(kind==2)packet.Write(verdict);
        }
        private static void NextTarget(Publication value)
        {
            if(!ReferenceEquals(Pending,value)||!Alive(value)){if(ReferenceEquals(Pending,value))Pending=null;return;}
            while(value.Attempt<value.Targets.Count&&value.Attempt<4)
            {
                var target=value.Targets[value.Attempt++];
                if(target.Actor?.m_nview?.IsValid()!=true||!ReferenceEquals(target.Actor.m_nview.GetZDO(),target.Data)||!ReferenceEquals(ZDOMan.instance.GetZDO(target.Data.m_uid),target.Data)||
                    target.Owner==0||target.Data.GetOwner()!=target.Owner||target.Data.OwnerRevision!=target.Revision)continue;
                value.Target=target;
                try{if(MasterIdolVisibleGesture.SendEmote(target.Actor,target.Owner,value.Home,value.Stamp,p=>Write(p,1,value)))return;}
                catch(Exception){MasterIdolResidentMetrics.Count("emote.request-unknown");}
                Pending=null;return; // Delivery uncertainty never authorizes fallback.
            }
            Pending=null;
        }
        internal static void Receive(Character actor,ZNetView view,long sender,long world,ZDOID uid,int revision,string home,long stamp,ZPackage packet)
        {
            byte kind=packet.ReadByte();var playerId=MasterIdolResidentWork.ReadUid(packet);int playerRevision=packet.ReadInt(),id=packet.ReadInt();string edge=packet.ReadString();byte verdict=kind==2?packet.ReadByte():(byte)0;
            if(kind!=1&&kind!=2||verdict>1||packet.GetPos()!=packet.Size()||packet.Size()>384||home==null||home.Length>128||
                !Guid.TryParseExact(edge,"N",out _)||playerRevision<0||playerRevision>ushort.MaxValue||world!=ZNet.instance.GetWorldUID())return;
            var data=view?.GetZDO();long now=ZNet.instance.GetTime().Ticks;
            if(data==null||data.m_uid!=uid||data.OwnerRevision!=revision||!ReferenceEquals(ZDOMan.instance?.GetZDO(uid),data)||!MasterIdolForestGuardState.ModuleBorn(data)||
                stamp<=0||stamp>now||now-stamp>3*TimeSpan.TicksPerSecond||stamp<MasterIdolVisibleGesture.ContextStart(actor)||
                !MasterIdolResidentPolicy.TryHome(actor,out var zone)||zone.Id!=home)return;
            var player=ZNetScene.instance?.FindInstance(playerId)?.GetComponent<Player>();var playerData=player?.m_nview?.GetZDO();
            if(playerData==null||playerData.m_uid!=playerId||!Current(player,playerData,(ushort)playerRevision,id)||!zone.Contains(player.transform.position)||!zone.Contains(actor.transform.position)||
                (player.transform.position-actor.transform.position).sqrMagnitude>64||!MasterIdolResidentPolicy.Friendly(actor,player))return;
            if(kind==2)
            {
                var value=Pending;var target=value?.Target;
                if(!Alive(value)||target==null||!ReferenceEquals(target.Data,data)||target.Owner!=sender||data.GetOwner()!=sender||target.Revision!=data.OwnerRevision||
                    !ReferenceEquals(value.Player,player)||value.Id!=id||value.Edge!=edge||value.Stamp!=stamp||value.Home!=home)return;
                if(verdict==1)Pending=null;else NextTarget(value);return;
            }
            if(!view.IsOwner()||data.GetOwner()!=ZNet.GetUID()||playerData.GetOwner()!=sender||!Fresh(player,id,stamp))return;
            QueueContext();var component=actor.GetComponent<MasterIdolResidentEmote>()??actor.gameObject.AddComponent<MasterIdolResidentEmote>();
            var receipt=component.Receipts.GetValue(player,_=>new Receipt());
            bool same=ReferenceEquals(receipt.PlayerData,playerData)&&receipt.PlayerOwner==sender&&receipt.PlayerRevision==playerRevision&&
                ReferenceEquals(receipt.ActorData,data)&&receipt.ActorOwner==data.GetOwner()&&receipt.ActorRevision==data.OwnerRevision&&
                receipt.Net==ZNet.instance&&receipt.Manager==ZDOMan.instance&&receipt.Generation==MasterIdolLoadIdentity.Generation&&receipt.World==world&&receipt.Id==id;
            if(same){if(receipt.Edge!=edge)return;verdict=receipt.Verdict;}
            else
            {
                verdict=component.TryQueue(player,playerData,id,home,stamp)?(byte)1:(byte)0;
                receipt.PlayerData=playerData;receipt.PlayerOwner=sender;receipt.PlayerRevision=(ushort)playerRevision;receipt.ActorData=data;receipt.ActorOwner=data.GetOwner();receipt.ActorRevision=data.OwnerRevision;
                receipt.Net=ZNet.instance;receipt.Manager=ZDOMan.instance;receipt.Generation=MasterIdolLoadIdentity.Generation;receipt.World=world;receipt.Id=id;receipt.Edge=edge;receipt.Verdict=verdict;
            }
            try{MasterIdolVisibleGesture.SendEmote(actor,sender,home,stamp,p=>{p.Write((byte)2);MasterIdolResidentWork.WriteUid(p,playerId);p.Write(playerRevision);p.Write(id);p.Write(edge);p.Write(verdict);});}
            catch(Exception){MasterIdolResidentMetrics.Count("emote.result-unknown");}
        }
        private bool TryQueue(Player player,ZDO playerData,int id,string home,long stamp)
        {
            if(Queued||QueueCount>=4||Social==null||!Social.SceneAvailable||MasterIdolResidentSceneAnimation.HomeActive(MasterIdolEffectZones.Find(home)))return false;
            var data=View.GetZDO();Player=player;PlayerData=playerData;PlayerOwner=playerData.GetOwner();PlayerRevision=playerData.OwnerRevision;
            ActorData=data;ActorOwner=data.GetOwner();ActorRevision=data.OwnerRevision;Id=id;Home=home;Stamp=stamp;Net=ZNet.instance;Manager=ZDOMan.instance;Generation=MasterIdolLoadIdentity.Generation;World=Net.GetWorldUID();
            Epoch=QueueEpoch;Until=Time.unscaledTime+QueueSeconds;Next=0;Queued=true;QueueCount++;return true;
        }
        private bool Valid(MasterIdolEffectZone home)
        {
            return Net==ZNet.instance&&Net?.GetWorld()!=null&&Net.GetWorldUID()==World&&MasterIdolLoadIdentity.LoadReady&&Manager==ZDOMan.instance&&Generation==MasterIdolLoadIdentity.Generation&&View?.IsValid()==true&&View.IsOwner()&&
                ReferenceEquals(View.GetZDO(),ActorData)&&ReferenceEquals(Manager?.GetZDO(ActorData.m_uid),ActorData)&&ActorData.GetOwner()==ActorOwner&&ActorData.OwnerRevision==ActorRevision&&
                home?.Id==Home&&Current(Player,PlayerData,PlayerRevision,Id)&&PlayerData.GetOwner()==PlayerOwner&&home.Contains(Player.transform.position)&&home.Contains(transform.position)&&
                (Player.transform.position-transform.position).sqrMagnitude<=64&&MasterIdolResidentPolicy.Friendly(Actor,Player);
        }
        internal static bool Idle(MasterIdolSocial social,MasterIdolEffectZone home,float dt)
        {
            var component=social?.GetComponent<MasterIdolResidentEmote>();if(component?.Queued!=true)return false;
            if(!component.Valid(home)||Time.unscaledTime>=component.Until||ZNet.instance.GetTime().Ticks-component.Stamp>3*TimeSpan.TicksPerSecond){component.Release();return false;}
            if(Time.unscaledTime<component.Next)return false;component.Next=Time.unscaledTime+.1f;
            if(!social.SceneAvailable)return false;
            if(!MasterIdolSocialScenes.React(social,component.Player))return false;
            component.Release();MasterIdolResidentMetrics.Count("emote.native-reaction-start");return true;
        }
        private void Release(){if(Queued&&Epoch==QueueEpoch)QueueCount=Math.Max(0,QueueCount-1);Queued=false;Player=null;PlayerData=ActorData=null;}
        private void Update()
        {
            if(!Queued||Time.unscaledTime<NextCleanup)return;NextCleanup=Time.unscaledTime+.1f;
            if(Time.unscaledTime>=Until||!Valid(MasterIdolEffectZones.Find(Home))||Social?.SceneAvailable!=true)Release();
        }
        private void OnDisable()=>Release();private void OnDestroy()=>Release();
    }
    [HarmonyPatch(typeof(Player),nameof(Player.StartEmote))]
    internal static class MasterIdolNativeEmoteStartPatch
    {private static void Prefix(Player __instance)=>MasterIdolResidentEmote.Before(__instance);private static void Postfix(Player __instance,bool __result){if(__result)MasterIdolResidentEmote.After(__instance,true);}}
    [HarmonyPatch(typeof(Player),"UpdateEmote")]
    internal static class MasterIdolNativeEmoteObservePatch
    {private static void Prefix(Player __instance)=>MasterIdolResidentEmote.Before(__instance);private static void Postfix(Player __instance)=>MasterIdolResidentEmote.After(__instance,false);}
}
