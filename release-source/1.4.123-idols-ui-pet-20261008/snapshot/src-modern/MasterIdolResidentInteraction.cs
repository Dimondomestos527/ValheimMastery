using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // A runtime adapter only: friendship/home/name remain native persistent ZDO data.
    internal sealed class MasterIdolResidentPet : MonoBehaviour
    {
        internal Character Actor; internal Tameable Native; internal ZDO Data;
        internal ZNet Session; internal ZDOMan Manager; internal long World; internal ulong Generation;
        internal string Home;
        internal bool Current => Session != null && ReferenceEquals(Session,ZNet.instance) &&
            ReferenceEquals(Manager,ZDOMan.instance) && World==Session.GetWorldUID() && Generation==MasterIdolLoadIdentity.Generation && MasterIdolLoadIdentity.LoadReady &&
            Actor?.m_nview?.IsValid()==true && !Actor.IsDead() && Data?.Persistent==true &&
            ReferenceEquals(Actor.m_nview.GetZDO(),Data) && ReferenceEquals(Manager.GetZDO(Data.m_uid),Data) &&
            MasterIdolNativeTaming.Assigned(Data) && MasterIdolResidents.Family(Actor) &&
            Home==Data.GetString(MasterIdolNativeTaming.HomeKey,"");
    }
    internal static class MasterIdolResidentPetRules
    {
        internal static bool Name(string name)
        {
            if(name==null||name.Length>10)return false;
            foreach(char c in name)if(char.IsControl(c)||c=='<'||c=='>')return false;
            return true;
        }
    }
    internal static class MasterIdolResidentInteraction
    {
        internal static void Ensure(Character actor)
        {
            var view=actor?.m_nview; var data=view?.GetZDO();
            if(!MasterIdolLoadIdentity.LoadReady||ZNet.instance==null||ZDOMan.instance==null||view?.IsValid()!=true||actor.IsDead()||
                data?.Persistent!=true||!ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid),data)||
                !MasterIdolResidents.Family(actor)||!MasterIdolNativeTaming.Assigned(data)||actor.GetComponent<MonsterAI>()==null)return;
            var tag=actor.GetComponent<MasterIdolResidentPet>();
            if(tag!=null||actor.GetComponent<Tameable>()!=null)return; // never mutate a foreign/manual component
            tag=actor.gameObject.AddComponent<MasterIdolResidentPet>();
            tag.Actor=actor;tag.Data=data;tag.Session=ZNet.instance;tag.Manager=ZDOMan.instance;
            tag.World=tag.Session.GetWorldUID();tag.Generation=MasterIdolLoadIdentity.Generation;tag.Home=data.GetString(MasterIdolNativeTaming.HomeKey,"");
            // Awake registers native name/command RPC on every peer, including headless owner.
            tag.Native=actor.gameObject.AddComponent<Tameable>();
            tag.Native.m_commandable=false;tag.Native.m_unsummonDistance=0;tag.Native.m_unsummonOnOwnerLogoutSeconds=0;
            tag.Native.CancelInvoke("TamingUpdate");tag.Native.enabled=false;
        }
        internal static MasterIdolResidentPet Tag(Tameable tame)
        {
            var tag=tame?.GetComponent<MasterIdolResidentPet>();
            return tag!=null&&ReferenceEquals(tag.Native,tame)?tag:null;
        }
        internal static bool PlayerAccess(MasterIdolResidentPet tag,Player player)
        {
            var data=player?.m_nview?.GetZDO();
            return tag?.Current==true&&player!=null&&!player.IsDead()&&player.m_nview?.IsValid()==true&&
                data!=null&&ReferenceEquals(ZDOMan.instance.GetZDO(data.m_uid),data)&&
                player.GetPlayerID()!=0&&data.GetLong(ZDOVars.s_playerID,0)==player.GetPlayerID()&&
                (player.transform.position-tag.Actor.transform.position).sqrMagnitude<=25f&&
                MasterIdolWorldIndex.WardAllows(player.GetPlayerID(),tag.Actor.transform.position);
        }
        internal static bool Sender(Player player,long sender,string author)
        {
            var net=ZNet.instance;var data=player?.m_nview?.GetZDO();
            if(net==null||data==null||data.GetOwner()!=sender)return false;
            bool local=sender==ZNet.GetUID()&&ReferenceEquals(player,Player.m_localPlayer);
            if(net.IsServer()&&!local)
            {
                var peer=net.GetPeer(sender);
                if(peer?.IsReady()!=true||peer.m_characterID!=data.m_uid||peer.m_playerID!=player.GetPlayerID())return false;
            }
            else if(!net.IsServer()&&net.GetServerPeer()?.IsReady()!=true)return false;
            if(author=="host")return local; // native unsigned local host only, never a remote attribution
            return Splatform.PlatformUserID.TryParse(author,out var id)&&id.IsValid&&
                ZNet.TryGetPlayerByPlatformUserID(id,out var info)&&info.m_characterID==data.m_uid;
        }
        internal static bool Rename(Tameable tame,long sender,string name,string author)
        {
            var tag=Tag(tame);
            if(tag?.Current!=true||!tag.Actor.m_nview.IsOwner()||sender==0||
                !MasterIdolResidentPetRules.Name(name)||author==null||author.Length>128)return false;
            foreach(char c in author)if(char.IsControl(c))return false;
            foreach(var player in Player.GetAllPlayers())
                if(PlayerAccess(tag,player)&&Sender(player,sender,author))return true;
            return false;
        }
    }
    [HarmonyPatch(typeof(Tameable),nameof(Tameable.Interact))]
    internal static class MasterIdolResidentPetInteractPatch
    {
        private static bool Prefix(Tameable __instance,Humanoid __0,ref bool __result)
        {
            var tag=MasterIdolResidentInteraction.Tag(__instance);if(tag==null)return true;
            if(Application.isBatchMode||!MasterIdolResidentInteraction.PlayerAccess(tag,__0 as Player)){__result=false;return false;}
            // Native pet text/stat/throttle and native platform-aware rename dialog; no follow command.
            return true;
        }
    }
    [HarmonyPatch(typeof(Tameable),"RPC_SetName")]
    internal static class MasterIdolResidentPetNamePatch
    {
        private static bool Prefix(Tameable __instance,long __0,string __1,string __2)=>
            MasterIdolResidentInteraction.Tag(__instance)==null||MasterIdolResidentInteraction.Rename(__instance,__0,__1,__2);
    }
    [HarmonyPatch(typeof(Tameable),nameof(Tameable.GetHoverText))]
    internal static class MasterIdolResidentPetHoverPatch
    {
        private static bool Prefix(Tameable __instance,ref string __result)
        {
            var tag=MasterIdolResidentInteraction.Tag(__instance);if(tag==null||tag.Current)return true;
            __result="";return false;
        }
    }
    [HarmonyPatch(typeof(Tameable),"IsTamed")]
    internal static class MasterIdolResidentPetTamedPatch
    {
        private static bool Prefix(Tameable __instance,ref bool __result)
        {var tag=MasterIdolResidentInteraction.Tag(__instance);if(tag==null)return true;__result=tag.Current;return false;}
    }
    [HarmonyPatch(typeof(Tameable),"IsHungry")]
    internal static class MasterIdolResidentPetHungerPatch
    {
        private static bool Prefix(Tameable __instance,ref bool __result)
        {if(MasterIdolResidentInteraction.Tag(__instance)==null)return true;__result=false;return false;}
    }
    // Adapter adds no feeding, follow, taming-progress or summon/despawn subsystem.
    [HarmonyPatch(typeof(Tameable),"RPC_Command")]
    internal static class MasterIdolResidentPetCommandPatch
    {private static bool Prefix(Tameable __instance)=>MasterIdolResidentInteraction.Tag(__instance)==null;}
    [HarmonyPatch(typeof(Tameable),"Command")]
    internal static class MasterIdolResidentPetLocalCommandPatch
    {private static bool Prefix(Tameable __instance)=>MasterIdolResidentInteraction.Tag(__instance)==null;}
    [HarmonyPatch(typeof(Tameable),"OnConsumedItem")]
    internal static class MasterIdolResidentPetFeedPatch
    {private static bool Prefix(Tameable __instance)=>MasterIdolResidentInteraction.Tag(__instance)==null;}
    [HarmonyPatch(typeof(Tameable),"TamingUpdate")]
    internal static class MasterIdolResidentPetTamingPatch
    {private static bool Prefix(Tameable __instance)=>MasterIdolResidentInteraction.Tag(__instance)==null;}
    [HarmonyPatch(typeof(Tameable),"UnSummon")]
    internal static class MasterIdolResidentPetUnsummonPatch
    {private static bool Prefix(Tameable __instance)=>MasterIdolResidentInteraction.Tag(__instance)==null;}
    [HarmonyPatch(typeof(Tameable),"RPC_UnSummon")]
    internal static class MasterIdolResidentPetUnsummonRpcPatch
    {private static bool Prefix(Tameable __instance)=>MasterIdolResidentInteraction.Tag(__instance)==null;}
}
