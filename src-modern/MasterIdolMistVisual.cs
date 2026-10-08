using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolMistVisual
    {
        private sealed class Field{internal GameObject Object;internal ParticleSystemForceField Force;internal string Key;}
        private readonly struct Candidate{internal readonly string Key;internal readonly MasterIdolEffectCircle Circle;internal readonly float Distance;internal Candidate(string key,MasterIdolEffectCircle circle,float distance){Key=key;Circle=circle;Distance=distance;}}
        private static readonly Dictionary<string,Field> Fields=new Dictionary<string,Field>(StringComparer.Ordinal);
        private static readonly List<Candidate> Candidates=new List<Candidate>();
        private static readonly List<string> Remove=new List<string>();private static readonly HashSet<string> Seen=new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<MasterIdolEffectZone> NearZones=new List<MasterIdolEffectZone>();private static ZNetScene Scene;private static float Next;
        private static void Destroy(Field field){if(field.Object!=null){field.Object.SetActive(false);UnityEngine.Object.Destroy(field.Object);}}
        internal static void Cleanup(){foreach(var field in Fields.Values)Destroy(field);Fields.Clear();Candidates.Clear();Remove.Clear();Seen.Clear();Scene=null;Next=0;}
        internal static void Tick()
        {
            if(!ReferenceEquals(Scene,ZNetScene.instance)){Cleanup();Scene=ZNetScene.instance;}
            if(Application.isBatchMode||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;Candidates.Clear();Seen.Clear();
            var player=Player.m_localPlayer;var snapshot=MasterIdolEffectZones.Snapshot;
            if(player!=null&&Scene!=null&&snapshot!=null){snapshot.Near("Mistlands",player.transform.position,120,NearZones);}
            else NearZones.Clear();foreach(var zone in NearZones)
            {
                if(zone.Type!="Mistlands")continue;for(int i=0;i<zone.Circles.Length;i++)
                {var circle=zone.Circles[i];float distance=Vector3.Distance(player.transform.position,circle.Point)-circle.Radius;if(distance<=120)Candidates.Add(new Candidate(zone.Id+":"+i,circle,distance));}
            }
            Candidates.Sort((a,b)=>a.Distance!=b.Distance?a.Distance.CompareTo(b.Distance):StringComparer.Ordinal.Compare(a.Key,b.Key));
            int count=Math.Min(32,Candidates.Count);for(int i=0;i<count;i++)
            {
                var candidate=Candidates[i];Seen.Add(candidate.Key);
                if(!Fields.TryGetValue(candidate.Key,out var field))
                {
                    var obj=new GameObject("VM_MistlandsNetworkClearance");obj.SetActive(false);var force=obj.AddComponent<ParticleSystemForceField>();force.endRange=candidate.Circle.Radius;
                    var demister=obj.AddComponent<Demister>();demister.m_disableForcefieldDelay=.8f;
                    field=new Field{Object=obj,Force=force,Key=candidate.Key};Fields.Add(candidate.Key,field);
                    obj.transform.position=candidate.Circle.Point;obj.SetActive(true);
                }
                if((field.Object.transform.position-candidate.Circle.Point).sqrMagnitude!=0)field.Object.transform.position=candidate.Circle.Point;if(field.Force.endRange!=candidate.Circle.Radius)field.Force.endRange=candidate.Circle.Radius;
            }
            Remove.Clear();foreach(string key in Fields.Keys)if(!Seen.Contains(key))Remove.Add(key);foreach(string key in Remove){Destroy(Fields[key]);Fields.Remove(key);}
        }
    }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolMistVisualTickPatch
    {private static void Postfix()=>MasterIdolMistVisual.Tick();}
    [HarmonyPatch(typeof(MasteryPlugin),"OnDestroy")]
    internal static class MasterIdolMistVisualCleanupPatch
    {private static void Prefix()=>MasterIdolCleanup.Run("mist-demisters",MasterIdolMistVisual.Cleanup,(stage,error)=>MasteryPlugin.Log?.LogWarning("[MasterIdols] "+stage+": "+error));}
}
