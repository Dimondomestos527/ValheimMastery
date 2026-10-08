using System;

using System.Collections.Generic;

using HarmonyLib;

using UnityEngine;



namespace ValheimMastery

{

    internal static class MasterIdolPlacement

    {

        // Recovery storage is world/session-bound; it is not fresh-action authorization.

        internal static GoldCraftingLedger CurrentLedger => GoldCraftingService.TryGetRecoveryLedger(

            ZNet.instance, ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0, out var ledger) ? ledger : null;

        private static bool GoldWorldReady(bool requireEnabled=true)

        {

            var net = ZNet.instance;

            return net?.GetWorld() != null && (!requireEnabled || GoldCraftingService.Enabled) &&

                GoldCraftingService.TryGetRecoveryLedger(net, net.GetWorldUID(), out _);

        }

        private sealed class Intent

        {

            internal MasterIdolReceipt Receipt;

            internal Player Player;

            internal Piece Piece;

            internal ZNet Session;

            internal ZRpc Rpc;

            internal Vector3 Position;

            internal Quaternion Rotation;

            internal string Nonce;

            internal bool Preflight, Granted, Cancelled, CancelSaved, Entered, Completed, Finished, Saved, AutoPending;

            internal ZDOID Output;

            internal long RequestedAt, NativeAt;

            internal float PreflightExpires;

        }

        private sealed class LiveGrant

        {

            internal ZNet Session;

            internal ZRpc Rpc;

            internal ZDOID Character;

            internal long Player, World;

            internal string Nonce;

        }

        private static readonly Dictionary<string, LiveGrant> Live = new Dictionary<string, LiveGrant>(StringComparer.Ordinal);

        private static Intent Current;

        private static ZNet Session;

        private static ZRpc ProtocolRpc;

        private static Player LocalPlayer;

        private static ZRpc LocalRpc;

        private static bool Supported, LegacyPeer;

        private static float Next, NextHello;

        private static int HelloAttempts;

        private static float NextNotice;

        internal static bool PreviewPending(Player player) => Current != null && Current.Player == player && Current.Session == ZNet.instance &&

            Current.Rpc == ZNet.instance?.GetServerRPC() && !Current.Cancelled && !Current.Entered && !Current.Finished;

        internal static bool AutoReady(Player player) => Current?.AutoPending == true && Current.Granted && !Current.Entered &&

            !Current.Cancelled && !Current.Finished && !WorkshopRemoteCraft.ClientBusy && !WorkshopRecovery.Outstanding &&

            Context(Current, player, Current.Piece);

        internal static bool ConsumeAuto()

        { if (!AutoReady(Player.m_localPlayer)) return false; Current.AutoPending = false; return true; }

        private static void Tell(Player player, string text)

        { player?.Message(MessageHud.MessageType.Center, "<color=#FFD36A>" + text + "</color>"); }

        private static bool Deny(Player player, string reason)

        {

            if (player == Player.m_localPlayer && Time.unscaledTime >= NextNotice)

            { NextNotice = Time.unscaledTime + 1f; Tell(player, reason); }

            return false;

        }

        internal static void Shutdown()

        {

            MasterIdolPreviewLock.Reset(); Current = null; Live.Clear();PendingRequests.Clear(); Session = null; ProtocolRpc = LocalRpc = null;

            LocalPlayer = null; Supported = false; LegacyPeer=false; Next = NextHello = 0; HelloAttempts = 0;

            MasterIdolWorldRegistry.Reset(); MasterIdolWorldIndex.Shutdown(); MasterIdolLoadIdentity.Reset();

        }

        internal static void Register(ZNetPeer peer)

        {

            if (peer?.m_rpc == null) return;

            peer.m_rpc.Register<ZPackage>("VM_Idol_Request_v1", Receive);

            peer.m_rpc.Register<ZPackage>("VM_Idol_Reply_v1", Reply);

        }

        private sealed class PendingRequest {internal LiveGrant Context;internal string Type;internal Vector3 Point;internal byte[] Packet;internal float Until;internal bool Cancel;internal MasterIdolAdmissionStore Store;internal GoldCraftingLedger Ledger;internal ZDOMan Manager;internal ulong Generation;}

