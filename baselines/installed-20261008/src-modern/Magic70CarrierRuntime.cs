using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static partial class Magic70Carrier
    {
        private const string CastRpc = "VM_Torbjorn70Cast";
        private const string ResultRpc = "VM_Torbjorn70Result";
        private const string CloseRpc = "VM_Torbjorn70CargoClosed";
        private const float InteractionRange = 5f;
        private const float RefreshInterval = 20f;
        private const float FollowInterval = .5f;
        // InventoryGui displays Inventory.GetName(), not Container.m_name.
        private static readonly FieldInfo CargoInventoryName = AccessTools.Field(typeof(Inventory), "m_name");

        private sealed class CastRequest
        {
            internal string Token;
            internal ZRpc Rpc;
            internal WorkshopActor Actor;
            internal long Author;
        }

        private sealed class CastResult
        {
            internal long Author;
            internal ZRpc Rpc;
            internal bool Granted;
            internal ZDOID Carrier;
        }

        private sealed class PendingClose
        {
            internal ZDOID Cargo;
            internal long Owner;
            internal long Author;
            internal uint Revision;
            internal Vector3 LastActorPosition;
            internal float Expires;
        }

        private sealed class CargoInventoryTag { internal Container Container; }
        private static readonly ConditionalWeakTable<Inventory, CargoInventoryTag> CargoInventories =
            new ConditionalWeakTable<Inventory, CargoInventoryTag>();
        private static readonly Queue<CastRequest> Requests = new Queue<CastRequest>();
        private static readonly Dictionary<string, CastRequest> Seen = new Dictionary<string, CastRequest>();
        private static readonly Dictionary<string, CastResult> Results = new Dictionary<string, CastResult>();
        private static readonly HashSet<long> PendingAuthors = new HashSet<long>();
        private static readonly Dictionary<ZDOID, PendingClose> PendingCloses = new Dictionary<ZDOID, PendingClose>();
        private static readonly Dictionary<ZRpc, float> PeerNextRequest = new Dictionary<ZRpc, float>();
        private static readonly List<ZDO> CarrierRecords = new List<ZDO>();
        private static readonly List<ZDO> CargoRecords = new List<ZDO>();
        private static readonly List<ZDO> SkeletonRecords = new List<ZDO>();
        private static readonly List<ZDO> ArcherRecords = new List<ZDO>();
        private static readonly Dictionary<long, List<ZDO>> CarriersByAuthor = new Dictionary<long, List<ZDO>>();
        private static readonly Dictionary<long, List<ZDO>> CargoByAuthor = new Dictionary<long, List<ZDO>>();
        private static readonly Dictionary<long, List<ZDO>> SkeletonsByAuthor = new Dictionary<long, List<ZDO>>();
        private static ZNet Session;
        private static CastRequest Active;
        private static string LocalPending;
        private static float LocalPendingSince;
        private static float LocalPendingCost;
        private static int ScanPhase, ScanCursor;
        private static bool IndexReady;
        private static float NextRefresh, NextFollow;

        internal static void Register(ZRpc rpc)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (rpc == null) return;
            rpc.Register<string>(CastRpc, ReceiveCast);
            rpc.Register<string, ZDOID, bool>(ResultRpc, ReceiveResult);
            rpc.Register<ZPackage>(CloseRpc, ReceiveClose);
#endif
        }

        internal static void Tick()
        {
#if MASTERY_CARRIER70_EXPERIMENT
            ZNet net = ZNet.instance;
            EnsureSession(net);
            if (net == null) return;
            if (!net.IsServer())
            {
                if (LocalPending != null && Time.time - LocalPendingSince > 20f)
                {
                    // A missing reply is uncertain; do not refund or replay a cast
                    // that may already have created its persistent carrier.
                    MasteryPlugin.Log.LogWarning("[Torbjorn70] cast reply timeout stage=no-server-result token=" + LocalPending + "; server handler/reply unavailable or outcome unknown; charge retained.");
                    LocalPending = null;
                    LocalPendingCost = 0f;
                }
                return;
            }

            ProcessPendingCloses();
            if (!IndexReady || Time.time >= NextRefresh)
            {
                if (IndexReady) ClearWorldIndex();
                IndexReady = false;
                if (!StepWorldIndex()) return;
            }
            FollowClosedCargo();
            if (Active == null && Requests.Count > 0) Active = Requests.Dequeue();
            if (Active != null && IndexReady)
            {
                CastRequest request = Active; Active = null;
                ProcessCast(request);
            }
#endif
        }

        private static void EnsureSession(ZNet net)
        {
            if (Session == net) return;
            Session = net;
            Requests.Clear(); Seen.Clear(); Results.Clear(); PendingAuthors.Clear(); PendingCloses.Clear(); PeerNextRequest.Clear();
            ClearWorldIndex();
            Active = null; LocalPending = null; LocalPendingCost = 0f; ScanPhase = ScanCursor = 0;
            IndexReady = false; NextRefresh = NextFollow = 0f;
        }

        internal static bool Input(Player player)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (!MagicSkillPassives.OwnerReady(player)) return true;
            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != "StaffSkeleton") return true;
            // At quality1 the carrier occupies the only native skeleton slot.
            // Refuse the ordinary cast before its health/eitr payment, instead
            // of spawning a skeleton which native slot enforcement must remove.
            if ((player.m_attack || player.m_attackHold) && weapon.m_quality <= 1 && OwnedSlotCount(player) > 0) return false;
            // Existing cargo/slot ownership survives skill loss after death;
            // only a NEW carrier cast requires the level70 unlock.
            if (!PerkRuntimeService.HasPerk(player, Skills.SkillType.BloodMagic, 70)) return true;
            if (!player.m_secondaryAttack && !player.m_secondaryAttackHold) return true;
            if (!player.m_secondaryAttack) return false;
            EnsureSession(ZNet.instance);
            Attack primary = weapon.m_shared?.m_attack;
            float cost = primary != null ? primary.m_attackEitr : 0f;
            if (LocalPending != null || Skeleton35Travel.PortalRecoveryPending || player.InAttack() || player.IsStaggering() || player.IsTeleporting() ||
                player.m_dodgeInvincible || player.InMinorAction() || player.m_blocking ||
                !float.IsFinite(cost) || cost <= 0f || !player.HaveEitr(cost)) return false;

            LocalPending = Guid.NewGuid().ToString("N");
            LocalPendingSince = Time.time;
            LocalPendingCost = cost;
            player.UseEitr(cost);
            OwnerSkillAuthority.SendNow();
            Magic70CastAnimation.Play(player);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Torbjorn70] cast request sent token=" + LocalPending + " actor=" + player.GetZDOID() + " server=" + (ZNet.instance?.IsServer() == true));
            if (ZNet.instance?.IsServer() == true)
                EnqueueCast(null, WorkshopActor.Resolve(null), LocalPending);
            else
                ZNet.instance?.GetServerRPC()?.Invoke(CastRpc, LocalPending);
            return false;
