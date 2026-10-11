using System;
namespace ValheimMastery
{
    internal enum WorkbenchOperationKind { Learn, Switch, Disable, Dismantle, Destroy }
    // Material rows below are legacy immutable journal compatibility only.
    internal readonly struct WorkbenchMaterial
    {
        internal readonly string Prefab; internal readonly int Amount;
        internal WorkbenchMaterial(string prefab, int amount) { Prefab= prefab; Amount=amount; }
    }
    // Native adapters must certify CURRENT server actor/world/UID/level/access.
    // This is server-private context; no client boolean is an authority proof.
    internal sealed class WorkbenchEnchantmentContext
    {
        internal long World, Actor; internal string Station, NativeUid, Network;
        internal int NativeLevel; internal Func<bool> Revalidate;
    }
    internal sealed class WorkbenchEnchantmentSnapshot
    {
        internal long World, Revision; internal string Station, Active, Reason;
        internal int NativeLevel; internal string[] Learned, Available;
        internal string PendingOperation="",PendingChoice="",PendingKind="";internal long PendingActor;
        internal WorkbenchEnchantmentChoiceCooldown[] Cooldowns=Array.Empty<WorkbenchEnchantmentChoiceCooldown>();
    }
    // Client display projection, never a cooldown admission authority.
    internal sealed class WorkbenchEnchantmentChoiceCooldown
    {
        internal string Choice;
        internal bool Ready,Pending;
        internal double RemainingSeconds;
    }
}
