# Fists100 / Atgeir100 — перевірка джерел і план реалізації
Дата: 2026-10-05. Owner: COMBAT_100. Task: COMBAT100_FISTS_ATGEIR_REVIEW_20261005.
Обсяг виконаного: відновлення промпту, поточні source/native reads, два bounded read-only scouts, root architecture review. Змінено тільки зарезервовані документи. Build, gameplay tests, запуск, deploy і зміни shared systems не виконувалися.

## 0. Авторитет, статус і прийняті рішення
Джерело вимог: [незмінений brief](COMBAT100_FISTS_ATGEIR_BRIEF_20261005.md). Пряме вкладення користувача 5649210d-c50b-488c-82aa-937dfb192013/Вставлений текст.txt збігається з відновленим текстом після нормалізації переносів: 12275 символів. Попереднє вкладення e94b64d1-a170-4ea0-9033-1a3a85691fea також ідентичне. Не використовувати старі Loki/Odin's Line, 15 секунд або замороження buff timers.

Поточна source authority: src-modern + ValheimMasteryPoC.csproj + tools/Build123.ps1; MasteryPlugin version 1.4.123. Під час root reads каталог src-modern містив 214 C# файлів, тому історичні 198 у bootstrap та попередній baseline не є поточним file count. Спільний checkout змінюють інші owner chats: звіт є датованою перевіркою конкретних шляхів, а не гарантією незмінності всього checkout чи відповідності вже встановленому DLL.

| Слот / компонент | Класифікація | Підстава |
|---|---|---|
| Fists / Unarmed100 catalog slot | CATALOG ONLY | Dedicated execution path для цих100 механік не встановлений у перевірених джерелах |
| Dual Trinkets | DESIGN ONLY | Нова вимога; native має один trinket slot |
| Týr's Second Breath | DESIGN ONLY | Lethal-save/Favor/state100 implementation не знайдена |
| Atgeir / Polearms100 catalog slot | CATALOG ONLY | Dedicated execution path для цих100 механік не встановлений |
| Reflection100 / Odin's Muster | DESIGN ONLY | Існують35/70 donors, але не execution100 |
| Client/native parity, exact command/vocal assets | UNKNOWN | Цей review перевіряв установлену server assembly статично |
| Gameplay / multiplayer / presentation regression | DEFERRED | Поточна фаза inventory/design; запуск не авторизований |

Відсутність підтвердженого execution path не доводить відсутність будь-якого майбутнього або щойно зміненого шляху. Перед реалізацією повторити bounded source drift check. Generic Fists scaling — XP & Skill Bonuses, не Combat100.

Підтверджені людські рішення:
- Fists: Tyr, реактивний lethal-save, 750 Favor, 60 секунд; жодної manual active button.
- Atgeir: Odin, 750 Favor, Muster300 секунд; timed beneficial effects +300 секунд один раз, без замороження.
- Dual Trinkets: ЗБЕРЕГТИ АВТОАКТИВАЦІЮ. Capacity = trigger threshold = C = 0.75 × (ізольований native threshold A + ізольований native threshold B), за однакових інших modifiers. Adrenaline float; integer rounding не потрібний.
- Чинний Gold контракт замінює застарілий абзац brief: cost≤250 →0; 250<cost≤500 →300s; cost>500 →1200s. Активний cooldown patron A блокує всі A і foreign B>250; B≤250 дозволений і не змінює таймер A. Обидві750 дії →20 хвилин.
- Favor pools окремі на player/world/patron; associated skills використовують pool свого patron. Tyr/Odin runtime wallets поки не доведені.

FACT SOURCE нижче означає прочитане поточне джерело; FACT NATIVE — встановлена server assembly/manifest, не live behavior. PROPOSAL — root engineering recommendation, не нове людське затвердження.

Native evidence: valheim_server_Data/Managed/assembly_valheim.dll; assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null; MVID 4668eeee-f19f-4992-9e3c-c61ec53f37cd; SHA256 7CAB9B49D31EC064591CA80402DD35C566E03B7297CFB7BF4696C38DA4E24D8B. Native methods/IL у наступних розділах належать саме цій assembly. Server/client parity та gameplay не перевірені.

## 1. Source / architecture findings

### 1.1 Fists: обладнання й adrenaline
FACT NATIVE: Humanoid.EquipItem IL09b8–09c7 unequip старий m_trinketItem та замінює єдину reference. Один slot явно враховується в UpdateModifiers, UpdateEquipmentStatusEffects, GetEquipmentWeight/GetSetCount, IsItemEquiped, UnequipAllItems, UpdateEquipment, death unequip та VisEquipment.SetTrinketItem. Другий m_equipped=true не створює працездатний другий slot. Load→EquipInventoryItems→EquipItem знову застосовує single-slot правила.

