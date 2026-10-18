using System;
namespace ValheimMastery
{
    // Read-only combat friendship seam. Never changes faction, tame, home,
    // movement, healing policy, items or native spell/collision admission.
    internal static class Ashlands70Hostility
    {
        private static bool Current(Character body)
        {
            if (MasteryPlugin.Settings?.Enabled?.Value != true || !MasterIdolLoadIdentity.LoadReady ||
                Ashlands70Cast.CurrentWorld == 0 || Ashlands70Cast.Now <= 0 || body?.m_nview?.IsValid() != true) return false;
            ZDO data = body.m_nview.GetZDO();
            // Reject old instances after load/session/manager replacement.
            return data != null && ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data);
        }
        internal static bool SourceMarked(Character body)
        {
            if (!Current(body) || body.IsPlayer()) return false;
            ZDO data = body.m_nview.GetZDO();
            return data.GetLong(Ashlands70Madness.WorldKey, 0) == Ashlands70Cast.CurrentWorld &&
                Ashlands70Rules.Active(Ashlands70Cast.Now, data.GetLong(Ashlands70Madness.UntilKey, 0));
        }
        // Marker query for dedicated admission: definitions are available even
        // when no physical Character instance is streamed. False is not an
        // authorization result; callers separately prove current live roster,
        // native prefab, home/presence and context before using this veto.
        internal static bool SourceMarkedCurrentData(ZDO data)
        {
            if (MasteryPlugin.Settings?.Enabled?.Value != true || !MasterIdolLoadIdentity.LoadReady ||
                Ashlands70Cast.CurrentWorld == 0 || Ashlands70Cast.Now <= 0 || data == null ||
                !ReferenceEquals(ZDOMan.instance?.GetZDO(data.m_uid), data)) return false;
            var definition = ZNetScene.instance?.GetPrefab(data.GetPrefab());
            if (definition == null || definition.GetComponent<Character>() == null || definition.GetComponent<Player>() != null) return false;
            return data.GetLong(Ashlands70Madness.WorldKey, 0) == Ashlands70Cast.CurrentWorld &&
                Ashlands70Rules.Active(Ashlands70Cast.Now, data.GetLong(Ashlands70Madness.UntilKey, 0));
        }
        internal static bool HostilePair(Character first, Character second)
            => first != second && Current(first) && Current(second) && !first.IsDead() && !second.IsDead() &&
               (SourceMarked(first) || SourceMarked(second));
        internal static bool OffensivePair(Character source, Character target)
            => source != target && Current(source) && Current(target) && !target.IsDead() &&
               (SourceMarked(source) || SourceMarked(target));
        // Native emitted effects may still resolve a dead source. Its CURRENT
        // unexpired mark applies at impact; expiry/unresolved source restores
        // friendship. No permanent launch mark or invented attacker is stored.
    }
}
