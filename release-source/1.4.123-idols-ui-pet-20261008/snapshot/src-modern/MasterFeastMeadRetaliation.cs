using System;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace ValheimMastery
{
    [Flags]
    internal enum FeastMeadElements { None = 0, Poison = 1, Frost = 2, Fire = 4 }

    internal sealed class FeastMeadResistanceRecord
    {
        internal FeastMeadElements Elements;
    }

    internal static class FeastMeadRetaliation
    {
        private static readonly ConditionalWeakTable<StatusEffect, FeastMeadResistanceRecord> Records =
            new ConditionalWeakTable<StatusEffect, FeastMeadResistanceRecord>();

        internal static FeastMeadElements Elements(StatusEffect template)
        {
            if (!(template is SE_Stats stats) || stats.m_mods == null) return FeastMeadElements.None;
            FeastMeadElements result = FeastMeadElements.None;
            foreach (HitData.DamageModPair pair in stats.m_mods)
            {
                var modifier = pair.m_modifier;
                if (modifier != HitData.DamageModifier.Resistant && modifier != HitData.DamageModifier.VeryResistant &&
                    modifier != HitData.DamageModifier.SlightlyResistant && modifier != HitData.DamageModifier.Immune) continue;
                if ((pair.m_type & HitData.DamageType.Poison) != 0) result |= FeastMeadElements.Poison;
                if ((pair.m_type & HitData.DamageType.Frost) != 0) result |= FeastMeadElements.Frost;
                if ((pair.m_type & HitData.DamageType.Fire) != 0) result |= FeastMeadElements.Fire;
            }
            return result;
        }

        internal static void Register(StatusEffect instance, StatusEffect template, bool mountains)
        {
            if (instance == null) return;
            Records.Remove(instance);
            FeastMeadElements elements = mountains ? Elements(template) : FeastMeadElements.None;
            if (elements != FeastMeadElements.None)
                Records.Add(instance, new FeastMeadResistanceRecord { Elements = elements });
        }

        internal static FeastMeadElements Active(Player player)
        {
            FeastMeadElements result = FeastMeadElements.None;
            foreach (StatusEffect effect in player.GetSEMan().GetStatusEffects())
                if (effect != null && effect.m_ttl > 0f && effect.m_time < effect.m_ttl && Records.TryGetValue(effect, out var record))
                    result |= record.Elements;
            return result;
        }

        internal static bool Owner(Player player) => player != null && player == Player.m_localPlayer &&
            player.m_nview != null && player.m_nview.IsValid() && player.m_nview.IsOwner();

        internal static void Return(Player player, Character attacker, float received)
        {
            float amount = received * .25f;
            if (amount <= 0f || !Owner(player) || attacker == null || attacker == player || attacker.IsDead() ||
                attacker.IsPlayer() || attacker.m_nview == null || !attacker.m_nview.IsValid() || !BaseAI.IsEnemy(player, attacker)) return;
            FeastMeadElements active = Active(player);
            TryReturn(player, attacker, active, FeastMeadElements.Poison, amount, "poison");
            TryReturn(player, attacker, active, FeastMeadElements.Frost, amount, "frost");
            TryReturn(player, attacker, active, FeastMeadElements.Fire, amount, "fire");
        }

        private static void TryReturn(Player player, Character attacker, FeastMeadElements active, FeastMeadElements element, float amount, string key)
        {
            if ((active & element) == 0 || !PerkCooldownStateService.TryConsume(player, "cooking70_mead_return_" + key, 5d)) return;
            var hit = new HitData {
                m_variant = 1270, m_hitType = HitData.HitType.EnemyHit, m_skill = Skills.SkillType.None,
                m_skillRaiseAmount = 0f, m_dodgeable = false, m_blockable = false,
                m_point = attacker.GetCenterPoint(), m_dir = (attacker.GetCenterPoint() - player.GetCenterPoint()).normalized };
            if (element == FeastMeadElements.Poison) hit.m_damage.m_poison = amount;
            else if (element == FeastMeadElements.Frost) hit.m_damage.m_frost = amount;
            else hit.m_damage.m_fire = amount;
            hit.SetAttacker(player);
            PerkHitContext context = PerkRuntimeService.GetHitContext(hit);
            context.IsPerkGenerated = true;
            context.PerkId = "cooking70_mead_return_" + key;
            context.AllowSelfProc = context.AllowOtherPerkProc = false;
            context.IgnoreReflect = context.IgnoreExecution = context.IgnoreOverdrawPayload = true;
            context.XpMultiplier = 0f;
            // The variant also survives serialization, unlike the local CWT.
            // Enemy owner resolves elemental damage/debuff/resistance normally.
            attacker.Damage(hit);
        }
    }

    internal sealed class FeastMeadIncomingHit
    {
        [ThreadStatic] internal static FeastMeadIncomingHit Current;
        internal Player Player;
        internal HitData Hit;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class FeastMeadIncomingScopePatch
    {
        private static void Prefix(Character __instance, HitData hit, out FeastMeadIncomingHit __state)
        {
            __state = FeastMeadIncomingHit.Current;
            FeastMeadIncomingHit.Current = __instance is Player player && FeastMeadRetaliation.Owner(player) && hit != null &&
                !PerkRuntimeService.IsPerkGenerated(hit) && (hit.m_hitType == HitData.HitType.EnemyHit ||
                    hit.m_hitType == HitData.HitType.Undefined && (hit.m_dodgeable || hit.m_blockable))
                ? new FeastMeadIncomingHit { Player = player, Hit = hit } : null;
        }
        private static void Finalizer(FeastMeadIncomingHit __state) => FeastMeadIncomingHit.Current = __state;
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class FeastMeadActualDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit, out float __state)
        {
            var incoming = FeastMeadIncomingHit.Current;
            __state = incoming != null && ReferenceEquals(incoming.Player, __instance) && ReferenceEquals(incoming.Hit, hit)
                ? __instance.GetHealth() : -1f;
        }
        private static void Postfix(Character __instance, HitData hit, float __state)
        {
            if (__state <= 0f || !(__instance is Player player)) return;
            float received = __state - Math.Max(0f, player.GetHealth());
            if (received > 0f) FeastMeadRetaliation.Return(player, hit.GetAttacker(), received);
        }
    }
}
