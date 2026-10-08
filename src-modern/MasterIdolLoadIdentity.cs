using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // Historical receipt Output is immutable. Native reload assigns new session IDs; only persisted native-load evidence may alias them.
    internal static class MasterIdolLoadIdentity
    {
        private sealed class LoadedProof{internal ZDO Object;internal string Token,Type;internal long Creator;internal Vector3 Position;}
        private static readonly Dictionary<ZDOID,LoadedProof> Loaded=new Dictionary<ZDOID,LoadedProof>();
        private static readonly Dictionary<string,ZDO> Current=new Dictionary<string,ZDO>(StringComparer.Ordinal);
        private static readonly HashSet<string> Duplicates=new HashSet<string>(StringComparer.Ordinal);
        private static ZNet Session;private static ZDOMan Manager;private static long World;private static uint Revision=uint.MaxValue;private static bool Overflow;
        internal static ulong Generation {get;private set;}
        private static int WorldLoadDepth,ChunkLoadDepth;private static ZDOMan LoadingManager;private static bool IncompleteLoad;
        internal static bool LoadReady=>WorldLoadDepth==0&&ChunkLoadDepth==0&&!IncompleteLoad&&!ZNet.m_loadError;
        internal static void BeginWorldLoad(){if(WorldLoadDepth++==0){IncompleteLoad=false;Reset();}}
        internal static void EndWorldLoad(Exception error){if(error!=null||ZNet.m_loadError)IncompleteLoad=true;WorldLoadDepth=Math.Max(0,WorldLoadDepth-1);}
        internal static void BeginChunks(ZDOMan manager){if(ChunkLoadDepth++==0){LoadingManager=manager;if(WorldLoadDepth==0)IncompleteLoad=false;}}
        internal static void EndChunks(Exception error){if(error!=null)IncompleteLoad=true;if((ChunkLoadDepth=Math.Max(0,ChunkLoadDepth-1))==0)LoadingManager=null;}
        internal static void ChunkFilename(ChunkSaveMapping mapping,string filename)
        {if(ChunkLoadDepth>0&&ReferenceEquals(mapping,LoadingManager?.m_chunkSaveMapping)&&string.IsNullOrEmpty(filename))IncompleteLoad=true;}
        internal static void Reset(){Generation++;Loaded.Clear();Current.Clear();Duplicates.Clear();Session=null;Manager=null;World=0;Revision=uint.MaxValue;Overflow=false;}
        private static bool Context()
        {
            var net=ZNet.instance;var manager=ZDOMan.instance;
            if(net?.IsServer()!=true||net.GetWorld()==null||manager==null)return false;
            if(!ReferenceEquals(Session,net)||!ReferenceEquals(Manager,manager)||World!=net.GetWorldUID())
            {Reset();Session=net;Manager=manager;World=net.GetWorldUID();}
            return true;
        }
        internal static void LoadedNative(ZDO zdo)
        {
            if(zdo==null)return;var profile=MasterIdolWorldRegistry.Profile(zdo.GetPrefab());if(profile==null||!Context())return;
            string token=zdo.GetString(MasterIdolWorldRegistry.TokenKey,"");if(!Guid.TryParseExact(token,"N",out _))return;
            if(Loaded.Count>=4096&&!Loaded.ContainsKey(zdo.m_uid)){Overflow=true;return;}
            Loaded[zdo.m_uid]=new LoadedProof{Object=zdo,Token=token,Type=profile.Id,Creator=zdo.GetLong(ZDOVars.s_creator,0),Position=zdo.GetPosition()};
            Revision=uint.MaxValue;
        }
        private static bool Ready()
        {
            if(!Context()||!LoadReady||Overflow||!MasterIdolWorldIndex.Ensure())return false;
            if(Revision==MasterIdolWorldIndex.Revision)return true;
            Current.Clear();Duplicates.Clear();
            foreach(var zdo in MasterIdolWorldIndex.Idols.Values)
            {
                if(!ReferenceEquals(Manager.GetZDO(zdo.m_uid),zdo))continue;
                string token=zdo.GetString(MasterIdolWorldRegistry.TokenKey,"");if(!Guid.TryParseExact(token,"N",out _))continue;
                if(Current.ContainsKey(token))Duplicates.Add(token);else Current.Add(token,zdo);
            }
            Revision=MasterIdolWorldIndex.Revision;return true;
        }
        internal static bool Matches(MasterIdolAdmission entry,ZDO zdo)
        {
            if(entry?.Applied!=true||zdo==null||!Ready())return false;
            bool unique=!Duplicates.Contains(entry.Token)&&Current.TryGetValue(entry.Token,out var candidate)&&ReferenceEquals(candidate,zdo);
            string type=MasterIdolWorldRegistry.Profile(zdo.GetPrefab())?.Id,token=zdo.GetString(MasterIdolWorldRegistry.TokenKey,"");
            long creator=zdo.GetLong(ZDOVars.s_creator,0);float distance=Vector3.Distance(zdo.GetPosition(),new Vector3(entry.X,entry.Y,entry.Z));
            bool loaded=Loaded.TryGetValue(zdo.m_uid,out var proof)&&ReferenceEquals(proof.Object,zdo)&&proof.Token==token&&proof.Type==type&&proof.Creator==creator&&Vector3.Distance(proof.Position,zdo.GetPosition())<=.1f;
            return MasterIdolLoadIdentityRules.Match(ReferenceEquals(Manager.GetZDO(zdo.m_uid),zdo),unique,loaded,entry.Output==zdo.m_uid.ToString(),token,entry.Token,type,entry.Type,creator,entry.Player,distance);
        }
        internal static ZDO Resolve(MasterIdolAdmission entry)
        {return entry!=null&&Ready()&&!Duplicates.Contains(entry.Token)&&Current.TryGetValue(entry.Token,out var zdo)&&Matches(entry,zdo)?zdo:null;}
        // Financial destroyed evidence additionally requires the existing durable authoritative Removed flag.
        internal static bool TokenAbsent(string token)=>Ready()&&!Current.ContainsKey(token)&&!Duplicates.Contains(token);
        internal static string Output(ZDO zdo)
        {var entry=MasterIdolWorldRegistry.Store?.Find(zdo?.GetString(MasterIdolWorldRegistry.TokenKey,""));return Matches(entry,zdo)?entry.Output:zdo?.m_uid.ToString();}
    }
    [HarmonyPatch(typeof(ZDO),"Load")]
    internal static class MasterIdolNativeLoadedPatch
    {private static void Postfix(ZDO __instance)=>MasterIdolLoadIdentity.LoadedNative(__instance);}
    [HarmonyPatch]
    internal static class MasterIdolNativeIdentityResetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods(){foreach(var m in AccessTools.GetDeclaredMethods(typeof(ZDOMan)))if(m.Name=="ResetBeforeLoad"||m.Name=="ShutDown")yield return m;}
        private static void Prefix()=>MasterIdolLoadIdentity.Reset();
    }
    [HarmonyPatch(typeof(ZNet),"ServerLoadWorld")]
    internal static class MasterIdolOuterLoadIdentityPatch
    {private static void Prefix()=>MasterIdolLoadIdentity.BeginWorldLoad();private static Exception Finalizer(Exception __exception){MasterIdolLoadIdentity.EndWorldLoad(__exception);return __exception;}}
    [HarmonyPatch(typeof(ZDOMan),"LoadChunks")]
    internal static class MasterIdolChunkLoadIdentityPatch
    {private static void Prefix(ZDOMan __instance)=>MasterIdolLoadIdentity.BeginChunks(__instance);private static Exception Finalizer(Exception __exception){MasterIdolLoadIdentity.EndChunks(__exception);return __exception;}}
    [HarmonyPatch(typeof(ChunkSaveMapping),"GetChunkFilename")]
    internal static class MasterIdolChunkFilenameIdentityPatch
    {private static void Postfix(ChunkSaveMapping __instance,string __result)=>MasterIdolLoadIdentity.ChunkFilename(__instance,__result);}
}