        private static readonly Dictionary<string,PendingRequest> PendingRequests=new Dictionary<string,PendingRequest>(StringComparer.Ordinal);private static readonly List<string> PendingIds=new List<string>();private static int PendingCursor;private static float NextPending;private static ulong PendingGeneration;private static ZDOMan PendingManager;

        internal static void Tick()

        {

            var net = ZNet.instance;

            if (Session != net)

            { Session = net; Current = null; Live.Clear(); PendingRequests.Clear();NextPending=0; ProtocolRpc = null; Supported = false; LegacyPeer=false; Next = NextHello = 0; HelloAttempts = 0; LocalPlayer = null; LocalRpc = null; }

            if (LocalPlayer != Player.m_localPlayer || LocalRpc != net?.GetServerRPC())

            {

                Current = null; LocalPlayer = Player.m_localPlayer; LocalRpc = net?.GetServerRPC();

                ProtocolRpc = null; Supported = false; LegacyPeer=false; HelloAttempts = 0; Next = NextHello = 0;

            }

            if(PendingGeneration!=MasterIdolLoadIdentity.Generation||!ReferenceEquals(PendingManager,ZDOMan.instance)){PendingGeneration=MasterIdolLoadIdentity.Generation;PendingManager=ZDOMan.instance;PendingRequests.Clear();Live.Clear();}

            MasterIdolWorldRegistry.Refresh();ProcessPending();

            if(Current?.Preflight==true && Time.unscaledTime>=Current.PreflightExpires){CancelLive();Tell(Player.m_localPlayer,"Вьолундр не почув поклику. Спробуй ще раз.");}

            if(PreviewPending(Player.m_localPlayer) && !MasterIdolPreviewLock.Matches(Player.m_localPlayer,Current.Piece))CancelLive();

            if (net == null || Time.time < Next) return;

            Next = Time.time + 2f;

            MasterIdolPieces.RefreshRecipes();

            if (Player.m_localPlayer == null) return;

            if (net.IsServer()) Supported = true;

            else if ((!Supported || ProtocolRpc != net.GetServerRPC()) && (HelloAttempts < 4 || Time.time >= NextHello))

            { HelloAttempts = Math.Min(4, HelloAttempts + 1); NextHello = Time.time + 10f; Send(0, null); }

            var player = Player.m_localPlayer;

            if (Current != null && !Current.Entered && !Current.Cancelled && !Context(Current, player, Current.Piece)) CancelLive();

            if(Current?.Cancelled==true&&Current.CancelSaved)Send(2,Current.Receipt,Current.Nonce??"");

            else if(Current!=null&&!Current.Preflight&&!Current.Granted&&!Current.Entered&&!Current.Cancelled)Send(1,Current.Receipt);

            var receipt = MasterIdolJournal.Read(player);

            if (receipt?.Phase == 3 && (Current == null || Current.Saved)) Send(3, receipt);

            // A loaded cancelled receipt can query its result, but cannot recreate a revocation certificate.

            else if (receipt?.Phase == 4 && Current == null) Send(4, receipt);

        }

        internal static bool BeforeTry(Player player, Piece piece)

