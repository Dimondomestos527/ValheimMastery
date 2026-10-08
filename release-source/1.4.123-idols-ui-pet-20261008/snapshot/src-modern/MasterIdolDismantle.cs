using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class MasterIdolDismantle
    {
        private static ZNet Session;
        private static readonly Dictionary<ZDOID,string> Approved=new Dictionary<ZDOID,string>();
        private static float Next,NextReconcile,Retry,RetryUntil;private static bool PendingRequest;private static Player RequestActor;private static ZDOID RequestCharacter;private static ZRpc RequestRpc;private static ulong RequestGeneration;
        private static ZDOID Requested;private static string RequestedToken;private static long RequestedWorld;
        internal static void Register(ZNetPeer peer)
        {peer?.m_rpc?.Register<ZPackage>("VM_Idol_Dismantle_v1",Receive);peer?.m_rpc?.Register<ZPackage>("VM_Idol_Dismantle_Result_v1",Reply);}
        internal static void Tick()
        {
            if(Session!=ZNet.instance){Session=ZNet.instance;Approved.Clear();Next=NextReconcile=0;PendingRequest=false;}
            if(PendingRequest&&Time.unscaledTime>=Retry){Retry=Time.unscaledTime+.5f;var player=Player.m_localPlayer;var zdo=ZDOMan.instance?.GetZDO(Requested);if(Time.unscaledTime>=RetryUntil||!ReferenceEquals(player,RequestActor)||ZNet.instance.GetWorldUID()!=RequestedWorld||MasterIdolLoadIdentity.Generation!=RequestGeneration||(ZNet.instance.IsServer()?null:ZNet.instance.GetServerRPC())!=RequestRpc||player==null||player.m_nview?.IsValid()!=true||player.m_nview.GetZDO().m_uid!=RequestCharacter||player.IsDead()||zdo==null||zdo.GetString(MasterIdolWorldRegistry.TokenKey,"")!=RequestedToken){PendingRequest=false;}else{var packet=new ZPackage();packet.Write(RequestedWorld);packet.Write(player.GetPlayerID());packet.Write(Requested);packet.Write(RequestedToken);if(ZNet.instance.IsServer()){packet.SetPos(0);Receive(null,packet);}else ZNet.instance.GetServerRPC()?.Invoke("VM_Idol_Dismantle_v1",packet);}}
            if(Time.unscaledTime>=NextReconcile){NextReconcile=Time.unscaledTime+2f;MasterIdolFavor.Reconcile();}
        }
        internal static bool NativeAllowed(WearNTear wear)
        {
            var zdo=wear?.GetComponent<ZNetView>()?.GetZDO();
            return ZNet.instance?.IsServer()==true&&Session==ZNet.instance&&zdo!=null&&Approved.TryGetValue(zdo.m_uid,out var token)&&zdo.GetString(MasterIdolWorldRegistry.TokenKey,"")==token;
        }
        internal static void Request(WearNTear wear)
        {
            var player=Player.m_localPlayer;var net=ZNet.instance;var idol=wear?.GetComponent<ZNetView>()?.GetZDO();
            if(player==null||net==null||idol==null||Time.unscaledTime<Next)return;
            Next=Time.unscaledTime+.5f;
            Requested=idol.m_uid;RequestedToken=idol.GetString(MasterIdolWorldRegistry.TokenKey,"");RequestedWorld=net.GetWorldUID();RequestActor=player;RequestCharacter=player.m_nview.GetZDO().m_uid;RequestRpc=net.IsServer()?null:net.GetServerRPC();RequestGeneration=MasterIdolLoadIdentity.Generation;PendingRequest=true;RetryUntil=Time.unscaledTime+10;Retry=Time.unscaledTime+.5f;
            var packet=new ZPackage();packet.Write(RequestedWorld);packet.Write(player.GetPlayerID());packet.Write(Requested);packet.Write(RequestedToken);
            if(net.IsServer()){packet.SetPos(0);Receive(null,packet);}else net.GetServerRPC()?.Invoke("VM_Idol_Dismantle_v1",packet);
        }
        private static void Respond(ZRpc rpc,long player,ZDOID id,string token,string text)
        {
            var packet=new ZPackage();packet.Write(ZNet.instance.GetWorldUID());packet.Write(player);packet.Write(id);packet.Write(token);packet.Write(text);
            if(rpc==null){packet.SetPos(0);Reply(null,packet);}else rpc.Invoke("VM_Idol_Dismantle_Result_v1",packet);
        }
        private static void Reply(ZRpc rpc,ZPackage packet)
        {
            var net=ZNet.instance;var player=Player.m_localPlayer;
            if(net==null||player==null||packet==null||packet.Size()>1024||(net.IsServer()?rpc!=null:rpc!=net.GetServerRPC()))return;
            try{long world=packet.ReadLong(),payer=packet.ReadLong();var id=packet.ReadZDOID();string token=packet.ReadString(),text=packet.ReadString();
                if(world==net.GetWorldUID()&&world==RequestedWorld&&payer==player.GetPlayerID()&&id==Requested&&token==RequestedToken&&text.Length<=256)
                    {PendingRequest=false;player.Message(MessageHud.MessageType.Center,"<color=#FFD36A>"+text+"</color>");}}catch{}
        }
        private static void Receive(ZRpc rpc,ZPackage packet)
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||packet==null||packet.Size()>256)return;
            if(Session!=net){Session=net;Approved.Clear();}
            try
            {
                if(packet.ReadLong()!=net.GetWorldUID())return;
                long player=packet.ReadLong();var id=packet.ReadZDOID();string token=packet.ReadString();var actor=WorkshopActor.Resolve(rpc);var idol=ZDOMan.instance?.GetZDO(id);
                if(actor?.Available!=true||actor.GetPlayerID()!=player||actor.IsDead()||actor.IsTeleporting()||idol==null||
                    !Guid.TryParseExact(token,"N",out _)||idol.GetString(MasterIdolWorldRegistry.TokenKey,"")!=token||MasterIdolWorldRegistry.Profile(idol.GetPrefab())==null||
                    Vector3.Distance(actor.EyePoint,idol.GetPosition())>actor.PlaceDistance+1f)return;
                bool hammer=rpc==null?GoldCraftingService.HoldsHammer(Player.m_localPlayer):ZDOMan.instance.GetZDO(actor.CharacterId)?.GetInt(ZDOVars.s_rightItem,0)=="Hammer".GetStableHashCode();
                if(!hammer)return;
                MasterIdolWorldRegistry.Refresh();
                if(!MasterIdolWorldIndex.WardAllows(player,idol.GetPosition())){Respond(rpc,player,id,token,"Оберіг не дозволяє тобі розібрати це творіння.");return;}
                var instance=ZNetScene.instance?.FindInstance(id);var piece=instance?.GetComponent<Piece>();var wear=instance?.GetComponent<WearNTear>();var view=instance?.GetComponent<ZNetView>();
                var store=MasterIdolWorldRegistry.Store;var entry=store?.Find(token);
                if(store?.Ready!=true||entry?.Applied!=true||!MasterIdolLoadIdentity.Matches(entry,idol)||entry.Type!=MasterIdolWorldRegistry.Profile(idol.GetPrefab()).Id||
                    idol.GetLong(ZDOVars.s_creator,0)!=entry.Player||piece==null||wear==null||view?.GetZDO()!=idol||!piece.m_canBeRemoved||!piece.CanBeRemoved())
                {if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogWarning("[MasterIdols] dismantle denied=identity-or-native-state output="+id+" historical="+(entry?.Output??"missing"));Respond(rpc,player,id,token,"Вьолундр ще не впізнав це творіння після повернення у світ. Зачекай мить.");return;}
                if(!entry.Closed && !store.BeginDismantle(token,entry.Player,entry.Output))return;
                entry=store.Find(token);
                if(!MasterIdolFavor.Begin(entry,actor)){Respond(rpc,player,id,token,"Вьолундр ще зберігає долю цього творіння. Зачекай мить.");return;}
                if(Approved.Count>=4096&&!Approved.ContainsKey(id))return;
                Approved[id]=token;
                MasterIdolWorldRegistry.Refresh(true); // disable before removal; desired state is retained.
                view.ClaimOwnership();wear.Remove(false); // native resource drops, removal effects and destroy path.
            }
            catch(Exception error){MasteryPlugin.Log.LogWarning("[MasterIdols] Dismantle deferred: "+error.Message);}
        }
        internal static void Destroyed(ZDOID id){Approved.Remove(id);MasterIdolFavor.Reconcile();}
    }
    [HarmonyPatch(typeof(WearNTear),nameof(WearNTear.Remove))]
    internal static class MasterIdolRemovePatch
    {
        private static bool Prefix(WearNTear __instance)
        {if(!MasterIdolDurability.Protected(__instance)||MasterIdolDismantle.NativeAllowed(__instance))return true;MasterIdolDismantle.Request(__instance);return false;}
    }
    [HarmonyPatch(typeof(WearNTear),"RPC_Remove")]
    internal static class MasterIdolRemoveRpcPatch
    {private static bool Prefix(WearNTear __instance)=>!MasterIdolDurability.Protected(__instance)||MasterIdolDismantle.NativeAllowed(__instance);}
}