#else
            return true;
#endif
        }

        private static void ReceiveCast(ZRpc rpc, string token)
        {
            if (ZNet.instance?.IsServer() != true || rpc == null) return;
            EnsureSession(ZNet.instance);
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Torbjorn70] cast request arrived token=" +
                    (token != null && token.Length <= 32 ? token : "invalid-length-" + (token?.Length ?? 0)) +
                    " peer=" + ZNet.instance.GetPeer(rpc)?.m_uid + " actor=" + (actor != null ? actor.CharacterId.ToString() : "unresolved"));
            if (PeerNextRequest.TryGetValue(rpc, out float next) && Time.time < next)
            {
                MasteryPlugin.Log.LogWarning("[Torbjorn70] cast rejected before queue stage=peer-rate-limit token=" + token);
                if (Guid.TryParseExact(token, "N", out _)) rpc.Invoke(ResultRpc, token, ZDOID.None, false);
                return;
            }
            PeerNextRequest[rpc] = Time.time + 1f;
            if (ZNet.instance.GetPeer(rpc)?.IsReady() != true || actor == null)
            {
                MasteryPlugin.Log.LogWarning("[Torbjorn70] cast rejected before queue: peer/actor not ready.");
                if (Guid.TryParseExact(token, "N", out _)) rpc.Invoke(ResultRpc, token, ZDOID.None, false);
                return;
            }
            EnqueueCast(rpc, actor, token);
        }

        private static void EnqueueCast(ZRpc rpc, WorkshopActor actor, string token)
        {
            if (actor == null || !Guid.TryParseExact(token, "N", out _)) return;
            if (Seen.TryGetValue(token, out CastRequest old))
            {
                if (old.Author == actor.GetPlayerID() && old.Rpc == rpc && Results.TryGetValue(token, out CastResult prior))
                    Reply(old, prior.Granted, prior.Carrier);
                return;
            }
            long author = actor.GetPlayerID();
            if (Seen.Count >= 4096 || Requests.Count >= 8 || PendingAuthors.Contains(author))
            {
                MasteryPlugin.Log.LogWarning("[Torbjorn70] cast rejected before queue stage=" +
                    (Seen.Count >= 4096 ? "receipt-limit" : Requests.Count >= 8 ? "queue-full" : "author-pending") +
                    " author=" + author + " token=" + token);
                Reply(new CastRequest { Token = token, Rpc = rpc, Actor = actor, Author = author }, false, ZDOID.None);
                return;
            }
            CastRequest request = new CastRequest { Token = token, Rpc = rpc, Actor = actor, Author = author };
            Seen.Add(token, request);
            PendingAuthors.Add(author);
            Requests.Enqueue(request);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Torbjorn70] cast queued author=" + author + " token=" + token + " character=" + actor.CharacterId);
            // Admission must use a fresh server snapshot, not the periodic UI/follow index.
            BeginFreshWorldIndex();
        }

        private static void ClearWorldIndex()
        {
            CarrierRecords.Clear(); CargoRecords.Clear(); SkeletonRecords.Clear(); ArcherRecords.Clear();
            CarriersByAuthor.Clear(); CargoByAuthor.Clear(); SkeletonsByAuthor.Clear();
        }

        private static void BeginFreshWorldIndex()
        {
            ClearWorldIndex();
            IndexReady = false; ScanPhase = 0; ScanCursor = 0;
        }

        private static bool StepWorldIndex()
        {
            if (ZDOMan.instance == null || ZNetScene.instance == null) return false;
            if (ScanPhase == 0)
            {
                if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabName, CarrierRecords, ref ScanCursor)) return false;
                ScanPhase = 1; ScanCursor = 0;
                return false;
            }
            if (ScanPhase == 1)
            {
                if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(CargoPrefabName, CargoRecords, ref ScanCursor)) return false;
                ScanPhase = 2; ScanCursor = 0;
                return false;
            }
            if (ScanPhase == 2)
            {
                if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("Skeleton_Friendly", SkeletonRecords, ref ScanCursor)) return false;
                ScanPhase = 3; ScanCursor = 0;
                return false;
            }
            if (ScanPhase == 3)
            {
                if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative("Skeleton_Friendly_Archer", ArcherRecords, ref ScanCursor)) return false;
                BuildIndexes();
                ScanPhase = 0; ScanCursor = 0; IndexReady = true; NextRefresh = Time.time + RefreshInterval;
                return true;
            }
            return false;
        }

        private static void BuildIndexes()
        {
            CarriersByAuthor.Clear(); CargoByAuthor.Clear();
            foreach (ZDO data in CarrierRecords)
                if (data != null && data.GetPrefab() == PrefabName.GetStableHashCode())
                    AddByAuthor(CarriersByAuthor, data.GetLong(AuthorKey, 0L), data);
            foreach (ZDO data in CargoRecords)
                if (data != null && data.GetPrefab() == CargoPrefabName.GetStableHashCode())
                    AddByAuthor(CargoByAuthor, data.GetLong(CargoAuthorKey, 0L), data);
            int skeletonHash = "Skeleton_Friendly".GetStableHashCode();
            foreach (ZDO data in SkeletonRecords)
                if (data != null && data.GetPrefab() == skeletonHash)
                    AddByAuthor(SkeletonsByAuthor, data.GetLong(AuthorKey, 0L), data);
            int archerHash = "Skeleton_Friendly_Archer".GetStableHashCode();
            foreach (ZDO data in ArcherRecords)
                if (data != null && data.GetPrefab() == archerHash)
                    AddByAuthor(SkeletonsByAuthor, data.GetLong(AuthorKey, 0L), data);
        }

        private static int CountCanonicalOwnedSkeletons(long author)
        {
            int count = 0;
            List<ZDO> records = Find(SkeletonsByAuthor, author);
            if (records == null) return 0;
            foreach (ZDO data in records)
                if (IsCurrent(data) && data.GetLong(AuthorKey, 0L) == author && data.GetFloat("health", 1f) > 0f)
                    count++;
            return count;
        }

        private static void AddByAuthor(Dictionary<long, List<ZDO>> map, long author, ZDO data)
        {
            if (author == 0) return;
            if (!map.TryGetValue(author, out List<ZDO> list)) map[author] = list = new List<ZDO>();
            if (!list.Contains(data)) list.Add(data);
        }

        private static void ProcessCast(CastRequest request)
        {
            if (request == null) return;
            WorkshopActor actor = request.Actor;
            ZNetPeer peer = request.Rpc != null ? ZNet.instance.GetPeer(request.Rpc) : null;
            ZDO caster = actor.Available ? ZDOMan.instance?.GetZDO(actor.CharacterId) : null;
            int staffHash = "StaffSkeleton".GetStableHashCode();
            Player localCaster = request.Rpc == null ? Player.m_localPlayer : null;
            bool exactLocalActor = localCaster != null && localCaster.GetZDOID() == actor.CharacterId &&
                localCaster.GetPlayerID() == request.Author;
            ItemDrop.ItemData localStaff = exactLocalActor ? localCaster.GetCurrentWeapon() : null;
            bool mastery = exactLocalActor
                ? PerkRuntimeService.HasPerk(localCaster, Skills.SkillType.BloodMagic, 70)
                : request.Rpc != null && OwnerSkillAuthority.Has(request.Rpc, actor.CharacterId, request.Author, Skills.SkillType.BloodMagic, 70);
            if (!actor.Available || actor.IsDead() || actor.IsTeleporting() ||
                (request.Rpc != null && peer?.IsReady() != true))
            { Reject(request, "actor-unavailable-or-state"); return; }
            if (!mastery)
            { Reject(request, "mastery-not-authoritative"); return; }
            int staffQuality = exactLocalActor && localStaff != null
                ? localStaff.m_quality
                : Magic70Carrier.GetEquippedStaffQuality(caster, staffHash);
            bool staffEquipped = exactLocalActor
                ? localStaff?.m_dropPrefab != null && localStaff.m_dropPrefab.name == "StaffSkeleton"
                : Magic70Carrier.HasEquippedStaff(caster, staffHash) && staffQuality > 0;
            if (!staffEquipped)
            {
                int observed = caster?.GetInt(ZDOVars.s_rightItem, 0) ?? 0;
                Reject(request, "staff-not-current observedZdo=" + observed + " expected=" + staffHash +
                    " hostNative=" + exactLocalActor + " localPrefab=" + localStaff?.m_dropPrefab?.name);
                return;
            }

            List<ZDO> authoredCarriers = Find(CarriersByAuthor, request.Author);
            int living = 0;
            if (authoredCarriers != null)
                foreach (ZDO data in authoredCarriers)
                    if (IsCurrent(data) && data.GetFloat("health", 1f) > 0f) living++;
            if (living > 1 || living == 1)
            { Reject(request, "living-carrier-exists"); return; }

            int ownedSkeletons = CountCanonicalOwnedSkeletons(request.Author);
            if (!BloodSkeleton35Service.CanAdmitCarrier(request.Author, staffQuality, ownedSkeletons))
            { Reject(request, "native-slot-full quality=" + staffQuality + " owned=" + ownedSkeletons); return; }

            List<ZDO> authoredStores = Find(CargoByAuthor, request.Author);
            if (authoredStores != null) authoredStores.RemoveAll(data => !IsCurrent(data));
            if (authoredStores != null && authoredStores.Count > 1)
            { Reject(request, "multiple-cargo-records"); return; }
            ZDO cargo = authoredStores != null && authoredStores.Count == 1 ? authoredStores[0] : null;
            if (cargo != null && (cargo.GetPrefab() != CargoPrefabName.GetStableHashCode() ||
                cargo.GetLong(CargoAuthorKey, 0L) != request.Author || !CanReclaimCargo(cargo)))
            { Reject(request, "cargo-owned-or-invalid"); return; }

            Quaternion rotation = caster.GetRotation();
            Vector3 forward = rotation * Vector3.forward;
            Vector3 proposal = actor.Position + forward * 1.8f;
            Vector3 point = proposal;
            if (Physics.Raycast(proposal + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f,
                LayerMask.GetMask("Default", "static_solid", "piece", "terrain")))
            {
                if (ground.normal.y < .5f || Vector3.Distance(actor.Position, ground.point) > 6f)
                { Reject(request, "unsafe-ground"); return; }
                point = ground.point + Vector3.up * .1f;
            }
            else point += Vector3.up * .1f;

            GameObject carrierPrefab = ZNetScene.instance.GetPrefab(PrefabName);
            if (carrierPrefab == null) { Reject(request, "carrier-prefab-missing"); return; }
            GameObject carrierObject = UnityEngine.Object.Instantiate(carrierPrefab, point, rotation);
            Character carrier = carrierObject != null ? carrierObject.GetComponent<Character>() : null;
            ZDO carrierData = carrier?.m_nview?.IsValid() == true ? carrier.m_nview.GetZDO() : null;
            if (carrier == null || carrierData == null)
            {
                if (carrierObject != null) UnityEngine.Object.Destroy(carrierObject);
                Reject(request, "carrier-instance-invalid"); return;
            }
            carrierData.Set(AuthorKey, request.Author);
            carrierData.Set(BirthKey, ZNet.instance.GetTime().Ticks);
            carrier.SetTamed(true);
            Tameable tame = carrier.GetComponent<Tameable>();
            Player localOwner = Player.GetPlayer(request.Author);
            if (localOwner != null) tame?.Command(localOwner, false);
            else if (peer != null)
            {
                carrierData.SetOwner(peer.m_uid);
                ZDOMan.instance.ForceSendZDO(carrierData.m_uid);
            }

            if (cargo == null)
            {
                GameObject cargoPrefab = ZNetScene.instance.GetPrefab(CargoPrefabName);
                if (cargoPrefab == null) { UnityEngine.Object.Destroy(carrierObject); Reject(request, "cargo-prefab-missing"); return; }
                GameObject cargoObject = UnityEngine.Object.Instantiate(cargoPrefab, point, rotation);
                Container container = cargoObject != null ? cargoObject.GetComponent<Container>() : null;
                cargo = container?.m_nview?.IsValid() == true ? container.m_nview.GetZDO() : null;
                if (container == null || cargo == null)
                { if (cargoObject != null) UnityEngine.Object.Destroy(cargoObject); UnityEngine.Object.Destroy(carrierObject); Reject(request, "cargo-instance-invalid"); return; }
                cargo.Set(CargoAuthorKey, request.Author);
                container.m_privacy = Container.PrivacySetting.Private;
                container.m_checkGuardStone = false;
                RegisterCargoInventory(container);
                HideCargo(container);
            }
            cargo.Set(CargoCarrierKey, carrierData.m_uid);
            cargo.SetPosition(point);
            carrierData.Set(CargoKey, cargo.m_uid);
            AddByAuthor(CarriersByAuthor, request.Author, carrierData);
            AddByAuthor(CargoByAuthor, request.Author, cargo);
            RecordOwnerLink(request.Author, carrierData.m_uid);
            Complete(request, true, carrierData.m_uid);
        }

        private static bool CanReclaimCargo(ZDO cargo)
        {
            if (cargo.GetOwner() == ZNet.GetUID()) return true;
            ZNetPeer owner = ZNet.instance?.GetPeer(cargo.GetOwner());
            if (owner != null && owner.IsReady()) return false;
            cargo.SetOwner(ZNet.GetUID());
            ZDOMan.instance?.ForceSendZDO(cargo.m_uid);
            return true;
        }

        private static void Reject(CastRequest request, string stage)
        {
            MasteryPlugin.Log.LogWarning("[Torbjorn70] cast rejected stage=" + stage + " author=" + request.Author + " token=" + request.Token);
            Complete(request, false, ZDOID.None);
        }

        private static bool IsCurrent(ZDO data) => data != null &&
            ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data);

        private static List<ZDO> Find(Dictionary<long, List<ZDO>> map, long author) =>
            map.TryGetValue(author, out List<ZDO> items) ? items : null;

        private static void Complete(CastRequest request, bool granted, ZDOID carrier)
        {
            PendingAuthors.Remove(request.Author);
            Reply(request, granted, carrier);
        }

        private static void Reply(CastRequest request, bool granted, ZDOID carrier)
        {
            Results[request.Token] = new CastResult { Author = request.Author, Rpc = request.Rpc, Granted = granted, Carrier = carrier };
            if (request.Rpc != null) request.Rpc.Invoke(ResultRpc, request.Token, carrier, granted);
            else FinishClient(request.Token, carrier, granted);
        }

        private static void ReceiveResult(ZRpc rpc, string token, ZDOID carrier, bool granted)
        {
            if (ZNet.instance?.IsServer() != false || rpc != ZNet.instance.GetServerRPC()) return;
            FinishClient(token, carrier, granted);
        }

        private static void FinishClient(string token, ZDOID carrier, bool granted)
        {
            if (token != LocalPending) return;
            Player player = Player.m_localPlayer;
            float cost = LocalPendingCost;
            LocalPending = null; LocalPendingCost = 0f;
            if (!granted)
            {
                MasteryPlugin.Log.LogWarning("[Torbjorn70] server denied carrier request token=" + token);
                if (cost > 0f) player?.AddEitr(cost);
                return;
            }
            RecordOwnerLink(player, carrier);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Torbjorn70] carrier admitted id=" + carrier + " owner=" + (player != null ? player.GetPlayerID().ToString() : "?"));
        }

        internal static void RecordOwnerLink(long author, ZDOID carrierId)
        {
            if (author == 0 || carrierId.IsNone()) return;
            Player player = Player.GetPlayer(author);
            if (player != null) RecordOwnerLink(player, carrierId);
        }

        internal static bool IsCargo(Container container) => container != null &&
            (container.GetComponent<Magic70CarrierCargoMarker>() != null ||
             (container.m_nview?.IsValid() == true && container.m_nview.GetZDO()?.GetPrefab() == CargoPrefabName.GetStableHashCode()));

        internal static bool IsCargoInventory(Inventory inventory) => inventory != null && CargoInventories.TryGetValue(inventory, out _);

        internal static bool CanAdd(Inventory inventory, ItemDrop.ItemData item) =>
            !IsCargoInventory(inventory) || IsAllowedCargo(item);

        internal static bool CanAdd(Inventory inventory, GameObject item)
        {
            if (!IsCargoInventory(inventory)) return true;
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            return drop != null && IsAllowedCargo(drop.m_itemData);
        }

        internal static void RegisterCargoInventory(Container container)
        {
            if (container == null || !IsCargo(container)) return;
            container.m_name = "Шлунок Торби";
            ZDO cargoData = container.m_nview?.GetZDO();
            if (cargoData != null) cargoData.Persistent = true;
            Inventory inventory = container.GetInventory();
            if (inventory == null) return;
            if (inventory.GetName() != "Шлунок Торби") CargoInventoryName.SetValue(inventory, "Шлунок Торби");
            CargoInventories.Remove(inventory);
            CargoInventories.Add(inventory, new CargoInventoryTag { Container = container });
        }

        private static void HideCargo(Container container)
        {
            if (container == null) return;
            foreach (Renderer renderer in container.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in container.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        internal static bool OpenFromCarrier(Character carrier, Humanoid user)
        {
            if (!IsCarrier(carrier)) return false;
            Player player = user as Player;
            ZDO carrierData = carrier.m_nview.GetZDO();
            long author = carrierData.GetLong(AuthorKey, 0L);
            if (player == null || player != Player.m_localPlayer || author == 0 || player.GetPlayerID() != author)
            {
                if (player != null) player.Message(MessageHud.MessageType.Center, "Торбйорн носить речі лише для свого власника.");
                return true;
            }
            ZDOID cargoId = carrierData.GetZDOID(CargoKey);
            Container container = cargoId.IsNone() ? null : ZNetScene.instance?.FindInstance(cargoId)?.GetComponent<Container>();
            if (container == null || !IsCargo(container))
            {
                player.Message(MessageHud.MessageType.Center, "Торбйорн ще не має доступної скрині. Спробуй пізніше.");
                return true;
            }
            RegisterCargoInventory(container);
            HideCargo(container);
            ZDO cargoData = container.m_nview.GetZDO();
            if (cargoData.GetInt(ZDOVars.s_inUse, 0) == 0)
            {
                // Opening native containers does not synchronously refresh their
                // managed Inventory. Read canonical bytes before displaying a
                // relocated proxy; Load suppresses Inventory.Save callbacks.
                container.Load();
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                {
                    byte[] payload = cargoData.GetByteArray(ZDOVars.s_items, null);
                    MasteryPlugin.Log.LogInfo("[Torbjorn70] cargo-open carrier=" + carrierData.m_uid +
                        " cargo=" + cargoData.m_uid + " itemBytes=" + (payload?.Length ?? 0) +
                        " loadedItems=" + container.GetInventory().NrOfItems());
                }
            }
            container.Interact(player, false, false);
            return true;
        }

        internal static bool AllowCargoAccess(Container container, long playerId)
        {
            if (!IsCargo(container)) return true;
            ZDO data = container.m_nview?.IsValid() == true ? container.m_nview.GetZDO() : null;
            return data != null && playerId != 0 && data.GetLong(CargoAuthorKey, 0L) == playerId;
        }

        internal static bool AllowCargoRpc(Container container, long senderId, long suppliedPlayerId)
        {
            if (!IsCargo(container)) return true;
            ZNet net = ZNet.instance;
            ZDO cargo = container.m_nview?.IsValid() == true ? container.m_nview.GetZDO() : null;
            if (net == null || cargo == null) return false;
            long author = cargo.GetLong(CargoAuthorKey, 0L);
            if (author == 0 || suppliedPlayerId != author) return false;
            ZNetPeer peer = net.GetPeer(senderId);
            ZDO actor;
            if (peer != null) actor = OwnerSkillAuthority.ResolveCharacterData(peer);
            // Native container ownership transfers to the opener. Requests
            // issued by that same owner then resolve locally, not through a
            // peer entry for ourselves (which does not exist on a client).
            else if (senderId == ZNet.GetUID() && Player.m_localPlayer?.m_nview?.IsValid() == true)
                actor = Player.m_localPlayer.m_nview.GetZDO();
            else return false;
            if (actor == null || actor.GetLong(ZDOVars.s_playerID, 0L) != author) return false;
            ZDO carrier = FindCarrierForCargo(cargo, author);
            if (carrier == null || carrier.GetFloat("health", 1f) <= 0f || carrier.GetLong(AuthorKey, 0L) != author ||
                carrier.GetZDOID(CargoKey) != cargo.m_uid) return false;
            return (actor.GetPosition() - carrier.GetPosition()).sqrMagnitude <= InteractionRange * InteractionRange;
        }

        private static ZDO FindCarrierForCargo(ZDO cargo, long author)
        {
            if (cargo == null) return null;
            ZDOID linked = cargo.GetZDOID(CargoCarrierKey);
            ZDO found = linked.IsNone() ? null : ZDOMan.instance?.GetZDO(linked);
            if (IsLinkedCarrier(found, cargo, author)) return found;
            foreach (Character character in Character.GetAllCharacters())
            {
                if (!IsCarrier(character) || character.IsDead()) continue;
                ZDO data = character.m_nview.GetZDO();
                if (IsLinkedCarrier(data, cargo, author)) return data;
            }
            List<ZDO> indexed = Find(CarriersByAuthor, author);
            if (indexed != null)
                foreach (ZDO candidate in indexed)
                    if (IsLinkedCarrier(candidate, cargo, author)) return candidate;
            return null;
        }

        private static bool IsLinkedCarrier(ZDO carrier, ZDO cargo, long author) =>
            carrier != null && IsCurrent(carrier) && carrier.GetPrefab() == PrefabName.GetStableHashCode() &&
            carrier.GetLong(AuthorKey, 0L) == author && carrier.GetFloat("health", 1f) > 0f &&
            carrier.GetZDOID(CargoKey) == cargo.m_uid;

        internal static void AfterContainerClosed(Container container)
        {
            if (!IsCargo(container) || container.m_nview?.IsValid() != true) return;
            ZDO cargo = container.m_nview.GetZDO();
            uint revision = cargo.DataRevision;
            ZNet net = ZNet.instance;
            if (net == null) return;
            if (net.IsServer())
            {
                Player local = Player.m_localPlayer;
                if (local != null && AllowCargoAccess(container, local.GetPlayerID()))
                    ReleaseCargo(cargo, ZNet.GetUID(), local.GetPlayerID(), revision, local.transform.position);
                return;
            }
            ZPackage packet = new ZPackage(); packet.Write(cargo.m_uid); packet.Write(revision);
            net.GetServerRPC()?.Invoke(CloseRpc, packet);
        }

        private static void ReceiveClose(ZRpc rpc, ZPackage packet)
        {
            if (ZNet.instance?.IsServer() != true || rpc == null || packet == null || packet.Size() > 64) return;
            ZDOID id; uint revision;
            try { packet.SetPos(0); id = packet.ReadZDOID(); revision = packet.ReadUInt(); }
            catch { return; }
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            ZDO cargo = ZDOMan.instance?.GetZDO(id);
            if (actor == null || peer?.IsReady() != true || cargo == null ||
                cargo.GetPrefab() != CargoPrefabName.GetStableHashCode() || cargo.GetLong(CargoAuthorKey, 0L) != actor.GetPlayerID() ||
                cargo.GetOwner() != peer.m_uid) return;
            ZDO carrier = FindCarrierForCargo(cargo, actor.GetPlayerID());
            if (carrier != null && (actor.Position - carrier.GetPosition()).sqrMagnitude > InteractionRange * InteractionRange) return;
            PendingCloses[id] = new PendingClose { Cargo = id, Owner = peer.m_uid, Author = actor.GetPlayerID(),
                Revision = revision, LastActorPosition = actor.Position, Expires = Time.time + 5f };
            ProcessPendingCloses();
        }

        private static void ProcessPendingCloses()
        {
            if (PendingCloses.Count == 0) return;
            var expired = new List<ZDOID>();
            foreach (var pair in PendingCloses)
            {
                PendingClose close = pair.Value;
                ZDO cargo = ZDOMan.instance?.GetZDO(close.Cargo);
                if (cargo == null || cargo.GetOwner() != close.Owner) { expired.Add(pair.Key); continue; }
                if (RevisionReached(cargo.DataRevision, close.Revision))
                {
                    ReleaseCargo(cargo, close.Owner, close.Author, close.Revision, close.LastActorPosition);
                    expired.Add(pair.Key);
                }
                else if (Time.time > close.Expires)
                {
                    // Never take ownership while the last vanilla Inventory.Save
                    // revision is still in flight; leaving it owned is safer than
                    // overwriting an accepted player's recent cargo changes.
                    if (MasteryPlugin.Settings.VerboseLogging.Value)
                        MasteryPlugin.Log.LogWarning("[Torbjorn70] cargo close deferred; ZDO revision did not arrive id=" + close.Cargo);
                    expired.Add(pair.Key);
                }
            }
            foreach (ZDOID id in expired) PendingCloses.Remove(id);
        }

        private static bool RevisionReached(uint current, uint expected) => unchecked((int)(current - expected)) >= 0;

        private static void ReleaseCargo(ZDO cargo, long previousOwner, long author, uint revision, Vector3 fallbackPosition)
        {
            if (cargo == null || cargo.GetPrefab() != CargoPrefabName.GetStableHashCode() ||
                cargo.GetLong(CargoAuthorKey, 0L) != author || cargo.GetOwner() != previousOwner ||
                !RevisionReached(cargo.DataRevision, revision)) return;
            ZDO carrier = FindCarrierForCargo(cargo, author);
            Vector3 destination = carrier != null ? carrier.GetPosition() : fallbackPosition;
            cargo.SetOwner(ZNet.GetUID());
            cargo.SetPosition(destination);
            ZDOMan.instance?.ForceSendZDO(cargo.m_uid);
            Container loaded = ZNetScene.instance?.FindInstance(cargo.m_uid)?.GetComponent<Container>();
            if (loaded != null)
            {
                loaded.Load();
                loaded.transform.position = destination;
                loaded.GetComponent<ZSyncTransform>()?.SyncNow();
                RegisterCargoInventory(loaded);
            }
        }

        private static void FollowClosedCargo()
        {
            if (Time.time < NextFollow || ZNet.instance?.IsServer() != true) return;
            NextFollow = Time.time + FollowInterval;
            foreach (ZDO cargo in CargoRecords)
            {
                if (!IsCurrent(cargo) || cargo.GetPrefab() != CargoPrefabName.GetStableHashCode()) continue;
                long author = cargo.GetLong(CargoAuthorKey, 0L);
                ZDO carrier = FindCarrierForCargo(cargo, author);
                if (author == 0 || carrier == null || carrier.GetFloat("health", 1f) <= 0f) continue;
                if (cargo.GetOwner() != ZNet.GetUID())
                {
                    if (ZNet.instance.GetPeer(cargo.GetOwner()) == null)
                    {
                        cargo.SetOwner(ZNet.GetUID());
                        ZDOMan.instance.ForceSendZDO(cargo.m_uid);
                    }
                    else continue;
                }
                cargo.Set(CargoCarrierKey, carrier.m_uid);
                Vector3 destination = carrier.GetPosition();
                if ((cargo.GetPosition() - destination).sqrMagnitude < .25f) continue;
                cargo.SetPosition(destination);
                Container loaded = ZNetScene.instance?.FindInstance(cargo.m_uid)?.GetComponent<Container>();
                if (loaded != null)
                {
                    loaded.transform.position = destination;
                    loaded.GetComponent<ZSyncTransform>()?.SyncNow();
                }
            }
        }

        internal static int GetNrOfInstancesWithCarrierSlot(GameObject prefab, Vector3 point, float radius,
            bool procreation, bool eventCreatures, SpawnAbility ability)
        {
            int count = SpawnSystem.GetNrOfInstances(prefab, point, radius, procreation, eventCreatures);
            if (ability?.m_owner is Player owner && IsOrdinarySkeletonPrefab(prefab) && OwnedSlotCount(owner) > 0)
                return count + 1;
            return count;
        }

        private static bool IsOrdinarySkeletonPrefab(GameObject prefab) => prefab != null &&
            (prefab.name.StartsWith("Skeleton_Friendly", StringComparison.Ordinal));
    }

    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Magic70CarrierInputPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return Magic70Carrier.Input(__instance);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Magic70CarrierAttackGuardPatch
    {
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (!Magic70Carrier.IsCarrier(__instance as Character)) return true;
            __result = false;
            return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class Magic70CarrierInteractPatch
    {
        private static bool Prefix(Tameable __instance, Humanoid user, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            Character carrier = __instance != null ? __instance.GetComponent<Character>() : null;
            if (!Magic70Carrier.IsCarrier(carrier)) return true;
            __result = Magic70Carrier.OpenFromCarrier(carrier, user);
            return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Tameable), "UnsummonMaxInstances")]
    internal static class Magic70CarrierNativeSkeletonLimitPatch
    {
        private static void Prefix(Tameable __instance, ref int maxInstances)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            Character creature = __instance?.GetComponent<Character>();
            ZDO data = creature?.m_nview?.GetZDO();
            int prefab = data?.GetPrefab() ?? 0;
            if (maxInstances <= 0 || (prefab != "Skeleton_Friendly".GetStableHashCode() &&
                prefab != "Skeleton_Friendly_Archer".GetStableHashCode())) return;
            Player owner = creature.GetComponent<MonsterAI>()?.GetFollowTarget()?.GetComponent<Player>();
            if (owner != null && data.GetLong(Magic70Carrier.AuthorKey, 0) == owner.GetPlayerID() &&
                Magic70Carrier.OwnedSlotCount(owner) > 0)
                maxInstances = Mathf.Max(0, maxInstances - 1);
#endif
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Magic70CarrierContainerAwakePatch
    {
        private static void Postfix(Container __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            Magic70Carrier.RegisterCargoInventory(__instance);
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.CheckAccess))]
    internal static class Magic70CarrierContainerAccessPatch
    {
        private static bool Prefix(Container __instance, long playerID, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (!Magic70Carrier.IsCargo(__instance)) return true;
            __result = Magic70Carrier.AllowCargoAccess(__instance, playerID);
            return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.RPC_RequestOpen))]
    internal static class Magic70CarrierOpenRpcPatch
    {
        private static bool Prefix(Container __instance, long __0, long playerID)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return Magic70Carrier.AllowCargoRpc(__instance, __0, playerID);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.RPC_RequestStack))]
    internal static class Magic70CarrierStackRpcPatch
    {
        private static bool Prefix(Container __instance, long __0, long playerID)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return Magic70Carrier.AllowCargoRpc(__instance, __0, playerID);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.RPC_RequestTakeAll))]
    internal static class Magic70CarrierTakeAllRpcPatch
    {
        private static bool Prefix(Container __instance, long __0, long playerID)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return Magic70Carrier.AllowCargoRpc(__instance, __0, playerID);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.CloseContainer))]
    internal static class Magic70CarrierCloseUiPatch
    {
        private static void Prefix(InventoryGui __instance, out Container __state) => __state = __instance?.m_currentContainer;
        private static void Postfix(Container __state)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            Magic70Carrier.AfterContainerClosed(__state);
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(ItemDrop.ItemData) })]
    internal static class Magic70CarrierAddItemPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (Magic70Carrier.CanAdd(__instance, item)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) })]
    internal static class Magic70CarrierAddItemGridPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (Magic70Carrier.CanAdd(__instance, item)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
    internal static class Magic70CarrierAddItemPositionPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (Magic70Carrier.CanAdd(__instance, item)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(GameObject), typeof(int) })]
    internal static class Magic70CarrierAddGameObjectPatch
    {
        private static bool Prefix(Inventory __instance, GameObject __0, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (Magic70Carrier.CanAdd(__instance, __0)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(int), typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class Magic70CarrierAddItemAmountPatch
    {
        private static bool Prefix(Inventory __instance, int __0, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            // Inventory.Load passes serialized ItemData WITHOUT SharedData here.
            // Validate the native prefab, not the unhydrated record. Rejecting
            // that record emptied every restored cargo inventory on reload.
            if (!Magic70Carrier.IsCargoInventory(__instance)) return true;
            if (Magic70Carrier.CanAdd(__instance, ObjectDB.instance?.GetItemPrefab(__0))) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new Type[] { typeof(ItemDrop.ItemData), typeof(int) })]
    internal static class Magic70CarrierCanAddItemPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (Magic70Carrier.CanAdd(__instance, item)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new Type[] { typeof(GameObject), typeof(int) })]
    internal static class Magic70CarrierCanAddGameObjectPatch
    {
        private static bool Prefix(Inventory __instance, GameObject __0, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (Magic70Carrier.CanAdd(__instance, __0)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.CanBeRemoved))]
    internal static class Magic70CarrierContainerRemovePatch
    {
        private static bool Prefix(Container __instance, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (!Magic70Carrier.IsCargo(__instance)) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    internal static class Magic70CarrierPieceRemovePatch
    {
        private static bool Prefix(Piece __instance, ref bool __result)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            if (__instance?.GetComponent<Magic70CarrierCargoMarker>() == null) return true;
            __result = false; return false;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
    internal static class Magic70CarrierDropPatch
    {
        private static bool Prefix(Piece __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return __instance?.GetComponent<Magic70CarrierCargoMarker>() == null;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class Magic70CarrierWearDamagePatch
    {
        private static bool Prefix(WearNTear __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return __instance?.GetComponent<Magic70CarrierCargoMarker>() == null;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Damage))]
    internal static class Magic70CarrierWearRpcDamagePatch
    {
        private static bool Prefix(WearNTear __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return __instance?.GetComponent<Magic70CarrierCargoMarker>() == null;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
    internal static class Magic70CarrierWearRemovePatch
    {
        private static bool Prefix(WearNTear __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return __instance?.GetComponent<Magic70CarrierCargoMarker>() == null;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.RPC_Remove))]
    internal static class Magic70CarrierWearRpcRemovePatch
    {
        private static bool Prefix(WearNTear __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return __instance?.GetComponent<Magic70CarrierCargoMarker>() == null;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy), new Type[] { typeof(HitData), typeof(bool) })]
    internal static class Magic70CarrierWearDestroyPatch
    {
        private static bool Prefix(WearNTear __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return __instance?.GetComponent<Magic70CarrierCargoMarker>() == null;
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.OnDestroyed))]
    internal static class Magic70CarrierContainerDestroyedPatch
    {
        private static bool Prefix(Container __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return !Magic70Carrier.IsCargo(__instance);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), "DropAllItems", new Type[] { })]
    internal static class Magic70CarrierContainerDropPatch
    {
        private static bool Prefix(Container __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return !Magic70Carrier.IsCargo(__instance);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.DropAllItems), new Type[] { typeof(GameObject) })]
    internal static class Magic70CarrierContainerDropLootPatch
    {
        private static bool Prefix(Container __instance)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            return !Magic70Carrier.IsCargo(__instance);
#else
            return true;
#endif
        }
    }

    [HarmonyPatch]
    internal static class Magic70CarrierSpawnCapPatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (Type nested in typeof(SpawnAbility).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (nested.Name.Contains("<Spawn>") && AccessTools.Method(nested, "MoveNext") is MethodBase method)
                    return method;
            return null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
#if MASTERY_CARRIER70_EXPERIMENT
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo native = AccessTools.Method(typeof(SpawnSystem), nameof(SpawnSystem.GetNrOfInstances),
                new[] { typeof(GameObject), typeof(Vector3), typeof(float), typeof(bool), typeof(bool) });
            MethodInfo replacement = AccessTools.Method(typeof(Magic70Carrier), nameof(Magic70Carrier.GetNrOfInstancesWithCarrierSlot),
                new[] { typeof(GameObject), typeof(Vector3), typeof(float), typeof(bool), typeof(bool), typeof(SpawnAbility) });
            int found = 0, index = -1;
            for (int i = 0; i < code.Count; i++)
                if (code[i].Calls(native)) { found++; index = i; }
            if (found != 1 || replacement == null)
            {
                MasteryPlugin.Log.LogError("[Torbjorn70] native SpawnAbility slot hook disabled; expected one prefab-count call.");
                return code;
            }
            CodeInstruction owner = new CodeInstruction(OpCodes.Ldloc_1);
            owner.labels.AddRange(code[index].labels); code[index].labels.Clear();
            owner.blocks.AddRange(code[index].blocks); code[index].blocks.Clear();
            code.Insert(index, owner);
            code[index + 1].operand = replacement;
            return code;
#else
            return instructions;
#endif
        }
    }
}
