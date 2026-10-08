using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    // Membership is opaque: never interchange these components with Workshop numeric IDs.
    internal static class MasterIdolNetworkCache
    {
        private sealed class Node { internal Vector3 Point;internal float Radius;internal int Group=-1; }
        private static Dictionary<ZDOID,Node> Nodes=new Dictionary<ZDOID,Node>();
        private static MasterIdolSpatialIndex Coverage=new MasterIdolSpatialIndex();
        private static Dictionary<int,string> Keys=new Dictionary<int,string>();
        private static uint Revision;private static bool Ready;
        private static readonly Dictionary<int,MasterIdolEffectCircle[]> PilotCoverage=new Dictionary<int,MasterIdolEffectCircle[]>();private static uint PilotRevision;private static int PilotEpoch=-1;
        internal static int Rebuilds {get;private set;}
        internal static void Reset(){Ready=false;Nodes.Clear();Coverage.Clear();Keys.Clear();PilotCoverage.Clear();}
        private static float Horizontal(Vector3 a,Vector3 b){a.y=b.y=0;return Vector3.Distance(a,b);}
        private static bool Ensure()
        {
            if(!MasterIdolWorldIndex.Ensure()){Reset();return false;}
            if(Ready && Revision==MasterIdolWorldIndex.TopologyRevision)return true;
            try
            {
                var nodes=new Dictionary<ZDOID,Node>();var coverage=new MasterIdolSpatialIndex();
                foreach(var pair in MasterIdolWorldIndex.Stations)
                {
                    if(!ReferenceEquals(ZDOMan.instance.GetZDO(pair.Key),pair.Value))throw new InvalidOperationException("Stale station membership.");
                    float radius=MasterIdolWorldIndex.StationRadius(pair.Value);if(radius<=0)continue;
                    var node=new Node {Point=pair.Value.GetPosition(),Radius=radius};nodes.Add(pair.Key,node);coverage.Set(pair.Key,node.Point,radius);
                }
                var queue=new Queue<ZDOID>();int group=0;
                foreach(var pair in nodes)
                {
                    if(pair.Value.Group>=0)continue;pair.Value.Group=group;queue.Enqueue(pair.Key);
                    while(queue.Count!=0)
                    {
                        var current=nodes[queue.Dequeue()];
                        foreach(var id in coverage.Near(current.Point,current.Radius))
                        {
                            var other=nodes[id];float reach=current.Radius+other.Radius;
                            if(other.Group<0 && Horizontal(current.Point,other.Point)<=reach){other.Group=group;queue.Enqueue(id);}
                        }
                    }
                    group++;
                }
                var keys=new Dictionary<int,string>();foreach(var pair in nodes){string id=pair.Key.ToString();if(!keys.TryGetValue(pair.Value.Group,out var key)||string.CompareOrdinal(id,key)<0)keys[pair.Value.Group]=id;}
                Nodes=nodes;Coverage=coverage;Keys=keys;Revision=MasterIdolWorldIndex.TopologyRevision;Ready=true;Rebuilds++;return true;
            }
            catch {Reset();return false;}
        }
        private static int Group(Vector3 point)
        {foreach(var id in Coverage.At(point)){var node=Nodes[id];if(Horizontal(point,node.Point)<node.Radius)return node.Group;}return -1;}
        internal static bool TryKey(Vector3 point,out string key)
        {key=null;if(!Ensure())return false;int group=Group(point);return group>=0 && Keys.TryGetValue(group,out key);}
        internal static bool Covers(Vector3 idol,Vector3 target)
        {if(!Ensure())return false;int group=Group(idol);return group>=0 && Group(target)==group;}
        internal static bool TryMembers(Vector3 point,out ZDOID[] members)
        {
            members=null;if(!Ensure())return false;int group=Group(point);if(group<0)return false;
            var result=new List<ZDOID>();foreach(var pair in Nodes)if(pair.Value.Group==group)result.Add(pair.Key);members=result.ToArray();return true;
        }
        internal static MasterIdolEffectCircle[] EffectCoverage(Vector3 point)
        {
            if(!Ensure())return Array.Empty<MasterIdolEffectCircle>();int group=Group(point);if(group<0)return Array.Empty<MasterIdolEffectCircle>();
            int epoch=MasterIdolPilotA.Generation;if(epoch!=PilotEpoch||PilotRevision!=Revision){PilotCoverage.Clear();PilotEpoch=epoch;PilotRevision=Revision;}
            if(MasterIdolPilotA.Enabled&&PilotCoverage.TryGetValue(group,out var cached))return cached;
            var result=new List<MasterIdolEffectCircle>();foreach(var node in Nodes.Values)if(node.Group==group)result.Add(new MasterIdolEffectCircle(node.Point,node.Radius));var output=result.ToArray();
            if(MasterIdolPilotA.Enabled)PilotCoverage[group]=output;return output;
        }
    }
}
