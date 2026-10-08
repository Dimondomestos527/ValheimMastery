# Favor economy / Pantheon — дизайн 2026-10-05

Статус: DESIGN SUGGESTION, не схвалення нового балансу і не реалізація. Задача GOLD_CORE_FAVOR_ECONOMY_DESIGN_20261005. Поточна implementation authority1.4.123. Погоджені patron pools і cooldown contract збережені; source/runtime не змінювалися.

**Design thesis — GENERAL GAME DESIGN PRINCIPLE / DESIGN SUGGESTION.** Favor має оплачувати завершену корисну справу: небезпечну сутичку, відкриття, підтверджену виробничу партію. Не кожен удар, крок або предмет. Нормальний ігровий цикл повинен наповнювати шкалу обраного напряму; перетворення оптимізованого XP на Favor робить валюту залежною від множників і дешевих повторів. Ціна обмежує довгострокову частоту, погоджений КД — витрачання запасів та великих разових нагород.

## Evidence and confidence

- **FACT FROM CURRENT PROJECT:** перевірене джерело, не live gameplay evidence.
- **FACT FROM VALHEIM / DEVELOPER SOURCE:** у цьому звіті нових зовнішніх фактів цього класу не вводиться. Згадані native API/типи є фактами використання у project source; точних prefab/audio names не пропонується.
- **GENERAL GAME DESIGN PRINCIPLE:** аргумент дизайну, не виміряний факт.
- **DESIGN SUGGESTION:** кандидатне правило/число, потребує рішення користувача.
- **TECHNICAL ASSUMPTION:** необхідна, але ще не доведена можливість або параметр моделі.

| Source evidence — FACT FROM CURRENT PROJECT | Що встановлено | Обмеження для дизайну |
| --- | --- | --- |
| GoldCraftingService.cs:14,102–125; GoldCraftingLedger.cs:60–66,139–179 | MaxFavor1000, один scalar Favor на player ID у worldUID-файлі; онлайн-гравцям зменшується exhaustion за Time.time; flush15sec | Немає реального другого patron wallet; це не offline UTC cooldown |
| GoldCooldownPolicy.cs:13–33; PANTHEON_CORE.md | <=250=>0; >250<=500=>300; >500=>1200; A блокує всі A та інші B>250 | Старі clauses original brief не застосовуються |
| GoldCraftingLedger.cs:263–298; GoldDivineTransactions.cs:50,391–434 | Reserve/Commit/settled receipts; character receipt key містить worldUID; pending price визначає debit | Поточний action wire crafting-shaped, без PatronId; generic transaction для інших powers не доведена |
| GoldFavorModel.cs:9–15,69–86 | Provisional0.5Favor/XP, charge-time x4; DR1/.5/.2 за однаковим source у300sec; effective event max25 при defaults | Сьогоднішня формула min(100,validatedXP*d*.5)/4; не загальна шкала винагород |
| GoldFavorService.cs:89–159; GoldCraftingService.cs:268–271 | Crafting100, supported craft/upgrade/build keys, canonical base/final XP checks, actor/auth/Gold gates | Не combat/exploration reward protocol і не durable completion proof кожної craft події |
| GoldCraftingLedger.cs:146–157,204–247 | Ordinary Gain dirty; processing award з replay bitmap і durable save, max300 на3600sec window | Не всі прирости синхронно durable. Background window anchored від першого award, не точне rolling60min |
| GoldFavorService.cs:290–356 | Processing proof від station owner; input author, sequence, Workshop coverage та unlock validation | Producer attribution є; universal support/offline durable delivery не доведені |
| ExperienceContext.cs:25–33,62–86,149–155 | XP modifiers/context і character-local first-unique skill/item/target key | FinalXp не незалежна економічна одиниця; first-unique key не world discovery receipt |
| LegacyCatchUpService.cs:49–112; WorldProgressionXpService.cs:26–101 | World-scoped catch-up evaluation; world boss defeat flags впливають на XP | Catch-up не Favor accomplishment; global defeated flag не особиста участь у босі |
| ProcessingStationProgression.cs:62–77,108–164; ProcessingPersistentBatches.cs:18–53 | Accepted input author збережений у batch; бонусний output не повторює XP; XP виробнику, не collector | Offline queue/durable Favor для всіх професій відсутня; один batch не доводить усі нові source contracts |
| CookingCraftingProgression.cs:169–214,235–242 | Producer і tapper різні ролі; output stamping використовує author | Favor за tap/bonus roll створюватиме хибне авторство або feedback loop |
| CraftingBuildReuseService.cs:31–49,86–116; CraftingBuildReuseModel.cs:13–39 | Demolition/replacement debt,300sec; server mirror transient | Не достатній захист repeatable Favor за будівництво/демонтаж |
| MasteryEventsExtended.cs:11–26; MasteryEventDispatchPatches.cs:17–43; PerkRuntimeServices.cs:9–83 | Kill/hit/XP events та generated-hit context/tags | Немає durable encounter/participant reward ledger; local generated flag не гарантує full network provenance |
| PerkCatalog.cs:31–79; bounded current100 search; PerkRuntimeServices.cs:178–219 | Каталог24 skills; Combat/Magic100 dedicated paths не знайдено; Pickaxes100 weight discount є | Каталог не ability contract; passive Pickaxes100 не платна Favor action |
| GoldDivineTransactions.cs:180–240,391–434; GoldMasterworkButton.cs:59–79 | Crafting100: dedicated q5 creation500; Forge+3/idol250 |750-cost powers у моделі — design input, не встановлена current implementation |
| GoldPantheonUi.cs:25–74,223–319,370–409; PerkProcHudService.cs:7–60; MasteryRavenTutorials.cs:25–83 | Contextual Völundr card, synthetic Tyr test card, native transient HUD/tutorial composition | Fixture не другий wallet; many-patron scrolling/controller UX не доведені |

