# Накопичення Favor Tyr/Odin — design review
Дата2026-10-05. Task COMBAT100_TYR_ODIN_FAVOR_EARNING_20261005. Owner COMBAT_100.
Уточнення користувача: Favor Tyr/Odin, не adrenaline. Auto-proc/capacity=C для двох трінкетів не змінюється.
Статус: DESIGN ONLY; core recommendation APPROVED HUMAN DESIGN2026-10-05, optional details DESIGN SUGGESTION. Ніяких source/shared/build/runtime/production-state змін.

## APPROVED HUMAN DESIGN —2026-10-05
Користувач прямо відповів «Приймаю пропозицію» на фінальну рекомендацію Combat100.
Прийнято:
- Favor за завершення реальної актуальної сутички, не кожен hit/XP/action.
- Початкові reward bands20/40/60 за threat; точний classifier і thresholds калібруються.
- Fists contribution→Tyr; Atgeir contribution→Odin; defense/parry/control підтверджують participation, не окрему виплату.
- Кожен справжній co-op participant отримує особистий reward; не last-hit monopoly/proximity reward.
- Змішаний внесок ділить один особистий reward між patrons, без full payout кожному.
- No income за received/self damage/lowHP/per-parry; Second Breath/Muster activation не earn/refund.
- Рідкі boss/discovery bonuses можуть доповнювати модель, але не steady income benchmark.
- Focused target450–600Favor/h на одного активно наповнюваного patron — прийнятий початковий орієнтир live calibration, не гарантована зарплата чи measured result.750 з0 при цих rates=100–75min; tactical reserve pacing.
Не затверджено автоматично: exact optional250recognition/+200first/150biome/100location rewards, cap changes, every technical suggestion in historical review, exact encounter/support/anti-camp classifier або distributed protocol. Вони лишаються окремими кандидатами/engineering gates.
Execution status лишається DESIGN ONLY (approved core design), не IMPLEMENTED/LIVE VERIFIED. Ціна750, duration60/300, current cooldown/patron-pool contracts і adrenalineC не змінені. Реалізація потребує окремої авторизації; Gold protocol лишається GOLD_CORE.
Цей блок має пріоритет над попереднім текстом «не approved» для переліченої core recommendation. Решта historical proposals не перетворюються на людські рішення.

## 1. Що запозичено з ремісництва, а що реально реалізовано
Переглянуто актуальний Gold economy artifact [FAVOR_ECONOMY_DESIGN_20261005](../../../docs/gold/FAVOR_ECONOMY_DESIGN_20261005.md), matching Gold handoff та current source. Релевантне обговорення зафіксоване там; пропозиції цього документа НЕ є схваленими числами чи гарантією реалізації.

FACT SOURCE:
- GoldFavorModel.cs:9–15,69–86: provisional0.5 Favor/validatedXP, charge multiplier4; для source у300s перші3 events d=1, наступні до10 d=.5, далі .2. Формула min(100,validatedXP×d×.5)/4, default event ceiling25. Це XP-based crafting модель, не approved universal combat earning.
- GoldFavorService.cs:89–159: Crafting-only observer, server validates canonical craft/upgrade/build base/finalXP, actor/unlock/authority/rate, build replacement suppression. Processing окрема receipt path. Не combat receipt.
- GoldCraftingLedger.Gain:146–157: Favor=min(1000,F+amount), dirty save; ordinary gain не синхронний durable completion journal.
- MasteryEvents.cs:19–34: HitResolved має Player/Target/Hit/Skill і generated context, але не actual final HP-loss, stable encounter ID чи participant contribution ledger. Extended EnemyKilledEvent має killer/target/skill, не всіх учасників.
- MasteryEventDispatchPatches.cs:17–57: ApplyDamage prefix/postfix та WasAlive/IsDead condition; це не доведений universal terminal-death callback і не ready Favor source. Native death може завершуватися пізніше; потрібен окремий terminal confirmation.
- Bounded current search у GoldFavor*/combat events і по Favor+Unarmed/Polearms/CombatFavor/EncounterReward/FavorEncounter не встановив Tyr/Odin earning implementation. Не робити висновок із catalog/localization.