Player.AddAdrenaline після positive gain/game rate/gain curve/SE modifier на GetMaxAdrenaline обходить equipped items із m_fullAdrenalineSE, послідовно ResetTime/AddStatusEffect, після proc adrenaline=0 (IL0085–0153). Окремого native per-trinket activation-cost поля не знайдено; SharedData.m_maxAdrenaline є equipment capacity contribution. Default constructor capacity100 не означає фактичний prefab threshold100. GetMaxAdrenaline додає equipment contribution index10.

SEMan.AddStatusEffect може відхилити ефект через CanAdd. Новий ефект клонується, публікується у list, Setup запускає message/VFX/callbacks, потім SetLevel. Два послідовні виклики не є atomic. Видалити A після відмови B — вже запізно для side effects. Same hash має native nonstacking/refresh semantics, а не два посилені шари.

### 1.2 Fists: точка запобігання смерті
FACT NATIVE: Character.RPC_Damage owner guard IL003f–0044; mitigation до ApplyDamage; pushback уже відбувся біля IL044e. ApplyDamage використовує local damage rate IL0063–0069, обчислює finalHP IL017e–0197, SetHealth IL01ea–01ec, далі stagger/OnDamaged. SetHealth owner-only clamp не запускає смерть. CustomFixedUpdate IL00e4→CheckDeath→m_isDead/OnDeath — terminal boundary.

Потрібен scoped final-health interception перед SetHealth після чинної mitigation, не raw incoming damage estimate і не resurrection після OnDeath. Existing Jump70 save (PerkGameplayPatchesA1.cs:18–42) і Maul protection (Fists70Maul.cs:1004–1018) мають пріоритет до рішення про lethal; якщо вони вже врятували —750 не витрачається.

Owner RPC_Heal є sink для local/remote native healing. HoT ticks зменшуються до Heal: блокувати heal, не чергувати відкладене лікування. Direct positive SetHealth paths потребують окремого аудиту; глобальна заборона всіх SetHealth зламає expiry і load.

MovementPerks.cs:65–69 застосовує ApplyStatusEffectJumpMods і до air jump перед scale0.8. Ground jump використовує той самий modifier. +30% HEIGHT за сталої gravity → вертикальна velocity ×sqrt(1.3)=1.140175425099138. Velocity×1.3 дала б height×1.69.

UseStamina→RPC_UseStamina має sender/receiver paths; discount застосовувати один раз із matching affordability. Не всі списання є normal action costs: Clubs70Reservation.cs:135–153 резервує фактичний before−after delta, release:77–85 повертає цей delta. Hold/refund не можна дисконтувати як звичайну витрату та повертати nominal суму. Polearms70 continuous costs/refunds і ShieldRush70 HaveStamina/UseStamina також потребують parity.

Для stagger недостатньо modifier AddStaggerDamage: очистити наявний meter і закрити direct Stagger/RPC_Stagger. Для hostile push не вимикати Rigidbody чи voluntary movement; triggering lethal hit міг уже змінити push state до health hook. Maul70 movement lock:1013–1015,1047–1068 зберігається; його Heal:446 підпадає під healing sink.

### 1.3 Atgeir: бойове вікно і відбиття
FACT SOURCE: PerkGameplayPatches70Polish.cs:156–157 IsActive включає Starting і Spinning; Begin у Prefix Attack.Start:288–300 виконується до native result. Native Start може відмовити через stamina IL024b–0265. Тому IsActive не можна напряму використовувати як reflection window.

Перший OnAttackTrigger:177–195 підтверджує Spinning. Продовження має delay0.92×0.65=0.598s; наступні цикли:246–261 scrub vanilla animation + programmatic AoE. Radius=BaseRadius×(1+0.20×Stacks), Stacks0..5. Release/stamina/death/stagger/weapon-loss:226–233,264–276 завершують spin/відновлюють animator. AttackIntentService.cs:6–22,26–46 — donor concrete secondary intent. Polearm35AutoSpin.cs:139–180 — phantom generated AoE, pending queue не є фактичною special animation.

FACT NATIVE: Attack.Update перевіряє InAttack/stagger/m_wasInAttack IL0017–0041 та Stop IL0178–0192. Abort тільки ставить flag; потрібне явне закриття window на abort, не ghost protection до наступного update.

FACT SOURCE: Build123.ps1:4 включає MASTERY_SHIELD35_EXPERIMENT та shield-rush gate. Blocking35CorrelatedProjectiles.cs:12 marker1235; :36–41 owner/non-reflected gate; :52–100 authentication/correlation/finite-range checks/receipts256/direct-explosion dedup. Private Return:126–143 створює новий native-prefab projectile від body center, targets original source/fallback incoming velocity, прибирає spawned AoE/secondary projectiles/item respawn. Це потрібні semantics, але не готовий perimeter API. Legacy Blocking70Reflection.cs:30–46,88–119 не дає доведеної ZNetView authority migration та excluded під нинішніми gates — не брати за основу.

