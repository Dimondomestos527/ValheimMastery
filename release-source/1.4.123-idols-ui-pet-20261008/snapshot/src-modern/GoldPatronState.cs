using System;

namespace ValheimMastery
{
    internal sealed class GoldPatronWallet
    {
        internal bool Unlocked;
        internal float Favor; // Total, INCLUDING all reserved funds; cap is 1000.
        internal float Held;
        internal float Available => Math.Max(0f, Favor - Held);
        internal GoldPatronWallet Copy() => (GoldPatronWallet)MemberwiseClone();
    }

    // The authenticated coordinator freezes these fields BEFORE disclosing a grant.
    // Generation is a player/world monotonic request sequence, not a client timestamp.
    internal sealed class GoldPatronGrant
    {
        internal string Patron, Action, Token;
        internal float Cost;
        internal long Generation;
        internal bool Prepared;
        internal GoldPatronGrant Copy() => (GoldPatronGrant)MemberwiseClone();
        internal bool Same(GoldPatronGrant other) => other != null && Patron == other.Patron &&
            Action == other.Action && Token == other.Token && Cost == other.Cost &&
            Generation == other.Generation && Prepared == other.Prepared;
    }

    internal enum GoldPatronOutcome { Committed = 1, Unused = 2 }
    internal enum GoldAwardResult { Rejected, Accepted, Duplicate, Expired, Unavailable }
    internal sealed class GoldPatronResult
    {
        internal GoldPatronGrant Grant;
        internal GoldPatronOutcome Outcome;
        internal GoldPatronResult Copy() => new GoldPatronResult { Grant = Grant.Copy(), Outcome = Outcome };
    }

    // No runtime implementation exists yet. A future combat coordinator must verify
    // a durable, authenticated owner terminal journal bound to the entire grant.
    // Timeout/disconnect/epoch/local deletion MUST NOT produce this evidence.
    internal interface IGoldPreparedEvidence
    {
        bool Verify(long playerId, GoldPatronGrant frozenGrant, GoldPatronOutcome outcome);
    }
}
