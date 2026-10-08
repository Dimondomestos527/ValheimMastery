using System;
using System.Globalization;
using UnityEngine;

namespace ValheimMastery
{
    // Centralized proc feedback. These are deliberately gameplay-proc effects, never level-up effects.
    internal static class PerkVisualService
    {
        internal static void PlayMilestone(Player player, Skills.SkillType skill, int milestone, PerkDefinition perk,
            bool firstUnlock = false)
        {
            if (player == null) return;
            // Native raven lessons are not disabled by hiding the milestone HUD.
            MasteryRavenTutorials.Unlock(player, skill, milestone);
            bool presentationEnabled = MasteryPlugin.Settings.EnableMilestoneVFX.Value ||
                MasteryPlugin.Settings.EnableMilestoneMessages.Value;
            if (presentationEnabled)
                MasteryMilestonePresentation.Show(player, skill, milestone, perk);
            if (firstUnlock && milestone == 100)
                PatronAscensionPresentation.Schedule(player, skill);
        }

        internal static void PlayAtPlayer(Player player, string perkId, bool showName = false)
        {
            if (player != null) PlayAtWorldPosition(player, perkId, player.transform.position + Vector3.up, showName);
        }

        internal static void PlayAtWorldPosition(Player player, string perkId, Vector3 position, bool showName = false)
        {
            if (player == null) return;
            if (MasteryPlugin.Settings?.VerboseLogging.Value == true)
                MasteryPlugin.Log.LogInfo("[PerkProc] " + perkId + " player=" + player.GetPlayerName() + " position=" + position);
            // Dedicated servers have no renderer. Relay confirmed gameplay feedback to the owning client.
            if (ZNet.instance != null && ZNet.instance.IsServer() && player != Player.m_localPlayer)
            {
                NetworkSync.SendProcFeedback(player, (showName ? "perk:" : "perk_silent:") + perkId, position);
                return;
            }
            PlayLocal(player, perkId, position, showName);
        }

        internal static void PlayProc(Player player, string perkId, Vector3 position, bool prominent = false, bool showName = false)
        {
            PlayAtWorldPosition(player, perkId, position, showName);
        }

        internal static void PlayProc(Player player, string perkId, bool prominent = false, bool showName = false)
        {
            if (player != null) PlayProc(player, perkId, player.transform.position + Vector3.up, prominent, showName);
        }

        // A confirmed fist hit updates the attack-speed state on the owning client.
        // The cue is deliberately small and physical: no shield flash or magic aura.
        internal static void PlayFistCombo(Player player, int stacks)
        {
            if (player == null || stacks < 1 || stacks > 3) return;
            if (ZNet.instance != null && ZNet.instance.IsServer() && player != Player.m_localPlayer)
            {
                NetworkSync.SendProcFeedback(player, "fists_combo:" + stacks, player.GetCenterPoint());
                return;
            }
            PerkPlayerTransientState state = PerkTransientStateService.For(player);
            state.FistComboStacks = stacks;
            state.LastFistHitSerial = state.FistAttackSerial;
            state.LastFistHitAt = Time.time;
            Vector3 point = player.GetCenterPoint() + player.transform.forward * 0.55f - Vector3.up * 0.22f;
            if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
                SpawnVanillaEffect("vfx_HitSparks", point, 0.30f + stacks * 0.10f);
            PerkAudioService.Play("fists35_rhythm", "sfx_unarmed_hit", point, 0.28f, volumeScale: .7f, pitchScale: 1f + stacks * .05f);
            if (MasteryPlugin.Settings.VerboseLogging.Value)
                MasteryPlugin.Log.LogInfo("[Fists35] confirmed stacks=" + stacks + " player=" + player.GetPlayerName());
        }
        // Called only after the extra drop has been created, not merely after a mining hit.
        internal static void PlayPickaxeResourceProc(Player player, Vector3 position)
        {
            if (player == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer() && player != Player.m_localPlayer)
            {
                NetworkSync.SendProcFeedback(player, "pickaxes_resource", position);
                return;
            }
            PlayPickaxeResourceProcLocal(player, position);
        }

        internal static void PlayWoodResourceProc(Player player, Vector3 position, int count = 1)
        {
            if (player == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer() && player != Player.m_localPlayer)
            {
                NetworkSync.SendProcFeedback(player, "wood_resource:" + count, position);
                return;
            }
            PlayWoodResourceProcLocal(player, position, count);
        }

