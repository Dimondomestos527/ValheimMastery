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
        internal static string Detail(string choice)
        {
            switch(choice)
            {
                case "Meadows":return GoldUiLocalization.Text("Under this blessing, crops ripen sooner and tame animals breed and mature faster. More can thrive near the workshop, where they withstand blows better.","Під цими чарами врожай достигає швидше, а свійські тварини швидше розмножуються й дорослішають. У межах майстерні їх може бути більше, і вони краще витримують удари.");
                case "BlackForest":return GoldUiLocalization.Text("Greylings and greydwarfs settle near the workshop and help defend it. Their shaman supports allies with healing.","Грейлінги та грейдворфи оселяються біля майстерні й допомагають її обороняти. Шаман підтримує союзників зціленням.");
                case "Swamp":return GoldUiLocalization.Text("The blessing forms a quiet haven: it holds hostile creatures at bay, shelters you from rain-soaking, and protects structures from rain wear.","Чари утворюють тихий прихисток: стримують ворожих істот, захищають тебе від дощового промокання, а споруди — від зношування під дощем.");
                case "Mountain":return GoldUiLocalization.Text("The forge's heat hastens furnaces and cooking. Structures withstand attacks better; they and equipment in your inventory gradually regain durability.","Жар горна пришвидшує роботу печей і приготування їжі. Споруди краще витримують атаки; вони та спорядження у твоєму інвентарі поступово відновлюють міцність.");
                case "Plains":return GoldUiLocalization.Text("Feasts eaten here sustain you longer. Within the blessing, already-active Rested, Forsaken powers, and poison, frost and fire resistance are renewed; your Forsaken power recovers sooner.","Бенкети, спожиті тут, довше підтримують твої сили. У межах чарів поновлюються вже активні відпочинок, сили босів та опір отруті, морозу й вогню; сила боса швидше відновлюється.");
                case "Mistlands":return GoldUiLocalization.Text("Within the blessing, mist parts and movement and falling become light, as with a feather cape. Eitr refining and sap collection quicken; refinery projectiles cannot harm workshop structures.","У межах чарів імла розступається, а рух і падіння стають легкими, як у пір’яній накидці. Очищення ейтру та збирання соку пришвидшуються; снаряди очищувача не шкодять спорудам майстерні.");
                default:return "";
            }
        }
    }
}
