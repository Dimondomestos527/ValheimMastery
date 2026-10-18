using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Only verified rain emitters; native fog, thunder, snow and ash retain their behavior.
    internal static class MasterIdolSwampRain
    {
        private sealed class Rain
        {internal ParticleSystem[] Particles;internal bool[] Expected;}
        private static readonly Dictionary<GameObject,Rain> Roots=new Dictionary<GameObject,Rain>();
        private static ZNetScene Scene;private static EnvMan Environment;private static float Next;private static bool Suppressed;
        private static bool IsRain(GameObject root)
        {string name=Utils.GetPrefabName(root);return name=="Rain"||name=="LightRain"||name=="MistlandsRain"||name=="SlimeRain";}
        internal static void Native(GameObject[] systems)
        {
            if(Application.isBatchMode||systems==null)return;Current();
            foreach(var root in systems)
            {
                if(root==null||!IsRain(root))continue;
                if(!Roots.TryGetValue(root,out var rain)){var particles=root.GetComponentsInChildren<ParticleSystem>(true);rain=new Rain{Particles=particles,Expected=new bool[particles.Length]};Roots.Add(root,rain);}
                for(int i=0;i<rain.Particles.Length;i++)if(rain.Particles[i]!=null)rain.Expected[i]=rain.Particles[i].emission.enabled;
            }
            Suppressed=Player.m_localPlayer!=null&&MasterIdolSwamp.Contains(Player.m_localPlayer.transform.position);Apply();
        }
        internal static void Cleanup(){Restore();Roots.Clear();Scene=null;Environment=null;Suppressed=false;Next=0;}
        private static void Current()
        {
            if(ReferenceEquals(Scene,ZNetScene.instance)&&ReferenceEquals(Environment,EnvMan.instance))return;
            Restore();Roots.Clear();Scene=ZNetScene.instance;Environment=EnvMan.instance;Suppressed=false;Next=0;
        }
        private static void Restore()
        {foreach(var rain in Roots.Values)for(int i=0;i<rain.Particles.Length;i++)if(rain.Particles[i]!=null){var emission=rain.Particles[i].emission;emission.enabled=rain.Expected[i];}}
        private static void Apply()
        {foreach(var rain in Roots.Values)for(int i=0;i<rain.Particles.Length;i++)if(rain.Particles[i]!=null){var emission=rain.Particles[i].emission;emission.enabled=rain.Expected[i]&&!Suppressed;}}
        internal static void Tick()
        {
            Current();if(Application.isBatchMode||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.25f;
            bool suppress=Player.m_localPlayer!=null&&MasterIdolSwamp.Contains(Player.m_localPlayer.transform.position);
            if(suppress==Suppressed)return;Suppressed=suppress;Apply();
        }
    }
    [HarmonyPatch(typeof(EnvMan),"SetParticleArrayEnabled")]
    internal static class MasterIdolSwampRainEmissionPatch
    {private static void Postfix(GameObject[] __0)=>MasterIdolSwampRain.Native(__0);}
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolSwampPresentationPatch
    {private static void Postfix(){MasterIdolSwampRain.Tick();MasterIdolSwampDomeVisual.Tick();}}
    internal static class MasterIdolSwampDomeVisual
    {
        private sealed class Shell
        {internal GameObject Object;internal Material Material;internal Vector3 Offset,Scale,TargetScale,Center;internal float Born;internal bool Opened;internal string Home;}
        private sealed class Activation
        {internal ZNet Session;internal ZNetScene Scene;internal long World;internal ulong Generation;internal float Start,Deadline;}
        private static readonly Dictionary<string,Activation> Activations=new Dictionary<string,Activation>(StringComparer.Ordinal);
        internal readonly struct ReadonlyState
        {
            internal readonly bool Active;internal readonly Vector3 Center;internal readonly float TargetRadius,Opening01,Age;
            internal ReadonlyState(bool active,Vector3 center,float radius,float opening,float age)
            {Active=active;Center=center;TargetRadius=radius;Opening01=opening;Age=age;}
        }
        // Called only for a committed, identity-checked local activation cue. No gameplay writes.
        internal static void BeginActivation(string home,float startedAt,float deadline)
        {
            var net=ZNet.instance;float now=Time.unscaledTime;
            if(Application.isBatchMode||string.IsNullOrEmpty(home)||net?.GetWorld()==null||ZNetScene.instance==null||
                !MasterIdolSanctuaryRules.Finite(startedAt)||!MasterIdolSanctuaryRules.Finite(deadline)||
                startedAt>now||deadline<=now||deadline<=startedAt||deadline-startedAt>10.01f)return;
            CurrentContext();
            Activations[home]=new Activation{Session=net,Scene=ZNetScene.instance,World=net.GetWorldUID(),
                Generation=MasterIdolLoadIdentity.Generation,Start=startedAt,Deadline=deadline};
            if(Shells.TryGetValue(home,out var shell)){shell.Opened=false;Reveal(shell);}
        }
        internal static void CancelActivation(string home)
        {
            if(home==null||!Activations.Remove(home))return;
            if(Shells.TryGetValue(home,out var shell)){shell.Born=Time.time-2f;shell.Opened=false;Reveal(shell);}
        }
        internal static void ClearActivations()
        {Activations.Clear();foreach(var shell in Shells.Values){shell.Born=Time.time-2f;shell.Opened=false;Reveal(shell);}}
        private static bool CurrentActivation(string home,out Activation cue)
        {
            if(home!=null&&Activations.TryGetValue(home,out cue)&&cue.Session==ZNet.instance&&cue.Scene==ZNetScene.instance&&
                cue.Generation==MasterIdolLoadIdentity.Generation&&cue.Session?.GetWorld()!=null&&cue.World==cue.Session.GetWorldUID()&&
                Time.unscaledTime>=cue.Start&&Time.unscaledTime<cue.Deadline)return true;
            cue=null;return false;
        }
        internal static bool TryRead(string home,out ReadonlyState state)
        {
            state=default;if(home==null||!ContextMatches()||MasterIdolEffectZones.Find(home)?.Type!="Swamp"||!Shells.TryGetValue(home,out var shell)||shell.Object==null)return false;
            float opening=shell.TargetScale.x>0?Mathf.Clamp01(shell.Object.transform.localScale.x/shell.TargetScale.x):0;
            float age=CurrentActivation(home,out var cue)?Time.unscaledTime-cue.Start:Time.time-shell.Born;
            state=new ReadonlyState(shell.Object.activeInHierarchy&&opening>0,shell.Center,shell.TargetScale.x*shell.Scale.x*.5f,opening,age);return true;
        }
        private static readonly Dictionary<string,Shell> Shells=new Dictionary<string,Shell>(StringComparer.Ordinal);
        private static readonly HashSet<string> Seen=new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> Remove=new List<string>();
        private static ZNetScene Scene;private static ZNet Session;private static long World;private static ulong Generation;private static float Next;
        private static bool ContextMatches()=>Scene==ZNetScene.instance&&Session==ZNet.instance&&
            Generation==MasterIdolLoadIdentity.Generation&&World==(ZNet.instance?.GetWorld()!=null?ZNet.instance.GetWorldUID():0);
        private static void CurrentContext()
        {
            if(ContextMatches())return;
            Cleanup();Scene=ZNetScene.instance;Session=ZNet.instance;Generation=MasterIdolLoadIdentity.Generation;
            World=Session?.GetWorld()!=null?Session.GetWorldUID():0;
        }
        internal static void Cleanup(){foreach(var shell in Shells.Values)Destroy(shell);Shells.Clear();Activations.Clear();Seen.Clear();Remove.Clear();Scene=null;Session=null;World=0;Generation=0;Next=0;}
        private static void Destroy(Shell shell)
        {if(shell.Object!=null)UnityEngine.Object.Destroy(shell.Object);if(shell.Material!=null)UnityEngine.Object.Destroy(shell.Material);}
        internal static float Opening(float age)=>Mathf.SmoothStep(.02f,1f,Mathf.Clamp01(age/2f));
        private static void Reveal(Shell shell)
        {
            if(shell.Object==null)return;
            bool cueActive=CurrentActivation(shell.Home,out var cue);
            float age=cueActive?Time.unscaledTime-cue.Start-3f:Time.time-shell.Born;
            float opening=cueActive&&age<0?0:Opening(age);var scale=shell.TargetScale*opening;
            shell.Object.transform.localScale=scale;
            shell.Object.transform.position=shell.Center-Vector3.Scale(shell.Offset,scale);
            shell.Opened=age>=2f;
        }
        internal static void Tick()
        {
            CurrentContext();
            if(Application.isBatchMode)return;
            Remove.Clear();foreach(var pair in Activations)if(!CurrentActivation(pair.Key,out _))Remove.Add(pair.Key);
            foreach(string key in Remove)CancelActivation(key);
            // Animate only the existing bounded shells; donor/zone work stays .5s.
            foreach(var shell in Shells.Values)if(!shell.Opened)Reveal(shell);
            if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;Seen.Clear();
            var player=Player.m_localPlayer;var domes=MasterIdolSwamp.Snapshot();
            if(player!=null&&Scene!=null)
            {
                // Asynchronous exact native donor. No prefab instantiation, trigger, controller or light.
                var donor=NativeSoftVisualAssets.Get<GameObject>("Assets/GameElements/Pieces/_res/guardstone/fx_guardstone_activate.prefab");
                var renderer=donor?.transform.Find("Dome")?.GetComponent<ParticleSystemRenderer>();
                var mesh=renderer?.renderMode==ParticleSystemRenderMode.Mesh?renderer.mesh:null;
                var nativeMaterial=renderer?.sharedMaterial;
                if(mesh!=null&&nativeMaterial!=null&&nativeMaterial.name=="shield"&&nativeMaterial.shader?.name=="Particles/Standard Unlit2"&&nativeMaterial.HasProperty("_Color"))foreach(var dome in domes)
                {
                    if((dome.Center-player.transform.position).sqrMagnitude>120*120||Seen.Count>=4)continue;Seen.Add(dome.Id);
                    if(!Shells.TryGetValue(dome.Id,out var shell))
                    {
                        var bounds=mesh.bounds;if(bounds.size.x<=0||bounds.size.y<=0||bounds.size.z<=0)continue;
                        var obj=new GameObject("VM_SwampSanctuaryShell");obj.SetActive(false);obj.layer=donor.layer;
                        obj.AddComponent<MeshFilter>().sharedMesh=mesh;var visual=obj.AddComponent<MeshRenderer>();
                        var material=new Material(nativeMaterial);material.SetColor("_Color",new Color(.48f,.62f,.58f,.40f));visual.sharedMaterial=material;visual.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;visual.receiveShadows=false;
                        shell=new Shell{Object=obj,Material=material,Offset=bounds.center,Scale=bounds.size,Born=Time.time,Home=dome.Id};Shells.Add(dome.Id,shell);
                    }
                    var scale=new Vector3(dome.Radius*2/shell.Scale.x,dome.Radius*2/shell.Scale.y,dome.Radius*2/shell.Scale.z);
                    if((shell.TargetScale-scale).sqrMagnitude!=0||(shell.Center-dome.Center).sqrMagnitude!=0)
                    {shell.Born=Time.time;shell.Opened=false;shell.TargetScale=scale;shell.Center=dome.Center;}
                    Reveal(shell);
                    if(!shell.Object.activeSelf)shell.Object.SetActive(true);
                }
            }
            Remove.Clear();foreach(var key in Shells.Keys)if(!Seen.Contains(key))Remove.Add(key);foreach(var key in Remove){Destroy(Shells[key]);Shells.Remove(key);}
        }
    }    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class MasterIdolSwampPresentationCleanupPatch
    {
        private static void Prefix()
        {
            MasterIdolCleanup.Run("swamp-rain",MasterIdolSwampRain.Cleanup,(stage,error)=>MasteryPlugin.Log?.LogWarning("[MasterIdols] "+stage+": "+error));
            MasterIdolCleanup.Run("swamp-dome",MasterIdolSwampDomeVisual.Cleanup,(stage,error)=>MasteryPlugin.Log?.LogWarning("[MasterIdols] "+stage+": "+error));
        }
    }}




