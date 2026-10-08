namespace ValheimMastery
{
    // Retained only as the learned-state key referenced by the existing
    // movement sync. The old target-side stagger RPC and its registration
    // have been removed; Dodge70 is now an owner-local stamina refund.
    internal static class PerfectDodgeStagger
    {
        internal static readonly int LearnedKey = "vm_dodge70_learned".GetStableHashCode();
    }
}
