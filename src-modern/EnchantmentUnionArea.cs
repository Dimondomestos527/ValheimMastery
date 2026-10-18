using System;using System.Collections.Generic;
namespace ValheimMastery {
 // Exact planar disc-union area; overlapping circles never multiply cue density.
 // Cached per changed projection by the consumer, never evaluated each render frame.
 internal static class EnchantmentUnionArea {
  internal static double Measure(MasterIdolEffectCircle[] circles){
   double total=0,turn=2*Math.PI;var arcs=new List<Tuple<double,double>>();
   for(int i=0;i<circles.Length;i++){
    var c=circles[i];double x=c.Point.x,z=c.Point.z,r=c.Radius;if(r<=0||double.IsNaN(r)||double.IsInfinity(r))return 0;bool contained=false;arcs.Clear();
    for(int j=0;j<circles.Length;j++){
     if(i==j)continue;var other=circles[j];double dx=other.Point.x-x,dz=other.Point.z-z,d=Math.Sqrt(dx*dx+dz*dz),s=other.Radius;
     if(d+r<=s+1e-7){if(d>1e-7||s>r+1e-7||j<i){contained=true;break;}continue;}
     if(d>=r+s||d+s<=r||d<1e-9)continue;
     double theta=Math.Atan2(dz,dx),half=Math.Acos(Math.Max(-1,Math.Min(1,(r*r+d*d-s*s)/(2*r*d))));
     double a=theta-half,b=theta+half;while(a<0){a+=turn;b+=turn;}while(a>=turn){a-=turn;b-=turn;}
     if(b<=turn)arcs.Add(Tuple.Create(a,b));else{arcs.Add(Tuple.Create(a,turn));arcs.Add(Tuple.Create(0d,b-turn));}
    }
    if(contained)continue;arcs.Sort((a,b)=>a.Item1.CompareTo(b.Item1));double cursor=0;
    foreach(var arc in arcs){if(arc.Item1>cursor)total+=Integral(x,z,r,cursor,arc.Item1);cursor=Math.Max(cursor,arc.Item2);}
    if(cursor<turn)total+=Integral(x,z,r,cursor,turn);
   }return Math.Max(0,total);
  }
  static double Integral(double x,double z,double r,double a,double b)=>.5*(r*r*(b-a)+r*x*(Math.Sin(b)-Math.Sin(a))-r*z*(Math.Cos(b)-Math.Cos(a)));
 }
}