        {

            var profile = MasterIdolPieces.Profile(piece); if (profile == null) return true;

            if (player == null || player != Player.m_localPlayer) return false;

            if (!GoldCraftingService.Enabled || !GoldCraftingService.Unlocked)

                return Deny(player, "Вьолундр ще не довірив тобі створення ідолів.");

            if(LegacyPeer)return Deny(player,"Цей світ ще не знає нових правил ідолів. Потрібне оновлення сервера.");

            if (!Supported || ZNet.instance == null || (!ZNet.instance.IsServer() && ProtocolRpc != ZNet.instance.GetServerRPC()))

                return Deny(player, "Зачекай мить — благословення Вьолундра ще не досягло цього світу.");

            if (player.IsDead() || player.IsTeleporting()) return false;

            // World free construction is handled entirely by vanilla requirements/payment.

            // Personal debug placement alone is not a verified zero-debit native path.

            if ((player.NoCostCheat() || player.m_noPlacementCost) && ZoneSystem.instance?.GetGlobalKey(piece.FreeBuildKey()) != true)

                return Deny(player, "Дарунок Вьолундра потребує матеріалів, доки цей світ не звільнений від їхньої ціни.");

            if (Current != null)

            {

                if (Current.Entered || Current.Cancelled || Current.Finished)

                    return Deny(player, "Дочекайся завершення попереднього творіння.");

                if (!Context(Current, player, piece)) { CancelLive(); return Deny(player, "Місце творіння змінилося. Обери його знову."); }

                if (!Current.Granted) return Deny(player, "Вьолундр придивляється до обраного місця…");

                return Current.Granted;

            }

            var previous = MasterIdolJournal.Read(player);

            if (previous != null && previous.Phase != 5)

            { return Deny(player, "Доля попереднього творіння ще не визначена. Зачекай на його завершення."); }

            if (WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding)

                return Deny(player, "Майстерня ще зайнята попередньою роботою.");

            if (!LocalWorkbench(player.transform.position))

                return Deny(player, "Створи ідола в межах будівельного радіуса верстака.");

            if (player.m_placementGhost == null || player.m_placementStatus != Player.PlacementStatus.Valid)

                return Deny(player, "Знайди для ідола вільне місце у своїй майстерні.");

            player.UpdatePlacementGhost(false); // fresh native validation and aim snapshot, no placement/payment replay

            if(player.m_placementStatus != Player.PlacementStatus.Valid || !MasterIdolPreviewLock.Lock(player,piece))

                return Deny(player,"Обране місце змінилося. Прицілься ще раз.");

            var receipt = new MasterIdolReceipt { Token = Guid.NewGuid().ToString("N"), Type = profile.Id,

                Player = player.GetPlayerID(), World = ZNet.instance.GetWorldUID(), Phase = 1, Output = ZDOID.None };

            var intent = new Intent { Player = player, Piece = piece, Session = ZNet.instance, Rpc = ZNet.instance.GetServerRPC(),

                Receipt = receipt, Position = player.m_placementGhost.transform.position, Rotation = player.m_placementGhost.transform.rotation };

            Current = intent; intent.Preflight=true; intent.PreflightExpires=Time.unscaledTime+5f; intent.RequestedAt = MasterIdolTiming.Start();

            OwnerSkillAuthority.SendNow(); Send(5, receipt); // eligibility only: no receipt mutation, reservation or execution grant

            Tell(player, "Вьолундр придивляється до обраного місця…");

            return false;

        }

        private static bool LocalWorkbench(Vector3 point)

        {

            var expected = MasterIdolPieces.Workbench;

            if (expected == null || CraftingStation.m_allStations == null) return false;

            foreach (var station in CraftingStation.m_allStations)

            {

                if (station == null || station.m_name != expected.m_name) continue;

                float radius = WorkshopStationRadius.EffectiveRadius(station.m_rangeBuild, station.GetLevel(false));

                float x = point.x - station.transform.position.x, z = point.z - station.transform.position.z;

                if (radius > 0 && x*x+z*z < radius*radius) return true;

            }

            return false;

        }

        private static bool Context(Intent intent, Player player, Piece piece) => intent != null && player != null &&

            intent.Session == ZNet.instance && intent.Player == player && player == Player.m_localPlayer &&

            intent.Rpc == ZNet.instance?.GetServerRPC() && piece == intent.Piece && player.GetSelectedPiece() == piece &&

            !player.IsDead() && !player.IsTeleporting() && GoldCraftingService.Enabled && GoldCraftingService.Unlocked &&

            LocalWorkbench(intent.Position) && MasterIdolPreviewLock.Matches(player,piece) &&

            player.m_placementGhost != null && Vector3.Distance(player.m_placementGhost.transform.position, intent.Position) <= .05f &&

            Quaternion.Angle(player.m_placementGhost.transform.rotation, intent.Rotation) <= 1f;

        private static void CancelLive()

