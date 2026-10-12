using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ValheimMastery
{
    internal static class FullResourceProcExperience
    {
        internal const float Window = 12f;
        internal const int MaximumRows = 4096, MaximumEpisodes = 64;
        internal static bool Enabled => MasteryPlugin.Settings.Enabled.Value;
        private static ZNet Session;
        private static long SessionWorld, Serial;
        private static string Epoch;
        private static float CleanupAt;
        private static readonly Dictionary<string, ResourceProcActorEpisode> Actors = new Dictionary<string, ResourceProcActorEpisode>();
        private static readonly Dictionary<string, ResourceProcSourceState> Sources = new Dictionary<string, ResourceProcSourceState>();
        private sealed class ChildProof { internal ResourceProcXpTicket Parent; internal string Expected; }
        private static ConditionalWeakTable<Component, ChildProof> Children = new ConditionalWeakTable<Component, ChildProof>();
        [ThreadStatic] internal static ResourceProcActorEpisode CurrentAttack;
        [ThreadStatic] internal static ResourceProcXpTicket Continuation;
        [ThreadStatic] private static Component ContinuationTarget;
        private sealed class AwardState { internal ResourceProcActorEpisode Actor; internal Skills.SkillType Skill; internal bool ScalePending; }
        [ThreadStatic] private static AwardState Award;
        internal struct AttackFrame { internal ResourceProcActorEpisode Previous, Episode; }

        internal static long World => ZNet.instance?.GetWorld() != null ? ZNet.instance.GetWorldUID() : 0;
        internal static void EnsureState()
        {
            long world = World;
            if (Session != ZNet.instance || SessionWorld != world)
            {
                Session = ZNet.instance; SessionWorld = world; Serial = 0; Epoch = Guid.NewGuid().ToString("N");
                Actors.Clear(); Sources.Clear(); Children = new ConditionalWeakTable<Component, ChildProof>(); CleanupAt = 0f;
                ResourceProcXpTransport.Reset();
            }
            if (Time.time < CleanupAt) return;
            CleanupAt = Time.time + 1f;
            foreach (string key in Actors.Where(pair => pair.Value.Expires < Time.time || pair.Value.Player != Player.m_localPlayer).Select(pair => pair.Key).ToArray()) Actors.Remove(key);
            foreach (string key in Sources.Where(pair => pair.Value.Expires < Time.time).Select(pair => pair.Key).ToArray()) Sources.Remove(key);
        }

        internal static void BeginAttack(Attack attack, out AttackFrame state)
        {
            state = new AttackFrame { Previous = CurrentAttack }; CurrentAttack = null;
            EnsureState(); ResourceProcXpTransport.Register();
            Player player = attack?.m_character as Player;
            if (!Enabled || World == 0 || player == null || player != Player.m_localPlayer || player.IsDead() ||
                player.m_nview?.IsOwner() != true || player.GetPlayerID() == 0 || attack.m_weapon?.m_shared == null ||
                !ResourceProcCredit.Finite(attack.m_raiseSkillAmount) || attack.m_raiseSkillAmount <= 0 || Actors.Count >= MaximumEpisodes || Serial == long.MaxValue) return;
            bool pick = attack.m_weapon.m_shared.m_skillType == Skills.SkillType.Pickaxes && PerkRuntimeService.HasPerk(player, Skills.SkillType.Pickaxes, 70);
            bool wood = attack.m_weapon.m_shared.m_skillType == Skills.SkillType.Axes && PerkRuntimeService.HasPerk(player, Skills.SkillType.WoodCutting, 35);
            if (!pick && !wood) return;
            var episode = new ResourceProcActorEpisode(player, World, Epoch, ++Serial, TierDatabase.ItemKey(attack.m_weapon),
                TierDatabase.GetItemTier(attack.m_weapon), attack.m_raiseSkillAmount, Time.time + Window, attack.m_weapon.m_shared.m_skillType);
            Actors.Add(episode.Key, episode); state.Episode = episode; CurrentAttack = episode;
        }
        internal static void FinishAttack(AttackFrame state, Exception error)
        {
            CurrentAttack = state.Previous;
            if (state.Episode == null) return;
            state.Episode.Returned = true; state.Episode.Failed |= error != null;
            try
            {
                for (int i = 0; i < state.Episode.PendingOrigins.Count; i++)
                    if (!state.Episode.Failed) AwardRows(state.Episode, state.Episode.PendingOrigins[i], state.Episode.PendingRows[i]);
            }
            catch (Exception failure) { ResourceProcXpTransport.Fault(failure); }
            finally { state.Episode.PendingOrigins.Clear(); state.Episode.PendingRows.Clear(); }
        }

        // Replaces only the one native HashSet award call; its virtual dispatch is unchanged.
        internal static void RaiseOrdinary(Character character, Skills.SkillType skill, float factor)
        {
            ResourceProcActorEpisode episode = CurrentAttack;
            if (episode?.Player != character || !GatheringProgressionService.IsGatheringSkill(skill) ||
                skill == Skills.SkillType.Farming || !ResourceProcCredit.Finite(factor) || factor <= 0)
            { character.RaiseSkill(skill, factor); return; }
            Skills.Skill native = null; float beforeLevel = 0f, beforeAcc = 0f, need = 0f, first = 1f;
            string firstKey = null; bool hadFirst = true;
            try
            {
                native = episode.Player.GetSkills().GetSkill(skill);
                beforeLevel = native.m_level; beforeAcc = native.m_accumulator; need = native.GetNextLevelRequirement();
                firstKey = ExperienceContext.ResourceProcFirstKey(skill);
                hadFirst = firstKey != null && episode.Player.m_customData.ContainsKey(firstKey);
                first = MasteryRuntime.FirstUniqueMultiplier;
            }
            catch (Exception failure) { native = null; ResourceProcXpTransport.Fault(failure); }
            // Never swallow, replace, or invoke twice an exception from the native award.
            character.RaiseSkill(skill, factor);
            if (native == null) { episode.Failed = true; return; }
            try
            {
                bool usedFirst = !hadFirst && firstKey != null && episode.Player.m_customData.ContainsKey(firstKey);
                episode.Credit(skill).Observe(ResourceProcCredit.Granted(beforeLevel, beforeAcc, need, native.m_level, native.m_accumulator), usedFirst ? first : 1f);
            }
            catch (Exception failure) { episode.Failed = true; ResourceProcXpTransport.Fault(failure); }
        }

        internal static float ApplyCredit(Skills.SkillType skill, float finalFactor)
        {
            if (Award?.Actor?.Player != Player.m_localPlayer || Award.Skill != skill || !Award.ScalePending) return finalFactor;
            // One direct native call consumes this gate before any postfix/event reentrancy.
            Award.ScalePending = false;
            Skills.Skill native = Award.Actor.Player.GetSkills().GetSkill(skill);
            float rate = native.m_info.m_increseStep * Game.m_skillGainRate;
            return Award.Actor.Credit(skill).Deduct(finalFactor, rate);
        }

        internal static ZNetView View(Component target) => target?.GetComponent<ZNetView>();
        internal static int Kind(Component target) => target is MineRock5 ? 1 : target is TreeBase || target is TreeLog ||
            target is Destructible destructible && (destructible.m_destructibleType & DestructibleType.Tree) != 0 ? 2 : 0;
        internal static int Maximum(Component target) => target is MineRock5 mine ? Math.Min(MaximumRows, mine.m_hitAreas.Count) : MaximumRows;

        internal static ResourceProcOrigin CaptureEnvelope(ResourceProcEnvelope key, long owner, Component target)
        {
            var actor = CurrentAttack; int kind = Kind(target);
            if (actor == null || actor.World != key.World || key.Sender != ZNet.GetUID() || kind == 0 || owner == 0 ||
                key.Target == ZDOID.None || actor.Expires < Time.time || actor.Targets.Count >= 2048) return null;
            Skills.SkillType skill = kind == 1 ? Skills.SkillType.Pickaxes : Skills.SkillType.WoodCutting;
            if (kind == 1 && actor.WeaponSkill != Skills.SkillType.Pickaxes || kind == 2 && actor.WeaponSkill != Skills.SkillType.Axes) return null;
            if (!PerkRuntimeService.HasPerk(actor.Player, skill, kind == 1 ? 70 : 35)) return null;
            int maximum = Maximum(target); if (maximum <= 0 || kind == 1 && (key.NativeIndex < 0 || key.NativeIndex >= maximum)) return null;
            foreach (var prior in actor.Targets.Values)
                if (prior.Envelope.Target == key.Target && prior.Owner != owner) actor.Transferred.Add(key.Target);
            if (actor.Transferred.Contains(key.Target)) return null;
            if (actor.Targets.TryGetValue(key, out var existing)) return existing;
            var origin = new ResourceProcOrigin(actor.Epoch, actor.Attack, actor.Author, actor.Character, owner, skill, actor.Tool,
                actor.ToolTier, actor.Raw, kind, maximum, TierDatabase.TargetKey(target), key);
            actor.Targets.Add(key, origin); return origin;
        }

        private static bool Live(ResourceProcXpTicket ticket) => Enabled && ticket != null && !ticket.State.Closed &&
            ticket.State.Expires >= Time.time && ticket.Origin.Envelope.World == World && ticket.TargetOwner == ZNet.GetUID();
        internal static bool MatchesActor(ResourceProcXpTicket ticket, Player attacker) => attacker != null &&
            attacker.GetPlayerID() == ticket.AuthorId && attacker.GetZDOID() == ticket.CharacterId;

        internal static bool TryGetCurrent(Component target, HitData hit, int nativeHitAreaIndex, out ResourceProcXpTicket ticket)
        {
            ticket = null; EnsureState(); var view = View(target); var frame = ResourceProcXpTransport.Current;
            if (!Enabled || target == null || view?.IsValid() != true || !view.IsOwner() || hit == null ||
                frame == null || frame.Key.Target != view.GetZDO().m_uid || frame.Origin == null) return false;
            var origin = frame.Origin; Player attacker = hit.GetAttacker() as Player;
            if (origin.Owner != ZNet.GetUID() || origin.Envelope.World != World || attacker == null ||
                attacker.GetPlayerID() != origin.Author || attacker.GetZDOID() != origin.Character) return false;
            if (!frame.Continued && (PerkRuntimeService.IsPerkGenerated(hit) || !ResourceProcCredit.Finite(hit.m_skillRaiseAmount) || hit.m_skillRaiseAmount <= 0 ||
                hit.m_skill != origin.Skill ||
                origin.Kind == 1 && origin.Envelope.NativeIndex != nativeHitAreaIndex)) return false;
            if (frame.Ticket != null)
            { ticket = Live(frame.Ticket) ? frame.Ticket : null; return ticket != null; }
            if (Sources.Count >= 2048 && !Sources.ContainsKey(origin.SourceKey)) return false;
            if (!Sources.TryGetValue(origin.SourceKey, out var source)) Sources.Add(origin.SourceKey, source = new ResourceProcSourceState(origin, Time.time + Window));
            if (source.Closed || source.Expires < Time.time || !SameSource(source.Origin, origin)) return false;
            ticket = new ResourceProcXpTicket(source, origin, view.GetZDO().m_uid, origin.Kind, origin.Maximum, TierDatabase.TargetKey(target));
            frame.Ticket = ticket; return true;
        }

        internal static ResourceProcXpResult BindChild(ResourceProcXpTicket parent, Component child, string expectedNativePrefab, out ResourceProcXpTicket childTicket)
        {
            childTicket = null;
            if (!Live(parent) || parent.Kind != 2 || child == null || Kind(child) != 2 ||
                TierDatabase.TargetKey(child) != GatheringProgressionService.Normalize(expectedNativePrefab)) return ResourceProcXpResult.NoOp;
            if (!Children.TryGetValue(child, out var proof))
            {
                var frame = ResourceProcXpTransport.Current;
                if (!(frame?.Ticket?.State == parent.State && frame.Ticket.Target == parent.Target) && Continuation != parent) return ResourceProcXpResult.NoOp;
                Children.Add(child, proof = new ChildProof { Parent = parent, Expected = expectedNativePrefab });
            }
            if (proof.Parent.State != parent.State || proof.Expected != expectedNativePrefab) return ResourceProcXpResult.NoOp;
            var view = View(child);
            if (view?.IsValid() != true) return ResourceProcXpResult.Pending;
            if (!view.IsOwner() || view.GetZDO().GetOwner() != parent.TargetOwner) return ResourceProcXpResult.NoOp;
            ZDOID uid = view.GetZDO().m_uid;
            if (!parent.State.Children.Contains(uid) && parent.State.Children.Count >= MaximumRows) return ResourceProcXpResult.NoOp;
            parent.State.Children.Add(uid);
            childTicket = new ResourceProcXpTicket(parent.State, parent.Origin, uid, 2, 1, TierDatabase.TargetKey(child));
            return ResourceProcXpResult.Accepted;
        }

        private sealed class ContinuationScope : IDisposable
        {
            internal ResourceProcXpTicket Previous; internal Component PreviousTarget;
            private bool Disposed;
            public void Dispose() { if (Disposed) return; Disposed = true; Continuation = Previous; ContinuationTarget = PreviousTarget; }
        }
        internal static IDisposable EnterContinuation(ResourceProcXpTicket ticket, Component target, Player attacker, HitData hit)
        {
            var view = View(target);
            if (!Live(ticket) || !MatchesActor(ticket, attacker) || hit?.GetAttacker() != attacker ||
                view?.IsValid() != true || !view.IsOwner() || view.GetZDO().m_uid != ticket.Target) return null;
            var scope = new ContinuationScope { Previous = Continuation, PreviousTarget = ContinuationTarget };
            Continuation = ticket; ContinuationTarget = target; return scope;
        }
        internal static ResourceProcXpTicket Carried(ResourceProcEnvelope key) => Live(Continuation) &&
            key.Sender == ZNet.GetUID() && View(ContinuationTarget)?.GetZDO()?.m_uid == key.Target ? Continuation : null;

        internal static ResourceProcXpResult ConfirmDestroyed(ResourceProcXpTicket ticket, ZDOID sourceUid, int segmentIndex, string actualPrefab, int resourceTier)
        {
            if (!Live(ticket) || sourceUid != ticket.Target || sourceUid == ZDOID.None ||
                ticket.Kind == 1 && (segmentIndex < 0 || segmentIndex >= ticket.Maximum) || ticket.Kind == 2 && segmentIndex != -1 ||
                string.IsNullOrEmpty(actualPrefab) || TierDatabaseKey(actualPrefab) != ticket.Prefab ||
                resourceTier != GatheringProgressionService.GetResourceTier(actualPrefab, ticket.Skill)) return ResourceProcXpResult.NoOp;
            var row = new ResourceProcRow(sourceUid, segmentIndex, ticket.Prefab, resourceTier);
            if (ticket.State.Rows.ContainsKey(row.Segment)) return ResourceProcXpResult.Duplicate;
            if (ticket.State.Rows.Count >= MaximumRows) return ResourceProcXpResult.NoOp;
            ticket.State.Rows.Add(row.Segment, row); return ResourceProcXpResult.Accepted;
        }
        private static string TierDatabaseKey(string value) => GatheringProgressionService.Normalize(value);
        internal static ResourceProcXpResult CloseEpisode(ResourceProcXpTicket rootTicket, bool complete)
        {
            if (!Live(rootTicket)) return rootTicket?.State.Closed == true ? ResourceProcXpResult.Duplicate : ResourceProcXpResult.NoOp;
            rootTicket.State.Closed = true;
            if (!complete || rootTicket.State.Rows.Count == 0) { rootTicket.State.Rows.Clear(); return ResourceProcXpResult.NoOp; }
            var rows = rootTicket.State.Rows.Values.OrderBy(row => row.Prefab, StringComparer.Ordinal).ThenBy(row => row.Segment.Source.ToString(), StringComparer.Ordinal).ThenBy(row => row.Segment.Index).ToArray();
            ResourceProcOrigin origin = rootTicket.State.Origin;
            if (origin.Envelope.Sender == ZNet.GetUID()) return Receive(origin, rows, ZNet.GetUID());
            ResourceProcXpTransport.SendReceipt(origin, rows); return ResourceProcXpResult.Pending;
        }

        private static bool SameSource(ResourceProcOrigin a, ResourceProcOrigin b) => a != null && b != null &&
            a.Epoch == b.Epoch && a.Attack == b.Attack && a.Author == b.Author && a.Character == b.Character && a.Owner == b.Owner &&
            a.Skill == b.Skill && a.Tool == b.Tool && a.ToolTier == b.ToolTier && a.Raw == b.Raw && a.Kind == b.Kind &&
            a.Maximum == b.Maximum && a.Prefab == b.Prefab && a.Envelope.World == b.Envelope.World &&
            a.Envelope.Sender == b.Envelope.Sender && a.Envelope.Target == b.Envelope.Target && a.Envelope.Method == b.Envelope.Method;
        private static bool SameOrigin(ResourceProcOrigin a, ResourceProcOrigin b) => SameSource(a, b) && a.Envelope.Equals(b.Envelope);
        internal static ResourceProcXpResult Receive(ResourceProcOrigin origin, ResourceProcRow[] rows, long sender)
        {
            EnsureState();
            if (!Enabled || origin == null) return ResourceProcXpResult.NoOp;
            if (!Actors.TryGetValue(origin.ActorKey, out var actor) || actor.Player != Player.m_localPlayer || actor.Player.IsDead() ||
                actor.Player.GetZDOID() != actor.Character || actor.Failed || actor.Expires < Time.time || sender != origin.Owner ||
                origin.Envelope.World != actor.World || actor.Transferred.Contains(origin.Envelope.Target) ||
                !actor.Targets.TryGetValue(origin.Envelope, out var expected) || !SameOrigin(origin, expected) || rows == null || rows.Length == 0 || rows.Length > origin.Maximum) return ResourceProcXpResult.NoOp;
            if (actor.CommittedRoots.Contains(origin.Envelope.Target)) return ResourceProcXpResult.Duplicate;
            var packetKeys = new HashSet<ResourceProcSegment>();
            foreach (var row in rows)
                if (row.Segment.Source == ZDOID.None || !packetKeys.Add(row.Segment) || string.IsNullOrEmpty(row.Prefab) || row.Prefab.Length > 96 ||
                    row.Tier != GatheringProgressionService.GetResourceTier(row.Prefab, origin.Skill) ||
                    origin.Kind == 1 && (row.Segment.Source != origin.Envelope.Target || row.Segment.Index < 0 || row.Segment.Index >= origin.Maximum || row.Prefab != origin.Prefab) ||
                    origin.Kind == 2 && row.Segment.Index != -1) return ResourceProcXpResult.NoOp;
            var fresh = rows.Where(row => !actor.Seen.Contains(row.Segment)).ToArray();
            if (fresh.Length == 0) return ResourceProcXpResult.Duplicate;
            if (actor.Seen.Count + fresh.Length > MaximumRows) return ResourceProcXpResult.NoOp;
            actor.CommittedRoots.Add(origin.Envelope.Target);
            foreach (var row in fresh) actor.Seen.Add(row.Segment);
            if (!actor.Returned) { actor.PendingOrigins.Add(origin); actor.PendingRows.Add(fresh); return ResourceProcXpResult.Pending; }
            AwardRows(actor, origin, fresh); return ResourceProcXpResult.Accepted;
        }
        private static void AwardRows(ResourceProcActorEpisode actor, ResourceProcOrigin origin, ResourceProcRow[] rows)
        {
            if (!Enabled || actor.Failed || actor.Player != Player.m_localPlayer || actor.Player.IsDead() || actor.Player.GetZDOID() != actor.Character || actor.World != World) return;
            AwardState previous = Award; Award = new AwardState { Actor = actor, Skill = origin.Skill };
            try
            {
                foreach (var row in rows)
                    using (ExperienceContext.BeginResourceProc(actor.Player, origin.Skill, actor.Tool, row.Prefab, row.Tier, actor.ToolTier))
                        for (int equivalent = 0; equivalent < 3; equivalent++)
                        {
                            Award.ScalePending = true;
                            try { actor.Player.RaiseSkill(origin.Skill, actor.Raw); }
                            finally { Award.ScalePending = false; }
                        }
            }
            catch (Exception failure) { actor.Failed = true; ResourceProcXpTransport.Fault(failure); }
            finally { Award = previous; }
        }
        internal static void ForgetActor(Player player)
        { foreach (string key in Actors.Where(pair => pair.Value.Player == player).Select(pair => pair.Key).ToArray()) Actors.Remove(key); }
    }
}
