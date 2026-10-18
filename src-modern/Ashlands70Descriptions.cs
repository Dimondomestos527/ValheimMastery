using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace ValheimMastery
{
    internal static class Ashlands70Descriptions
    {
        internal static bool Ukrainian => Localization.instance != null && string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", StringComparison.OrdinalIgnoreCase);
        internal static string Name(Ashlands70Kind kind) => Ukrainian ?
            (kind == Ashlands70Kind.Madness ? "Поклик безумства" : kind == Ashlands70Kind.Chain ? "Кайдани грози" : kind == Ashlands70Kind.Grove ? "Жива галявина" : "Вихор ламкості") :
            (kind == Ashlands70Kind.Madness ? "Call of madness" : kind == Ashlands70Kind.Chain ? "Storm shackles" : kind == Ashlands70Kind.Grove ? "Living grove" : "Fracturing whirlwind");
        internal static string Text(Ashlands70Kind kind) => Ukrainian ?
            (kind == Ashlands70Kind.Madness ? "Посох занурює всіх істот поруч у безумство: вони ворогують з усіма, навіть із господарями та союзниками. Беззбройні істоти тікають від усіх." :
             kind == Ashlands70Kind.Chain ? "Нерухомий магічний ланцюг проймає червона блискавка. Вороги, які торкаються його, знову й знову отримують силу одного снаряда Дандра." :
             kind == Ashlands70Kind.Grove ? "Довкола проростає галявина з рослинами різних земель. Вона прискорює відновлення здоров’я, витривалості й ейтру гравців на 25% та зменшує отриману шкоду на 15%; дружні прикликані істоти отримують удвічі сильніший захист." :
             "Десять уламків кружляють навколо власника, даруючи 20 броні. Поглинений бар’єром, заблокований чи отриманий удар спрямовує один уламок у нападника. Кожен уламок витрачається один раз; броня лишається до завершення вихору.") :
            (kind == Ashlands70Kind.Madness ? "The staff drives every nearby creature mad: they turn against everyone, including masters and allies. Unarmed creatures flee from everyone." :
             kind == Ashlands70Kind.Chain ? "Red lightning courses through a stationary magical chain. Foes touching it repeatedly suffer the force of one Dundr projectile." :
             kind == Ashlands70Kind.Grove ? "A grove of plants from distant lands blooms nearby. Players regenerate health, stamina and eitr 25% faster and take 15% less damage; friendly summoned creatures receive twice that protection." :
             "Ten fragments orbit the wielder, granting 20 armor. A hit absorbed by a barrier, blocked or suffered sends one fragment at the attacker. Each fragment is spent once; armor remains until the whirlwind ends.");
        private static readonly Dictionary<Ashlands70Kind, StatusEffect> Hud = new Dictionary<Ashlands70Kind, StatusEffect>();
        internal static void Status(SEMan manager, List<StatusEffect> effects)
        {
            if (Application.isBatchMode || !(manager.m_character is Player player) || player != Player.m_localPlayer || player.IsDead() || !MasteryPlugin.Settings.Enabled.Value) return;
            Ashlands70State orbit = Ashlands70World.Orbit(player);
            if (orbit != null) Add(effects, Ashlands70Kind.Orbit, (float)((orbit.Grant.Until - Ashlands70Cast.Now) / (double)TimeSpan.TicksPerSecond), orbit.Remaining);
            if (Ashlands70World.InGrove(player)) Add(effects, Ashlands70Kind.Grove, 0f, -1);
        }
        private static void Add(List<StatusEffect> effects, Ashlands70Kind kind, float left, int count)
        {
            // These objects are HUD projections only: never Setup/AddStatusEffect or gameplay modifiers.
            if (!Hud.TryGetValue(kind, out StatusEffect status)) { status = ScriptableObject.CreateInstance<StatusEffect>(); status.name = "VM_Ashlands70Hud_" + kind; Hud.Add(kind, status); }
            if (status.m_icon == null)
            {
                GameObject item = ObjectDB.instance?.GetItemPrefab(Ashlands70Rules.Staff(kind));
                Sprite[] icons = item?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_icons;
                if (icons == null || icons.Length == 0 || icons[0] == null) return;
                status.m_icon = icons[0];
            }
            status.m_name = Name(kind) + (count >= 0 ? " · " + count + "/10" : ""); status.m_tooltip = Text(kind);
            status.m_ttl = Mathf.Max(0f, left); status.m_time = 0f; effects.Add(status);
        }
        internal static void Reset() { foreach (StatusEffect status in Hud.Values) if (status != null) UnityEngine.Object.Destroy(status); Hud.Clear(); }
    }
    [HarmonyPatch(typeof(SEMan), "GetHUDStatusEffects")]
    internal static class Ashlands70StatusHudPatch
    { private static void Postfix(SEMan __instance, List<StatusEffect> __0) => Ashlands70Descriptions.Status(__instance, __0); }
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
    internal static class Ashlands70ItemDescriptionPatch
    {
        private const string Marker = "<color=#DD9977>• 70 — ";
        [HarmonyPriority(int.MinValue)] private static void Postfix(ItemDrop.ItemData __instance, ref string __result)
        {
            if (__instance?.m_shared == null || string.IsNullOrEmpty(__result) || __result.Contains(Marker)) return;
            Ashlands70Kind kind = Ashlands70Rules.Kind(PerkRuntimeService.ItemPrefabName(__instance));
            if (kind == Ashlands70Kind.None || __instance.m_shared.m_skillType != Ashlands70Rules.Skill(kind)) return;
            bool ua = Ashlands70Descriptions.Ukrainian, unlocked = PerkRuntimeService.HasPerk(Player.m_localPlayer, Ashlands70Rules.Skill(kind), 70);
            string dormant = unlocked ? "" : ua ? "ще дрімає — " : "dormant — ";
            __result += "\n" + Marker + dormant + Ashlands70Descriptions.Name(kind) + ": " + Ashlands70Descriptions.Text(kind) + "</color>";
        }
    }
}
