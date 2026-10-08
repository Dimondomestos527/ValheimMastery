using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolLight:MonoBehaviour
    {
        internal Fireplace Fire;internal ZNetView View;internal float Next;internal LinkedListNode<MasterIdolLight> Node;
        private void Awake(){Fire=GetComponent<Fireplace>();View=GetComponent<ZNetView>();}
        private void OnEnable(){MasterIdolLighting.Register(this);}
        private void OnDisable(){MasterIdolLighting.Remove(this);}
        private void OnDestroy(){MasterIdolLighting.Remove(this);}
    }
    internal static class MasterIdolLighting
    {
        private static readonly LinkedList<MasterIdolLight> Loaded=new LinkedList<MasterIdolLight>();private static float Next;
        private static long Checks,Writes;
        internal static void Register(MasterIdolLight light){if(light.Node==null&&Loaded.Count<4096)light.Node=Loaded.AddLast(light);}
        internal static void Remove(MasterIdolLight light){if(light.Node!=null){Loaded.Remove(light.Node);light.Node=null;}}
        internal static string Describe()=>"lighting loaded="+Loaded.Count+" checks="+Checks+" writes="+Writes+" budget=8checks/2writes per0.5s";
        internal static void Tick()
        {
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;if(!MasterIdolEffectZones.Ready)return;
            int budget=Math.Min(8,Loaded.Count),written=0;
            for(int n=0;n<budget;n++)
            {
                var light=Loaded.First.Value;Loaded.RemoveFirst();light.Node=Loaded.AddLast(light);
                if(light==null||light.Fire==null||light.View?.IsValid()!=true||!light.View.IsOwner()||Time.unscaledTime<light.Next)continue;
                light.Next=Time.unscaledTime+5;Checks++;
                var fire=light.Fire;var zdo=light.View.GetZDO();var zone=MasterIdolEffectZones.At("BlackForest",light.transform.position);
                if(zone==null||!MasterIdolActivity.Near(light.transform.position)||fire.m_infiniteFuel||!fire.m_canRefill||fire.m_fuelItem==null||zdo.GetInt(ZDOVars.s_state,1)==2)continue;
                float fuel=zdo.GetFloat(ZDOVars.s_fuel,0),max=fire.m_maxFuel;
                if(!MasterIdolResidentCycleRules.Refill(fuel,max))continue;
                if(!MasterIdolNetworkQuota.TryUid(zone.Id.Split('/')[0],out long user,out uint id))continue;
                var idol=ZDOMan.instance?.GetZDO(new ZDOID(user,id));long creator=idol?.GetLong(ZDOVars.s_creator,0)??0;
                if(creator==0||!MasterIdolEffectZones.Allows(creator,light.transform.position))continue;
                bool resin=Utils.GetPrefabName(fire.m_fuelItem.gameObject)=="Resin",worker=false;
                foreach(var member in zone.Residents)
                {
                    if(!MasterIdolResidentCycleRules.Role(member.Value,resin)||!MasterIdolNetworkQuota.TryUid(member.Key,out long npcUser,out uint npcId))continue;
                    var instance=ZNetScene.instance?.FindInstance(new ZDOID(npcUser,npcId));var ai=instance?.GetComponent<BaseAI>();var actor=instance?.GetComponent<Character>();
                    if(actor==null||ai==null||ai.IsSleeping()||!zone.Contains(actor.transform.position)||MasterIdolSocialSeats.Battle(ai,actor))continue;
                    worker=true;break;
                }
                // Commit only on the current Fireplace owner; no amount RPC or inventory path.
                if(!worker||!light.View.IsOwner()||MasterIdolEffectZones.Find(zone.Id)!=zone)continue;
                zdo.Set(ZDOVars.s_fuel,max);fire.UpdateState();Writes++;MasterIdolNativePresentation.Refilled(fire,fuel,max);
                if(++written>=2)break;
            }
        }
    }
    [HarmonyPatch(typeof(Fireplace),"Awake")]
    internal static class MasterIdolLightAttach
    {private static void Postfix(Fireplace __instance){if(__instance.GetComponent<MasterIdolLight>()==null)__instance.gameObject.AddComponent<MasterIdolLight>();}}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolLightTick
    {private static void Postfix(){try{MasterIdolTiming.Measure("lighting.batch",()=>{MasterIdolLighting.Tick();return true;});}catch(Exception error){MasteryPlugin.Log.LogWarning("[MasterIdols] Lighting deferred: "+error.Message);}}}
}


