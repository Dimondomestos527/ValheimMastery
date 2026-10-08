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
        {internal GameObject Object;internal Material Material;internal Vector3 Offset,Scale;}
        private static readonly Dictionary<string,Shell> Shells=new Dictionary<string,Shell>(StringComparer.Ordinal);
        private static readonly HashSet<string> Seen=new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> Remove=new List<string>();
        private static ZNetScene Scene;private static float Next;
        internal static void Cleanup(){foreach(var shell in Shells.Values)Destroy(shell);Shells.Clear();Seen.Clear();Remove.Clear();Scene=null;Next=0;}
        private static void Destroy(Shell shell)
        {if(shell.Object!=null)UnityEngine.Object.Destroy(shell.Object);if(shell.Material!=null)UnityEngine.Object.Destroy(shell.Material);}
        internal static void Tick()
        {
            if(!ReferenceEquals(Scene,ZNetScene.instance)){foreach(var shell in Shells.Values)Destroy(shell);Shells.Clear();Scene=ZNetScene.instance;Next=0;}
            if(Application.isBatchMode||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;Seen.Clear();
            var player=Player.m_localPlayer;var domes=MasterIdolSwamp.Snapshot();
            if(player!=null&&Scene!=null)
            {
                // Asynchronous exact native donor. No prefab instantiation, trigger, controller or light.
                var donor=NativeSoftVisualAssets.Get<GameObject>("Assets/Characters/TraderHaldor/ForceField.prefab");
                var mesh=donor?.GetComponent<MeshFilter>()?.sharedMesh;var renderer=donor?.GetComponent<MeshRenderer>();
                var nativeMaterial=renderer?.sharedMaterial;
                if(mesh!=null&&nativeMaterial!=null&&nativeMaterial.name=="ForceField")foreach(var dome in domes)
                {
                    if((dome.Center-player.transform.position).sqrMagnitude>120*120||Seen.Count>=4)continue;Seen.Add(dome.Id);
                    if(!Shells.TryGetValue(dome.Id,out var shell))
                    {
                        var bounds=mesh.bounds;if(bounds.size.x<=0||bounds.size.y<=0||bounds.size.z<=0)continue;
                        var obj=new GameObject("VM_SwampSanctuaryShell");obj.layer=donor.layer;
                        obj.AddComponent<MeshFilter>().sharedMesh=mesh;var visual=obj.AddComponent<MeshRenderer>();
                        var material=new Material(nativeMaterial);visual.sharedMaterial=material;visual.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;visual.receiveShadows=false;
                        shell=new Shell{Object=obj,Material=material,Offset=bounds.center,Scale=bounds.size};Shells.Add(dome.Id,shell);
                    }
                    var scale=new Vector3(dome.Radius*2/shell.Scale.x,dome.Radius*2/shell.Scale.y,dome.Radius*2/shell.Scale.z);
                    shell.Object.transform.localScale=scale;shell.Object.transform.position=dome.Center-Vector3.Scale(shell.Offset,scale);
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




