using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace ValheimMastery
{
    internal sealed class PerkPlayerTransientState
    {
        // Fists 35 / global combat-flow state.
        internal int FistComboStacks;
        internal int FistAttackSerial;
        internal int LastFistHitSerial;
        internal float LastFistHitAt;
        internal int AdrenalineComboStacks;
        internal float LastCombatHitAt;
        internal int LastCombatHitFrame = -1;
    }

    internal static class PerkTransientStateService
    {
        private static readonly ConditionalWeakTable<Player, PerkPlayerTransientState> States =
            new ConditionalWeakTable<Player, PerkPlayerTransientState>();

        internal static PerkPlayerTransientState For(Player player) => States.GetOrCreateValue(player);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDamaged))]
    internal static class Running35EmergencyStaminaPatch
    {
        private static void Postfix(Player __instance, HitData hit)
        {
            if (!MovementPerkService.OwnerReady(__instance) || !MovementPerkService.DirectAttack(__instance, hit) || __instance.GetHealth() <= 0f || __instance.GetHealth() >= __instance.GetMaxHealth() * 0.5f ||
                !PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Run, 35) ||
                !PerkCooldownStateService.TryConsume(__instance, "run_35", 300d))
                return;

            PerkRuntimeService.RestoreStamina(__instance, __instance.GetMaxStamina() * 0.35f);
            Stride70Service.State(__instance).EmergencyUntil = Time.time + 10f;
            RoadRhythmVisual.Set(__instance, Stride70Service.GetStacks(__instance));
            MovementPerkService.Proc(__instance, "run_35", "run_35", Skills.SkillType.Run, 35);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDamaged))]
    internal static class Running70TacticalRetreatPatch
    {
        // Superseded by Stride70Perk: Run70 no longer activates from damage.
        private static void Postfix(Player __instance, HitData hit) { }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetRunSpeedFactor))]
    internal static class Running70SpeedPatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            int stacks = Stride70Service.GetStacks(__instance);
            __result *= 1f + 0.20f * stacks / 3f + Stride70Service.EmergencySpeed(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetJogSpeedFactor))]
    internal static class Running70JogSpeedPatch
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            __result *= 1f + Stride70Service.EmergencySpeed(__instance);
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    [HarmonyPriority(Priority.First)]
    internal static class CombatPerks35OutgoingHitPatch
    {
        private static void Prefix(HitData hit)
        {
            if (hit == null)
                return;
            Player attacker = hit.GetAttacker() as Player;
            if (attacker == null)
                return;

            if (hit.m_skill == Skills.SkillType.Clubs)
                CombatSkillScalingService.ApplyClubStaggerScaling(attacker, hit);

            // Modify only the packet; vanilla victim awareness decides whether backstab applies.
            if (hit.m_skill == Skills.SkillType.Knives && hit.m_backstabBonus > 1f && !PerkRuntimeService.IsPerkGenerated(hit) &&
                PerkRuntimeService.TryMarkApplied(hit, "knife_passive_backstab"))
                hit.m_backstabBonus += .02f * Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(attacker, Skills.SkillType.Knives), 0f, 100f);

        }
    }
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    internal static class Knives35KillPatch
    {
        private static bool Prepare() => false;
        private static void Prefix(Character __instance, out bool __state) => __state = __instance != null && !__instance.IsDead();

        private static void Postfix(Character __instance, HitData hit, bool __state)
        {
            if (!__state || __instance == null || !__instance.IsDead() || hit == null || hit.m_skill != Skills.SkillType.Knives)
                return;
            Player attacker = hit.GetAttacker() as Player;
            if (attacker == null) return;
            MasteryPlugin.Log.LogInfo("[Knife35] confirmed knife kill player=" + attacker.GetPlayerName());
            ShadowStep35Service.ArmFromConfirmedKill(attacker);
        }
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
#if !MASTERY_RELEASE_SAFE
    internal static class SpearsThrowPassiveBalance
    {
        internal static float VelocityFactor(float level) => 1f + 0.003f * Mathf.Clamp(level, 0f, 100f);
        internal static float ForceFactor(float level) => 1f + 0.004f * Mathf.Clamp(level, 0f, 100f);
    }

    internal static class SpearsThrownSkillPassivePatch
    {
        private sealed class ScaledMarker { }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Projectile, ScaledMarker> Scaled =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Projectile, ScaledMarker>();

        private static void Postfix(Projectile __instance, Character owner, HitData hitData, ItemDrop.ItemData item)
        {
            Player player = owner as Player;
            if (__instance == null || player == null || hitData?.m_skill != Skills.SkillType.Spears ||
                item?.m_shared?.m_skillType != Skills.SkillType.Spears) return;
            if (Scaled.TryGetValue(__instance, out _)) return;
            Scaled.Add(__instance, new ScaledMarker());
            float level = Mathf.Clamp(PerkRuntimeService.GetActualSkillLevel(player, Skills.SkillType.Spears), 0f, 100f);
            __instance.m_vel *= SpearsThrowPassiveBalance.VelocityFactor(level);
            __instance.m_attackForce *= SpearsThrowPassiveBalance.ForceFactor(level);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[SpearsPassive] level=" + level.ToString("0.#") +
                    " velocity=" + __instance.m_vel.magnitude.ToString("0.##") +
                    " force=" + __instance.m_attackForce.ToString("0.##"));
        }
    }
