using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ValheimMastery
{
    // One server scheduler. Existing arrivals occupy ordinary authoritative roster slots.
    internal static class MasterIdolRecruitment
    {
        private sealed class Vacancy{internal readonly Queue<double> Times=new Queue<double>();internal double Since=>Times.Count>0?Times.Peek():0;internal float Next;}
        private static readonly string[] Families={"Greyling","Greydwarf","Greydwarf_Elite","Greydwarf_Shaman"};
        private static readonly Dictionary<string,Vacancy> Vacancies=new Dictionary<string,Vacancy>(StringComparer.Ordinal);
        private static readonly Dictionary<string,HashSet<string>> PreviousMembers=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string,Dictionary<string,string>> Arrivals=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
        private static ZNet Session;private static long World;private static float Next,NextSave;private static int Cursor;private static MasterIdolRecruitmentStore Store;private static bool Dirty;
        internal static IEnumerable<MasterIdolResidentCandidate> Pending(MasterIdolEffectZone zone)
        {
            if(Session!=ZNet.instance||ZNet.instance?.GetWorld()==null||World!=ZNet.instance.GetWorldUID())yield break;
            if(Arrivals.TryGetValue(zone.Id,out var arrivals))foreach(var member in arrivals.ToArray())
            {
                if(!MasterIdolNetworkQuota.TryUid(member.Key,out long user,out uint id))continue;
                var zdo=ZDOMan.instance?.GetZDO(new ZDOID(user,id));
                if(zdo==null||zdo.GetPrefab()!=member.Value.GetStableHashCode()||zdo.GetFloat(ZDOVars.s_health,1)<=0||zdo.GetBool(ZDOVars.s_tamed,false)&&!MasterIdolNativeTaming.SameHome(zdo.GetString("vm.idol.home.v1",""),zone.Id)){arrivals.Remove(member.Key);continue;}
                yield return new MasterIdolResidentCandidate(member.Key,member.Value,true,zone.Id);
            }
        }
        internal static void Tick(List<MasterIdolEffectZone> zones,ISet<string> assigned,Func<bool> publishable)
        {
            var net=ZNet.instance;if(net?.IsServer()!=true||net.GetWorld()==null||!MasterIdolWorldRegistry.EffectsSettled)return;
            if(Session!=net||World!=net.GetWorldUID()){Session=net;World=net.GetWorldUID();Vacancies.Clear();Arrivals.Clear();PreviousMembers.Clear();Next=NextSave=0;Cursor=0;Store=new MasterIdolRecruitmentStore(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"ValheimMastery","Idols",World+".residents.vmi"),World);foreach(var saved in Store.Load().OrderBy(x=>x.Key,StringComparer.Ordinal)){string group=saved.Key.Substring(0,saved.Key.LastIndexOf('|'));if(!Vacancies.TryGetValue(group,out var entry)){entry=new Vacancy();Vacancies[group]=entry;}entry.Times.Enqueue(saved.Value);}foreach(var saved in Store.Members)PreviousMembers[saved.Key]=new HashSet<string>(saved.Value,StringComparer.Ordinal);Dirty=false;}
            Store.Poll();
            var forest=zones.Where(x=>x.Type=="BlackForest").OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray();
            if(forest.Length>128){return;}
            var active=new HashSet<string>(forest.Select(x=>x.Id),StringComparer.Ordinal);
            foreach(string id in Arrivals.Keys.Where(x=>!active.Contains(x)).ToArray())Arrivals.Remove(id);
            foreach(string stale in PreviousMembers.Keys.Where(x=>!active.Contains(x.Substring(0,x.LastIndexOf('|')))).ToArray()){PreviousMembers.Remove(stale);Dirty=true;}
            foreach(string stale in Vacancies.Keys.Where(x=>!active.Contains(x.Substring(0,x.LastIndexOf('|')))).ToArray()){Vacancies.Remove(stale);Dirty=true;}
                        double now=net.GetTimeSeconds();if(now<=0||double.IsNaN(now)||double.IsInfinity(now))return;
            foreach(var home in forest)foreach(string type in Families)
            {
                string vacancyKey=home.Id+"|"+type;
                                var members=new HashSet<string>(home.Residents.Where(x=>x.Value==type).Select(x=>x.Key),StringComparer.Ordinal);
                if(!Vacancies.TryGetValue(vacancyKey,out var record)){record=new Vacancy();}
                var prior=record.Times.ToArray();
                var slots=PreviousMembers.TryGetValue(vacancyKey,out var oldMembers)
                    ?MasterIdolRecruitmentRules.Reconcile(prior,oldMembers,members,MasterIdolResidentRoster.Limit(type),now)
                    :MasterIdolRecruitmentRules.Slots(prior,MasterIdolResidentRoster.Limit(type)-members.Count,now);
                if(oldMembers==null||!oldMembers.SetEquals(members)){PreviousMembers[vacancyKey]=members;Dirty=true;}
                if(!prior.SequenceEqual(slots)){record.Times.Clear();foreach(double time in slots)record.Times.Enqueue(time);Dirty=true;}
                if(slots.Length==0)Vacancies.Remove(vacancyKey);else Vacancies[vacancyKey]=record;
            }
            if(Dirty&&Time.unscaledTime>=NextSave&&Store.Queue(State(),PreviousMembers)){Dirty=false;NextSave=Time.unscaledTime+60f;}
            if(!Store.Ready||Time.unscaledTime<Next||forest.Length==0)return;Next=Time.unscaledTime+2f;
            // Maximum ONE home/family and THREE terrain/path candidates per scheduler tick.
            int index=Cursor++%(forest.Length*Families.Length);var zone=forest[index/Families.Length];string family=Families[index%Families.Length];
            if(!MasterIdolActivity.Near(zone))return;
            string key=zone.Id+"|"+family;int count=zone.Residents.Count(x=>x.Value==family);
            if(!MasterIdolRecruitmentRules.Needs(family,count)){Vacancies.Remove(key);return;}
            if(!Vacancies.TryGetValue(key,out var vacancy)||vacancy.Times.Count==0)return;
            int stage=MasterIdolRecruitmentRules.Stage(now,vacancy.Since,EnvMan.instance?.m_dayLengthSec??0);
            if(stage==0||Time.unscaledTime<vacancy.Next)return;vacancy.Next=Time.unscaledTime+30f;
            int paths=0;
            foreach(var zdo in MasterIdolWorldIndex.NearbyForestResidents(zone.Home,320f,family).OrderBy(x=>(x.GetPosition()-zone.Home).sqrMagnitude))
            {
                if(assigned.Contains(zdo.m_uid.ToString())||active.Contains(zdo.GetString("vm.idol.home.v1",""))||WorldGenerator.instance?.GetBiome(zdo.GetPosition())!=Heightmap.Biome.BlackForest)continue;
                var instance=ZNetScene.instance?.FindInstance(zdo.m_uid);var creature=instance?.GetComponent<Character>();var ai=instance?.GetComponent<BaseAI>();
                if(creature==null||ai==null||MasterIdolSocialSeats.Battle(ai,creature))continue;
                if(ai is MonsterAI monster&&(zdo.GetBool(ZDOVars.s_eventCreature,monster.m_eventCreature)||zdo.GetBool(ZDOVars.s_despawnInDay,monster.m_despawnInDay)))continue;
                if(paths>=(stage==2?2:3))break;paths++;
                if(!Safe(zdo.GetPosition(),zone,ai.m_pathAgentType,out _))continue;
                if(!publishable())return;Admit(zone,zdo,family,assigned);Trace("lured",zone,family,zdo.m_uid);return;
            }
            if(stage<2)return;
            var prefab=ZNetScene.instance?.GetPrefab(family);var prototype=prefab?.GetComponent<BaseAI>();if(prototype==null)return;
            for(int n=paths;n<3;n++)
            {
                float angle=UnityEngine.Random.value*Mathf.PI*2;var circle=zone.Circles[UnityEngine.Random.Range(0,zone.Circles.Length)];float radius=circle.Radius+UnityEngine.Random.Range(80f,140f);
                var point=circle.Point+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                if(zone.Contains(point,25)||!Safe(point,zone,prototype.m_pathAgentType,out point))continue;
                // Native prefab Awake establishes prefab/type/persistence/rotation/health; never naked CreateNewZDO.
                if(!publishable())return;
                var created=UnityEngine.Object.Instantiate(prefab,point,Quaternion.identity);
                var view=created.GetComponent<ZNetView>();var creature=created.GetComponent<Character>();
                if(view?.IsValid()!=true||!view.IsOwner()||creature==null){UnityEngine.Object.Destroy(created);return;}
                if(created.GetComponent<MonsterAI>() is MonsterAI resident){resident.m_eventCreature=false;resident.m_despawnInDay=false;view.GetZDO().Set(ZDOVars.s_eventCreature,false);view.GetZDO().Set(ZDOVars.s_despawnInDay,false);}
                Admit(zone,view.GetZDO(),family,assigned);view.GetZDO().Set("vm.idol.home.v1",zone.Id);MasterIdolWorldIndex.Observe(view.GetZDO());
                Trace("spawned",zone,family,view.GetZDO().m_uid);return;
            }
        }
        private static bool Safe(Vector3 point,MasterIdolEffectZone zone,Pathfinding.AgentType agent,out Vector3 ground)
        {
            ground=point;if(ZoneSystem.instance==null||Pathfinding.instance==null||!ZoneSystem.instance.IsZoneLoaded(point)||!ZoneSystem.instance.IsZoneLoaded(zone.Inside(point)))return false;
            ZoneSystem.instance.GetGroundData(ref ground,out var normal,out var biome,out var area,out var heightmap);
            if(heightmap==null||normal.y<.7f||ground.y<ZoneSystem.instance.m_waterLevel+.3f||Physics.CheckSphere(ground+Vector3.up, .5f,LayerMask.GetMask("piece","static_solid"),QueryTriggerInteraction.Ignore))return false;
            var from=ground;return MasterIdolTiming.Measure("recruitment.path",()=>Pathfinding.instance.HavePath(from,zone.Inside(from),agent));
        }
        private static void Admit(MasterIdolEffectZone zone,ZDO zdo,string family,ISet<string> assigned)
        {
            string uid=zdo.m_uid.ToString();
            if(!Arrivals.TryGetValue(zone.Id,out var arrivals)){arrivals=new Dictionary<string,string>(StringComparer.Ordinal);Arrivals[zone.Id]=arrivals;}
            arrivals[uid]=family;zone.Residents[uid]=family;assigned.Add(uid);
            if(PreviousMembers.TryGetValue(zone.Id+"|"+family,out var members))members.Add(uid);
            if(Vacancies.TryGetValue(zone.Id+"|"+family,out var vacancy)&&vacancy.Times.Count>0){vacancy.Times.Dequeue();Dirty=true;}
            // Same missing-family batch can fill remaining slots after its first wait, at most one per tick.
        }
                        internal static string Describe()
        {
            double now=ZNet.instance?.GetTimeSeconds()??0;double day=EnvMan.instance?.m_dayLengthSec??0;
            string header="recruitment ready="+(Store?.Ready==true)+" world="+World+" slots="+Vacancies.Values.Sum(x=>x.Times.Count)+" arrivals="+Arrivals.Values.Sum(x=>x.Count)+" daySeconds="+day;
            return header+"\n"+string.Join("\n",State().Take(32).Select(x=>x.Key+" vacantGameSeconds="+Math.Max(0,now-x.Value).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" stage="+MasterIdolRecruitmentRules.Stage(now,x.Value,day)));
        }
        private static Dictionary<string,double> State()
        {
            var result=new Dictionary<string,double>(StringComparer.Ordinal);
            foreach(var entry in Vacancies){int slot=0;foreach(double time in entry.Value.Times)result.Add(entry.Key+"|"+slot++,time);}
            return result;
        }
        private static void Trace(string action,MasterIdolEffectZone zone,string family,ZDOID id)
        {if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogInfo("[IdolTest] recruitment "+action+" family="+family+" npc="+id+" home="+zone.Id);}
    }
}












