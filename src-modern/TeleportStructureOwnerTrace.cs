using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Read-only, finite evidence. Never changes support, ownership, damage or RPCs.
    internal static class TeleportStructureOwnerTrace
    {
        internal const int MaxTerminals=8,MaxDepth=8,RingSize=16,MaxTeleports=4;
        internal const float MaxAge=90f,LogCooldown=1f;
        internal enum Stage { Wear,Support,Direct,Rpc,Apply,Destroy,SupportResult,Exit }
        internal struct Sample
        {
            internal ZDOID Id;internal Vector3 Position;
            internal float At,Age,Support,Min,Max,Health,Maximum,Delta;
            internal long Owner,Creator;internal uint Revision;internal ushort OwnerRevision;
            internal int Frame,Marker;
            internal bool NoSupportWear;
        }
        internal sealed class Frame
        {
            internal WearNTear Subject;internal ZNetView View;internal ZDO Data;
            internal Sample Before;internal Stage Kind;internal float Damage;internal int HitType;
            internal bool Wear,Support,Direct,Rpc,Apply,DeleteReturned,ZoneLoaded,AreaReady;
            internal string Prefab;
        }
        internal struct Scope {internal bool Entered;internal int Slot,Epoch;internal Frame Frame;}
        struct Record {internal Sample Sample;internal Stage Stage;internal int Result,HitType;internal float Damage;}
        static readonly Record[] Ring=new Record[RingSize];
        static int cursor,count,terminals,teleports,epoch,thread;
        static float lastTerminal=float.NegativeInfinity;
        static ZNet session;static long world;
        static Player teleportPlayer;static bool teleportPending;
        [ThreadStatic] static Frame[] frames;
        [ThreadStatic] static int depth;

        static bool Current()
        {
            var net=ZNet.instance;if(net==null||net.GetWorld()==null)return false;
            long id=net.GetWorldUID();if(id==0)return false;
            if(!ReferenceEquals(session,net)||world!=id)
            {
                session=net;world=id;epoch++;thread=System.Threading.Thread.CurrentThread.ManagedThreadId;
                terminals=teleports=cursor=count=depth=0;lastTerminal=float.NegativeInfinity;
                teleportPlayer=null;teleportPending=false;
                Array.Clear(Ring,0,Ring.Length);if(frames!=null)Array.Clear(frames,0,frames.Length);
            }
            return thread==System.Threading.Thread.CurrentThread.ManagedThreadId;
        }
        static bool Candidate(WearNTear wear)
        {
            if(!Current()||terminals>=MaxTerminals||wear==null||wear.m_nview==null||
                !wear.m_nview.IsValid()||!wear.m_nview.IsOwner())return false;
            var piece=wear.m_piece;if(piece==null)piece=wear.GetComponent<Piece>();
            float age=Time.time-wear.m_createTime;
            return piece!=null&&piece.GetCreator()==0&&!float.IsNaN(age)&&!float.IsInfinity(age)&&age>=0f&&age<=MaxAge;
        }
        static Sample Read(WearNTear wear,ZDO data)
        {
            var piece=wear.m_piece;if(piece==null)piece=wear.GetComponent<Piece>();
            return new Sample{Id=data.m_uid,Position=wear.transform.position,At=Time.realtimeSinceStartup,
                Age=Time.time-wear.m_createTime,Support=wear.m_support,Min=wear.GetMinSupport(),Max=wear.GetMaxSupport(),
                Health=data.GetFloat(ZDOVars.s_health,wear.m_health),Maximum=wear.m_health,
                Owner=data.GetOwner(),Creator=piece==null?-1:piece.GetCreator(),Revision=data.DataRevision,
                OwnerRevision=data.OwnerRevision,Frame=Time.frameCount,Delta=Time.unscaledDeltaTime,
                Marker=data.GetInt(Crafting70Service.StabilityKey,0),NoSupportWear=wear.m_noSupportWear};
        }
        static void Add(Stage stage,Sample sample,int result=-1,float damage=0f,int hitType=-1)
        {Ring[cursor]=new Record{Stage=stage,Sample=sample,Result=result,Damage=damage,HitType=hitType};cursor=(cursor+1)%RingSize;count=Math.Min(RingSize,count+1);}
        internal static Scope Begin(WearNTear wear,Stage stage,float damage=0f,HitData hit=null)
        {
            int prior=depth,priorEpoch=epoch;
            try
            {
                if(!Candidate(wear)||depth>=MaxDepth)return default;
                prior=depth;priorEpoch=epoch;var data=wear.m_nview.GetZDO();if(data==null)return default;
                var sample=Read(wear,data);
                if(frames==null)frames=new Frame[MaxDepth];var frame=frames[depth]??(frames[depth]=new Frame());
                frame.Subject=wear;frame.View=wear.m_nview;frame.Data=data;frame.Before=sample;frame.Kind=stage;
                frame.Damage=damage;frame.HitType=hit==null?-1:(int)hit.m_hitType;
                frame.Wear=stage==Stage.Wear;frame.Support=stage==Stage.Support;frame.Direct=stage==Stage.Direct;
                frame.Rpc=stage==Stage.Rpc;frame.Apply=stage==Stage.Apply;frame.DeleteReturned=false;
                frame.ZoneLoaded=frame.AreaReady=false;frame.Prefab=null;
                for(int i=0;i<depth;i++)if(ReferenceEquals(frames[i]?.Subject,wear))
                {
                    var parent=frames[i];parent.Wear|=frame.Wear;parent.Support|=frame.Support;
                    parent.Direct|=frame.Direct;parent.Rpc|=frame.Rpc;parent.Apply|=frame.Apply;
                    frame.Wear|=parent.Wear;frame.Support|=parent.Support;frame.Direct|=parent.Direct;
                    frame.Rpc|=parent.Rpc;frame.Apply|=parent.Apply;
                }
                if(stage==Stage.Destroy)
                {
                    frame.Prefab=Utils.GetPrefabName(wear.gameObject);
                    frame.ZoneLoaded=ZoneSystem.instance!=null&&ZoneSystem.instance.IsZoneLoaded(sample.Position);
                    frame.AreaReady=ZNetScene.instance!=null&&ZNetScene.instance.IsAreaReady(sample.Position);
                }
                Add(stage,sample,-1,damage,frame.HitType);depth=prior+1;
                return new Scope{Entered=true,Slot=prior,Epoch=epoch,Frame=frame};
            }
            catch {if(priorEpoch==epoch)depth=prior;return default;}
        }
        internal static void SupportResult(WearNTear wear,bool result)
        {
            try
            {
                if(depth==0||!Candidate(wear))return;
                for(int i=0;i<depth;i++)if(ReferenceEquals(frames[i]?.Subject,wear))frames[i].Support=true;
                Add(Stage.SupportResult,Read(wear,wear.m_nview.GetZDO()),result?1:0);
            }
            catch { }
        }
        internal static void DeleteReturned(ZDO data)
        {
            try {for(int i=0;i<depth;i++)if(ReferenceEquals(frames[i]?.Data,data))frames[i].DeleteReturned=true;}
            catch { }
        }
        internal static Exception End(Scope scope,Exception error)
        {
            if(!scope.Entered)return error;
            try
            {
                if(scope.Epoch!=epoch)return error;
                var frame=scope.Frame;
                if(frame.Kind==Stage.Destroy&&error==null&&Current()&&scope.Epoch==epoch&&
                    !ReferenceEquals(frame.View,null)&&frame.View.GetZDO()==null&&
                    terminals<MaxTerminals&&Time.realtimeSinceStartup-lastTerminal>=LogCooldown)
                {
                    terminals++;lastTerminal=Time.realtimeSinceStartup;
                    Flush(frame);
                }
                else if(error==null&&!ReferenceEquals(frame.View,null)&&ReferenceEquals(frame.View.GetZDO(),frame.Data))
                    Add(Stage.Exit,Read(frame.Subject,frame.Data));
            }
            catch { }
            finally
            {
                if(scope.Epoch==epoch)
                {
                    depth=Math.Min(depth,scope.Slot);
                    scope.Frame.Subject=null;scope.Frame.View=null;scope.Frame.Data=null;
                }
            }
            return error; // Preserve the exact original exception object.
        }
        static string Number(float value)=>value.ToString("R",CultureInfo.InvariantCulture);
        static void Snapshot(StringBuilder text,Sample s)
        {
            text.Append("{t=").Append(Number(s.At)).Append(" frame=").Append(s.Frame).Append(" dt=").Append(Number(s.Delta))
                .Append(" age=").Append(Number(s.Age)).Append(" owner=").Append(s.Owner).Append(" rev=").Append(s.Revision)
                .Append('/').Append(s.OwnerRevision).Append(" creator=").Append(s.Creator).Append(" marker=").Append(s.Marker)
                .Append(" hp=").Append(Number(s.Health)).Append('/').Append(Number(s.Maximum)).Append(" support=")
                .Append(Number(s.Support)).Append('/').Append(Number(s.Min)).Append('/').Append(Number(s.Max))
                .Append(" noSupportWear=").Append(s.NoSupportWear).Append('}');
        }
        static void Flush(Frame frame)
        {
            var before=frame.Before;var zone=ZoneSystem.GetZone(before.Position);var text=new StringBuilder(4096);
            text.Append("[WorldStructureTrace] utc=").Append(DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture))
                .Append(" monoTicks=").Append(Stopwatch.GetTimestamp()).Append(" side=").Append(session.IsServer()?"server":"client")
                .Append(" world=").Append(world).Append(" n=").Append(terminals).Append(" uid=").Append(before.Id)
                .Append(" prefab=").Append(frame.Prefab).Append(" zone=").Append(zone.x).Append(',').Append(zone.y)
                .Append(" loaded=").Append(frame.ZoneLoaded).Append(" areaReady=").Append(frame.AreaReady)
                .Append(" ownerAtEntry=true localSceneRemoval=true deleteCallReturned=").Append(frame.DeleteReturned)
                .Append(" chain=wear:").Append(frame.Wear).Append(" support:").Append(frame.Support)
                .Append(" direct:").Append(frame.Direct).Append(" rpc:").Append(frame.Rpc).Append(" apply:").Append(frame.Apply)
                .Append(" destroyHitType=").Append(frame.HitType).Append(" pre=");Snapshot(text,before);
            text.Append(" history=[");
            for(int i=0;i<count;i++)
            {
                var record=Ring[(cursor-count+i+RingSize)%RingSize];if(record.Sample.Id!=before.Id)continue;
                text.Append(record.Stage).Append(':').Append(record.Result).Append(" damage=").Append(Number(record.Damage))
                    .Append(" hitType=").Append(record.HitType);Snapshot(text,record.Sample);text.Append(';');
            }
            text.Append(']');MasteryPlugin.Log?.LogInfo(text.ToString());
        }
        internal static bool TeleportBefore(Player player)
        {try{return player!=null&&player==Player.m_localPlayer&&player.IsTeleporting();}catch{return false;}}
        internal static void TeleportStart(Player player,bool accepted)
        {
            try
            {
                if(!accepted||player==null||player!=Player.m_localPlayer||!Current())return;
                if(teleports>=MaxTeleports){teleportPending=false;teleportPlayer=null;return;}
                teleports++;teleportPending=true;teleportPlayer=player;TeleportLine(player,"start",teleports);
            }
            catch { }
        }
        internal static void TeleportEnd(Player player,bool wasTeleporting)
        {
            try
            {
                if(!wasTeleporting||player==null||player!=Player.m_localPlayer||player.IsTeleporting()||!Current()||
                    !teleportPending||!ReferenceEquals(teleportPlayer,player))return;
                teleportPending=false;teleportPlayer=null;TeleportLine(player,"end",teleports);
            }
            catch { }
        }
        static void TeleportLine(Player player,string phase,int run)
        {
            var point=player.transform.position;var target=player.m_teleportTargetPos;
            MasteryPlugin.Log?.LogInfo("[WorldStructureTeleport] utc="+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)+
                " monoTicks="+Stopwatch.GetTimestamp()+" world="+world+" run="+run+" phase="+phase+
                " frame="+Time.frameCount+" dt="+Number(Time.unscaledDeltaTime)+" pos="+Number(point.x)+","+Number(point.y)+","+Number(point.z)+
                " target="+Number(target.x)+","+Number(target.y)+","+Number(target.z));
        }
    }
    [HarmonyPatch(typeof(WearNTear),"UpdateWear",new[]{typeof(float)})]
    internal static class WorldStructureWearTracePatch
    {
        static void Prefix(WearNTear __instance,out TeleportStructureOwnerTrace.Scope __state)=>__state=TeleportStructureOwnerTrace.Begin(__instance,TeleportStructureOwnerTrace.Stage.Wear);
        static Exception Finalizer(Exception __exception,TeleportStructureOwnerTrace.Scope __state)=>TeleportStructureOwnerTrace.End(__state,__exception);
    }
    [HarmonyPatch(typeof(WearNTear),"UpdateSupport",new Type[0])]
    internal static class WorldStructureSupportTracePatch
    {
        static void Prefix(WearNTear __instance,out TeleportStructureOwnerTrace.Scope __state)=>__state=TeleportStructureOwnerTrace.Begin(__instance,TeleportStructureOwnerTrace.Stage.Support);
        static Exception Finalizer(Exception __exception,TeleportStructureOwnerTrace.Scope __state)=>TeleportStructureOwnerTrace.End(__state,__exception);
    }
    [HarmonyPatch(typeof(WearNTear),"HaveSupport",new Type[0])]
    internal static class WorldStructureHaveSupportTracePatch
    {[HarmonyPriority(Priority.Last)]static void Postfix(WearNTear __instance,bool __result)=>TeleportStructureOwnerTrace.SupportResult(__instance,__result);}
    [HarmonyPatch(typeof(WearNTear),"Damage",new[]{typeof(HitData)})]
    internal static class WorldStructureDirectTracePatch
    {
        static void Prefix(WearNTear __instance,HitData __0,out TeleportStructureOwnerTrace.Scope __state)=>__state=TeleportStructureOwnerTrace.Begin(__instance,TeleportStructureOwnerTrace.Stage.Direct,0,__0);
        static Exception Finalizer(Exception __exception,TeleportStructureOwnerTrace.Scope __state)=>TeleportStructureOwnerTrace.End(__state,__exception);
    }
    [HarmonyPatch(typeof(WearNTear),"RPC_Damage",new[]{typeof(long),typeof(HitData)})]
    internal static class WorldStructureDamageRpcTracePatch
    {
        static void Prefix(WearNTear __instance,HitData __1,out TeleportStructureOwnerTrace.Scope __state)=>__state=TeleportStructureOwnerTrace.Begin(__instance,TeleportStructureOwnerTrace.Stage.Rpc,0,__1);
        static Exception Finalizer(Exception __exception,TeleportStructureOwnerTrace.Scope __state)=>TeleportStructureOwnerTrace.End(__state,__exception);
    }
    [HarmonyPatch(typeof(WearNTear),"ApplyDamage",new[]{typeof(float),typeof(HitData)})]
    internal static class WorldStructureApplyTracePatch
    {
        static void Prefix(WearNTear __instance,float __0,HitData __1,out TeleportStructureOwnerTrace.Scope __state)=>__state=TeleportStructureOwnerTrace.Begin(__instance,TeleportStructureOwnerTrace.Stage.Apply,__0,__1);
        static Exception Finalizer(Exception __exception,TeleportStructureOwnerTrace.Scope __state)=>TeleportStructureOwnerTrace.End(__state,__exception);
    }
    [HarmonyPatch(typeof(WearNTear),"Destroy",new[]{typeof(HitData),typeof(bool)})]
    internal static class WorldStructureDestroyTracePatch
    {
        static void Prefix(WearNTear __instance,HitData __0,out TeleportStructureOwnerTrace.Scope __state)=>__state=TeleportStructureOwnerTrace.Begin(__instance,TeleportStructureOwnerTrace.Stage.Destroy,0,__0);
        static Exception Finalizer(Exception __exception,TeleportStructureOwnerTrace.Scope __state)=>TeleportStructureOwnerTrace.End(__state,__exception);
    }
    [HarmonyPatch(typeof(ZDOMan),"DestroyZDO",new[]{typeof(ZDO)})]
    internal static class WorldStructureDeleteTracePatch
    {static void Postfix(ZDO __0)=>TeleportStructureOwnerTrace.DeleteReturned(__0);}
    [HarmonyPatch(typeof(Player),"TeleportTo",new[]{typeof(Vector3),typeof(Quaternion),typeof(bool)})]
    internal static class WorldStructureTeleportStartPatch
    {static void Postfix(Player __instance,bool __result)=>TeleportStructureOwnerTrace.TeleportStart(__instance,__result);}
    [HarmonyPatch(typeof(Player),"UpdateTeleport",new[]{typeof(float)})]
    internal static class WorldStructureTeleportEndPatch
    {
        static void Prefix(Player __instance,out bool __state)=>__state=TeleportStructureOwnerTrace.TeleportBefore(__instance);
        static void Postfix(Player __instance,bool __state)=>TeleportStructureOwnerTrace.TeleportEnd(__instance,__state);
    }
}
