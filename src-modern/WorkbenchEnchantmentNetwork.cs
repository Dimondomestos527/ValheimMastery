using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ValheimMastery
{
    internal static class WorkbenchEnchantmentNetwork
    {
        const int MaxPacket=8192,MaxPending=256;
        const string RequestRpc="VM_Enchant_FreeRequest_v2",ResultRpc="VM_Enchant_FreeResult_v2";
        sealed class ClientRequest
        {internal string Op,Choice;internal int Kind;internal long World,Actor;internal ZDOID Native;internal Player Player;internal ZNet Session;internal ZRpc Rpc;internal Action<bool,string> Done;internal float Retry;}
        sealed class ServerRequest
        {internal string Op,ReceiptOp,Choice;internal int Kind;internal long World,Actor;internal ZDOID Native,Character;internal ZRpc Rpc;internal WorkbenchEnchantmentContext Context;}
        static readonly Dictionary<string,ClientRequest> Clients=new Dictionary<string,ClientRequest>(StringComparer.Ordinal);
        static readonly Dictionary<string,ServerRequest> Servers=new Dictionary<string,ServerRequest>(StringComparer.Ordinal);
        static readonly Dictionary<ZDOID,WorkbenchEnchantmentSnapshot> Views=new Dictionary<ZDOID,WorkbenchEnchantmentSnapshot>();
        static ZNet Session;static ZDOMan Manager;static long World;
        static ulong Generation;static float CleanupAt;
        static float RecoveryAt;static int RecoveryCursor;
        static readonly Queue<Action> LocalCalls=new Queue<Action>();static bool ticking;
        static readonly Dictionary<ZDOID,float> ViewTimes=new Dictionary<ZDOID,float>();
        internal static void Shutdown(){Clients.Clear();Servers.Clear();Views.Clear();ViewTimes.Clear();LocalCalls.Clear();Session=null;Manager=null;World=0;}
        internal static void RegisterPeer(ZNetPeer peer)
        {var rpc=peer?.m_rpc;rpc?.Register<ZPackage>(RequestRpc,Receive);rpc?.Register<ZPackage>(ResultRpc,Result);}
        internal static void Tick()
        {
            var net=ZNet.instance;if(net==null||net.GetWorld()==null)return;
            if(Session!=net||Manager!=ZDOMan.instance||World!=net.GetWorldUID()||Generation!=MasterIdolLoadIdentity.Generation){Shutdown();Session=net;Manager=ZDOMan.instance;World=net.GetWorldUID();Generation=MasterIdolLoadIdentity.Generation;}
            if(ticking)return;ticking=true;
            try
            {
            int calls=Math.Min(LocalCalls.Count,32);for(int i=0;i<calls;i++)LocalCalls.Dequeue()();
            if(net.IsServer()&&Time.unscaledTime>=CleanupAt)
            {CleanupAt=Time.unscaledTime+.5f;foreach(var r in Servers.Values.ToArray()){var actor=WorkshopActor.Resolve(r.Rpc);if(actor?.Available!=true||actor.CharacterId!=r.Character||r.World!=World||r.Rpc!=null&&net.GetPeer(r.Rpc)==null){Servers.Remove(r.Op);}}}
            if(net.IsServer()&&Time.unscaledTime>=RecoveryAt)
            {
                RecoveryAt=Time.unscaledTime+2f;var peers=net.GetPeers();int total=peers.Count+1;
                int bounded=Math.Min(8,total);
                for(int i=0;i<bounded;i++){int index=(RecoveryCursor+i)%total;WorkbenchEnchantmentRuntime.ReconcileRemoved(index==0?null:peers[index-1].m_rpc);}
                RecoveryCursor=(RecoveryCursor+bounded)%total;
            }
            foreach(var c in Clients.Values.ToArray())
            {
                if(c.Session!=net||Player.m_localPlayer!=c.Player||c.Player.GetPlayerID()!=c.Actor||(net.IsServer()?null:net.GetServerRPC())!=c.Rpc){Clients.Remove(c.Op);Views.Remove(c.Native);c.Done?.Invoke(false,"Дія ще не підтверджена. Повернися до верстака після відновлення зв’язку.");continue;}
                if(Time.unscaledTime>=c.Retry){c.Retry=Time.unscaledTime+2f;Send(c);}
            }
            }
            finally{ticking=false;}
        }
        static bool Local(Player p,CraftingStation station)=>p!=null&&p==Player.m_localPlayer&&p.GetCurrentCraftingStation()==station&&!p.IsDead()&&!p.IsTeleporting()&&station?.GetComponent<ZNetView>()?.IsValid()==true;
        internal static WorkbenchEnchantmentSnapshot Snapshot(Player p,CraftingStation station)
        {
            Tick();if(!Local(p,station))return null;var uid=station.GetComponent<ZNetView>().GetZDO().m_uid;
            if(Views.TryGetValue(uid,out var view)&&view.World==World)
            {bool refresh=view.NativeLevel!=station.GetLevel(false)||!ViewTimes.TryGetValue(uid,out var at)||Time.unscaledTime-at>=2f;if(refresh&&!Clients.Values.Any(c=>c.Native==uid))Queue(p,station,0,"",null);return refresh?null:view;}
            if(!Clients.Values.Any(c=>c.Native==uid&&c.Kind==0))Queue(p,station,0,"",null);return null;
        }
        internal static bool Request(Player p,CraftingStation station,WorkbenchEnchantmentSnapshot snapshot,WorkbenchOperationKind kind,string choice,Action<bool,string> completion)
        {
            if(snapshot==null||snapshot.World!=ZNet.instance?.GetWorldUID()||kind!=WorkbenchOperationKind.Learn&&kind!=WorkbenchOperationKind.Switch&&kind!=WorkbenchOperationKind.Disable)return false;
            var data=station?.GetComponent<ZNetView>()?.GetZDO();if(data==null||!Views.TryGetValue(data.m_uid,out var current)||snapshot.Station!=current.Station||snapshot.Revision!=current.Revision||snapshot.NativeLevel!=station.GetLevel(false))return false;
            return Queue(p,station,kind==WorkbenchOperationKind.Learn?1:2,kind==WorkbenchOperationKind.Disable?"":choice,completion);
        }
        static bool Queue(Player p,CraftingStation station,int kind,string choice,Action<bool,string> done)
        {
            Tick();if(!Local(p,station)||Clients.Count>=MaxPending||choice==null||choice.Length>32)return false;var uid=station.GetComponent<ZNetView>().GetZDO().m_uid;
            if(Clients.Values.Any(c=>c.Native==uid))return false;
            var c=new ClientRequest{Op=Guid.NewGuid().ToString("N"),Kind=kind,Choice=choice,World=World,Actor=p.GetPlayerID(),Native=uid,Player=p,Session=Session,Rpc=Session.IsServer()?null:Session.GetServerRPC(),Done=done,Retry=Time.unscaledTime+2f};Clients.Add(c.Op,c);Send(c);return true;
        }
        static void Send(ClientRequest c)
        {var packet=new ZPackage();packet.Write(c.World);packet.Write(c.Actor);packet.Write(c.Native);packet.Write(c.Op);packet.Write(c.Kind);packet.Write(c.Choice);if(c.Session.IsServer()){LocalCalls.Enqueue(()=>{packet.SetPos(0);Receive(null,packet);});}else c.Rpc?.Invoke(RequestRpc,packet);}
        static void Receive(ZRpc rpc,ZPackage packet)
        {
            if(ZNet.instance?.IsServer()!=true||packet==null||packet.Size()>MaxPacket)return;
            try
            {
                long world=packet.ReadLong(),actor=packet.ReadLong();var native=packet.ReadZDOID();string op=packet.ReadString();int kind=packet.ReadInt();string choice=packet.ReadString();
                if(world!=ZNet.instance.GetWorldUID()||!Guid.TryParseExact(op,"N",out _)||kind<0||kind>2||choice.Length>32)return;
                var actual=WorkshopActor.Resolve(rpc);if(actual?.Available!=true||actual.GetPlayerID()!=actor)return;
                if(Servers.TryGetValue(op,out var prior)){if(prior.Rpc!=rpc||prior.Native!=native||prior.Actor!=actor||prior.Character!=actual.CharacterId||prior.Kind!=kind||prior.Choice!=choice)return;Execute(prior);return;}
                if(Servers.Count>=MaxPending){Deny(rpc,world,actor,native,op,"unconfirmed");return;}var context=WorkbenchEnchantmentRuntime.Resolve(rpc,native,true,true);if(context==null){Deny(rpc,world,actor,native,op,"access");return;}
                string receiptOp=op;var pending=WorkbenchEnchantmentRuntime.Journal.ReadPending(context.Station);
                if(pending?.Actor==actor&&kind==1&&pending.Kind=="FreeLearn"&&pending.Choice==choice)receiptOp=pending.Operation;
                else if(kind==1)
                {
                    // Reserve may have persisted before the station Pending row.
                    var clock=WorkbenchEnchantmentRuntime.CooldownContext(context,rpc);
                    var permit=WorkbenchEnchantmentCooldownProtocol.Authority?.Query(clock,choice)?.Permit;
                    if(permit!=null&&permit.Character==actor&&permit.World==world&&permit.Station==context.Station&&permit.Choice==choice&&
                        (permit.Phase==WorkbenchCooldownPermitPhase.Reserved||permit.Phase==WorkbenchCooldownPermitPhase.CommitIntent))receiptOp=permit.Operation;
                }
                var request=new ServerRequest{Op=op,ReceiptOp=receiptOp,Choice=choice,Kind=kind,World=world,Actor=actor,Native=native,Character=actual.CharacterId,Rpc=rpc,Context=context};Servers.Add(op,request);Execute(request);
            }
            catch(Exception e){MasteryPlugin.Log.LogWarning("[WorkbenchEnchantments] Request unresolved: "+e.Message);}
        }
        static void Execute(ServerRequest r)
        {
            var context=WorkbenchEnchantmentRuntime.Resolve(r.Rpc,r.Native,false,true);var actual=WorkshopActor.Resolve(r.Rpc);
            if(context==null||actual?.CharacterId!=r.Character||context.Actor!=r.Actor||context.World!=r.World||context.Station!=r.Context.Station){Deny(r.Rpc,r.World,r.Actor,r.Native,r.Op,"unconfirmed");Servers.Remove(r.Op);return;}
            r.Context=context;bool success=false;var clock=WorkbenchEnchantmentRuntime.CooldownContext(context,r.Rpc);
            try
            {if(r.Kind==0)success=true;else if(r.Kind==1)success=WorkbenchEnchantmentRuntime.Service.Learn(context,r.ReceiptOp,r.Choice,clock);else if(r.Kind==2)success=WorkbenchEnchantmentRuntime.Service.Switch(context,r.Choice,WorkbenchEnchantmentRuntime.NetworkAllows);}
            finally{}
            var pendingRow=WorkbenchEnchantmentRuntime.Journal.ReadPending(context.Station);bool pending=r.Kind!=0&&pendingRow?.Actor==r.Actor&&pendingRow.Operation==r.ReceiptOp;
            Reply(r,success,pending,success?"":pending?"pending":r.Kind==2?"network":"capacity");
        }
        static void Reply(ServerRequest r,bool success,bool pending,string reason)
        {
            var view=WorkbenchEnchantmentRuntime.Service.Snapshot(r.Context,WorkbenchEnchantmentRuntime.CooldownContext(r.Context,r.Rpc));
            if(view==null){var b=WorkbenchEnchantmentRuntime.Journal.ReadStation(r.Context.Station);if(b?.Removed!=true)return;view=new WorkbenchEnchantmentSnapshot{World=r.World,Station=b.Id,Revision=WorkbenchEnchantmentRuntime.Journal.Revision,NativeLevel=r.Context.NativeLevel,Active="",Reason="removed",Learned=b.Grants.Select(g=>g.Choice).ToArray(),Available=Array.Empty<string>()};WorkbenchEnchantmentRules.TryAvailable(view.Learned,view.NativeLevel,out view.Available);}
            var p=new ZPackage();p.Write(r.World);p.Write(r.Actor);p.Write(r.Native);p.Write(r.Op);p.Write(success);p.Write(pending);p.Write(reason);p.Write(true);p.Write(view.Station);p.Write(view.Revision);p.Write(view.NativeLevel);p.Write(view.Active);p.Write(view.Reason);p.Write(view.PendingOperation);p.Write(view.PendingChoice);p.Write(view.PendingKind);p.Write(view.PendingActor);p.Write(view.Learned.Length);foreach(var id in view.Learned)p.Write(id);p.Write(view.Available.Length);foreach(var id in view.Available)p.Write(id);
            p.Write(view.Cooldowns.Length);foreach(var status in view.Cooldowns){p.Write(status.Choice);p.Write(status.Ready);p.Write(status.Pending);p.Write(status.RemainingSeconds);}
            if(r.Rpc==null){LocalCalls.Enqueue(()=>{p.SetPos(0);Result(null,p);});}else r.Rpc.Invoke(ResultRpc,p);
            if(!pending){Servers.Remove(r.Op);}
        }
        static void Deny(ZRpc rpc,long world,long actor,ZDOID native,string op,string reason)
        {
            var p=new ZPackage();p.Write(world);p.Write(actor);p.Write(native);p.Write(op);p.Write(false);p.Write(false);p.Write(reason);p.Write(false);
            if(rpc==null)LocalCalls.Enqueue(()=>{p.SetPos(0);Result(null,p);});else rpc.Invoke(ResultRpc,p);
        }
        static void Result(ZRpc rpc,ZPackage packet)
        {
            var net=ZNet.instance;if(net==null||packet==null||packet.Size()>MaxPacket||(net.IsServer()?rpc!=null:rpc!=net.GetServerRPC()))return;
            try
            {
                long world=packet.ReadLong(),actor=packet.ReadLong();var native=packet.ReadZDOID();string op=packet.ReadString();bool ok=packet.ReadBool(),pending=packet.ReadBool();string reason=packet.ReadString();
                if(world!=World||!Clients.TryGetValue(op,out var c)||c.Actor!=actor||c.Native!=native||c.Player!=Player.m_localPlayer||reason.Length>256)return;
                if(!packet.ReadBool()){Clients.Remove(op);Views.Remove(native);c.Done?.Invoke(false,reason);return;}
                var view=new WorkbenchEnchantmentSnapshot{World=world,Station=packet.ReadString(),Revision=packet.ReadLong(),NativeLevel=packet.ReadInt(),Active=packet.ReadString(),Reason=packet.ReadString(),PendingOperation=packet.ReadString(),PendingChoice=packet.ReadString(),PendingKind=packet.ReadString(),PendingActor=packet.ReadLong()};
                int n=packet.ReadInt();if(n<0||n>6)return;view.Learned=new string[n];for(int i=0;i<n;i++)view.Learned[i]=packet.ReadString();n=packet.ReadInt();if(n<0||n>6)return;view.Available=new string[n];for(int i=0;i<n;i++)view.Available[i]=packet.ReadString();
                n=packet.ReadInt();if(n!=6)return;view.Cooldowns=new WorkbenchEnchantmentChoiceCooldown[n];var ids=new HashSet<string>(StringComparer.Ordinal);
                for(int i=0;i<n;i++)
                {
                    var status=new WorkbenchEnchantmentChoiceCooldown{Choice=packet.ReadString(),Ready=packet.ReadBool(),Pending=packet.ReadBool(),RemainingSeconds=packet.ReadDouble()};
                    if(!WorkbenchEnchantmentRules.Known(status.Choice)||!ids.Add(status.Choice)||!double.IsFinite(status.RemainingSeconds)||status.RemainingSeconds<0||status.RemainingSeconds>1800||status.Ready&&(status.Pending||status.RemainingSeconds!=0))return;view.Cooldowns[i]=status;
                }
                if(!Guid.TryParseExact(view.Station,"N",out _)||!WorkbenchEnchantmentRules.TryAvailable(view.Learned,view.NativeLevel,out var available)||!available.SequenceEqual(view.Available)||view.Active.Length!=0&&!available.Contains(view.Active))return;
                Views[native]=view;ViewTimes[native]=Time.unscaledTime;if(!pending){Clients.Remove(op);c.Done?.Invoke(ok,reason);
                    if(c.Kind==0&&view.PendingKind=="FreeLearn"&&view.PendingActor==actor&&Guid.TryParseExact(view.PendingOperation,"N",out _)&&WorkbenchEnchantmentRules.Known(view.PendingChoice)&&Local(c.Player,c.Player.GetCurrentCraftingStation()))Queue(c.Player,c.Player.GetCurrentCraftingStation(),1,view.PendingChoice,null);}
            }
            catch{}
        }
    }
}


