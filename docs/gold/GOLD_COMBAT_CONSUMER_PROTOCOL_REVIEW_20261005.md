# Gold-контракт для Tyr / Odin — протокольний review

2026-10-05. Задача GOLD_CORE_COMBAT_CONSUMER_PROTOCOL_REVIEW_20261005. Аналіз і документація; implementation не авторизовано. Gold root володіє wallet/admission/ledger-контрактом, Combat100 — gameplay effects і їхнім життєвим циклом.

## Вердикт

**SOURCE / PROTOCOL REVIEW COMPLETE. FINAL TRANSACTION CONTRACT INCOMPLETE. NOT IMPLEMENTATION-READY.**

Prepared one-use escrow — придатний напрям для reactive Tyr. Резерв 750 вирішує overspend, але не атомарність порятунку, відкликання дозволу, recovery та початок КД. Поточний crafting-протокол не є готовим generic combat API.

Odin має простішу асинхронну admission, але одна ledger operation не робить застосування ефекту до кількох гравців атомарним. Success boundary та partial-delivery policy потрібні перед реалізацією.

## Погоджені вимоги та джерела

**FACT FROM HUMAN BRIEF:** Tyr — reactive lethal-save, 750 Favor, 60 секунд, без ручної кнопки. Odin — Muster, 750 Favor, 300 секунд. Затверджені ефекти не перепроєктовуються.

Gold policy збережено: cost<=250 — 0; 250<cost<=500 — 300 секунд; cost>500 — 1200 секунд. Активний КД покровителя A блокує всі дії A та foreign B>250; B<=250 дозволено без зміни таймера A. Обидві нові дії коштують 750, отже мають 20-хвилинний Gold КД.

Separate player/world/patron wallets і спільний pool усіх linked skills — погоджений дизайн. Реальні Tyr/Odin wallets ще не реалізовані.

| FACT FROM CURRENT PROJECT | Наслідок |
| --- | --- |
| GoldCooldownPolicy.cs:13–33 | Pure policy підтримує ціни та A/B blocking; це не multi-wallet протокол |
| GoldCraftingLedger.cs:60–67,263–298, version3 | Один Favor/exhaustion/pending; implicit Völundr; debit і timer за saved cost; origin PatronId не зберігається |
| GoldDivineTransactions.cs:10–37,306–352 | Recipe/prefab/station kinds1/2; pending/settled recovery перед новою admission; Crafting-specific validation |
| GoldCraftingService.cs:102–125,143–145 | World UID ledger, online server Time.time cooldown; HasGold вимагає Crafting100 |
| OwnerSkillAuthority.cs:14–18,56–68 | Unarmed snapshot є; Polearms snapshot немає. Authenticated owner identity не доводить lethal time |
| GoldCharacterSave.cs:12–35 | Перевіряє actual completed FileWriter.Finish; не атомізує profile/server ledger або новий combat state |

**FACT REPORTED BY COMBAT100 REVIEW:** native lethal decision синхронний на character owner, після mitigation/інших saves, перед final SetHealth. Native IL нами не перевірявся повторно. Jump/Maul precedence, heal suppression, food/HP terminal order та решта gameplay семантики залишаються Combat100 scope.

Source fingerprints before/after у validation/gold-combat-protocol-review-20261005. Policy A17B6CFDA2A782D8413F85D7B5C14AA732889F0C01A8CD27090934A83911533C; ledger 18DC61D8202585EF556C2FE90F87DC050F525B19856031A88DD3E79B94E82C00; GDT CA7B7424C9760E87A8409A3A5747003045399C06899054BB916A97E77988A276. Не відновлювати source зі старого snapshot.

## Findings

P1 тут означає blocker для implementation acceptance запропонованого протоколу, а не observed bug чинного Crafting gameplay.

