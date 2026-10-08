using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class TargetEffect
    {
        internal string Id;
        internal ZDOID SourcePlayer;
        internal int Stacks;
        internal float Strength;
        internal float ExpiresAt;
    }

    internal sealed class TargetEffectState
    {
        internal readonly Dictionary<string, TargetEffect> Effects = new Dictionary<string, TargetEffect>(StringComparer.Ordinal);
    }

    internal static class TargetEffectService
    {
        internal static TargetEffect Apply(Character target, string id, Player source, int stacks, float strength, float duration)
        {
            if (target == null || string.IsNullOrEmpty(id)) return null;
            TargetEffectState state = MasteryStateStore.GetTargetState<TargetEffectState>(target);
            if (!state.Effects.TryGetValue(id, out TargetEffect effect))
            {
                effect = new TargetEffect { Id = id };
                state.Effects[id] = effect;
            }
            effect.SourcePlayer = source != null ? source.GetZDOID() : ZDOID.None;
            effect.Stacks = Mathf.Max(0, stacks);
            effect.Strength = Mathf.Max(0f, strength);
            effect.ExpiresAt = Time.time + Mathf.Max(0f, duration);
            return effect;
        }

        internal static bool TryGet(Character target, string id, out TargetEffect effect)
        {
            effect = null;
            if (target == null || string.IsNullOrEmpty(id) || !MasteryStateStore.TryGetTargetState<TargetEffectState>(target, out TargetEffectState state) ||
                !state.Effects.TryGetValue(id, out effect)) return false;
            if (effect.ExpiresAt > Time.time) return true;
            state.Effects.Remove(id);
            effect = null;
            return false;
        }

        internal static void Remove(Character target, string id)
        {
            if (target != null && MasteryStateStore.TryGetTargetState<TargetEffectState>(target, out TargetEffectState state)) state.Effects.Remove(id);
        }

        internal static IEnumerable<TargetEffect> GetActive(Character target)
        {
            if (!MasteryStateStore.TryGetTargetState<TargetEffectState>(target, out TargetEffectState state)) yield break;
            foreach (TargetEffect effect in new List<TargetEffect>(state.Effects.Values))
                if (effect.ExpiresAt > Time.time) yield return effect;
                else state.Effects.Remove(effect.Id);
        }
    }
}