Read-only scouts: favor_attribution_scout та favor_100_ux_scout; root відповідає за висновки. Main/source fingerprints у validation/gold-favor-economy-design-20261005/source-before.csv і source-after.csv. Static/model evidence не означає LIVE VERIFIED.

## FAVOR SCALE

**DESIGN SUGGESTION:** зберегти hard cap1000 на покровителя. Soft cap не потрібен.1000 вміщує750 та запас250, але не дві major actions по750. Запас250 після major того самого покровителя витрачається лише після його КД; це не негайне combo. Інший покровитель має власний баланс і може використовувати <=250 під чужим КД.

**DESIGN SUGGESTION / TECHNICAL ASSUMPTION:** ціль400–600 Favor/год до активно розвиненого patron pool за змістовний звичайний loop;450 — контрольний сценарій,600 — швидкий. Це tuning target, не виміряний current income. На нові discovery spikes не можна спирати постійну швидкість endgame. Відсутній контент/багато переїздів можуть знизити repeatable rate до300.

| Ціна | Час з нуля при450/год | При600/год | Власний КД — погоджений FACT FROM CURRENT PROJECT |
| --- | --- | --- | --- |
|100 |13.33хв |10хв |0 |
|250 |33.33хв |25хв |0 |
|500 |66.67хв |50хв |5хв |
|750 |100хв |75хв |20хв |

**GENERAL GAME DESIGN PRINCIPLE:** поєднання750 ціни та20-хв КД не означає750-power кожні20хв. Для такого сталого темпу потрібно2250Favor/год. Такий дохід одночасно оплачує22.5 дії по100 або9 по250 за годину. Якщо конкретний patron має тільки750-перк, він буде звертатися до нього приблизно раз на75–100хв при цих rates. Це реальний tradeoff, не питання красивої шкали.

**DESIGN SUGGESTION:** невеликі повідомлення про приріст — aggregated contextual, а не кожен raw event. Під час активної пригоди гравець бачить змістовні прирости приблизно раз на2–5хв, якщо виконано qualifying accomplishments. Не вводити tick-income, щоденні завдання чи таймер, який сам додає Favor.

**DESIGN SUGGESTION:** cap обрізає надлишок без прихованого банку.999+10=>1000, award1, overflow9. Подія/first claim вважається використаною навіть при0 actual gain; overflow не переноситься іншому patron. Не накопичувати ваучери поза шкалою.

**FACT FROM CURRENT PROJECT / DESIGN SUGGESTION:** зберегти чинний world/player scope балансу між сесіями; не вводити перенесення Favor між worlds/characters. Тимчасовий display, UI focus та локальний cache не є балансом. Scope first-bonus history, запропонований нижче, потребує окремого architecture review і не змінює current persistence цим документом.

## FAVOR SOURCES

Усі amounts/ranges та очікувані rates нижче — **DESIGN SUGGESTION**. Категорії описують завершені справи, не hook-per-hit. Expected/hour — умовний внесок у session; рядки не можна складати без реального часу та attribution. Нагороди різних джерел маршрутизуються, а не копіюються у всі patron pools.

