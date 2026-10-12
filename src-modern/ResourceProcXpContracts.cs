using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal enum ResourceProcXpResult { Accepted, Duplicate, NoOp, Pending }

    internal readonly struct ResourceProcEnvelope : IEquatable<ResourceProcEnvelope>
    {
        internal readonly long World, Sender, Message;
        internal readonly ZDOID Target;
        internal readonly int Method, NativeIndex;
        internal ResourceProcEnvelope(long world, long sender, long message, ZDOID target, int method, int index)
        { World = world; Sender = sender; Message = message; Target = target; Method = method; NativeIndex = index; }
        public bool Equals(ResourceProcEnvelope other) => World == other.World && Sender == other.Sender &&
            Message == other.Message && Target == other.Target && Method == other.Method && NativeIndex == other.NativeIndex;
        public override bool Equals(object value) => value is ResourceProcEnvelope other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(World, Sender, Message, Target, Method, NativeIndex);
    }

    internal readonly struct ResourceProcSegment : IEquatable<ResourceProcSegment>
    {
        internal readonly ZDOID Source;
        internal readonly int Index;
        internal ResourceProcSegment(ZDOID source, int index) { Source = source; Index = index; }
        public bool Equals(ResourceProcSegment other) => Source == other.Source && Index == other.Index;
        public override bool Equals(object value) => value is ResourceProcSegment other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Source, Index);
    }

    internal readonly struct ResourceProcRow
    {
        internal readonly ResourceProcSegment Segment;
        internal readonly string Prefab;
        internal readonly int Tier;
        internal ResourceProcRow(ZDOID source, int index, string prefab, int tier)
        { Segment = new ResourceProcSegment(source, index); Prefab = prefab; Tier = tier; }
    }

    // Parsed once; the authoritative native actor/tool/header are immutable afterwards.
    internal sealed class ResourceProcOrigin
    {
        internal readonly string Epoch, Tool, Prefab;
        internal readonly long Attack, Author, Owner;
        internal readonly ZDOID Character;
        internal readonly Skills.SkillType Skill;
        internal readonly int ToolTier, Kind, Maximum;
        internal readonly float Raw;
        internal readonly ResourceProcEnvelope Envelope;
        internal string ActorKey => Epoch + "." + Attack;
        internal string SourceKey => Envelope.World + "." + Envelope.Sender + "." + ActorKey + "." + Envelope.Target;
        internal ResourceProcOrigin(string epoch, long attack, long author, ZDOID character, long owner,
            Skills.SkillType skill, string tool, int toolTier, float raw, int kind, int maximum,
            string prefab, ResourceProcEnvelope envelope)
        {
            Epoch = epoch; Attack = attack; Author = author; Character = character; Owner = owner;
            Skill = skill; Tool = tool; ToolTier = toolTier; Raw = raw; Kind = kind; Maximum = maximum;
            Prefab = prefab; Envelope = envelope;
        }
    }

    internal sealed class ResourceProcSourceState
    {
        internal readonly ResourceProcOrigin Origin;
        internal readonly float Expires;
        internal readonly Dictionary<ResourceProcSegment, ResourceProcRow> Rows = new Dictionary<ResourceProcSegment, ResourceProcRow>();
        internal readonly HashSet<ZDOID> Children = new HashSet<ZDOID>();
        internal bool Closed;
        internal ResourceProcSourceState(ResourceProcOrigin origin, float expires) { Origin = origin; Expires = expires; }
    }

    internal sealed class ResourceProcXpTicket
    {
        internal readonly ResourceProcSourceState State;
        internal readonly ResourceProcOrigin Origin;
        internal readonly ZDOID Target;
        internal readonly int Kind, Maximum;
        internal readonly string Prefab;
        internal object GroupIdentity => State;
        internal long AuthorId => Origin.Author;
        internal ZDOID CharacterId => Origin.Character;
        internal ZDOID RootTarget => State.Origin.Envelope.Target;
        internal ZDOID SourceTarget => Target;
        internal long TargetOwner => Origin.Owner;
        internal Skills.SkillType Skill => Origin.Skill;
        internal string ToolKey => Origin.Tool;
        internal int ToolTier => Origin.ToolTier;
        internal long NativeMessageId => Origin.Envelope.Message;
        internal ResourceProcXpTicket(ResourceProcSourceState state, ResourceProcOrigin origin, ZDOID target,
            int kind, int maximum, string prefab)
        { State = state; Origin = origin; Target = target; Kind = kind; Maximum = maximum; Prefab = prefab; }
    }

    internal sealed class ResourceProcCredit
    {
        internal double Remaining;
        // Native Skill.Raise discards surplus at one level crossing; observe its real result.
        internal static double Granted(float beforeLevel, float beforeAcc, float beforeNeed, float afterLevel, float afterAcc)
        {
            if (!Finite(beforeLevel) || !Finite(beforeAcc) || !Finite(beforeNeed) || !Finite(afterLevel) ||
                !Finite(afterAcc) || beforeAcc < 0 || afterAcc < 0 || afterLevel < beforeLevel || beforeLevel >= 100f) return 0;
            return Math.Max(0d, afterLevel > beforeLevel ? beforeNeed - beforeAcc + afterAcc : afterAcc - beforeAcc);
        }
        internal void Observe(double actualDelta, float first)
        { if (actualDelta > 0 && !double.IsNaN(actualDelta) && !double.IsInfinity(actualDelta) && Finite(first) && first >= 1f) Remaining += actualDelta / first; }
        internal float Deduct(float scaledFactor, float nativeRate)
        {
            if (!Finite(scaledFactor) || scaledFactor <= 0 || !Finite(nativeRate) || nativeRate <= 0 || Remaining <= 0) return scaledFactor;
            double deducted = Math.Min(Remaining, (double)scaledFactor * nativeRate);
            Remaining = Math.Max(0d, Remaining - deducted);
            return (float)Math.Max(0d, scaledFactor - deducted / nativeRate);
        }
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class ResourceProcActorEpisode
    {
        internal readonly Player Player;
        internal readonly long World, Author, Attack;
        internal readonly ZDOID Character;
        internal readonly string Epoch, Tool;
        internal readonly int ToolTier;
        internal readonly Skills.SkillType WeaponSkill;
        internal readonly float Raw, Expires;
        internal readonly Dictionary<ResourceProcEnvelope, ResourceProcOrigin> Targets = new Dictionary<ResourceProcEnvelope, ResourceProcOrigin>();
        internal readonly Dictionary<Skills.SkillType, ResourceProcCredit> Credits = new Dictionary<Skills.SkillType, ResourceProcCredit>();
        internal readonly HashSet<ResourceProcSegment> Seen = new HashSet<ResourceProcSegment>();
        internal readonly HashSet<ZDOID> Transferred = new HashSet<ZDOID>();
        internal readonly HashSet<ZDOID> CommittedRoots = new HashSet<ZDOID>();
        internal readonly List<ResourceProcOrigin> PendingOrigins = new List<ResourceProcOrigin>();
        internal readonly List<ResourceProcRow[]> PendingRows = new List<ResourceProcRow[]>();
        internal bool Returned, Failed;
        internal string Key => Epoch + "." + Attack;
        internal ResourceProcActorEpisode(Player player, long world, string epoch, long attack,
            string tool, int toolTier, float raw, float expires, Skills.SkillType weaponSkill)
        {
            Player = player; World = world; Author = player.GetPlayerID(); Character = player.GetZDOID();
            Epoch = epoch; Attack = attack; Tool = tool; ToolTier = toolTier; Raw = raw; Expires = expires; WeaponSkill = weaponSkill;
        }
        internal ResourceProcCredit Credit(Skills.SkillType skill)
        { if (!Credits.TryGetValue(skill, out var credit)) Credits.Add(skill, credit = new ResourceProcCredit()); return credit; }
    }
}
