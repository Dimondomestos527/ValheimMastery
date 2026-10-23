using System;
using System.Linq;
using System.Collections;
using System.Reflection;
using ValheimMastery;
using UnityEngine;
using L=ValheimMastery.MasterIdolForestGuardLifecycle;
using Ctl=ValheimMastery.MasterIdolForestGuardLifecycleController;
using O=ValheimMastery.MasterIdolForestGuardLifecycleOwner;

partial class Cases {
 static void Main(string[] args){
  if(args.Length>0&&args[0]=="legacy-guard-repro"){StaleDispatch();return;}
  StaleDispatch();InactiveAck();OldSlots();
  Console.WriteLine("RESULT targetedProofs=3 failed=0; actual V2 helpers, controlled native/load/Cargo/callback boundaries; LIVE UNRUN");
 }
 static (Character[] actors,ZDO[] bodies) Four(){
  Setup();MasteryPlugin.Log=new();
  var actors=new Character[4];var bodies=new ZDO[4];actors[0]=Actor;bodies[0]=Data;
  for(int family=1;family<4;family++){
   var d=new ZDO{m_uid=new(9,(uint)(family+1)),Owner=11,Family=family};
   d.Fields["born"]=10L+family;d.Fields["nonce"]=new string((char)('b'+family),32);d.Fields["home"]=Home;d.Fields["name"]="guard-"+family;
   ZDOMan.instance.All[d.m_uid]=d;var a=new Character();a.m_nview.Data=d;ZNetScene.instance.Bodies[d.m_uid]=a.gameObject;
   MasterIdolForestGuardState.Slots[MasterIdolForestRules.Families[family]]=new(){Uid=d.m_uid.ToString(),Family=MasterIdolForestRules.Families[family],Born=10+family,Nonce=d.GetString("nonce"),Home=Home,Name=d.GetString("name")};
   actors[family]=a;bodies[family]=d;
  }
  return(actors,bodies);
 }
 static void StaleDispatch(){
  var (actors,bodies)=Four();Step();Step();int writes=bodies.Sum(d=>d.Writes);bool cargo=false,noException=true;
  MasterIdolResidentCargo.During=d=>{MasterIdolResidentCargo.During=null;cargo=true;Ctl.Reset();};
  try{Switch(false,1101);}catch(Exception e){Console.WriteLine("OBSERVED Cargo callback: "+e.GetType().Name);noException=false;}
  bool cargoSafe=cargo&&noException&&actors.All(a=>a.gameObject.activeSelf)&&writes==bodies.Sum(d=>d.Writes);

  (actors,bodies)=Four();Step();Step();Switch(false,1102);Step();bool native=false;noException=true;
  actors[0].gameObject.Changed=on=>{if(on){actors[0].gameObject.Changed=null;native=true;Ctl.Reset();}};
  try{Switch(true,1103);}catch(Exception e){Console.WriteLine("OBSERVED native callback: "+e.GetType().Name);noException=false;}
  bool nativeSafe=native&&noException&&actors.Skip(1).All(a=>!a.gameObject.activeSelf)&&bodies.Skip(1).All(d=>d.GetInt(L.PhaseKey)==3);

  (actors,bodies)=Four();Step();Step();writes=bodies.Sum(d=>d.Writes);bool diagnostic=false;noException=true;
  MasteryPlugin.Log.During=line=>{if(line.Contains("reason=request-before")){MasteryPlugin.Log.During=null;diagnostic=true;Ctl.Reset();}};
  try{Switch(false,1104);}catch(Exception e){Console.WriteLine("OBSERVED diagnostic callback: "+e.GetType().Name);noException=false;}
  bool diagnosticSafe=diagnostic&&noException&&actors.All(a=>a.gameObject.activeSelf)&&writes==bodies.Sum(d=>d.Writes);
  C(cargoSafe&&nativeSafe&&diagnosticSafe,"P1: Cargo, native owner application and diagnostic reentry invalidate captured dispatch; no stale sibling request/flag write or exception");
 }
 static void InactiveAck(){
  var (actors,bodies)=Four();Step();Step();bool during=false;
  actors[0].gameObject.Changed=on=>{if(!on) during=ReferenceEquals(ZNetScene.instance.FindInstance(bodies[0].m_uid),actors[0].gameObject)&&actors[0].m_nview.IsValid()&&actors[0].m_nview.IsOwner();};
  Switch(false,1201);Step();
  var running=(IDictionary)typeof(O).GetField("Running",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  var pending=running[bodies[0].m_uid];
  bool current=(bool)typeof(O).GetMethod("CurrentApplication",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{pending,(object)bodies[0]});
  bool applied=bodies.All(d=>L.TryReadAcceptedIntent(d,out var row,out _)&&row.Applied&&row.Status==L.Phase.Suspended&&O.Native(row,d));
  C(during&&current&&applied&&actors.All(a=>!a.gameObject.activeSelf)&&bodies.All(d=>!L.CanOperate(d)),"P2: SetActive(false) preserves FindInstance/view ownership and actual CurrentApplication -> Stamp -> Report -> controller Applied path for all four inactive LOADED bodies (isolated host ACK)");
 }
 static void OldSlots(){
  var (actors,bodies)=Four();
  ZNetScene.instance.Bodies.Clear();Character.All.Clear();Character.Updaters.Clear();BaseAI.All.Clear();BaseAI.Updaters.Clear();ZDOMan.instance.All.Clear();
  var records=MasterIdolForestGuardState.Slots.Values.ToArray();
  string[] ids=records.Select(r=>r.Uid).ToArray();
  Step();Step();
  bool missing=records.All(r=>MasterIdolForestGuardLoadIdentity.Resolve(r)==null&&MasterIdolForestGuardLoadIdentity.Diagnostic(r)=="direct-zdo-missing/no-native-load-witness");
  for(int family=0;family<4;family++){
   var d=bodies[family];if(family==0)d.Guard=false;else if(family==1)d.Family=2;else if(family==2)d.Fields["nonce"]=new string('f',32);else d.Fields["born"]=999L;
   ZDOMan.instance.All[d.m_uid]=d;
  }
  int writes=bodies.Sum(d=>d.Writes);Step();Step();
  string[] reasons=records.Select(r=>MasterIdolForestGuardLoadIdentity.Diagnostic(r)).ToArray();
  Console.WriteLine("RECOVERY BLOCKED: "+string.Join("; ",reasons));
  bool blocked=records.All(r=>r.Death==0&&MasterIdolForestGuardLoadIdentity.Resolve(r)==null)&&records.Select(r=>r.Uid).SequenceEqual(ids)&&MasterIdolForestGuardState.Slots.Count==4;
  bool noOutput=Character.All.Count==0&&ZNetScene.instance.Bodies.Count==0&&MasterIdolResidentCargo.Calls==0&&bodies.All(d=>!L.CanOperate(d))&&writes==bodies.Sum(d=>d.Writes);
  C(missing&&blocked&&noOutput&&reasons.All(r=>r.Contains("no-native-load-witness")),"P3: old four ALIVE slots / zero residents, missing then nonmodule/family/nonce/born mismatches stay RECOVERY BLOCKED; no slot deletion, UID repair, native mutation, owner dispatch or duplicate body");
 }
}