        {

            var intent = Current;

            if (intent == null || intent.Entered || intent.Cancelled || intent.Finished) return;

            if(intent.Preflight){intent.Cancelled=true;Current=null;return;} // no request was issued or recorded

            intent.Cancelled = true; intent.Granted = false; // Terminal BEFORE save/transport; late grants cannot reopen it.

            intent.CancelSaved = MasterIdolJournal.Record(intent.Player, intent.Receipt, 4, ZDOID.None);

            if(intent.CancelSaved&&!intent.Preflight)Send(2,intent.Receipt,intent.Nonce??"");

        }

        internal static void Enter(Player player, Piece piece, Vector3 position, Quaternion rotation)

        {

            if (MasterIdolPieces.Profile(piece) == null) return;

            var intent = Current;

            if (intent == null || intent.Entered || intent.Cancelled || !intent.Granted || !Context(intent, player, piece) ||

                Vector3.Distance(position, intent.Position) > .05f || Quaternion.Angle(rotation, intent.Rotation) > 1f)

                throw new InvalidOperationException("Master Idol placement has no matching execution permit.");

            intent.NativeAt=MasterIdolTiming.Start();

            intent.Entered = true; intent.Granted = false; // Irreversible even if native Instantiate subsequently throws.

            if (!MasterIdolJournal.Record(player, intent.Receipt, 2, ZDOID.None))

                throw new InvalidOperationException("Master Idol receipt could not be recorded.");

        }

        internal static void Stamp(ZDO output)

        {

            var intent = Current;

            if (intent == null || !intent.Entered || intent.Cancelled || output == null || intent.Output != ZDOID.None ||

                MasterIdolWorldRegistry.Profile(output.GetPrefab())?.Id != intent.Receipt.Type)

                throw new InvalidOperationException("Master Idol output does not match the admitted placement.");

            output.Set(MasterIdolWorldRegistry.TokenKey, intent.Receipt.Token); intent.Output = output.m_uid;

        }

        internal static void Tried(Piece piece, bool success)

        { if (Current?.Entered == true && Current.Piece == piece) Current.Completed = success; }

        internal static bool Complete(Player player)

        {

            var intent = Current;

            if (intent == null || intent.Player != player || !intent.Entered || !intent.Completed || intent.Output == ZDOID.None || intent.Finished) return true;

            intent.Finished = true;

            intent.Saved = MasterIdolJournal.Record(player, intent.Receipt, 3, intent.Output);

            if (!intent.Saved) return false;

            MasterIdolTiming.Finish("placement.native-to-paid",intent.NativeAt);

            Send(3, intent.Receipt); return true;

        }

        internal static string NativeAttempt(Player player) => MasterIdolReceiptBoundary.Capture(Current?.Receipt.Token,

            Current?.Entered == true, Current != null && Current.Player == player && Current.Session == ZNet.instance);

        internal static void NativeCompleted(string attempt, Player player, Exception error)

        {

            if (MasterIdolReceiptBoundary.CanComplete(attempt, Current?.Receipt.Token, Current?.Entered == true, Current?.Completed == true, error != null) &&

                Current.Player == player && Current.Session == ZNet.instance && !WorkshopRemoteCraft.Executing && !Complete(player))

                MasteryPlugin.Log.LogError("[MasterIdols] Owner receipt uncertain; no output acknowledgment, hold retained.");

        }

        internal static string WorkshopAttempt(object state, Player player) => MasterIdolReceiptBoundary.Capture(Current?.Receipt.Token,

            Current?.Entered == true, Current != null && Current.Player == player && Current.Session == ZNet.instance &&

                Traverse.Create(state).Field("BuildPiece").GetValue<Piece>() == Current.Piece);

        internal static void WorkshopCompleted(string attempt, object state, Player player)

