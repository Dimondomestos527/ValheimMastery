using HarmonyLib;
namespace ValheimMastery
{
    internal static class MasterIdolEffectsSummary
    {
        internal static string For(string type)=>type=="Meadows"
            ?GoldUiLocalization.Text("Crops, breeding, maturation and tame population ×2. Tamed creatures take 75% less damage and stay within the workshop.","Ріст, розмноження, дорослішання й ліміт свійських тварин ×2. Вони отримують на 75% менше шкоди й залишаються біля майстерні.")
            :type=="BlackForest"?GoldUiLocalization.Text("Greydwarfs become friendly residents. They respect protected players, tamed creatures and settlement structures, and stay near home.","Грейдворфи стають дружніми мешканцями. Вони не чіпають дозволених гравців, свійських тварин та споруди й тримаються біля дому.")
            :GoldUiLocalization.Text("This land's blessing has not awakened yet.","Благословення цього краю ще не пробуджене.");
    }
    [HarmonyPatch(typeof(MasteryPlugin),"Update")]
    internal static class MasterIdolBiomeEffects
    {
        private static void Postfix()=>MasterIdolEffectZones.Tick();
    }
    [HarmonyPatch(typeof(NetworkSync),nameof(NetworkSync.Register))]
    internal static class MasterIdolEffectNetworkPatch
    {private static void Postfix(ZNetPeer peer)=>MasterIdolEffectZones.Register(peer);}
}
