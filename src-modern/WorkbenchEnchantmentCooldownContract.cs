using System;
namespace ValheimMastery
{
    internal enum WorkbenchCooldownStatus { Unknown, Ready, Cooldown, Pending, Committed, Cancelled }
    internal enum WorkbenchCooldownPermitPhase { Reserved, CommitIntent, Committed, Cancelled }
    // Server-private callbacks/identity, never constructed from an untrusted wire bool.
    internal sealed class WorkbenchCooldownContext
    {
        internal long Character,World;
        internal string Station,NativeUid,NativeWitness,CharacterUid;
        internal Func<bool> IdentityCurrent,MayLearn;
        internal Func<string,bool> OriginalNativeIdentity;
    }
    internal sealed class WorkbenchCooldownPermit
    {
        internal string Operation,Token,Choice,Station,NativeUid,NativeWitness;
        internal long Character,World,Generation;
        internal WorkbenchCooldownPermitPhase Phase;
    }
    internal sealed class WorkbenchCooldownView
    {
        internal string Choice;
        internal WorkbenchCooldownStatus Status;
        internal double RemainingSeconds;
        internal long Generation;
        internal WorkbenchCooldownPermit Permit;
        internal bool Ready=>Status==WorkbenchCooldownStatus.Ready;
        internal bool Pending=>Status==WorkbenchCooldownStatus.Pending||Status==WorkbenchCooldownStatus.Unknown;
    }
    // Filled/read back by the world journal owner. VerifyPersisted re-reads exact
    // immutable result and digest; no client ACK or in-memory grant is a certificate.
    internal sealed class WorkbenchCooldownGrantCertificate
    {
        internal string Operation,Token,Choice,Station,OriginalNativeUid,NativeWitness;
        internal long Character,World,GrantedAtUtcTicks,ReadyAtUtcTicks,WorldRevision;
        internal Func<bool> VerifyPersisted;
    }
    internal enum WorkbenchCooldownNoGrantKind { Cancelled, NoOp }
    internal sealed class WorkbenchCooldownNoGrantCertificate
    {
        internal string Operation,Token,Choice,Station,OriginalNativeUid,NativeWitness;
        internal long Character,World,WorldRevision;
        internal WorkbenchCooldownNoGrantKind Kind;
        internal Func<bool> VerifyPersisted;
    }
    internal interface IWorkbenchEnchantmentCooldownAuthority
    {
        WorkbenchCooldownView Query(WorkbenchCooldownContext context,string choice);
        WorkbenchCooldownView Inspect(WorkbenchCooldownContext context,string operation,string choice);
        WorkbenchCooldownView Reserve(WorkbenchCooldownContext context,string operation,string choice);
        bool PrepareCommit(WorkbenchCooldownContext context,WorkbenchCooldownPermit permit);
        bool Complete(WorkbenchCooldownContext context,WorkbenchCooldownPermit permit,WorkbenchCooldownGrantCertificate grant);
        bool Cancel(WorkbenchCooldownContext context,WorkbenchCooldownPermit permit,WorkbenchCooldownNoGrantCertificate noGrant);
    }
}