| Компонент | Класифікація |
|---|---|
| Crafting validatedXP→Favor formula | IMPLEMENTED EXECUTION PATH у прочитаному source; gameplay rate не виміряний |
| Tyr/Odin Favor earnings | UNKNOWN execution coverage; нові запропоновані mechanics DESIGN ONLY |
| Patron-specific actual Tyr/Odin wallets | DESIGN ONLY за поточними owner records; legacy runtime Völundr |
| Нова encounter completion/participation reward система | DESIGN ONLY |
| Live hourly calibration / multiplayer earning | DEFERRED поточною baseline scope |

Датовані SHA256 refs:
GoldFavorModel 779DAE4122066E60316AD4B6C168E76D24D11498CABE1DE384C27714BD6B329D;
GoldFavorService C8418CC2173E6A5CACAF9C73397AEFF67BA5A3226DA1BFF46F1B37E3DF3177D3;
MasteryEvents C82A50F86AC7A456F39BA26DE0B5A96BCA1CC989853F9CDEB9AD74CA5A537B01;
MasteryEventDispatchPatches D36E24409566B86F438E1D680D55225B32F65DD132F4F6325984863A9A523E4A;
Gold economy design 73C3B81AFECEB51DA8EB9B7FED04EFAA4B850502256B44F4ED1522823EE8E6C6.
Source version1.4.123 не означає незмінний checkout чи installed DLL equivalence.

Принципи ремісничого дизайну для перенесення:
1. Оплачувати завершений корисний результат. У ремеслі — authored valuable batch/net spent inputs; у бою — закриту реальну загрозу.
2. Не прив'язувати валюту до FinalXP/multipliers або числа дешевих events.
3. Нагороджувати автора/учасника, не collector/last-hit observer.
4. Один outcome — один особистий reward; kill+XP+proc не три виплати.
5. Repetition penalty для exploit loops, а не просто однакового потрібного продукту/типу ворога.
6. Рідкі first bonuses не маскують steady-state; full wallet не є benchmark порожнього.
7. Порівнювати Fav/h одного активно наповнюваного patron; загальний450 split225/225 не дорівнює450 кожному.
8. Cap/overflow/cooldown — окремі обмеження; cooldown не є автоматичним темпом накопичення.

## 2. Два можливі підходи

| Підхід | Сильна сторона | Слабкість |
|---|---|---|
| A. Завершені небезпечні сутички, один bounty на учасника | Узгоджений із ремісничим accomplishment принципом; підтримує parry/control/co-op; не заохочує weak weapon/high hit count | Треба визначити encounter closure, contribution та stable identity |
| B. Bounty за реально переможеного hostile enemy, budget об'єднаних загроз | Простіша terminal identity; швидший feedback | Per-mob income заохочує dense spawn camps; без encounter budget/support attribution last-hit і AoE мають перекіс |

ROOT RECOMMENDATION: A. B може бути лише underlying terminal evidence для A, не другою винагородою. Не вводити одночасно per-hit, per-kill і encounter payout. Шкала Favor не повинна поводитися як adrenaline.

## 3. Звідки конкретно накопичувати

Усі amounts — початковий DESIGN SUGGESTION. Не йдеться про гарантовану виплату за будь-якого mob.

| Подія | ОсобистеR | Умова |
|---|---:|---|
| Завершена звичайна актуальна сутичка |20 | Реальна ворожа загроза, не trivial grind |
| Завершена складна сутичка |40 | Вищий threat band, не окремі40 за кожного моба |
| Завершена особливо небезпечна/elite сутичка |60 | Верхній звичайний encounter budget, не ×кількість цілей |
| Бос repeatable |30–50 | Current Gold candidate bands; той самий boss type у rolling60min:1,.5,0 |
| Перший особисто переможений boss type |+200 до його одногоR | Особиста участь, durable first claim; не global defeated flag |
| Реальний new site completion |20–25 | Stable site/participation receipt, один neutral reward |
| Новий biome/location class |150/100 | Рідкі optional Gold candidates, не сталий бойовий дохід |
| Unlock recognition |250 один раз | OPTIONAL із Gold proposal; не реалізовано/не approved цим task |

Tyr: meaningful contribution Unarmed/Fists або інших явно mapped Tyr skills наповнює Tyr. Кулаки не отримують більший reward за довгий бій/малий damage; не потрібно навмисно доводити себе до1HP.
Odin: meaningful Polearms/Atgeir або інших явно mapped Odin skills contribution. AoE/control/parry можуть підтверджувати роль у результаті, але не дають власну виплату за кожну ціль/animation/proc.
Список інших associated skills береться з майбутнього затвердженого mapping, не вигадується тут.

