using System;
using System.IO;
using UnityEngine;
namespace ValheimMastery
{
    // Server-local independent character authority. World switches change ONLY
    // transient context; no native world/profile save, C100 file or clock touched.
    internal static class WorkbenchEnchantmentCooldownProtocol
    {
        static string directory;
        static ZNet session;static ZDOMan manager;static long world;static ulong generation,epoch,baselineEpoch;
        static WorkbenchEnchantmentCharacterLedger ledger;
        internal static IWorkbenchEnchantmentCooldownAuthority Authority=>Current()&&ledger?.Available==true?ledger:null;
        internal static long UtcNowTicks=>Authority!=null?ledger.UtcNowTicks:0;
        internal static string Failure=>ledger?.Failure??"authority-unavailable";
        internal static void Initialize(string storageDirectory)
        {directory=Path.GetFullPath(storageDirectory);ledger=null;ResetSession();}
        internal static void ResetSession(){epoch=checked(epoch+1);baselineEpoch=0;session=null;manager=null;world=0;generation=0;}
        internal static void Shutdown(){ResetSession();ledger=null;directory=null;}
        static bool Current()=>baselineEpoch==epoch&&MasteryPlugin.Settings?.Enabled?.Value==true&&session!=null&&ReferenceEquals(session,ZNet.instance)&&session.IsServer()&&ReferenceEquals(manager,ZDOMan.instance)&&world!=0&&session.GetWorldUID()==world&&generation==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady;
        internal static void Tick()
        {
            var net=ZNet.instance;var man=ZDOMan.instance;
            if(directory==null||net?.IsServer()!=true||net.GetWorld()==null||man==null||!MasterIdolLoadIdentity.LoadReady||MasteryPlugin.Settings?.Enabled?.Value!=true){ResetSession();return;}
            if(session!=net||manager!=man||world!=net.GetWorldUID()||generation!=MasterIdolLoadIdentity.Generation)epoch=checked(epoch+1);
            session=net;manager=man;world=net.GetWorldUID();generation=MasterIdolLoadIdentity.Generation;
            if(world==0)return;
            // One initial verified file read/create on this authority host. No scan,
            // flush, countdown write or auto-reset occurs during subsequent frames.
            if(ledger==null)ledger=new WorkbenchEnchantmentCharacterLedger(Path.Combine(directory,"characters-v1.bin"),()=>DateTime.UtcNow.Ticks);
            if(baselineEpoch!=epoch&&ledger.BeginEpoch(world))baselineEpoch=epoch;
        }
        internal static WorkbenchCooldownContext CreateContext(WorkbenchEnchantmentContext domain,ZRpc rpc)
        {
            if(!Current()||ledger?.Available!=true||domain==null||domain.World!=world)return null;
            var actor=WorkshopActor.Resolve(rpc);var bench=WorkbenchEnchantmentRuntime.Journal?.ReadStation(domain.Station);
            if(actor?.Available!=true||actor.GetPlayerID()!=domain.Actor||bench==null||bench.NativeUid!=domain.NativeUid||string.IsNullOrEmpty(bench.NativeWitness))return null;
            var capturedSession=session;var capturedManager=manager;long capturedWorld=world;ulong capturedGeneration=generation,capturedEpoch=epoch;var character=actor.CharacterId;
            var parts=domain.NativeUid.Split(':');
            if(parts.Length!=2||!long.TryParse(parts[0],out long user)||!uint.TryParse(parts[1],out uint id))return null;
            ZDO native=manager.GetZDO(new ZDOID(user,id));
            if(native==null)return null;
            var context=new WorkbenchCooldownContext{Character=domain.Actor,World=domain.World,Station=domain.Station,NativeUid=domain.NativeUid,NativeWitness=bench.NativeWitness,CharacterUid=character.ToString()};
            context.IdentityCurrent=()=>
            {
                if(!Current()||epoch!=capturedEpoch||session!=capturedSession||manager!=capturedManager||world!=capturedWorld||generation!=capturedGeneration||domain.Revalidate?.Invoke()!=true)return false;
                if(!Current()||epoch!=capturedEpoch||session!=capturedSession||manager!=capturedManager||world!=capturedWorld||generation!=capturedGeneration)return false;
                var current=WorkshopActor.Resolve(rpc);var station=WorkbenchEnchantmentRuntime.Journal?.ReadStation(domain.Station);
                return current?.Available==true&&current.GetPlayerID()==domain.Actor&&current.CharacterId==character&&station!=null&&station.Id==context.Station&&station.NativeUid==domain.NativeUid&&station.NativeWitness==context.NativeWitness&&ReferenceEquals(manager.GetZDO(native.m_uid),native);
            };
            context.MayLearn=()=>context.IdentityCurrent()&&(rpc==null?OwnerSkillAuthority.Has(Player.m_localPlayer,Skills.SkillType.Crafting,100):OwnerSkillAuthority.Has(rpc,character,domain.Actor,Skills.SkillType.Crafting,100));
            // A reload alias requires the domain owner's exact original-operation proof.
            context.OriginalNativeIdentity=original=>original==context.NativeUid&&context.IdentityCurrent();
            return context;
        }
        // Settlement only: Utility supplies a server-private durable Removed proof.
        // Missing native object is never a destruction certificate.
        internal static WorkbenchCooldownContext CreateRecoveryContext(WorkbenchEnchantmentContext domain,ZRpc rpc,Func<bool> verifyPersistedRemoved)
        {
            if(!Current()||ledger?.Available!=true||domain==null||domain.World!=world||verifyPersistedRemoved==null)return null;
            var actor=WorkshopActor.Resolve(rpc);var bench=WorkbenchEnchantmentRuntime.Journal?.ReadStation(domain.Station);
            if(actor?.Available!=true||actor.GetPlayerID()!=domain.Actor||bench==null||bench.Id!=domain.Station||bench.NativeUid!=domain.NativeUid||string.IsNullOrEmpty(bench.NativeWitness))return null;
            var capturedSession=session;var capturedManager=manager;long capturedWorld=world,capturedActor=domain.Actor;ulong capturedEpoch=epoch;var character=actor.CharacterId;
            string stationId=bench.Id,uid=bench.NativeUid,witness=bench.NativeWitness;
            var context=new WorkbenchCooldownContext{Character=capturedActor,World=capturedWorld,Station=stationId,NativeUid=uid,NativeWitness=witness,CharacterUid=character.ToString(),MayLearn=()=>false};
            Func<bool> bound=()=>
            {
                if(!Current()||epoch!=capturedEpoch||session!=capturedSession||manager!=capturedManager||world!=capturedWorld)return false;
                var current=WorkshopActor.Resolve(rpc);var removed=WorkbenchEnchantmentRuntime.Journal?.ReadStation(stationId);
                if(current?.Available!=true||current.GetPlayerID()!=capturedActor||current.CharacterId!=character||removed==null||removed.Id!=stationId||removed.NativeUid!=uid||removed.NativeWitness!=witness)return false;
                return true;
            };
            context.IdentityCurrent=()=>{try{return bound()&&verifyPersistedRemoved()&&bound();}catch{return false;}};
            context.OriginalNativeIdentity=original=>original==uid&&context.IdentityCurrent();
            return context.IdentityCurrent()?context:null;
        }
        internal static WorkbenchCooldownPermit[] ReadPending(ZRpc rpc)
        {
            if(!Current()||ledger?.Available!=true)return Array.Empty<WorkbenchCooldownPermit>();
            var actor=WorkshopActor.Resolve(rpc);
            return actor?.Available==true?ledger.ReadPending(actor.GetPlayerID(),world):Array.Empty<WorkbenchCooldownPermit>();
        }
        internal static WorkbenchCooldownPermit ReadOriginal(ZRpc rpc,string operation,string choice)
        {
            if(!Current()||ledger?.Available!=true)return null;
            var actor=WorkshopActor.Resolve(rpc);
            return actor?.Available==true?ledger.ReadOriginal(actor.GetPlayerID(),world,operation,choice):null;
        }
        internal static bool IsRetiredPending(WorkbenchCooldownPermit original)=>Current()&&ledger?.Available==true&&original?.World==world&&ledger.IsRetiredPending(original);
        internal static WorkbenchCooldownContext CreateSettlementContext(WorkbenchCooldownPermit original,ZRpc rpc,Func<bool> verifyPersistedOperation)
        {
            if(!Current()||ledger?.Available!=true||original==null||original.World!=world||verifyPersistedOperation==null)return null;
            var actor=WorkshopActor.Resolve(rpc);
            if(actor?.Available!=true||actor.GetPlayerID()!=original.Character)return null;
            var stored=ledger.ReadOriginal(original.Character,world,original.Operation,original.Choice);
            if(stored==null||stored.Token!=original.Token||stored.Station!=original.Station||stored.NativeUid!=original.NativeUid||stored.NativeWitness!=original.NativeWitness||stored.Generation!=original.Generation)return null;
            var capturedSession=session;var capturedManager=manager;long capturedWorld=world,capturedActor=stored.Character;ulong capturedEpoch=epoch,capturedGeneration=generation;var character=actor.CharacterId;
            var context=new WorkbenchCooldownContext{Character=capturedActor,World=capturedWorld,Station=stored.Station,NativeUid=stored.NativeUid,NativeWitness=stored.NativeWitness,CharacterUid=character.ToString(),MayLearn=()=>false};
            Func<bool> bound=()=>
            {
                if(!Current()||epoch!=capturedEpoch||generation!=capturedGeneration||session!=capturedSession||manager!=capturedManager||world!=capturedWorld)return false;
                var current=WorkshopActor.Resolve(rpc);var row=WorkbenchEnchantmentRuntime.Journal?.ReadStation(stored.Station);
                return current?.Available==true&&current.GetPlayerID()==capturedActor&&current.CharacterId==character&&row!=null&&row.Id==stored.Station&&row.NativeWitness==stored.NativeWitness;
            };
            context.IdentityCurrent=()=>{try{return bound()&&verifyPersistedOperation()&&bound();}catch{return false;}};
            context.OriginalNativeIdentity=uid=>uid==stored.NativeUid&&context.IdentityCurrent();
            return context.IdentityCurrent()?context:null;
        }
    }
}
