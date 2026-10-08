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
        private static readonly Dictionary<int,ZDOID[]> Members=new Dictionary<int,ZDOID[]>();
        private static readonly Dictionary<int,MasterIdolEffectCircle[]> Circles=new Dictionary<int,MasterIdolEffectCircle[]>();
        internal static int Rebuilds {get;private set;}
        internal static void Reset(){Ready=false;Nodes.Clear();Coverage.Clear();Keys.Clear();Members.Clear();Circles.Clear();}
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
                var queue=new Queue<ZDOID>();var neighbours=new HashSet<ZDOID>();int group=0;
                foreach(var pair in nodes)
                {
                    if(pair.Value.Group>=0)continue;pair.Value.Group=group;queue.Enqueue(pair.Key);
                    while(queue.Count!=0)
                    {
                        var current=nodes[queue.Dequeue()];
                        coverage.FillNear(current.Point,current.Radius,neighbours);foreach(var id in neighbours)
                        {
                            var other=nodes[id];float reach=current.Radius+other.Radius;
                            if(other.Group<0 && Horizontal(current.Point,other.Point)<=reach){other.Group=group;queue.Enqueue(id);}
                        }
                    }
                    group++;
                }
                var keys=new Dictionary<int,string>();foreach(var pair in nodes){string id=pair.Key.ToString();if(!keys.TryGetValue(pair.Value.Group,out var key)||string.CompareOrdinal(id,key)<0)keys[pair.Value.Group]=id;}
                Members.Clear();Circles.Clear();var grouped=new Dictionary<int,List<ZDOID>>();
                foreach(var pair in nodes){if(!grouped.TryGetValue(pair.Value.Group,out var list)){list=new List<ZDOID>();grouped.Add(pair.Value.Group,list);}list.Add(pair.Key);}
                foreach(var pair in grouped){pair.Value.Sort((a,b)=>string.CompareOrdinal(a.ToString(),b.ToString()));var ids=pair.Value.ToArray();var circles=new MasterIdolEffectCircle[ids.Length];for(int i=0;i<ids.Length;i++){var node=nodes[ids[i]];circles[i]=new MasterIdolEffectCircle(node.Point,node.Radius);}Members[pair.Key]=ids;Circles[pair.Key]=circles;}
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
            return Members.TryGetValue(group,out members); // Immutable internal snapshots; callers must not mutate.
        }
        internal static MasterIdolEffectCircle[] EffectCoverage(Vector3 point)
        {
            if(!Ensure())return Array.Empty<MasterIdolEffectCircle>();int group=Group(point);if(group<0)return Array.Empty<MasterIdolEffectCircle>();
            return Circles.TryGetValue(group,out var circles)?circles:Array.Empty<MasterIdolEffectCircle>();
        }
    }
}
