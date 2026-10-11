using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    // This file exists ONLY in the disposable passive-test snapshot/package.
    // Explicit console invocation mutates only the local test player's skills/items.
    [HarmonyPatch(typeof(PerkDebugService), nameof(PerkDebugService.RegisterCommands))]
    internal static class Combat100PassiveTestCommands
    {
        private static readonly string[] Candidates = { "TrinketSilverResist", "TrinketSilverDamage", "TrinketBronzeHealth", "TrinketBronzeStamina", "TrinketIronHealth", "TrinketIronStamina", "TrinketBlackDamageHealth", "TrinketBlackStamina" };
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("vm_c100", "TEST BUILD: vm_c100 preview Unarmed|Polearms | state | cross Unarmed|Polearms | below Unarmed|Polearms | items | kit", Run, false);
            MasteryPlugin.Log.LogWarning("[Combat100 TEST] Passive-only test kit. Paid Combat100 classes excluded. vm_c100 enabled; use a disposable character.");
        }
        private static void Run(Terminal.ConsoleEventArgs args)
        {
            var p = Player.m_localPlayer;
            if (p == null || p.m_nview?.IsOwner() != true || p.IsDead() || MasteryPlugin.Settings?.Enabled.Value != true)
            { args.Context.AddString("Local living owner and enabled Mastery required."); return; }
            try
            {
                string action = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "state";
                if (action == "state") { State(p, args); return; }
                if (action == "items" || action == "kit") { Items(p, args, action == "kit"); return; }
                if (action == "give" && args.Args.Length == 3)
                {
                    string name = args.Args[2];
                    if (name != "FistFenrirClaw" && Array.IndexOf(Candidates, name) < 0)
                    { args.Context.AddString("Only FistFenrirClaw and listed first-five-material trinkets supported."); return; }
                    var prefab = ObjectDB.instance?.GetItemPrefab(name); var item = prefab?.GetComponent<ItemDrop>()?.m_itemData;
                    if (item == null || (name != "FistFenrirClaw" && (item.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.Trinket || !Combat100DualTrinkets.SafeEffect(item.m_shared.m_fullAdrenalineSE))))
                    { args.Context.AddString("Unavailable or unsupported trinket: " + name + "; use vm_c100 items/kit."); return; }
                    args.Context.AddString(p.GetInventory().AddItem(prefab, 1) != null ? "Added1 x " + name : "Inventory full; nothing added."); return;
                }
                if ((action != "cross" && action != "below" && action != "preview") || args.Args.Length != 3 ||
                    !Enum.TryParse(args.Args[2], true, out Skills.SkillType type) ||
                    (type != Skills.SkillType.Unarmed && type != Skills.SkillType.Polearms))
                { args.Context.AddString("vm_c100 state | cross Unarmed|Polearms | below Unarmed|Polearms | items | kit"); return; }
                if (MasteryMilestonePresentation.IsActive || PatronAscensionPresentation.IsPendingOrActive)
                { args.Context.AddString("Wait until both celebration scenes finish."); return; }
                if (action == "preview")
                {
                    foreach (var perk in PerkCatalog.Get(type))
                        if (perk.Milestone == 100)
                        {
                            MasteryMilestonePresentation.Show(p, type, 100, perk);
                            PatronAscensionPresentation.Schedule(p, type);
                            args.Context.AddString("Лише VFX/SFX: новий урок ворона не створюється. Після сцени може продовжитися стара непрочитана черга. Для першого відкриття: cross на новому тестовому персонажі."); return;
                        }
                    args.Context.AddString("No milestone definition for preview."); return;
                }
                var skills = p.GetSkills(); var entry = skills.GetSkill(type);
                bool recognized = PerkStateService.WasEverUnlocked(p, type, 100);
                entry.m_level = 99f; entry.m_accumulator = 0f;
                if (action == "cross")
                {
                    // Native Raise checks the real threshold, increments once, calls
                    // native OnSkillLevelup and normal Mastery RaiseSkill postfix.
                    entry.m_accumulator = entry.GetNextLevelRequirement();
                    skills.RaiseSkill(type, 1f);
                    args.Context.AddString(type + " actual=" + entry.m_level + "; first patron recognition=" + !recognized);
                    if (entry.m_level < 100f) args.Context.AddString("Native XP did not reach100; do not count celebration as tested.");
                }
                else args.Context.AddString(type + " actual99; unlock history preserved; this is a negative passive test.");
            }
            catch (Exception e) { args.Context.AddString("Combat100 test failed: " + e.Message); MasteryPlugin.Log.LogError("[Combat100 TEST] " + e); }
        }
        private static void State(Player p, Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("Combat100 passive TEST; paid abilities excluded from this DLL. World=" + (ZNet.instance?.GetWorldUID() ?? 0));
            args.Context.AddString("Unarmed=" + PerkRuntimeService.GetActualSkillLevel(p, Skills.SkillType.Unarmed) + "; Polearms=" + PerkRuntimeService.GetActualSkillLevel(p, Skills.SkillType.Polearms));
            args.Context.AddString("Adrenaline=" + p.m_adrenaline + "/" + p.GetMaxAdrenaline());
            foreach (var item in p.GetInventory().GetAllItems())
                if (item.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Trinket)
                    args.Context.AddString(item.m_shared.m_name + "; equipped=" + item.m_equipped + "; secondary=" + item.m_customData.ContainsKey(Combat100DualTrinkets.SlotKey) + "; fullSE=" + (item.m_shared.m_fullAdrenalineSE?.name ?? "none") + "; safe=" + Combat100DualTrinkets.SafeEffect(item.m_shared.m_fullAdrenalineSE));
            var se = p.GetSEMan();
            if (se != null) foreach (var effect in se.m_statusEffects) args.Context.AddString("ActiveSE=" + effect.name);
        }
        private static void Items(Player p, Terminal.ConsoleEventArgs args, bool give)
        {
            var safe = new List<GameObject>(); var names = new List<string>(); var hashes = new HashSet<int>();
            foreach (string name in Candidates)
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(name); var item = prefab?.GetComponent<ItemDrop>()?.m_itemData;
                var effect = item?.m_shared?.m_fullAdrenalineSE;
                bool supported = item?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Trinket && Combat100DualTrinkets.SafeEffect(effect);
                args.Context.AddString(name + ": found=" + (item != null) + "; compatible=" + supported + "; SE=" + (effect?.GetType().Name ?? "none"));
                if (supported && hashes.Add(effect.NameHash())) { safe.Add(prefab); names.Add(name); }
            }
            if (!give) return;
            var weapon = ObjectDB.instance?.GetItemPrefab("FistFenrirClaw");
            if (weapon == null || safe.Count < 2)
            { args.Context.AddString("Kit refused: fist weapon or two distinct supported first-five-material trinkets unavailable. No items added."); return; }
            var inventory = p.GetInventory();
            if (inventory.AddItem(weapon, 1) == null) { args.Context.AddString("Inventory full; weapon not added. No trinkets added."); return; }
            for (int i = 0; i < 2; ++i)
            {
                if (inventory.AddItem(safe[i], 1) == null)
                {
                    args.Context.AddString("Inventory full; partial kit. Already added items remain. Free space, then issue ALL following missing-item commands:");
                    for (int missing = i; missing < 2; ++missing) args.Context.AddString("vm_c100 give " + names[missing]);
                    return;
                }
            }
            args.Context.AddString("Added FistFenrirClaw + " + names[0] + " + " + names[1] + ". Equip both trinkets manually; no Favor changed.");
        }
    }
}
