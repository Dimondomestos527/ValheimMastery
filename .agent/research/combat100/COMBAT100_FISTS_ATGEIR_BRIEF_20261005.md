# Recovered Fists100 / Atgeir100 brief
Source: Valheim_Mastery, thread6ac2badc-782c-83eb-a8dd-a13a39bdb3ab, latest human turndaea1129-1cec-480b-a8cc-f0fa4bae70db, assistant messaged55f47db-b8da-43f6-bc5b-06580c9ac01a. Original body follows without edits.
AUTHORITATIVE CORRECTION: user in Combat100 explicitly retained current2026-10-05 GoldCooldownPolicy:cost<=250=>0;250<cost<=500=>5min;cost>500=>20min. Any active patronA cooldown blocks allA and foreignB>250, not only foreignmajor; B<=250 permitted without resetting timerA. Patron-specific pools shared by associated skills; no common cross-patron wallet.
Human latest source decision confirmsTyr/Fists750/60sec and Odin/Atgeir750/5min/+5min timed buffs once; supersedes old15sec/freeze/other active ideas. Assistant tuning figures and status-effect categories still require evidence; never promoted to implemented source.
Task is retrieval+source/architecture review under current baseline, no source implementation authorized by preserving this text.

## Original combined prompt

Ти працюєш над Valheim Mastery.

Перед початком обов'язково ознайомся з поточними canonical repository docs/source для:

- Combat 100
- Gold Core / Pantheon
- Combat 35/70
- Atgeir / Polearm mechanics
- Fists / Unarmed mechanics
- Shield 35 projectile reflection
- Trinket / Adrenaline system
- movement / jump implementation
- stagger / knockback / force handling
- food / Rested status handling
- status effects / timed buffs
- cooldown / Favor / exhaustion semantics
- multiplayer authority and persistence
- VFX/SFX infrastructure
- vanilla asset workflow/catalog

Як додатковий design context використай `VALHEIM_MASTERY_IDEATION_CONTEXT.md`, але canonical current source/docs мають вищий пріоритет, якщо snapshot застарів.

Не змінюй затверджену gameplay semantics через технічну зручність. Якщо конкретна частина конфліктує з поточною архітектурою, спочатку зафіксуй проблему і запропонуй найближчий технічно коректний варіант.

# SHARED GOLD COOLDOWN RULE

Нове canonical design rule для Gold abilities:

- Favor cost < 250: Gold cooldown не створюється.
- Favor cost 250–499: 5-minute Gold cooldown.
- Favor cost >= 500: 20-minute Gold cooldown.

Коли Gold ability запускає cooldown:

1. cooldown блокує всі Gold abilities того самого patron;
2. abilities інших patrons із Favor cost >= 500 також недоступні;
3. abilities інших patrons із cost < 500 залишаються доступними.

Не припускай існування готової універсальної реалізації цього правила. Перевір current Gold Core та визнач правильну shared integration.

---

# ATGEIR 100

Patron: ODIN

Gameplay owner: Combat 100, із Gold Core review.

## PASSIVE — projectile reflection during special attack

Поки гравець виконує Atgeir special attack / Eye of the Storm, hostile projectiles повинні відбиватися назад у ворогів.

Основна desired semantics:

- reflection працює лише протягом фактичної активної combat-window special attack;
- projectile бажано перехоплювати в районі зовнішньої частини sweep/circle Atgeir;
- projectile не повинен візуально долітати до центру player model і тільки там раптово відбиватися;
- якщо точне moving sweep interception непропорційно складне, допустимий fallback — кільцева interception zone приблизно по combat radius special attack;
- reflection behavior за можливості має reuse semantics Shield 35, а не створювати незалежну несумісну reflection system;
- reflected projectile має бути спрямований назад до валідного source attacker, якщо це підтримується відповідним projectile type;
- не використовувати тупе нескінченне `velocity *= -1`, якщо поточна система Shield 35 має кращу target/provenance logic;
- reflected/generated hit не повинен створювати recursion, нескінченний reflection ping-pong або неправильний XP/proc farming;
- beams, persistent AoE та інші атаки, що технічно не є нормальними hostile projectiles, не потрібно насильно трактувати як projectiles.

Перевір multiplayer ownership, generated-hit provenance і interaction між двома Atgeir/Shield reflection users.

## ACTIVE — ODIN'S MUSTER

Combat/support Gold ability.

Favor cost: 750.

Gold cooldown: 20 minutes according to shared rule.

Duration: 5 minutes.

Activation:

