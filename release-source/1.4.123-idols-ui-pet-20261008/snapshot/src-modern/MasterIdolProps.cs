using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal sealed class MasterIdolProp:MonoBehaviour
    {
        internal ZNetView View;private float Next;private ZDOID Id;private Vector3 Position;private bool Added;
        private void Awake(){View=GetComponent<ZNetView>();}
        private void Update(){if(Time.unscaledTime<Next)return;Next=Time.unscaledTime+1;if(View?.IsValid()!=true){Remove();return;}var zdo=View.GetZDO();var point=transform.position;if(!Added||!MasterIdolProps.Has(this,Id)||Id!=zdo.m_uid||(point-Position).sqrMagnitude!=0){Remove();Id=zdo.m_uid;Position=point;Added=MasterIdolProps.Add(this,Id,Position);}}
        private void Remove(){if(Added)MasterIdolProps.Remove(this,Id);Added=false;}
        private void OnDisable()=>Remove();private void OnDestroy()=>Remove();
    }
    internal static class MasterIdolProps
    {
        private sealed class Cached {internal ZDO Identity;internal int Children;internal Chair[] Chairs;}
        private static readonly ConditionalWeakTable<ZNetView,Cached> Cache=new ConditionalWeakTable<ZNetView,Cached>();
        private static readonly Dictionary<ZDOID,MasterIdolProp> Loaded=new Dictionary<ZDOID,MasterIdolProp>();
        private static readonly HashSet<ZDOID> NearIds=new HashSet<ZDOID>();private static readonly Dictionary<int,bool> Definitions=new Dictionary<int,bool>();private static readonly MasterIdolSpatialIndex Points=new MasterIdolSpatialIndex();private static ZNetScene Scene;private static ulong Generation;
        private static void Current(){if(ReferenceEquals(Scene,ZNetScene.instance)&&Generation==MasterIdolLoadIdentity.Generation)return;Loaded.Clear();Definitions.Clear();Points.Clear();Scene=ZNetScene.instance;Generation=MasterIdolLoadIdentity.Generation;}
        internal static bool Has(MasterIdolProp prop,ZDOID id){Current();return Loaded.TryGetValue(id,out var found)&&ReferenceEquals(found,prop);}
        internal static bool Add(MasterIdolProp prop,ZDOID id,Vector3 position){Current();if(prop.View?.GetZDO()==null||!ReferenceEquals(ZDOMan.instance?.GetZDO(id),prop.View.GetZDO()))return false;if(Loaded.Count>=4096&&!Loaded.ContainsKey(id))return false;Loaded[id]=prop;Points.Set(id,position,0);return true;}
        internal static void Remove(MasterIdolProp prop,ZDOID id){if(Loaded.TryGetValue(id,out var found)&&ReferenceEquals(found,prop)){Loaded.Remove(id);Points.Remove(id);}}
        internal static void Nearby(Vector3 point,float radius,List<MasterIdolProp> result){Current();result.Clear();Points.FillNear(point,radius,NearIds);foreach(var id in NearIds){if(result.Count>=32)break;if(Loaded.TryGetValue(id,out var prop)&&prop!=null&&prop.View?.IsValid()==true&&(prop.transform.position-point).sqrMagnitude<radius*radius)result.Add(prop);}}
        internal static void Track(ZNetView view){if(view?.IsValid()!=true||view.GetZDO()==null)return;Current();int hash=view.GetZDO().GetPrefab();if(!Definitions.TryGetValue(hash,out bool eligible)){var prefab=Scene?.GetPrefab(hash);eligible=prefab!=null&&(prefab.GetComponent<Fireplace>()!=null||prefab.GetComponent<CraftingStation>()!=null||prefab.GetComponentInChildren<Chair>(true)!=null);if(Definitions.Count<4096)Definitions[hash]=eligible;}if(eligible&&view.GetComponent<MasterIdolProp>()==null)view.gameObject.AddComponent<MasterIdolProp>();}
        internal static Chair[] Chairs(ZNetView furniture)
        {
            if(furniture==null||!furniture.IsValid())return null;var value=Cache.GetValue(furniture,_=>new Cached());var identity=furniture.GetZDO();int children=furniture.transform.childCount;
            if(!ReferenceEquals(identity,value.Identity)||value.Children!=children||value.Chairs==null){value.Identity=identity;value.Children=children;value.Chairs=furniture.GetComponentsInChildren<Chair>(true);MasterIdolPerf.Count("props.chair-cache-rebuild");}
            return value.Chairs;
        }
    }
    [HarmonyPatch(typeof(ZNetView),"Awake")]
    internal static class MasterIdolPropAttachPatch{private static void Postfix(ZNetView __instance)=>MasterIdolProps.Track(__instance);}
}