Native Projectile.FixedUpdate owner-only переміщує projectile й перевіряє sorted RaycastAll/SphereCastAll. Interception має перевіряти swept segment і native collision order: wall до ring перемагає, ring до body перемагає. Postfix після damage або перевірка лише current position запізнілі.

Projectile.Setup IL00be–00e0/OnHit IL033e–034c переносить variant у новий HitData. OnHit IL04b5–04d7 окремо RaiseSkill/AddAdrenaline. Тому заборона proc у damage hook не блокує native XP/adrenaline: reflected factory має явно zero m_raiseSkillAmount/m_adrenaline. PROPOSAL: також zero m_healthReturn, щоб не створити випадковий lifesteal від generated return. Damage/elemental payload зберегти за затвердженим Shield35 contract; beams/nonprojectile AoE виключити.

PerkRuntimeServices.cs:9–23 має process context, :72–83 wire marker1235. MasteryEventDispatchPatches.cs:29–40 створює default context; default false не гарантує застосування wire fallback. Новий return потребує transport normalization у всіх relevant subscribers, не тільки local weak-table flag.

### 1.4 Muster: статуси, stats і відновлення
FACT NATIVE: у StatusEffect немає universal beneficial/debuff polarity. Category/icon/hidden/attributes не доводять позитивність. StatusAttribute enum також не є taxonomy. Небезпечна ідея — автоматично продовжувати все з іконкою.

Remaining=ttl−time; ResetTime обнуляє elapsed. Existing AddStatusEffect може повернути null навіть після успішного refresh; Internal/hash/RPC overloads потребують coverage та lookup actual instance. SE_Rested.ResetTime→UpdateTTL може перезаписати TTL/elapsed за comfort. Blind ttl+=300 може зламати чи втратити natural refresh.

Armor native additive SE route: SE_Stats.ModifyArmorMods додає m_addArmor до multiplier; GetBodyArmor→ApplyArmorMods. Native SE_Stats не має maxHP/maxStamina hooks: GetTotalFoodValue outputs→UpdateFood SetMaxHealth/SetMaxStamina IL012d–0149. PROPOSAL: +25/+25 до aggregate values, +10 additive armor у чинному native order; не змінювати food strength і не створювати regen. Exact +10 після всіх сторонніх multipliers не обіцяється.

UpdateFood(0,true) усе одно віднімає1s food/time; це не pure recalc. Cooking35Perk.cs:55–66,76–82 перераховує decay caches; MasterFeastService.cs:176–189,281–292 додає no-decay override. MagicSkillPassives.cs:38–58 UpdateFood hooks можуть Heal. Потрібен pure food-cache/effective-max recalculation, який зберігає існуючі prepared/slow-decay/masterfeast правила, не викликає time/heal/UI callbacks.

Native Player.Save/Load не зберігає всі custom active SE/extension receipts. MasteryStateStore transient weak-table — не persistence. Generic timed state після logout не можна вважати автоматично відновленим.

### 1.5 Shared Gold зараз
FACT SOURCE: GoldCooldownPolicy реалізує чинні cost brackets/foreign block/zero-cost no-reset. GoldCraftingLedger.cs:62–67 має один Favor/exhaustion/pending; version3:12. Reserve:263–277 hardcodes LegacyPatronVolundr; Commit:282–298 idempotent settled token/debit/cooldown/persistence rollback. GoldDivineTransactions GoldAction:10–37 містить recipe/prefab/quality та contract kinds1/2; Receive:306–321 recovery before fresh admission. Це crafting action contract, не універсальний combat registry.

GoldCraftingService HasGold:143–145 hardcodes Crafting100; Tick:104–120 world UID, Time.time та онлайн-player elapse. OwnerSkillAuthority snapshot має Unarmed, але не Polearms у nonmagic transported fields; binding/authority перевірити й розширювати через Gold/network owner. GoldCharacterSave перевіряє actual writer completion, але сам по собі не є combat replay journal.

Snapshot hashes при root reads:
- GoldCooldownPolicy A17B6CFDA2A782D8413F85D7B5C14AA732889F0C01A8CD27090934A83911533C
- GoldCraftingLedger 18DC61D8202585EF556C2FE90F87DC050F525B19856031A88DD3E79B94E82C00
- GoldDivineTransactions CA7B7424C9760E87A8409A3A5747003045399C06899054BB916A97E77988A276
- OwnerSkillAuthority 7C286872A8040A4793E7021CB97CCC08983875BAF02C8B50D4F45F2B8C2C4842
Це dated evidence, не вимога відкотити подальші легітимні Gold зміни.

