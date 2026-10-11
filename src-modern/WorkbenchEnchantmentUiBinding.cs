using System;
using System.Linq;
namespace ValheimMastery
{
    internal static class WorkbenchEnchantmentUiBinding
    {
        internal static void Initialize()
        {
            WorkbenchEnchantmentUi.Configure(WorkbenchEnchantmentNetwork.Snapshot,WorkbenchEnchantmentNetwork.Request,
                ApprovedUiAssetBank.Enchantment,Detail);
        }
        internal static void Shutdown()=>WorkbenchEnchantmentUi.Shutdown();
        static string Detail(string choice)
        {
            switch(choice)
            {
                case "Meadows":return GoldUiLocalization.Text("Crops, animal breeding and maturation progress twice as fast. Tamed animal limits double; incoming damage is reduced by 75%.","Рослини, розмноження й дорослішання тварин прискорюються вдвічі. Ліміт свійських тварин подвоюється; отримана ними шкода зменшується на 75%.");
                case "BlackForest":return GoldUiLocalization.Text("Friendly forest residents patrol and help the workshop: four greylings, two greydwarfs, one shaman and one brute. The shaman can heal allies.","Дружні лісові мешканці обходять майстерню й допомагають їй: чотири грейлінги, два грейдворфи, шаман і велетень. Шаман може лікувати союзників.");
                case "Swamp":return GoldUiLocalization.Text("A sanctuary keeps hostile creatures outside and shields the workshop from rain.","Святилище утримує ворожих істот за межею та захищає майстерню від дощу.");
                case "Mountain":return GoldUiLocalization.Text("Furnaces and cooking progress faster. Buildings receive 90% less damage; buildings and equipment gradually repair.","Печі та приготування працюють швидше. Будівлі отримують на 90% менше шкоди; будівлі й спорядження поступово відновлюються.");
                case "Plains":return GoldUiLocalization.Text("Feasts consumed here last three times longer. Active Rested, boss powers and poison, frost and fire resistance are renewed while you remain under the enchantment.","Спожиті тут бенкети тривають утричі довше. Поки діють чари, поновлюються активні відпочинок, сили босів та опір отруті, морозу й вогню.");
                case "Mistlands":return GoldUiLocalization.Text("Feather fall protects players. Eitr refining and sap collection progress faster; refinery projectiles cannot damage buildings in the workshop.","Легке падіння захищає гравців. Очищення ейтру та збирання соку прискорюються; снаряди очищувача не пошкоджують будівлі майстерні.");
                default:return "";
            }
        }
    }
}