        internal static void PlayGenericResourceBonus(Player player, Vector3 position, int count = 1)
        {
            if (player == null || count <= 0) return;
            if (ZNet.instance != null && ZNet.instance.IsServer() && player != Player.m_localPlayer) { NetworkSync.SendProcFeedback(player, "resource_bonus:" + count, position); return; }
            PlayGenericResourceBonusLocal(player, position, count);
        }

        internal static void PlayVanillaCraftBonusCue(Player player, Vector3 position, bool playCraftEffect = true)
        {
            // The old farming DamageText hook passes false. The following, narrowly
            // patched Pickable bonus EffectList call now owns that cue exactly once.
            if (playCraftEffect) VanillaBonusFeedback.Play(player);
        }

        internal static void StartBlockingArmorAura(Player player, float duration)
        {
            if (player == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) { NetworkSync.SendProcFeedback(player, "blocking_armor:" + duration.ToString("0.###", CultureInfo.InvariantCulture), player.GetCenterPoint()); return; }
            StartArmorAuraLocal(player, duration);
        }
        internal static void MarkBowWeakPoint(Player player, Vector3 point, float duration)
        {
            if (player == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                NetworkSync.SendProcFeedback(player, "bow_mark:" + duration.ToString("0.###", CultureInfo.InvariantCulture), point);
                return;
            }
            StartBowWeakPointLocal(point, duration);
        }

        internal static BowWeakPointMarker MarkBowWeakPointLocal(Player viewer, Character target, Collider surface, float duration, float visualRadius = 0.28f)
        {
            if (viewer == null || viewer != Player.m_localPlayer || target == null || surface == null) return null;
            if (MasteryPlugin.Settings?.VerboseLogging.Value == true)
                MasteryPlugin.Log.LogInfo("[PerkProc] bows_35_mark target=" + target.name + " local-only front-facing marker");
            GameObject runner = new GameObject("ValheimMastery_BowWeakPoint");
            BowWeakPointMarker marker = runner.AddComponent<BowWeakPointMarker>();
            marker.Initialize(viewer, target, surface, duration, visualRadius);
            return marker;
        }        internal static void MarkExecutionTarget(Player player, Vector3 point, float duration) { if (player == null) return; if (ZNet.instance != null && ZNet.instance.IsServer()) { NetworkSync.SendProcFeedback(player, "axe_execution:" + duration.ToString("0.###", CultureInfo.InvariantCulture), point); return; } StartExecutionTargetLocal(point, duration); }
        private static void PlayBlockingArmorActivation(Player player)
        {
            if (player == null) return;
            Vector3 position = player.GetCenterPoint();
            SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.65f, 3.40f);
            SpawnVanillaEffect("vfx_HitSparks", position + Vector3.up * 0.25f, 2.25f);
            PerkAudioService.Play("blocking_35_activation", "sfx_perfectblock", position, 1.55f);
        }

