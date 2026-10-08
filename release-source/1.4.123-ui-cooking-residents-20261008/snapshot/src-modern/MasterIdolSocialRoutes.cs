using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolSocialRoutes
    {
        internal readonly struct Interval{internal readonly double Start,End;internal Interval(double start,double end){Start=start;End=end;}}
        internal static bool Inside(MasterIdolEffectZone zone,Vector3 from,Vector3 to,IList<Vector3> corners,List<Interval> intervals=null)
        {
            if(zone==null||zone.Circles.Length>128||corners.Count>128)return false;
            intervals=intervals??new List<Interval>(zone.Circles.Length);
            var previous=from;
            foreach(var corner in corners){if(!Segment(zone,previous,corner,intervals))return false;previous=corner;}
            return Segment(zone,previous,to,intervals);
        }
        private static bool Segment(MasterIdolEffectZone zone,Vector3 from,Vector3 to,List<Interval> ranges)
        {
            if(!zone.Contains(from)||!zone.Contains(to))return false;
            double dx=(double)to.x-from.x,dz=(double)to.z-from.z,a=dx*dx+dz*dz;
            if(a<1e-12)return true;ranges.Clear();
            foreach(var circle in zone.Circles)
            {
                double x=(double)from.x-circle.Point.x,z=(double)from.z-circle.Point.z;
                double b=2*(x*dx+z*dz),c=x*x+z*z-(double)circle.Radius*circle.Radius;
                double discriminant=b*b-4*a*c;if(discriminant<=0)continue;
                double root=Math.Sqrt(discriminant),start=Math.Max(0,(-b-root)/(2*a)),end=Math.Min(1,(-b+root)/(2*a));
                if(end>start)ranges.Add(new Interval(start,end));
            }
            ranges.Sort((left,right)=>left.Start.CompareTo(right.Start));double covered=0;bool first=true;
            foreach(var interval in ranges)
            {
                if(first){if(interval.Start>0)return false;first=false;}
                else if(interval.Start>=covered)return false; // Tangency alone is not strict union coverage.
                covered=Math.Max(covered,interval.End);if(covered>=1)return true;
            }
            return false;
        }
    }
}