## 2. Точний план реалізації — після окремої авторизації

### Етап A. Спільні контракти перед mechanic code
1. Створити implementation task із exact reservations та source drift check. Root інтегрує shared зміни серійно; ізольовані нові Combat100 services можна делегувати лише після встановлення non-overlap.
2. Gold owner визначає Tyr/Odin wallets, combat unlock/admission, authority snapshot для Polearms/Unarmed, action receipts і cooldown policy reuse. Не дублювати ledger/Favor у combat files.
3. Зафіксувати combat state identity: world/player/character/activation serial, effect-instance IDs, expiry, applied-recipient receipts, terminal exhaustion applied/pending. Розрізняти permit, consumed activation і terminal completion.
4. Узгодити persistent clock із серверним часом/restart recovery; Gold cooldown clock не змінювати в Combat task. PROPOSAL: combat60/300s продовжують минати offline, expired Second Breath виконує terminal penalty після load перед gameplay один раз. Конкретний trusted persisted clock — Gold/network review gate, не DateTime.Now на клієнті.
5. Root + affected owners review equipment/SE, generated projectile, damage, food/stamina contracts. Не починати Second Breath із звичайного asynchronous RPC prefix.

### Етап B. Dual Trinkets
1. Stable item-instance second-slot resolver, зберігаючи vanilla primary reference. Validate distinct instances/type/effect admission; не змінювати shared ItemData definitions.
2. Покрити UI slot selection, equip/unequip/drop/death/weight/modifiers/equip-SE/durability/load/skill-loss. Skill-loss → deterministic second-slot unequip, не зникнення предмета. Visual representation другого trinket не вигадувати до evidence/UX review.
3. Один stable equipment snapshot рахує isolated thresholds A/B з однаковими іншими modifiers; C float. Наприклад100+100→150 тільки ілюстрація формули.
4. Capacity/threshold=C. Capacity change/equip/load не proc; clamp excess без активації. Лише qualifying positive gain запускає combined proc. PROPOSAL: retain native one activation/reset0 для oversized gain, без серії повторних procs.
5. Preflight обоє effects, resolve duplicate hashes за normal stacking, staged gameplay publication і presentation після успіху. Unsupported irreversible callbacks → incompatible із явним поясненням, а не partial activation.
6. Один serial/consume C, обоє effects успішно або жоден. Native one-trinket path залишається; no extra strength multiplier. Failure не списує adrenaline і не показує success cue.

### Етап C. Atgeir reflection
1. Scoped successful secondary-attack window: підтверджений Start/concrete intent і фактичне active execution; continuous spin має validated continuation. Starting alone та phantom35 pending виключені.
2. Використати дозволений brief fallback: moving ring/low cylinder на поточному combat radius. Particle sweep лишається косметикою. Пріоритет actual outer sweep не доведений native geometry, тому не обіцяти його без collision evidence.
3. Owner projectile swept-segment collision + wall/native-hit ordering; actual perimeter contact, not body center. Define inside-start/vertical/tangent/moving-defender cases до implementation.
4. Shared Shield35 factory/correlation semantics; sender/projectile authority, actor/window validation, deterministic tie між shield/ring/двома rings. Consume original один раз і spawn return один раз.
5. Preserve safe return payload, source targeting, no ping-pong; transport generated provenance; zero native XP/adrenaline/healthReturn. Missing/dead source — узгоджений Shield35 fallback. Cosmetic collision не авторитет.

### Етап D. Odin's Muster
1. Holding Atgeir → Gold750 committed action receipt → cast snapshot caster + alive non-PvP allied players у35m. PROPOSAL: PvP-enabled caster відхиляється, щоб не обходити exclusion через self. Native distance не визначає ally; за наявності додаткових friend systems потрібен їх explicit contract.
2. Один300s nonstacking/nonrefreshing Muster per recipient; leave range зберігає state, late arrival не додається. Repeated/other caster не refresh existing Muster.
3. +25 HP/+25 stamina aggregate, +10 additive armor; не Heal/refill current bars. Pure max recalc при apply/remove/load. No regen.
4. Small audited beneficial-family registry з explicit Mastery registration і adapters; unknown/negative/mixed/food/item-potion cooldown/Gold-exhaustion/internal timers/Muster виключити. Audit фактичні effects до включення, не назви/іконки.
5. Existing і newly applied eligible concrete instance під Muster отримує remaining+300 один раз. Receipt живе до кінця самого instance, а не тільки Muster. Remove+genuinely new instance може отримати новий grant; refresh same instance — ні.
6. PROPOSAL refresh contract: після першого grant R→R+300; natural base refresh обчислювати без bonus-mutated TTL; remaining=max(previousRemaining,nativeBaseRefreshRemaining), без нового+300. Rested спеціальний adapter; перевірити всі include families. Це finite one-time extension, не timer freeze.
7. Durable recipient application/extension receipts і restore-unexpired; repeated RPC/recovery не повторюють grants. Effect application failures мають явний partial-recipient receipt/recovery policy, не другий Favor debit.
8. Animation лише safe native pose після callback audit; короткий cast pulse/teammate confirmation, no permanent5min aura.

