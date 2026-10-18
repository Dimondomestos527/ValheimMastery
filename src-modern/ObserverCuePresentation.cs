using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Per-viewer, bounded cosmetic leases. No gameplay RPC, inventory, save, HUD or authority calls.
    internal sealed class ObserverCuePresentation:MonoBehaviour
    {
        private sealed class Run
        {internal ObserverCueEvent Event;internal int Pulse;internal bool Audio,Visual;internal float Next;internal Player Actor;}
        private readonly Dictionary<string,Run> Pending=new Dictionary<string,Run>(StringComparer.Ordinal);
        private readonly Dictionary<string,double> ClosedWork=new Dictionary<string,double>(StringComparer.Ordinal);
        private readonly List<string> Remove=new List<string>(32);
        private ZNet Net;private ZNetScene Scene;private ZDOMan Manager;private Player Viewer;private ulong Generation;private long World;
        internal static void Install()=>ObserverCueNetwork.Configure(Receive);
        private static bool Graphics=>!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
        private static void Receive(ObserverCueEvent cue)
        {
            var viewer=Player.m_localPlayer;
            if(!Graphics||viewer==null||cue==null)return;
            var sink=viewer.GetComponent<ObserverCuePresentation>()??viewer.gameObject.AddComponent<ObserverCuePresentation>();
            sink.Accept(cue);
        }
        private bool Context()
        {
            var net=ZNet.instance;var scene=ZNetScene.instance;var manager=ZDOMan.instance;var viewer=Player.m_localPlayer;
            if(!Graphics||net?.GetWorld()==null||scene==null||manager==null||viewer==null||viewer!=GetComponent<Player>()||viewer.IsDead()||viewer.IsTeleporting()||MasteryPlugin.Instance?.isActiveAndEnabled!=true||!MasteryPlugin.Settings.Enabled.Value)
            {Clear();return false;}
            if(Net!=net||Scene!=scene||Manager!=manager||Viewer!=viewer||Generation!=MasterIdolLoadIdentity.Generation||World!=net.GetWorldUID())
            {Clear();Net=net;Scene=scene;Manager=manager;Viewer=viewer;Generation=MasterIdolLoadIdentity.Generation;World=net.GetWorldUID();}
            if(!net.IsServer()&&!NetworkSync.HasCurrentServerSettings(net.GetServerRPC())){Clear();return false;}
            return true;
        }
        private void Clear(){Pending.Clear();ClosedWork.Clear();Remove.Clear();Net=null;Scene=null;Manager=null;Viewer=null;World=0;Generation=0;}
        private void Accept(ObserverCueEvent cue)
        {
            if(!Context()||cue.World!=World||Net.GetTime().Ticks>=cue.Until)return;
            PruneClosed();
            if(cue.Kind==ObserverCueKind.CraftingStop)
            {
                Remove.Clear();foreach(var pair in Pending)if(pair.Value.Event.WorkKey==cue.WorkKey)Remove.Add(pair.Key);
                foreach(var key in Remove)Pending.Remove(key);
                if(ClosedWork.Count<128)ClosedWork[cue.WorkKey]=Time.unscaledTimeAsDouble+30;
                return;
            }
            if(cue.Kind==ObserverCueKind.CraftingWork&&ClosedWork.ContainsKey(cue.WorkKey)||Pending.ContainsKey(cue.Key)||Pending.Count>=32)return;
            int skipped=cue.Kind==ObserverCueKind.CraftingWork?Math.Max(0,(int)((Net.GetTime().Ticks-cue.Issued)/(double)TimeSpan.TicksPerSecond)):0;
            Pending.Add(cue.Key,new Run{Event=cue,Pulse=Math.Min(4,skipped),Next=Time.unscaledTime});
            Tick();
        }
        private void PruneClosed()
        {
            Remove.Clear();foreach(var pair in ClosedWork)if(Time.unscaledTimeAsDouble>=pair.Value)Remove.Add(pair.Key);
            foreach(var key in Remove)ClosedWork.Remove(key);
        }
        private bool Station(ObserverCueEvent cue,out CraftingStation station)
        {
            station=null;var go=Scene.FindInstance(cue.Station);var view=go?.GetComponent<ZNetView>();var data=view?.GetZDO();
            if(view?.IsValid()!=true||data==null||data.m_uid!=cue.Station||!ReferenceEquals(Manager.GetZDO(cue.Station),data)||data.GetPrefab()!="piece_workbench".GetStableHashCode()||
                data.GetString(WorkbenchEnchantmentRuntime.StationKey,"")!=cue.Marker)return false;
            station=go.GetComponent<CraftingStation>();return station!=null;
        }
        private bool Actor(Run run)
        {
            var cue=run.Event;var actor=Scene.FindInstance(cue.Actor)?.GetComponent<Player>();var view=actor?.m_nview;var data=view?.GetZDO();
            if(actor==null||actor.IsDead()||actor.IsTeleporting()||view?.IsValid()!=true||data==null||!ReferenceEquals(Manager.GetZDO(cue.Actor),data)||
                data.m_uid!=cue.Actor||data.GetOwner()!=cue.Owner||data.OwnerRevision!=cue.OwnerRevision||actor.GetPlayerID()!=cue.Player)return false;
            run.Actor=actor;return true;
        }
        private bool Step(Run run)
        {
            var cue=run.Event;long now=Net.GetTime().Ticks;
            if(now>=cue.Until)return false;
            if(now<cue.Issued)return true; // Wait out allowed clock skew; never consume/restart the original preparation window.
            if(cue.Kind==ObserverCueKind.EnchantLearn||cue.Kind==ObserverCueKind.EnchantActivate)
            {
                if(!Station(cue,out var station))return true; // Retry streaming/marker readiness only within original preparation.
                if(cue.Kind==ObserverCueKind.EnchantLearn)
                {if((Viewer.transform.position-station.transform.position).sqrMagnitude>64*64)return false;}
                else
                {
                    var zone=MasterIdolEffectZones.Find(cue.Station+"/"+cue.Marker);
                    if(zone==null||zone.Type!=cue.Choice)return true;
                    if(!zone.Contains(Viewer.transform.position,64))return false;
                }
                WorkbenchEnchantmentBenchVisual.AcceptedObserver(station,cue);return false;
            }
            if(!Actor(run))return run.Actor==null; // A once-bound actor losing identity/peace ends, never resumes.
            if((Viewer.transform.position-run.Actor.transform.position).sqrMagnitude>64*64)return false;
            if(cue.Kind==ObserverCueKind.CraftingApplied)
            {
                // Each independent channel consumes once; a cold VFX cannot replay an already played SFX.
                if(!run.Audio)run.Audio=GoldMasterworkPresentation.ObserverSuccessAudio(cue.Position);
                if(!run.Visual)run.Visual=GoldMasterworkPresentation.ObserverSuccessVisual(cue.Position);
                return !run.Audio||!run.Visual;
            }
            if(cue.Kind!=ObserverCueKind.CraftingWork||ClosedWork.ContainsKey(cue.WorkKey))return false;
            CraftingStation workbench=cue.Station==ZDOID.None?null:Scene.FindInstance(cue.Station)?.GetComponent<CraftingStation>();
            if(cue.Station!=ZDOID.None&&workbench==null)return true;
            if(!run.Audio){run.Audio=true;GoldMasterworkPresentation.ObserverWorkingAudio(workbench,run.Actor.transform.position);}
            if(run.Pulse<4)
            {
                double elapsed=(now-cue.Issued)/(double)TimeSpan.TicksPerSecond;
                if(elapsed>=run.Pulse)
                {
                    GoldMasterworkPresentation.ObserverWorkingPulse(run.Actor.transform.position+Vector3.up*.95f+run.Actor.transform.forward*.35f);
                    run.Pulse=Math.Min(4,Math.Max(run.Pulse+1,(int)Math.Floor(elapsed)+1));
                }
            }
            return true; // Never restart or catch up missed pulses. Original server five-second deadline owns this lease.
        }
        private void Tick()
        {
            if(!Context())return;PruneClosed();Remove.Clear();
            foreach(var pair in Pending)
            {
                var run=pair.Value;if(Time.unscaledTime<run.Next)continue;run.Next=Time.unscaledTime+.1f;
                try{if(!Step(run))Remove.Add(pair.Key);}catch(Exception){Remove.Add(pair.Key);}
            }
            foreach(var key in Remove)Pending.Remove(key);
        }
        private void Update()=>Tick();
        private void OnDisable()=>Clear();
        private void OnDestroy()=>Clear();
    }
}