| Source | Favor amount/range | Repeatability / орієнтир за годину | Anti-farm | Solo | Co-op | Why it exists |
| --- | --- | --- | --- | --- | --- | --- |
| Звичайна релевантна сутичка |20 |6–12=>120–240 |Лише реальна загроза; один encounter, не kill counter |Повна eligible винагорода |Кожному meaningful participant |Повсякденний бойовий loop |
| Складна сутичка/кілька загроз |40 |3–6=>120–240 |Не окрема виплата за кожного моба; штучний spawn-loop diminish |Повна |Особиста participation eligibility |Оплачує складність, а не число ударів |
| Рідкісна/elite загроза |40–60 |0–2=>0–120 |Curated risk band; event cap60 |Повна |Без last-hit competition |Небезпечні зустрічі |
| Зірки/вища загроза |Включено в20/40/60, не друга нагорода |Не самостійний source |Star не перетворює trivial enemy на високий bounty; зняття gear не підвищує tier |Одна нагорода |Те саме |Уточнює challenge classification |
| Бос, звичайна частина |Ранній30, середній40, пізній50 |За тим самим boss type у60хв:1,.5,потім0 |Persistent repeat history; summon cost не заміна anti-farm |Особиста eligibility |Кожному eligible, без party multiplier |Боси мають divine значення, але не безкінечний ATM |
| Перший особистий boss type |+200; разом230–250 |Одноразово |Без retroactive payout із global defeat flags |Лише власна участь |Окремий first claim кожного |Велике просування |
| Перший новий biome type |150 |Одноразово |Real discovery, не перетин кордону повторно |Одному discoverer |Кожному, хто справді відкрив |Експедиції |
| Перший dungeon/важливий location class |100 |Одноразово |Не за кожен reload/new seed |Персонально |Фактичні explorers |Помітна нова знахідка |
| Нове змістовне site completion |20–25 |Напр.12x25=>300, поки є нові sites |Stable world/site identity; visited site0; reward completion, не ping координати |Повна |Лише explorers/participants |Повторювана exploration без ходіння між двома точками |
| Цінна активна виробнича партія |20–40, винятково до60 |Напр.10x30=>300 |Net consumed inputs/valuable outcome; no recycle/refund/Gold-created award |Автор |За внесками авторів, не collector |Crafting/Cooking/Farming loop |
| Підтверджена processing/harvest партія |20–40 |Напр.3x40=>120; passive ceiling300/player/60min |Finite authored input/cycle; once, не output pickup; спільний earning ceiling, не wallet |Автор |Розподіл input authors |Професійні цикли без mob requirement |
| Перший technology/resource/recipe milestone |50–100 |Одноразово |Лише власне нове досягнення; не новий skill key того самого результату |Особисто |Окремо за реальним вкладом |Технологічний прогрес |
| Новий Gold patron recognition |250 |Один раз на character/patron, умовний кандидат |Без re-unlock/world-hop/rollback replay; до доведення durable claim не є готовим source |Особисто |Особисто |Першу Gold-дію не відкладати на годину |
| Low HP, hazard, weather, відстань без accomplishment |0 |Ніколи |Немає self-damage/standing-in-fire reward |0 |0 |Не винагороджуємо штучну небезпеку |
| Idle, кроки, parry/dodge count, build/remove loop, bonus outputs |0 |Ніколи як окремий source |Без окремої виплати за proc/repeat/place |0 |0 |Прибирає дешеві валютні ферми |

**TECHNICAL ASSUMPTION:** danger bands та valuable batch thresholds треба визначити на фактичних approved action/resource contracts. Існуючі tier tables не одна універсальна шкала: gathering, items і targets мають різні ranges/fallbacks. Simulation задана кількістю qualifying events; source поки не доводить, що гравець реально завершує їх у такому темпі.

**DESIGN SUGGESTION — risk/survival:** довга експедиція сама по собі не оплачується за хвилини. Повернення з цінним вантажем може завершувати вже приписану ресурсну справу, але не давати новий bounty за перенесення старих речей між скринями, повторне вивантаження чи teleport. Відсутність Rested, погода, голод і lowHP не є reward triggers; справжня небезпека лише уточнює вже валідовану сутичку/відкриття. Інакше оптимальною грою стає навмисне погіршення власного стану.

## FAVOR FORMULA / RULES

**DESIGN SUGGESTION:** не конвертувати FinalXp. Для завершеної справи:

`R = B × d + I`, де B — зрозуміла нагорода категорії, d — лише потрібне anti-repeat зниження, I — одноразовий bonus. Для звичайної чесної сутички/цінної партії d=1; не штрафувати просто за те, що кулінар готує той самий потрібний продукт із нових витрачених ресурсів. Повторні bosses мають явно описані1/.5/0 у rolling60min; refundable/recycled/artificial loops мають0, а не дешевий unlimited reward.

`ΔF_p = min(1000 − F_p, q_p × R)`; `Σq_p ≤ 1` для однієї особистої винагороди. Сумарно до всіх pools цього гравця не потрапляє більшеR. Не перенаправляти overflow у peer pool.

**DESIGN SUGGESTION — routing:**

1. Skill-specific accomplishment іде через explicit skill→patron mapping. Усі linked skills наповнюють один pool. Skill-level perk eligibility для витрати лишається дією відповідного owner.
2. Якщо один encounter містить внески кількох mapped skills/patrons, особистеR ділиться за валідованою contribution attribution; не виплачуєтьсяR кожному skill. Шкала показує ці частки, а не дубльований приріст.
3. Нейтральне boss/discovery accomplishment іде одному honored patron, обраному до encounter/discovery. Якщо його не вибрано, default — явно показаний останній valid patron. Зміна UI вибору після початку не змінює одержувача цього event.
4. Processing patron/author визначені accepted input, а не моментом pickup. Зміна mapping/focus до завершення не змінює вже приписану партію.
5. Не виділяти другий bounty за той самий outcome через kill+XP+firstUnique+support hooks. First bonus додається до одногоR.