#else
    internal static class Spears35ProjectilePatch
    {
        private static void Postfix(Projectile __instance, Character owner, HitData hitData)
        {
            Player player = owner as Player;
            if (__instance == null || hitData == null || hitData.m_skill != Skills.SkillType.Spears ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Spears, 35)) return;
            // Keep native spear secondary launch animation/audio. The perk
            // changes flight, not the presentation of this ordinary throw.
            __instance.m_vel *= 2f;
            __instance.m_damage.Modify(2f);
            __instance.m_gravity *= 0.35f;
        }
    }
#endif

    [HarmonyPatch(typeof(Player), nameof(Player.UseEitr))]
    internal static class ElementalMagic35FreeCastPatch
    {
        // Replaced by staff-specific charged fire and frost-ground mechanics.
        private static bool Prepare() => false;
        private static void Prefix(Player __instance, ref float v)
        {
            ItemDrop.ItemData weapon = __instance?.GetCurrentWeapon();
            // Ice35 has been replaced by ground control, not the old random free cast.
            if (IceStaff35Trail.IsIceStaff(weapon)) return;
            if (v > 0f && weapon?.m_shared?.m_skillType == Skills.SkillType.ElementalMagic &&
                PerkRuntimeService.HasPerk(__instance, Skills.SkillType.ElementalMagic, 35) && PerkRuntimeService.RollChance(0.20f))
                { v = 0f; PerkVisualService.PlayProc(__instance, "elementalmagic_35", false, false); }
        }
    }

    [HarmonyPatch(typeof(Fish), "RPC_Pickup")]
    internal static class Fishing35PreserveBaitPatch
    {
        internal sealed class CatchState { internal string Bait, Fish; internal int Before; internal float At; }
        private static readonly ConditionalWeakTable<Fish, CatchState> Pending = new ConditionalWeakTable<Fish, CatchState>();
        internal static void CaptureRequest(Fish fish, Humanoid character)
        {
            if (character != Player.m_localPlayer || fish?.m_fishingFloat == null || fish.m_pickupItem == null) return;
            ItemDrop item = fish.m_pickupItem.GetComponent<ItemDrop>();
            if (item == null) return;
            Pending.Remove(fish);
            Pending.Add(fish, new CatchState { Bait = fish.m_fishingFloat.GetBait(), Fish = item.m_itemData.m_shared.m_name, At = Time.time });
        }
        private static void Prefix(Fish __instance, out CatchState __state)
        {
            __state = null;
            Player player = Player.m_localPlayer;
            ItemDrop item = __instance?.m_pickupItem?.GetComponent<ItemDrop>();
            if (player == null || item == null ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Fishing, 35)) return;
            string bait = __instance.m_fishingFloat?.GetBait();
            if (bait == null && Pending.TryGetValue(__instance, out CatchState pending) && Time.time - pending.At <= 10f) bait = pending.Bait;
            Pending.Remove(__instance);
            if (string.IsNullOrEmpty(bait)) return;
            __state = new CatchState { Bait = bait, Fish = item.m_itemData.m_shared.m_name,
                Before = player.GetInventory().CountItems(item.m_itemData.m_shared.m_name) };
        }
        private static void Postfix(CatchState __state)
        {
            Player player = Player.m_localPlayer;
            if (__state == null || player == null || player.GetInventory().CountItems(__state.Fish) <= __state.Before ||
                !PerkRuntimeService.RollChance(.50f)) return;
            GameObject bait = ObjectDB.instance?.GetItemPrefab(__state.Bait);
            if (bait != null)
            {
                if (player.GetInventory().CanAddItem(bait, 1)) player.GetInventory().AddItem(bait, 1);
                else UnityEngine.Object.Instantiate(bait, player.transform.position + UnityEngine.Vector3.up, UnityEngine.Quaternion.identity)
                    .GetComponent<ItemDrop>()?.SetStack(1);
                VanillaBonusFeedback.Show(player, 1);
                PerkVisualService.PlayProc(player, "fishing_35", false, false);
            }
        }
    }

    [HarmonyPatch(typeof(Fish), nameof(Fish.Pickup))]
    internal static class Fishing35CatchRequestPatch
    {
        private static void Prefix(Fish __instance, Humanoid character) => Fishing35PreserveBaitPatch.CaptureRequest(__instance, character);
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class FarmingPassiveVanillaRollPatch
    {
        private static void Prefix(Pickable __instance, Humanoid character, out float __state)
        {
            if (character == Player.m_localPlayer) { __state = float.NaN; return; }
            __state = __instance != null ? __instance.m_maxLevelBonusChance : 0f;
            // The local player's confirmed roll is configured by Farming35BonusHarvestPatch.
            // Cross-patch prefix/postfix order must not overwrite that chance.
            if (__instance != null && (__instance.m_harvestable || __instance.m_pickRaiseSkill == Skills.SkillType.Farming))
                __instance.m_maxLevelBonusChance = 0f;
        }

        private static void Postfix(Pickable __instance, float __state)
        {
            if (__instance != null && !float.IsNaN(__state)) __instance.m_maxLevelBonusChance = __state;
        }

        private static Exception Finalizer(Pickable __instance, float __state, Exception __exception)
        {
            if (__instance != null && !float.IsNaN(__state)) __instance.m_maxLevelBonusChance = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class Farming35BonusHarvestPatch
    {
        [ThreadStatic] private static bool _clusterHarvest;
        internal sealed class State
        {
            internal Skills.SkillType Skill;
            internal float Chance;
            internal bool Restored;
            internal bool EligibleForFarming;
            internal Pickable PreviousBonusContext;
        }
        [ThreadStatic] internal static Pickable ActiveBonusContext;

        private static void Prefix(Pickable __instance, Humanoid character, out State __state)
        {
            __state = null;
            if (__instance == null) return;
            Player player = character as Player;
            if (player == null || player != Player.m_localPlayer || !__instance.CanBePicked() || __instance.m_pickedLocal) return;

            // All Pickable bonus rolls need their confirmed +1 sound, including wild berries
            // whose prefabs do not set m_harvestable. Only farming-eligible plants get XP
            // and the adjusted resource chance below.
            bool eligible = __instance.m_harvestable || __instance.m_pickRaiseSkill == Skills.SkillType.Farming;
            __state = new State { Skill = __instance.m_pickRaiseSkill, Chance = __instance.m_maxLevelBonusChance,
                EligibleForFarming = eligible, PreviousBonusContext = ActiveBonusContext };
            ActiveBonusContext = __instance;
            if (!eligible) return;

            string key = __instance.gameObject.name.Replace("(Clone)", string.Empty);
            ExperienceContext.ObserveGatheringAction(player, Skills.SkillType.Farming, "harvest." + key,
                GatheringProgressionService.GetResourceTier(key, Skills.SkillType.Farming));

            bool cultivated = __instance.m_pickRaiseSkill == Skills.SkillType.Farming;
            FarmingPerkService.CaptureHarvest(__instance, player, cultivated, _clusterHarvest);
            // Let vanilla perform one roll and its normal bonus resource VFX. Do not add
            // a second independent roll on the owner/server after the vanilla client roll.
            __instance.m_pickRaiseSkill = Skills.SkillType.Farming;
            float factor = player.GetSkillFactor(Skills.SkillType.Farming);
            float wildBonus = !cultivated && FarmingPerkService.HasCultivatorOnHotbar(player) &&
                PerkRuntimeService.HasPerk(player, Skills.SkillType.Farming, 35) ? 0.15f : 0f;
            __instance.m_maxLevelBonusChance = 0.5f + (factor > 0f ? wildBonus / factor : 0f);
        }

        private static void Postfix(Pickable __instance, Humanoid character, State __state)
        {
            Restore(__instance, __state);
            if (__state == null || !__state.EligibleForFarming || _clusterHarvest || __instance == null || !__instance.m_pickedLocal) return;
            Player player = character as Player;
            if (player == null || !FarmingPerkService.HasCultivatorOnHotbar(player) ||
                !PerkRuntimeService.HasPerk(player, Skills.SkillType.Farming, 35) ||
                __instance.m_pickRaiseSkill == Skills.SkillType.Farming) return;
            try
            {
                _clusterHarvest = true;
                FarmingPerkService.HarvestWildCluster(__instance, player, 4f, 12);
            }
            finally { _clusterHarvest = false; }
        }

        private static void Restore(Pickable pickable, State state)
        {
            if (pickable == null || state == null || state.Restored) return;
            pickable.m_pickRaiseSkill = state.Skill;
            pickable.m_maxLevelBonusChance = state.Chance;
            ActiveBonusContext = state.PreviousBonusContext;
            state.Restored = true;
        }

        private static System.Exception Finalizer(Pickable __instance, State __state, System.Exception __exception)
        { Restore(__instance, __state); return __exception; }
    }

    // Vanilla displays +1 inside Pickable.Interact before invoking the bonus EffectList.
    // Tie the pickup cue to that confirmed roll instead of making a second random roll.
    [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText),
        new[] { typeof(DamageText.TextType), typeof(Vector3), typeof(string), typeof(bool) })]
    internal static class FarmingConfirmedBonusSoundPatch
    {
        private static void Prefix(DamageText.TextType type, Vector3 pos, string text)
        {
            if (type != DamageText.TextType.Bonus || string.IsNullOrEmpty(text) || !text.StartsWith("+")) return;
            if (Farming35BonusHarvestPatch.ActiveBonusContext != null)
                PerkVisualService.PlayVanillaCraftBonusCue(Player.m_localPlayer, pos, false);
            // InventoryGui.DoCrafting and CookingStation.OnInteract already invoke
            // m_craftBonusEffect immediately after their own confirmed Bonus text.
        }
    }

    internal static class Crafting35TransactionState
    {
        [ThreadStatic] internal static Crafting35AttemptState Current;
    }

    internal sealed class Crafting35AttemptState
    {
        internal Crafting35AttemptState Previous;
        internal Player Player;
        internal CraftingOutcomeSnapshot Outcome;
        internal bool PreserveResources;
        internal bool ConsumptionSkipped;
        internal bool Restored;
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class Crafting35TransactionPatch
    {
        private static void Prefix(InventoryGui __instance, Player player, out Crafting35AttemptState __state)
        {
            ItemDrop.ItemData upgrade = __instance?.m_craftUpgradeItem;
            __state = new Crafting35AttemptState
            {
                Previous = Crafting35TransactionState.Current,
                Player = player,
                PreserveResources = upgrade != null && PerkRuntimeService.HasPerk(player, Skills.SkillType.Crafting, 35) &&
                    PerkRuntimeService.RollChance(0.35f)
            };
            if (__state.PreserveResources)
                __state.Outcome = CraftingOutcomeSnapshot.Capture(player, __instance.m_craftRecipe, upgrade.m_quality + 1);
            Crafting35TransactionState.Current = __state;
        }

        private static void Postfix(Player player, Crafting35AttemptState __state)
        {
            if (__state?.ConsumptionSkipped == true && player != null && __state.Outcome?.HasSuccessfulOutput() == true)
            {
                PerkVisualService.PlayProc(player, "crafting_35", false, false);
                player.Message(MessageHud.MessageType.TopLeft, PerkLocalization.Localize("$vm_perk_crafting_35_name"), 0, player.m_textIcon);
            }
            Restore(__state);
        }

        private static Exception Finalizer(Exception __exception, Crafting35AttemptState __state)
        {
            Restore(__state);
            return __exception;
        }

        private static void Restore(Crafting35AttemptState state)
        {
            if (state == null || state.Restored) return;
            Crafting35TransactionState.Current = state.Previous;
            state.Restored = true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class Crafting35ResourceConsumptionPatch
    {
        private static bool Prefix(Player __instance)
        {
            Crafting35AttemptState state = Crafting35TransactionState.Current;
            if (state?.PreserveResources != true || state.Player != __instance) return true;
            state.ConsumptionSkipped = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSwimming))]
    internal static class Swimming70EmergencyStaminaPatch
    {
        private static void Prefix(Player __instance)
        {
            if (!MovementPerkService.Valid(__instance) || !__instance.IsSwimming() || Stride70Service.State(__instance).WaterActive ||
                !PerkRuntimeService.HasPerk(__instance, Skills.SkillType.Swim, 70) ||
                __instance.GetStamina() >= __instance.GetMaxStamina() * 0.15f ||
                !PerkCooldownStateService.TryConsume(__instance, "swim_70", 180d)) return;
            PerkRuntimeService.RestoreStamina(__instance, __instance.GetMaxStamina() * 0.25f);
            MovementPerkService.Proc(__instance, "swim_70", "swim_70", Skills.SkillType.Swim, 70);
        }
    }
    [HarmonyPatch(typeof(SE_Wet), nameof(SE_Wet.UpdateStatusEffect))]
    internal static class Swimming35WetDurationPatch
    {
        private sealed class Marker { }
        private static readonly ConditionalWeakTable<SE_Wet, Marker> ExitFeedbackPlayed = new ConditionalWeakTable<SE_Wet, Marker>();

        private static void Postfix(SE_Wet __instance, float dt)
        {
            Player player = __instance?.m_character as Player;
            if (player == null || player != Player.m_localPlayer || !PerkRuntimeService.HasPerk(player, Skills.SkillType.Swim, 35)) return;

            if (player.InLiquidWetDepth(0f))
            {
                ExitFeedbackPlayed.Remove(__instance);
                return;
            }

            // Vanilla begins at two minutes. While dry, only the portion above 30 seconds
            // decays four times as fast; the final 30 seconds stay at vanilla speed.
            float remaining = Mathf.Max(0f, __instance.m_ttl - __instance.m_time);
            if (remaining > 30f) __instance.m_time += Mathf.Min(remaining - 30f, dt * 3f);

            if (!ExitFeedbackPlayed.TryGetValue(__instance, out _))
            {
                ExitFeedbackPlayed.Add(__instance, new Marker());
                PerkFeedbackService.Play(player, "swim_35", player.transform.position, false);
            }
        }
    }

}







