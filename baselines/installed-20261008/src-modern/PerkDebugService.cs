using UnityEngine;
using System;

namespace ValheimMastery
{
    internal static class PerkDebugService
    {
        internal static void RegisterCommands()
        {
            if (!MasteryPlugin.Settings.UIDebugLogging.Value)
                return;
            // The umbrella command is intentionally non-cheat: it dispatches only mod-owned,
            // server-validated test requests and must remain usable on a dedicated server.
            new Terminal.ConsoleCommand("vm", "vm perk <id> force | vm sword70 debug on|off | vm fists70 <gauge/ready/cooldown/state/target> | vm state | vm targeteffects | vm vfx <...>", Vm, false);
            new Terminal.ConsoleCommand("vm_skill", "vm_skill <SkillType> <level>", SetSkill, true);
            new Terminal.ConsoleCommand("vm_forceproc", "vm_forceproc <on|off>", ForceProc, true);
            new Terminal.ConsoleCommand("vm_clearcooldowns", "Clear Valheim Mastery cooldowns", ClearCooldowns, true);
            new Terminal.ConsoleCommand("vm_state", "vm_state <SkillType>", PrintState, true);
            new Terminal.ConsoleCommand("vm_allskills70", "Set every Valheim Mastery skill to level 70 for testing", SetAllSkills70, false);
            new Terminal.ConsoleCommand("vm_give", "vm_give <PrefabName> [amount]", GiveItem, false);
            new Terminal.ConsoleCommand("vm_pressure", "vm_pressure <0-1>", SetPressure, true);
            new Terminal.ConsoleCommand("vm_chop", "vm_chop <0-3>", SetChop, true);
            new Terminal.ConsoleCommand("vm_targeteffects", "Print target effects for aimed creature", PrintTargetEffects, true);
            new Terminal.ConsoleCommand("vm_feasts", "List current Feast item prefabs", ListFeasts, true);
            VfxAuditionService.Register();
            MasteryPlugin.Log.LogWarning("Valheim Mastery debug commands are ENABLED.");
        }

        private static void Vm(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length < 2) { args.Context.AddString("vm perk <id> force | state | targeteffects | pressure <0-1> | chop <0-3> | vfx <list/play/recipe/inspect>"); return; }
            string action = args.Args[1].ToLowerInvariant();
            if (action == "pantheon") { GoldPantheonUi.Debug(args); return; }
            if (action == "favor") { GoldCraftingService.Debug(args); return; }
            if (action == "perk" && args.Args.Length >= 4 && string.Equals(args.Args[3], "force", StringComparison.OrdinalIgnoreCase))
            {
                PerkRuntimeService.ForceProc = true;
                args.Context.AddString("ForceProc enabled for test: " + args.Args[2] + ". Disable with vm_forceproc off.");
                return;
            }
            if (action == "state") { PrintAllStates(args); return; }
            if (action == "targeteffects") { PrintTargetEffects(args); return; }
            if (action == "pressure") { SetPressure(args); return; }
            if (action == "chop") { SetChop(args); return; }
            if (action == "attackgeom") { bool show = args.Args.Length >= 3 && string.Equals(args.Args[2], "show", StringComparison.OrdinalIgnoreCase); args.Context.AddString(AxeGeometryAuditService.ArmMessage(show)); return; }
            if (action == "vfx") { VfxAuditionService.ExecuteAlias(args); return; }
            if (action == "assetdump") { RuntimeAssetAuditService.Execute(args); return; }
            if (action == "workshop")
            {
                if (args.Args.Length >= 3 && args.Args[2] == "craft")
                { args.Context.AddString(WorkshopRemoteCraft.BeginSelectedCraft()); return; }
                Player player = Player.m_localPlayer;
                args.Context.AddString(player == null ? "Player is not ready." :
                    WorkshopRemoteCraft.DescribeStatus() + "\n" + WorkshopNetwork.DebugSummary(player.transform.position));
                return;
            }
#if MASTERY_SPEAR35_EXPERIMENT
            if (action == "speartrace") { args.Context.AddString(SpearThrowLifecycleService.DebugSummary(Player.m_localPlayer)); return; }
            if (action == "spearrecall") { args.Context.AddString(SpearThrowLifecycleService.TryRecall(Player.m_localPlayer)); return; }
            if (action == "spearaudit") { args.Context.AddString(NetworkSync.SendSpearSkillAudit()); return; }
#endif
            if (action == "audition") { RuntimeAssetAuditService.Audition(args); return; }
            if (action == "shadowstep" && args.Args.Length >= 3 && string.Equals(args.Args[2], "ready", StringComparison.OrdinalIgnoreCase)) { ArmShadowStep(args); return; }
            if (action == "knife70" && args.Args.Length >= 3 && HandleKnife70(args)) return;
            if (action == "fists70" && args.Args.Length >= 3) { HandleFists70(args); return; }
            if (action == "bow70" && args.Args.Length >= 3) { HandleBow70(args); return; }
            if (action == "sword70" && args.Args.Length >= 3) { HandleSword70(args); return; }
            if ((action == "knife35" || action == "knife70") && args.Args.Length >= 3 && string.Equals(args.Args[2], "targetdebug", StringComparison.OrdinalIgnoreCase)) { args.Context.AddString(AssassinBlink70Service.GetTargetDebug(Player.m_localPlayer)); return; }
            if ((action == "knife35" || action == "knife70") && args.Args.Length >= 4 && string.Equals(args.Args[2], "cooldown", StringComparison.OrdinalIgnoreCase) && args.Args[3] == "0") { ClearKnife35Cooldown(args); return; }
            if (action == "polearmstate") { PrintPolearmState(args); return; }
            args.Context.AddString("Unknown vm action: " + action);
        }