        {

            if (!MasterIdolReceiptBoundary.CanComplete(attempt, Current?.Receipt.Token, Current?.Entered == true, Current?.Completed == true, false) ||

                Current.Finished || Current.Player != player ||

                Current.Session != ZNet.instance || Traverse.Create(state).Field("BuildPiece").GetValue<Piece>() != Current.Piece) return;

            var fields = Traverse.Create(state);

            if (!fields.Field("Paid").GetValue<bool>() || !fields.Field("BuildCompleted").GetValue<bool>()) return;

            string id = fields.Field("Id").GetValue<string>();

            WorkshopRecovery.Record(id, WorkshopReceiptPhase.Committed);

            if (Complete(player)) return;

            fields.Field("BuildCompleted").SetValue(false); // Preserve existing Workshop uncertain-output branch; never refund a created idol.

            WorkshopRecovery.Record(id, WorkshopReceiptPhase.Uncertain);

            throw new InvalidOperationException("Master Idol owner receipt uncertain; Workshop escrow retained.");

        }

        internal static bool ServerHold(WorkshopActor actor, Piece piece, Vector3 position)

        {

            var profile = MasterIdolPieces.Profile(piece); if (profile == null) return true;

            if (actor == null || MasterIdolWorldRegistry.Store?.Ready != true || !GoldCraftingService.Enabled ||

                !GoldWorldReady() || !GoldCraftingService.ServerLedger.Get(actor.GetPlayerID()).Unlocked) return false;

            foreach (var entry in MasterIdolWorldRegistry.Store.Records)

                if (entry.Type == profile.Id && entry.Player == actor.GetPlayerID() && entry.Issued && !entry.Closed && !entry.Applied &&

                    Live.TryGetValue(entry.Token, out var live) && LiveMatches(live, actor) &&

                    Vector3.Distance(position, new Vector3(entry.X, entry.Y, entry.Z)) <= .05f) return true;

            return false;

        }

        private static bool LiveMatches(LiveGrant live, WorkshopActor actor) => live != null && actor != null &&

            live.Session == ZNet.instance && live.World == ZNet.instance.GetWorldUID() && live.Rpc == actor.Rpc &&

            live.Character == actor.CharacterId && live.Player == actor.GetPlayerID();

        private static void Send(int kind, MasterIdolReceipt receipt, string nonce = "")

        {

            try

            {

                var net = ZNet.instance; if (net == null) return;

                var packet = new ZPackage(); packet.Write(kind); packet.Write(net.GetWorldUID());

                if (receipt != null)

                {

                    packet.Write(receipt.Player); packet.Write(receipt.Token); packet.Write(receipt.Type);

                    packet.Write(nonce); packet.Write(receipt.Output);

                    packet.Write(Current?.Receipt == receipt ? Current.Position : Vector3.zero);

                }

                if (net.IsServer()) { packet.SetPos(0); Receive(null, packet); }

                else net.GetServerRPC()?.Invoke("VM_Idol_Request_v1", packet);

            }

            catch (Exception error) { MasteryPlugin.Log.LogWarning("[MasterIdols] Transport deferred: " + error.Message); }

        }

        private static void Respond(ZRpc rpc, int kind, string token, string nonce = "", string message = "")

        {

            var packet = new ZPackage(); packet.Write(kind); packet.Write(ZNet.instance.GetWorldUID());

            packet.Write(token ?? ""); packet.Write(nonce); packet.Write(message);

            if (rpc == null) { packet.SetPos(0); Reply(null, packet); } else rpc.Invoke("VM_Idol_Reply_v1", packet);

        }

        private static void Receive(ZRpc rpc, ZPackage packet)

