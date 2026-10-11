using System;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    [HarmonyPatch(typeof(ZDOMan),"HandleDestroyedZDO")]
    internal static class WorkbenchEnchantmentDestroyedPatch
    {
        sealed class Observation{internal ZNet Session;internal ZDOMan Manager;internal long World;internal ulong Generation;internal string Station,Uid,Witness;internal ZDOID Native;}
        static void Prefix(ZDOMan __instance,ZDOID uid,out Observation __state)
        {
            __state=null;var net=ZNet.instance;if(net?.IsServer()!=true||!WorkbenchEnchantmentRuntime.Ready)return;var data=__instance.GetZDO(uid);if(data?.GetPrefab()!="piece_workbench".GetStableHashCode())return;
            string id=data.GetString(WorkbenchEnchantmentRuntime.StationKey,"");var b=WorkbenchEnchantmentRuntime.Journal.ReadStation(id);if(b==null||b.NativeWitness!=WorkbenchEnchantmentRuntime.Witness(data)||!WorkbenchEnchantmentRuntime.UniqueNative(data,id))return;
            if(b.NativeUid!=uid.ToString()){if(!WorkbenchEnchantmentRuntime.BindLoadedNative(data))return;b=WorkbenchEnchantmentRuntime.Journal.ReadStation(id);}
            __state=new Observation{Session=net,Manager=__instance,World=net.GetWorldUID(),Generation=MasterIdolLoadIdentity.Generation,Station=id,Uid=uid.ToString(),Witness=b.NativeWitness,Native=uid};
        }
        static void Postfix(ZDOMan __instance,ZDOID uid,Observation __state,bool __runOriginal)
        {
            var s=__state;if(s==null||!__runOriginal)return;
            WorkbenchEnchantmentRuntime.Service?.DestroyVerified(s.World,s.Station,s.Uid,()=>s.Session==ZNet.instance&&s.Manager==__instance&&__instance==ZDOMan.instance&&s.World==ZNet.instance?.GetWorldUID()&&s.Generation==MasterIdolLoadIdentity.Generation&&uid==s.Native&&__instance.GetZDO(uid)==null&&WorkbenchEnchantmentRuntime.Journal?.ReadStation(s.Station)?.NativeWitness==s.Witness);
        }
    }
}


