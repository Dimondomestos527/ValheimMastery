using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
namespace ValheimMastery
{
    // Local cosmetic projection only; no ZDO writes, lights, colliders or world scan.
    internal sealed class WorkbenchEnchantmentBenchVisual : MonoBehaviour
    {
        static readonly LinkedList<WorkbenchEnchantmentBenchVisual> Loaded=new LinkedList<WorkbenchEnchantmentBenchVisual>();
        static readonly Dictionary<string,Material> GlowMaterials=new Dictionary<string,Material>(StringComparer.Ordinal);static float Next;
        LinkedListNode<WorkbenchEnchantmentBenchVisual> node;ZNetView view;ZDO identity;ZDOID uid;
        string marker,home,choice;SpriteRenderer symbol;float RetryAsset;static int Pulses;
        sealed class Ritual
        {
            internal Coroutine Routine;internal bool Slot;internal GameObject Root;
            internal ZNet Session;internal ZNetScene Scene;internal Player Player;internal long World;
            internal ZDO Data;internal ZDOID Uid;internal string Marker,Home,Type,EndReason="deadline";internal bool FirstLearn,RootReported,EmitReported;internal int EmitCalls;
        }
        Ritual ritual;
        IDisposable audioLease;
        MaterialPropertyBlock pulseProperties;
        static bool Graphics=>!Application.isBatchMode&&SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null;
        internal static void Attach(CraftingStation station)
        {
            if(!Graphics||station==null||Utils.GetPrefabName(station.gameObject)!="piece_workbench")return;
            if(station.GetComponent<WorkbenchEnchantmentBenchVisual>()==null)station.gameObject.AddComponent<WorkbenchEnchantmentBenchVisual>();
        }
        void OnEnable(){if(Graphics&&node==null)node=Loaded.AddLast(this);}
        void OnDisable(){StopAudio();StopPulse();if(node!=null){Loaded.Remove(node);node=null;}Hide();}
        void OnDestroy(){OnDisable();if(symbol!=null)Destroy(symbol.gameObject);}
        void Hide(){if(symbol!=null)symbol.enabled=false;choice=null;}
        internal static void Accepted(CraftingStation station,string type,bool firstLearn)
        {
            if(!Graphics||station==null||!WorkbenchEnchantmentRules.Known(type)||Player.m_localPlayer==null||
                (Player.m_localPlayer.transform.position-station.transform.position).sqrMagnitude>64)return;
            Attach(station);var visual=station.GetComponent<WorkbenchEnchantmentBenchVisual>();
            if(visual==null||!visual.isActiveAndEnabled)return;
            var data=station.GetComponent<ZNetView>()?.GetZDO();var net=ZNet.instance;
            if(data==null||net?.GetWorld()==null||net.GetWorldUID()==0||ZNetScene.instance==null)return;
            var run=new Ritual{Session=net,Scene=ZNetScene.instance,Player=Player.m_localPlayer,World=net.GetWorldUID(),
                Data=data,Uid=data.m_uid,Marker=data.GetString(WorkbenchEnchantmentRuntime.StationKey,""),Type=type,FirstLearn=firstLearn,Slot=false};
            if(Guid.TryParseExact(run.Marker,"N",out _))run.Home=run.Uid+"/"+run.Marker;
            // A newly committed action replaces the previous local presentation.
            // In particular, Learn followed immediately by Activate must show Activate.
            bool replaced=visual.ritual!=null;
            if(replaced)visual.ritual.EndReason="replaced-by-committed-action";
            visual.StopPulse();
            visual.StopAudio();
            WorkbenchEnchantmentVisualTrace.Write("accepted",type,"learn="+firstLearn+" replaced="+replaced+" vfx="+MasteryPlugin.Settings.EnablePerkProcVFX.Value);
            if(MasteryPlugin.Settings.EnablePerkSFX.Value)
            {
                try{visual.audioLease=EnchantmentRitualAudio.TryPlay(station);}catch{}
            }
            if(!MasteryPlugin.Settings.EnablePerkProcVFX.Value||Pulses>=4)
            {WorkbenchEnchantmentVisualTrace.Write("skipped",type,"vfx="+MasteryPlugin.Settings.EnablePerkProcVFX.Value+" active="+Pulses);return;}
            run.Slot=true;
            Pulses++;visual.ritual=run;
            try{run.Routine=visual.StartCoroutine(visual.Pulse(run,type,firstLearn));}
            catch{visual.ReleasePulse(run);throw;}
        }
        void StopPulse()
        {
            var run=ritual;if(run==null)return;ritual=null;ReleaseSlot(run);
            try{if(run.Routine!=null)StopCoroutine(run.Routine);}catch{}
            finally{ReleasePulse(run);}
        }
        void StopAudio()
        {
            var lease=audioLease;audioLease=null;
            try{lease?.Dispose();}catch{} // Audio cleanup cannot suppress VFX cleanup.
        }
        static void ReleaseSlot(Ritual run)
        {
            if(run.Slot){run.Slot=false;Pulses=Math.Max(0,Pulses-1);}
        }
        void ReleasePulse(Ritual run)
        {
            if(run.RootReported||run.EmitCalls>0)WorkbenchEnchantmentVisualTrace.Write("finished",run.Type,"reason="+run.EndReason+" roots="+run.RootReported+" emitCalls="+run.EmitCalls);
            ReleaseSlot(run);bool restore=ritual==null||ReferenceEquals(ritual,run);
            if(ReferenceEquals(ritual,run))ritual=null;
            var owned=run.Root;run.Root=null;try{if(owned!=null)Destroy(owned);}catch{}
            if(!restore)return; // An old disposed coroutine cannot reset a newer cue.
            RestoreSymbol();
        }
        void RestoreSymbol()
        {try{if(symbol!=null){symbol.SetPropertyBlock(null);var sprite=symbol.sprite;if(sprite!=null){float size=Math.Max(sprite.bounds.size.x,sprite.bounds.size.y);if(size>0)symbol.transform.localScale=Vector3.one*(.28f/size);}}}catch{}}
        bool Current(Ritual run)
        {
            if(run==null||!Graphics||!isActiveAndEnabled||!MasteryPlugin.Settings.Enabled.Value||
                run.Session!=ZNet.instance||run.Scene!=ZNetScene.instance||run.Player!=Player.m_localPlayer||run.Player==null||ZNet.instance?.GetWorld()==null||run.World!=ZNet.instance.GetWorldUID())
            {if(run!=null)run.EndReason="context";return false;}
            if(view==null)view=GetComponent<ZNetView>();var data=view?.GetZDO();
            if(view?.IsValid()!=true||!ReferenceEquals(data,run.Data)||data.m_uid!=run.Uid||data.GetPrefab()!="piece_workbench".GetStableHashCode())
            {run.EndReason="station-binding";return false;}
            string markerNow=data.GetString(WorkbenchEnchantmentRuntime.StationKey,"");
            if(run.Marker.Length==0&&Guid.TryParseExact(markerNow,"N",out _)){run.Marker=markerNow;run.Home=run.Uid+"/"+markerNow;}
            bool matches=markerNow==run.Marker;if(!matches)run.EndReason="station-marker";return matches;
        }
        bool Live(Ritual run)=>ReferenceEquals(ritual,run)&&MasteryPlugin.Settings.EnablePerkProcVFX.Value&&Current(run);
        MasterIdolEffectZone Matching(Ritual run,string type)
        {
            var zone=MasterIdolEffectZones.Find(run.Home);
            return zone!=null&&zone.Id==run.Home&&zone.Type==type&&(zone.Home-run.Data.GetPosition()).sqrMagnitude<=.01f?zone:null;
        }
        IEnumerator Pulse(Ritual run,string type,bool firstLearn)
        {
            try
            {
            float cueStart=Time.unscaledTime,nextMaterial=0,duration=WorkbenchEnchantmentThematicBurst.Duration(firstLearn);int local=0;
            SpriteRenderer animated=null;Vector3 scale=Vector3.one;
            while(Live(run))
            {
                float elapsed=Time.unscaledTime-cueStart;if(elapsed<0f||elapsed>=duration)break;
                // Async native material readiness may lag the accepted action. Retry only
                // within this cue; missed emissions and the original deadline stay unchanged.
                if(run.Root==null&&elapsed>=nextMaterial)
                {
                    nextMaterial=elapsed+.1f;
                    run.Root=WorkbenchEnchantmentThematicBurst.Create(transform,type,firstLearn,Palette(type));
                    if(run.Root!=null&&!run.RootReported){run.RootReported=true;WorkbenchEnchantmentVisualTrace.Write("root-created",type,WorkbenchEnchantmentThematicBurst.Describe(run.Root));}
                }
                while(local<WorkbenchEnchantmentThematicBurst.Waves(firstLearn)&&elapsed>=WorkbenchEnchantmentThematicBurst.LocalAt(local,firstLearn))
                {
                    if(WorkbenchEnchantmentThematicBurst.Timely(elapsed,WorkbenchEnchantmentThematicBurst.LocalAt(local,firstLearn),firstLearn))
                    {
                        WorkbenchEnchantmentThematicBurst.Emit(run.Root,type,transform.position+new Vector3(0,1.15f,.18f),Palette(type),WorkbenchEnchantmentThematicBurst.LocalCount,firstLearn,firstLearn?0:WorkbenchEnchantmentThematicBurst.FormationProgress(elapsed));
                        run.EmitCalls++;
                        if(run.Root!=null&&!run.EmitReported){run.EmitReported=true;WorkbenchEnchantmentVisualTrace.Write("first-emission",type,WorkbenchEnchantmentThematicBurst.Describe(run.Root));}
                    }
                    local++;
                }
                // Cover this station's actual build circle without enumerating world objects.
                if(!firstLearn)
                {
                    // Accepted already proved this local action. The symbol animation
                    // does not need a separate network-geometry publication to arrive.
                    ShowSymbol(type);
                    if(symbol!=null&&symbol.enabled&&choice==type)
                    {
                        if(animated!=symbol){animated=symbol;scale=symbol.transform.localScale;}
                        float phase=elapsed/duration;
                        float wave=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);
                        symbol.transform.localScale=scale*(1f+wave*.12f);
                        if(pulseProperties==null)pulseProperties=new MaterialPropertyBlock();
                        pulseProperties.SetColor("_EmissionColor",Palette(type)*(2.4f+wave*1.8f));symbol.SetPropertyBlock(pulseProperties);
                    }
                }
                else if(animated!=null){RestoreSymbol();animated=null;}
                yield return null;
            }
            }
            finally{ReleasePulse(run);}
        }
        internal static void Tick()
        {
            if(!Graphics||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.1f;
            WorkbenchEnchantmentThematicBurst.Prepare();
            int budget=Math.Min(16,Loaded.Count);
            for(int i=0;i<budget;i++){var entry=Loaded.First;var item=entry.Value;Loaded.Remove(entry);Loaded.AddLast(entry);if(item!=null)item.Refresh();}
        }
        void Refresh()
        {
            if(Player.m_localPlayer==null||!MasteryPlugin.Settings.Enabled.Value||(Player.m_localPlayer.transform.position-transform.position).sqrMagnitude>4096){Hide();return;}
            // Keep the same accepted local cue through the ordinary projection tick.
            // Its normal five-second lifetime/identity/config cleanup still applies.
            if(ritual!=null&&!ritual.FirstLearn&&Live(ritual)){ShowSymbol(ritual.Type);return;}
            if(view==null)view=GetComponent<ZNetView>();var data=view?.GetZDO();
            if(view?.IsValid()!=true||data?.GetPrefab()!="piece_workbench".GetStableHashCode()){Hide();return;}
            string current=data.GetString(WorkbenchEnchantmentRuntime.StationKey,"");
            if(!ReferenceEquals(identity,data)||uid!=data.m_uid||marker!=current)
            {identity=data;uid=data.m_uid;marker=current;home=Guid.TryParseExact(marker,"N",out _)?uid+"/"+marker:null;Hide();}
            var zone=MasterIdolEffectZones.Find(home);
            if(zone==null||!WorkbenchEnchantmentRules.Known(zone.Type)){Hide();return;}
            ShowSymbol(zone.Type);
        }
        void ShowSymbol(string type)
        {
            if(!WorkbenchEnchantmentRules.Known(type))return;
            if(choice==type&&symbol!=null&&symbol.enabled)return;
            if(Time.unscaledTime<RetryAsset)return;RetryAsset=Time.unscaledTime+1f;
            var sprite=ApprovedUiAssetBank.Enchantment(type);if(sprite==null){Hide();return;}
            if(symbol==null&&!CreateSymbol())return;
            var material=GlowMaterial(type,sprite);if(material==null){Hide();return;}
            symbol.sprite=sprite;symbol.sharedMaterial=material;symbol.color=Color.white;
            float size=Math.Max(sprite.bounds.size.x,sprite.bounds.size.y);if(size<=0){Hide();return;}
            symbol.transform.localScale=Vector3.one*(.28f/size);symbol.enabled=true;choice=type;
        }
        bool CreateSymbol()
        {
            // Only the inspected native workbench model is supported; unknown models fail closed.
            if(transform.Find("New/high")==null||transform.Find("collider")==null)return false;
            var root=new GameObject("VM_ActiveEnchantmentSymbol");root.layer=gameObject.layer;root.transform.SetParent(transform,false);
            root.transform.localPosition=new Vector3(0f,1.01f,0f);root.transform.localRotation=Quaternion.Euler(-90f,0f,0f);
            symbol=root.AddComponent<SpriteRenderer>();symbol.shadowCastingMode=ShadowCastingMode.Off;symbol.receiveShadows=false;symbol.enabled=false;return true;
        }
        Material GlowMaterial(string type,Sprite sprite)
        {
            if(GlowMaterials.TryGetValue(type,out var cached)&&cached!=null)return cached;
            var native=transform.Find("New/high")?.GetComponent<MeshRenderer>()?.sharedMaterial;
            if(native?.shader?.name!="Custom/Piece"||!native.HasProperty("_EmissionMap")||!native.HasProperty("_EmissionColor")||!native.HasProperty("_MainTex")||!native.HasProperty("_Cutoff"))return null;
            var copy=new Material(native){name="VM_WorkbenchEnchantmentGlow_"+type};
            copy.SetTexture("_MainTex",sprite.texture);copy.SetTexture("_EmissionMap",sprite.texture);
            copy.SetTextureScale("_MainTex",Vector2.one);copy.SetTextureOffset("_MainTex",Vector2.zero);
            copy.SetTextureScale("_EmissionMap",Vector2.one);copy.SetTextureOffset("_EmissionMap",Vector2.zero);
            copy.SetColor("_EmissionColor",Palette(type)*2.4f);copy.SetFloat("_Cutoff",.02f);
            if(copy.HasProperty("_Color"))copy.SetColor("_Color",Color.white);
            if(copy.HasProperty("_Cull"))copy.SetFloat("_Cull",0);
            copy.EnableKeyword("_EMISSION");GlowMaterials[type]=copy;return copy;
        }
        static Color Palette(string id)
        {
            switch(id){case "Meadows":return new Color(1,.82f,.18f);case "BlackForest":return new Color(.18f,.8f,.38f);case "Swamp":return new Color(.64f,1,.12f);case "Mountain":return new Color(.35f,.8f,1);case "Plains":return new Color(1,.25f,.025f);default:return new Color(.72f,.35f,1);}
        }
        internal static void Clear()
        {
            foreach(var item in Loaded)
            {
                if(item==null)continue;item.StopAudio();item.StopPulse();try{item.Hide();}catch{}
                var icon=item.symbol;item.symbol=null;try{if(icon!=null)UnityEngine.Object.Destroy(icon.gameObject);}catch{}
            }
            foreach(var material in GlowMaterials.Values)try{if(material!=null)UnityEngine.Object.Destroy(material);}catch{}
            GlowMaterials.Clear();Next=0;
        }
    }
    [HarmonyPatch(typeof(CraftingStation),nameof(CraftingStation.Start))]
    internal static class WorkbenchEnchantmentVisualAttachPatch
    {static void Postfix(CraftingStation __instance)=>WorkbenchEnchantmentBenchVisual.Attach(__instance);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class WorkbenchEnchantmentVisualTickPatch
    {static void Postfix()=>WorkbenchEnchantmentBenchVisual.Tick();}
    [HarmonyPatch(typeof(ZNetScene),"OnDestroy")]
    internal static class WorkbenchEnchantmentVisualCleanupPatch
    {static void Postfix()=>WorkbenchEnchantmentBenchVisual.Clear();}
}