### Етап E. Týr's Second Breath
Головна прогалина: player owner мусить синхронно вирішити lethal, а Gold ledger server-owned. Безкоштовний optimistic60s або пізнє resurrection не відповідають вимогам.

Root PROPOSAL для Gold review: durable prepared one-use admission/escrow.
- Сервер резервує750 доступного Favor та видає authenticated permit world/player/character/session/patron/cooldown epoch. Preparation ще не запускає cooldown і не є completed action.
- Owner READY тільки після ACK. Lethal consumption і revoke серіалізовані: durable local activation receipt ДО HP1. Weak-table/customData без actual completed write не досить. Write failure → відсутність protection.
- Commit idempotent: server atomically consumes hold, debit750, cooldown, consumed marker/result receipt. Lethal activation є logical use irrevocable authorization; delayed physical settlement — окремий reconciliation state, не другий debit.
- Будь-який blocking foreign/Tyr Gold transition вимагає terminal revoke: UNUSED/REVOKED або CONSUMED receipt. Простого ACK «видалив token» недостатньо.
- Timeout/disconnect не refund possibly consumed hold, не видає новий token до reconciliation. UNKNOWN/QUARANTINED state й recovery procedure обов'язкові; permanent frozen funds не приховувати.
- Cooldown epoch включає всі blocking changes/eligibility/session/world; визначити trustworthy activation time для початку20min. Commit receive time після disconnect не є lethal time.
Цей напрям перевірений read-only reviewer і НЕ Є завершеним протоколом. Fatal gates: synchronous durable owner write, crash-safe server commit, terminal revoke, reconciliation і trusted clock. Якщо Gold Core не може забезпечити їх, спочатку змінити архітектуру admission; не підміняти semantics без згоди користувача.

Після contract gate:
1. Scoped finalHP hook після mitigation/інших saves; qualifying normal melee/projectile/DoT/fall/drowning/environmental damage. Admin/direct system SetHealth(0)/OnDeath не intercept. Generated hostile damage не прирівнювати до admin.
2. Consume eligible one-use permit, HP1, state60s, одноразовий debit750/20min. Repeated lethal в одному state лише clamp1, не нова activation.
3. Healing owner sink + audited direct health writes; ticks нормальні/no deferred megaheal. Видалення heal effect не потрібне.
4. Initial tuning speed×1.2; jump vertical×sqrt1.3 однаково ground/air; stamina final normal cost×0.6 з matching affordability. Точне gameplay tuning ще не live підтверджене.
5. Clear/suppress stagger і hostile-hit push; undo тільки originating-hit delta на trigger; voluntary movement/air jump/Maul lock/scripted displacement зберегти.
6. Deadline/state persistence; reconnect не reset60 чи уникнення penalty. Real forced death terminates state, не resurrect corpse; terminal handling в journal.
7. Expiry order: remove protection/modifiers/heal suppression → remove Rested → food.time=min(old,300) → pure caches/max recalculation → HP=0.5×NEW maxHP. Без extra food tick, food deletion, direct strength mutation чи heal callbacks. Поточний Muster maxHP враховується.
8. Terminal penalty один раз/recovery-safe. При реальній смерті не застосовувати HP50% до corpse; відокремити forced-death terminal від нормального expiry.

### Етап F. Перевірки й release boundary
Після authorization: відповідні client/server builds та static Harmony/serialization/compile checks із exact DLL hashes. Потім лише в окремо авторизованій runtime scope disposable character/world, host і remote clients, natural triggers і named matrix нижче. Build success не є gameplay PASS. Deploy/release packaging — окремо.

## 3. Повторно використані системи
- GoldCooldownPolicy, ledger receipt/idempotency/persistence primitives — через GOLD_CORE, не copy-paste crafting admission.
- AttackIntentService та підтверджені Polearms70 lifecycle/radius дані.
- Shield35 source-targeting/correlation/one-reflection provenance; потрібен shared extraction, нинішній private center-return не perimeter API.
- Native SEMan, armor modifier, aggregate max-stat route; audited timer adapters.
- Native jump SE hook; owner healing/stamina sinks із action scopes.
- Cooking35/MasterFeast food rules; pure recalc extraction, не invoke UI/heal posthooks.
- NativePerkAssetResolver, NativeSoftVisualAssets, VfxRecipeService, PerkNativeFeedback, VfxPool, PerkAudioService. Ніякого нового per-perk loader/pool.
- GoldCharacterSave як перевірка completed save, не як готовий distributed combat journal.