        {

            var net = ZNet.instance;

            if (net?.IsServer() != true || packet == null || packet.Size() > 1024) return;

            try

            {

                byte[] requestBytes=packet.GetArray();int kind = packet.ReadInt(); if (packet.ReadLong() != net.GetWorldUID()) return;

                var actor = WorkshopActor.Resolve(rpc); if (actor == null || !actor.Available) return;

                MasterIdolWorldRegistry.Refresh();

                if (kind == 0) { Respond(rpc, 7, ""); return; } // paired preflight capability

                long player = packet.ReadLong(); string token = packet.ReadString(), type = packet.ReadString(), nonce = packet.ReadString();

                ZDOID output = packet.ReadZDOID(); Vector3 point = packet.ReadVector3();

                if (player != actor.GetPlayerID() || !Guid.TryParseExact(token, "N", out _) || MasterIdolProfiles.Find(type) == null || nonce.Length > 32) return;

                var store = MasterIdolWorldRegistry.Store; if (store?.Ready != true) { Respond(rpc, kind==5?-5:-1, token, "", "Пам’ять цього світу не приймає нових творінь. Ідол ще не постане."); return; }

                var old = store.Find(token);

                if (kind == 3)

                {

                    if(old?.Applied==true && old.Closed && old.Player==player && old.Type==type && old.Output==output.ToString())

                        Respond(rpc,2,token); // exact terminal output may already have been dismantled; never rebind or recreate.

                    else if (MasterIdolWorldRegistry.Confirm(token, player, output))

                        Respond(rpc, 2, token);

                    return;

                }

                if (kind == 4)

                { if (old?.Closed == true && old.Player == player && old.Type==type && (old.Applied||MasterIdolFavor.Cancel(old))) Respond(rpc, 2, token); return; }

                if(kind==2&&PendingRequests.TryGetValue(token,out var cancellation)&&LiveMatches(cancellation.Context,actor)&&nonce.Length==0){cancellation.Cancel=true;return;}

                if (kind == 2)

                {

                    if (!Live.TryGetValue(token, out var live) || !LiveMatches(live, actor) || live.Nonce != nonce ||

                        MasterIdolWorldRegistry.HasTokenObject(token)) return;

                    if (store.RevokeIssued(token, player) && MasterIdolFavor.Cancel(store.Find(token))) Respond(rpc, 2, token);

                    return;

                }

                if (kind == 1 && old != null && old.Player == player && old.Type == type && old.Issued && !old.Closed && !old.Applied &&

                    Live.TryGetValue(token, out var replay) && LiveMatches(replay, actor) &&

                    Vector3.Distance(point, new Vector3(old.X, old.Y, old.Z)) <= .05f)

                { Respond(rpc, 1, token, replay.Nonce); return; }

                if(kind==1&&PendingRequests.TryGetValue(token,out var pending))

                {

                    if(!PendingContext(pending)||!LiveMatches(pending.Context,actor)||pending.Type!=type||(pending.Point-point).sqrMagnitude>.0025f)return;

                    if(pending.Cancel)return;

                    string invalid=AdmissionDenial(kind,old,rpc,actor,player,point,type,true);

                    if(invalid!=null){pending.Cancel=true;return;}

                    CompletePending(token,pending,actor);return;

                }

                string denial=AdmissionDenial(kind,old,rpc,actor,player,point,type);

                if(denial!=null)

                {

                    if(MasteryPlugin.Settings.UIDebugLogging.Value)MasteryPlugin.Log.LogWarning("[MasterIdols] placement denied="+denial+"; "+MasterIdolWorldIndex.Diagnostics());

                    string text=denial=="favor"?"Вьолундр просить 250 прихильності за це творіння.":denial=="workbench"?"Обране місце лежить поза будівельним радіусом верстака.":

                        denial=="ward"?"Оберіг не дозволяє створити ідола на цій землі.":

                        denial=="outstanding"?"Доля попереднього творіння ще не визначена. Новий ідол поки не постане.":

                        denial=="occupied"?"Пам’ять цього світу поки не приймає нових творінь.":

                        denial=="skill"?"Вьолундр довіряє ці ідоли лише майстрам сотого рівня ремесла.":

                        denial=="range"?"Обране місце надто далеко. Підійди ближче.":

                        "Благословення Вьолундра ще не готове. Зачекай мить.";

                    Respond(rpc,kind==5?-5:-1,token,"",text);return;

                }

                if(kind==5){Respond(rpc,5,token);return;} // read-only check; actual kind1 rechecks and persists

                if(PendingRequests.Count>=128)return;

                var request=new PendingRequest{Context=new LiveGrant{Session=net,Rpc=rpc,Character=actor.CharacterId,Player=player,World=net.GetWorldUID()},Type=type,Point=point,Packet=requestBytes,Until=Time.unscaledTime+15,Store=store,Ledger=CurrentLedger,Manager=ZDOMan.instance,Generation=MasterIdolLoadIdentity.Generation};

                PendingRequests[token]=request;CompletePending(token,request,actor);



            }

            catch (Exception error) { MasteryPlugin.Log.LogWarning("[MasterIdols] Request rejected: " + error.Message); }

        }

