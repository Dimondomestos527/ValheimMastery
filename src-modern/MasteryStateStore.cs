using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ValheimMastery
{
    /// <summary>
    /// Shared in-memory state owned by a concrete Unity/game object.  This deliberately
    /// distinguishes session/transient state from Player.m_customData, which remains the
    /// store for persistence such as milestone unlocks and cooldown timestamps.
    /// </summary>
    internal static class MasteryStateStore
    {
        private sealed class StateBag
        {
            internal readonly Dictionary<Type, object> Values = new Dictionary<Type, object>();
        }

        private static readonly ConditionalWeakTable<object, StateBag> PlayerStates = new ConditionalWeakTable<object, StateBag>();
        private static readonly ConditionalWeakTable<object, StateBag> TargetStates = new ConditionalWeakTable<object, StateBag>();

        internal static T GetPlayerState<T>(Player player) where T : class, new() => Get<T>(PlayerStates, player);
        internal static T GetTargetState<T>(Character target) where T : class, new() => Get<T>(TargetStates, target);
        internal static T GetObjectState<T>(object owner) where T : class, new() => Get<T>(TargetStates, owner);
        internal static bool TryGetObjectState<T>(object owner, out T state) where T : class => TryGet(TargetStates, owner, out state);

        internal static bool TryGetPlayerState<T>(Player player, out T state) where T : class
            => TryGet(PlayerStates, player, out state);

        internal static bool TryGetTargetState<T>(Character target, out T state) where T : class
            => TryGet(TargetStates, target, out state);

        internal static void RemovePlayerState<T>(Player player) where T : class => Remove<T>(PlayerStates, player);
        internal static void RemoveTargetState<T>(Character target) where T : class => Remove<T>(TargetStates, target);

        private static T Get<T>(ConditionalWeakTable<object, StateBag> table, object owner) where T : class, new()
        {
            if (owner == null) return null;
            StateBag bag = table.GetOrCreateValue(owner);
            Type key = typeof(T);
            if (bag.Values.TryGetValue(key, out object existing)) return existing as T;
            T created = new T();
            bag.Values[key] = created;
            return created;
        }

        private static bool TryGet<T>(ConditionalWeakTable<object, StateBag> table, object owner, out T state) where T : class
        {
            state = null;
            return owner != null && table.TryGetValue(owner, out StateBag bag) &&
                bag.Values.TryGetValue(typeof(T), out object existing) && (state = existing as T) != null;
        }

        private static void Remove<T>(ConditionalWeakTable<object, StateBag> table, object owner) where T : class
        {
            if (owner != null && table.TryGetValue(owner, out StateBag bag)) bag.Values.Remove(typeof(T));
        }
    }
}
