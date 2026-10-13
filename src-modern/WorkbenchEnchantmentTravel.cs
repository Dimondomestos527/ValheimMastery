using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class WorkbenchEnchantmentTravel
    {
        const string AskRpc="VM_EnchantTravelAsk_v1",MenuRpc="VM_EnchantTravelMenu_v1",ChooseRpc="VM_EnchantTravelChoose_v1",GrantRpc="VM_EnchantTravelGrant_v1";
        const int Limit=4096,Chunk=128;
        sealed class Menu
        {
            internal ZNet Session;internal ZRpc Rpc;internal ulong Generation;internal long World,Actor;internal ZDOID Character;
            internal string Nonce;internal float Until;internal WorkbenchTravelEndpoint Source;internal List<WorkbenchTravelEndpoint> Targets;
        }
        sealed class ClientState
        {
            internal ZNet Session;internal ZRpc Rpc;internal ulong Generation;internal long World,Actor;internal ZDOID Character,Source;
            internal string Token,Nonce;internal float Until;internal int Page,Total=-1;internal bool Choosing,Ready;
            internal readonly List<WorkbenchTravelDestination> Choices=new List<WorkbenchTravelDestination>();
            internal readonly Dictionary<ZDOID,string> Tokens=new Dictionary<ZDOID,string>();
        }
        static readonly Dictionary<long,Menu> Menus=new Dictionary<long,Menu>();
        static readonly Dictionary<long,float> LastAsk=new Dictionary<long,float>();
        static readonly List<long> Expired=new List<long>(128);
        static readonly Queue<Action> LocalDelivery=new Queue<Action>();
        static ZNet Session;static ZDOMan Manager;static long World;static ulong Generation;static float Next;
        static ClientState Local;
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>(AskRpc,ReceiveAsk);peer?.m_rpc?.Register<ZPackage>(MenuRpc,ReceiveMenu);peer?.m_rpc?.Register<ZPackage>(ChooseRpc,ReceiveChoose);peer?.m_rpc?.Register<ZPackage>(GrantRpc,ReceiveGrant);}
        static void Current()
        {
            var net=ZNet.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(Session!=net||Manager!=ZDOMan.instance||World!=world||Generation!=MasterIdolLoadIdentity.Generation)
            {Reset();Session=net;Manager=ZDOMan.instance;World=world;Generation=MasterIdolLoadIdentity.Generation;}
        }
        internal static void Reset(){Menus.Clear();LastAsk.Clear();Expired.Clear();LocalDelivery.Clear();Session=null;Manager=null;World=0;Generation=0;Next=0;Cancel();}
        internal static void Cancel(){Local=null;WorkbenchEnchantmentTravelMap.Close();}
        internal static bool Open(CraftingStation station,Humanoid user,bool hold)
        {
            var player=user as Player;var data=station?.GetComponent<ZNetView>()?.GetZDO();
            if(hold||!MasterIdolPlains.Owned(player)||player.IsDead()||player.IsTeleporting()||data==null||data.GetPrefab()!="piece_workbench".GetStableHashCode())return false;
            string token=data.GetString(WorkbenchEnchantmentRuntime.StationKey,"");
            if(!Guid.TryParseExact(token,"N",out _)||MasterIdolEffectZones.Find(data.m_uid+"/"+token)==null)return false;
            if(!MasterIdolTravel.PortalRules(player)){Notice("Шлях закритий: залиш заборонену ношу або дочекайся тихої години.");return true;}
            Current();Cancel();
            Local=new ClientState{Session=Session,Rpc=Session.IsServer()?null:Session.GetServerRPC(),Generation=Generation,World=World,Actor=player.GetPlayerID(),Character=player.m_nview.GetZDO().m_uid,Source=data.m_uid,Token=token,Nonce=Guid.NewGuid().ToString("N"),Until=Time.unscaledTime+30};
            AskPage(Local);return true;
        }
        static void AskPage(ClientState c)
        {
            var packet=new ZPackage();packet.Write(c.World);packet.Write(c.Actor);packet.Write(c.Source);packet.Write(c.Token);packet.Write(c.Nonce);packet.Write(c.Page);
            Send(c.Rpc,AskRpc,packet,ReceiveAsk);
        }
        static void Send(ZRpc rpc,string name,ZPackage packet,Action<ZRpc,ZPackage> local)
        {
            if(rpc!=null){rpc.Invoke(name,packet);return;}
            // Host pages use the same packets without a recursive 32-page frame.
            if(LocalDelivery.Count>=256){Cancel();return;}
            LocalDelivery.Enqueue(()=>{packet.SetPos(0);local(null,packet);});
        }
        static bool Server(ZPackage packet)=>ZNet.instance?.IsServer()==true&&packet!=null&&packet.Size()<=512;
        static bool Same(WorkbenchTravelEndpoint a,WorkbenchTravelEndpoint b)=>a!=null&&b!=null&&a.Id==b.Id&&a.Station==b.Station&&a.Choice==b.Choice&&
            (a.Point-b.Point).sqrMagnitude<=.01f&&Quaternion.Angle(a.Rotation,b.Rotation)<=1f;
        static bool MenuCurrent(Menu menu,WorkshopActor actor)
        {
            return menu!=null&&menu.Session==ZNet.instance&&menu.Generation==MasterIdolLoadIdentity.Generation&&menu.World==ZNet.instance?.GetWorldUID()&&Time.unscaledTime<=menu.Until&&
                actor?.Available==true&&actor.GetPlayerID()==menu.Actor&&actor.CharacterId==menu.Character&&actor.Rpc==menu.Rpc&&!actor.IsDead()&&!actor.IsTeleporting()&&
                MasterIdolTravel.GlobalPortalRules()&&WorkbenchEnchantmentRuntime.TryTravelEndpoint(menu.Source.Id,actor,out var from)&&Same(menu.Source,from)&&
                Vector3.Distance(actor.Position,from.Point)<=4&&MasterIdolWorldIndex.WardAllows(menu.Actor,actor.Position);
        }
        static void ReceiveAsk(ZRpc rpc,ZPackage packet)
        {
            if(!Server(packet))return;Current();
            try
            {
                long world=packet.ReadLong(),player=packet.ReadLong();var source=packet.ReadZDOID();string token=packet.ReadString(),nonce=packet.ReadString();int page=packet.ReadInt();
                if(packet.GetPos()!=packet.Size()||world!=World||page<0||page>=Limit/Chunk||!Guid.TryParseExact(token,"N",out _)||!Guid.TryParseExact(nonce,"N",out _))return;
                var actor=WorkshopActor.Resolve(rpc);
                if(actor?.Available!=true||actor.GetPlayerID()!=player||!MasterIdolTravel.GlobalPortalRules()||!WorkbenchEnchantmentRuntime.TryTravelEndpoint(source,actor,out var from)||
                    from.Station!=token||Vector3.Distance(actor.Position,from.Point)>4||!MasterIdolWorldIndex.WardAllows(player,actor.Position))return;
                Menus.TryGetValue(player,out var menu);
                if(menu==null||menu.Nonce!=nonce)
                {
                    if(page!=0)return;
                    if(LastAsk.TryGetValue(player,out float last)&&Time.unscaledTime-last<.5f)return;
                    Prune();if((Menus.Count>=128&&!Menus.ContainsKey(player))||(LastAsk.Count>=128&&!LastAsk.ContainsKey(player)))return;
                    LastAsk[player]=Time.unscaledTime;var candidates=WorkbenchEnchantmentRuntime.TravelCandidates();
                    if(candidates.Length>Limit){SendMenuFailure(rpc,world,player,source,token,nonce,"Давніх шляхів забагато, щоб зараз їх роздивитися.");return;}
                    var targets=new List<WorkbenchTravelEndpoint>();
                    foreach(var id in candidates)if(id!=source&&WorkbenchEnchantmentRuntime.TryTravelEndpoint(id,actor,out var target))targets.Add(target);
                    targets.Sort((a,b)=>{int order=(a.Point-from.Point).sqrMagnitude.CompareTo((b.Point-from.Point).sqrMagnitude);return order!=0?order:StringComparer.Ordinal.Compare(a.Id.ToString(),b.Id.ToString());});
                    menu=new Menu{Session=Session,Rpc=rpc,Generation=Generation,World=world,Actor=player,Character=actor.CharacterId,Nonce=nonce,Until=Time.unscaledTime+30,Source=from,Targets=targets};Menus[player]=menu;
                }
                if(menu.Rpc!=rpc||menu.Source.Id!=source||menu.Source.Station!=token||!MenuCurrent(menu,actor))
                {SendMenuFailure(rpc,world,player,source,token,nonce,"Шлях уже змінився. Звернися до верстака ще раз.");return;}
                int start=page*Chunk;if(start>menu.Targets.Count||start==menu.Targets.Count&&start!=0)return;
                int count=Math.Min(Chunk,menu.Targets.Count-start);var result=new ZPackage();WriteHeader(result,world,player,source,token,nonce);result.Write(true);result.Write(page);result.Write(menu.Targets.Count);result.Write(count);
                for(int i=0;i<count;i++)
                {
                    var target=menu.Targets[start+i];
                    if(!WorkbenchEnchantmentRuntime.TryTravelEndpoint(target.Id,actor,out var now)||!Same(target,now))
                    {Menus.Remove(player);SendMenuFailure(rpc,world,player,source,token,nonce,"Один зі шляхів згас. Роздивися їх знову.");return;}
                    result.Write(target.Id);result.Write(target.Station);result.Write(target.Choice);result.Write(target.Point);
                }
                if(result.Size()>16384){Menus.Remove(player);SendMenuFailure(rpc,world,player,source,token,nonce,"Шляхи ще не готові відкритися.");return;}
                Send(rpc,MenuRpc,result,ReceiveMenu);
            }catch(Exception error){Warn("menu",error);}
        }
        static void WriteHeader(ZPackage p,long world,long actor,ZDOID source,string token,string nonce)
        {p.Write(world);p.Write(actor);p.Write(source);p.Write(token);p.Write(nonce);}
        static void SendMenuFailure(ZRpc rpc,long world,long actor,ZDOID source,string token,string nonce,string text)
        {var p=new ZPackage();WriteHeader(p,world,actor,source,token,nonce);p.Write(false);p.Write(text);Send(rpc,MenuRpc,p,ReceiveMenu);}
        static bool Client(ZRpc rpc,ZPackage p)
        {
            var c=Local;return c!=null&&p!=null&&p.Size()<=16384&&c.Session==ZNet.instance&&c.Generation==MasterIdolLoadIdentity.Generation&&
                c.Rpc==(ZNet.instance?.IsServer()==true?null:ZNet.instance?.GetServerRPC())&&rpc==c.Rpc&&MasterIdolPlains.Owned(Player.m_localPlayer)&&
                c.Character==Player.m_localPlayer.m_nview.GetZDO().m_uid&&c.Actor==Player.m_localPlayer.GetPlayerID()&&c.World==ZNet.instance?.GetWorldUID()&&Time.unscaledTime<=c.Until;
        }
        static bool Header(ZPackage p,ClientState c)=>p.ReadLong()==c.World&&p.ReadLong()==c.Actor&&p.ReadZDOID()==c.Source&&p.ReadString()==c.Token&&p.ReadString()==c.Nonce;
        static bool Finite(Vector3 point)=>MasterIdolSanctuaryRules.Finite(point.x)&&MasterIdolSanctuaryRules.Finite(point.y)&&MasterIdolSanctuaryRules.Finite(point.z);
        static void ReceiveMenu(ZRpc rpc,ZPackage packet)
        {
            if(!Client(rpc,packet))return;var c=Local;
            try
            {
                if(!Header(packet,c)||c.Choosing||c.Ready)return;
                if(!packet.ReadBool()){string text=packet.ReadString();if(text.Length<=256&&packet.GetPos()==packet.Size()){Cancel();Notice(text);}return;}
                int page=packet.ReadInt(),total=packet.ReadInt(),count=packet.ReadInt();
                if(page!=c.Page||total<0||total>Limit||c.Total>=0&&c.Total!=total||count!=Math.Min(Chunk,total-page*Chunk)||count<0)return;
                var received=new List<WorkbenchTravelDestination>();var tokens=new Dictionary<ZDOID,string>();
                for(int i=0;i<count;i++)
                {
                    var id=packet.ReadZDOID();string token=packet.ReadString(),type=packet.ReadString();var point=packet.ReadVector3();
                    if(id==ZDOID.None||id==c.Source||!Guid.TryParseExact(token,"N",out _)||!WorkbenchEnchantmentRules.Known(type)||!Finite(point)||c.Tokens.ContainsKey(id)||tokens.ContainsKey(id))return;
                    tokens.Add(id,token);received.Add(new WorkbenchTravelDestination(id,point,type,page*Chunk+i+1));
                }
                if(packet.GetPos()!=packet.Size()||!Valid())return;
                c.Total=total;c.Choices.AddRange(received);foreach(var item in tokens)c.Tokens.Add(item.Key,item.Value);
                if(total==0){Cancel();Notice("Інший пробуджений верстак ще не відкрив свого шляху.");return;}
                if(c.Choices.Count<total){c.Page++;AskPage(c);return;}
                c.Ready=true;WorkbenchEnchantmentTravelMap.Show(c.Choices.AsReadOnly(),Choose,Cancel);
            }catch{if(ReferenceEquals(Local,c))Cancel();}
        }
        static void Choose(ZDOID id)
        {
            var c=Local;if(c?.Ready!=true||c.Choosing||!Valid()||!c.Tokens.TryGetValue(id,out var token))return;
            if(!MasterIdolTravel.PortalRules(Player.m_localPlayer)){Cancel();Notice("Ця ноша не пройде давнім шляхом.");return;}
            c.Choosing=true;WorkbenchEnchantmentTravelMap.Close();var p=new ZPackage();WriteHeader(p,c.World,c.Actor,c.Source,c.Token,c.Nonce);p.Write(id);p.Write(token);Send(c.Rpc,ChooseRpc,p,ReceiveChoose);
        }
        static void ReceiveChoose(ZRpc rpc,ZPackage packet)
        {
            if(!Server(packet))return;
            try
            {
                long world=packet.ReadLong(),player=packet.ReadLong();var source=packet.ReadZDOID();string token=packet.ReadString(),nonce=packet.ReadString();var target=packet.ReadZDOID();string targetToken=packet.ReadString();
                if(packet.GetPos()!=packet.Size()||!Menus.TryGetValue(player,out var menu)||menu.Rpc!=rpc||menu.World!=world||menu.Source.Id!=source||menu.Source.Station!=token||menu.Nonce!=nonce)return;
                Menus.Remove(player);var actor=WorkshopActor.Resolve(rpc);var selected=menu.Targets.FirstOrDefault(t=>t.Id==target&&t.Station==targetToken);
                if(!MenuCurrent(menu,actor)||selected==null||!WorkbenchEnchantmentRuntime.TryTravelEndpoint(target,actor,out var now)||!Same(selected,now)||
                    !MasterIdolWorldIndex.WardAllows(player,now.Point+now.Rotation*Vector3.forward*2+Vector3.up))
                {GrantDenied(menu,"Цей шлях уже згас або оберіг не дозволяє пройти.");return;}
                var result=new ZPackage();WriteHeader(result,world,player,source,token,nonce);result.Write(true);result.Write(now.Point+now.Rotation*Vector3.forward*2+Vector3.up);result.Write(now.Rotation);Send(rpc,GrantRpc,result,ReceiveGrant);
            }catch(Exception error){Warn("grant",error);}
        }
        static void GrantDenied(Menu menu,string text)
        {var result=new ZPackage();WriteHeader(result,menu.World,menu.Actor,menu.Source.Id,menu.Source.Station,menu.Nonce);result.Write(false);result.Write(text);Send(menu.Rpc,GrantRpc,result,ReceiveGrant);}
        static void ReceiveGrant(ZRpc rpc,ZPackage packet)
        {
            if(!Client(rpc,packet))return;var c=Local;
            try
            {
                if(!Header(packet,c)||!c.Choosing)return;
                if(!packet.ReadBool()){string text=packet.ReadString();if(text.Length<=256&&packet.GetPos()==packet.Size()){Cancel();Notice(text);}return;}
                var point=packet.ReadVector3();var rotation=packet.ReadQuaternion();
                if(packet.GetPos()!=packet.Size()||!Finite(point)||!MasterIdolSanctuaryRules.Finite(rotation.x)||!MasterIdolSanctuaryRules.Finite(rotation.y)||!MasterIdolSanctuaryRules.Finite(rotation.z)||!MasterIdolSanctuaryRules.Finite(rotation.w)||!Valid())return;
                if(!MasterIdolTravel.PortalRules(Player.m_localPlayer)){Cancel();Notice("Ця ноша вже не пройде давнім шляхом.");return;}
                Cancel();if(!Player.m_localPlayer.TeleportTo(point,rotation,true))Notice("Давній шлях ще не готовий прийняти тебе.");
            }catch{if(ReferenceEquals(Local,c))Cancel();}
        }
        internal static bool Valid()
        {
            var c=Local;var p=Player.m_localPlayer;var source=c==null?null:ZDOMan.instance?.GetZDO(c.Source);
            return c!=null&&c.Session==ZNet.instance&&c.Generation==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady&&c.World==ZNet.instance?.GetWorldUID()&&
                c.Rpc==(ZNet.instance?.IsServer()==true?null:ZNet.instance?.GetServerRPC())&&MasterIdolPlains.Owned(p)&&c.Actor==p.GetPlayerID()&&c.Character==p.m_nview.GetZDO().m_uid&&!p.IsDead()&&!p.IsTeleporting()&&
                Time.unscaledTime<=c.Until&&source!=null&&source.GetPrefab()=="piece_workbench".GetStableHashCode()&&source.GetString(WorkbenchEnchantmentRuntime.StationKey,"")==c.Token&&
                Vector3.Distance(p.transform.position,source.GetPosition())<=4&&MasterIdolEffectZones.Find(c.Source+"/"+c.Token)!=null;
        }
        static void Prune()
        {
            if(Menus.Count!=0)
            {
                foreach(var item in Menus)if(item.Value.Session!=ZNet.instance||item.Value.Generation!=MasterIdolLoadIdentity.Generation||Time.unscaledTime>item.Value.Until)Expired.Add(item.Key);
                foreach(long id in Expired){Menus.Remove(id);LastAsk.Remove(id);}Expired.Clear();
            }
            if(LastAsk.Count!=0)
            {
                foreach(var item in LastAsk)if(Time.unscaledTime-item.Value>30)Expired.Add(item.Key);
                foreach(long id in Expired)LastAsk.Remove(id);Expired.Clear();
            }
        }
        internal static void Tick()
        {
            Current();int budget=8;while(budget-->0&&LocalDelivery.Count!=0)LocalDelivery.Dequeue()();
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.25f;Prune();if(Local!=null&&!Valid())Cancel();
        }
        static void Notice(string text)=>Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"<color=#FFD36A>"+text+"</color>");
        static void Warn(string stage,Exception error){try{MasteryPlugin.Log?.LogWarning("[WorkbenchTravel] "+stage+": "+error.Message);}catch{}}
    }
    [HarmonyPatch(typeof(CraftingStation),nameof(CraftingStation.Interact))]
    internal static class WorkbenchTravelInteractionPatch
    {static bool Prefix(CraftingStation __instance,Humanoid __0,bool __1,bool __2,ref bool __result){if(!__2||!WorkbenchEnchantmentTravel.Open(__instance,__0,__1))return true;__result=true;return false;}}
    [HarmonyPatch(typeof(ZNet),nameof(ZNet.OnNewConnection))]
    internal static class WorkbenchTravelRegisterPatch {static void Postfix(ZNetPeer peer)=>WorkbenchEnchantmentTravel.Register(peer);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class WorkbenchTravelTickPatch {static void Postfix()=>WorkbenchEnchantmentTravel.Tick();}
    [HarmonyPatch(typeof(ZNet),"OnDestroy")]
    internal static class WorkbenchTravelCleanupPatch {static void Prefix()=>WorkbenchEnchantmentTravel.Reset();}
}