        private static bool PendingContext(PendingRequest request)=>ReferenceEquals(request.Store,MasterIdolWorldRegistry.Store)&&ReferenceEquals(request.Ledger,CurrentLedger)&&ReferenceEquals(request.Manager,ZDOMan.instance)&&request.Generation==MasterIdolLoadIdentity.Generation&&request.Context.Session==ZNet.instance&&request.Context.World==ZNet.instance?.GetWorldUID();

        private static void CompletePending(string token,PendingRequest request,WorkshopActor actor)

        {

            var store=MasterIdolWorldRegistry.Store;if(store?.Ready!=true)return;

            if(!store.ReserveIssued(new MasterIdolAdmission{Token=token,Type=request.Type,Player=request.Context.Player,RequiresFavor=true,X=request.Point.x,Y=request.Point.y,Z=request.Point.z}))

            {if(!store.Busy&&!store.LoadDeferred){request.Cancel=true;}return;}

            // Only committed Issued is visible here. Live grants cannot be used as an async staging area.

            if(!MasterIdolFavor.Buy(request.Context.Player,token,request.Type)){request.Cancel=true;return;}

            request.Context.Nonce=Guid.NewGuid().ToString("N");Live[token]=request.Context;PendingRequests.Remove(token);Respond(request.Context.Rpc,1,token,request.Context.Nonce);

        }

        private static void ProcessPending()

        {

            if(ZNet.instance?.IsServer()!=true||Time.unscaledTime<NextPending)return;NextPending=Time.unscaledTime+.1f;PendingIds.Clear();foreach(string id in PendingRequests.Keys)PendingIds.Add(id);

            int budget=Math.Min(16,PendingIds.Count);for(int i=0;i<budget;i++){string token=PendingIds[PendingCursor++%PendingIds.Count];if(!PendingRequests.TryGetValue(token,out var request))continue;var actor=WorkshopActor.Resolve(request.Context.Rpc);if(!PendingContext(request)||actor==null||!actor.Available||!LiveMatches(request.Context,actor)){if(Time.unscaledTime>=request.Until)PendingRequests.Remove(token);continue;}

                if(Time.unscaledTime>=request.Until)request.Cancel=true;

                if(request.Cancel){var store=MasterIdolWorldRegistry.Store;if(store?.Ready!=true||store.Busy||MasterIdolWorldRegistry.HasTokenObject(token))continue;var entry=store.Find(token);if(entry==null){PendingRequests.Remove(token);Respond(request.Context.Rpc,-1,token,"","Творіння не постало. Обери місце знову.");continue;}if(store.RevokeIssued(token,entry.Player)&&CurrentLedger!=null&&(CurrentLedger.FindIdol(request.Context.World,entry.Player,token,entry.Type)==null||MasterIdolFavor.Cancel(store.Find(token)))){PendingRequests.Remove(token);Respond(request.Context.Rpc,-1,token,"","Творіння не постало. Обери місце знову.");}continue;}

                Receive(request.Context.Rpc,new ZPackage(request.Packet));

            }

        }

        private static string AdmissionDenial(int kind,MasterIdolAdmission old,ZRpc rpc,WorkshopActor actor,long player,Vector3 point,string type,bool pending=false)

