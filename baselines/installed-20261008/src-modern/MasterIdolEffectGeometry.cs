using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal readonly struct MasterIdolEffectCircle
    {
        internal readonly Vector3 Point;internal readonly float Radius;
        internal MasterIdolEffectCircle(Vector3 point,float radius){Point=point;Radius=radius;}
        internal bool Contains(Vector3 point,float margin=0){var center=Point;center.y=point.y=0;return Vector3.Distance(point,center)<Radius+margin;}
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
        private readonly Dictionary<long,List<MasterIdolEffectZone>> Cells=new Dictionary<long,List<MasterIdolEffectZone>>();
        private static int Cell(float point)=>(int)Math.Floor(point/32f);
        private static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        internal bool Add(MasterIdolEffectZone zone)
        {
            if(zone==null||zone.Id==null||ById.ContainsKey(zone.Id)||zone.Circles==null||zone.Circles.Length==0)return false;
            foreach(string uid in zone.Residents.Keys)if(ResidentHomes.ContainsKey(uid))return false;
            foreach(var circle in zone.Circles)
            {
                int minX=Cell(circle.Point.x-circle.Radius),maxX=Cell(circle.Point.x+circle.Radius),minZ=Cell(circle.Point.z-circle.Radius),maxZ=Cell(circle.Point.z+circle.Radius);
                if((long)(maxX-minX+1)*(maxZ-minZ+1)>4096)return false;
                for(int x=minX;x<=maxX;x++)for(int z=minZ;z<=maxZ;z++)
                {long key=Key(x,z);if(!Cells.TryGetValue(key,out var list)){list=new List<MasterIdolEffectZone>();Cells[key]=list;}if(!list.Contains(zone))list.Add(zone);}
            }
            ById.Add(zone.Id,zone);foreach(string uid in zone.Residents.Keys)ResidentHomes.Add(uid,zone);return true;
        }
        internal MasterIdolEffectZone At(string type,Vector3 point)
        {if(Cells.TryGetValue(Key(Cell(point.x),Cell(point.z)),out var list))foreach(var zone in list)if(zone.Type==type&&zone.Contains(point))return zone;return null;}
        internal bool Allows(long player,Vector3 point)
        {foreach(var ward in Wards)if(ward.Circle.Contains(point)&&!ward.Allows(player))return false;return true;}
    }
}



