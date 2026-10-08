using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Converts only server-validated Crafting action XP. This service never accepts Favor values.
    internal static class GoldFavorService
    {
        private const string XpRpc = "VM_Gold_FavorXpV1";
        private const string ProcessingRpc = "VM_Gold_ProcessingReceiptV1";
        private const string ProcessingAckRpc = "VM_Gold_ProcessingAckV1";
        private const string ProcessingSequenceKey = "vm.gold.processing.sequence.v1";
        private const string ProcessingProofPrefix = "vm.gold.processing.proof.";
        private const float MaxBossMultiplier = 100f;
        private const float MaxFirstUniqueMultiplier = 100f;
        private const float MaxExpectedBaseXp = 10000f;
        private const float MaxExpectedFinalXp = 1000000f;
        private const int MaxPendingProcessingReceipts = 256;
        private sealed class Rate { internal float Start; internal int Count; }
        private sealed class PendingProcessing
        {
            internal ZDOID Station;
            internal long Author, Sequence;
            internal string Ore, Receipt;
            internal float LastSent;
        }
        private static readonly Dictionary<long, Rate> Rates = new Dictionary<long, Rate>();
        private static readonly Dictionary<string, PendingProcessing> PendingReceipts = new Dictionary<string, PendingProcessing>(StringComparer.Ordinal);
        private static ConfigEntry<float> Conversion;
        private static ConfigEntry<float> ChargeTimeMultiplier;
        private static ZNet Session;
        private static float NextPrune;

        internal static bool Enabled => GoldCraftingService.Enabled;

        internal static void Initialize(ConfigFile config)
        {
            Conversion = config.Bind("GoldDevelopment", "CraftingFavorPerXP", GoldFavorModel.DefaultFavorPerXp,
                "Provisional Favor per validated Crafting XP. Balance is not calibrated until timed live testing.");
            if (!GoldFavorModel.Finite(Conversion.Value) || Conversion.Value < 0f || Conversion.Value > 10f)
                Conversion.Value = GoldFavorModel.DefaultFavorPerXp;
            ChargeTimeMultiplier = config.Bind("GoldCrafting", "FavorChargeTimeMultiplier", GoldFavorModel.DefaultChargeTimeMultiplier,
                "Favor awards divided by this value after the event cap. Default4 makes all filling four times slower than1.4.117; existing XP conversion settings are preserved.");
            if (!GoldFavorModel.Finite(ChargeTimeMultiplier.Value) || ChargeTimeMultiplier.Value < 1f || ChargeTimeMultiplier.Value > 100f)
                ChargeTimeMultiplier.Value = GoldFavorModel.DefaultChargeTimeMultiplier;
            GoldFavorTelemetry.Configure(Conversion.Value, ChargeTimeMultiplier.Value);
        }

        internal static void Register(ZRpc rpc)
        {
            rpc.Register<string, float, float>(XpRpc, ReceiveXp);
            rpc.Register<ZPackage>(ProcessingRpc, ReceiveProcessingReceipt);
            rpc.Register<ZPackage>(ProcessingAckRpc, ReceiveProcessingAck);
            CraftingBuildReuseService.Register(rpc);
        }

        internal static void Tick()
        {
            CraftingBuildReuseService.Tick();
            if (Session != ZNet.instance)
            {
                Session = ZNet.instance; NextPrune = 0f;
                Rates.Clear(); PendingReceipts.Clear();
            }
            if (Time.unscaledTime < NextPrune) return;
            NextPrune = Time.unscaledTime + 10f;
            var stale = new List<long>();
            foreach (var pair in Rates) if (Time.unscaledTime - pair.Value.Start > 10f) stale.Add(pair.Key);
            foreach (long id in stale) Rates.Remove(id);
            if (ZNet.instance?.IsServer() != true)
            {
                var expired = new List<string>();
                foreach (var pair in PendingReceipts)
                {
                    PendingProcessing pending = pair.Value;
                    if (Time.unscaledTime - pending.LastSent < 1f) continue;
                    if (Time.unscaledTime - pending.LastSent > 60f) { expired.Add(pair.Key); continue; }
                    SendProcessing(pending);
                }
                foreach (string key in expired) PendingReceipts.Remove(key);
            }
        }

        internal static void Observe(SkillXpEvent value)
        {
            if (!Enabled || value?.Player == null || value.Player != Player.m_localPlayer || value.Skill != Skills.SkillType.Crafting) return;
            string source = ExperienceContext.ActionKey(Skills.SkillType.Crafting);
            if (!IsSupportedSource(source)) return; // processing XP is paid only from completion receipts.
            ZNet net = ZNet.instance;
            if (net == null || !GoldFavorModel.Finite(value.BaseXp) || !GoldFavorModel.Finite(value.FinalXp)) return;
            if (net.IsServer())
            {
                ReceiveXp(null, source, value.BaseXp, value.FinalXp);
                return;
            }
            net.GetServerRPC()?.Invoke(XpRpc, source, value.BaseXp, value.FinalXp);
        }

        private static void ReceiveXp(ZRpc rpc, string source, float claimedBaseXp, float claimedFinalXp)
        {
            if (!Enabled || ZNet.instance?.IsServer() != true || GoldCraftingService.ServerLedger?.IsAvailable != true || !IsSupportedSource(source) ||
                !GoldFavorModel.Finite(claimedBaseXp) || !GoldFavorModel.Finite(claimedFinalXp) ||
                claimedBaseXp <= 0f || claimedBaseXp > MaxExpectedBaseXp || claimedFinalXp <= 0f || claimedFinalXp > MaxExpectedFinalXp) return;
            WorkshopActor actor = WorkshopActor.Resolve(rpc);
            if (actor == null || !actor.Available || actor.IsDead() || actor.IsTeleporting() || !GoldCraftingService.HasGold(actor)) return;
            long playerId = actor.GetPlayerID();
            var state = GoldCraftingService.ServerLedger.Get(playerId);
            if (state?.Unlocked != true || !RateAllowed(playerId)) return;

            if (!TryCanonicalBaseXp(source, out float expectedBase)) return;
            if (!Near(claimedBaseXp, expectedBase, .02f, .02f)) return;
            float boss = WorldProgressionXpService.GetBossXpMultiplier();
            if (!GoldFavorModel.Finite(boss) || boss < 0f || boss > MaxBossMultiplier) return;
            float normal = expectedBase * boss;
            float firstUnique = Math.Min(MaxFirstUniqueMultiplier, Math.Max(1f, MasteryRuntime.FirstUniqueMultiplier));
            float unique = normal * firstUnique;
            float validatedXp = Near(claimedFinalXp, normal, .02f, .02f) ? normal :
                (firstUnique > 1f && Near(claimedFinalXp, unique, .02f, .02f) ? unique : 0f);
            if (validatedXp <= 0f || validatedXp > MaxExpectedFinalXp) return;

            // Normal owner XP is already suppressed before RaiseSkill. This is a
            // second server-side guard for stale owner state, not a client Favor claim.
            if (source.StartsWith("build.", StringComparison.Ordinal) &&
                CraftingBuildReuseService.ConsumeServerReplacement(playerId, source.Substring(6))) return;

            GoldFavorModel.Result result = GoldFavorTelemetry.Observe(playerId, source, validatedXp);
            if (!result.Accepted || result.FavorGain <= 0f) return;
            float before = state.Favor;
            if (!GoldCraftingService.ServerLedger.Gain(playerId, result.FavorGain)) return;
            float actual = Math.Max(0f, GoldCraftingService.ServerLedger.Get(playerId).Favor - before);
            GoldFavorTelemetry.RecordAwarded(playerId, actual);
            GoldFavorTelemetry.TraceAward(source, playerId, result, actual);
            if (actual > 0f) GoldCraftingService.NotifyFavor(playerId);
        }

        private static bool RateAllowed(long playerId)
        {
            float now = Time.unscaledTime;
            if (!Rates.TryGetValue(playerId, out Rate rate)) Rates.Add(playerId, rate = new Rate { Start = now });
            if (now < rate.Start || now - rate.Start >= 1f) { rate.Start = now; rate.Count = 0; }
            return ++rate.Count <= 10;
        }

        private static bool IsSupportedSource(string source)
        {
            if (String.IsNullOrWhiteSpace(source) || source.Length > 160 || source.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            return source.StartsWith("craft.", StringComparison.Ordinal) || source.StartsWith("upgrade.", StringComparison.Ordinal) || source.StartsWith("build.", StringComparison.Ordinal);
        }

        private static bool TryCanonicalBaseXp(string source, out float xp)
        {
            xp = 0f;
            int firstDot = source.IndexOf('.');
            if (firstDot <= 0) return false;
            string kind = source.Substring(0, firstDot);
            if (kind == "craft" || kind == "upgrade")
            {
                int qualityDot = source.LastIndexOf(".q", StringComparison.Ordinal);
                if (qualityDot <= firstDot || qualityDot + 2 >= source.Length ||
                    !Int32.TryParse(source.Substring(qualityDot + 2), out int quality) || quality < 1 || quality > 10) return false;
                string itemKey = source.Substring(firstDot + 1, qualityDot - firstDot - 1);
                Recipe match = null;
                foreach (Recipe recipe in ObjectDB.instance?.m_recipes ?? new List<Recipe>())
                    if (recipe?.m_item?.m_itemData != null && String.Equals(TierDatabase.ItemKey(recipe.m_item.m_itemData), itemKey, StringComparison.OrdinalIgnoreCase))
                    { match = recipe; break; }
                if (match == null) return false;
                xp = .15f + ResourceValue(match.m_resources, quality);
                return xp > 0f && xp <= MaxExpectedBaseXp;
            }
            if (kind == "build")
            {
                string pieceKey = source.Substring(firstDot + 1);
                if (pieceKey.Length == 0) return false;
                foreach (GameObject prefab in ZNetScene.instance?.m_prefabs ?? new List<GameObject>())
                {
                    if (prefab == null || !String.Equals(prefab.name.Replace("(Clone)", string.Empty), pieceKey, StringComparison.OrdinalIgnoreCase)) continue;
                    Piece piece = prefab.GetComponent<Piece>();
                    if (piece == null) continue;
                    xp = .10f + ResourceValue(piece.m_resources, 1);
                    return xp > 0f && xp <= MaxExpectedBaseXp;
                }
            }
            return false;
        }

        private static float ResourceValue(Piece.Requirement[] requirements, int quality)
        {
            if (requirements == null || requirements.Length > 128) return 0f;
            float total = 0f;
            foreach (Piece.Requirement requirement in requirements)
            {
                if (requirement == null) continue;
                int amount = requirement.GetAmount(quality);
                if (amount < 0 || amount > 100000) return 0f;
                total += TierDatabase.GetResourceValue(requirement.m_resItem?.m_itemData, amount);
                if (!GoldFavorModel.Finite(total) || total > MaxExpectedBaseXp) return 0f;
            }
            return total;
        }

        private static bool Near(float claimed, float expected, float absolute, float relative) =>
            Math.Abs(claimed - expected) <= Math.Max(absolute, Math.Abs(expected) * relative);

        // Completion patch captures the newly appended persistent input receipt; no idle-time accrual.
        [HarmonyPatch(typeof(ProcessingPersistentBatches), nameof(ProcessingPersistentBatches.Complete))]
        private static class ProcessingCompletePatch
        {
            private sealed class BeforeState { internal string Raw; internal bool ChangedOre; internal int Previous; internal string Ore; }
            private static void Prefix(Smelter station, string ore, out BeforeState __state)
            {
                __state = null;
                if (!Enabled || station?.m_nview?.IsOwner() != true || station.m_nview.GetZDO() == null) return;
                var zdo = station.m_nview.GetZDO();
                int previous = station.m_spawnStack ? zdo.GetInt(ZDOVars.s_spawnAmount, 0) : 0;
                __state = new BeforeState
                {
                    Raw = zdo.GetString(ProcessingPersistentBatches.OutputKey, ""),
                    ChangedOre = station.m_spawnStack && previous > 0 && zdo.GetString(ZDOVars.s_spawnOre, "") != ore,
                    Previous = previous, Ore = ore
                };
            }
            private static void Postfix(Smelter station, BeforeState __state)
            {
                if (__state == null || station?.m_nview?.IsOwner() != true) return;
                var zdo = station.m_nview.GetZDO();
                string raw = zdo.GetString(ProcessingPersistentBatches.OutputKey, "");
                if (raw.Length == 0 || raw.Length > 300000 || String.Equals(raw, __state.Raw, StringComparison.Ordinal)) return;
                string receiptWire = LastReceipt(raw);
                ProcessingBatchReceipt receipt = ProcessingBatchReceipt.Decode(receiptWire);
                if (receipt.Author == 0 || receipt.Level < 100f) return;
                long sequence = zdo.GetLong(ProcessingSequenceKey, 0) + 1;
                if (sequence <= 0) return;
                zdo.Set(ProcessingProofPrefix + (sequence % 64), sequence + "|" + __state.Ore + "|" + receiptWire);
                zdo.Set(ProcessingSequenceKey, sequence);
                ZNet net = ZNet.instance;
                var pending = new PendingProcessing { Station = zdo.m_uid, Author = receipt.Author, Ore = __state.Ore, Sequence = sequence, Receipt = receiptWire };
                if (net?.IsServer() == true) ProcessProcessingReceipt(null, zdo.m_uid, receipt.Author, __state.Ore, sequence, receiptWire);
                else
                {
                    if (PendingReceipts.Count >= MaxPendingProcessingReceipts) return;
                    PendingReceipts[ReceiptKey(zdo.m_uid, sequence)] = pending;
                    SendProcessing(pending);
                }
            }
        }

        private static string ReceiptKey(ZDOID station, long sequence) => station + ":" + sequence;
        private static void SendProcessing(PendingProcessing pending)
        {
            ZNet net = ZNet.instance;
            if (net == null || net.IsServer()) return;
            pending.LastSent = Time.unscaledTime;
            var package = new ZPackage(); package.Write(pending.Station); package.Write(pending.Author);
            package.Write(pending.Ore); package.Write(pending.Sequence); package.Write(pending.Receipt);
            net.GetServerRPC()?.Invoke(ProcessingRpc, package);
        }

        private static void ReceiveProcessingReceipt(ZRpc rpc, ZPackage package)
        {
            if (package == null || package.Size() > 256 || ZNet.instance?.IsServer() != true) return;
            try
            {
                ZDOID stationId = package.ReadZDOID(); long author = package.ReadLong();
                string ore = package.ReadString(); long sequence = package.ReadLong(); string receipt = package.ReadString();
                if (ore == null || ore.Length > 64 || receipt == null || receipt.Length > 64) return;
                ProcessProcessingReceipt(rpc, stationId, author, ore, sequence, receipt);
            }
            catch { /* Malformed or truncated request: reject without changing ledger state. */ }
        }

        private static void ReceiveProcessingAck(ZRpc rpc, ZPackage package)
        {
            if (ZNet.instance?.IsServer() != false || rpc != ZNet.instance.GetServerRPC()) return;
            ZDOID stationId; long sequence; bool consumed;
            if (package == null || package.Size() > 32) return;
            try { stationId = package.ReadZDOID(); sequence = package.ReadLong(); consumed = package.ReadBool(); }
            catch { return; }
            if (consumed) PendingReceipts.Remove(ReceiptKey(stationId, sequence));
        }

        private static string LastReceipt(string raw)
        {
            int index = raw.LastIndexOf(';');
            return index < 0 ? raw : raw.Substring(index + 1);
        }

        private static void ProcessProcessingReceipt(ZRpc rpc, ZDOID stationId, long author, string ore, long sequence, string receiptWire)
        {
            if (!Enabled || ZNet.instance?.IsServer() != true || GoldCraftingService.ServerLedger?.IsAvailable != true || author == 0 || sequence <= 0 ||
                String.IsNullOrWhiteSpace(ore) || ore.Length > 64 || String.IsNullOrEmpty(receiptWire) || receiptWire.Length > 64) return;
            ZDO station = ZDOMan.instance?.GetZDO(stationId);
            if (station == null || station.m_uid != stationId || station.GetOwner() == 0 ||
                (rpc == null ? station.GetOwner() != ZNet.GetUID() : ZNet.instance.GetPeer(rpc)?.m_uid != station.GetOwner())) return;
            long latest = station.GetLong(ProcessingSequenceKey, 0);
            string stationKey = stationId.ToString();
            if (GoldCraftingService.ServerLedger.HasBackgroundReceipt(author, stationKey, sequence) ||
                GoldCraftingService.ServerLedger.IsBackgroundReceiptExpired(author, stationKey, sequence))
            { ReplyProcessing(rpc, stationId, sequence, true); return; }
            if (sequence > latest || latest <= 0) { ReplyProcessing(rpc, stationId, sequence, false); return; }
            if (latest - sequence >= 64) { ReplyProcessing(rpc, stationId, sequence, true); return; }
            string proof = station.GetString(ProcessingProofPrefix + (sequence % 64), "");
            string expectedProof = sequence + "|" + ore + "|" + receiptWire;
            if (!String.Equals(proof, expectedProof, StringComparison.Ordinal))
            { ReplyProcessing(rpc, stationId, sequence, false); return; }
            ProcessingBatchReceipt receipt = ProcessingBatchReceipt.Decode(receiptWire);
            if (receipt.Author != author || receipt.Level < 100f) { ReplyProcessing(rpc, stationId, sequence, false); return; }
            int component = WorkshopWorldRecords.Covered(station.GetPosition());
            if (component < 0 || !AuthorCovered(author, component) || GoldCraftingService.ServerLedger.Get(author)?.Unlocked != true)
            { ReplyProcessing(rpc, stationId, sequence, false); return; }

            int tier = Math.Max(GatheringProgressionService.GetResourceTier(ore, Skills.SkillType.Pickaxes),
                GatheringProgressionService.GetResourceTier(ore, Skills.SkillType.Farming));
            string normalizedOre = ore.Replace("(Clone)", string.Empty).Trim().TrimStart('$');
            if (String.Equals(normalizedOre, "Eitr", StringComparison.OrdinalIgnoreCase)) tier = 6;
            // Same normal processing XP as ProcessingStationProgression + native
            // ExperienceContext's world multiplier. No guessed first-unique reward:
            // the owner's pre-Gold history may already have consumed that one-off bonus.
            float xp = .25f * Math.Max(1, tier) * WorldProgressionXpService.GetBossXpMultiplier();
            if (!GoldFavorModel.Finite(xp) || xp <= 0f || xp > MaxExpectedFinalXp) return;
            GoldFavorModel.Result result = GoldFavorTelemetry.Observe(author, "processing." + ore, xp);
            if (!result.Accepted || result.FavorGain <= 0f) return;
            float accepted = GoldCraftingService.ServerLedger.GainBackground(author, result.FavorGain,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d, stationKey, sequence);
            bool consumed = GoldCraftingService.ServerLedger.HasBackgroundReceipt(author, stationKey, sequence) ||
                GoldCraftingService.ServerLedger.IsBackgroundReceiptExpired(author, stationKey, sequence);
            ReplyProcessing(rpc, stationId, sequence, consumed);
            if (accepted > 0f)
            {
                GoldFavorTelemetry.RecordAwarded(author, accepted);
                GoldFavorTelemetry.TraceAward("processing." + ore, author, result, accepted);
                GoldCraftingService.NotifyFavor(author);
            }
        }

        private static void ReplyProcessing(ZRpc rpc, ZDOID stationId, long sequence, bool consumed)
        {
            if (rpc == null) return;
            var package = new ZPackage(); package.Write(stationId); package.Write(sequence); package.Write(consumed);
            rpc.Invoke(ProcessingAckRpc, package);
        }

        private static bool AuthorCovered(long playerId, int component)
        {
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == playerId && local.m_nview?.GetZDO() is ZDO own && WorkshopWorldRecords.Covered(own.GetPosition()) == component) return true;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                ZDO character = OwnerSkillAuthority.ResolveCharacterData(peer);
                if (character != null && character.GetLong(ZDOVars.s_playerID, 0) == playerId && WorkshopWorldRecords.Covered(character.GetPosition()) == component) return true;
            }
            return false;
        }
    }
}