        private static void HandleSword70(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length >= 4 && string.Equals(args.Args[2], "debug", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(args.Args[3], "on", StringComparison.OrdinalIgnoreCase) || string.Equals(args.Args[3], "off", StringComparison.OrdinalIgnoreCase)))
            {
                bool enabled = string.Equals(args.Args[3], "on", StringComparison.OrdinalIgnoreCase);
                SwordOffensiveParryService.SetDebug(enabled);
                args.Context.AddString("Sword70 debug=" + enabled + ". " + SwordOffensiveParryService.DebugSummary(Player.m_localPlayer));
                return;
            }
            if (string.Equals(args.Args[2], "state", StringComparison.OrdinalIgnoreCase))
            {
                args.Context.AddString(SwordOffensiveParryService.DebugSummary(Player.m_localPlayer));
                return;
            }
            args.Context.AddString("Usage: vm sword70 debug on|off | vm sword70 state");
        }

        private static void HandleBow70(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            string sub = args.Args[2].ToLowerInvariant();
            OverdrawState state = MasteryStateStore.GetPlayerState<OverdrawState>(player);
            if (sub == "charge")
            {
                state.FullDrawSince = Time.time - Overdraw70Service.BuildupStart; state.ChargedUntil = 0f; state.Phase = OverdrawPhase.Charging;
                args.Context.AddString("Bow70 charge state armed. Keep the bow drawn."); return;
            }
            if (sub == "ready")
            {
                state.FullDrawSince = Time.time - Overdraw70Service.HoldAfterFullDraw; state.ChargedUntil = float.PositiveInfinity; state.Phase = OverdrawPhase.Ready;
                args.Context.AddString("Bow70 ready state armed. Keep the bow drawn and release."); return;
            }
            if (sub == "debug" && args.Args.Length >= 4)
            {
                Bow70DebugState.Enabled = string.Equals(args.Args[3], "on", StringComparison.OrdinalIgnoreCase);
                args.Context.AddString("Bow70 debug=" + Bow70DebugState.Enabled); return;
            }
            if (sub == "penetrationdebug") { Bow70DebugState.Enabled = true; args.Context.AddString(Bow70DebugState.Summary(player)); return; }
            if (sub == "shockwavedebug")
            {
                Vector3 direction = player.GetLookDir().normalized; Bow70ShockwaveVisual.Spawn(player.GetCenterPoint() + direction * 1.2f, direction);
                args.Context.AddString("Bow70 shockwave visual spawned; raw damage=0 by design."); return;
            }
            args.Context.AddString("Usage: vm bow70 charge | ready | debug on|off | penetrationdebug | shockwavedebug");
        }

        private static void HandleFists70(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            string sub = args.Args[2].ToLowerInvariant();
            if (sub == "gauge" && args.Args.Length >= 4 && float.TryParse(args.Args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gauge))
            { NetworkSync.SendClientAbility("fists70_debug_gauge:" + Mathf.Clamp01(gauge).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); args.Context.AddString("Fists70 gauge request sent."); return; }
            if (sub == "ready") { NetworkSync.SendClientAbility("fists70_debug_ready"); args.Context.AddString("Fists70 ready request sent."); return; }
            if (sub == "cooldown" && args.Args.Length >= 4 && float.TryParse(args.Args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            { NetworkSync.SendClientAbility("fists70_debug_cooldown:" + Mathf.Max(0f, seconds).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)); args.Context.AddString("Fists70 cooldown request sent."); return; }
            Character target = Fists70ClientService.FindTarget(player, 30f);
            if (target == null) { args.Context.AddString("Fists70 target: none."); return; }
            if (sub == "target") { args.Context.AddString("Fists70 target=" + target.gameObject.name + " class=" + Fists70TargetClassifier.Classify(target)); return; }
            if (sub == "state")
            {
                if (!Fists70ClientService.TryGet(target, out Fists70ClientTargetState state)) { args.Context.AddString("Fists70 state not synced for target."); return; }
                args.Context.AddString("TargetClass=" + state.TargetClass + " Gauge=" + state.Gauge.ToString("P0") + " Hits=" + state.Hits + " Cooldown=" + Mathf.Max(0f, state.CooldownUntil - Time.time).ToString("0.0") + " MaulState=" + state.Phase + " Reserved=" + state.ReservedRemaining.ToString("0.0"));
                return;
            }
            args.Context.AddString("Usage: vm fists70 gauge <0..1> | ready | cooldown <seconds> | state | target");
        }

        private static bool HandleKnife70(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return true; }
            string sub = args.Args[2].ToLowerInvariant();
            string command;
            if (sub == "chance" || sub == "samechance" || sub == "otherchance")
            {
                if (args.Args.Length < 4 || !float.TryParse(args.Args[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                { args.Context.AddString("Usage: vm knife70 " + sub + " <0..1>"); return true; }
                command = sub + ":" + Mathf.Clamp01(value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (sub == "debug")
            {
                if (args.Args.Length < 4 || (args.Args[3] != "on" && args.Args[3] != "off")) { args.Context.AddString("Usage: vm knife70 debug on|off"); return true; }
                command = "debug:" + args.Args[3];
                if (args.Args[3] == "off") Knife70DebugOverlay.Set("", false);
            }
            else if (sub == "chain" || sub == "stop" || sub == "state") command = sub;
            else if (sub == "shadow")
            {
                Character target = AssassinBlink70Service.FindCrosshairEnemy(player);
                if (target == null) { args.Context.AddString("Aim at a valid hostile target within 20 m."); return true; }
                ZDOID id = target.GetZDOID();
                command = "shadow:" + id.UserID.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + id.ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else return false;

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                string result = Knife70ShadowStrikeService.HandleDebugCommand(player, command);
                args.Context.AddString(result);
                Knife70DebugOverlay.Set(result + " | " + Knife70ShadowStrikeService.DebugSummary(), sub == "debug" ? args.Args[3] == "on" : true);
            }
            else
            {
                NetworkSync.SendKnife70DebugCommand(command);
                args.Context.AddString("Knife70 command sent to authoritative server: " + sub);
            }
            return true;
        }
        private static void ArmShadowStep(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            ShadowStep35Service.ArmLocal(player, ShadowStep35Service.ReadyDuration);
            args.Context.AddString("ShadowStep READY for 5.0s.");
        }

        private static void ClearKnife35Cooldown(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            PerkCooldownStateService.ReduceRemaining(player, "knives_35", 99999f);
            args.Context.AddString("Knife35 blink cooldown cleared.");
        }

        private static void PrintPolearmState(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            if (MasteryStateStore.TryGetPlayerState<Polearm35SpinState>(player, out Polearm35SpinState follow))
                args.Context.AddString("Polearms35 pending=" + follow.Pending + " remain=" + Mathf.Max(0f, follow.ExpiresAt - Time.time).ToString("0.00") + "s");
            args.Context.AddString("Polearms70 " + Polearms70ContinuousSpinService.GetDebugState(player));
        }
        private static void PrintAllStates(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            foreach (Skills.Skill entry in player.GetSkills().GetSkillList())
            {
                if (entry?.m_info == null || !PerkCatalog.Contains(entry.m_info.m_skill)) continue;
                float level = entry.m_level;
                args.Context.AddString(entry.m_info.m_skill + "=" + level.ToString("0.0") + " [" + (level >= 35f ? "35 " : "") + (level >= 70f ? "70 " : "") + (level >= 100f ? "100" : "") + "]");
            }
            if (MasteryStateStore.TryGetPlayerState<ShadowStepState>(player, out ShadowStepState shadow))
                args.Context.AddString("ShadowStep=" + (shadow.ReadyUntil > Time.time ? "READY " + (shadow.ReadyUntil - Time.time).ToString("0.0") + "s" : "off"));
            if (MasteryStateStore.TryGetPlayerState<OverdrawState>(player, out OverdrawState overdraw))
            {
                float held = overdraw.FullDrawSince < 0f ? 0f : Mathf.Max(0f, Time.time - overdraw.FullDrawSince);
                args.Context.AddString("Overdraw=" + Mathf.Min(2.5f, held).ToString("0.0") + "/2.5 phase=" + overdraw.Phase + (overdraw.Phase == OverdrawPhase.Ready ? " READY" : ""));
            }
            args.Context.AddString("AssassinBlink cooldown=" + PerkCooldownStateService.GetRemainingSeconds(player, "knives_35").ToString("0.0") + "s");
            args.Context.AddString("Stride=" + Stride70Service.GetStacks(player) + "/3");
            if (MasteryStateStore.TryGetPlayerState<BlockingStoredPressureState>(player, out BlockingStoredPressureState pressure))
                args.Context.AddString("StoredPressure=" + pressure.Value.ToString("0.00") + " | " + Mathf.Max(0f, pressure.ExpiresAt - Time.time).ToString("0.0") + "s");
            if (MasterFeastService.TryGetActive(player, out MasterFeastState feast))
                args.Context.AddString("Feast=" + feast.Theme + " | " + Mathf.Max(0f, feast.ExpireTime - Time.time).ToString("0.0") + "s");
        }
        private static void SetSkill(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null || args.Args.Length < 3 || !Enum.TryParse(args.Args[1], true, out Skills.SkillType skill) || !float.TryParse(args.Args[2], out float level))
            {
                args.Context.AddString("Usage: vm_skill <SkillType> <level>");
                return;
            }
            foreach (Skills.Skill entry in player.GetSkills().GetSkillList())
                if (entry?.m_info != null && entry.m_info.m_skill == skill)
                {
                    entry.m_level = Math.Max(0f, Math.Min(100f, level));
                    args.Context.AddString(skill + " actual level=" + entry.m_level);
                    return;
                }
            args.Context.AddString("Skill not found: " + skill);
        }

        private static void SetAllSkills70(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("Player is not ready."); return; }
            int changed = 0;
            foreach (Skills.Skill entry in player.GetSkills().GetSkillList())
                if (entry?.m_info != null && PerkCatalog.Contains(entry.m_info.m_skill))
                {
                    entry.m_level = 70f;
                    changed++;
                }
            args.Context.AddString("Valheim Mastery: set " + changed + " skills to level 70.");
        }
        private static void GiveItem(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null || args.Args.Length < 2)
            {
                args.Context.AddString("Usage: vm_give <PrefabName> [amount]");
                return;
            }
            int amount = 1;
            if (args.Args.Length >= 3) int.TryParse(args.Args[2], out amount);
            amount = Math.Max(1, Math.Min(100, amount));
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(args.Args[1]);
            if (prefab == null)
            {
                args.Context.AddString("Unknown item prefab: " + args.Args[1]);
                return;
            }
            player.GetInventory().AddItem(prefab, amount);
            args.Context.AddString("Added " + amount + " x " + args.Args[1]);
        }
        private static void ForceProc(Terminal.ConsoleEventArgs args)
        {
            PerkRuntimeService.ForceProc = args.Args.Length >= 2 && string.Equals(args.Args[1], "on", StringComparison.OrdinalIgnoreCase);
            args.Context.AddString("Mastery ForceProc=" + PerkRuntimeService.ForceProc);
        }

        private static void ClearCooldowns(Terminal.ConsoleEventArgs args)
        {
            PerkCooldownStateService.Clear(Player.m_localPlayer);
            args.Context.AddString("Mastery cooldowns cleared.");
        }

        private static void PrintState(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null || args.Args.Length < 2 || !Enum.TryParse(args.Args[1], true, out Skills.SkillType skill))
            {
                args.Context.AddString("Usage: vm_state <SkillType>");
                return;
            }
            float level = PerkRuntimeService.GetActualSkillLevel(Player.m_localPlayer, skill);
            args.Context.AddString(skill + ": level=" + level + ", perks=" + (level >= 35 ? "35 " : "") + (level >= 70 ? "70 " : "") + (level >= 100 ? "100" : ""));
        }
        private static void SetPressure(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            if (player == null || args.Args.Length < 2 || !float.TryParse(args.Args[1], out float value)) { args.Context.AddString("Usage: vm_pressure <0-1>"); return; }
            BlockingStoredPressureState state = MasteryStateStore.GetPlayerState<BlockingStoredPressureState>(player);
            state.Value = Mathf.Clamp01(value); state.ExpiresAt = Time.time + 3f;
            args.Context.AddString("StoredPressure=" + state.Value.ToString("0.00") + " for 3s");
        }

        private static void SetChop(Terminal.ConsoleEventArgs args)
        {
            Player player = Player.m_localPlayer;
            Character target = GetAimedTarget(player);
            if (player == null || target == null || args.Args.Length < 2 || !int.TryParse(args.Args[1], out int stacks)) { args.Context.AddString("Aim at an enemy, then use vm_chop <0-3>"); return; }
            stacks = Mathf.Clamp(stacks, 0, 3);
            if (stacks == 0) TargetEffectService.Remove(target, Axe35Service.ChopChopId);
            else TargetEffectService.Apply(target, Axe35Service.ChopChopId, player, stacks, stacks * Axe35Service.VulnerabilityPerStack, 180f);
            args.Context.AddString("Chop-chop=" + stacks + "/3 on " + target.name);
        }

        private static void PrintTargetEffects(Terminal.ConsoleEventArgs args)
        {
            Character target = GetAimedTarget(Player.m_localPlayer);
            if (target == null) { args.Context.AddString("Aim at a creature first."); return; }
            args.Context.AddString("Effects on " + target.name + ":");
            foreach (TargetEffect effect in TargetEffectService.GetActive(target)) args.Context.AddString("  " + effect.Id + " stacks=" + effect.Stacks + " strength=" + effect.Strength.ToString("0.##") + " remain=" + Mathf.Max(0f, effect.ExpiresAt - Time.time).ToString("0.0"));
        }

        private static Character GetAimedTarget(Player player)
        {
            if (player == null) return null;
            Vector3 eye = player.GetEyePoint();
            if (!Physics.Raycast(eye, player.GetAimDir(eye), out RaycastHit hit, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return null;
            return hit.collider?.GetComponentInParent<Character>();
        }
        private static void ListFeasts(Terminal.ConsoleEventArgs args)
        {
            int count = 0;
            foreach (GameObject prefab in ObjectDB.instance?.m_items ?? new System.Collections.Generic.List<GameObject>())
                if (prefab != null && prefab.name.IndexOf("feast", StringComparison.OrdinalIgnoreCase) >= 0)
                { args.Context.AddString(prefab.name); count++; }
            args.Context.AddString("Feast prefabs=" + count);
        }
    }
}

