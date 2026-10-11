using System;
using System.Collections.Generic;
namespace ValheimMastery
{
 internal sealed class MasterIdolObservationRotation<T> where T:class
 {
  private readonly LinkedList<T> Queue=new LinkedList<T>();private readonly Dictionary<T,LinkedListNode<T>> Nodes=new Dictionary<T,LinkedListNode<T>>();
  internal int Count=>Queue.Count;
  internal void Add(T value){if(value==null||Nodes.ContainsKey(value))return;Nodes.Add(value,Queue.AddLast(value));}
  internal void Remove(T value){if(value!=null&&Nodes.TryGetValue(value,out var node)){Queue.Remove(node);Nodes.Remove(value);}}
  internal T Take(){var node=Queue.First;if(node==null)return null;Queue.RemoveFirst();Queue.AddLast(node);return node.Value;}
 }
 internal class MasterIdolObservationReceipt
 {
  internal long Stamp;internal float Until,AcceptedAt,RetainUntil;
  internal bool Accept(long stamp,bool visible,float now)
  {
   if(!float.IsFinite(now)||stamp<=Stamp||visible&&Stamp!=0&&now<AcceptedAt+.25f)return false;
   Stamp=stamp;AcceptedAt=now;Until=visible?now+MasterIdolVisibilityRules.Lease:0;RetainUntil=now+2.5f;return true;
  }
 }
 internal static class MasterIdolVisibilityRules
 {
  internal const float Radius=20f, ReportCadence=.5f, Lease=1.25f;
  internal const int MaxObservers=8, MaxReportsPerFrame=4;
  internal static bool Viewport(float x,float y,float z)=>float.IsFinite(x)&&float.IsFinite(y)&&float.IsFinite(z)&&z>0&&x>=.04f&&x<=.96f&&y>=.04f&&y<=.96f;
  internal static bool Stamp(long stamp,long now,long context)=>stamp>context+TimeSpan.TicksPerSecond&&stamp<=now+TimeSpan.TicksPerSecond&&now-stamp<=TimeSpan.TicksPerSecond;
  internal static bool Fresh(float until,float now)=>float.IsFinite(until)&&float.IsFinite(now)&&until>now;
 }
}
