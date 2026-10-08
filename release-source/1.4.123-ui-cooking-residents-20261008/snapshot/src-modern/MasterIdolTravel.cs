using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolTravelDestination
    {internal ZDOID Id;internal string Token,Type;internal Vector3 Point;internal Quaternion Rotation;}
    internal static class MasterIdolTravel
    {
        private sealed class Menu
        {internal ZDOID Character;internal ZNet Session;internal ulong Generation;internal long World,Player;internal ZRpc Rpc;internal ZDOID Source;internal string SourceToken,Nonce;internal float Until;internal List<MasterIdolTravelDestination> Destinations;}
        private static readonly Dictionary<long,Menu> Menus=new Dictionary<long,Menu>();
        private static readonly Dictionary<long,float> LastAsk=new Dictionary<long,float>();private static ZNet Session;
        private static float NextTick;private static readonly List<long> Expired=new List<long>();
        private static ulong LocalGeneration;private static ZRpc LocalRpc;private static ZDOID Character;
        private static long World,PlayerId;private static string Nonce,SourceToken;private static ZDOID Source;private static float Asked;private static bool Choosing;
        internal static bool GlobalPortalRules()
        {
            var zones=ZoneSystem.instance;if(zones==null||zones.GetGlobalKey(GlobalKeys.NoPortals))return false;
            if(zones.GetGlobalKey(GlobalKeys.NoBossPortals)&&(!string.IsNullOrEmpty(RandEventSystem.instance?.GetBossEvent())||zones.GetGlobalKey(GlobalKeys.activeBosses,out float bosses)&&bosses>0))return false;
            return true;
        }
        internal static bool PortalRules(Player player)=>GlobalPortalRules()&&player.IsTeleportable(false);
        internal static void Cancel(){Nonce=null;Choosing=false;MasterIdolTravelUi.Close();}
        internal static void Reset(){Menus.Clear();LastAsk.Clear();Session=null;Nonce=null;Choosing=false;MasterIdolTravelUi.Close();}
        private static void Current(){if(!ReferenceEquals(Session,ZNet.instance)){Reset();Session=ZNet.instance;}}
        internal static bool Open(Humanoid user,ZDO source,bool hold)
        {
            var player=user as Player;if(hold||!MasterIdolPlains.Owned(player)||source==null||player.IsDead()||player.IsTeleporting())return false;
            if(!PortalRules(player)){Notice("Шлях закритий: залиш заборонену ношу або дочекайся тихої години.");return true;}
            Current();LocalGeneration=MasterIdolLoadIdentity.Generation;LocalRpc=ZNet.instance.IsServer()?null:ZNet.instance.GetServerRPC();Character=player.m_nview.GetZDO().m_uid;Source=source.m_uid;SourceToken=source.GetString(MasterIdolWorldRegistry.TokenKey,"");World=ZNet.instance.GetWorldUID();PlayerId=player.GetPlayerID();Ask(0);return true;
        }
        private static void Ask(int page)
        {
            Nonce=Guid.NewGuid().ToString("N");Asked=Time.unscaledTime;Choosing=false;
            var packet=new ZPackage();packet.Write(World);packet.Write(PlayerId);packet.Write(Source);packet.Write(SourceToken);packet.Write(Nonce);packet.Write(page);
            if(ZNet.instance.IsServer()){packet.SetPos(0);ReceiveAsk(null,packet);}else ZNet.instance.GetServerRPC()?.Invoke("VM_Idol_TravelAsk_v1",packet);
        }
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>("VM_Idol_TravelAsk_v1",ReceiveAsk);peer?.m_rpc?.Register<ZPackage>("VM_Idol_TravelMenu_v1",ReceiveMenu);peer?.m_rpc?.Register<ZPackage>("VM_Idol_TravelChoose_v1",ReceiveChoose);peer?.m_rpc?.Register<ZPackage>("VM_Idol_TravelGrant_v1",ReceiveGrant);}
        private static bool Endpoint(ZDO zdo,long player)=>MasterIdolWorldRegistry.TravelEndpoint(zdo)&&MasterIdolWorldIndex.WardAllows(player,zdo.GetPosition());
        private static void Send(ZRpc rpc,string name,ZPackage packet,Action<ZRpc,ZPackage> host){if(rpc==null){packet.SetPos(0);host(null,packet);}else rpc.Invoke(name,packet);}
        private static bool Server(ZPackage packet)=>ZNet.instance?.IsServer()==true&&packet!=null&&packet.Size()<=512;
        private static void ReceiveAsk(ZRpc rpc,ZPackage packet)
        {
            if(!Server(packet))return;Current();try
            {
                long world=packet.ReadLong(),player=packet.ReadLong();var source=packet.ReadZDOID();string token=packet.ReadString(),nonce=packet.ReadString();int page=packet.ReadInt();
                var actor=WorkshopActor.Resolve(rpc);var zdo=ZDOMan.instance?.GetZDO(source);
                if(!GlobalPortalRules()||world!=ZNet.instance.GetWorldUID()||packet.GetPos()!=packet.Size()||page<0||page>128||!Guid.TryParseExact(nonce,"N",out _)||actor?.Available!=true||actor.GetPlayerID()!=player||actor.IsDead()||actor.IsTeleporting()||zdo==null||zdo.GetString(MasterIdolWorldRegistry.TokenKey,"")!=token||Vector3.Distance(actor.Position,zdo.GetPosition())>4||!Endpoint(zdo,player))return;
                if(LastAsk.TryGetValue(player,out float last)&&Time.unscaledTime-last<.5f)return;
                if((Menus.Count>=128&&!Menus.ContainsKey(player))||(LastAsk.Count>=128&&!LastAsk.ContainsKey(player))){Prune();if(Menus.Count>=128||LastAsk.Count>=128)return;}LastAsk[player]=Time.unscaledTime;
                var destinations=new List<MasterIdolTravelDestination>();foreach(var target in MasterIdolWorldIndex.Idols.Values)
                {if(target.m_uid==source||!Endpoint(target,player))continue;destinations.Add(new MasterIdolTravelDestination{Id=target.m_uid,Token=target.GetString(MasterIdolWorldRegistry.TokenKey,""),Type=MasterIdolWorldRegistry.Profile(target.GetPrefab()).Id,Point=target.GetPosition(),Rotation=target.GetRotation()});if(destinations.Count>4096)return;}
                destinations.Sort((a,b)=>{int comparison=(a.Point-zdo.GetPosition()).sqrMagnitude.CompareTo((b.Point-zdo.GetPosition()).sqrMagnitude);return comparison!=0?comparison:StringComparer.Ordinal.Compare(a.Id.ToString(),b.Id.ToString());});
                int start=page*32;if(start>=destinations.Count)start=0;page=start/32;int count=Math.Min(32,destinations.Count-start);var selected=destinations.GetRange(start,count);
                Menus[player]=new Menu{Character=actor.CharacterId,Session=ZNet.instance,Generation=MasterIdolLoadIdentity.Generation,World=world,Player=player,Rpc=rpc,Source=source,SourceToken=token,Nonce=nonce,Until=Time.unscaledTime+30,Destinations=selected};
                var result=new ZPackage();result.Write(world);result.Write(player);result.Write(source);result.Write(token);result.Write(nonce);result.Write(page);result.Write(destinations.Count);result.Write(count);
                foreach(var destination in selected){result.Write(destination.Id);result.Write(destination.Token);result.Write(destination.Type);result.Write(destination.Point);}
                Send(rpc,"VM_Idol_TravelMenu_v1",result,ReceiveMenu);
            }catch(Exception error){MasteryPlugin.Log.LogWarning("[MasterIdols] Travel menu: "+error.Message);}
        }
        private static bool Client(ZRpc rpc,ZPackage packet)
        {return ZNet.instance!=null&&LocalGeneration==MasterIdolLoadIdentity.Generation&&LocalRpc==(ZNet.instance.IsServer()?null:ZNet.instance.GetServerRPC())&&MasterIdolPlains.Owned(Player.m_localPlayer)&&Character==Player.m_localPlayer.m_nview.GetZDO().m_uid&&packet!=null&&packet.Size()<=16384&&(ZNet.instance.IsServer()?rpc==null:rpc==ZNet.instance.GetServerRPC());}
        private static bool Header(ZPackage packet)
        {return packet.ReadLong()==World&&World==ZNet.instance.GetWorldUID()&&packet.ReadLong()==PlayerId&&PlayerId==Player.m_localPlayer.GetPlayerID()&&packet.ReadZDOID()==Source&&packet.ReadString()==SourceToken&&packet.ReadString()==Nonce&&Time.unscaledTime-Asked<30;}
        private static void ReceiveMenu(ZRpc rpc,ZPackage packet)
        {
            if(!Client(rpc,packet))return;try
            {
                if(!Header(packet)||Choosing)return;int page=packet.ReadInt(),total=packet.ReadInt(),count=packet.ReadInt();if(page<0||page>128||total<0||total>4096||count<0||count>32)return;
                var choices=new List<MasterIdolTravelDestination>();for(int i=0;i<count;i++){var id=packet.ReadZDOID();string token=packet.ReadString(),type=packet.ReadString();var point=packet.ReadVector3();if(!Guid.TryParseExact(token,"N",out _)||MasterIdolProfiles.Find(type)==null||!MasterIdolSanctuaryRules.Finite(point.x)||!MasterIdolSanctuaryRules.Finite(point.y)||!MasterIdolSanctuaryRules.Finite(point.z))return;choices.Add(new MasterIdolTravelDestination{Id=id,Token=token,Type=type,Point=point});}
                if(packet.GetPos()!=packet.Size())return;if(count==0){Notice("Поруч із цим шляхом ще немає іншого пробудженого ідола.");Nonce=null;return;}
                MasterIdolTravelUi.Show(choices,page,total,Choose,()=>Ask((page+1)*32<total?page+1:0));
            }catch{ }
        }
        private static void Choose(MasterIdolTravelDestination target)
        {
            if(!Valid()||Choosing)return;if(!PortalRules(Player.m_localPlayer)){Notice("Ця ноша не пройде давнім шляхом.");Cancel();return;}Choosing=true;
            var packet=new ZPackage();packet.Write(World);packet.Write(PlayerId);packet.Write(Source);packet.Write(SourceToken);packet.Write(Nonce);packet.Write(target.Id);packet.Write(target.Token);
            if(ZNet.instance.IsServer()){packet.SetPos(0);ReceiveChoose(null,packet);}else ZNet.instance.GetServerRPC()?.Invoke("VM_Idol_TravelChoose_v1",packet);
        }
        private static void Deny(ZRpc rpc,Menu menu,string text)
        {var packet=new ZPackage();packet.Write(menu.World);packet.Write(menu.Player);packet.Write(menu.Source);packet.Write(menu.SourceToken);packet.Write(menu.Nonce);packet.Write(false);packet.Write(text);Send(rpc,"VM_Idol_TravelGrant_v1",packet,ReceiveGrant);}
        private static void ReceiveChoose(ZRpc rpc,ZPackage packet)
        {
            if(!Server(packet))return;try
            {
                long world=packet.ReadLong(),player=packet.ReadLong();var source=packet.ReadZDOID();string token=packet.ReadString(),nonce=packet.ReadString();var target=packet.ReadZDOID();string targetToken=packet.ReadString();
                if(packet.GetPos()!=packet.Size()||!Menus.TryGetValue(player,out var menu)||menu.Session!=ZNet.instance||menu.Generation!=MasterIdolLoadIdentity.Generation||menu.World!=world||world!=ZNet.instance.GetWorldUID()||menu.Rpc!=rpc||menu.Source!=source||menu.SourceToken!=token||menu.Nonce!=nonce||Time.unscaledTime>menu.Until)return;
                Menus.Remove(player);var actor=WorkshopActor.Resolve(rpc);var from=ZDOMan.instance?.GetZDO(source);var to=ZDOMan.instance?.GetZDO(target);
                bool listed=false;foreach(var choice in menu.Destinations)if(choice.Id==target&&choice.Token==targetToken&&to!=null&&(choice.Point-to.GetPosition()).sqrMagnitude<=.01f){listed=true;break;}
                if(!GlobalPortalRules()||!listed||actor?.Available!=true||actor.GetPlayerID()!=player||actor.CharacterId!=menu.Character||actor.IsDead()||actor.IsTeleporting()||from==null||to==null||from.GetString(MasterIdolWorldRegistry.TokenKey,"")!=token||to.GetString(MasterIdolWorldRegistry.TokenKey,"")!=targetToken||Vector3.Distance(actor.Position,from.GetPosition())>4||!Endpoint(from,player)||!Endpoint(to,player)||!MasterIdolWorldIndex.WardAllows(player,actor.Position)||!MasterIdolWorldIndex.WardAllows(player,to.GetPosition()+to.GetRotation()*Vector3.forward*2+Vector3.up)){Deny(rpc,menu,"Цей шлях уже згас або оберіг не дозволяє пройти.");return;}
                var result=new ZPackage();result.Write(world);result.Write(player);result.Write(source);result.Write(token);result.Write(nonce);result.Write(true);result.Write(to.GetPosition()+to.GetRotation()*Vector3.forward*2+Vector3.up);result.Write(to.GetRotation());
                Send(rpc,"VM_Idol_TravelGrant_v1",result,ReceiveGrant);
            }catch(Exception error){MasteryPlugin.Log.LogWarning("[MasterIdols] Travel grant: "+error.Message);}
        }
        private static void ReceiveGrant(ZRpc rpc,ZPackage packet)
        {
            if(!Client(rpc,packet))return;try
            {
                if(!Header(packet)||!Choosing)return;bool accepted=packet.ReadBool();if(!accepted){string text=packet.ReadString();if(text.Length<=256&&packet.GetPos()==packet.Size()){Cancel();Notice(text);}return;}if(!Valid())return;var point=packet.ReadVector3();var rotation=packet.ReadQuaternion();if(packet.GetPos()!=packet.Size()||!MasterIdolSanctuaryRules.Finite(point.x)||!MasterIdolSanctuaryRules.Finite(point.y)||!MasterIdolSanctuaryRules.Finite(point.z))return;if(!PortalRules(Player.m_localPlayer)){Cancel();Notice("Ця ноша вже не пройде давнім шляхом.");return;}
                Nonce=null;Choosing=false;MasterIdolTravelUi.Close();if(!Player.m_localPlayer.TeleportTo(point,rotation,true))Notice("Давній шлях ще не готовий прийняти тебе.");
            }catch{ }
        }
        internal static bool Valid()
        {var player=Player.m_localPlayer;var source=ZDOMan.instance?.GetZDO(Source);return Nonce!=null&&LocalGeneration==MasterIdolLoadIdentity.Generation&&LocalRpc==(ZNet.instance?.IsServer()==true?null:ZNet.instance?.GetServerRPC())&&Session==ZNet.instance&&World==ZNet.instance?.GetWorldUID()&&MasterIdolPlains.Owned(player)&&Character==player.m_nview.GetZDO().m_uid&&!player.IsDead()&&!player.IsTeleporting()&&Time.unscaledTime-Asked<30&&source!=null&&source.GetString(MasterIdolWorldRegistry.TokenKey,"")==SourceToken&&Vector3.Distance(player.transform.position,source.GetPosition())<=4;}
        private static void Prune(){Expired.Clear();foreach(var pair in Menus)if(Time.unscaledTime>pair.Value.Until||pair.Value.Session!=ZNet.instance)Expired.Add(pair.Key);foreach(long id in Expired){Menus.Remove(id);LastAsk.Remove(id);}Expired.Clear();foreach(var pair in LastAsk)if(Time.unscaledTime-pair.Value>30)Expired.Add(pair.Key);foreach(long id in Expired)LastAsk.Remove(id);}
        internal static void Tick(){Current();if(Time.unscaledTime<NextTick)return;NextTick=Time.unscaledTime+.25f;Prune();if(Nonce!=null&&!Valid()){Nonce=null;Choosing=false;MasterIdolTravelUi.Close();}}
        private static void Notice(string text)=>Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"<color=#FFD36A>"+text+"</color>");
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class MasterIdolTravelNetworkPatch
    {private static void Postfix(ZNetPeer peer)=>MasterIdolTravel.Register(peer);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolTravelTickPatch
    {private static void Postfix()=>MasterIdolTravel.Tick();}
    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class MasterIdolTravelCleanupPatch
    {private static void Prefix()=>MasterIdolCleanup.Run("idol-travel",MasterIdolTravel.Reset,(stage,error)=>MasteryPlugin.Log?.LogWarning("[MasterIdols] "+stage+": "+error));}
}




