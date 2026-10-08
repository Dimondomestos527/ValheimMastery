using System.Collections.Generic;

namespace ValheimMastery
{
    internal static class PerkNarrativeService
    {
        // One short saga line per skill. Mechanical explanations stay in the
        // individual perk descriptions below instead of being buried in prose.
        private static readonly Dictionary<Skills.SkillType, string> UaSkillLead = new Dictionary<Skills.SkillType, string>
        {
            [Skills.SkillType.Cooking] = "Навіть похідна вечеря може стати бенкетом.",
            [Skills.SkillType.Crafting] = "Коваль залишає слід там, де інші бачать брухт.",
            [Skills.SkillType.Farming] = "Земля щедра до того, хто знає її звички.",
            [Skills.SkillType.Fishing] = "Терпіння вікінга довше за риб’ячу пам’ять.",
            [Skills.SkillType.Jump] = "Навіть каміння мусить наздоганяти твої кроки.",
            [Skills.SkillType.Pickaxes] = "Гора міцна, та залізна воля впертіша.",
            [Skills.SkillType.Ride] = "Двоє в дорозі швидші, коли довіряють одне одному.",
            [Skills.SkillType.Run] = "Шлях любить тих, хто не збивається з ритму.",
            [Skills.SkillType.Sneak] = "Тиша буває гострішою за клинок.",
            [Skills.SkillType.Swim] = "Хвиля відступає перед упертим плавцем.",
            [Skills.SkillType.WoodCutting] = "Ліс пам’ятає кожен удар сокири.",
            [Skills.SkillType.Swords] = "Клинок знаходить щілину раніше за думку.",
            [Skills.SkillType.Axes] = "Кожна зарубка наближає останній удар.",
            [Skills.SkillType.Clubs] = "Залізний дзвін переконує краще за погрози.",
            [Skills.SkillType.Knives] = "Тінь ступає там, де ворог не чекає ножа.",
            [Skills.SkillType.Spears] = "Спис пам’ятає дорогу до руки господаря.",
            [Skills.SkillType.Polearms] = "Одне коло сталі тримає цілий натовп.",
            [Skills.SkillType.Bows] = "Тятива шепоче про мить, якої ворог не бачить.",
            [Skills.SkillType.Crossbows] = "Болт летить швидше за погану звістку.",
            [Skills.SkillType.Unarmed] = "Коли зброї немає, відповідь дають кулаки.",
            [Skills.SkillType.Blocking] = "Міцний щит іноді промовляє першим.",
            [Skills.SkillType.Dodge] = "Найкращий удар — той, якого не було.",
            [Skills.SkillType.ElementalMagic] = "Стихії слухають лише впевнену руку.",
            [Skills.SkillType.BloodMagic] = "Кров пам’ятає навіть забутих союзників."
        };
        private static readonly Dictionary<string, string> Ua = new Dictionary<string, string>
        {
            ["cooking_35"]="Приготовані тобою страви довше зберігають поживну силу, а тривалі медовиці діють довше. Майстерний дар працює й для того, кого ти пригостиш.", ["cooking_70"]="Твій бенкет зберігає поживну силу до кінця й дарує благословення свого біому. Новий бенкет змінює благословення, а не додає його до попереднього.",
            ["crafting_35"]="Покращуючи спорядження, майстер інколи зберігає витратні матеріали. Натискання на переробний станок завантажує сировину чи паливо великою порцією.", ["crafting_70"]="Твої конструкції міцніше тримаються, а крафт бере матеріали з доступних скринь у мережі майстерні. Потрібний станок і його рівень усе ще мають значення.",
            ["farming_35"]="Тримай культиватор на панелі швидкого доступу: врожай сам пересаджується, витрачаючи насіння, а дикі культури збираються гуртом і частіше дають зайву здобич.", ["farming_70"]="Посадженим тобою культурам не заважають тіснота й дах, але ґрунт та біом мають підходити. Тварини поруч швидше приручаються.",
            ["fishing_35"]="Успішний вилов нерідко повертає наживку для наступного закидання: навіть черв’як хоче ще побачити світанок.", ["fishing_70"]="Одразу після підсікання витягування риби витрачає значно менше витривалості. Скористайся першою миттю, поки здобич не зібралася з силами.",
            ["jump_35"]="Можеш безпечно падати з більшої висоти: земля не одразу виставляє рахунок за геройство.", ["jump_70"]="Натисни стрибок ще раз у повітрі, щоб відштовхнутися вдруге. Захист від смертельного падіння інколи залишає тебе живим, але потребує перепочинку.",
            ["pickaxes_35"]="Руда та інші не-квестові предмети, яких зазвичай не пускають крізь портали, важать значно менше. Послаблені правила порталів не забирають цей дар.", ["pickaxes_70"]="Удар кайла інколи пошкоджує сусідні частини тієї самої жили. Зрідка могутній удар розсипає всю жилу, супроводжуваний підземним громом.",
            ["ride_35"]="Припини спринт верхи, щоб їздовий звір швидше відновив витривалість. Навіть бикоящур має право перевести подих.", ["ride_70"]="Їздовий звір легше переживає падіння, менше хитається й відлітає від ворожих ударів.",
            ["run_35"]="Безперервний спринт набирає ритм: ти біжиш швидше й бережеш витривалість, доки не зупинишся. Небезпечне поранення повертає частину сил і ненадовго прискорює втечу.", ["run_70"]="На повному ритмі схили й мілководдя менше стримують тебе. Не відпускай спринт, виходячи на глибоку воду: ненадовго можеш бігти по хвилях, після чого потрібен перепочинок.",
            ["sneak_35"]="Навприсядки рухаєшся швидше, витрачаєш менше сил і стаєш менш помітним та гучним. Ліс чує тебе пізніше.", ["sneak_70"]="Завмри навприсядки поза ворожим поглядом, щоб увійти під покров. Здалеку тебе важче помітити; близький ворог, атака, спринт або поранення розкривають схованку.",
            ["swim_35"]="Пливеш швидше й бережеш витривалість; після виходу на берег мокрий одяг швидше висихає.", ["swim_70"]="У воді рухаєшся ще швидше й можеш відновлювати витривалість, якщо завмреш серед хвиль.",
            ["woodcutting_35"]="Удар сокирою інколи розколює дерево й слідом розбиває його колоди та пеньок. Деревина лишається здобиччю на землі, а не нав’язується твоєму інвентарю.", ["woodcutting_70"]="Деревина займає більші стаки саме у твоєму інвентарі та важить менше. Звичайне дерево стає майже невагомим; скрині зберігають звичайні стаки.",
            ["swords_35"]="Спецатака меча стає швидшою, сильнішою й краще ламає рівновагу. Ворог, який уже похитнувся, отримує сильніші удари клинком.", ["swords_70"]="Почни атаку мечем перед самим ворожим ударом ближнього бою, щоб парирувати його без зупинки власного замаху. Продовжена атака сильніше ламає рівновагу.",
            ["axes_35"]="Сокира залишає зарубки, які підсилюють наступну різальну шкоду й готують ціль до страти. З досвідом спецатака стає швидшою.", ["axes_70"]="Зарубки інколи позначають жертву для КАТА: добий її спецатакою сокири. Страта повертає здоров’я й ненадовго посилює різальні удари, а потім потребує перепочинку.",
            ["clubs_35"]="Влуч булавою в ворога під час його атаки, щоб сильніше зламати рівновагу. Небезпечніший супротивник відчує важчий дзвін у голові.", ["clubs_70"]="Спецатака булави б’є по ворогах навколо й сильно ламає рівновагу. Уже приголомшені отримують критичний удар і відлітають під металевий дзвін.",
            ["knives_35"]="Навприсядки наведи ніж на ворога й натисни спецатаку: тінь перенесе тебе за його спину та доведе удар до цілі. Вбивство наближає наступний тіньовий крок.", ["knives_70"]="Влучання ножем інколи кличе тіньовий удар. Тінь може повторити удар або перейти на сусіднього ворога, продовжуючи полювання без твого нового замаху.",
            ["spears_35"]="Кинутий спис летить швидше, завдає більше шкоди й менше просідає.",
            ["spears_70"]="Влуч списом і натисни спецатаку з вільною рукою: малого ворога підтягнеш до себе, а до велетня рушиш сам. Перешкода дозволяє повторити спробу; блок повертає спис.",
            ["polearms_35"]="Парирування, що збило ворога з рівноваги, викликає швидкий круговий контрудар. Промах із парируванням без приголомшення не запускає відплату.", ["polearms_70"]="Утримуй спецатаку після початкового удару, щоб продовжити вихор. Кожен оберт пришвидшує й розширює його, витрачаючи витривалість; відпускання зупиняє крутіння.",
            ["bows_35"]="Натягуючи лук, відкриваєш золоту слабку зону на ворогові біля прицілу. Влуч у мітку, щоб стріла завдала сильнішого удару.", ["bows_70"]="Потримай повністю натягнутий лук до сигналу готовності: посилена стріла б’є сильніше, летить швидше й пробиває ворогів. Влучання у слабку зону береже її силу, а хвиля за ціллю відкидає наступних.",
            ["crossbows_35"]="Майстер тримає удар, поки готує важкий постріл; захист належить перезарядці, а не зброї в руках.", ["crossbows_70"]="Власний арбалет стає серцем облогової машини; спорожнілий магазин потребує рук господаря.",
            ["fists_35"]="Послідовні влучання руками чи кулачною зброєю прискорюють удари. Не барися: довга пауза без влучання скидає бойовий ритм.", ["fists_70"]="Кулачні удари по велетню або босу наповнюють його шкалу ЦУП. Коли вона повна, наведися на ціль і натисни спецатаку для важкої серії; без готової цілі залишається звичайний стусан.",
#if MASTERY_SHIELD35_EXPERIMENT
            ["blocking_35"]="Стріли й короткі вибухи справжніх снарядів не лякають майстра: точне парирування повертає їхню силу нападнику. Великий щит не грає в пінг-понг — міцний блок гасить приголомшення й ворожі чари. Хмари та небезпеки землі не відбиваються.",
#else
            ["blocking_35"]="Послідовні успішні блоки накопичують тиск. Точне парирування витрачає його на сильніше приголомшення нападника та приплив адреналіну.",
#endif
#if MASTERY_SHIELD_RUSH_EXPERIMENT
            ["blocking_70"]="Блокуй щитом і натисни ухил. Малий щит несе далі: зіткнення хвилею ламає рівновагу ворогів. Великий має коротший хід, але проходить крізь натовп і розкидає його вбік. Камінь і стіни досі зупиняють тебе; ривок не робить невразливим.",
#else
            ["blocking_70"]="Лише зі щитом: блок повертає частину шкоди, а точне парирування відсилає снаряд назад. Парирування ближнього удару інколи завдає відплати нападнику й ворогам поруч.",
#endif
            ["dodge_35"]="Коли ворожий удар минає тебе під час перекату, ухил прискорюється й ненадовго подовжує невразливість. Без уникненої атаки перк мовчить.", ["dodge_70"]="Перекат, що справді врятував від ворожого удару, повертає половину витрачених на нього сил. Дару потрібен короткий перепочинок. Світ цього разу спробував — та схибив.",
            ["elementalmagic_35"]="Вогняний посох накопичує жар: довше заряджання посилює полум’я, вибух і горіння. Крижаний посох залишає морозний слід, який ранить і сповільнює ворогів. Обморожена летюча здобич опускається до землі й залишається внизу, поки діє холод; боси не підкоряються.", ["elementalmagic_70"]="Вогняний посох прикликає Жарика — вогняного супутника, який б’ється на твоєму боці. Сильніший посох підсилює його; новий поклик повертає й лікує того самого живого Жарика. Крижаний посох здіймає бурю, що ранить і сковує ворогів; новий поклик переносить її.",
            ["bloodmagic_35"]="Кістяний посох збирає навколо тебе свиту скелетів. Посох захисту підтримує життя носія щита й дозволяє підживлювати ще цілі щити власною кров’ю. Кожна наступна секунда підживлення забирає більше здоров’я.", ["bloodmagic_70"]="Кістяний посох прикликає Торбу — живучого носія портальних речей, який уникає бою й відновлюється в безпеці. Посох захисту замикає ворога в кривавій клітці; коли пута лускають, сила щита карає бранця."
        };
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["pickaxes_70"] = "A pickaxe strike sometimes damages neighbouring pieces of the same vein. Rarely, a mighty strike crumbles the entire deposit with subterranean thunder.",
            ["dodge_70"] = "A roll that truly avoids an enemy's blow returns half the stamina it spent. The gift needs a short respite before it can answer again. The world tried. It missed."
        };

