namespace ValheimMastery
{
    // Read-only native discovery and presentation. Never unlocks a recipe or perk.
    internal static class CrossbowsHookSecretText
    {
        internal static bool Discovered(Player player) => player != null &&
            (player.IsRecipeKnown("$item_graplinghook") || player.IsMaterialKnown("$item_graplinghook"));
        internal static string Progression(Player player, bool ua)
        {
            string text = ua
                ? "Із досвідом арбалет краще парирує й сильніше вражає оглушених істот."
                : "As your skill grows, crossbows parry better and hit staggered creatures harder.";
            if (Discovered(player)) text += ua
                ? " Гак заряджається швидше й сягає далі."
                : " The grappling hook charges faster and reaches farther.";
            return text;
        }
        internal static string ReloadDescription(bool ua) => ua
            ? "Готуючи важкий постріл, ти тримаєш удар. Перезарядка дає +20 броні та ще 40% від поточної, подвоює межу оглушення й не втрачає захисту на бігу. Завершення чи переривання заряджання прибирає цей захист."
            : "Preparing a heavy shot, you stand firm. Reloading grants +20 armor and another 40% of your current armor, doubles stagger capacity, and keeps its protection while sprinting. Finishing or interrupting the reload ends that protection.";
        internal static string Section(Player player, bool ua)
        {
            if (!Discovered(player)) return "";
            return ua
                ? "\n<color=#69b9d9>Таємниця майстра — гак. Приціл допомагає оцінити, чи закріпиться гак. Його перезаряджання теж додає броню та стійкість до оглушення.</color>"
                : "\n<color=#69b9d9>Master's secret — the grappling hook. The reticle helps you judge whether the hook can attach. Reloading it also grants armor and stagger resistance.</color>";
        }
    }
}
