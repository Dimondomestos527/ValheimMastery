using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Text;
namespace UnityEngine{
public static class Time{public static float unscaledTime=100;public static int frameCount;}
public struct Vector3{public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 zero=>default;}
public class Transform{public Vector3 position;public Transform Parent;public bool IsChildOf(Transform t){for(var p=Parent;p!=null;p=p.Parent)if(p==t)return true;return false;}}
[Flags]public enum RigidbodyConstraints{None=0,FreezeAll=126}
public class Rigidbody{public Vector3 position,velocity,angularVelocity;public RigidbodyConstraints constraints;}
public class Collider{public bool enabled=true;}
public class Renderer{public bool enabled=true;public Transform transform;}
public class GameObject{
 public Character Actor;public Transform transform=new();public bool activeSelf=true;public Renderer[] Renderers=Array.Empty<Renderer>();public Action<bool> Changed;
 public T GetComponent<T>()where T:class=>typeof(T)==typeof(Character)?Actor as T:typeof(T)==typeof(BaseAI)||typeof(T)==typeof(MonsterAI)?Actor?.Ai as T:null;
 public T[] GetComponentsInChildren<T>(bool inactive)where T:class=>Renderers.OfType<T>().ToArray();
 public void SetActive(bool value){if(activeSelf==value)return;activeSelf=value;if(Actor!=null){if(value){if(!Character.Updaters.Contains(Actor))Character.Updaters.Add(Actor);if(!BaseAI.Updaters.Contains(Actor.Ai))BaseAI.Updaters.Add(Actor.Ai);}else{Character.Updaters.Remove(Actor);BaseAI.Updaters.Remove(Actor.Ai);}}Changed?.Invoke(value);}
}
}
namespace HarmonyLib{[AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public class HarmonyPatch:Attribute{public HarmonyPatch(Type t,string n){}}[AttributeUsage(AttributeTargets.Method)]public class HarmonyPriority:Attribute{public HarmonyPriority(int p){}}public static class Priority{public const int First=800;}}
public struct ZDOID:IEquatable<ZDOID>{public static readonly ZDOID None=new(0,0);public long User;public uint Number;public ZDOID(long u,uint n){User=u;Number=n;}public override string ToString()=>User+":"+Number;public bool Equals(ZDOID o)=>User==o.User&&Number==o.Number;public override bool Equals(object o)=>o is ZDOID z&&Equals(z);public override int GetHashCode()=>HashCode.Combine(User,Number);public static bool operator==(ZDOID a,ZDOID b)=>a.Equals(b);public static bool operator!=(ZDOID a,ZDOID b)=>!a.Equals(b);}
public class ZDO{
 public ZDOID m_uid;public long Owner=11;public ushort OwnerRevision=1;public bool Guard=true;public int Family;public float Health=100;public int Writes;public Dictionary<string,object> Fields=new();
 public long GetLong(string k,long d=0)=>Fields.TryGetValue(k,out var v)?(long)v:d;public int GetInt(string k,int d=0)=>Fields.TryGetValue(k,out var v)?(int)v:d;public string GetString(string k,string d="")=>Fields.TryGetValue(k,out var v)?(string)v:d;public ZDOID GetZDOID(string k)=>Fields.TryGetValue(k,out var v)?(ZDOID)v:ZDOID.None;public float GetFloat(string k,float d=0)=>Health;public bool GetBool(string k,bool d=false)=>Fields.TryGetValue(k,out var v)?(bool)v:d;
 public void Set(string k,long v){Fields[k]=v;Writes++;}public void Set(string k,int v){Fields[k]=v;Writes++;}public void Set(string k,string v){Fields[k]=v;Writes++;}public void Set(string k,bool v){Fields[k]=v;Writes++;}public void SetPosition(UnityEngine.Vector3 v){Writes++;}public long GetOwner()=>Owner;
}
public static class ZDOVars{public const string s_health="health",s_haveTargetHash="haveTarget",s_tamedName="name";}
public class ZDOMan{public static ZDOMan instance;public Dictionary<ZDOID,ZDO> All=new();public ZDO GetZDO(ZDOID id)=>All.TryGetValue(id,out var d)?d:null;}
public class ZPackage{readonly byte[] Bytes;public ZPackage(byte[] b){Bytes=b;}public int Size()=>Bytes.Length;public int GetPos()=>0;public byte[] GetArray()=>Bytes;}
public class ZRpc{public readonly List<(string,ZPackage)> Sent=new();public void Register<T>(string s,Action<ZRpc,T> a){}public void Invoke(string s,params object[] args){Sent.Add((s,(ZPackage)args[0]));}}
public class ZNetPeer{public long m_uid;public ZRpc m_rpc;public bool Ready=true,Confirmed=true;public bool IsReady()=>Ready;}
public class ZNet{public static ZNet instance;public bool Server=true,SettingsConfirmed=true;public long LocalPeer=11,World=7;public object WorldData=new();public List<ZNetPeer> Peers=new();public ZNetPeer ServerPeer;public ZRpc ServerRpc;public static long GetUID()=>instance.LocalPeer;public object GetWorld()=>World==0?null:WorldData;public long GetWorldUID()=>World;public bool IsServer()=>Server;public List<ZNetPeer> GetPeers()=>Peers;public ZNetPeer GetServerPeer()=>ServerPeer;public ZRpc GetServerRPC()=>ServerRpc;}
public class ZNetView{public ZDO Data;public bool IsValid()=>Data!=null;public bool IsOwner()=>Data?.Owner==ZNet.GetUID();public ZDO GetZDO()=>Data;}
public class Character{
 public static readonly List<Character> All=new(),Updaters=new();public static List<Character> GetAllCharacters()=>All;
 public ZNetView m_nview=new();public MonsterAI Ai=new();public UnityEngine.GameObject gameObject,m_visual;public UnityEngine.Transform transform=>gameObject.transform;public UnityEngine.Rigidbody m_body=new();public UnityEngine.Collider Collider=new();public bool Dead,Attack,Dodge,Stagger;
 public Character(){gameObject=new(){Actor=this};m_visual=new();m_visual.Renderers=new[]{new UnityEngine.Renderer{transform=m_visual.transform}};All.Add(this);Updaters.Add(this);BaseAI.All.Add(Ai);BaseAI.Updaters.Add(Ai);Ai.m_nview=m_nview;Ai.m_character=this;Ai.m_animator=new(){m_animator=new object(),m_nview=m_nview};}
 public BaseAI GetBaseAI()=>Ai;public bool IsDead()=>Dead;public bool InAttack()=>Attack;public bool InDodge()=>Dodge;public bool IsStaggering()=>Stagger;public UnityEngine.Collider GetCollider()=>Collider;
}
public class Humanoid:Character{}
public class ZSyncAnimation{public object m_animator;public ZNetView m_nview;}
public class BaseAI{public static readonly List<BaseAI> All=new(),Updaters=new();public static List<BaseAI> GetAllInstances()=>All;public ZNetView m_nview;public Character m_character;public ZSyncAnimation m_animator;public T GetComponent<T>()where T:class=>m_character as T;}
public class MonsterAI:BaseAI{public Character m_targetCreature;public float m_updateTargetTimer;public bool UpdateAI(float dt)=>true;}
public class ZNetScene{public static ZNetScene instance;public Dictionary<ZDOID,UnityEngine.GameObject> Bodies=new();public UnityEngine.GameObject FindInstance(ZDOID id)=>Bodies.TryGetValue(id,out var b)?b:null;}
namespace ValheimMastery{
public static class NetworkSync{public static bool HasServerSettings=>ZNet.instance.SettingsConfirmed;public static bool HasPeerSettings(ZNetPeer p)=>p.Ready&&p.Confirmed;public static bool HasCurrentServerSettings(ZRpc rpc)=>HasServerSettings&&ZNet.instance.ServerPeer?.Ready==true&&rpc==ZNet.instance.ServerRpc;public static void Register(ZNetPeer p){}}
public class Logger{public List<string> Lines=new();public Action<string> During;public void LogInfo(string s){Lines.Add(s);During?.Invoke(s);}}
public static class MasteryPlugin{public static Logger Log=new();public static Config Settings=new();public class Config{public Toggle Enabled=new();}public class Toggle{public bool Value=true;}}
public static class MasterIdolLoadIdentity{public static ulong Generation=1;public static bool LoadReady=true,CapturingGuardNative=true;}
public enum ForestWardCommittedStatus{Unknown,Inactive,Active}
public struct ForestWardCommittedRead{public long JournalRevision;}
public static class WorkbenchEnchantmentRuntime{public static ForestWardCommittedStatus Status=ForestWardCommittedStatus.Active;public static long Revision=50;public static string UnknownHome;public static ForestWardCommittedStatus ReadForestWardCommitted(string home,out ForestWardCommittedRead read){read=new(){JournalRevision=Revision};return home==UnknownHome?ForestWardCommittedStatus.Unknown:Status;}}
public class MasterIdolForestGuardRecord{public string Uid,Nonce,Home,Family,Name;public long Born,Death,Respawn;public bool Valid()=>MasterIdolForestRules.Home(Home)&&MasterIdolForestRules.Nonce(Nonce)&&Born>0&&MasterIdolForestRules.Family(Family)>=0&&MasterIdolNetworkQuota.TryUid(Uid,out _,out _);public MasterIdolForestGuardRecord Copy()=>(MasterIdolForestGuardRecord)MemberwiseClone();}
public static class MasterIdolForestGuardState{
 public const string BornKey="born",NonceKey="nonce";public static bool Ready=true;public static readonly Dictionary<string,MasterIdolForestGuardRecord> Slots=new();
 public static bool ModuleBorn(ZDO d)=>d?.Guard==true;public static int Family(ZDO d)=>d?.Family??-1;public static ZDO Data(string uid)=>MasterIdolNetworkQuota.TryUid(uid,out var u,out var n)?ZDOMan.instance.GetZDO(new(u,n)):null;
 public static bool Matches(MasterIdolForestGuardRecord r,ZDO d)=>r!=null&&d?.Guard==true&&d.m_uid.ToString()==r.Uid&&r.Born==d.GetLong(BornKey)&&r.Nonce==d.GetString(NonceKey)&&r.Family==MasterIdolForestRules.Families[d.Family];
}
public static class MasterIdolNativeTaming{public const string HomeKey="home";public static string Token(string s)=>s?.Contains('/')==true?s[(s.IndexOf('/')+1)..]:null;public static bool SameHome(string a,string b)=>Token(a)!=null&&Token(a)==Token(b);}
public static class MasterIdolNetworkQuota{public static bool TryUid(string s,out long u,out uint n){u=0;n=0;var a=s?.Split(':');return a?.Length==2&&long.TryParse(a[0],out u)&&uint.TryParse(a[1],out n)&&n!=0;}}
public static class MasterIdolForestRules{
 public static string[] Families={"Greyling","Greydwarf","Greydwarf_Elite","Greydwarf_Shaman"};public static bool Nonce(string s)=>s?.Length==32&&Guid.TryParseExact(s,"N",out _);public static bool Home(string s)=>MasterIdolNativeTaming.Token(s)!=null;
 public static int Family(string n)=>Array.IndexOf(Families,n);public static bool Name(string n)=>n!=null&&n.Length<=128;
 public static string ReadText(BinaryReader reader,int max){uint count=0;bool end=false;for(int i=0;i<5;i++){byte next=reader.ReadByte();if(i==4&&(next&248)!=0)throw new InvalidDataException();count|=(uint)(next&127)<<(7*i);if((next&128)==0){end=true;break;}}if(!end||count>max||count>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException();return new UTF8Encoding(false,true).GetString(reader.ReadBytes((int)count));}
}
internal enum ResidentCargoDrainStatus{Pending,Ready,Rejected}internal enum ResidentCargoDrainReason{ControlledBoundary}
public static class MasterIdolResidentCargo{
 public static int Calls;public static bool Settled=true;public static Action<ZDO> During;public static string InventoryBytes="items-with-full-metadata";
 internal static ResidentCargoDrainStatus TryDrainForDormancy(in ForestGuardDrainWitness witness,out ResidentCargoDrainReason reason){Calls++;reason=ResidentCargoDrainReason.ControlledBoundary;During?.Invoke(witness.Body);return Settled&&MasterIdolForestGuardLifecycle.StillCurrentCargoQuiesce(in witness)==ForestGuardDrainIntent.CurrentDormant?ResidentCargoDrainStatus.Ready:ResidentCargoDrainStatus.Pending;}
}
public class MasterIdolEffectZone{}
public static class MasterIdolEffectZones{public static MasterIdolEffectZone Find(string home)=>new();}
public static class MasterIdolRecruitment{public static bool PointReady=true;public static bool TryPosition(MasterIdolEffectZone home,int family,UnityEngine.Vector3? observer,out UnityEngine.Vector3 point,out UnityEngine.Vector3 endpoint){point=new(4,0,4);endpoint=new(8,0,4);return PointReady;}}
public static class MasterIdolResidentBirthPresentation{public static bool Throw=true;public static int Notifications;internal static void NotifyActive(Character actor,MasterIdolForestGuardLifecycle.State row){Notifications++;if(Throw)throw new Exception("unavailable cosmetic");}public static void Stop(Character actor){if(Throw)throw new Exception("cosmetic cleanup unavailable");}}
public static class MasterIdolResidentMetrics{public static void Count(string s){}}
}