**DESIGN SUGGESTION:** patron, відкритий хоча б однією100 skill, може отримувати qualifying contribution усіх його linked skills; користуватися конкретною ability можна лише за її власною eligibility. **FACT FROM CURRENT PROJECT:** нинішній Crafting Favor service вимагає Crafting100/HasGold; запропоноване ширше правило не є вже реалізованим фактом.

**FACT FROM CURRENT PROJECT / TECHNICAL ASSUMPTION:** clock моделі відповідає server game seconds, онлайн, game speed1, без pause. КД не зменшується під час logout. Нові repeat windows пропонуються durable server UTC rolling60min без reset при reconnect; це інший годинник, не час КД. Income може приходити під активним КД; Favor не відновлюється сам із плином часу.

## FIRST-TIME / MAJOR REWARDS

**DESIGN SUGGESTION:**250 recognition,150 biome type,100 location class,50–100 technology milestone та+200 first boss — рідкі spikes. Їх не включати у сталий повторюваний дохід. Не виплачувати всі старі досягнення, коли гравець досяг100, і не використовувати XP catch-up як Favor source.

**DESIGN SUGGESTION / TECHNICAL ASSUMPTION:** recognition та перші biome/boss/technology-type claims пропонуються once per character/type (patron лише для recognition), незалежно від new-world seed, щоб world hopping не відновлював bonus. Site completion має world-specific identity. Це потребує architecture review для durable claim authority/rollback consistency; наявні character customData first/Seen keys не є готовим server-authoritative Favor claim ledger. Баланси лишаються world-scoped. Без підтвердження цієї сумісності разові spikes — conditional design, не готова економічна гарантія.

**GENERAL GAME DESIGN PRINCIPLE:** новий100 гравець міг уже відкрити всі біоми й перемогти босів. Планувати його pacing на гарантованих нових firsts — помилка. Нижче є окремий сценарій лише recognition250, без discovery/boss припущень.

## PROFESSION FAVOR

**DESIGN SUGGESTION:** active professions мають цільову економіку того ж порядку400–600/год при достатньому реальному обсязі корисної роботи. Production inputs, raw harvesting і processing не повинні кожні окремо виплачувати повну ціну одного продукту; винагорода за кожну стадію відповідає лише її доданій корисній роботі. Не будувати систему оцінки кожного предмета за всесвітньою ціною золота.

Партія — підтверджений істотний завершений обсяг, не кожна морквина/злиток. Її nominal20–40 залежить від approved recipe/resource/work classification; у числовому прикладі десять активних партій по30 та три processing по40. Це explicit workload assumption, не виміряний native recipe throughput.

**DESIGN SUGGESTION:** пасивні processing/harvest awards мають загальну межу300 на player за rolling60min, спільну для їхніх routed patron shares. Це anti-AFK earning bound, НЕ спільний Favor wallet. Active verified crafting не має штучного daily/hourly quota. Поточна300 background window — precedent, але запропонований rolling/global-multi-source обсяг не current implementation.

**DESIGN SUGGESTION:** producer отримує свою винагороду; collector/tapper/станція-owner не підміняють producer. За collaborative batch ділиться одне personal/batch reward entitlement за фактичними accepted inputs, без множення на кількість помічників. Повністю offline completion не породжує queued Favor або offline income у рекомендованій початковій моделі; продукція має власні gameplay semantics. Online автор може отримати finite reward за реально вкладені ним inputs, навіть якщо у мить завершення не стоїть біля pickup. Це відрізняється від AFK глядача без внеску.

**DESIGN SUGGESTION:** Gold-created item, bonus RNG output, refunded placement і наступне перетворення того самого бонусного output не дають self-recharge. Exceptional outcome можна красиво показати, але не множити Favor за proc. Не вводити repeatable Favor за piece placement: чинний demolition debt не закриває restart/expiry/rollback. Substantial first technology unlock можна оцінити; суб'єктивну «красиву базу» автоматично не оплачувати.

## CO-OP ATTRIBUTION

**DESIGN SUGGESTION:** boss/encounter completion дає кожному eligible participant власнеR без last-hit bonus і без party-size multiplier. Чотири legitimate participants можуть отримати по250 за власний first late-boss, а не ділити250 на чотирьох. Це1000 сумарно команді, але250 кожному, не1000 кожному і не250 усім його patrons.

Eligibility повинна поєднувати реальний вклад та участь у місці/часі encounter. Candidate damage threshold —5% effective encounter damage; альтернативно підтверджений useful support для eligible fighter. Немає reward за proximity, overheal, self-inflicted wounds або натискання buff в порожнечу. Для довгої сутички потрібен sustained вклад, наприклад дві корисні support події з проміжком10sec і недавня реальна активність до completion; короткий encounter не карається за неможливість набрати10sec. Legitimate death у сутичці не повинна автоматично забирати earned participation.