        internal static string Description(PerkDefinition perk)
        {
            if (perk == null) return "";
            bool ukrainian = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", System.StringComparison.OrdinalIgnoreCase);
#if MASTERY_RELEASE_SAFE
            if (perk.Id == "crossbows_70") return ukrainian ? "Вторинна атака з арбалетом — особиста платформа. Поклади свій арбалет і запас болтів у контейнер; порожні руки + вторинна атака — запуск / зупинка. Турель шукає ворожих істот у передньому секторі, без гравців і приручених. Забери ту саму зброю та решту болтів із зупиненого контейнера, потім прибери порожню платформу." : "Crossbow secondary deploys one personal platform. Supply your crossbow and a reserve of bolts through the container; empty hands + secondary starts / stops it. It searches the forward sector for hostile creatures, never players or tames. Retrieve the same weapon and remaining bolts from the stopped turret, then clear the empty platform.";
#endif
            if (perk.Id == "crossbows_35") return ukrainian ? "Під час активної перезарядки арбалета: +20 броні, +40% поточної броні та подвоєна межа оглушення. Після завершення або переривання захист зникає. Штатний внесок навички у шкоду збережено на 85%." : "While actively reloading a crossbow: +20 armor, +40% current armor and double stagger capacity. Protection ends when the reload ends or is interrupted. The native skill-derived damage contribution is retained at 85%.";
#if MASTERY_CLUBS70_EXPERIMENT
            if (perk.Id == "clubs_70") return ukrainian ? "Затисни блок із булавою чи молотом, накопичуючи заряд. Сильний удар булави отримує більше шкоди й відкидання, а добитий малий ворог летить швидше та завдає значно більше шкоди при зіткненні; ехо булави немає. Повний заряд молота залишає два слабші ехо з інтервалом 1,25 с." : "Hold block with a mace or hammer to gather a charge. A mace secondary gains damage and pushback, and a slain small foe flies faster and deals much greater collision damage; no mace echo. A full hammer charge leaves two weaker echoes at 1.25-second intervals.";
#endif
#if MASTERY_SHIELD_RUSH_EXPERIMENT
            if (perk.Id == "blocking_70") return ukrainian ? Ua[perk.Id] : "Block with a shield and dodge. A small shield travels farther and breaks enemy balance on impact; a tower shield travels less but cuts through crowds and scatters them sideways. Solid walls still stop you; the rush grants no invulnerability.";
#endif
#if MASTERY_CLUBS35_EXPERIMENT
            if (perk.Id == "clubs_35") return ukrainian ? "Силовий удар булави, що добиває малого ворога, жбурляє тіло в наступного супротивника. Удар молота залишає в землі слабше відлуння." : "A mace secondary that finishes a small foe hurls the body into the next enemy. A hammer strike leaves a weaker echo in the ground.";
#endif
            string mechanics = PerkLocalization.Localize(perk.DescriptionToken);
            if (Localization.instance == null ||
                !string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", System.StringComparison.OrdinalIgnoreCase))
            {
                if (En.TryGetValue(perk.Id, out string englishNarrative)) return englishNarrative;
                return mechanics;
            }
            if (!Ua.TryGetValue(perk.Id, out string narrative)) return mechanics;
            return narrative + (perk.Id == "crafting_70" && PotentialForgeRepair.Discovered(Player.m_localPlayer) ? PotentialForgeRepair.Secret : "");
        }
        internal static string SkillPassive(Player player, Skills.SkillType skill)
        {
            bool ukrainian = Localization.instance != null &&
                string.Equals(Localization.instance.GetSelectedLanguage(), "Ukrainian", System.StringComparison.OrdinalIgnoreCase);
            string lead = ukrainian && UaSkillLead.TryGetValue(skill, out string line)
                ? "<color=#B7A781>" + line + "</color>\n\n" : "";
            if (ukrainian && skill == Skills.SkillType.ElementalMagic)
                return lead + "<color=#B9C5CE>Бонус навички:</color> швидший базовий реген ейтру без зміни затримки його відновлення.\n\n";
            if (ukrainian && skill == Skills.SkillType.BloodMagic)
                return lead + "<color=#B9C5CE>Бонус навички:</color> додаткове лікування під час звичайного тіку регенерації.\n\n";
            if (skill != Skills.SkillType.Unarmed) return lead;
            if (!ukrainian)
                return "<color=#B9C5CE>Skill passives (no perk required):</color>\n" +
                    "Skill increases adrenaline gain and strengthens kick damage, stagger and knockback. Bare hands also gain damage and attack speed; fist weapons retain a restrained vanilla skill-derived damage bonus, not a penalty to total damage.\n\n";
            return lead + "<color=#B9C5CE>Бонуси навички (не потребують перка):</color>\n" +
                "Навичка підсилює отримання адреналіну, шкоду та ефекти стусана, а також шкоду й швидкість атак голими руками.\n" +
                "Кулачна зброя отримує послаблену частку ванільного бонусу навички; це стосується лише бонусу навички, не загальної шкоди.\n\n";
        }
    }
}
