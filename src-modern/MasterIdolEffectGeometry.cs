using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal readonly struct MasterIdolEffectCircle
    {
        internal readonly Vector3 Point;internal readonly float Radius;
        internal MasterIdolEffectCircle(Vector3 point,float radius){Point=point;Radius=radius;}
        internal bool Contains(Vector3 point,float margin=0){float reach=Radius+margin,x=point.x-Point.x,z=point.z-Point.z;return reach>0&&x*x+z*z<reach*reach;}
        internal Vector3 Inside(Vector3 point,float margin=0)
        {var delta=point-Point;delta.y=0;float distance=delta.magnitude;return distance<(Radius+margin)*.9f?point:Point+delta.normalized*((Radius+margin)*.9f);}
    }
    internal sealed class MasterIdolEffectZone
    {
        internal string Id,Type;internal Vector3 Home;
        internal Dictionary<string,string> Residents=new Dictionary<string,string>(StringComparer.Ordinal);internal MasterIdolEffectCircle[] Circles;
        internal bool Contains(Vector3 point,float margin=0){foreach(var circle in Circles)if(circle.Contains(point,margin))return true;return false;}
        internal Vector3 Inside(Vector3 point,float margin=0)
        {Vector3 nearest=Home;float best=float.MaxValue;foreach(var circle in Circles){var candidate=circle.Inside(point,margin);float distance=(candidate-point).sqrMagnitude;if(distance<best){best=distance;nearest=candidate;}}return nearest;}
    }
    internal sealed class MasterIdolEffectWard
    {
        internal MasterIdolEffectCircle Circle;internal long Creator;internal long[] Permitted;
        internal bool Allows(long player){if(player==Creator)return true;foreach(long id in Permitted)if(id==player)return true;return false;}
    }
    internal sealed class MasterIdolEffectGeometry
    {
        internal readonly Dictionary<string,MasterIdolEffectZone> ById=new Dictionary<string,MasterIdolEffectZone>(StringComparer.Ordinal);
        internal readonly Dictionary<string,MasterIdolEffectZone> ResidentHomes=new Dictionary<string,MasterIdolEffectZone>(StringComparer.Ordinal);
        internal readonly List<MasterIdolEffectWard> Wards=new List<MasterIdolEffectWard>();
        private readonly struct Coverage {internal readonly MasterIdolEffectZone Zone;internal readonly MasterIdolEffectCircle Circle;internal Coverage(MasterIdolEffectZone zone,MasterIdolEffectCircle circle){Zone=zone;Circle=circle;}}
        private readonly Dictionary<long,List<Coverage>> Cells=new Dictionary<long,List<Coverage>>();
        private readonly Dictionary<long,List<MasterIdolEffectWard>> WardCells=new Dictionary<long,List<MasterIdolEffectWard>>();private int WardCount=-1,WardEntries;
        private readonly List<MasterIdolEffectWard> WardOverflow=new List<MasterIdolEffectWard>();
        private readonly List<Coverage> Overflow=new List<Coverage>();private int IndexedEntries;private const int MaxIndexEntries=262144;
        private readonly HashSet<string> NearSeen=new HashSet<string>(StringComparer.Ordinal);
        private static int Cell(float point)=>(int)Math.Floor(point/32f);
        private static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        internal bool Add(MasterIdolEffectZone zone)
        {
            if(zone==null||zone.Id==null||ById.ContainsKey(zone.Id)||zone.Circles==null||zone.Circles.Length==0)return false;
            foreach(string uid in zone.Residents.Keys)if(ResidentHomes.ContainsKey(uid))return false;
            foreach(var circle in zone.Circles)
            {
                int minX=Cell(circle.Point.x-circle.Radius),maxX=Cell(circle.Point.x+circle.Radius),minZ=Cell(circle.Point.z-circle.Radius),maxZ=Cell(circle.Point.z+circle.Radius);
                long entries=(long)(maxX-minX+1)*(maxZ-minZ+1);if(entries>4096)return false;if(IndexedEntries+entries>MaxIndexEntries){Overflow.Add(new Coverage(zone,circle));continue;}IndexedEntries+=(int)entries;
                for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++)
                {long key=Key(x,z);if(!Cells.TryGetValue(key,out var list)){list=new List<Coverage>();Cells[key]=list;}list.Add(new Coverage(zone,circle));}
            }
            ById.Add(zone.Id,zone);foreach(string uid in zone.Residents.Keys)ResidentHomes.Add(uid,zone);return true;
        }
        internal MasterIdolEffectZone At(string type,Vector3 point)
        {if(Cells.TryGetValue(Key(Cell(point.x),Cell(point.z)),out var list))foreach(var entry in list)if(entry.Zone.Type==type&&entry.Circle.Contains(point))return entry.Zone;foreach(var entry in Overflow)if(entry.Zone.Type==type&&entry.Circle.Contains(point))return entry.Zone;return null;}
        internal bool Allows(long player,Vector3 point)
        {EnsureWards();if(WardCells.TryGetValue(Key(Cell(point.x),Cell(point.z)),out var list))foreach(var ward in list)if(ward.Circle.Contains(point)&&!ward.Allows(player))return false;foreach(var ward in WardOverflow)if(ward.Circle.Contains(point)&&!ward.Allows(player))return false;return true;}
        private void EnsureWards(){if(WardCount==Wards.Count)return;WardCells.Clear();WardOverflow.Clear();WardEntries=0;foreach(var ward in Wards){var circle=ward.Circle;long entries=(long)(Cell(circle.Point.x+circle.Radius)-Cell(circle.Point.x-circle.Radius)+1)*(Cell(circle.Point.z+circle.Radius)-Cell(circle.Point.z-circle.Radius)+1);if(entries>4096||WardEntries+entries>MaxIndexEntries){WardOverflow.Add(ward);continue;}WardEntries+=(int)entries;for(int x=Cell(circle.Point.x-circle.Radius);x<=Cell(circle.Point.x+circle.Radius);x++)for(int z=Cell(circle.Point.z-circle.Radius);z<=Cell(circle.Point.z+circle.Radius);z++){long key=Key(x,z);if(!WardCells.TryGetValue(key,out var list)){list=new List<MasterIdolEffectWard>();WardCells[key]=list;}list.Add(ward);}}WardCount=Wards.Count;}
        internal void Near(string type,Vector3 point,float radius,List<MasterIdolEffectZone> result)
        {result.Clear();NearSeen.Clear();for(int x=Cell(point.x-radius);x<=Cell(point.x+radius);x++)for(int z=Cell(point.z-radius);z<=Cell(point.z+radius);z++)if(Cells.TryGetValue(Key(x,z),out var list))foreach(var entry in list)if(entry.Zone.Type==type&&NearSeen.Add(entry.Zone.Id))result.Add(entry.Zone);foreach(var entry in Overflow)if(entry.Zone.Type==type&&NearSeen.Add(entry.Zone.Id))result.Add(entry.Zone);}
    }
}



