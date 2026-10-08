using HarmonyLib;
namespace ValheimMastery
{
    internal static class MasterIdolEffectsSummary
    {
        internal static string For(string type)=>type=="Meadows"
            ?GoldUiLocalization.Text("Crops, breeding, maturation and tame population ×2. Tamed creatures take 75% less damage and stay within the workshop.","Ріст, розмноження, дорослішання й ліміт свійських тварин ×2. Вони отримують на 75% менше шкоди й залишаються біля майстерні.")
            :type=="BlackForest"?GoldUiLocalization.Text("Greydwarfs become friendly residents. They respect protected players, tamed creatures and settlement structures, and stay near home.","Грейдворфи стають дружніми мешканцями. Вони не чіпають дозволених гравців, свійських тварин та споруди й тримаються біля дому.")
            :type=="Swamp"?GoldUiLocalization.Text("A sanctuary dome shelters this workshop from hostile creatures and rain, up to 50 metres.","Купол до 50 метрів оберігає майстерню від ворожих істот і дощу.")
            :type=="Mountain"?GoldUiLocalization.Text("Heat processing ×2, structures take 90% less attack damage, and equipment and buildings recover 1% durability per second.","Теплове виробництво ×2, споруди отримують на 90% менше шкоди від атак. Спорядження й будівлі відновлюють 1% міцності за секунду.")
            :type=="Plains"?GoldUiLocalization.Text("Feasts eaten here last three times longer. Active Forsaken powers, Rested and resistance meads are renewed.","Спожиті тут застілля тривають утричі довше. Активні сили босів, Rested і медовиці опору поновлюються.")
            :type=="Mistlands"?GoldUiLocalization.Text("The mist clears; Feather Cape movement is granted, magical processing is faster, and refinery radiation cannot damage workshop structures.","Туман розсіюється; діє рух пір’яної накидки, магічне виробництво прискорене, випромінювання рафінерії не шкодить спорудам майстерні.")
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