| Severity | Проблема | Потрібний контракт |
| --- | --- | --- |
| P1 | Hold не забезпечує synchronous lethal-save | Доставлений permit має one-use authority та доказовий lifecycle; no optimistic protection до admission |
| P1 | Same-Tyr cheap action може пройти після локального consume, до server receipt | Fence для всіх Tyr actions і foreign>250, не лише нових cooldown-starting actions |
| P1 | Receipt-before-HP1 залишає crash window | Визначити irrevocable use, durable effect entitlement і recovery того самого стану |
| P1 | ACK/timeout/missing profile не доводять UNUSED | Release лише після terminal proof; UNKNOWN не refund; практична reconciliation procedure |
| P1 | Client timestamp/epoch не доводять activation time | Окремі trusted effect deadline та Gold cooldown start/online accounting |
| P1 | Scalar/Crafting gates не підтримують combat consumers | Patron-bound balances/unlocks/receipts/origin та Polearms authority |
| P1 | Odin delivery не all-or-none | Один cast/debit; frozen recipients; per-recipient outcomes та once-extension lineage |
| P2 | Reentrant/повільний profile save у damage hook | Actual writer completion не доводить безпечності цього місця виклику |
| P2 | Старий READY після restart/rollback/pruning | Revalidation, generation/tombstone retention; не покладатися лише на 64 settled tokens |
| P2 | Guard може затримувати інші Gold дії до lethal | Явний UX reserve/revoke/unknown; preparation не оголошувати активним 20-хв КД |

## Shared invariants — DESIGN RECOMMENDATION

1. Wallet identity — player/world/patron. Action price, patron, owner binding та generation фіксуються до admission. UI selection не змінює вже дозволену операцію.
2. Hold входить у cap: total Favor = available + held <=1000. При F1000 і hold750 доступно250, total1000. Preparation не створює додаткового місця для накопичення. Revoke звільняє hold один раз, consume зменшує total на750.
3. Максимум один expensive speculative reactive permit на player/world coordinator. Два незалежні delivered750 permits не можуть запускати взаємно блокувальні дії.
4. Один activationId дає максимум один consume, debit750 і origin-Tyr timer. Повторний lethal під чинним60s effect — gameplay handling, не нова фінансова операція.
5. Wallet locks недостатні: permission та cooldown transitions різних patrons серіалізовані одним authoritative coordinator.
6. Доки delivered Tyr permit може бути consumed, всі Tyr actions, включно з0/250, та foreign>250 проходять terminal reconciliation перед admission. B<=250 дозволено за власними funds/eligibility/pending: це безпечно і при UNUSED, і при CONSUMED. Така дія не змінює Tyr timer. Gains не блокуються чужим КД.
7. REVOKED/UNUSED і CONSUMED взаємовиключні для того самого permit generation. Consume і revoke мають одну локальну серіалізовану точку рішення.
8. Server epoch increment, timeout чи disconnect не відкликають доставлений дозвіл миттєво. UNKNOWN не перетворюється на refund або новий expensive permit.
9. Loaded READY не дає автоматичного захисту до server revalidation. Зміна session забороняє новий use старого grant, але не повинна відхиляти законний старий CONSUMED receipt для reconciliation. Потрібне fresh authenticated binding того самого persistent character до frozen claim.
10. Duplicate settlement не debit і не restart1200; duplicate activation не нові60; duplicate terminal completion не повторна penalty.

Поточний singlePending не підходить як blanket block усіх foreign cheap actions. Незмінений exhaustion guard у Crafting Receive — Völundr-only path, не generic foreign admission.

## Tyr: proposed state contract

| Стан | Значення | Перехід |
| --- | --- | --- |
| NONE | Немає hold/permit | Prepare за fresh authority/funds/availability |
| PREPARED | Durable750 hold і frozen permit; без action/CD | Delivery/owner installation або доказове cancel до disclosure |
| READY | Current authenticated one-use grant, required owner write/ACK завершені | Qualifying lethal consume або serialized revoke |
| CONSUMED, unsettled | Irrevocable obligation та durable effect intent; сервер може ще не знати | Replay цього consume до settlement; не refund/new grant |
| SETTLED | Debit750, timer origin/duration і consumed result durable як один server outcome | ACK/recovery того самого activation |
| REVOKED / UNUSED | Durable proof без use для цієї generation | Release once; token більше ніколи не READY |
| UNKNOWN / QUARANTINED | Можливий consume, outcome не доведений | Reconcile; conflict fence збережений; Bcheap за правилами вище |