**TECHNICAL ASSUMPTION:**5%/10sec та support equivalence — candidate eligibility tuning, не established network capabilities. Сьогоднішні Killer/Hit/XP events не дають усіх даних. Лише activity/presence без contribution proof недостатні. Якщо proof відсутній, не видавати blind reward всьому peer list.

**DESIGN SUGGESTION:** generated hit не дає окремого Favor. Він може завершити encounter, де root player уже має legitimate contribution; це не причина скасувати чесну нагороду. Reward один за завершення, а provenance служить attribution original actor. Low-tier summon/controlled friendly targets/самопошкодження не qualifying hostiles.

## ANTI-EXPLOIT

Усі правила цього розділу — **DESIGN SUGGESTION**, якщо не позначено інакше.

- Платити тільки validated completion з once-per-recipient entitlement, authoritative world/actor/patron association; не client-claimed Favor чи переліком XP strings.
- Dedup має охоплювати різні hooks, retries, generated hits, owner transfer, reconnect та restart. Наявність source key або firstUnique customData цього сама не доводить.
- Greyling/тривіальний content нижче earned progression band дає0. Equip downgrade/self-damage не роблять його небезпечнішим. Current gear snapshot не є єдиним danger metric.
- Natural difficult encounters мають capped20/40/60 bounty за outcome, не за групу secondary hit events. Artificial fixed spawn/arena loops вимагають targeted diminishing за origin; якщо origin не доведений, не називати цю ферму закритою.
- Boss same-type repeats: base30/40/50, потім половина, далі0 у rolling60min; first+200 лише один раз за тип. Різні реально пройдені bosses — різні accomplishments, не exploit за самим фактом повтору гри.
- Не штрафувати однаковий корисний рецепт з новими реально витраченими матеріалами. Штрафувати refundable/recycled/generated production та безособові pickup loops.
- Passive cap300 діє перед routing across patrons, тож відкриття нових patrons не множить цей earning budget.
- Failed/cancelled Gold action не створює Favor нагороду або новий КД. Receipt replay не виконує gameplay effect повторно і не заряджає timer. Uncertain result не можна «просто refund» без узгодження durable outcome.
- На cap entitlement споживається, overflow зникає. Немає переносного hidden claim bank або reward reallocating між patron pools.
- Disconnect не очищає DR/first claims/pending/КД; death не award/reset. Imported character/save rollback/world hopping залишаються явними threat cases, а не вирішуються лише написом worldUID.

**FACT FROM CURRENT PROJECT:** ordinary craft Gain dirty/flush15sec, session-transient diminishing та вузька receipt схема означають, що exactly-once future economy не доведена існуючим XP hook. Не рекламувати весь proposed anti-exploit як «у нас уже є».

**DESIGN SUGGESTION / TECHNICAL ASSUMPTION — settings:** вимкнення Gold або конкретної дії не є способом reset/refund/перекидання wallet. UI явно показує недоступність; збережені balance/pending/outcome не зникають. Поточний global Crafting enable gate не є вже реалізованими independent patron settings. Взаємодія toggling settings із pending recovery і timer catch-up потребує окремої перевірки, без припущення про безкоштовне завершення КД через reconnect чи повторне ввімкнення.

## FAVOR GAUGE UX

Усі UX правила — **DESIGN SUGGESTION**; native UI composition feasibility — **TECHNICAL ASSUMPTION**, current precedent наведено в evidence table.

Основний вигляд — contextual Pantheon, одна компактна картка на patron з `Favor / 1000`. На HUD тимчасово показувати лише patron/action, з яким зараз працює гравець: при виборі ability, прирості, витраті або відмові. Постійні три–шість MMO bars не потрібні. При багатьох patrons потрібен обмежений список/scroll та controller navigation; fixture двох карток не доводить масштабування.

Continuous fill легше читати за десятки сегментів. На картці selected ability показувати один marker її вартості; додатково subtle750 marker, лише якщо цей patron справді має таку available action. Не ставити250/500/750 на всі шкали автоматично. Readiness від authoritative balance/eligibility/timer, не rounded animated number.

Стани повинні бути різні: «Готово», «Бракує120 Favor», «Виснаження Völundr:03:20», «Цю дію блокує виснаження іншого покровителя», «Чекаємо підтвердження», «Дія вимкнена/недоступна». Не замінювати все одним сірим disabled button.

Під активним КД A усі його action buttons locked; у B cheap<=250 buttons лишаються eligible за іншими правилами, >250 locked. B gauge може наповнюватися і відображатися активною — foreign КД не означає blocked income чи blocked whole patron.

Small gains агрегувати2–3sec в одну коротку підказку з назвою patron і фактичним credited amount; large milestone має окрему коротку cue. Source cap loss не анімувати як отримані гроші. На full одна subtle gold rim і1000/1000, без нескінченних «FULL!» повідомлень.