Defense/support:
- Реальне успішне блокування/перехоплення загрози може бути eligibility/contribution evidence конкретного enemy action, не raw parry counter.
- Просто tanking damage/стоять поряд/натиснути buff — не meaningful participation.
- Gold cast сам по собі не дає Favor. Actual encounter може дати його qualifying учаснику за незалежні gameplay actions; не refund cost proportional to affected teammates.
- Reflected/generated hits, spin extra pulses чи proc не mint окремі rewards/routing weights. Вони можуть завершити ту саму сутичку; уже підтверджений normal participant не втрачає один bounty.
- Не вимагати тільки damage last-hit: це робить захисника чи control contributor невигідним. Exact bounded defensive evidence/eligibility threshold потребує owner review, чинні events цього не доводять.

## 4. Routing: роздільні pools без подвійних виплат
Approved constraint зберігається: individual player/world/patron pools; linked skills спільні всередині свого patron.

PROPOSAL:
R=B×d+I.
За одну особисту сутичку ΔTyr=qT×R, ΔOdin=qO×R, де qT+qO≤1. Для однорідного contribution100% у відповідний pool.
Для змішаного contribution q визначаються validated effective contribution до threat resolution, не held weapon на останньому кадрі, hit count чи raw overkill. Direct effective HP damage cap за HP target; defensive credit bounded і once per real hostile action. Different evidence units не додавати без normalization/calibration.

ПрикладR40, Tyr/Odin contribution75/25 →30 Tyr +10 Odin, НЕ40+40.
Weaponswitch після завершення не змінює routing. Накопичення під активним cooldown дозволене; cooldown блокує spend за чинним policy, не income.
Neutral accomplishment/first bonus → один honored patron, вибраний ДО події, як у Gold proposal; UI choice не може перекинути вже зароблений specific reward.
Hard cap1000/patron — рекомендація з current crafting precedent. Overflow discard без transfer/hidden ticket. User approved separate pools, але всі нові cap/routing details лишаються proposals.

Co-op:
Кожен meaningful eligible participant отримує своєR; no last-hit monopoly/party-size multiplier/proximity reward. Усередині його особистогоR діє routing conservation. Чотири реальні учасники можуть отримати4×R сумарно — це4 people, а не duplication одному player. Faster co-op encounter throughput обов'язково калібрувати окремо. Одна подряпина/AFK observer не автоматично full reward.

## 5. Anti-farm: що повинно давати0
- LowHP, власноруч отримана шкода, відсутність Rested/їжі, погода, naked gear, stamina spend.
- Кожен hit/parry/dodge/secondary pulse/reflect або XP grant як самостійна виплата.
- PvP, friendly/tamed/owned targets, admin-spawn/debug events, fake damage replay.
- Healing/resetHP/re-engagement однієї й тієї ж unresolved загрози, repeated same enemy life completion.
- Gold ability trigger/Second Breath survival/Muster recipients як direct earn/refund.
- Cheap trivial mobs лише через те, що player зняв armor/узяв слабку зброю.
- Reconnect/reload/world-hop replay однієї reward receipt/first claim.

Не накладати source-name DR ремесла механічно на combat: багато законних сутичок проти одного виду ворога не є одним предметом, який нескінченно rebuild. Натомість repeat budget має keyed confirmed exploit/spawner/boss/site context; що саме є artificial loop і розмір вікна потребують gameplay calibration. Дві послідовні справжні threat groups не штрафувати лише за однаковий prefab.

Encounter contract перед implementation:
- Stable authenticated world/enemy-life/group/participant IDs; server binds claims, owner simulation supplies validated evidence, not arbitrary client '+40'.
- Group belongs to shared hostile threat episode; chain pulls/splitting не створюють додаткові повні budgets.
- Closure після terminal resolution і короткого quiet window; candidate10–15s only після зникнення active threat. Disconnect/escape/pause не є victory trigger.
- Bounded grouping за вже залученими threats/часом, не довільна proximity всієї біоми. Continuous waves потребують legitimate completed episode boundaries.
- One personal receipt atomically paid/routed once; overflow також settles receipt.
- Repeated known spawn context cannot reset budget через pull/relog; exact camp detection/DR not implemented. Повна гарантія anti-farm не заявляється.