## 4. Нові shared integrations — декларації до майбутніх mutations
ЖОДНА mutation в цьому review не виконана. Наступний implementation task мусить перенести ці PROPOSED SHARED SYSTEM CHANGE записи, exact files, reservations і affected-owner signoff до першої source правки.

| System/files | Причина | Affected owners | Required regression |
|---|---|---|---|
| GoldCraftingLedger/GoldDivineTransactions/GoldFavor*/GoldCharacterSave | Patron wallets, combat action admission, reactive escrow, durable receipts/clock | GOLD_CORE, actual100 consumers, INFRASTRUCTURE | reserve/use/revoke races, crash/reconnect/restart, cooldown brackets, crafting unchanged |
| NetworkSync/OwnerSkillAuthority | Unarmed/Polearms unlock snapshots, authenticated combat RPC/state | GOLD_CORE, INFRASTRUCTURE, combat/magic consumers | spoof/stale/session/world binding, host/remote/headless |
| Native equipment + new scoped second-slot adapter; shared UI if needed | Actual second trinket in every equipment/load path | CORE_UX, XP, relevant combat/item domains | load/death/drop/modifiers/weight/duplicate effects |
| SE integration / timer registry / persistence | Atomic dual activation, Muster one-time extension, Second Breath journal | all affected SE owners, Gold, UX | native refresh/Rested/stacking, rollback/replay, persistence |
| Shield35 factory / PerkRuntimeServices / MasteryEventDispatchPatches | Perimeter returns and transported generated flag | COMBAT35_70, XP, MAGIC, INFRASTRUCTURE | native XP/adrenaline, all subscribers, echo/recursion |
| AttackIntent/Polearms70 lifecycle exposure | Successful window/radius without changing35/70 mechanics | COMBAT35_70 | failed Start, abort, stacks, phantom35 exclusion |
| Damage/Heal/Stamina/Movement integration | Final lethal boundary and nonduplicated modifiers | COMBAT35_70, UTILITY35_70, MAGIC, XP | save precedence, Maul, ground/air jump, costs/reservations |
| Cooking35/MasterFeast pure recalc boundary | Exact terminal food/maxHP without timer/heal side effects | UTILITY35_70/100, MAGIC, combat | food299/300/>300, decay/no-decay, Blood healing absent |
| Shared presentation framework only if unsupported cue needs it | Observer/state cues, hand-follow/trails beyond current anchor support | CORE_UX, shared VFX consumers | lifecycle/pool/async/headless/no gameplay callback |

Root owns shared architecture/integration. Scouts не редагують shared files. Не виправляти побічно старі Shield35/event bugs без окремо записаного scope.

## 5. Невирішені конфлікти й рішення перед implementation
1. Вирішено користувачем: native fullbar versus «75% costs» → auto-proc capacity/threshold=C.
2. Gold synchronous lethal versus asynchronous ledger — реальний architecture blocker. Prepared escrow напрям не є production-ready; Gold owner review та durable protocol необхідні.
3. Native beneficial taxonomy відсутня. Root пропонує small audited family registry/default exclude; початковий перелік actual effects ще треба встановити. Icon/category inference неприйнятний.
4. Same-instance native refresh/Rested TTL overwrite — запропонований finite-credit adapter contract вище; потрібна owner перевірка включених families.
5. Offline time source відсутнє для custom states. Root пропонує offline-elapsing combat deadline з trusted persistence; online-only залишок би дозволяв свідому паузу berserker через logout. Не вибирати випадково з native save behavior. Gold cooldown contract окремий.
6. Exact command pose/warrior vocal/second trinket visual і meaningful presentation alternative — UNKNOWN/не обрані. Потрібна native client discovery й конкретний вибір користувача перед presentation implementation за VANILLA_ASSET_WORKFLOW.
7. Reflect payload m_healthReturn і ring already-inside/vertical edge policy — technical contracts перед factory implementation. Не переносити unintended lifesteal і не називати sphere body-check «perimeter».
Решту tuning можна почати з brief targets; непідтверджене не маскувати як затверджене. Немає потреби знову перепитувати750/60s/300s/формулуC чи погоджений cooldown.

## 6. Regression matrix — НЕ ВИКОНАНО
Tags: GENERATED_HIT_RECURSION, PROJECTILE_OWNER, GOLD_LEDGER, RPC_AUTHORITY, HEADLESS, RECONNECT, SERVER_RESTART. Сценарії українською; цей список не PASS.

