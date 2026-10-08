namespace ValheimMastery
{
    internal enum WorkshopLeaseReadiness { Waiting, Ready, Abort }
    internal static class WorkshopLeaseRules
    {
        internal const int MaximumSnapshotBytes = 262144;
        internal static WorkshopLeaseReadiness Evaluate(long currentOwner, long previousOwner, long serverOwner,
            bool inUse, bool connected, byte[] replicated, byte[] acknowledged)
        {
            if (serverOwner == 0 || previousOwner == 0 || inUse || !connected ||
                (currentOwner != previousOwner && currentOwner != serverOwner) ||
                (acknowledged != null && (acknowledged.Length == 0 || acknowledged.Length > MaximumSnapshotBytes)))
                return WorkshopLeaseReadiness.Abort;
            if (currentOwner != serverOwner || acknowledged == null || replicated == null || replicated.Length != acknowledged.Length)
                return WorkshopLeaseReadiness.Waiting;
            for (int i = 0; i < replicated.Length; i++)
                if (replicated[i] != acknowledged[i]) return WorkshopLeaseReadiness.Waiting;
            return WorkshopLeaseReadiness.Ready;
        }
    }
}