Spend: при pending позначити резерв/очікування, authoritative committed balance оновлює fill; reject знімає pending, не грає success cue. Не демонструвати остаточну divine перемогу до durable outcome. Підказка нехватки показує exact deficit та0/5/20min own cooldown перед викликом, не HUD calculator.

**FACT FROM CURRENT PROJECT:** current masterwork copy ще пов'язане з inspiration, хоча існує dedicated crafting button. **DESIGN SUGGESTION:** опис має вести creation до цієї кнопки; Forge arming пояснюється окремо. Source UI в цій задачі не редагується.

## VFX/SFX INTENT

Усі choices — **DESIGN SUGGESTION**. Лише наявні vanilla resources або Unity-native composition; exact asset identities не вигадуються і не затверджуються тут.

| Подія | Намір presentation |
| --- | --- |
|Small gain |Тихий короткий glow шкали, один aggregated +amount; без світових частинок за кожен mob |
|Large gain |Помітніший pulse patron card і короткий акцент; показати причину, не нову combat animation |
|Cap |Один м'який accent transition, сталий subtle rim; no repeating sound |
|Spend |Fill відходить на committed amount, короткий підтверджувальний cue після authoritative success; gameplay VFX належить ability owner |
|Insufficient |Короткий neutral denial і точна нехватка; не success flash і не нескінченний warning |
|First Gold unlock |Існуюча ceremony/tutorial semantics; пояснити patron/shared pool/ціни/КД; recognition reward cue лише якщо bonus реально committed |

Accessibility/settings можуть прибрати cosmetic cues, але не state number/reason/eligibility. Remote observers не повинні чути всі особисті small-gain звуки. Major ability world effects не перепроєктовуються цією економікою.

## EXAMPLE PLAYER SESSION

**TECHNICAL ASSUMPTION:** це inputs моделі, не sampled live throughput. Start0 у зазначених pools, cap1000, без spend у перших п'яти рядках. Rates вже після anti-farm/eligibility. Кожен counted event незалежно qualified, без дублювання kill/XP і без повторення first claims. Тривалість60 online game minutes; boosts XP/catch-up не застосовуються до Favor.

| Стиль / routing | Явні нарахування за1год | Баланс наприкінці |
| --- | --- | --- |
|Combat solo, один eligible skill patron A |6×20+4×40+2×60+2 neutral sites×25=450; усеA |A450 |
|Active profession, patron V |10 active batches×30+3processing batches×40=420 |V420 |
|Explorer, honored A |12new sites×25+first biome150+first location class100=550 |A550; повторення уже відвіданих sites не зберігає цей rate |
|4-player co-op, кожен eligible, свійA |12×20+6×40+2×60=600 кожному;20encounters/h — faster-party assumption |600 кожному,2400команді; AFK observer0 |
|Mixed skills/patrons |Combat6×20+2×40=200A; neutral2×25=50A; profession5×30+1valuable50=200V |A250,V200; НЕ450 кожному |
|Новий Gold без нових firsts |Regular420+conditional recognition250=670 |V670 без750 spend |
|Новий Gold, оптимістичні нові firsts |Regular420; recognition250t0; biome150t20; lateboss250t40;750actiont40 |320; income1070−spend750, overflow0 |

Останній рядок — generous conditional scenario: regular420 залишається попри час експедиції/боса. Це не гарантована нормальна година і не приклад реалізованої750 ability. Без recognition/spikes current zero-start420 дав би лише420. При no-spend у optimistic hour income1070 обрізався б до1000; своєчасний spend змінює overflow.

## ECONOMY SIMULATION

**TECHNICAL ASSUMPTION:**750-action доступна як параметр, не факт current Combat/Magic implementation. Гравець використовує тільки її, негайно за першої дозволеної можливості; resources/targets/casting constraints не обмежують доступ — числа є можливостями фінансування, не guaranteed real casts. Session без death/pause/logout, game time speed1, initial timer0; income продовжується під КД. Cap1000 кожного patron, hard overflow discarded. Spend у0<=t<T; нарахування до endpointT враховані. Баланси й gains задані у Favor, t у хвилинах.

Для одного patron без spikes: `time_to_funds=max(0,(750−F)*60/r)`, earliest cast=`max(current_time+time_to_funds,cooldown_ready)`. Між подіями credit=`r*Δt/60`, баланс обрізається cap1000; після cast debit750 і ready=t+20. Spikes застосовуються у заданий момент до наступної admission; повторний paid event не існує.

| Repeatable per-patron r | Початок | Major за2год | Major за3год | Часи cast до180хв | Balance2h /3h |
| --- | --- | --- | --- | --- | --- |
|300/h |0 |0 |1 |150 |600 /150 |
|420/h |0 |1 |1 |107.14 |90 /510 |
|450/h |0 |1 |1 |100 |150 /600 |
|600/h |0 |1 |2 |75,150 |450 /300 |
|300/h |1000 |2 |2 |0,100 |100 /400 |
|420/h |1000 |2 |3 |0,71.43,178.57 |340 /10 |
|450/h |1000 |2 |3 |0,66.67,166.67 |400 /100 |
|600/h |1000 |2 |3 |0,50,125 |700 /550 |

