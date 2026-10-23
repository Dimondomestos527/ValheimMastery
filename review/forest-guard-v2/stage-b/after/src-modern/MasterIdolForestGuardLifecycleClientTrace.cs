using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Client-only bounded observation; no native/ZDO/RPC writes or authority bypass.
    internal static class MasterIdolForestGuardLifecycleClientTrace
    {
        static ZNet Net;static ZDOMan Manager;static ulong Generation;static long World;
        static float Next,StopAt;static int Lines;static string LastHeader;
        static readonly Dictionary<ZDOID,string> Last=new Dictionary<ZDOID,string>();
        internal static void Tick()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;
            if(net?.IsServer()!=false||net.GetWorld()==null||manager==null)return;
            long world=net.GetWorldUID();
            if(Net!=net||Manager!=manager||Generation!=MasterIdolLoadIdentity.Generation||World!=world)
            {Net=net;Manager=manager;Generation=MasterIdolLoadIdentity.Generation;World=world;Next=0;StopAt=Time.unscaledTime+300;Lines=0;LastHeader=null;Last.Clear();}
            if(Lines>=64||Time.unscaledTime>=StopAt||Time.unscaledTime<Next)return;Next=Time.unscaledTime+2;
            var rows=MasterIdolForestGuardLifecycle.DiagnosticRows(out var summary);
            var ids=new List<ZDOID>(4);
            foreach(var row in rows){if(ids.Count>=4)break;if(!ids.Contains(row.Id))ids.Add(row.Id);}
            var geometry=MasterIdolEffectZones.ObserveCurrent();
            if(geometry!=null)foreach(string uid in geometry.ResidentHomes.Keys)
            {
                if(ids.Count>=4)break;
                if(MasterIdolNetworkQuota.TryUid(uid,out long user,out uint number))
                {var id=new ZDOID(user,number);if(!ids.Contains(id))ids.Add(id);}
            }
            foreach(var stale in new List<ZDOID>(Last.Keys))if(!ids.Contains(stale))Last.Remove(stale);
            string header=summary+" projectionReady="+(geometry!=null)+" subjects="+ids.Count;
            if(header!=LastHeader){Write("world="+world+" "+header);LastHeader=header;}
            foreach(var id in ids)
            {
                var data=manager.GetZDO(id);string state="uid="+id+" "+MasterIdolForestGuardLifecycle.DiagnosticSubject(data);
                if(data!=null)
                {
                    var instance=ZNetScene.instance?.FindInstance(id);var actor=instance!=null?instance.GetComponent<Character>():null;
                    var ai=actor!=null?actor.GetBaseAI():null;
                    state+=" family="+MasterIdolForestGuardState.Family(data)+" nativeOwner="+data.GetOwner()+" nativeOwnerRev="+data.OwnerRevision+" localWriter="+(data.GetOwner()==ZNet.GetUID())+
                        " nativePhase="+data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)+" nativeCycle="+data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0)+" nativeIntent="+data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0)+
                        " loaded="+(actor!=null)+" active="+(actor!=null&&actor.gameObject.activeSelf)+" ai="+(ai!=null)+
                        " aiViewReady="+(ai!=null&&ai.m_nview!=null&&ai.m_nview.IsValid()&&ReferenceEquals(ai.m_nview.GetZDO(),data))+
                        " cuePlaying="+(actor!=null&&MasterIdolResidentBirthPresentation.CosmeticPlaying(actor))+" "+MasterIdolForestGuardLifecycleOwner.Diagnostic(id);
                }
                if(Last.TryGetValue(id,out string before)&&before==state)continue;
                // Maximum four identities retained. Replaced/dead subjects cannot
                // displace an unlimited stream into a global scan.
                if(!Last.ContainsKey(id)&&Last.Count>=4)continue;
                Last[id]=state;Write("world="+world+" "+state);
            }
        }
        static void Write(string message){if(Lines>=64)return;Lines++;MasteryPlugin.Log?.LogInfo("[ForestLifecycleClient] "+message);}
    }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolForestGuardLifecycleClientTracePatch
    {static void Postfix()=>MasterIdolForestGuardLifecycleClientTrace.Tick();}
}
