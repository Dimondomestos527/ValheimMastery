using System;
using UnityEngine;
namespace ValheimMastery
{
    internal static class MasterIdolSanctuaryRules
    {
        internal const float MaximumRadius=35f;
        // Centre of the horizontal union envelope, independent of station density.
        // Keep the existing mean native Y datum; this is a sphere, not a cylinder or terrain sample.
        internal static bool TryGeometry(System.Collections.Generic.IReadOnlyList<MasterIdolEffectCircle> circles,out Vector3 center,out float radius)
        {
            center=Vector3.zero;radius=0;if(circles==null||circles.Count==0)return false;
            float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity,y=0;
            foreach(var circle in circles)
            {
                var point=circle.Point;float reach=circle.Radius;
                if(!Finite(point.x)||!Finite(point.y)||!Finite(point.z)||!Finite(reach)||reach<=0)return false;
                minX=Math.Min(minX,point.x-reach);maxX=Math.Max(maxX,point.x+reach);
                minZ=Math.Min(minZ,point.z-reach);maxZ=Math.Max(maxZ,point.z+reach);y+=point.y;
            }
            center=new Vector3((minX+maxX)*.5f,y/circles.Count,(minZ+maxZ)*.5f);
            if(!Finite(center.x)||!Finite(center.y)||!Finite(center.z))return false;
            foreach(var circle in circles)
            {
                float x=circle.Point.x-center.x,z=circle.Point.z-center.z;
                radius=Math.Max(radius,(float)Math.Sqrt((double)x*x+(double)z*z)+circle.Radius);
            }
            radius=Math.Min(MaximumRadius,radius);return Finite(radius)&&radius>0;
        }
        internal static bool Contains(Vector3 center,float radius,Vector3 point)=>(point-center).sqrMagnitude<radius*radius;
        internal static Vector3 Outside(Vector3 center,float radius,Vector3 point)
        {var direction=point-center;if(direction.sqrMagnitude<.0001f)direction=Vector3.forward;return center+direction.normalized*(radius+.15f);}
        // Return first sphere entry, including an already-inside creature. No solid collider.
        internal static bool Entry(Vector3 center,float radius,Vector3 from,Vector3 to,out Vector3 stopped)
        {
            stopped=to;if(Contains(center,radius,from)){stopped=Outside(center,radius,to);return true;}
            var delta=to-from;var offset=from-center;float a=delta.sqrMagnitude;if(a<.000001f)return false;
            float b=Vector3.Dot(offset,delta),c=offset.sqrMagnitude-radius*radius,discriminant=b*b-a*c;
            if(discriminant<0)return false;float t=(-b-(float)Math.Sqrt(discriminant))/a;
            if(t<0||t>1)return false;stopped=Outside(center,radius,from+delta*t);return true;
        }
        internal static float Repair(float current,float maximum,float seconds)
        {if(!Finite(current)||!Finite(maximum)||!Finite(seconds)||current<=0||maximum<=0||seconds<=0||current>=maximum)return current;return Math.Min(maximum,current+maximum*.01f*seconds);}
        internal static double BonusTime(double native,double active)
        {if(double.IsNaN(native)||double.IsInfinity(native)||native<=0||double.IsNaN(active)||double.IsInfinity(active)||active<=0)return native;return native+Math.Min(native,Math.Min(active,5));}
        internal static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
    }
    // Transient observed continuity; an owner/zone/session change cannot grant catch-up bonuses.
    internal sealed class MasterIdolObservedClock
    {
        private ZNet Session;private long World;private string Zone;private float Last;private ZDO Identity;private ushort OwnerRevision;private long Owner;
        internal float Step(string zone,ZDO identity=null,float minimum=0)
        {
            var session=ZNet.instance;long world=session?.GetWorld()!=null?session.GetWorldUID():0;float now=Time.time;
            float elapsed=Session==session&&World==world&&Zone==zone&&zone!=null&&ReferenceEquals(Identity,identity)&&OwnerRevision==(identity?.OwnerRevision??0)&&Owner==(identity?.GetOwner()??0)?Mathf.Clamp(now-Last,0,5):0;
            if(elapsed>0&&elapsed<minimum)return 0;
            Session=session;World=world;Zone=zone;Last=now;Identity=identity;OwnerRevision=identity?.OwnerRevision??0;Owner=identity?.GetOwner()??0;return elapsed;
        }
    }
}


