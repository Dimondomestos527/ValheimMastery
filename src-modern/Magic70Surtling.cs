using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal static class Magic70Surtling
    {
        internal const string PrefabName = "VM_MuspelSurtling70";
        internal const float Cost = 80f;
        internal const string AuthorKey = "vm.skeleton.master";
        internal const string LootAuthorKey = "vm.surtling.author";
        internal const string BirthTimeKey = "vm.surtling.born";
        internal const string QualityKey = "vm.muspel.quality";
        internal const string SummonSound = "sfx_dverger_fireball_rain_shot";
        internal const string DeathTimeKey = "vm.surtling.died";
        internal const string DeathNoticeKey = "vm.surtling.death_notice";
        internal const string DeathCooldownId = "magic70_surtling_death";
        private const string DeathRpc = "VM_MuspelSurtlingDeath";
        private const string MoveRpc = "VM_MuspelRecallMove";
        private const string MoveResultRpc = "VM_MuspelRecallResult";
        private sealed class Request { internal string Id; internal int Quality; internal float Skill; internal long Author; internal ZRpc Rpc; internal WorkshopActor Actor; internal Vector3 Point; }
        private sealed class Recall { internal Request Request; internal ZDOID Id; internal long Owner; internal float Expires; }
        private sealed class DeathReceipt { internal Player Target; internal long Sender, Stamp; internal float Expires; }
        private sealed class LocalBinding { internal long Author, Owner, SeenAt; internal float NextCapture; }
        private static readonly Queue<Request> Requests = new Queue<Request>();
        private static readonly Dictionary<string, Request> Seen = new Dictionary<string, Request>();
        private static readonly Dictionary<string, bool> Results = new Dictionary<string, bool>();
        private static readonly Dictionary<string, float> ResultRefunds = new Dictionary<string, float>();
        private static readonly Dictionary<string, Recall> Recalls = new Dictionary<string, Recall>();
        private static readonly List<ZDO> Found = new List<ZDO>();
        private static readonly Dictionary<ZDOID, DeathReceipt> DeathReceipts = new Dictionary<ZDOID, DeathReceipt>();
        private static readonly Dictionary<ZDOID, LocalBinding> LocalBindings = new Dictionary<ZDOID, LocalBinding>();
        private static readonly List<ZDOID> ReceiptIds = new List<ZDOID>();
        private static readonly List<ZDOID> BindingIds = new List<ZDOID>();
        private static Request Active;
        private static int Scan;
        private static string Pending;
        private static float PendingSince;
        private static float PendingCost;
        private static ZNet Session;
        // Clamp native appearance; saved staff quality independently controls stats.
        internal static int NativeLevelForQuality(int quality) => SurtlingQualityRules.VisualLevel(quality);
        internal static int SummonQuality(Character creature)
        {
            int saved = creature.m_nview?.GetZDO()?.GetInt(QualityKey, 0) ?? 0;
            return saved > 0 ? saved : SurtlingQualityRules.LegacyQuality(
                creature.m_nview?.GetZDO()?.GetInt(ZDOVars.s_level, creature.GetLevel()) ?? creature.GetLevel());
        }
        internal static void ApplyTierAppearance(Character creature)
        {
            if (!HasSummonAuthor(creature)) return;
            GameObject visual = creature.GetVisual();
            if (visual == null) return;
            int level = NativeLevelForQuality(SummonQuality(creature));
            foreach (LevelEffects effects in visual.GetComponentsInChildren<LevelEffects>(true))
            {
                // Initialize the native character binding without manually calling
                // Start, which would duplicate its level-event subscription.
                effects.m_character = creature;
                effects.SetupLevelVisualization(level);
            }
        }
        internal static float SkillFactor(float skill) => 1f + .01f * Mathf.Clamp(skill, 0f, 100f);
        internal static bool IsCompanion(Character character) => character != null && character.name.StartsWith(PrefabName, StringComparison.Ordinal);
        internal static void Register(ZRpc rpc)
        {
            rpc.Register<string, int, float, Vector3>("VM_MuspelCastV2", Receive);
            rpc.Register<string, bool, float>("VM_MuspelResult", Result);
            rpc.Register<ZPackage>(MoveRpc, MoveRequest);
            rpc.Register<string, ZDOID, bool>(MoveResultRpc, MoveResult);
        }
        internal static void RegisterPlayer(Player player)
        {
            ZNetView view = player?.m_nview;
            if (view?.IsValid() != true) return;
            view.Register<ZDOID, long>(DeathRpc, (sender, sourceId, stamp) => QueueDeathReceipt(player, sender, sourceId, stamp));
        }
        internal static void RegisterPrefab(ZNetScene scene)
        {
            NativeSoftVisualAssets.GetPrefab(SummonSound);
            if (scene == null || scene.m_namedPrefabs.ContainsKey(PrefabName.GetStableHashCode())) return;
            GameObject native = scene.GetPrefab("Surtling");
            if (native == null) return;
            GameObject staging = new GameObject("VM_InactiveSurtlingTemplate"); staging.SetActive(false);
            GameObject prefab = UnityEngine.Object.Instantiate(native, staging.transform, false); prefab.name = PrefabName;
            Character character = prefab.GetComponent<Character>();
            character.m_name = "Жарик"; character.m_faction = Character.Faction.Players; character.m_health = 120f;
            CharacterDrop drops = prefab.GetComponent<CharacterDrop>(); if (drops != null) drops.m_dropsEnabled = false;
            Tameable tame = prefab.GetComponent<Tameable>() ?? prefab.AddComponent<Tameable>();
            tame.m_startsTamed = true; tame.m_commandable = true; tame.m_unsummonDistance = 0f;
            tame.m_unsummonOnOwnerLogoutSeconds = 0f;
            prefab.GetComponent<ZNetView>().m_persistent = true;
            prefab.GetComponent<MonsterAI>().m_attackPlayerObjects = false;
            prefab.AddComponent<Magic70SurtlingLife>();
            prefab.AddComponent<Magic70SurtlingBirthVisual>();
            scene.m_namedPrefabs.Add(PrefabName.GetStableHashCode(), prefab); scene.m_prefabs.Add(prefab);
        }
        internal static bool Input(Player player)
        {
            if (!MagicSkillPassives.OwnerReady(player) || !FireStaff35Charge.IsStaff(player.GetCurrentWeapon()) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.ElementalMagic, 70)) return true;
            if (!player.m_secondaryAttack && !player.m_secondaryAttackHold) return true;
            EnsureSession(); FireStaff35Charge.Cancel();
            player.m_queuedSecondAttackTimer = 0f;
            float eitrCost = PerkCooldownStateService.GetRemainingSeconds(player, DeathCooldownId) > 0d ? Cost * 2f : Cost;
            if (!player.m_secondaryAttack || Pending != null || player.InAttack() || player.IsStaggering() ||
                player.IsTeleporting() || player.m_dodgeInvincible || player.InMinorAction() || player.m_blocking || !player.HaveEitr(eitrCost)) return false;
            Vector3 proposal = player.transform.position + player.transform.forward * 2f;
            if (!Physics.Raycast(proposal + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f,
                LayerMask.GetMask("Default", "static_solid", "piece", "terrain")) || ground.normal.y < .5f ||
                Vector3.Distance(ground.point, player.transform.position) > 6f) return false;
            proposal = ground.point + Vector3.up * .1f;
            Pending = Guid.NewGuid().ToString("N"); PendingSince = Time.time; PendingCost = eitrCost;
            player.UseEitr(eitrCost);
            Magic70CastAnimation.Play(player);
            int quality = player.GetCurrentWeapon().m_quality;
            float skill = PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.ElementalMagic);
            OwnerSkillAuthority.SendNow();
            if (ZNet.instance.IsServer()) Admit(null, WorkshopActor.Resolve(null), Pending, quality, skill, proposal);
            else ZNet.instance.GetServerRPC()?.Invoke("VM_MuspelCastV2", Pending, quality, skill, proposal);
            return false;
        }
        private static void Receive(ZRpc rpc, string id, int quality, float skill, Vector3 point)
        {
            if (ZNet.instance?.IsServer() != true) return;
            EnsureSession();
            ZNetPeer peer = ZNet.instance.GetPeer(rpc);
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (peer?.IsReady() != true || actor == null)
            {
                if (Guid.TryParseExact(id, "N", out _)) rpc.Invoke("VM_MuspelResult", id, false, 0f);
                return;
            }
            Admit(rpc, actor, id, quality, skill, point);
        }
        private static void Admit(ZRpc rpc, WorkshopActor actor, string id, int quality, float skill, Vector3 point)
        {
            if (actor == null || !Guid.TryParseExact(id, "N", out _) || !OwnerSkillAuthority.Valid(skill) || skill < 70f || quality < 1) return;
            if (Seen.TryGetValue(id, out Request prior))
            {
                if (prior.Author == actor.GetPlayerID() && prior.Rpc == rpc && Results.TryGetValue(id, out bool result))
                    Reply(prior, result, ResultRefunds.TryGetValue(id, out float refund) ? refund : 0f);
                return;
            }
            Request request = new Request { Id = id, Rpc = rpc, Author = actor.GetPlayerID(), Quality = quality, Skill = skill, Actor = actor, Point = point };
            ZDO character = ZDOMan.instance?.GetZDO(actor.CharacterId);
            bool mastery = rpc == null ? PerkRuntimeService.HasPerk(Player.m_localPlayer, Skills.SkillType.ElementalMagic, 70) :
                OwnerSkillAuthority.Has(rpc, actor.CharacterId, request.Author, Skills.SkillType.ElementalMagic, 70);
            if (!actor.Available || actor.IsDead() || actor.IsTeleporting() || !mastery ||
                !float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z) ||
                Vector3.Distance(point, actor.Position) > 6f ||
                character?.GetInt(ZDOVars.s_rightItem, 0) != "StaffFireball".GetStableHashCode() ||
                character.GetInt(ZDOVars.s_rightItemQuality, 1) != quality || Requests.Count >= 8)
            { Reply(request, false); return; }
            foreach (Request queued in Requests) if (queued.Author == request.Author) { Reply(request, false); return; }
            if (Active?.Author == request.Author) { Reply(request, false); return; }
            foreach (Recall recall in Recalls.Values) if (recall.Request.Author == request.Author) { Reply(request, false); return; }
            // Never evict replay identities during a live session: after the
            // companion dies, replaying an evicted success could summon for free.
            if (Seen.Count >= 4096) { Reply(request, false); return; }
            Seen[id] = request; Requests.Enqueue(request);
        }
        private static void EnsureSession()
        {
            ZNet net = ZNet.instance;
            if (Session != net)
            { Requests.Clear(); Seen.Clear(); Results.Clear(); ResultRefunds.Clear(); Recalls.Clear(); Found.Clear(); DeathReceipts.Clear(); LocalBindings.Clear(); BindingIds.Clear(); Active = null; Pending = null; PendingCost = 0f; Session = net; }
        }
        internal static void BeforeNativeDeath(Character creature)
        {
            if (creature?.m_nview?.IsValid() != true || !creature.m_nview.IsOwner() || creature.GetHealth() > 0f) return;
            ZDO source = creature.m_nview.GetZDO();
            if (source == null || source.GetPrefab() != PrefabName.GetStableHashCode() ||
                source.GetBool(DeathNoticeKey, false)) return;
            long author = source.GetLong(LootAuthorKey, source.GetLong(AuthorKey, 0));
            if (author == 0) return;
            long stamp = ZNet.instance.GetTime().Ticks;
            source.Set(DeathTimeKey, stamp);
            source.Set(DeathNoticeKey, true);
            Player summoner = Player.GetPlayer(author);
            ZNetView targetView = summoner?.m_nview;
            if (summoner == null || targetView?.IsValid() != true) return;
            ZDO targetData = targetView.GetZDO();
            if (targetData == null || targetData.GetLong(ZDOVars.s_playerID, 0) != author) return;
            ZDOID sourceId = source.m_uid;
            if (targetView.IsOwner() && source.GetOwner() == ZNet.GetUID())
                QueueDeathReceipt(summoner, ZNet.GetUID(), sourceId, stamp);
            else
            {
                long owner = targetData.GetOwner();
                if (owner != 0) targetView.InvokeRPC(owner, DeathRpc, sourceId, stamp);
            }
        }
        private static void QueueDeathReceipt(Player target, long sender, ZDOID sourceId, long stamp)
        {
            if (target?.m_nview?.IsValid() != true || !target.m_nview.IsOwner() ||
                target != Player.m_localPlayer || target.GetPlayerID() == 0 || DeathReceipts.Count >= 64) return;
            if (!DeathReceipts.ContainsKey(sourceId))
                DeathReceipts[sourceId] = new DeathReceipt { Target = target, Sender = sender, Stamp = stamp, Expires = Time.time + 2.5f };
            ProcessDeathReceipts();
        }
        private static void ProcessDeathReceipts()
        {
            if (ZNet.instance == null) return;
            long now = ZNet.instance.GetTime().Ticks;
            PruneLocalBindings(now);
            if (DeathReceipts.Count == 0) return;
            ReceiptIds.Clear();
            foreach (ZDOID id in DeathReceipts.Keys) ReceiptIds.Add(id);
            foreach (ZDOID id in ReceiptIds)
            {
                if (!DeathReceipts.TryGetValue(id, out DeathReceipt receipt)) continue;
                Player target = receipt.Target;
                if (target?.m_nview?.IsValid() != true || !target.m_nview.IsOwner() || target != Player.m_localPlayer ||
                    Time.time > receipt.Expires) { DeathReceipts.Remove(id); continue; }
                ZDO source = ZDOMan.instance?.GetZDO(id);
                if (source == null)
                {
                    if (ApplyCachedDeathReceipt(id, target, receipt.Sender, receipt.Stamp, now)) DeathReceipts.Remove(id);
                    continue;
                }
                if (source.GetOwner() != receipt.Sender || source.GetPrefab() != PrefabName.GetStableHashCode() ||
                    source.GetLong(LootAuthorKey, source.GetLong(AuthorKey, 0)) != target.GetPlayerID())
                { DeathReceipts.Remove(id); continue; }
                long died = source.GetLong(DeathTimeKey, 0);
                if (died == 0 || died != receipt.Stamp || !source.GetBool(DeathNoticeKey, false) ||
                    source.GetFloat("health", 1f) > 0f) continue; // wait for the authoritative death fields
                long age = now - died;
                if (age < -TimeSpan.TicksPerSecond || age > TimeSpan.TicksPerSecond * 12L)
                { DeathReceipts.Remove(id); continue; }
                ApplyDeathCooldown(target, age);
                DeathReceipts.Remove(id);
            }
            ReceiptIds.Clear();
        }
        private static bool ApplyCachedDeathReceipt(ZDOID id, Player target, long sender, long stamp, long now)
        {
            if (!LocalBindings.TryGetValue(id, out LocalBinding binding) || binding.Author != target.GetPlayerID() ||
                binding.Owner != sender || now < binding.SeenAt - TimeSpan.TicksPerSecond ||
                now - binding.SeenAt > TimeSpan.TicksPerSecond * 5L) return false;
            long age = now - stamp;
            if (age < -TimeSpan.TicksPerSecond || age > TimeSpan.TicksPerSecond * 10L ||
                stamp < binding.SeenAt - TimeSpan.TicksPerSecond) return false;
            ApplyDeathCooldown(target, age);
            LocalBindings.Remove(id);
            return true;
        }
        private static void ApplyDeathCooldown(Player target, long ageTicks)
        {
            double remaining = 10d - ageTicks / (double)TimeSpan.TicksPerSecond;
            if (remaining > 0d && !PerkCooldownStateService.TryConsume(target, DeathCooldownId, remaining) &&
                PerkCooldownStateService.GetRemainingSeconds(target, DeathCooldownId) < remaining)
            {
                double expiry = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d + remaining;
                target.m_customData["valheim_mastery.cooldown." + DeathCooldownId] = expiry.ToString("R", CultureInfo.InvariantCulture);
            }
        }
        internal static void Tick()
        {
            EnsureSession();
            ZNet net = ZNet.instance;
            if (net == null) return;
            ProcessDeathReceipts();
            if (!net.IsServer())
            {
                // Uncertain response is not automatically refunded or replayed: a lost result
                // must not create a free second summon. The server's world scan prevents duplicates.
                if (Pending != null && Time.time - PendingSince > 20f) { Pending = null; PendingCost = 0f; }
                return;
            }
            ExpireRecalls();
            if (Active == null && Requests.Count > 0) { Active = Requests.Dequeue(); Found.Clear(); Scan = 0; }
            if (Active == null || ZDOMan.instance == null) return;
            if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabName, Found, ref Scan)) return;
            Request request = Active; Active = null;
            WorkshopActor actor = request.Actor;
            ZDO casterData = ZDOMan.instance.GetZDO(actor.CharacterId);
            if (!actor.Available || actor.IsDead() || actor.IsTeleporting() ||
                (request.Rpc != null && ZNet.instance.GetPeer(request.Rpc)?.IsReady() != true) ||
                casterData?.GetInt(ZDOVars.s_rightItem, 0) != "StaffFireball".GetStableHashCode() ||
                casterData.GetInt(ZDOVars.s_rightItemQuality, 1) != request.Quality ||
                (request.Rpc != null && !OwnerSkillAuthority.Has(request.Rpc, actor.CharacterId, request.Author, Skills.SkillType.ElementalMagic, 70)))
            { Reply(request, false); return; }
            ZDO existing = null;
            foreach (ZDO minion in Found)
                if (minion.GetLong(AuthorKey, 0) == request.Author && minion.GetLong(LootAuthorKey, request.Author) == request.Author && minion.GetFloat("health", 1f) > 0f)
                { if (existing != null) { Reply(request, false); return; } existing = minion; }
            Vector3 point = request.Point;
            if (Vector3.Distance(point, actor.Position) > 6f)
            { Reply(request, false); return; }
            // A dedicated server need not render terrain around remote players.
            // Validate loaded geometry when present, otherwise use the owner's
            // native ground proposal, bounded to the authenticated character.
            if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 5f,
                LayerMask.GetMask("Default", "static_solid", "piece", "terrain")))
            {
                if (ground.normal.y < .5f || Vector3.Distance(point, ground.point) > .75f)
                { Reply(request, false); return; }
                point = ground.point + Vector3.up * .1f;
            }
            if (existing != null) { RecallExisting(request, existing, point); return; }
            GameObject prefab = ZNetScene.instance.GetPrefab(PrefabName);
            if (prefab == null) { Reply(request, false); return; }
            GameObject instance = UnityEngine.Object.Instantiate(prefab, point, Quaternion.identity);
            Character creature = instance.GetComponent<Character>(); ZDO data = creature.m_nview.GetZDO();
            data.Set(AuthorKey, request.Author); data.Set(LootAuthorKey, request.Author);
            data.Set(BirthTimeKey, ZNet.instance.GetTime().Ticks);
            data.Set("vm.muspel.skill", request.Skill);
            data.Set(QualityKey, request.Quality);
            creature.SetLevel(NativeLevelForQuality(request.Quality)); creature.SetTamed(true);
            creature.SetHealth(creature.GetMaxHealth());
            Player caster = Player.GetPlayer(request.Author);
            if (caster != null) instance.GetComponent<Tameable>().Command(caster, false);
            else
            {
                // Dedicated admission is independent of a rendered Player. The
                // creature's owner binds follow after the character loads there.
                data.SetOwner(ZNet.instance.GetPeer(request.Rpc).m_uid);
                ZDOMan.instance.ForceSendZDO(data.m_uid);
            }
            Reply(request, true);
        }
        private static void RecallExisting(Request request, ZDO data, Vector3 point)
        {
            ZDOID id = data.m_uid;
            GameObject liveObject = ZNetScene.instance?.FindInstance(id)?.gameObject;
            ZNetView live = liveObject != null ? liveObject.GetComponent<ZNetView>() : null;
            if (live == null)
            {
                // Heal on its next authoritative owner tick after native health setup.
                // Preserve identity, author and death history. The authenticated
                // recast updates its saved stats and heals on its next owner tick.
                data.Set(QualityKey, request.Quality);
                data.Set("vm.muspel.skill", request.Skill);
                data.Set("vm.muspel.recallheal", true);
                data.SetOwner(request.Rpc == null ? ZNet.GetUID() : ZNet.instance.GetPeer(request.Rpc).m_uid);
                data.SetPosition(point); ZDOMan.instance.ForceSendZDO(id);
                Reply(request, true, Cost); // Finish limits this refund to the actual surcharge.
                return;
            }
            if (live.IsOwner())
            {
                if (!MoveLoaded(live, data, id, request.Author, point, request.Quality, request.Skill)) { Reply(request, false); return; }
                Reply(request, true, Cost); return;
            }
            long owner = data.GetOwner(); ZNetPeer controller = null;
            foreach (ZNetPeer candidate in ZNet.instance.GetPeers())
                if (candidate != null && candidate.IsReady() && candidate.m_uid == owner) { controller = candidate; break; }
            if (controller?.m_rpc == null || Recalls.Count >= 64) { Reply(request, false); return; }
            Recalls[request.Id] = new Recall { Request = request, Id = id, Owner = owner, Expires = Time.time + 5f };
            ZPackage package = new ZPackage(); package.Write(request.Id); package.Write(id); package.Write(point); package.Write(request.Author);
            package.Write(request.Quality); package.Write(request.Skill);
            controller.m_rpc.Invoke(MoveRpc, package);
        }
        // Server-to-current-owner handoff for an already-instantiated minion.
        private static void MoveRequest(ZRpc rpc, ZPackage package)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer() || rpc != ZNet.instance.GetServerRPC() ||
                Player.m_localPlayer == null || package == null || package.Size() > 128) return;
            string requestId; ZDOID id; Vector3 point; long author;
            int quality = 0; float skill = 0f;
            try
            {
                requestId = package.ReadString(); id = package.ReadZDOID(); point = package.ReadVector3(); author = package.ReadLong();
                // Additive tail: old servers can still request movement/old stats.
                if (package.GetPos() < package.Size())
                { quality = package.ReadInt(); skill = package.ReadSingle();
                    if (quality < 1 || !OwnerSkillAuthority.Valid(skill) || skill < 70f || package.GetPos() != package.Size()) return; }
            }
            catch (Exception) { return; }
            ZDO data = ZDOMan.instance?.GetZDO(id);
            ZNetView live = ZNetScene.instance?.FindInstance(id)?.GetComponent<ZNetView>();
            bool moved = Guid.TryParseExact(requestId, "N", out _) && float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z) &&
                IsRecallTarget(data, id, author) && live?.IsOwner() == true && MoveLoaded(live, data, id, author, point, quality, skill);
            ZNet.instance.GetServerRPC()?.Invoke(MoveResultRpc, requestId, id, moved);
        }
        private static void MoveResult(ZRpc rpc, string requestId, ZDOID id, bool moved)
        {
            if (ZNet.instance?.IsServer() != true || !Recalls.TryGetValue(requestId, out Recall recall)) return;
            ZNetPeer peer = ZNet.instance.GetPeer(rpc); ZDO data = ZDOMan.instance?.GetZDO(id);
            if (peer == null || peer.m_uid != recall.Owner || data?.GetOwner() != recall.Owner ||
                id != recall.Id || !IsRecallTarget(data, id, recall.Request.Author)) return;
            Recalls.Remove(requestId); Reply(recall.Request, moved, moved ? Cost : 0f);
        }
        private static bool IsRecallTarget(ZDO data, ZDOID id, long author) => data != null && data.m_uid == id &&
            data.GetPrefab() == PrefabName.GetStableHashCode() && data.GetLong(AuthorKey, 0) == author &&
            data.GetLong(LootAuthorKey, author) == author && data.GetFloat("health", 1f) > 0f;
        private static bool MoveLoaded(ZNetView live, ZDO data, ZDOID id, long author, Vector3 point, int quality = 0, float skill = 0f)
        {
            if (live?.IsOwner() != true || !IsRecallTarget(data, id, author)) return false;
            Character character = live.GetComponent<Character>();
            if (character?.m_body == null) return false;
            character.m_body.linearVelocity = Vector3.zero; character.m_body.position = point;
            character.transform.position = point; live.GetComponent<ZSyncTransform>()?.SyncNow();
            if (quality > 0)
            {
                data.Set(QualityKey, quality); data.Set("vm.muspel.skill", skill);
                character.SetLevel(NativeLevelForQuality(quality));
            }
            character.SetHealth(character.GetMaxHealth());
            return true;
        }
        private static void ExpireRecalls()
        {
            if (Recalls.Count == 0) return;
            List<string> expired = null;
            foreach (var pair in Recalls) if (Time.time > pair.Value.Expires) (expired ??= new List<string>()).Add(pair.Key);
            if (expired == null) return;
            foreach (string id in expired)
            {
                Recalls.Remove(id);
                // Owner may have moved successfully but its reply was lost.
                // Do not refund an uncertain action; client uses its existing
                // bounded pending timeout, rather than allowing free recalls.
                if (MasteryPlugin.Settings.VerboseLogging.Value)
                    MasteryPlugin.Log.LogWarning("[Surtling70] recall outcome unknown token=" + id);
            }
        }
        internal static bool HasSummonAuthor(Character character)
        {
            if (!IsCompanion(character) || character.m_nview?.IsValid() != true) return false;
            ZDO data = character.m_nview.GetZDO();
            return data != null && (data.GetLong(LootAuthorKey, 0) != 0 || data.GetLong(AuthorKey, 0) != 0);
        }
        internal static void RememberLocalAuthored(Character creature)
        {
            if (!IsCompanion(creature) || creature.m_nview?.IsValid() != true || ZNet.instance == null) return;
            ZDO data = creature.m_nview.GetZDO();
            Player local = Player.m_localPlayer;
            if (data == null || data.GetPrefab() != PrefabName.GetStableHashCode() || local == null) return;
            long author = data.GetLong(LootAuthorKey, data.GetLong(AuthorKey, 0));
            if (author == 0 || author != local.GetPlayerID()) return;
            long now = ZNet.instance.GetTime().Ticks;
            PruneLocalBindings(now);
            ZDOID id = data.m_uid;
            if (LocalBindings.TryGetValue(id, out LocalBinding prior) && Time.time < prior.NextCapture) return;
            if (prior == null && LocalBindings.Count >= 8)
            {
                ZDOID oldest = default; long seenAt = long.MaxValue;
                foreach (var pair in LocalBindings) if (pair.Value.SeenAt < seenAt) { oldest = pair.Key; seenAt = pair.Value.SeenAt; }
                LocalBindings.Remove(oldest);
            }
            LocalBindings[id] = new LocalBinding { Author = author, Owner = data.GetOwner(), SeenAt = now, NextCapture = Time.time + .25f };
        }
        private static void PruneLocalBindings(long now)
        {
            BindingIds.Clear();
            foreach (var pair in LocalBindings)
                if (now < pair.Value.SeenAt - TimeSpan.TicksPerSecond || now - pair.Value.SeenAt > TimeSpan.TicksPerSecond * 5L)
                    BindingIds.Add(pair.Key);
            foreach (ZDOID id in BindingIds) LocalBindings.Remove(id);
            BindingIds.Clear();
        }
        private static void Reply(Request request, bool granted, float refund = 0f)
        {
            if (Seen.ContainsKey(request.Id)) { Results[request.Id] = granted; ResultRefunds[request.Id] = refund; }
            if (request.Rpc != null) request.Rpc.Invoke("VM_MuspelResult", request.Id, granted, refund);
            else Finish(request.Id, granted, refund);
        }
        private static void Result(ZRpc rpc, string id, bool granted, float refund)
        { if (ZNet.instance?.IsServer() == false && rpc == ZNet.instance.GetServerRPC()) Finish(id, granted, refund); }
        private static void Finish(string id, bool granted, float refund)
        {
            if (id != Pending) return;
            float charged = PendingCost;
            Pending = null;
            PendingCost = 0f;
            if (!granted && charged > 0f) Player.m_localPlayer?.AddEitr(charged);
            else if (granted)
            {
                if (refund > 0f) Player.m_localPlayer?.AddEitr(Mathf.Clamp(refund, 0f, Mathf.Max(0f, charged - Cost)));
                // Recall replies carry Cost as their surcharge-refund allowance; fresh
                // births use their observer-local cue instead of a duplicate ACK sound.
                if (refund >= Cost && Player.m_localPlayer != null)
                    PerkAudioService.Play("magic70_surtling_recall", SummonSound, Player.m_localPlayer.GetCenterPoint(), .5f, .55f);
            }
        }
    }
    internal sealed class Magic70SurtlingLife : MonoBehaviour
    {
        private Character _character;
        private MonsterAI _ai;
        private int _appliedQuality;
        private float _appliedSkill = -1f;
        private readonly Collider[] _hits = new Collider[96];
        private readonly HashSet<Character> _seen = new HashSet<Character>();
        private void Awake()
        {
            _character = GetComponent<Character>(); _ai = GetComponent<MonsterAI>();
            _character.m_onDeath += Explode;
        }
        private void OnDestroy() { if (_character != null) _character.m_onDeath -= Explode; }
        private void Update()
        {
            if (_character.m_nview?.IsValid() != true || _character.IsDead()) return;
            Magic70Surtling.RememberLocalAuthored(_character);
            if (!_character.m_nview.IsOwner()) { SyncReplicaAppearance(); return; }
            ZDO data = _character.m_nview.GetZDO();
            if (Magic70Surtling.HasSummonAuthor(_character))
            {
                int quality = Magic70Surtling.SummonQuality(_character);
                if (data.GetInt(Magic70Surtling.QualityKey, 0) < 1)
                    data.Set(Magic70Surtling.QualityKey, quality);
                int visualLevel = Magic70Surtling.NativeLevelForQuality(quality);
                float skill = data.GetFloat("vm.muspel.skill", 0f);
                if (_character.GetLevel() != visualLevel)
                {
                    // Old native level4 saves retain their HP fraction and old
                    // quality3 stats while acquiring a supported level3 appearance.
                    float healthFraction = Mathf.Clamp01(_character.GetHealthPercentage());
                    _character.SetLevel(visualLevel);
                    _character.SetHealth(_character.GetMaxHealth() * healthFraction);
                }
                else if (_appliedQuality != quality || _appliedSkill != skill)
                    _character.SetupMaxHealth();
                _appliedQuality = quality; _appliedSkill = skill;
            }
            if (data.GetBool("vm.muspel.recallheal", false))
            {
                data.Set("vm.muspel.recallheal", false);
                _character.SetHealth(_character.GetMaxHealth());
            }
            long author = _character.m_nview.GetZDO().GetLong(Magic70Surtling.AuthorKey, 0);
            Player summoner = Player.GetPlayer(author);
            if (_ai.GetFollowTarget() == null && summoner != null)
            {
                _ai.SetFollowTarget(summoner.gameObject);
                _character.m_nview.GetZDO().Set(ZDOVars.s_follow, summoner.GetPlayerName());
            }
            if (_character.GetHealthPercentage() > .2f) return;
            Character enemy = _ai.m_targetCreature;
            if (enemy == null || enemy.IsDead() || enemy.IsPlayer() || !BaseAI.IsEnemy(_character, enemy)) return;
            if ((enemy.GetCenterPoint() - _character.GetCenterPoint()).sqrMagnitude <= 9f) _character.SetHealth(0f);
            else _ai.MoveTo(Time.deltaTime, enemy.transform.position, 1f, true);
        }
        private void SyncReplicaAppearance()
        {
            if (!Magic70Surtling.HasSummonAuthor(_character)) return;
            int level = Magic70Surtling.NativeLevelForQuality(Magic70Surtling.SummonQuality(_character));
            if (_character.GetLevel() == level) return;
            // Native levels normally stay fixed after Awake. An already-loaded
            // replica needs its local cache/visual event refreshed after migration;
            // SetLevel here would incorrectly write the non-owned ZDO.
            _character.m_level = level;
            _character.m_onLevelSet?.Invoke(level);
        }
        private void Explode()
        {
            if (_character.m_nview?.IsValid() != true || !_character.m_nview.IsOwner()) return;
            ZDO data = _character.m_nview.GetZDO(); if (data.GetBool("vm.muspel.exploded", false)) return;
            data.Set("vm.muspel.exploded", true);
            Vector3 point = _character.GetCenterPoint();
            float damage = 80f * Magic70Surtling.SkillFactor(data.GetFloat("vm.muspel.skill", 0f));
            int count = Physics.OverlapSphereNonAlloc(point, 4f, _hits, LayerMask.GetMask("character", "character_net")); _seen.Clear();
            for (int i = 0; i < count; i++)
            {
                Character enemy = _hits[i]?.GetComponentInParent<Character>(); _hits[i] = null;
                if (enemy == null || enemy == _character || !_seen.Add(enemy) || enemy.IsDead() || enemy.IsPlayer() || !BaseAI.IsEnemy(_character, enemy)) continue;
                HitData hit = new HitData { m_point = enemy.GetCenterPoint(), m_dir = (enemy.GetCenterPoint() - point).normalized,
                    m_blockable = false, m_dodgeable = true, m_staggerMultiplier = .5f, m_variant = 1270, m_skillRaiseAmount = 0f };
                hit.m_damage.m_fire = damage; hit.SetAttacker(_character); enemy.Damage(hit);
            }
            PerkNativeFeedback.PlayVfx("fx_fireball_staff_explosion", point, 1.5f, 2f);
            PerkAudioService.Play("magic70_surtling_death", "sfx_imp_fireball_explode", point, .5f, .9f);
        }
    }
    // Both CharacterDrop.OnDeath and the ragdoll route call GenerateDropList;
    // Ragdoll later passes that saved list to DropItems. Return an empty list
    // only for authored summons, leaving wild Surtlings entirely native.
    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class Magic70SurtlingLootPatch
    {
        private static bool Prefix(CharacterDrop __instance,
            ref List<KeyValuePair<GameObject, int>> __result)
        {
            Character character = __instance != null ? __instance.GetComponent<Character>() : null;
            if (!Magic70Surtling.HasSummonAuthor(character)) return true;
            __result = new List<KeyValuePair<GameObject, int>>();
            return false;
        }
    }
    // Vanilla enables the new star flames but leaves the old flames enabled.
    // Normalize only our authored companion to the exact native selected row.
    [HarmonyPatch(typeof(LevelEffects), "SetupLevelVisualization")]
    internal static class Magic70SurtlingTierVisualPatch
    {
        private static void Prefix(LevelEffects __instance, ref int level)
        {
            Character creature = __instance.m_character ?? __instance.GetComponentInParent<Character>();
            if (!Magic70Surtling.HasSummonAuthor(creature)) return;
            level = Magic70Surtling.NativeLevelForQuality(Magic70Surtling.SummonQuality(creature));
            for (int i = 0; i < __instance.m_levelSetups.Count; i++)
            {
                GameObject flames = __instance.m_levelSetups[i].m_enableObject;
                if (flames != null) flames.SetActive(i == level - 2);
            }
            // Native level1 returns before restoring the base scale.
            if (level == 1) __instance.transform.localScale = Vector3.one;
        }
        private static void Postfix(LevelEffects __instance)
        {
            Character creature = __instance.m_character;
            if (!Magic70Surtling.HasSummonAuthor(creature)) return;
            creature.GetComponent<Magic70SurtlingBirthVisual>()?.TierScaleChanged(
                __instance.transform, __instance.transform.localScale);
        }
    }
    [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
    internal static class Magic70SurtlingHudPatch
    {
        private static void Postfix(EnemyHud __instance)
        {
            foreach (var pair in __instance.m_huds)
            {
                if (!Magic70Surtling.IsCompanion(pair.Key) || pair.Value == null) continue;
                if (pair.Value.m_level2 != null) pair.Value.m_level2.gameObject.SetActive(false);
                if (pair.Value.m_level3 != null) pair.Value.m_level3.gameObject.SetActive(false);
            }
        }
    }
    // A local render-only copy of the real SurtlingCore item mesh. The source
    // prefab is inspected but never instantiated, so its ItemDrop/ZNetView,
    // physics and loot components can never enter the scene.
    internal sealed class Magic70SurtlingBirthVisual : MonoBehaviour
    {
        private const float Duration = 3.6f, CoreHold = 2.45f, GrowStart = 2.5f;
        private Character _character;
        private GameObject _coreVisual;
        private Transform _model;
        private Vector3 _modelScale;
        private float _createdAt;
        private float _headHeight = 1.5f, _nextSparkAt;
        private int _sparkCount;
        private bool _running, _cleaned, _soundPlayed;
        private float _nextSoundAttempt;
        private void Awake() { _character = GetComponent<Character>(); _createdAt = Time.time; }
        private void Update()
        {
            if (_cleaned || _character == null || _character.IsDead()) { Cleanup(); return; }
            if (_character.m_nview?.IsValid() != true) return;
            ZDO data = _character.m_nview.GetZDO();
            bool authored = data.GetLong(Magic70Surtling.LootAuthorKey, 0) != 0 ||
                data.GetLong(Magic70Surtling.AuthorKey, 0) != 0;
            if (!authored)
            { if (Time.time - _createdAt > 5f) Cleanup(); return; }
            long born = data.GetLong(Magic70Surtling.BirthTimeKey, 0);
            if (born <= 0)
            { if (Time.time - _createdAt > 5f) Cleanup(); return; }
            ZNet net = ZNet.instance;
            if (net == null) return;
            float age = Mathf.Max(0f, (float)((net.GetTime().Ticks - born) / (double)TimeSpan.TicksPerSecond));
            // Replication can consume the entire short server birth window.
            // For a freshly instantiated creature allow the remaining visual on
            // its observer, but never replay an old summon after loading a zone.
            if (age < 5f) age = Mathf.Min(age, Time.time - _createdAt);
            if (!_running)
            {
                if (age >= Duration) { Cleanup(); return; } // late loads never replay birth
                Begin(); _running = true;
            }
            if (age < Duration && !_soundPlayed && Time.time >= _nextSoundAttempt &&
                Player.m_localPlayer != null && !Application.isBatchMode && MasteryPlugin.Settings.EnablePerkSFX.Value)
            {
                _nextSoundAttempt = Time.time + .25f;
                GameObject sound = NativePerkAssetResolver.Resolve(Magic70Surtling.SummonSound);
                _soundPlayed = PerkAudioService.PlayPrefab("magic70_surtling_birth", sound,
                    _character.transform.position + Vector3.up * _headHeight, .18f, .55f);
            }
            float growth = Mathf.Clamp01((age - GrowStart) / (Duration - GrowStart));
            if (_model != null) _model.localScale = _modelScale * Mathf.Lerp(.02f, 1f, growth);
            UpdateIgnition(age);
            if (_coreVisual != null)
            {
                float fade = Mathf.Clamp01((age - CoreHold) / .24f);
                _coreVisual.transform.localScale = Vector3.one * (.7f * (1f - fade));
                _coreVisual.transform.position = _character.transform.position + Vector3.up * _headHeight;
                float charge = Mathf.Clamp01(age / CoreHold);
                float turn = 90f * charge + 720f * charge * charge;
                _coreVisual.transform.rotation = Quaternion.Euler(14f + Mathf.Sin(charge * 19f) * 22f,
                    turn, -10f + Mathf.Cos(charge * 17f) * 18f);
            }
            if (age >= Duration) Cleanup();
        }
        private void Begin()
        {
            Magic70Surtling.ApplyTierAppearance(_character);
            GameObject visual = _character.GetVisual();
            if (visual != null)
            {
                _model = visual.transform; _modelScale = _model.localScale;
                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    bool meshFound = false;
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer is ParticleSystemRenderer) continue;
                        if (!meshFound) { bounds = renderer.bounds; meshFound = true; }
                        else bounds.Encapsulate(renderer.bounds);
                    }
                    if (meshFound) _headHeight = Mathf.Clamp(bounds.max.y - _character.transform.position.y, .75f, 3f);
                }
            }
            GameObject item = ObjectDB.instance?.GetItemPrefab("SurtlingCore");
            if (item == null) return;
            _coreVisual = new GameObject("VM_SurtlingCoreBirthVisual");
            _coreVisual.transform.position = _character.transform.position + Vector3.up * .25f;
            CopyRenderTree(item.transform, _coreVisual.transform);
            if (_coreVisual.GetComponentsInChildren<MeshRenderer>(true).Length == 0)
            { Destroy(_coreVisual); _coreVisual = null; }
            else _coreVisual.transform.localScale = Vector3.one * .7f;
        }
        internal void TierScaleChanged(Transform model, Vector3 scale)
        { if (!_cleaned && _model == model) _modelScale = scale; }
        private void UpdateIgnition(float age)
        {
            if (age < 0f || age >= CoreHold || _sparkCount >= 16 || age < _nextSparkAt) return;
            Vector3 center = _character.transform.position + Vector3.up * _headHeight;
            PerkNativeFeedback.PlayVfx("vfx_HitSparks", center + UnityEngine.Random.insideUnitSphere * .18f, .3f, .42f);
            float charge = Mathf.Clamp01(age / CoreHold);
            _nextSparkAt = age + Mathf.Lerp(.42f, .12f, charge);
            _sparkCount++;
        }
        private static void CopyRenderTree(Transform source, Transform parent)
        {
            GameObject nodeObject = new GameObject(source.name);
            Transform node = nodeObject.transform; node.SetParent(parent, false);
            node.localPosition = source.localPosition; node.localRotation = source.localRotation; node.localScale = source.localScale;
            MeshFilter sourceMesh = source.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            if (sourceMesh != null && sourceMesh.sharedMesh != null && sourceRenderer != null)
            {
                nodeObject.AddComponent<MeshFilter>().sharedMesh = sourceMesh.sharedMesh;
                MeshRenderer renderer = nodeObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
            }
            for (int i = 0; i < source.childCount; i++) CopyRenderTree(source.GetChild(i), node);
        }
        private void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;
            if (_model != null) _model.localScale = _modelScale;
            if (_coreVisual != null) Destroy(_coreVisual);
            _coreVisual = null; Destroy(this);
        }
        private void OnDestroy()
        {
            if (_model != null) _model.localScale = _modelScale;
            if (_coreVisual != null) Destroy(_coreVisual);
        }
    }
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Magic70SurtlingPrefabPatch { private static void Postfix(ZNetScene __instance) => Magic70Surtling.RegisterPrefab(__instance); }
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Magic70SurtlingPlayerRpcPatch { private static void Postfix(Player __instance) => Magic70Surtling.RegisterPlayer(__instance); }
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class Magic70SurtlingDeathNoticePatch
    { private static void Prefix(Character __instance) => Magic70Surtling.BeforeNativeDeath(__instance); }
    [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
    internal static class Magic70SurtlingInputPatch
    { [HarmonyPriority(Priority.First)] private static bool Prefix(Player __instance) => Magic70Surtling.Input(__instance); }
    [HarmonyPatch(typeof(Character), nameof(Character.SetupMaxHealth))]
    internal static class Magic70SurtlingHealthPatch
    {
        // Native SetupMaxHealth temporarily clamps HP to the visual level's
        // smaller maximum. Preserve the pre-clamp HP through our stat correction.
        private static void Prefix(Character __instance, out float __state)
        {
            __state = Magic70Surtling.IsCompanion(__instance) && __instance.m_nview?.IsValid() == true &&
                __instance.m_nview.IsOwner() ? __instance.GetHealth() : -1f;
        }
        private static void Postfix(Character __instance, float __state)
        {
            if (!Magic70Surtling.IsCompanion(__instance) || __instance.m_nview?.IsValid() != true || !__instance.m_nview.IsOwner()) return;
            float skill = __instance.m_nview.GetZDO().GetFloat("vm.muspel.skill", 0f);
            float baseMax = __instance.GetMaxHealthBase();
            float statLevel = SurtlingQualityRules.StatLevel(Magic70Surtling.SummonQuality(__instance));
            float maximum = baseMax * statLevel * Magic70Surtling.SkillFactor(skill);
            __instance.SetMaxHealth(maximum);
            if (__state >= 0f) __instance.SetHealth(Mathf.Min(__state, maximum));
        }
    }
    [HarmonyPatch(typeof(Attack), "ModifyDamage")]
    internal static class Magic70SurtlingDamagePatch
    {
        private static void Postfix(Attack __instance, HitData hitData)
        {
            Character character = __instance.m_character;
            if (hitData != null && Magic70Surtling.IsCompanion(character))
                hitData.m_damage.Modify(Magic70Surtling.SkillFactor(character.m_nview?.GetZDO()?.GetFloat("vm.muspel.skill", 0f) ?? 0f) *
                    SurtlingQualityRules.DamageCorrection(Magic70Surtling.SummonQuality(character), character.GetLevel()));
        }
    }
}
