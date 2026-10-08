using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    // Superseded by Polearm35AutoSpin: the old global slash multiplier is intentionally removed.
    [HarmonyPatch(typeof(Character), "UpdateGroundContact")]
    internal static class Jumping35SafeFallPatch
    {
        private static void Prefix(Character __instance)
        {
            Player player = __instance as Player;
            if (player == Player.m_localPlayer && player != null && player.m_groundContact && PerkRuntimeService.HasPerk(player, Skills.SkillType.Jump, 35))
                player.m_maxAirAltitude -= 4f;
        }
    }

    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    [HarmonyPriority(Priority.First)]
    internal static class Jumping70FallProtectionPatch
    {
        private const string PerkId = "jump_70";

        private static void Prefix(Character __instance, HitData hit)
        {
            Player player = __instance as Player;
            if (player == null || player != Player.m_localPlayer || player.IsDead() || hit == null || hit.m_hitType != HitData.HitType.Fall ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Jump, 70) ||
                !PerkRuntimeService.TryMarkApplied(hit, PerkId))
                return;

            float rawDamage = hit.GetTotalDamage();
            float rate = Mathf.Max(0.0001f, Game.m_localDamgeTakenRate);
            if (rawDamage * rate < player.GetHealth())
                return;
            if (!PerkCooldownStateService.TryConsume(player, PerkId, 300d))
                return;

            float allowedRawDamage = Mathf.Max(0f, player.GetHealth() - 1f) / rate;
            hit.ApplyModifier(rawDamage > 0f ? allowedRawDamage / rawDamage : 0f);
            MovementPerkService.Proc(player, "jump_70", "jump_70_save", Skills.SkillType.Jump, 70);
            MasteryPlugin.Log.LogInfo("Perk proc: Jumping70 prevented lethal fall damage; cooldown=300s.");
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetTotalWeight))]
    internal static class MasteryInventoryWeightPatch
    {
        private static void Postfix(Inventory __instance, ref float __result)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return;
            __result = Mathf.Max(0f, __result - PerkProfessionService.GetInventoryWeightDiscount(player, __instance) - PerkProfessionService.GetMasterFeastWeightDiscount(player, __instance));
        }
    }

    internal sealed class Blocking35ArmorState
    {
        internal float ArmorUntil;
        internal float ArmorBonus;
    }

    internal static class Blocking35Service
    {
        private const float Duration = 15f;
        private const float WaveRadius = 7f;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, Blocking35ArmorState> States = new System.Runtime.CompilerServices.ConditionalWeakTable<Player, Blocking35ArmorState>();

        internal static void TriggerGuardBreak(Player player, ItemDrop.ItemData blocker, Character attacker, Vector3 point)
        {
            if (player == null || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Blocking, 35) ||
                !PerkCooldownStateService.TryConsume(player, "blocking_35_guardbreak", 300d))
                return;

            bool shield = blocker?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            bool fists70 = blocker?.m_shared?.m_skillType == Skills.SkillType.Unarmed &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Unarmed, 70);

            // Every valid guard-break restores health. Stamina and armor are the shield-only payoff.
            player.Heal(player.GetMaxHealth() * 0.20f, true);
            if (shield)
            {
                PerkRuntimeService.RestoreStamina(player, player.GetMaxStamina() * 0.20f);
                Blocking35ArmorState state = States.GetOrCreateValue(player);
                state.ArmorUntil = Time.time + Duration;
                state.ArmorBonus = 5f * Mathf.Max(1, TierDatabase.GetItemTier(blocker));
                PerkVisualService.StartBlockingArmorAura(player, Duration);
            }

            // Shield break always creates the cone. Bare hands/cestus gain it at Fists 70, but never armor.
            if (shield || fists70)
                TriggerWave(player, attacker, point);

            PerkVisualService.PlayProc(player, "blocking_35_guardbreak", point, true, false);
            player.Message(MessageHud.MessageType.Center, PerkLocalization.Localize("$vm_perk_blocking_35_name"), 0, player.m_textIcon);
        }

        internal static float GetArmorBonus(Player player)
        {
            if (player == null || !States.TryGetValue(player, out Blocking35ArmorState state) || state.ArmorUntil < Time.time) return 0f;
            return state.ArmorBonus;
        }

        private static void TriggerWave(Player player, Character attacker, Vector3 point)
        {
            Vector3 origin = player.GetCenterPoint();
            Vector3 forward = attacker != null ? (attacker.transform.position - player.transform.position).normalized : player.transform.forward;
            foreach (Character target in Character.GetAllCharacters())
            {
                if (target == null || target == player || target.IsDead() || target.IsBoss()) continue;
                Vector3 delta = target.GetCenterPoint() - origin;
                if (delta.sqrMagnitude > WaveRadius * WaveRadius) continue;
                Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
                if (horizontal.sqrMagnitude < 0.01f || Vector3.Dot(forward, horizontal.normalized) < 0.5f) continue; // 120 degree cone
                if (!BaseAI.IsEnemy(player, target)) continue;
                target.Stagger(horizontal.normalized);
                PerkVisualService.PlayAtWorldPosition(player, "blocking_35", target.GetCenterPoint(), false);
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetBodyArmor))]
    internal static class Blocking35ArmorPatch
    {
        private static void Postfix(Player __instance, ref float __result) => __result += Blocking35Service.GetArmorBonus(__instance);
    }
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class BlockingPerksPatch
    {
        internal sealed class BlockState
        {
            internal Player Player;
            internal Character Attacker;
            internal ItemDrop.ItemData Blocker;
            internal float Damage;
            internal bool Perfect;
            internal bool WasStaggering;
            internal bool WasStaggerWhenBlocked;
        }

        private static void Prefix(Humanoid __instance, HitData hit, Character attacker, out BlockState __state)
        {
            Player player = __instance as Player;
            ItemDrop.ItemData blocker = player?.GetCurrentBlocker();
            ExperienceContext.ObserveBlock(player, attacker);
            Polearm35ParryResolutionService.Begin(player, attacker, player != null && player.m_blockTimer >= 0f && player.m_blockTimer <= Humanoid.m_perfectBlockInterval);
            __state = new BlockState
            {
                Player = player, Attacker = attacker, Blocker = blocker,
                Damage = hit?.GetTotalBlockableDamage() ?? 0f,
                Perfect = player != null && player.m_blockTimer >= 0f && player.m_blockTimer <= Humanoid.m_perfectBlockInterval,
                WasStaggering = player != null && player.IsStaggering(),
                WasStaggerWhenBlocked = player != null && player.m_staggerWhenBlocked
            };
        }

        private static void Postfix(HitData hit, bool __result, BlockState __state)
        {
            if (__state?.Player == null || hit == null) return;
            MasteryEventBus.Publish(new BlockEvent { Defender = __state.Player, Attacker = __state.Attacker, Blocker = __state.Blocker, BlockedDamage = __state.Damage, IsPerfectParry = __state.Perfect, Succeeded = __result });
#if !MASTERY_SHIELD35_EXPERIMENT
            if (__result) BlockingStoredPressureService.ObserveSuccessfulBlock(__state.Player, __state.Attacker, __state.Perfect);
#endif
            if (__result && __state.Perfect) MasteryExtendedEventBus.Publish(new ParryEvent { Defender = __state.Player, Attacker = __state.Attacker, Blocker = __state.Blocker, BlockedDamage = __state.Damage });
            PolearmParryContext polearmContext = Polearm35ParryResolutionService.End(__result);
            if (polearmContext != null) Polearm35AutoSpinService.Queue(__state.Player, polearmContext);
            // Blocking 35 final design is Stored Pressure. Legacy guard-break rewards are not dispatched here; a vanilla guard break remains vanilla.
            if (!__result || __state.Attacker == null || __state.Attacker.IsDead() ||
                !PerkRuntimeService.HasPerk(__state.Player, Skills.SkillType.Blocking, 70) ||
                PerkRuntimeService.IsPerkGenerated(hit)) return;

            if (Blocking70ProjectileParryPatch.IsManualBlock) return;
            Blocking70ReflectionService.HandleBlock(__state.Player, __state.Attacker, __state.Damage, __state.Perfect);
        }
    }
}