Durable server hold/guard створюються до disclosure. Owner READY вимагає authenticated grant та підтвердженого збереження потрібного стану. Відсутній ACK не є доказом, що дозвіл не доставлено.

Consume linearization point — після інших native saves, до HP1. Якщо Jump/Maul уже врятували, Tyr750 не використовується. Gold не змінює цю gameplay precedence.

### Receipt перед HP1: незакритий crash

Якщо CONSUMED збережено, але процес упав до HP1, фінансова obligation вже існує, а effect міг не з'явитися. Якщо HP1 до запису, write failure може дати неоплачений порятунок. Окремий marker не робить profile, HP, effect state і server ledger атомарними.

**DESIGN RECOMMENDATION:** irreversible consumption створює один durable activation entitlement. Recovery завершує або відновлює тільки той самий дозволений unexpired state, без нового60s вікна/debit. Receipt та recoverable gameplay snapshot мають узгоджений зміст. Failed write до consumption не дозволяє protection; ambiguous completion потребує quarantine до gameplay/reconciliation.

Це не доведена native save implementation. Paid-but-never-published attempt, відновлення після реальної смерті, гарантоване atomic HP/effect/receipt save та expiry при непевному clock не погоджені мовчки. Success boundary потребує Combat100/root рішення. Synchronous profile save може бути reentrant/повільним; Saving guard просто повертає false при вкладеному save.

### Revoke та conflict admission

Перед конфліктною дією потрібне terminal evidence того самого permit:

- REVOKED/UNUSED: release hold і fresh admission;
- CONSUMED: settle, після чого blocking cooldown відхиляє Tyr/foreign>250;
- UNKNOWN: conflict request не допускається.

«Видалив token із пам'яті» або «отримав revoke» не є terminal proof. Server-only epoch не запобігає use вже доставленого grant старим owner.

Lost/corrupt owner journal, character rollback, older server backup або незв'язана пара saves — recovery gate. Quarantine забезпечує safety, але не availability. Потрібні retention/recovery authority і документована процедура завершення; вічний прихований hold не прийнятний результат. Цей review не авторизує administrative money restore.

## Clock — unresolved design gate

Розділяти:

- Gold1200: чинний online server game-time cooldown;
- Tyr60/Odin300: Combat100 запропонував expiry, що продовжується offline;
- permit/session/generation/lease validity: admission lifecycle.

Authenticated owner UTC не доводить lethal time. Server receipt arrival не є часом lethal; epoch не містить elapsed accounting. GoldCharacterSave не розв'язує clock.

**Conditional DESIGN RECOMMENDATION:** консервативний фінансовий варіант — один1200 timer від durable server settlement; до нього conflict guard закритий. Offline час не віднімається. Це простіше для safety, але latency/disconnect можуть подовжити фактичне обмеження від моменту порятунку. Не називати його точними20min від lethal без погодження.

Якщо необхідний початок від lethal, потрібен trusted activation та online elapsed contract; його нині немає. Offline effect deadline також потребує окремого trusted persisted clock/restart rule. Не підміняти його client DateTime.Now чи залишком у weak-table.

## Odin: admission та recipient recovery — DESIGN RECOMMENDATION

Manual cast може дочекатися server admission. Перевіряються Odin wallet, fresh actor/Polearms authority, availability та frozen valid recipient plan. Combat100 визначає ally/range/nonPvP/eligible effects; Gold забезпечує контракт і financial outcome. Recipe-shaped Crafting wire не копіюється як generic combat API.

Рекомендована **окрема success-boundary choice**: durable committed cast entitlement з frozen recipient plan; debit750 + Odin1200 + result receipt один раз до asynchronous delivery. Невалідний/unsupported cast відхиляється до charge. Після commit recovery продовжує ту саму delivery, не новий paid cast. Partial delivery не дає automatic refund.

Це потребує погодження mechanic owner. Оплачена cast не означає atomic success усіх recipients. Current Crafting charge-after-durable-item не доводить all-or-none group transaction. Альтернатива charge-after-first-durable-Apply потребує власного admission fence до settlement і також не готова.

Обов'язкові delivery semantics:

- Frozen roster не розширюється late join, повторним RPC чи новим range scan. Recipient, який помер/змінив світ/став unavailable, має явний result.
- Recipient receipt окремий від caster financial receipt. Duplicate delivery не refresh300 і не повторює+300.
- Retry/reconnect отримує залишок первинного effect deadline або terminal missed delivery, не нові повні300 секунд. Це конкретна delivery policy для погодження Combat100.
- Extension receipt прив'язана stable effect-instance lineage/generation, не тільки SE hash або CLR object. Вона живе до завершення instance навіть після кінця Muster. Reload не робить той самий effect новим для другого+300.
- Native Save не зберігає всі custom SE автоматично. Потрібні explicit effect/receipt recovery semantics. Gold не обіцяє adapter за назвою/іконкою effect.
- Lost ACK відтворює той самий result. Після partial success немає бездоказового refund/rollback всіх гравців через одного failed peer.

## Terminal gameplay та financial replay

Tyr expiry/forced death і Muster expiry — окремі effect lifecycle transitions, не ще один GoldCommit.

Saved «penalty done» без saved actual food/HP/state може пропустити penalty після crash. Penalty без durable marker може повторитися. Потрібна узгоджена native snapshot/recovery boundary; одного weak-table flag недостатньо.

Gold не змінює порядок Rested/food/HP50%/Muster max stats із Combat brief. Combat100 доводить no-heal/no-resurrection та terminal application. Expired60s effect не очищає1200 Gold timer; offline effect clock не прискорює online Gold cooldown.

## Required test matrix — НЕ ВИКОНАНО

| Сценарій | Обов'язковий результат |
| --- | --- |
| Exact750/<750, hold750 приF1000, gain999→1000 | No overreserve/hidden cap room; інші wallets незалежні |
| Prepare write failure до disclosure | No READY/protection/committed charge |
| Delivery або READY ACK lost | Hold не release як unused; same-generation recovery |
| Owner consume save fail/reentrant save | No protection до proven use |
| Crash після consumed receipt, до HP1 | Defined entitlement/paid outcome; без нових60/debit |
| Lethal vs Tyrcheap0/100/250 | One serialized outcome, cheap не проходить під possibly active Tyr |
| Lethal vs Odin750/Völundr500 | Terminal revoke або consume settlement перед новою admission |
| Bcheap0/250 під Tyr5/20/UNKNOWN | Eligible за власними умовами; Tyr timer не reset |
| Double lethal/same-hit/consume replay | One750/1200/activation |
| Revoke ACK+old owner profile | Старий READY не відновлює право використання |
| Disconnect/missing profile/new session/world switch | No inferred refund/new expensive permit; accept lawful old consume for reconciliation |
| Server crash між debit/CD/terminal steps, ACK lost | One durable outcome, idempotent replay |
| Crafting500/250 під reactive guard | Exact foreign exception, не global all-action block |
| Odin partial delivery/death/late join/retry | One cast/debit, frozen results, no implicit refund/refresh |
| Muster refresh/reload/expiry | Once-extension lineage збережена |
| Tyr expiry/forced death/penalty save failure | Once terminal result, no corpse resurrection/penalty skip |
| Settings-off/skill loss/ownership transfer | Fresh eligibility/fence, no reset funds/receipts |
| Trusted clock/restart/offline | Separate Gold/effect clocks, no invented elapsed credit |

Це перелік майбутніх перевірок, не виконаний test suite. Runtime/crash tests потребують окремої авторизації disposable world/character, host/remote owners і matching client/server builds.

## Handoff

Prepared Tyr напрям зберегти для завершення архітектурного контракту. Implementation acceptance блокують consumption/effect durability, terminal revoke/reconciliation, trusted clocks і missing multi-patron/skill authority.

Головне доповнення до Combat proposal: **same-Tyr cheap action також конфліктує з possibly consumed permit; foreign cheap дозволено.** Не перетворювати escrow на blanket Gold block.

Odin потребує cast entitlement і per-recipient delivery journal. Один debit не доводить all-or-none300s/+300 extension.

Source, core policy, economy rates, Combat records, project/build/runtime/production state не змінено. Review завершений; protocol readiness не заявлено. Наступний дозволений обсяг — окремо авторизоване завершення architecture/failure/clock choices, а не mechanic code через чинний crafting RPC.