**GENERAL GAME DESIGN PRINCIPLE:** «2–3 major за вечір» правдоподібно зі стартовим запасом або швидким meaningful loop, але не однакова гарантія для порожнього ledger. При450/h сталий темп0.6major/h; при600/h0.8/h.20min cooldown має теоретичний ceiling3/h, але ресурс нижчий. First events не повинні маскувати цей steady-state.

| Додатковий параметричний сценарій |2h |3h | Cast times | Balance2h /3h |
| --- | --- | --- | --- | --- |
|450/h,start0, один lateboss250t60 |1 |2 |66.67,166.67 |400 /100 |
|420/h,start0, лише recognition250t0 |1 |2 |71.43,178.57 |340 /10 |
|420/h,start0, recognition250t0+biome150t20+lateboss250t40 |1 |2 |40,121.43 |740 /410 |

**TECHNICAL ASSUMPTION — дві шкали:** total income450/h, routing A50%/B50%, тобто225/h кожному; це не450/h кожному. Only750 greedy casts, tie A first; global foreign>250 block застосовується до будь-якого active A/B timer.

| Initial A/B |2h casts A/B |3h casts A/B |Balances2h A/B |Balances3h A/B |Overflow |
| --- | --- | --- | --- | --- | --- |
|0/0 |0/0 |0/0 |450/450 |675/675 |0 |
|1000/1000 |1/1 |2/2 |700/625 |175/100 |B75 |

У full випадку casts A0,B20,A133.33,B153.33. B стоїть на cap перші20min під A cooldown, тому втрачає75. У zero випадку перші750 доступні лишеt200; shared sum1350 за3h не можна витратити як один combined wallet. Fragmentation — реальний ризик цього погодженого multi-patron design. Honored patron спрямовує neutral events до потрібного pool; skill-specific gains не перетягуються довільно.

**TECHNICAL ASSUMPTION — cheap actions:** зF1000 і450/h за1год можна профінансувати14дій по100 (end50) або5дій по250(end200), якщо немає активного КД цього patron/іншого blocking rule й action-specific constraints дозволяють. Це includes stockpile burst: не14/h steady income; сталі4.5/h і1.8/h. Не вводити прихований shared cooldown<=250 задля маскування цієї арифметики.

**FACT FROM CURRENT PROJECT / DESIGN SUGGESTION — A/B matrix:** при active A20min абоA5min A0/A250/A500 blocked; B0/B250 eligible за funds/own action checks, B250.01/B500/B750 blocked. B cheap spend залишає A countdown без reset/extension. Це той самий прийнятий контракт, не major-only blocking.

CSV з inputs/results/cast timestamps: validation/gold-favor-economy-design-20261005/one-hour-inputs.csv, single-patron-summary.csv, single-patron-casts.csv, two-patrons-summary.csv, two-patrons-casts.csv, cap-cheap-cases.csv. Перевірено conservation start+income−overflow−spend=end та мінімальний20min gap. Це arithmetic/model checks, не запуск gameplay або новий код проєкту.

## ECONOMY STRESS TEST — 14 cases

Expected policy outcomes — **DESIGN SUGGESTION**; числові cases — **TECHNICAL ASSUMPTION**, source limitations позначені прямо.