- player performs a short battle-cry / command presentation while holding an Atgeir;
- використовуй лише vanilla Valheim animation/pose possibilities;
- exact animation має бути знайдена через current native/source review;
- imported custom animation заборонена.

Affected players:

- caster;
- friendly allied players у приблизному радіусі 35 m на момент activation;
- buff після отримання не потребує залишатися біля caster;
- PvP/enemy characters не отримують buff.

Base buff:

- +25 Maximum HP
- +25 Maximum Stamina
- +10 Armor

No regeneration bonuses.

### Divine buff-duration effect

Odin's Muster має дозволяти команді накопичити сильний timed-buff setup перед важкою битвою.

Замість постійної зупинки timer бажана простіша semantics:

1. Кожний eligible positive timed buff, який уже активний на гравцеві в момент отримання Odin's Muster, отримує +5 minutes remaining duration ОДИН РАЗ.
2. Кожний eligible positive timed buff, який активується на гравцеві протягом 5-minute Odin's Muster window, також отримує +5 minutes duration ОДИН РАЗ при застосуванні.
3. Odin's Muster ніколи не подовжує сам себе.
4. Повторний application одного й того самого status-effect instance не повинен випадково додавати +5 minutes багаторазово через internal refresh callbacks.

Eligible examples повинні включати, якщо вони справді представлені normal timed positive status effects:

- Forsaken/Boss powers;
- Trinket timed buffs;
- Rested;
- Mastery timed positive buffs;
- інші нормальні beneficial timed status effects.

Не подовжувати:

- food duration;
- negative status effects / debuffs;
- potion/item cooldowns;
- Gold cooldowns;
- Gold exhaustion;
- internal mechanic timers;
- Odin's Muster itself.

Не створюй великий hardcoded whitelist без потреби. Спочатку перевір, чи current Valheim/status-effect architecture дозволяє надійно класифікувати beneficial timed effects. Якщо ні, задокументуй категорії, які потребують explicit handling.

### Multiple Odin's Muster users

Odin's Muster не stack'ається сам із собою.

Новий Muster не повинен:

- додавати другий +25/+25/+10 layer;
- нескінченно продовжувати існуючий Muster;
- повторно додавати +5 minutes до тих самих already-extended effects через просту reapplication exploit.

Визнач технічно надійну multiplayer semantics після source review.

## ATGEIR PRESENTATION INTENT

Activation:

- Atgeir піднімається / ставиться у виразну command pose;
- короткий бойовий клич;
- одна сильна radial vanilla-only pulse/wave;
- nearby affected teammates отримують короткий confirmation flash/pulse.

Persistent 5-minute Christmas-tree VFX не потрібен.

SFX:

- battle-command / warrior vocal character;
- low, martial resonance;
- короткий metallic Atgeir accent;
- nearby players повинні чути activation.

Exact vanilla assets не вигадувати. Спочатку asset/source review.

---

# FISTS 100

Patron: TÝR

Gameplay owner: Combat 100, із Gold Core review.

## PASSIVE — Dual Trinkets

Level 100 Fists дозволяє одночасно екіпірувати 2 compatible Trinkets.

При Trinket activation:

- обидва equipped Trinket effects активуються як одна combined activation;
- required Adrenaline cost = 75% від суми normal Adrenaline costs обох Trinkets;
- rounding rule визначити відповідно до current Adrenaline representation;
- якщо Adrenaline недостатньо для повної combined cost, не активується жоден із двох effects;
- activation має бути atomic з gameplay point of view;
- один Trinket не повинен активуватися без другого через часткове списання cost;
- normal stacking rules самих Trinket effects залишаються чинними;
- Fists 100 не створює додаткового multiplier зверху на effect strength.

Перевір compatibility із inventory/equipment UI, persistence, death/reconnect і current Trinket slot architecture.

## REACTIVE GOLD PASSIVE — TÝR'S SECOND BREATH

Це не manual active button.

Favor cost: 750.

Gold cooldown: 20 minutes according to shared major Gold rule.

Trigger:

Якщо player із Fists 100 отримує lethal damage, перед реальною смертю перевір:

- чи доступна Týr Gold ability;
- чи немає blocking Gold cooldown;
- чи player має >=750 Favor;
- чи damage/death event є нормальним gameplay lethal event, для якого ця mechanic призначена.

Якщо всі умови виконані:

- списати 750 Favor;
- запустити відповідний 20-minute Gold cooldown;
- player не помирає;
- HP стає 1;
- активується Týr's Second Breath на 60 seconds.

Не активувати ability, якщо cooldown активний або Favor недостатній.

Не намагатися перехоплювати admin/debug/system-forced death paths, якщо вони не є нормальним combat/environmental lethal damage. Exact exclusions визначити після native source review.

