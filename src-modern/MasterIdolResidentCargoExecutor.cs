using System;
using System.Collections.Generic;
using UnityEngine;
namespace ValheimMastery
{
    internal static partial class MasterIdolResidentCargo
    {
        private sealed class Grant
        {
            internal Kind Kind;internal string Nonce,Epoch,Home,BodyNonce,Witness;internal ZDOID Actor,Cargo,Target,Bench;
            internal int ActorRevision,CargoRevision,TargetRevision;internal long Born,Principal,Issued;
            internal uint CargoSaved,TargetSaved;internal float Until;internal ulong Generation;
            internal Vector3 BenchPoint;
        }
        private static readonly Dictionary<ZDOID,Grant> Executing=new Dictionary<ZDOID,Grant>();
        private static readonly Dictionary<string,float> Executed=new Dictionary<string,float>();
        private sealed class CloseRetry{internal uint Revision;internal float Until,Next;}
        private static readonly Dictionary<ZDOID,CloseRetry> CloseRetries=new Dictionary<ZDOID,CloseRetry>();
        private static string LocalOpenNonce;private static float LocalOpenUntil;private static Container ManualBox;private static ZDOID ManualActor;
        private static void ResetExecutor(){Executing.Clear();Executed.Clear();CloseRetries.Clear();LocalOpenNonce=null;ManualBox=null;ManualActor=ZDOID.None;}
        private static void CancelExecutor(ZDOID actor){if(Executing.TryGetValue(actor,out var grant)){Executed[grant.Nonce]=Time.unscaledTime+15;Executing.Remove(actor);}}
        private static bool ExecutorLocked(ZDOID id)
        {foreach(var grant in Executing.Values)if(grant.Cargo==id||grant.Target==id)return true;return false;}
        private static void SendGrant(Operation op,ResidentCargoBinding binding)
        {
            var p=Header();p.Write((byte)op.Kind);p.Write(op.Nonce);p.Write(op.Actor.m_uid);p.Write((int)op.Actor.OwnerRevision);p.Write(op.Actor.GetLong(MasterIdolForestGuardState.BornKey,0));p.Write(op.Actor.GetString(MasterIdolForestGuardState.NonceKey,""));p.Write(op.Epoch);p.Write(op.Home);
            p.Write(op.Cargo.m_uid);p.Write((int)op.Cargo.OwnerRevision);p.Write(op.Cargo.DataRevision);p.Write(op.Target?.m_uid??ZDOID.None);p.Write((int)(op.Target?.OwnerRevision??0));p.Write(op.Target?.DataRevision??0u);
            p.Write(binding.Bench?.m_uid??ZDOID.None);p.Write(binding.Principal);p.Write(binding.Bench==null?"":WorkbenchEnchantmentRuntime.Witness(binding.Bench));p.Write(binding.Bench?.GetPosition()??Vector3.zero);p.Write(Session.GetTime().Ticks);
            if(op.Executor==ZNet.GetUID())ApplyGrant(p);else if(PeerReady(Peer(op.Executor)))Peer(op.Executor).m_rpc.Invoke(GrantRpc,p);
        }
        private static void ReceiveGrant(ZRpc rpc,ZPackage p){if(TrustedServer(rpc))ApplyGrant(p);}
        private static void ApplyGrant(ZPackage p)
        {
            try
            {
                if(!ReadHeader(p))return;
                var g=new Grant{Kind=(Kind)p.ReadByte(),Nonce=p.ReadString(),Actor=p.ReadZDOID(),ActorRevision=p.ReadInt(),Born=p.ReadLong(),BodyNonce=p.ReadString(),Epoch=p.ReadString(),Home=p.ReadString(),Cargo=p.ReadZDOID(),CargoRevision=p.ReadInt(),CargoSaved=p.ReadUInt(),Target=p.ReadZDOID(),TargetRevision=p.ReadInt(),TargetSaved=p.ReadUInt(),Bench=p.ReadZDOID(),Principal=p.ReadLong(),Witness=p.ReadString(),BenchPoint=p.ReadVector3(),Issued=p.ReadLong(),Until=Time.unscaledTime+6,Generation=MasterIdolLoadIdentity.Generation};
                if(p.GetPos()!=p.Size()||g.Kind>Kind.Deposit||!Guid.TryParseExact(g.Nonce,"N",out _)||!MasterIdolForestRules.Nonce(g.BodyNonce)||g.Home.Length>128||g.Witness.Length>256||g.Epoch.Length>32||g.Kind!=Kind.Open&&g.Principal==0)return;
                // A replay must not turn a successfully opened UI into failure.
                if(Executed.ContainsKey(g.Nonce)||Executing.TryGetValue(g.Actor,out var same)&&same.Nonce==g.Nonce)return;
                if(Math.Abs(Session.GetTime().Ticks-g.Issued)>3*TimeSpan.TicksPerSecond||Executing.Count>=4||g.Kind==Kind.Open&&g.Nonce!=LocalOpenNonce||Executing.ContainsKey(g.Actor))
                {ResidentCargoOpenTrace.Write(g.Kind==Kind.Open,"executor-grant-rejected",g.Nonce);Executed[g.Nonce]=Time.unscaledTime+15;SendDone(g,false);return;}
                ResidentCargoOpenTrace.Write(g.Kind==Kind.Open,"executor-grant",g.Nonce);Executing[g.Actor]=g;
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.grant-rejected");}
        }
        private static bool LocalBinding(Grant g,out Character actor,out Container cargo)
        {
            actor=null;cargo=null;var body=Manager.GetZDO(g.Actor);var store=Manager.GetZDO(g.Cargo);var bench=Manager.GetZDO(g.Bench);
            if(g.Generation!=MasterIdolLoadIdentity.Generation||!MasterIdolForestGuardState.ModuleBorn(body)||body.OwnerRevision!=g.ActorRevision||body.GetLong(MasterIdolForestGuardState.BornKey,0)!=g.Born||body.GetString(MasterIdolForestGuardState.NonceKey,"")!=g.BodyNonce||body.GetString(EpochKey,"")!=g.Epoch||body.GetString(MasterIdolNativeTaming.HomeKey,"")!=g.Home||body.GetZDOID(Link)!=g.Cargo){ResidentCargoOpenTrace.Write(g.Kind==Kind.Open,"executor-bind-body",g.Nonce);return false;}
            if(!Owner(store)||store.OwnerRevision!=g.CargoRevision||!Reached(store.DataRevision,g.CargoSaved)||store.GetZDOID(BodyKey)!=g.Actor||store.GetLong(BornKey,0)!=g.Born||store.GetString(NonceKey,"")!=g.BodyNonce||store.GetLong(WorldKey,0)!=World||store.GetInt(FamilyKey,-1)!=MasterIdolForestGuardState.Family(body)){ResidentCargoOpenTrace.Write(g.Kind==Kind.Open,"executor-bind-store",g.Nonce);return false;}
            if(g.Kind!=Kind.Open)
            {
                var zone=MasterIdolEffectZones.ResidentHome(g.Actor.ToString(),MasterIdolForestRules.Families[MasterIdolForestGuardState.Family(body)]);
                if(zone?.Id!=g.Home||zone.Home!=g.BenchPoint)return false;
                // A connected workshop network can extend beyond this peer's
                // streamed sectors. The authenticated server supplied the proof;
                // when a local bench ZDO exists it must agree, never contradict.
                if(bench!=null&&(!ReferenceEquals(Manager.GetZDO(g.Bench),bench)||bench.GetLong(ZDOVars.s_creator,0)!=g.Principal||WorkbenchEnchantmentRuntime.Witness(bench)!=g.Witness))return false;
            }
            actor=ActorFor(body);cargo=ContainerFor(store);
            bool ready=actor!=null&&!actor.IsDead()&&cargo!=null&&cargo.IsOwner()&&!cargo.IsInUse()&&store.GetLong(OpenPeerKey,0)==0&&store.GetInt(ZDOVars.s_inUse,0)==0;if(!ready){ResidentCargoOpenTrace.Write(g.Kind==Kind.Open,"executor-native-body-container-wait",g.Nonce);}return ready;
        }
        private static void TickExecutor()
        {
            foreach(var pair in new List<KeyValuePair<ZDOID,Grant>>(Executing))
            {
                var g=pair.Value;bool done=false,success=false;
                var executionNet=Session;var executionManager=Manager;var executionScene=ZNetScene.instance;long executionWorld=World;ulong executionGeneration=Generation;
                try
                {
                    if(Time.unscaledTime>g.Until){ResidentCargoOpenTrace.Write(g.Kind==Kind.Open,"executor-timeout",g.Nonce);done=true;continue;}
                    if(!LocalBinding(g,out var actor,out var cargo))continue;
                    var body=actor.m_nview.GetZDO();
                    if(g.Kind==Kind.Open)
                    {
                        var player=Player.m_localPlayer;
                        if(player==null||player.IsDead()||player.IsTeleporting()||g.Nonce!=LocalOpenNonce||!InRange(body,player.m_nview?.GetZDO(),5)||!MasterIdolEffectZones.Allows(player.GetPlayerID(),body.GetPosition())){ResidentCargoOpenTrace.Write(true,"executor-player-range-projection",g.Nonce);done=true;continue;}
                        if(InventoryGui.instance==null){ResidentCargoOpenTrace.Write(true,"executor-no-ui",g.Nonce);continue;}
                        // An asynchronous open may arrive after the player has
                        // opened another chest. Never orphan that native lease.
                        if(InventoryGui.instance.m_currentContainer!=null&&InventoryGui.instance.m_currentContainer!=cargo){ResidentCargoOpenTrace.Write(true,"executor-other-container",g.Nonce);done=true;continue;}
                        // Reserve before native UI callbacks; a replay cannot Show
                        // again or refill anything. Native InventoryGui is real.
                        Executed[g.Nonce]=Time.unscaledTime+15;cargo.Load();var data=cargo.m_nview.GetZDO();
                        data.Set(OpenPeerKey,ZNet.GetUID());data.Set(OpenPlayerKey,player.GetPlayerID());cargo.Save();
                        ManualBox=cargo;ManualActor=g.Actor;InventoryGui.instance.Show(cargo,1);cargo.SetInUse(true);ResidentCargoOpenTrace.Write(true,"executor-native-show-returned",g.Nonce);LocalOpenNonce=null;success=true;done=true;continue;
                    }
                    if(!actor.m_nview.IsOwner()){done=true;continue;}
                    var pin=new DancePin(actor);if(!pin.Current()){done=true;continue;}
                    MasterIdolResidentDance.Cancel(actor,"cargo.grant");
                    if(!pin.Current()||!Executing.TryGetValue(g.Actor,out var currentGrant)||!ReferenceEquals(currentGrant,g)||
                        !LocalBinding(g,out var currentActor,out var currentCargo)||!ReferenceEquals(currentActor,actor)||!ReferenceEquals(currentCargo,cargo)||
                        !actor.m_nview.IsOwner()||!Peace(actor)||!MasterIdolActivity.Near(actor.transform.position)){done=true;continue;}
                    body=actor.m_nview.GetZDO();
                    var target=Manager.GetZDO(g.Target);var instance=ZNetScene.instance.FindInstance(g.Target);var zone=MasterIdolEffectZones.Find(g.Home);
                    if(target==null||instance==null||!InRange(body,target,3)||zone==null||!zone.Contains(target.GetPosition())||!MasterIdolEffectZones.Allows(g.Principal,body.GetPosition())||!MasterIdolEffectZones.Allows(g.Principal,target.GetPosition())){done=true;continue;}
                    if(g.Kind==Kind.Pickup)
                    {
                        var drop=instance.GetComponent<ItemDrop>();if(drop?.m_nview?.IsValid()!=true||!ReferenceEquals(drop.m_nview.GetZDO(),target)){done=true;continue;}
                        if(!drop.m_nview.IsOwner()){drop.RequestOwn();continue;}if(!drop.CanPickup(true))continue;
                        Executed[g.Nonce]=Time.unscaledTime+15;cargo.Load();ResidentCargoTransfer.Pickup(cargo,drop);success=true;done=true;
                    }
                    else
                    {
                        var destination=ContainerFor(target);var inc=destination?.GetComponentInParent<Incinerator>();
                        if(destination==null||inc==null||!Owner(target)||target.OwnerRevision!=g.TargetRevision||!Reached(target.DataRevision,g.TargetSaved)||!destination.IsOwner()||destination.IsInUse()||target.GetInt(ZDOVars.s_inUse,0)!=0||inc.isInUse||!destination.CheckAccess(g.Principal))continue;
                        cargo.Load();destination.Load();Executed[g.Nonce]=Time.unscaledTime+15;ResidentCargoTransfer.Deposit(cargo,destination);success=true;done=true;
                    }
                }
                catch(Exception error)
                {
                    done=true;
                    if(g.Kind==Kind.Open)
                    {
                        var box=ContainerFor(Manager.GetZDO(g.Cargo));
                        if(box!=null&&box.IsOwner()&&Player.m_localPlayer!=null)
                        {
                            try{if(InventoryGui.instance?.m_currentContainer==box)InventoryGui.instance.CloseContainer();else if(box.IsInUse())box.SetInUse(false);}catch(Exception){/* Clear our reservation below; never reset inventory. */}
                            var data=box.m_nview.GetZDO();if(data.GetLong(OpenPeerKey,0)==ZNet.GetUID()){data.Set(OpenPeerKey,0L);data.Set(OpenPlayerKey,0L);Manager.ForceSendZDO(data.m_uid);}ManualBox=null;
                        }
                    }
                    MasteryPlugin.Log?.LogWarning("[GuardCargo] owner transfer retained/rolled back: "+error.Message);
                }
                finally
                {
                    if(done&&ReferenceEquals(Session,executionNet)&&ReferenceEquals(Session,ZNet.instance)&&ReferenceEquals(Manager,executionManager)&&ReferenceEquals(Manager,ZDOMan.instance)&&
                        ReferenceEquals(ZNetScene.instance,executionScene)&&World==executionWorld&&Session?.GetWorld()!=null&&Session.GetWorldUID()==executionWorld&&
                        Generation==executionGeneration&&Generation==MasterIdolLoadIdentity.Generation&&MasterIdolLoadIdentity.LoadReady&&
                        Executing.TryGetValue(pair.Key,out var finishing)&&ReferenceEquals(finishing,g)){Executing.Remove(pair.Key);Executed[g.Nonce]=Time.unscaledTime+15;SendDone(g,success);if(g.Kind!=Kind.Open)ActorFor(Manager.GetZDO(g.Actor))?.GetComponent<MasterIdolResidentCargoAgent>()?.Result(g.Nonce);else if(!success&&LocalOpenNonce==g.Nonce)LocalOpenNonce=null;}
                }
            }
            if(LocalOpenNonce!=null&&Time.unscaledTime>LocalOpenUntil){ResidentCargoOpenTrace.Write(true,"client-timeout",LocalOpenNonce);LocalOpenNonce=null;Player.m_localPlayer?.Message(MessageHud.MessageType.Center,"Вартовий зараз зайнятий або його речі ще завантажуються.");}
            if(ManualBox!=null&&Player.m_localPlayer!=null)
            {
                var actor=Manager.GetZDO(ManualActor);var player=Player.m_localPlayer;
                bool allowed=actor!=null&&MasterIdolForestGuardState.ModuleBorn(actor)&&ResidentCargoRules.Living(actor.GetFloat(ZDOVars.s_health,1))&&!player.IsDead()&&InRange(actor,player.m_nview?.GetZDO(),5)&&MasterIdolEffectZones.Allows(player.GetPlayerID(),actor.GetPosition());
                if(!allowed&&InventoryGui.instance?.m_currentContainer==ManualBox)InventoryGui.instance.CloseContainer();
                if(!ManualBox.IsInUse())ManualBox=null;
            }
            foreach(var pair in new List<KeyValuePair<ZDOID,CloseRetry>>(CloseRetries))
            {
                var data=Manager.GetZDO(pair.Key);var retry=pair.Value;
                if(data==null||!Owner(data)||Time.unscaledTime>retry.Until){CloseRetries.Remove(pair.Key);continue;}
                if(Time.unscaledTime<retry.Next)continue;retry.Next=Time.unscaledTime+.5f;
                var packet=Header();packet.Write(pair.Key);packet.Write(retry.Revision);
                if(Session.IsServer())QueueClose(ZNet.GetUID(),packet);else Session.GetServerRPC()?.Invoke(CloseRpc,packet);
            }
            foreach(var nonce in new List<string>(Executed.Keys))if(Executed[nonce]<Time.unscaledTime)Executed.Remove(nonce);
        }
        private static void SendDone(Grant g,bool success)
        {
            var p=Header();p.Write(g.Nonce);p.Write(g.Actor);p.Write(success);p.Write(Manager.GetZDO(g.Cargo)?.DataRevision??0u);p.Write(Manager.GetZDO(g.Target)?.DataRevision??0u);
            Manager.ForceSendZDO(g.Cargo);if(Session.IsServer())ApplyDone(ZNet.GetUID(),p);else Session.GetServerRPC()?.Invoke(DoneRpc,p);
        }
        private static void ReceiveDone(ZRpc rpc,ZPackage p)
        {if(!Context()||!Session.IsServer())return;var peer=Session.GetPeer(rpc);if(PeerReady(peer))ApplyDone(peer.m_uid,p);}
        private static void ApplyDone(long sender,ZPackage p)
        {
            try
            {
                if(!ReadHeader(p,192))return;string nonce=p.ReadString();var actor=p.ReadZDOID();bool success=p.ReadBool();uint cargoRevision=p.ReadUInt(),targetRevision=p.ReadUInt();
                if(p.GetPos()!=p.Size()||!Pending.TryGetValue(actor,out var op)||op.Nonce!=nonce||!op.Granted||op.Executor!=sender)return;
                // Completion never writes inventory or creates another grant.
                // Keep open admission until owner's GUI metadata has arrived.
                if(op.Kind==Kind.Open&&success){op.Done=true;op.DoneSaved=cargoRevision;}
                else
                {
                    Pending.Remove(actor);
                    if(op.Kind==Kind.Open&&ManualLeases.TryGetValue(op.Cargo.m_uid,out var lease)&&lease.Nonce==nonce)lease.Until=Time.unscaledTime;
                }
                MasterIdolResidentMetrics.Count(success?"cargo.completed":"cargo.retained");
            }
            catch(Exception){MasterIdolResidentMetrics.Count("cargo.done-rejected");}
        }
    }
}
