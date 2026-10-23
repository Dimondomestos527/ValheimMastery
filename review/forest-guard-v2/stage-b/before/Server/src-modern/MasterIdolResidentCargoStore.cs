using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal readonly struct ResidentCargoBinding
    {
        internal readonly ZDO Actor, Cargo, Bench;
        internal readonly int Family;
        internal readonly long Principal;
        internal readonly string Home;
        internal ResidentCargoBinding(ZDO actor, ZDO cargo, ZDO bench, int family, long principal, string home)
        { Actor=actor; Cargo=cargo; Bench=bench; Family=family; Principal=principal; Home=home; }
    }
    // A persistent native chest is independent of the mortal NPC body. Only the
    // server's four logical slots may rebind it after a confirmed respawn.
    internal static partial class MasterIdolResidentCargo
    {
        internal const string Prefab="VM_ForestGuardCargo_v1", Link="vm.bf.cargo", WorldKey="vm.bf.cargo.world", FamilyKey="vm.bf.cargo.family",
            BodyKey="vm.bf.cargo.body", NonceKey="vm.bf.cargo.nonce", BornKey="vm.bf.cargo.born", EpochKey="vm.bf.cargo.epoch",
            OpenPeerKey="vm.bf.cargo.openpeer", OpenPlayerKey="vm.bf.cargo.openplayer",OpeningKey="vm.bf.cargo.opening",OpeningNonce="vm.bf.cargo.openingnonce";
        private static readonly Dictionary<int,HashSet<ZDOID>> Stores=new Dictionary<int,HashSet<ZDOID>>();
        private sealed class Retirement{internal ZDOID Actor;internal float Until;}
        private static readonly Dictionary<int,Retirement> Retired=new Dictionary<int,Retirement>();
        private static ZNet Session; private static ZDOMan Manager; private static long World; private static ulong Generation;
        private static bool Complete; private static float Next;
        internal static bool Context()
        {
            var net=ZNet.instance; var man=ZDOMan.instance; if(net?.GetWorld()==null||man==null)return false;
            if(Session!=net||Manager!=man||World!=net.GetWorldUID()||Generation!=MasterIdolLoadIdentity.Generation)
            { Reset(); Session=net; Manager=man; World=net.GetWorldUID(); Generation=MasterIdolLoadIdentity.Generation; }
            return MasterIdolLoadIdentity.LoadReady;
        }
        internal static void Reset()
        { Stores.Clear(); Retired.Clear();Complete=false; Next=0; ResetNetwork(); Session=null; Manager=null; World=0; Generation=0; }
        internal static void Observe(ZDO data)
        {
            // Called by the existing startup enumeration even before LoadReady.
            if(ZNet.instance?.IsServer()!=true||data==null)return;
            Context(); if(data.GetPrefab()!=Prefab.GetStableHashCode()||!data.Persistent||data.GetLong(WorldKey,0)!=World)return;
            int family=data.GetInt(FamilyKey,-1); if(family<0||family>3)return;
            if(!Stores.TryGetValue(family,out var ids))Stores[family]=ids=new HashSet<ZDOID>(); ids.Add(data.m_uid);
        }
        internal static void IndexComplete() { if(ZNet.instance?.IsServer()==true){Context(); Complete=true;} }
        private static ZDO Store(int family)
        {
            if(!Stores.TryGetValue(family,out var ids))return null;
            ids.RemoveWhere(id=>{var data=Manager.GetZDO(id);return data==null||!data.Persistent||data.GetPrefab()!=Prefab.GetStableHashCode()||data.GetInt(FamilyKey,-1)!=family||data.GetLong(WorldKey,0)!=World;});
            if(ids.Count!=1)return null; foreach(var id in ids)return Manager.GetZDO(id); return null;
        }
        internal static bool IsCargo(Container container)=>container!=null&&container.m_nview?.IsValid()==true&&container.m_nview.GetZDO().GetPrefab()==Prefab.GetStableHashCode();
        internal static Container ContainerFor(ZDO data)=>data==null?null:ZNetScene.instance?.FindInstance(data.m_uid)?.GetComponentInChildren<Container>(true);
        internal static Character ActorFor(ZDO data)=>data==null?null:ZNetScene.instance?.FindInstance(data.m_uid)?.GetComponent<Character>();
        internal static bool TryBinding(ZDO actor,out ResidentCargoBinding binding,bool manual=false)
        {
            binding=default;
            if(!Context())return BindingFailure(actor,manual,"binding-context");
            if(!Session.IsServer()||!Complete||!MasterIdolWorldIndex.Ready||!MasterIdolForestGuardState.Ready)return BindingFailure(actor,manual,"binding-index-ready");
            if(!ReferenceEquals(Manager.GetZDO(actor?.m_uid??ZDOID.None),actor)||!MasterIdolForestGuardState.ModuleBorn(actor))return BindingFailure(actor,manual,"binding-current-body");
            int family=MasterIdolForestGuardState.Family(actor); string type=MasterIdolForestRules.Families[family];
            if(Retired.TryGetValue(family,out var retired)&&retired.Actor==actor.m_uid&&Time.unscaledTime<retired.Until)return BindingFailure(actor,manual,"binding-retiring");
            float health=actor.GetFloat(ZDOVars.s_health,1);
            if(!MasterIdolForestGuardState.Slots.TryGetValue(type,out var record)||record.Death!=0||!MasterIdolForestGuardState.Matches(record,actor)||!ResidentCargoRules.Living(health))return BindingFailure(actor,manual,"binding-slot-identity-health");
            string home=actor.GetString(MasterIdolNativeTaming.HomeKey,"");
            if(!MasterIdolForestRules.Home(home)||!MasterIdolNativeTaming.SameHome(record.Home,home))return BindingFailure(actor,manual,"binding-home");
            ZDO bench=null;long principal=0;
            if(!manual)
            {var zone=MasterIdolEffectZones.ResidentHome(actor.m_uid.ToString(),type);if(zone==null||!MasterIdolNativeTaming.SameHome(zone.Id,home)||!WorkbenchEnchantmentRuntime.TryCargoBench(zone.Id,out bench,out principal))return BindingFailure(actor,manual,"binding-current-home-bench");}
            ZDO cargo=Store(family);
            if(cargo==null||actor.GetZDOID(Link)!=cargo.m_uid||cargo.GetZDOID(BodyKey)!=actor.m_uid||cargo.GetString(NonceKey,"")!=record.Nonce||cargo.GetLong(BornKey,0)!=record.Born)return BindingFailure(actor,manual,"binding-cargo-link");
            binding=new ResidentCargoBinding(actor,cargo,bench,family,principal,home);return true;
        }
        private static bool BindingFailure(ZDO actor,bool manual,string reason)
        {if(manual)ResidentCargoOpenTrace.Write(true,reason);else ResidentCargoOpenTrace.WriteAuto(reason,MasterIdolForestGuardState.Family(actor));return false;}
        internal static bool BeforeRemoval(ZDO actor,string reason)
        {
            if(!MasterIdolForestGuardState.ModuleBorn(actor))return true;
            if(!Context()||!Session.IsServer()||!Complete)return RemovalWait(actor,reason,"quiesce-context");
            ZDO cargo=Store(MasterIdolForestGuardState.Family(actor));
            if(cargo==null){bool absent=actor.GetZDOID(Link)==ZDOID.None&&!Stores.ContainsKey(MasterIdolForestGuardState.Family(actor));return absent||RemovalWait(actor,reason,"quiesce-store-unresolved");}
            if(cargo.GetZDOID(BodyKey)!=actor.m_uid||cargo.GetString(NonceKey,"")!=actor.GetString(MasterIdolForestGuardState.NonceKey,"")||cargo.GetLong(BornKey,0)!=actor.GetLong(MasterIdolForestGuardState.BornKey,0))return RemovalWait(actor,reason,"quiesce-store-birth-binding");
            // Native death is never blocked. The independent ZDO survives it;
            // replay may wait for an open GUI to close before destroying bodies.
            if(reason=="Death")return true;
            if(cargo.GetLong(OpenPeerKey,0)!=0)return RemovalWait(actor,reason,"quiesce-open-peer");
            if(cargo.GetInt(ZDOVars.s_inUse,0)!=0)return RemovalWait(actor,reason,"quiesce-native-inuse");
            // An unshown grant still owns a lease. Wait for closed-owner
            // recovery including its saved revision before replay can remove.
            if(ManualLeases.ContainsKey(cargo.m_uid))return RemovalWait(actor,reason,"quiesce-manual-lease");
            if(cargo.GetOwner()!=ZNet.GetUID()){RequestReclaim(actor,cargo);return RemovalWait(actor,reason,"quiesce-owner-reclaim");}
            if(PendingFor(actor.m_uid)){RequestReclaim(actor,cargo);return RemovalWait(actor,reason,"quiesce-pending-reclaim");}
            CancelServer(actor.m_uid);
            var box=ContainerFor(cargo); if(box!=null){if(box.IsInUse())return RemovalWait(actor,reason,"quiesce-loaded-inuse");box.Load();box.Save();}
            if(!cargo.Persistent)return RemovalWait(actor,reason,"quiesce-not-persistent");
            // Root's pre-consume pass renews this 3s lease every .5s. A rejected
            // or expired unconsumed replay cannot retire a living guard forever.
            ManualLeases.Remove(cargo.m_uid);Retired[MasterIdolForestGuardState.Family(actor)]=new Retirement{Actor=actor.m_uid,Until=Time.unscaledTime+3};CancelExecutor(actor.m_uid);return true;
        }
        private static bool RemovalWait(ZDO actor,string reason,string stage)
        {if(reason=="Suspension")ResidentCargoOpenTrace.WriteAuto(stage,MasterIdolForestGuardState.Family(actor));return false;}
        internal static void Bound(Character actor)
        { if(actor!=null&&MasterIdolForestGuardState.ModuleBorn(actor.m_nview?.GetZDO()))(actor.GetComponent<MasterIdolResidentCargoAgent>()??actor.gameObject.AddComponent<MasterIdolResidentCargoAgent>()).Attach(actor); }
        internal static void Tick()
        {
            if(!Context())return; TickNetwork();
            if(!Session.IsServer()||!Complete||!MasterIdolWorldIndex.Ready||!MasterIdolForestGuardState.Ready||Time.unscaledTime<Next)return;
            Next=Time.unscaledTime+.5f;
            foreach(var record in MasterIdolForestGuardState.Slots.Values)
            {
                var actor=MasterIdolForestGuardState.Data(record.Uid); if(record.Death!=0||!MasterIdolForestGuardState.Matches(record,actor))continue;
                int family=MasterIdolForestGuardState.Family(actor);
                if(Retired.TryGetValue(family,out var retired)){if(retired.Actor==actor.m_uid&&Time.unscaledTime<retired.Until)continue;Retired.Remove(family);}
                var body=ActorFor(actor); if(body!=null)Bound(body);
                string home=actor.GetString(MasterIdolNativeTaming.HomeKey,"");
                if(!MasterIdolForestRules.Home(home)||!MasterIdolNativeTaming.SameHome(record.Home,home))continue;
                ZDO cargo=Store(family);
                if(cargo==null)
                {
                    if(Stores.TryGetValue(family,out var existing)&&existing.Count!=0||actor.GetZDOID(Link)!=ZDOID.None)continue;
                    var prefab=ZNetScene.instance.GetPrefab(Prefab);if(prefab==null)continue;
                    var instance=UnityEngine.Object.Instantiate(prefab,actor.GetPosition(),Quaternion.identity);var view=instance.GetComponent<ZNetView>();
                    if(view?.IsValid()!=true||!view.IsOwner()){UnityEngine.Object.Destroy(instance);continue;}
                    cargo=view.GetZDO();cargo.Persistent=true;cargo.Set(WorldKey,World);cargo.Set(FamilyKey,family);Observe(cargo);
                }
                long oldOpen=cargo.GetLong(OpenPeerKey,0);
                if(cargo.GetOwner()==ZNet.GetUID()&&oldOpen!=0&&!PeerReady(Peer(oldOpen))){cargo.Set(OpenPeerKey,0L);cargo.Set(OpenPlayerKey,0L);cargo.Set(ZDOVars.s_inUse,0);}
                if(cargo.GetLong(OpenPeerKey,0)!=0||cargo.GetInt(ZDOVars.s_inUse,0)!=0||PendingFor(actor.m_uid)||ManualLeases.ContainsKey(cargo.m_uid))continue;
                if(actor.GetOwner()==ZNet.GetUID())actor.Set(OpeningKey,0L);
                if(cargo.GetOwner()!=ZNet.GetUID()){SendLink(actor,cargo);continue;}
                // Only this authoritative current slot may replace body identity.
                cargo.Set(BodyKey,actor.m_uid);cargo.Set(NonceKey,record.Nonce);cargo.Set(BornKey,record.Born);
                // Link is an owner-authored actor field; remote owner receives it
                // from the server-ready response rather than server stealing actor.
                if(actor.GetOwner()==ZNet.GetUID())actor.Set(Link,cargo.m_uid);
                else SendLink(actor,cargo);
                cargo.SetPosition(actor.GetPosition());var box=ContainerFor(cargo);if(box!=null)box.transform.position=actor.GetPosition();
            }
        }
        internal static void RegisterPrefab(ZNetScene scene)
        {
            if(scene==null||scene.m_namedPrefabs.ContainsKey(Prefab.GetStableHashCode()))return;
            var native=scene.GetPrefab("piece_chest_wood");if(native==null)return;
            var staging=new GameObject("VM_InactiveForestCargo");staging.SetActive(false);
            var prefab=UnityEngine.Object.Instantiate(native,staging.transform,false);prefab.name=Prefab;
            var box=prefab.GetComponent<Container>();var view=prefab.GetComponent<ZNetView>();
            if(box==null||view==null){UnityEngine.Object.Destroy(staging);return;}
            view.m_persistent=true;box.m_width=5;box.m_height=2;box.m_name="Речі вартового";box.m_privacy=Container.PrivacySetting.Public;box.m_checkGuardStone=false;box.m_autoDestroyEmpty=false;
            var wear=prefab.GetComponent<WearNTear>();if(wear!=null){wear.m_noRoofWear=false;wear.m_noSupportWear=false;wear.m_burnable=false;wear.m_snowDamageImmune=wear.m_ashDamageImmune=true;}
            foreach(var renderer in prefab.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            foreach(var collider in prefab.GetComponentsInChildren<Collider>(true))collider.enabled=false;
            scene.m_namedPrefabs.Add(Prefab.GetStableHashCode(),prefab);scene.m_prefabs.Add(prefab);
        }
    }
    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class ResidentCargoPrefabPatch { [HarmonyPriority(Priority.Last)] private static void Postfix(ZNetScene __instance)=>MasterIdolResidentCargo.RegisterPrefab(__instance); }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class ResidentCargoTickPatch { private static void Postfix()=>MasterIdolResidentCargo.Tick(); }
}