## 6. Баланс темпу — відтворювана арифметика
Candidate focused repeatable target450–600 Favor/h із Gold design; не observed rate і не passive timer income. Core combat candidate workloads:
-6 ordinary×20 +4 hard×40 +2 elite×60=400 за12 encounters/h.
-12 ordinary×20 +6 hard×40 +2 elite×60=600 за20 encounters/h.
Перший model приблизно5min/episode, другий3min/episode inclusive travel — workload assumption, не факт Valheim.
Gold450 example додає до combat400 два нові sites×25. Це mixed loop, не доказ450 repeatable combat forever.

Формули:
t0=60×750/r хв із0.
Після spend750 із1000 залишається250; tnext=max(20,60×(750−250)/r).
Steady affordable casts/h=r/750, до теоретичного cooldown ceiling3/h для одного доступного patron.
Muster average funded uptime=5×(r/750)/60, якщо casts не overlap/без запасу та action завжди доступна. Це ресурсна оцінка, не guaranteed uses.

| Focused r/h |0→750 |Наступна після spend із1000 |Steady Muster uptime |
|---:|---:|---:|---:|
|400 |112.5min |75min |4.44% |
|450 |100min |66.67min |5% |
|600 |75min |50min |6.67% |
|900 |50min |33.33min |10% |
|2250 |20min |20min, CD limit |25% |

900/2250 — лише альтернативи для stress test, не рекомендоване тихе підняття rates.2250/h необхідні для сталого750 every20min; тоді дешеві100/250 actions інших/майбутніх contracts теж оплачує цей високий дохід. Balance всього patron доведеться переоцінити.

Якщо total450/h розподілити Tyr/Odin50/50:
r кожного225/h;750 доступні через200min; за3h по675. Спільна сума1350 не оплачує одну дію: pools не об'єднуються.
Це справжня ціна versatility. Neutral honored routing трохи допомагає, не усуває її.
Не лікувати split-risk виплатою fullR обом або прихованим shared wallet.

Tyr reactive availability: r750 funding не означає guaranteed save everyXmin; ще потрібні qualifying lethal/admission/cooldown. Заборонити економічний feedback від HP1 state.
Odin duration300s при100min funding —5% steady uptime. Такий perk є рідкісним tactical reserve. Якщо очікується майже регулярний party skill, price750/r450 — слабкий баланс саме для цього очікування. До рішення про desired use frequency цифри не затверджувати.

ROOT starting recommendation: використати accomplishment model20/40/60 і перевіряти450–600 focused/h, окремо solo Fists, solo Atgeir, co-op, mixed routing;600 є верхнім початковим target, не гарантованою мінімальною зарплатою.750 costs/20min CDs не змінювати цим task. Unlock recognition/firsts лише optional relief, не benchmark steady earning.

## 7. Що треба інтегрувати надалі
Shared mutations зараз NONE. Future declaration before code:
- Gold Core reward admission/PatronId wallets, durable paid receipts/cap/first/repeat history; shared reason: combat outcomes not craftingXP.
- Combat event/terminal owner evidence, effective damage/support capture, generated transport normalization; affected Combat35_70, XP, networking.
- Root participant/encounter service contract і serialized settlement; bounded memory/performance/owner migration.
- UX лише authoritative aggregated '+Favor reason/patron', не popup per hit. Assets/presentation implementation N/A цього task.
- Reuse approved cooldown policy; preserve same patron all blocked/foreign>250 blocked; no income freeze, no reset за дешеву action чи relog.

Required future tests (NOT RUN):
solo/multiple targets/direct/generated final blow; weak/trapped/spawner/PvP/tamed/debug; damage-after-mitigation/overkill; healer/defender/one-hit AFK; two skills/time weapon switch; duplicate receipts/reconnect/crash/owner migration; maxcap/overflow/first; cooldown income; Tyr HP1/Muster no feedback; solo/co-op20–60min real rates; finished versus aborted encounter/chain waves.
Прийнятність pacing визначається real funded750 times від0 та після spend, не лише screenshot full scale.

## 8. Підсумок рішення і handoff
Найсильніший варіант: Favor за завершення актуальної сутички; Fists/Polearms внесок визначає Tyr/Odin routing; parry/control підтримують participation, але не direct payout. Незалежні rare exploration/first rewards можуть доповнювати focused pool через honored routing.
Відкинуті per-hit/received-damage/lowHP/Gold-refund системи створюють валютний farm і конфліктують із ремісничим принципом корисного результату.
Новий earning proposal DESIGN ONLY. Реалізація, exact thresholds/camp classifier/receipt protocol і live calibration — наступна окремо авторизована задача.