        internal static void PlayBlockingArmorPulse(Player player)
        {
            if (player == null) return;
            Vector3 position = player.GetCenterPoint(); SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.75f, 1.05f);
            PerkAudioService.Play("blocking_35_armor_aura", "sfx_perfectblock", position, 1.20f);
        }
        private static void StartArmorAuraLocal(Player player, float duration)
        {
            GameObject runner = new GameObject("ValheimMastery_Blocking35Aura");
            BlockingArmorAura aura = runner.AddComponent<BlockingArmorAura>(); aura.Initialize(player, duration);
            Blocking35StatusIconService.Show(player, duration);
            PlayBlockingArmorActivation(player);
        }
        private static void StartBowWeakPointLocal(Vector3 point, float duration)
        {
            GameObject runner = new GameObject("ValheimMastery_BowWeakPoint");
            BowWeakPointMarker marker = runner.AddComponent<BowWeakPointMarker>();
            marker.Initialize(point, duration);
        }        private static void StartExecutionTargetLocal(Vector3 point, float duration) { GameObject runner = new GameObject("ValheimMastery_AxeExecutionTarget"); ExecutionTargetMarker marker = runner.AddComponent<ExecutionTargetMarker>(); marker.Initialize(point, duration); }
        internal static void PlayRemoteProc(Player player, string procId, Vector3 position)
        {
            if (player == null || string.IsNullOrEmpty(procId)) return;
            if (AxeTargetVisualService.TryHandle(procId, position)) return;
            if (OverdrawPenetrationService.TryHandleVisual(procId, position)) return;
            if (procId.StartsWith("knife35_ready:")) { if (float.TryParse(procId.Substring("knife35_ready:".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float readyDuration)) ShadowStep35Service.ArmLocal(player, readyDuration); return; }
            if (procId == "knife35_blink_kill") { AssassinBlink70Service.OnConfirmedKillLocal(player); return; }
            if (procId == "knife35_blink_impact") { AssassinBlinkVisualService.PlayImpact(position); return; }
            if (procId.StartsWith("blocking_armor:")) { if (float.TryParse(procId.Substring("blocking_armor:".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration)) StartArmorAuraLocal(player, duration); return; }
            if (procId.StartsWith("axe_execution:")) { if (float.TryParse(procId.Substring("axe_execution:".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration)) StartExecutionTargetLocal(position, duration); return; }
            if (procId.StartsWith("bow_mark:")) { if (float.TryParse(procId.Substring("bow_mark:".Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float duration)) StartBowWeakPointLocal(position, duration); return; }
            if (procId.StartsWith("resonance:")) { string[] parts = procId.Split(':'); if (parts.Length == 3 && int.TryParse(parts[1], out int first) && int.TryParse(parts[2], out int second)) PlayElementalResonance(player, first, second, position); return; }
            if (procId.StartsWith("fists_combo:")) { int stacks; if (int.TryParse(procId.Substring("fists_combo:".Length), out stacks)) PlayFistCombo(player, stacks); return; }
            if (procId.StartsWith("fists35_adrenaline:"))
            {
                if (int.TryParse(procId.Substring("fists35_adrenaline:".Length), out int stacks))
                {
                    PerkPlayerTransientState state = PerkTransientStateService.For(player);
                    state.AdrenalineComboStacks = Mathf.Clamp(stacks, 0, 4);
                    state.LastCombatHitAt = Time.time;
                }
                return;
            }
            if (procId == "pickaxes_resource")
            {
                PlayPickaxeResourceProcLocal(player, position);
                return;
            }
            if (procId == "wood_resource") { PlayWoodResourceProcLocal(player, position); return; }
            if (procId.StartsWith("wood_resource:"))
            {
                if (int.TryParse(procId.Substring("wood_resource:".Length), out int count))
                    PlayWoodResourceProcLocal(player, position, Mathf.Clamp(count, 1, 5));
                return;
            }
            if (procId.StartsWith("resource_bonus:"))
            {
                if (int.TryParse(procId.Substring("resource_bonus:".Length), out int count))
                    PlayGenericResourceBonusLocal(player, position, Mathf.Clamp(count, 1, 999));
                return;
            }
            if (procId == "resource_bonus") { PlayGenericResourceBonusLocal(player, position, 1); return; }
            const string silentPrefix = "perk_silent:";
            if (procId.StartsWith(silentPrefix))
            {
                string silentPerkId = procId.Substring(silentPrefix.Length);
                PlayLocal(player, silentPerkId, position, false);
                return;
            }
            const string prefix = "perk:";
            if (procId.StartsWith(prefix))
            {
                string perkId = procId.Substring(prefix.Length);
                PlayLocal(player, perkId, position, true);
            }
        }

        private static void PlayLocal(Player player, string perkId, Vector3 position, bool showName)
        {
            if (!VfxRecipeService.Play(perkId, player, position)) SpawnThematic(perkId, position);
            if (showName && MasteryPlugin.Settings.EnablePerkProcMessages.Value)
                player.Message(MessageHud.MessageType.TopLeft, PerkLocalization.Localize("$vm_perk_" + perkId + "_name"), 0, PerkUiIconService.ForPerk(perkId, player.m_textIcon));
        }


        private static void PlayPickaxeResourceProcLocal(Player player, Vector3 position)
        {
            if (player == null || player != Player.m_localPlayer) return;
            VanillaBonusFeedback.Show(player, 1);
            if (MasteryPlugin.Settings.EnablePerkProcMessages.Value)
                player.Message(MessageHud.MessageType.TopLeft, "Кайло: +1 додатковий ресурс", 0, PerkUiIconService.ForPerk("pickaxes_35", player.m_textIcon));
        }

        private static void PlayWoodResourceProcLocal(Player player, Vector3 position, int count = 1)
        {
            if (player == null || player != Player.m_localPlayer) return;
            VanillaBonusFeedback.Show(player, count);
            if (MasteryPlugin.Settings.EnablePerkProcMessages.Value)
                player.Message(MessageHud.MessageType.TopLeft, "Рубання дерев: +" + count + " додаткової деревини", 0,
                    PerkUiIconService.ForPerk("woodcutting_35", player.m_textIcon));
        }

        private static void PlayGenericResourceBonusLocal(Player player, Vector3 position, int count)
        {
            if (player == null || player != Player.m_localPlayer || count <= 0) return;
            VanillaBonusFeedback.Show(player, count);
            if (MasteryPlugin.Settings.EnablePerkProcMessages.Value)
                player.Message(MessageHud.MessageType.TopLeft, "+" + count + " додатковий ресурс", 0, player.m_textIcon);
        }

        // Every entry is a short, purpose-built proc cue. They never reuse the skill-level-up effect.
        internal static void PlayElementalResonance(Player player, int first, int second, Vector3 position)
        {
            if (player == null) return;
            if (ZNet.instance != null && ZNet.instance.IsServer()) { NetworkSync.SendProcFeedback(player, "resonance:" + first + ":" + second, position); return; }
            if (MasteryPlugin.Settings.EnablePerkProcVFX.Value)
            {
                SpawnElement(first, position, 0.72f); SpawnElement(second, position, 0.72f);
                SpawnVanillaEffect("vfx_HitSparks", position, 0.50f);
            }
            PerkAudioService.Play("elementalmagic_70", "sfx_Potion_eitr_minor", position, 0.35f);
            if (MasteryPlugin.Settings.EnablePerkProcMessages.Value) player.Message(MessageHud.MessageType.TopLeft, PerkLocalization.Localize("$vm_perk_elementalmagic_70_name"), 0, PerkUiIconService.ForPerk("elementalmagic_70", player.m_textIcon));
        }

        private static void SpawnElement(int element, Vector3 position, float scale)
        {
            string prefab = element == 1 ? "vfx_Burning" : element == 2 ? "vfx_Frost" : element == 3 ? "fx_Lightning" : "vfx_Poison";
            SpawnVanillaEffect(prefab, position, scale);
        }
        private static void SpawnThematic(string perkId, Vector3 position)
        {
            string sfx = null;
            switch (perkId)
            {
                case "cooking_70":
                    SpawnVanillaEffect("vfx_MeadSwimmer", position, 1.10f); sfx = "sfx_eat"; break;
                case "cooking_35":
                    SpawnVanillaEffect("vfx_MeadSwimmer", position, 1.45f); SpawnVanillaEffect("vfx_pickable_pick", position, 0.55f); sfx = "sfx_eat"; break;
                case "crafting_70":
                    SpawnVanillaEffect("vfx_Place_forge", position, 2.15f); SpawnVanillaEffect("vfx_HitSparks", position + Vector3.up * 0.4f, 1.55f); sfx = "sfx_gui_craftitem_forge"; break;
                case "crafting_35":
                    SpawnVanillaEffect("vfx_Place_forge", position, 1.45f); SpawnVanillaEffect("vfx_ForgeAddFuel", position, 1.10f); sfx = "sfx_gui_craftitem_forge"; break;
                case "farming_35":
                    SpawnVanillaEffect("vfx_pickable_pick", position, 1.40f); SpawnVanillaEffect("vfx_bush_leaf_puff_heath", position, 1.15f); sfx = "sfx_pickable_pick"; break;
                case "fishing_35":
                    SpawnVanillaEffect("fx_WaterImpact_Big", position, 1.70f); SpawnVanillaEffect("fx_float_hitwater", position, 1.15f); sfx = "sfx_land_water"; break;
                case "pickaxes_35":
                    SpawnVanillaEffect("vfx_RockDestroyed", position, 1.55f); SpawnVanillaEffect("vfx_RockHit", position, 1.20f); sfx = "sfx_rock_destroyed"; break;
                case "pickaxes_70":
                    SpawnStrippedPickaxeLayer("vfx_RockHit", position, 1.75f); SpawnStrippedPickaxeLayer("vfx_RockDestroyed", position, 1.35f); SpawnStrippedPickaxeLayer("fx_Lightning", position + Vector3.up * .18f, 0.42f, true); sfx = "sfx_rock_destroyed"; break;
                case "pickaxes_70_collapse":
                    SpawnStrippedPickaxeLayer("vfx_RockHit", position, 2.25f); SpawnStrippedPickaxeLayer("vfx_RockDestroyed", position, 2.45f); SpawnStrippedPickaxeLayer("fx_Lightning", position + Vector3.up * .65f, 1.35f, true); sfx = "sfx_rock_destroyed"; break;
                case "pickaxes_70_secondary":
                    SpawnStrippedPickaxeLayer("vfx_RockHit", position, 0.65f); break;
                case "fishing_70":
                    SpawnVanillaEffect("fx_float_hitwater", position, 0.60f); break;
                case "crossbows_70":
                    SpawnVanillaEffect("vfx_arrowhit", position, 0.70f); sfx = "sfx_arrow_hit"; break;
                case "axes_70_windup":
                    SpawnVanillaEffect("vfx_HitSparks", position + Vector3.up * 0.7f, 1.75f); sfx = "sfx_axe_swing"; break;
                case "axes_70":
                    SpawnVanillaEffect("vfx_BloodHit", position, 1.35f); SpawnVanillaEffect("vfx_HitSparks", position, 1.45f); SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.35f, 0.75f); sfx = "sfx_axe_hit"; break;
                case "clubs_70_windup":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.5f, 1.35f); sfx = "sfx_club_swing"; break;
                case "clubs_70":
                    SpawnVanillaEffect("vfx_HitSparks", position, 2.10f); SpawnVanillaEffect("vfx_perfectblock", position, 1.20f); sfx = "sfx_club_hit"; break;
                case "clubs_70_crit":
                    SpawnVanillaEffect("vfx_HitSparks", position, 2.75f); SpawnVanillaEffect("vfx_perfectblock", position, 1.70f); SpawnVanillaEffect("vfx_BloodHit", position, 1.15f); sfx = "sfx_smelter_produce"; break;
                case "ride_70":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.45f, 0.65f); SpawnVanillaEffect("vfx_HitSparks", position, 0.75f); sfx = "sfx_perfectblock"; break;
                case "fists_70":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.45f, 0.70f); SpawnVanillaEffect("vfx_HitSparks", position, 1.10f); sfx = "sfx_perfectblock"; break;
                case "run_35":
                    SpawnVanillaEffect("fx_land", position, 0.9f); sfx = "sfx_dodge"; break;
                case "run_70":
                    SpawnVanillaEffect("vfx_BloodHit", position + Vector3.up * 0.70f, 2.80f); SpawnVanillaEffect("vfx_HitSparks", position, 1.70f); sfx = "sfx_dodge"; break;
                case "sneak_35":
                    SpawnVanillaEffect("vfx_bush_leaf_puff_heath", position, 1.35f); SpawnVanillaEffect("vfx_BloodHit", position, 1.00f); sfx = "sfx_dodge"; break;
                case "swim_35":
                    SpawnVanillaEffect("fx_float_hitwater", position, 1.75f); SpawnVanillaEffect("vfx_MeadSwimmer", position, 1.05f); sfx = "sfx_land_water"; break;
                case "swim_70":
                    SpawnVanillaEffect("vfx_MeadSwimmer", position + Vector3.up * 0.35f, 2.25f); SpawnVanillaEffect("fx_float_hitwater", position, 2.10f); sfx = "sfx_land_water"; break;
                case "woodcutting_35":
                    SpawnVanillaEffect("vfx_tree_fall_hit", position, 2.40f); SpawnVanillaEffect("vfx_tree_fall_hit", position + Vector3.right * 0.70f, 1.60f); SpawnVanillaEffect("vfx_tree_fall_hit", position - Vector3.right * 0.70f, 1.60f); sfx = "sfx_tree_fall"; break;
                case "woodcutting_35_small":
                    SpawnVanillaEffect("vfx_tree_fall_hit", position, 1.20f); SpawnVanillaEffect("vfx_tree_fall_hit", position + Vector3.right * 0.25f, 0.90f); SpawnVanillaEffect("vfx_tree_fall_hit", position - Vector3.right * 0.25f, 0.90f); SpawnVanillaEffect("vfx_tree_fall_hit", position + Vector3.forward * 0.20f, 0.80f); sfx = "sfx_axe_hit"; break;
                case "swords_35":
                    SpawnVanillaEffect("vfx_HitSparks", position, 1.45f); SpawnVanillaEffect("vfx_perfectblock", position, 1.05f); sfx = "sfx_sword_hit"; break;
                case "axes_35_ready":
                    SpawnVanillaEffect("vfx_BloodHit", position + Vector3.up * 0.60f, 1.10f); SpawnVanillaEffect("vfx_HitSparks", position + Vector3.up * 0.85f, 1.55f); sfx = "sfx_perfectblock"; break;
                case "axes_35_strike":
                    SpawnVanillaEffect("vfx_BloodHit", position, 2.45f); SpawnVanillaEffect("vfx_HitSparks", position, 2.05f); SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.35f, 1.35f); sfx = "sfx_axe_hit"; break;
                case "clubs_35":
                    SpawnVanillaEffect("vfx_HitSparks", position, 1.55f); SpawnVanillaEffect("vfx_perfectblock", position, 0.90f); sfx = "sfx_club_hit"; break;
                case "knives_35":
                    SpawnVanillaEffect("vfx_BloodHit", position, 1.35f); SpawnVanillaEffect("vfx_HitSparks", position, 0.85f); sfx = "sfx_knife_swing"; break;
                case "knives_70":
                    SpawnVanillaEffect("fx_perfectdodge", position, 1.45f); SpawnVanillaEffect("vfx_BloodHit", position + Vector3.up * 0.25f, 1.25f); sfx = "sfx_knife_swing"; break;
                case "polearms_70":
                    SpawnVanillaEffect("vfx_HitSparks", position, 1.85f); SpawnVanillaEffect("fx_perfectdodge", position, 1.25f); sfx = "sfx_atgeir_attack_secondary"; break;
                case "spears_35":
                    SpawnVanillaEffect("vfx_BloodHit", position, 1.35f); SpawnVanillaEffect("vfx_arrowhit", position, 1.20f); sfx = "sfx_spear_hit"; break;
                case "polearms_35":
                    SpawnVanillaEffect("vfx_HitSparks", position, 1.65f); SpawnVanillaEffect("vfx_BloodHit", position, 0.90f); sfx = "sfx_atgeir_attack"; break;
                case "bows_35":
                    SpawnVanillaEffect("vfx_arrowhit", position, 1.35f); SpawnVanillaEffect("vfx_arrowhit", position, 0.95f); sfx = "sfx_bow_fire"; break;
                case "bows_70":
                    SpawnVanillaEffect("vfx_arrowhit", position, 2.10f); SpawnVanillaEffect("vfx_perfectblock", position, 0.85f); sfx = "sfx_arrow_hit"; break;
                case "fists_35":
                    SpawnVanillaEffect("vfx_HitSparks", position, 1.45f); sfx = "sfx_unarmed_hit"; break;
                case "blocking_35_guardbreak":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.35f, 2.75f); SpawnVanillaEffect("vfx_HitSparks", position, 2.00f); sfx = "sfx_perfectblock"; break;
                case "blocking_35_armor":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 1.00f, 1.80f); SpawnVanillaEffect("vfx_HitSparks", position + Vector3.up * 0.25f, 0.85f); sfx = "sfx_perfectblock"; break;                case "blocking_35":
                    SpawnVanillaEffect("vfx_perfectblock", position, 1.65f); SpawnVanillaEffect("vfx_HitSparks", position, 1.10f); sfx = "sfx_perfectblock"; break;
                case "dodge_35":
                    SpawnVanillaEffect("fx_perfectdodge", position, 1.55f); SpawnVanillaEffect("vfx_perfectblock", position, 0.80f); sfx = "sfx_perfect_dodge"; break;
                case "blocking_70":
                    SpawnVanillaEffect("vfx_HitSparks", position, 1.10f); sfx = "sfx_metal_shield_blocked"; break;
                case "blocking_70_projectile":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.30f, 2.10f); SpawnVanillaEffect("vfx_arrowhit", position, 1.25f); sfx = "sfx_metal_shield_blocked"; break;
                case "blocking_70_parry":
                    SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.20f, 2.55f); SpawnVanillaEffect("vfx_HitSparks", position, 2.20f); sfx = "sfx_smelter_produce"; break;
                case "dodge_70":
                    SpawnVanillaEffect("fx_perfectdodge", position, 1.40f); SpawnVanillaEffect("vfx_HitSparks", position, 1.00f); sfx = "sfx_dodge"; break;
                case "elementalmagic_35":
                    SpawnVanillaEffect("vfx_Potion_eitr_minor", position + Vector3.up * 0.55f, 2.80f); SpawnVanillaEffect("vfx_HitSparks", position, 1.45f); SpawnVanillaEffect("vfx_perfectblock", position + Vector3.up * 0.2f, 0.70f); sfx = "sfx_Potion_eitr_minor"; break;
                case "bloodmagic_35":
                    SpawnVanillaEffect("vfx_BloodHit", position, 1.75f); SpawnVanillaEffect("vfx_Potion_eitr_minor", position, 1.20f); sfx = "fx_bloodweapon_hit"; break;
            }
            if (!string.IsNullOrEmpty(sfx)) PerkAudioService.Play(perkId, sfx, position);
        }

        private static void SpawnVanillaEffect(string prefabName, Vector3 position, float scale = 1f)
        {
            PerkNativeFeedback.PlayVfx(prefabName, position, scale);
        }

        private static void SpawnStrippedPickaxeLayer(string prefabName, Vector3 position, float scale, bool lightning = false)
        {
            if (!MasteryPlugin.Settings.EnablePerkProcVFX.Value) return;
            // The shared pool now restores the untuned baseline on every lease and strips
            // audio/gameplay scripts before activation. Never instantiate a live damage AoE.
            GameObject prefab = ZNetScene.instance?.GetPrefab(prefabName);
            if (prefab == null) return;
            VfxRecipeService.Spawn(new VfxLayer
            {
                PrefabName = prefabName,
                RootScale = scale,
                ParticleSizeMultiplier = lightning ? 0.90f : 0.85f,
                EmissionMultiplier = lightning ? 0.85f : 0.80f,
                LightIntensityMultiplier = lightning ? 0.55f : 0.5f,
                DisableBuiltinAudio = true,
                DisableCamShaker = true
            }, position);
        }
    }


    internal sealed class BlockingArmorAura : MonoBehaviour
    {
        private Player _player; private float _until; private float _nextPulse;
        internal void Initialize(Player player, float duration) { _player = player; _until = Time.time + Mathf.Max(0.1f, duration); _nextPulse = Time.time + 1.2f; }
        private void Update() { if (_player == null || Time.time >= _until) { Destroy(gameObject); return; } if (Time.time >= _nextPulse) { _nextPulse = Time.time + 1.8f; PerkVisualService.PlayBlockingArmorPulse(_player); } }
    }
    // Local HUD marker; the actual armor remains server-authoritative in Blocking35Service.
    internal static class Blocking35StatusIconService
    {
        internal static void Show(Player player, float duration)
        {
            if (player == null || player != Player.m_localPlayer || player.m_seman == null) return;
            SE_Stats marker = ScriptableObject.CreateInstance<SE_Stats>();
            marker.m_name = "Загартований щит";
            marker.m_tooltip = "+ броня від Blocking 35";
            marker.m_icon = PerkUiIconService.ForPerk("blocking_35", player.m_textIcon);
            marker.m_ttl = duration;
            marker.m_flashIcon = true;
            player.m_seman.AddStatusEffect(marker, false, 0, duration, 0);
        }
    }
    internal sealed class BowWeakPointMarker : MonoBehaviour
    {
        private float _until;
        private Light _light;
        private LineRenderer _ring;
        private Material _material;
        private Player _viewer;
        private Character _target;
        private Collider _surface;
        private float _horizontalBias;
        private float _verticalBias;
        private float _baseScale = 0.28f;
        private bool _tracksTarget;
        private static readonly Vector2[] UnitRing = CreateUnitRing();
        private readonly Vector3[] _ringPositions = new Vector3[28];
        internal Vector3 CurrentWorldPoint => transform.position;

        internal void Initialize(Vector3 point, float duration) => Initialize(null, point, duration);

        internal void Initialize(Transform target, Vector3 point, float duration, float visualRadius = 0.28f)
        {
            if (target != null)
            {
                transform.SetParent(target, false);
                transform.localPosition = point;
            }
            else transform.position = point;
            _until = Time.time + Mathf.Max(0.1f, duration);
            _baseScale = Mathf.Clamp(visualRadius, 0.10f, 0.35f);
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.72f, 0.16f, 1f);
            _light.range = 2.5f;
            _light.intensity = 4f;
            CreateRing();
            RefreshRing();
        }

        internal void Initialize(Player viewer, Character target, Collider surface, float duration, float visualRadius)
        {
            _tracksTarget = true;
            _viewer = viewer; _target = target; _surface = surface;
            _horizontalBias = UnityEngine.Random.Range(-0.24f, 0.24f);
            _verticalBias = UnityEngine.Random.Range(0.28f, 0.62f);
            string targetName = target?.gameObject?.name ?? "";
            if (targetName.IndexOf("lox", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _horizontalBias = UnityEngine.Random.Range(-0.16f, 0.16f);
                _verticalBias = UnityEngine.Random.Range(0.42f, 0.68f);
            }
            _until = Time.time + Mathf.Max(0.1f, duration);
            _baseScale = Mathf.Clamp(visualRadius, 0.10f, 0.35f);
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = new Color(1f, 0.55f, 0.08f, 1f);
            _light.range = 1.65f; _light.intensity = 2.2f;
            CreateRing();
            RefreshSurfacePoint();
            RefreshRing();
        }

        private void CreateRing()
        {
            _ring = gameObject.AddComponent<LineRenderer>();
            _material = MasteryVfxMaterial.CloneFromPrefab("vfx_HitSparks");
            _ring.material = _material; _ring.useWorldSpace = true; _ring.loop = true;
            _ring.positionCount = 28; _ring.numCapVertices = 2; _ring.widthMultiplier = 0.035f;
            Color gold = new Color(1f, 0.43f, 0.035f, 0.95f); _ring.startColor = gold; _ring.endColor = gold;
        }

        private static Vector2[] CreateUnitRing()
        {
            Vector2[] points = new Vector2[28];
            for (int i = 0; i < points.Length; ++i)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return points;
        }

        private void RefreshSurfacePoint()
        {
            if (_viewer == null || _target == null || _surface == null) return;
            Vector3 eye = _viewer.GetEyePoint(); Bounds bounds = _surface.bounds;
            Vector3 view = (bounds.center - eye).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, view).normalized;
            float lateralExtent = Mathf.Max(bounds.extents.x, bounds.extents.z);
            Vector3 desired = bounds.center + right * lateralExtent * _horizontalBias + Vector3.up * bounds.extents.y * _verticalBias;
            Ray ray = new Ray(eye, (desired - eye).normalized);
            if (_surface.Raycast(ray, out RaycastHit hit, Vector3.Distance(eye, desired) + bounds.extents.magnitude * 2f)) transform.position = hit.point - ray.direction * 0.025f;
            else transform.position = _surface.ClosestPoint(eye);
        }

        private void Update()
        {
            // Keep the five-second mark after releasing the arrow, but remove orphaned world visuals.
            if (Time.time >= _until || (_tracksTarget && (_viewer == null || _viewer != Player.m_localPlayer ||
                _target == null || _target.IsDead() || _surface == null))) { Destroy(gameObject); return; }
            RefreshSurfacePoint();
            RefreshRing();
        }

        private void RefreshRing()
        {
            bool visible = MasteryPlugin.Settings.EnablePerkProcVFX.Value;
            if (_light != null) _light.enabled = visible;
            if (_ring != null) _ring.enabled = visible;
            // Surface tracking remains active for the in-flight arrow even with decorative effects disabled.
            if (!visible) return;
            float pulse = 0.82f + Mathf.PingPong(Time.time * 4f, 0.42f);
            if (_light != null) _light.intensity = 1.4f + Mathf.PingPong(Time.time * 5f, 1.4f);
            if (_ring != null)
            {
                Vector3 normal = _viewer != null ? (_viewer.GetEyePoint() - transform.position).normalized : (Camera.main != null ? -Camera.main.transform.forward : Vector3.forward);
                Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right;
                Vector3 up = Vector3.Cross(normal, right).normalized;
                float radius = _baseScale * pulse;
                for (int i = 0; i < _ringPositions.Length; ++i)
                {
                    Vector2 unit = UnitRing[i];
                    _ringPositions[i] = transform.position + (right * unit.x + up * unit.y) * radius;
                }
                _ring.SetPositions(_ringPositions);
            }
        }

        private void OnDestroy() { if (_material != null) Destroy(_material); }
    }    internal sealed class ExecutionTargetMarker : MonoBehaviour
    {
        private float _until; private float _nextPulse; private Light _light;
        internal void Initialize(Vector3 point, float duration)
        {
            transform.position = point + Vector3.up * 0.55f; _until = Time.time + Mathf.Max(0.1f, duration); _nextPulse = Time.time;
            _light = gameObject.AddComponent<Light>(); _light.type = LightType.Point; _light.color = new Color(1f, 0.08f, 0.03f, 1f); _light.range = 3.2f; _light.intensity = 4.5f;
        }
        private void Update()
        {
            if (Time.time >= _until) { Destroy(gameObject); return; }
            if (_light != null) _light.intensity = 2.8f + Mathf.PingPong(Time.time * 6f, 3.2f);
            if (Time.time < _nextPulse) return; _nextPulse = Time.time + 0.65f;
            GameObject prefab = ZNetScene.instance?.GetPrefab("vfx_BloodHit");
            if (prefab != null) { GameObject vfx = Instantiate(prefab, transform.position, Quaternion.identity); if (vfx != null) vfx.transform.localScale *= 0.55f; }
        }
    }}