| # | Сценарій | Очікування / висновок |
| --- | --- | --- |
|1 |New level100 |Current Unlock не видає250. Conditional recognition250 дає першуcheap дію відразу; при420/h750 на71.43min без нових firsts. Не припускати незнайдені біоми/босів у кожного нового100 |
|2 |Endgame solo |Без first spikes450/h: zero1major/2–3h; full2/2h,3/3h. Неточний rate прямо змінює доступність; потрібна live calibration |
|3 |4-player co-op |Кожен реальний participant має personal bounty, не quarter share і не×4. Faster600/h — workload assumption; сумарні4×600 не потрапляють одному гравцю |
|4 |Boss farming |Уже claimed lateboss:50+25+0...=75 заsame type/rolling60min. First adds200 лише раз. Summoning/material cost не запобігає repeat exploit сам по собі |
|5 |Greyling/low-tier farm |Trivial below earned progression band0;400kills=>0. Stars/роздягання не обходять це правило; unknown threat класифікувати консервативно |
|6 |Base profession farm |Build/remove/bonus/pickup0. Реальна нова valuable production eligible; input finite, attribution once, passive total<=300/hour перед routing. Legitimate supply work не прирівнюється до exploit |
|7 |AFK teammate |Proximity alone0boss/combat. Online author finite completed inputs може отримати власний processing bounty; це не reward AFK spectator за працю команди |
|8 |At cap |999+10=>1000,+1actual,9lost.1000+450 безspend=>1000,450lost. First claim не накопичується як невидимий ticket; без spamFULL |
|9 |Repeated cheap<=250 |Безown cooldown нові valid actions допускаються; repeated requests idempotent і pending serial.450/h sustaining4.5×100 або1.8×250, не infinite. Active own timer блокує навітьcheap |
|10 |Saving750 |450/h з0=>100min;600/h=>75min. Диверсифікація225/225=>200min кожному; суму pools не об'єднувати. Не обіцятиmajor щогодини всім |
|11 |After750 from1000 |Залишок250; за20min при450/h+150=>400. КД завершився, але750 funds ще немає; наступнийmajort66.67. B<=250 може діяти у цей час зB balance |
|12 |Multiple patrons |A-specific і neutral rewards routing frozen; no blanket broadcast. КДsource показаний, B cheap eligible; all linked skills одногоpatron без skill sub-wallets |
|13 |Moving worlds |Current worldUID ledgers не переносять Favor автоматично; pending receipts world-bound. A timer не витікає офлайн. Global first dedup — нова умовна семантика з architecture review; output item transfer/rollback loopholes не вважати закритими лише world ledger |
|14 |Death/reconnect/restart |Не обнулювати/нагороджувати Favor заdeath. Skill eligibility може впасти, balance лишається. No offline cooldown reduction. Persisted pending/receipt outcome визначає settle/refund; не повторювати first rewards/DR. Поточний15sec dirty-flush crash window ще live-test case |

## RISKS

**GENERAL GAME DESIGN PRINCIPLE / DESIGN SUGGESTION:** найбільший balance ризик — багаті screenshots і slow empty progression. Не порівнювати full-ledger player із new100 і не рахувати одноразові firsts як repeatable rate. Якщо patron має лише750 action і очікується часто вживана divine power,450/h недостатньо для цього очікування; підняття rate змінитьcheap pacing, а не тількиmajor.

Multi-patron fragmentation може зробити нормальну mixed session слабшою за single-patron. Не лікувати її тайним shared wallet або multipayout; це порушує погоджений контракт. Focus нейтральних accomplishments допомагає, але не прибирає витрати розподіленого income.

**TECHNICAL ASSUMPTION:** meaningful profession value, support attribution, durable encounter/discovery IDs, generated provenance across peers і lifetime first authority ще не підтверджені universal source paths. Глобальні first claims та rollback-safe history потребують architecture review; стверджувати «anti-farming завершений» перед цим було б неправдою.

**FACT FROM CURRENT PROJECT:** XP/world/catch-up modifiers, source LRU/session resets, build-debt expiry і anchored background window не повна future economy. Не переносити їхні гарантії в нові rewards автоматично. Two-player authority, native character/server save order, world switches, settings та crash windows лишаються live verification gaps.

**DESIGN SUGGESTION:** при недостатній proof не додавати reward, який на вигляд чесний, але допускає повторні claims. Не робити підтримку групи лише proximity. Не брати current actor gear за єдиний risk metric. Не фінансувати expensive manufacturingFavor без фактично витрачених ресурсів.

## RECOMMENDED FINAL MODEL

Усі нові balance/UX/attribution правила нижче — **DESIGN SUGGESTION**, не затверджена зміна.

1. Hard1000 на patron/player у чинному world scope; no soft cap, no hidden overflow bank.
2.400–600/h до активно розвиненого patron через meaningful outcomes;450 reference, passive processing/harvest<=300/player/rolling60min із збереженим per-patron routing. Не доходи за ticks/XP/catch-up.
3. Hybrid20/40/60 repeatable encounters і20–40 production;100–250 рідкі first spikes. Recognition250 — conditional once-per-character/patron кандидат, не current unlock grant.
4. Skill rewards своєму patron; neutral achievements одному заздалегідь обраному; multi-skill outcomes ділять одне personalR. All linked skills share pool, no per-skill ledger.
5. Complete participation awards уco-op без last-hit/party multiplier/AFK proximity; producer, не collector, володіє profession claim.
6. Погоджений0/5/20min cost contract та A-all/B>250 blocking збережений дослівно. Cheap foreign action не змінює чужий timer; gains можуть приходити підКД.
7. Contextual patron card/action gauge, authoritative readiness, pending/deficit/source cooldown states, batch notifications; vanilla/native presentation intent.
8. First-bonus authority, valid event identity та proof gates залишаються architecture/design dependencies, не мовчазна persistence migration. Конкретні750 abilities не перепроєктовуються.

Дизайн сформований, але не реалізаційний план. Наступне рішення по дизайну — чи прийнятний resource pacing750 раз на75–100min без запасу, conditional recognition250, запропоновані sources/routing/first semantics та професійна швидкість. Жоден із цих нових параметрів не впроваджено цим brief. No source/build/deploy/runtime/production mutation.