| Компонент | Cases | Acceptance |
|---|---|---|
| Slot/adrenaline | один/два, same hash, unsupported B, exact/below/oversized gain, capacity change, reentrant gain | C/capacity; нуль partial effects/debit; no equip-proc/no multiplier |
| Equipment | equip/load/death/drop/reconnect/skill loss, passive SE/weight/durability | stable instances, предмети не губляться, другий slot не зникає мовчки |
| Reaction | melee/projectile/DoT/fall/drowning, existing Jump70/Maul save, simultaneous lethal | final mitigation first; HP1 до death; one750/20min |
| Gold | недостатньо/exact750, own/foreign cooldown, duplicate/late ACK, revoke-use race | same authorized outcome; no free protection/no double debit |
| Crash recovery | owner-write failure, consumed receipt lost in transit, server crash між settlement steps, lost owner state | recovery-safe use/debit/cooldown; explicit quarantined outcome |
| Survival/heal | repeated lethal, HoT/lifesteal/direct heal/remote heal, forced admin death | HP≥1 qualifying; heal0; no queued megaheal/no admin resurrection |
| Mobility/costs | ground/air jump, run/jump/dodge/block/special/swim/build etc., reservations/refunds | modifiers once; affordability=debit; no stamina mint |
| Stagger/force | existing meter, direct/RPC stagger, triggering hit push, voluntary physics/Maul | hostile immunity; controls/choreography remain |
| Exhaustion | food299/300/>300, expired food, Cooking35/MasterFeast/Blood/Muster | min(old,300), no extra tick, pure maxima, HP50% newmax once |
| Window | rejected Start, Starting, first trigger, continuous stacks0..5, release/death/stagger/weapon/scene | protection лише confirmed combat window |
| Geometry | fast segment across ring+body, grazing, moving actor, vertical/inside-start, wall before ring | no tunneling/through-wall; perimeter before body |
| Reflection authority | remote projectile/defender, migration, shield/ring race, two reflectors | one original consumed/one return, authenticated request |
| Generated payload | source missing/dead, marker reconstruction/RPC, elemental payload, AoE/beam | no ping-pong/proc/XP/adrenaline/lifesteal farming |
| Muster recipients | radius boundary35m, leave/late arrival, dead/PvP, two casters | snapshot only, no stack/refresh, one debit |
| Buff timers | existing/new, public/hash/internal/RPC apply, ResetTime, Rested comfort, remove/new | +300 once per instance; no freeze/no refresh mint |
| Buff exclusions/stats | food/negative/mixed/unknown/cooldowns/Muster; other stat modifiers | no excluded extension; +25/+25/+10 once; no refill/regen |
| Timed persistence | logout/reconnect/offline expiry/server restart, duplicate restore | no duration reset/regrant; terminal penalty once |
| Presentation | owner/remote, cold asset, missing/disabled VFX/SFX, headless, scene exit/spam | gameplay незалежне; no Guardian activation, bounded cleanup |

Перед LIVE VERIFIED зафіксувати exact client/server variant hashes, native identity/parity, disposable profile/world/settings і natural-trigger/log/observer evidence. Екран меню, audition або synthetic tests не замінюють gameplay.

## 7. Фактично знайдені native assets і presentation plan
Manifest_extended server SHA256 E2E68ADD2932110DF89022E38C2C518EE8A3566875A2A5226E8BE09A965E2E91.
Усі prefab identities нижче SOURCE VERIFIED тільки за static manifest path/source reference. RUNTIME RESOLVED / LIVE VERIFIED для цих нових cues — НЕ встановлено. Actual audio clips, shapes і headless/client availability не доведені.

| Exact name / type | Native path | Provenance / proposed fit |
|---|---|---|
| sfx_atgeir_attack_secondary / prefab | Assets/GameElements/Items/weapons/_res/atgier/fx/sfx_atgeir_attack_secondary.prefab | Polearm35AutoSpin.cs:181; possible short metal accent, actual clip UNKNOWN |
| fx_StaffShield_Hit / prefab | Assets/GameElements/StatusEffects/effects/shield/fx_StaffShield_Hit.prefab | Blocking35CorrelatedProjectiles.cs:142; short interception/confirmation candidate, appearance UNTESTED |
| fx_GP_Activation / prefab | Assets/GameElements/StatusEffects/effects/fx_GP_Activation.prefab | Manifest identity; activation donor, radial shape UNKNOWN |
| vfx_perfectblock / prefab | Assets/Characters/character_effects/vfx_perfectblock.prefab | VANILLA_ASSET_CATALOG:26; physical flash candidate |
| sfx_perfectblock / prefab | Assets/Audio/sfx/sfx_perfectblock.prefab | Catalog:27 / Shield35:143; impact accent, clip UNKNOWN |
| fx_eikthyr_stomp / prefab | Assets/Characters/Eikthyr/fx/fx_eikthyr_stomp.prefab | Catalog:33; bounded pulse candidate, never spawn damaging creature/attack object |