## SECOND BREATH STATE — 60 seconds

Поки active:

### Survival

- HP не може впасти нижче 1;
- player не може померти від normal qualifying damage;
- player не може отримувати healing;
- healing effects можуть продовжувати існувати технічно, але не повинні піднімати HP під час state;
- після завершення не повинно відбутися накопичене deferred mega-heal.

### Mobility

Дати помітний berserker mobility boost:

- increased movement/run speed;
- increased jump height;
- bonus/second jump також повинен отримувати відповідний relative boost.

Перевір current jump implementation. Якщо bonus jump уже базується на modified base jump parameters, не дублюй bonus випадково.

Exact tuning values можна визначити після source/gameplay review. Початковий design target:

- приблизно +20% movement speed;
- приблизно +30% jump height.

### Stamina

Усі normal player action stamina costs знижуються.

Initial design target: приблизно -40%.

Не давати infinite stamina.

Перевір:

- attacks;
- sprint;
- jump;
- dodge;
- block-related stamina;
- special attacks;
- інші action costs, щоб effect був послідовним і не дублював existing modifiers.

### Stagger and force

Під час Second Breath:

- stagger meter не накопичується;
- player не може бути staggered;
- hostile hit force/knockback не відкидає player.

Не відключай нормальний voluntary movement, jump physics або інші необхідні character controllers.

Перевір, чи існують displacement mechanics, які не є normal hit force і не повинні блокуватися.

## SECOND BREATH END / EXHAUSTION

Після завершення 60 seconds:

1. зняти Týr's Second Breath effects;
2. видалити Rested, якщо він активний;
3. для кожної активної їжі:
   - якщо remaining duration >5 minutes, встановити remaining duration = 5 minutes;
   - якщо remaining duration <=5 minutes, не змінювати;
4. після актуального recalculation food-derived stats встановити HP = 50% від НОВОГО current maximum HP.

Не змінювати strength їжі напряму.

Не видаляти їжу.

Не ставити її duration назад на 5 minutes, якщо вона вже мала менше.

Під час самих 60 seconds food timers продовжують працювати нормально.

Це conceptual cost ability:

player переживає смертельний момент і отримує одну хвилину надзвичайної фізичної витривалості, але після цього стає невідпочилим і дуже швидко залишиться без їжі.

## FISTS PRESENTATION INTENT

Trigger:

- короткий сильний divine/physical pulse у момент, коли lethal hit перетворюється на Second Breath;
- Týr theme має відчуватися як warrior refusing to fall, а не magic resurrection;
- ефект має бути легко помітний owner та nearby players.

During state:

- restrained persistent cue;
- subtle hand/body energy;
- можливий короткий trail під час sprint;
- не створювати величезний постійний aura clutter протягом 60 seconds.

End:

- effect різко згасає;
- короткий exhaustion cue.

Exact vanilla VFX/SFX assets знайти через current asset workflow. Не вигадувати prefab/clip names.

---

# ACCEPTANCE / EDGE CASE REVIEW

Окремо перевір і задокументуй:

ATGEIR:
- projectile reflection із moving special attack;
- projectile ownership/source;
- reflected projectile hitting original attacker;
- multiple reflectors;
- generated-hit proc/XP recursion;
- remote player visibility;
- Odin's Muster range;
- join/leave/death during 5-minute buff;
- multiple Odin users;
- status-effect refresh versus one-time +5-minute extension;
- buffs activated during Muster;
- Rested;
- Forsaken Powers;
- Trinket buffs;
- food exclusion;
- Gold cooldown exclusion;
- VFX disabled behavior.

FISTS:
- lethal hit interception order;
- simultaneous/multi-hit lethal damage;
- DoT/environmental damage;
- repeated damage while at 1 HP;
- healing suppression;
- lifesteal/regeneration interactions;
- health recalculation after food-duration modification;
- death/logout/reconnect during Second Breath;
- stagger/knockback suppression;
- dual Trinket persistence;
- insufficient Adrenaline;
- Trinket combinations with overlapping effects;
- multiplayer owner/remote representation;
- Favor debit and Gold cooldown atomicity.

Не вважай build success доказом correct gameplay.

Наприкінці дай:

1. source/architecture findings;
2. exact implementation plan;
3. reused existing systems;
4. new shared integration required;
5. unresolved design questions, тільки якщо source реально виявив конфлікт;
6. regression/test matrix;
7. список vanilla presentation assets/candidates, які були фактично знайдені, із provenance;
8. estimated implementation risk для Atgeir та Fists окремо.