        {

            if((kind!=1 && kind!=5)||old!=null&&!pending)return "request";

            if(!GoldWorldReady())return "gold-ready";

            if(!GoldCraftingService.ServerLedger.Get(player).Unlocked)return "unlock";

            if((!pending||GoldCraftingService.ServerLedger.FindIdol(ZNet.instance.GetWorldUID(),player,old?.Token??"",type)?.Phase!=GoldIdolPhase.Paid)&&GoldCraftingService.ServerLedger.Get(player).Favor-GoldCraftingService.ServerLedger.Get(player).Wallets[GoldCooldownPolicy.LegacyPatronId].Held<250f)return "favor";

            if(actor.IsDead()||actor.IsTeleporting())return "actor";

            if(!(rpc==null?OwnerSkillAuthority.Has(Player.m_localPlayer,Skills.SkillType.Crafting,100):OwnerSkillAuthority.Has(rpc,actor.CharacterId,player,Skills.SkillType.Crafting,100)))return "skill";

            if(!Finite(point)||Vector3.Distance(actor.EyePoint,point)>actor.PlaceDistance+1f)return "range";

            if(!MasterIdolWorldIndex.HasWorkbench(point))return "workbench";

            if(!MasterIdolWorldIndex.WardAllows(player,point))return "ward";

            if(!pending&&MasterIdolWorldRegistry.Store.Outstanding(player))return "outstanding";

            if(!pending&&!MasterIdolWorldRegistry.CanReserve(type))return "occupied";

            return null;

        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&

            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

        private static void Reply(ZRpc rpc, ZPackage packet)

        {

            var net = ZNet.instance;

            if (net == null || packet == null || packet.Size() > 1024 || (net.IsServer() ? rpc != null : rpc != net.GetServerRPC())) return;

            try

            {

                byte[] requestBytes=packet.GetArray();int kind = packet.ReadInt(); if (packet.ReadLong() != net.GetWorldUID()) return;

                string token = packet.ReadString(), nonce = packet.ReadString(), reason = packet.ReadString();

                if (kind == 7) { Supported = true; LegacyPeer=false; ProtocolRpc = rpc; return; }

                if(kind==0||kind==6){Supported=false;LegacyPeer=true;return;} // old hello is not preflight support

                var player = Player.m_localPlayer;

                var pending=Current;

                if((kind==5 || kind==-5) && pending?.Preflight==true && pending.Receipt.Token==token && pending.Session==net && pending.Player==player)

                {

                    if(kind==-5){pending.Cancelled=true;Current=null;Tell(player,reason);return;}

                    var previous=MasterIdolJournal.Read(player);

                    if(!Context(pending,player,pending.Piece) || (previous!=null && previous.Phase!=5) || WorkshopRemoteCraft.ClientBusy || WorkshopRecovery.Outstanding)

                    {CancelLive();return;}

                    pending.Preflight=false;

                    if(!MasterIdolJournal.Record(player,pending.Receipt,1,ZDOID.None))

                    {pending.Cancelled=true;Tell(player,"Вьолундр не зміг закарбувати твоє творіння. Ідол ще не постане.");return;}

                    Send(1,pending.Receipt);return;

                }

                var receipt = MasterIdolJournal.Read(player);

                if (receipt == null || receipt.Token != token) return;

                if (kind == 2)

                {

                    if (receipt.Phase != 3 && receipt.Phase != 4) return;

                    if (MasterIdolJournal.SettleAcknowledged(player, receipt) && Current?.Receipt.Token == token) Current = null;

                    return;

                }

                var intent = Current; if (intent == null || intent.Receipt.Token != token || intent.Session != net || intent.Player != player || intent.Entered || intent.Preflight) return;

                if (kind == -1)

                {

                    intent.Cancelled = true;

                    if (MasterIdolJournal.Record(player, intent.Receipt, 5, ZDOID.None)) Current = null;

                    Tell(player, reason); return;

                }

                if (kind != 1 || !Guid.TryParseExact(nonce, "N", out _)) return;

                MasterIdolTiming.Finish("placement.request-to-grant",intent.RequestedAt);

                intent.Nonce = nonce;

                if (intent.Cancelled) { if (intent.CancelSaved) Send(2, intent.Receipt, nonce); return; }

                if (!Context(intent, player, intent.Piece)) { CancelLive(); return; }

                if (!intent.Granted) intent.AutoPending = true; intent.Granted = true;

            }

            catch (Exception error) { MasteryPlugin.Log.LogWarning("[MasterIdols] Reply ignored: " + error.Message); }

        }

    }

}