Можливі варіанти Muster після client audition:
- A: короткий sanitized fx_GP_Activation cast cue + fx_StaffShield_Hit recipient confirmation; sfx_atgeir_attack_secondary metallic accent. Очікувана активація/підтвердження, appearance ще не спостерігався.
- B: bounded sanitized fx_eikthyr_stomp pulse + vfx_perfectblock recipient cue; sfx_perfectblock accent. Очікуваний фізичний command pulse, actual fit UNTESTED.
Точний warrior voice не знайдений; metal accent не підміняє затверджений voice без вибору. Жоден варіант не обрано. Scale/brightness/pitch до audition лише tuning proposals.

FACT NATIVE: Player.StartGuardianPower IL006e використовує gpower, але CharacterAnimEvent.GPower→ActivateGuardianPower змінює native Forsaken buff/cooldown/adrenaline. Raw trigger unsafe. StartEmote/UpdateEmote має emote_<name> machinery; exact safe command name/clip UNKNOWN. Magic70CastAnimation показує scoped callback/pose lifecycle, але staff shield pose не є погодженим Atgeir battlecry.

Tyr: m_adrenalinePopEffects, Character.m_jumpEffects, StatusEffect.TriggerStartEffects — verified native presentation paths, serialized prefab/clip identities UNKNOWN. Не називати їх конкретними Tyr assets. Persistent hand/body attachment/trail support потребує actual anchor audit; shared resolver enum не гарантує implementation усіх anchors.

Початковий PROPOSED presentation budget для майбутньої реалізації:
- Cast/trigger primary cue ≤1s; recipient flash≤0.5s, один на accepted receipt. Не масштабувати particle count пропорційно35m radius.
- Reflection flash≤0.25s, per defender throttle≤4/s; gameplay returns не throttled косметикою.
- One-shot sound один на semantic event, одна активна копія на actor/cue; no continuous5min audio. Remote attenuation/volume/clip після audition.
- Persistent Tyr cue максимум один owned lease/actor; ≤2 hand layers якщо anchors supported; no dynamic light; не використовувати нинішній short VfxPool lease як60s lifecycle без adapter.
- Початково≤128 particles/system,≤256/actor для коротких cues; це budget proposal, не performance PASS. Global overlap budget/runtime profiling ще pending.
- Owner і nearby observers render receipt once; dedicated server не render/play; missing/late asset не змінює action/state. No custom imported assets, donor global mutations чи gameplay controllers у visual clone.
- Scene exit/death/expiry cancel stale callbacks/owned leases; no expired action replay. П'ятихвилинної аури Muster не створювати.

## 8. Ризики окремо

### Atgeir — HIGH
Reflection HIGH: рання collision/ownership race, moving perimeter, native reconstructed HitData, незалежний XP/adrenaline grant, два reflectors. Donor Shield35 скорочує payload work, але не вирішує geometry чи ownership.
Muster MEDIUM-HIGH: taxonomy відсутня, Rested refresh/SE persistence нетривіальні, aggregate max-stat/recipient receipts спільні. Без audited adapters +300 буде або втрачатися, або повторно створюватися.
Presentation: command callback може реально активувати Forsaken; exact cry/pose UNKNOWN. Cosmetic failure не має зривати Favor commit чи gameplay.

### Fists — VERY HIGH для Second Breath; HIGH для Dual
Dual — повна equipment/load integration, effect callback atomicity, capacity-change races. «Просто другий slot» не є достатнім планом.
Second Breath — distributed lethal admission, durable use/commit/revoke/recovery та food/heal/force order. Без Gold contract одна з вимог750-before-survival або synchronous death prevention зламається. Online-only clock створює logout pause; weak persistence дозволяє уникнути penalty.
Найсильніша практична послідовність: shared contracts → Dual → reflection → Muster → reactive save після Gold durability gate. Не починати з красивого VFX чи HP clamp і не видавати partial mechanic за завершений perk.

## Handoff
Review COMPLETE, gameplay implementation не розпочато. Наступний authorized implementation task починається з source drift check та Gold/network/equipment contract review; exact protocol/clock і meaningful presentation selection — gates до відповідних змін. Gold Core володіє wallets/ledger/protocol, Combat100 — effects/action semantics. Root серійно інтегрує shared edits; no deployment/launch/release inference.
