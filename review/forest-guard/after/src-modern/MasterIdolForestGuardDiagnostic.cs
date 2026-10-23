using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Task-local observations only. Never calls gameplay Context/Local/Peace/BindBody,
    // never writes native state, never sends RPC. Four families, eight normal
    // pending lanes, a thirty-two event routine critical ring and first/latest
    // unflushed decisive failures each. Routine traffic cannot evict a cause.
    internal static class MasterIdolForestGuardDiagnostic
    {
        internal enum Lane { Request,Apply,Reply,Outcome,Position,Stage,Cue,Wait }
        sealed class Family
        {
            internal float Until;
            internal int Normal,Critical,Suppressed;
            internal readonly string[] Last=new string[16],Pending=new string[16];
            internal readonly float[] RepeatAt=new float[16];
            internal readonly string[] Failures=new string[32];internal int Head,Count,Dropped;
            internal string FirstFailure,LatestFailure;internal int FailureOverwritten;
        }
        static readonly Family[] Families=new Family[4];
        static ZNet Net;static ZDOMan Manager;static long World,Sequence;static ulong Generation;static float Next;
        [ThreadStatic] static MasterIdolForestGuardLifecycle.State? PositionRow;
        internal readonly struct PositionScope:IDisposable
        {
            readonly MasterIdolForestGuardLifecycle.State? Before;
            internal PositionScope(MasterIdolForestGuardLifecycle.State row){Before=PositionRow;PositionRow=row;}
            public void Dispose(){PositionRow=Before;}
        }
        internal static PositionScope Position(MasterIdolForestGuardLifecycle.State row)=>new PositionScope(row);
        internal static void PositionEvent(string stage,string reason,string detail)
        {if(PositionRow.HasValue)Event(PositionRow.Value,Lane.Position,stage+"/"+reason,detail??"",true,reason!="point-ready");}
        internal static bool PositionReject(bool reject,string reason)
        {if(reject)PositionEvent("guard",reason,"");return reject;}
        static bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;long world=net?.GetWorld()!=null?net.GetWorldUID():0;
            if(net!=Net||manager!=Manager||world!=World||Generation!=MasterIdolLoadIdentity.Generation)
            {Array.Clear(Families,0,4);Net=net;Manager=manager;World=world;Generation=MasterIdolLoadIdentity.Generation;Sequence=0;Next=0;}
            return net!=null&&manager!=null&&world!=0&&float.IsFinite(Time.unscaledTime);
        }
        static MasterIdolForestGuardLifecycle.State Read(Character actor)
        {
            var data=actor?.m_nview?.GetZDO();if(data==null)return PositionRow??default;
            return new MasterIdolForestGuardLifecycle.State{Id=data.m_uid,Family=MasterIdolForestGuardState.Family(data),
                Owner=data.GetOwner(),OwnerRevision=data.OwnerRevision,Born=data.GetLong(MasterIdolForestGuardState.BornKey,0),
                Cycle=data.GetLong(MasterIdolForestGuardLifecycle.CycleKey,0),IntentRevision=data.GetLong(MasterIdolForestGuardLifecycle.IntentKey,0),
                Attempt=data.GetString(MasterIdolForestGuardLifecycle.AttemptKey,""),Status=(MasterIdolForestGuardLifecycle.Phase)data.GetInt(MasterIdolForestGuardLifecycle.PhaseKey,0)};
        }
        internal static bool Check(bool value,Character actor,Lane lane,string reason)
        {if(!value)Event(actor,lane,reason,"",true,true);return value;}
        internal static bool Reject(bool value,Character actor,Lane lane,string reason)
        {if(value)Event(actor,lane,reason,"",true,true);return value;}
        internal static bool Check(bool value,MasterIdolForestGuardLifecycle.State row,Lane lane,string reason)
        {if(!value)Event(row,lane,reason,"",true,true);return value;}
        internal static bool Reject(bool value,MasterIdolForestGuardLifecycle.State row,Lane lane,string reason)
        {if(value)Event(row,lane,reason,"",true,true);return value;}
        internal static void Event(Character actor,Lane lane,string reason,string detail="",bool critical=false,bool decisive=false)
        {try{Event(Read(actor),lane,reason,detail,critical,decisive);}catch(Exception){}}
        static string Limit(string text,int count)=>text==null?"":text.Length<=count?text:text.Substring(0,count);
        static string Clock(float value)=>value.ToString("0.000",CultureInfo.InvariantCulture);
        static bool Spend(Family family,bool critical)
        {
            if(critical){if(family.Critical>=16)return false;family.Critical++;}
            else {if(family.Normal>=16)return false;family.Normal++;}
            return true;
        }
        static void Emit(string text,int suppressed)
        {try{MasteryPlugin.Log?.LogInfo("[ForestGuardDiag] "+text+" emitted="+Clock(Time.unscaledTime)+" suppressed="+suppressed);}catch(Exception){}}
        static void Renew(Family family,float now)
        {
            if(now<family.Until)return;
            family.Until=now+10;family.Normal=family.Critical=0;
            // Reserved cause evidence flushes before all routine Request/Reply/
            // Outcome traffic. Keep first plus latest when several causes arrive.
            if(family.FirstFailure!=null)
            {
                Spend(family,true);Emit(family.FirstFailure+" retained=true decisive=true failureOverwritten="+family.FailureOverwritten,family.Suppressed);
                if(family.LatestFailure!=null&&family.LatestFailure!=family.FirstFailure)
                {Spend(family,true);Emit(family.LatestFailure+" retained=true decisive=true failureOverwritten="+family.FailureOverwritten,family.Suppressed);}
                family.FirstFailure=family.LatestFailure=null;family.FailureOverwritten=0;
            }
            while(family.Count>0&&Spend(family,true))
            {
                string retained=family.Failures[family.Head];family.Failures[family.Head]=null;
                family.Head=(family.Head+1)%32;family.Count--;
                Emit(retained+" retained=true criticalDropped="+family.Dropped,family.Suppressed);
            }
            // All retained failures preserve their original sequence/time. An
            // overflow drops the oldest only and explicitly reports that count.
            for(int i=0;i<8;i++)
            {
                var pending=family.Pending[i];if(pending==null||!Spend(family,i>=8))continue;
                family.Pending[i]=null;Emit(pending+" retained=true",family.Suppressed);
            }
            family.Suppressed=0;
        }
        internal static void Event(MasterIdolForestGuardLifecycle.State row,Lane lane,string reason,string detail="",bool critical=false,bool decisive=false)
        {
            try
            {
                if(row.Family<0||row.Family>=4||row.Id==ZDOID.None||!Context())return;
                decisive|=reason?.EndsWith("-exception",StringComparison.Ordinal)==true;
                critical|=decisive;
                float now=Time.unscaledTime;var family=Families[row.Family]??(Families[row.Family]=new Family());Renew(family,now);
                int index=(int)lane+(critical?8:0);if(index<0||index>=16)return;
                string signature=row.Id+"/"+row.Status+"/"+row.Cycle+"/"+row.IntentRevision+"/"+Limit(row.Attempt,32)+"/"+row.Owner+"/"+row.OwnerRevision+"/"+Limit(reason,96)+"/"+Limit(detail,384);
                if(signature==family.Last[index]&&now<family.RepeatAt[index]){family.Suppressed++;return;}
                family.Last[index]=signature;family.RepeatAt[index]=now+5;
                string text="seq="+(++Sequence)+" observed="+Clock(now)+" frame="+Time.frameCount+" role="+(Net.IsServer()?"host-or-server":"peer")+
                    " world="+World+" generation="+Generation+" family="+row.Family+" uid="+row.Id+" owner="+row.Owner+" ownerRev="+row.OwnerRevision+
                    " phase="+row.Status+" cycle="+row.Cycle+" intent="+row.IntentRevision+" attempt="+Limit(row.Attempt,32)+" lane="+lane+
                    " reason="+Limit(reason,96)+" "+Limit(detail,384);
                if(!Spend(family,critical))
                {
                    if(decisive)
                    {
                        if(family.FirstFailure==null)family.FirstFailure=text;
                        else if(family.LatestFailure!=null)family.FailureOverwritten++;
                        family.LatestFailure=text;
                    }
                    else if(critical)
                    {
                        if(family.Count==32){family.Head=(family.Head+1)%32;family.Count--;family.Dropped++;}
                        family.Failures[(family.Head+family.Count)%32]=text;family.Count++;
                    }
                    else family.Pending[index]=text;
                    family.Suppressed++;return;
                }
                // A new emitted event supersedes any older retained event in this lane.
                family.Pending[index]=null;Emit(text,family.Suppressed);family.Suppressed=0;
            }
            catch(Exception){} // Logging failure must never change the actual branch.
        }
        internal static void Tick()
        {
            try{if(!Context()||Time.unscaledTime<Next)return;Next=Time.unscaledTime+.5f;foreach(var family in Families)if(family!=null)Renew(family,Time.unscaledTime);}
            catch(Exception){}
        }
    }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolForestGuardDiagnosticPatch
    {static void Postfix()=>MasterIdolForestGuardDiagnostic.Tick();}
}
