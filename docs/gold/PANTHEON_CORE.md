# Canonical source-derived index

## Current P key and station crafting 2026-10-05 — source/static accepted

GOLD_CORE_WEAPON_KEY_P_20261005: shared manual weapon input default P supersedes historical F8 statements below. Retired ActiveAbilityKey F7/F8 values visibly migrate to P at initialization/setting change; None/other custom keys survive. RegisterManual(id, weaponSkill, selected, tryStart) routes only the native current weapon visibly held in right/left hand, filters skill before pure selection and rechecks item instance+skill before coordinator. No match shows localized no-special-ability feedback, no financial admission; rejection/unknown never falls through. Reactive/passive perks and station/recipe crafting do not register manual actions. Input exact02D3AD6D4ABC1AC491A861C7C33B8D6FD1EC775B23C669BCB758DA8B6D73AC30 source integrated after Combat/UX approval and94 linked assertions/dual isolated builds. No actual manual coordinators yet; not native ability/live acceptance.

Crafting F7 binding/dispatch is now removed; Forge uses its explicit station button intent, preserving250Favor/Hammer/required inventory idol/same-item+3/variant/receipts. Kind1 creation remains its own500Favor station button; native MasterIdol recipes remain their own construction flow. Ordinary native craft is not an implicit Gold transaction. Forge freezes selected item reference/quality/item variant separately from GUI variant/recipe/player/session/station; context change/close/cancel clears intent before admission. Cloned button focus returns to active/interactable native craft fallback or clears before hide/disable/destroy. Both former F7/F8 key paths are retired; only migration references remain.

Exact final Utility domain and UX approvals,94 linked input/56 extracted intent+policy/11 extracted focus checks, Gold125+existing/receipts,34 compiled Gold100 checks+static-only notice per variant, both final native-reference builds/provenance32/4 PASS. Final5source integration/readback matches compiled candidate; report validation/gold-weapon-key-p-20261005/FINAL_REPORT_UA.md and task/handoff govern current scope. No DLL install/game launch/production state writes. Live native UI/controller/Forge/payment/reconnect/crash evidence remains required separately; shared Gold world-bound accessor/settings split remain separate tracked work.

As of2026-10-04,1.4.123. Read CONTEXT_AND_STATUS and KNOWN_ISSUES. This is a concise source navigation/status placeholder, not a full gameplay specification. Exact mechanics require current source and compile gates; no new design approval. Gameplay regression DEFERRED. No source presence promoted to LIVE VERIFIED.

## Status: MIXED
IMPLEMENTED IN SOURCE: GoldCraftingLedger, GoldFavorModel/Service/Telemetry, GoldReceiptPolicy, GoldCharacterSave and shared transaction/ACK/UI portions of GoldCraftingService/GoldDivineTransactions/GoldPantheonUi.
STATIC VERIFIED: existing Gold QC evidence, some historical assertions stale; do not claim all old tests passed.
LIVE VERIFIED: full cap/transaction/crash-recovery matrix pending.
Owner GOLD_CORE; root shared integration. Utility100 owns Crafting100 action gameplay, not the shared receipt protocol.
Protect world-UID ledger and player receipt state as consistent pair. Favor set command is not full reset. Arming rules/costs read current source, not old screenshot expectation.
Regression: 999→1000/overflow, auth/idempotency, pending/commit/save order/reconnect/restart; explicit separately scoped disposable crash scenarios. No tests now.

## Approved patron Favor policy — 2026-10-05

Status: shared wallet/coordinator IMPLEMENTED IN SOURCE / STATIC VERIFIED under GOLD_CORE_MULTIPATRON_LEDGER_20261005 below. Skill-specific earnings/unlocks/combat invocation remain pending; no LIVE VERIFIED claim. The original user decision remains authoritative.

User decision: «Різні шкали Favor для різних покровителів, якщо навички мають одного покровителя, то заповнюють її всі вони і так само спільно витрачають».

- Favor belongs to a patron for the individual player in the existing world-scoped state. Different patrons have separate balances and scales; this decision does not pool balances across players or worlds.
- All skills associated with one patron contribute to the same patron pool. Their eligible actions spend from that same pool; no separate skill balance and no duplicated or mirrored Favor.
- Pantheon displays one Favor scale per patron. Multiple associated skills share that scale and may expose their own actions; adding a skill for the same patron does not add another balance/scale.
- Example: skills S1 and S2 belong to patron A, S3 to patron B. Starting at zero, S1 awards100 and S2 awards60: A=160. An S2 action costs40: A=120. B stays unchanged throughout; an S3 award/action belongs only to B.

Shared implementation requirements: stable PatronId and explicit skill/action-to-patron routing; world/player/patron balance identity; exact patron binding frozen into an admitted transaction so retry/recovery or mapping changes cannot debit another patron. Wallet identity, frozen generic grants, funding/conflicts and legacy-compatible migration are now implemented in the foundation below. Actual skill/action registration, award validation and combat owner-journal integration remain the corresponding consumer integration scope. Root owns shared design/integration with affected action/UI owners.

Required future tests: two skills award/spend from one patron; another patron remains unchanged; one UI scale per patron; simultaneous actions from associated skills cannot overspend or double debit; per-patron cap boundaries; replay/crash/reconnect/world/player isolation and save/rollback consistency; correct patron displayed and debited after skill/action selection changes. Test Tyr UI fixture is synthetic and cannot satisfy these authoritative multi-patron tests.

Not decided by the Favor rule alone: exhaustion/cooldown (resolved below); skill-level/Gold-unlock eligibility for contributing awards; conversion/rates/anti-farming budgets; new cap values/unlock policy. The foundation retains cap1000 and creates no automatic rewards/unlocks. Legacy Favor/Unlocked now alias the canonical Völundr wallet rather than defining a shared cross-patron balance.

Decision/task: .agent/tasks/gold/GOLD_CORE_PATRON_FAVOR_POLICY_20261005.md; matching progress/handoff. Human «Продовжуй реалізацію» authorized the source/static foundation below; runtime/deployment remain separately scoped.

## Погоджена політика КД усіх Gold-дій — 2026-10-05

Людське уточнення: «в межах одного покровителя кд для всього, а для інших покровителів тільки на дії після 250 вартості».

| Зарезервована ціна дії | КД після успішного Commit |
| --- | --- |
| 0–250 Favor включно | 0 |
| >250–500 Favor включно | 300 секунд / 5 хв |
| >500 Favor | Попередні 1200 секунд / 20 хв |

Активний спільний таймер має покровителя-джерело A. Він блокує всі дії A незалежно від ціни. Для інших покровителів B він блокує лише дії дорожчі за250; B<=250 дозволено, і така дія не скидає та не подовжує таймер A. Після завершення таймера діють звичайні перевірки. Ціна береться з авторитетного збереженого pending, а покровитель — з авторитетного контракту, не вибраної UI-картки.

IMPLEMENTED IN SOURCE / STATIC VERIFIED, вузький обсяг: GoldCooldownPolicy централізує тривалість, admission і збереження активного таймера; GoldCraftingLedger застосовує її до нинішнього Völundr-only стану. Masterwork500 отримує5 хв, Forge250 —0 власного КД; активний КД Völundr блокує також Forge250. Pending/unlock/Favor/auth/receipt/rollback захисти не прибрано. Форматv3 і верхня межа завантаження1200 незмінні; старі активні таймери не скорочуються заднім числом.

Виняток іншого покровителя STATIC VERIFIED лише як pure policy model. Поточний ledger має один Favor scalar і не зберігає окремого PatronId джерела виснаження; його історичний покровитель неявно Völundr. Другий реальний покровитель потребує окремої інтеграції patron wallets, прив'язки дії/pending/receipt до PatronId, збереження походження таймера й сумісної міграції. Виклик policy сам по собі не дозволяє списувати Favor іншого покровителя з Völundr wallet.

Докази: validation/gold-cost-tier-cooldown-20261005/verification-results.csv, qc-gold-ledger.log, qc-gold-transactions.log, snapshot/validation/gold-cooldown-build/ та REVIEW_UA.txt. Обидві окремі збірки успішні; ledger/receipt QC і read-only persistence review прийняті. Не встановлено й не запущено; LIVE VERIFIED не заявлено. Подальша інтеграція Utility100 повинна використовувати ці GoldCooldownPolicy/ledger і нові очікування, а не стару frozen20min версію.

Задача/прогрес/передача: GOLD_CORE_COST_TIER_COOLDOWN_20261005. Для живого тестування залишаються countdown/UI, дві навички одного покровителя, різні реальні покровителі, відключені settings, два гравці, reconnect/restart, worldUID, character/server save order, дозволені disposable crash windows і debt/anti-farming/rollback consistency. Ці сценарії не підтверджуються чистими моделями або успішною компіляцією.

## Shared multi-patron foundation — 2026-10-05

IMPLEMENTED IN SOURCE / STATIC VERIFIED, human «Продовжуй реалізацію». GOLD_CORE_MULTIPATRON_LEDGER_20261005 supersedes the earlier v3-only implementation limits above; old task evidence remains historical.

- GoldCraftingLedger + GoldPatronLedger + GoldPatronState implement bounded world/player/patron wallets and schema4. v1/v2/v3 map only to Völundr, preserving balance/unlock/background receipts/pending/remaining cooldown. Legacy properties alias the same canonical wallet; no duplicated balance.
- Generic ReserveAction/SettleAction freezes patron/action/token/cost/generation/prepared. Shared funding/conflict gates cover legacy/manual/prepared claims. Total includes held<=1000; Available=total-held. Persisted origin enforces the approved same/foreign250 rule. Commit/replay does not restart another patron timer.
- Coordinator-only Award performs all split wallet increments AND durable source/sequence receipt in one Gold save.64-source/64-sequence replay window per player/world; duplicate/expired does not gain. Cap consumes receipt, no overflow bank. Producer must persist stable event/source/sequence/contribution split; no raw client award RPC or new earning rates implemented here.
- Prepared grants are accounting/fences only. Settlement requires future authenticated durable IGoldPreparedEvidence; no native protection, verifier/RPC, timeout refund or trusted lethal/effect clock supplied. Financial cooldown starts at durable commit as existing behavior. Combat100 integration gates remain in GOLD_COMBAT_CONSUMER_PROTOCOL_REVIEW_20261005.
- Current Crafting shared gates use Available and exact patron policy; existing effect/receipt settlement preserved. Authenticated bounded world/player wallet snapshots parse completely before apply; reset/fault/stale-state invalidation. Fixed-height scrolling real-wallet cards and explicit held funds; synthetic Tyr remains local fixture. UI/native cues/arm removal in separate Utility100 candidate not integrated by this task.

Verification: accepted ledgerQC125 new patron/award assertions plus existing suite; receiptQC PASS; both isolated Build123 variants/provenance32 fingerprints/4 generated inputs PASS;27 adapted isolated compiled Crafting100 checks per variant; two read-only reviews APPROVED. Evidence validation/gold-multipatron-20261005/accepted/, compiled-qc/*-accepted.log, integration-manifest.csv, REVIEW_UA.md, REPORT_UA.md, API_UA.md.

No install/game/server launch/production migration. Actual combat unlocks/earnings/effects, independent combat settings/lifecycle, producer journal/authority and native crash/reconnect/UI tests remain pending. Schema4 compatibility applies to the accepted final build, not earlier unreleased pre-award v4 evidence files. Never split rollback client/server/world/character state.


## Historical shared manual input foundation — superseded by P above

GOLD_CORE_PANTHEON_INPUT_IMPLEMENT_20261005 COMPLETE. At that checkpoint PantheonActiveInput/MasteryPlugin implemented configurable common F8 input, immutable startup registry, exclusive one-action dispatch/no fallback, native focus/build-menu checks, bilingual ambiguity message, deferred callback-time shutdown and transactional startup/core cleanup. Crafting F7 was unchanged at that checkpoint; reactive Tyr excluded. Combat100 transfer/input review and Core UX final input/lifecycle review APPROVED; root serial integration only. Current key/Forge contract is the P section above, not this historical checkpoint.

There are no manual coordinators registered yet: framework presence does not implement Odin or any other active perk. Each future coordinator must pass pure selection/Gold authority/receipt/effect recovery review and register at an agreed startup slot BEFORE FreezeRegistration. False/exception does not authorize refund or alternate action; Accepted is only coordinator admission, exception is OutcomeUnknown. Existing Gold service lifecycle still uses Crafting configuration until separately reviewed Combat settings split.

Evidence validation/gold-pantheon-input-20261005/FINAL_REPORT_UA.md and integrated/:70 linked input assertions, Gold125/existing suite and receipt QC, both native-reference builds and32/4provenance PASS. Exact inputCF14678B350C1E8AB45AB38161339719FF9AA3B4863000F53FE2935CE98CD04E, pluginA5C4FD5926896193136DE0EF2F3FF4575E53E10222C5DD546355C4FD3C770EF2 equal frozen compiled sources. No install/launch/live acceptance. Key remap/controller/focus/two-owner/reconnect/runtime crash matrix remains separately authorized future work.

## Current shared authority/lifecycle — 2026-10-05

GOLD_CORE_AUTHORITY_LIFECYCLE_20261005 COMPLETE / SOURCE STATIC VERIFIED. This supersedes the historical Crafting-dependent shared service gate described above. CoreEnabled (Pantheon.EnableGoldCore + global Mastery)/CoreReady independent of CraftingEnabled; legacy Enabled remains Crafting-only. Craft OFF leaves shared wallets/exhaustion/snapshots and exact terminal receipt recovery active, while new craft actions/XP/unlock stay blocked. Shared UI requires current authoritative CoreReady and fresh snapshot. Exhaustion marker is informational for the active patron, not a universal prohibition: foreign-patron actions costing at most250 remain subject to the existing exemption.

Fresh `TryGetServerLedger(expectedSession,expectedWorld,out ledger)` requires enabled core and current loaded server session/world UID. Before delayed fresh mutation recheck CoreEnabled/current ledger identity. `TryGetRecoveryLedger`, `IsServerLedgerCurrent` and compatibility `ServerLedger` provide current-world recovery storage ONLY, never fresh income/action permission. Accessors fail closed before Tick after session/world replacement; they do not initialize or rebind storage. Deferred grants bind session/world/character before journal access; Requested effects require fresh DivineActionsReady, terminal Applied/Rejected/settled replay remains available when disabled. Native item payment/rollback, prices and schema4 unchanged; private Idol Session/_path contract preserved.

Wallet snapshotV2 retains legacy crafting-ready header and adds shared-core-ready; world/player/full packet validation retained. V1 conservative fallback supported, old readers rejectV2; use paired updated client/server DLLs. Separate native authenticated PolearmsV1 channel carries world/character/actual owner level, revalidates current peer/session/world/native identity and age0..10. Owner testimony is not server-owned progression and grants no XP/Favor/unlock/effects. Existing skill channels unchanged. Local actor/session guards remain consumer obligations; same-world/same-character delivery has no session nonce/sequence.

Exact canonical sources GCS3DFFE0BA..., GDT2A26B82C..., statusD3A7ED79..., authorityC800E0B7..., compiledQC D8809DB9... match approved+tested readbacks. Combat100/UX/Utility100 and2 bounded read-only reviewers accepted relevant source; Combat/Utility accepted the stronger compiled-QC update.126 actual-source behavioral assertions, Gold125+existing/receipt suites,17 unchangedpayment/legacy methods, both frozen native builds0errors/provenance32+4 and39 compiledPASS entries/variant passed. Evidence/API/runtime matrix: validation/gold-authority-lifecycle-20261005/REPORT_UA.md and integration-final.csv. Concurrent unrelated Combat edits after frozen build are recorded separately; this is not global release acceptance. No installation/game/server launch/production saves. Runtime multi-patron UI, reconnect/restart/two-player/config toggles/disposable crash/save-order tests remain LIVE_TEST_REQUIRED, and actual Combat100 consumer/gameplay wiring belongs to its owner.

## Current shared financial result retention — 2026-10-05

GOLD_CORE_RESULT_ACK_FENCE_20261005 COMPLETE / SOURCE STATIC VERIFIED. Gold ledger schema5 retains bounded exact CoordinatorPins per player/patron. Versions1–4 read with empty pins. Manual ReserveAction(player,grant,requireCoordinatorAck:true) atomically reserves and pins a nonprepared action; existing callers defaultfalse. PinAction upgrades only exact retained nonprepared claim/result. AcknowledgeAction(player,grant,Committed/Unused) releases an exact financial-result pin only after the trusted consumer has durably recorded its own matching outcome. ACK is not recipient delivery or proof that a native effect was applied.

Pins survive settlement, prevent new same-patron admissions and result replacement until ACK, and add no Held funds, debit, cooldown or foreign-patron conflict. Income, cap and existing foreign-patron rules remain unchanged. Unknown/storage faults never authorize inferred settlement, timeout release or automatic refund. Exact duplicate ACK is idempotent while the matching result is retained; stale ACK cannot release another action's pin. Old reader schema4 cannot read5: future deployment/rollback must preserve coupled Gold and consumer journals, world/character consistency and paired binaries.

Consumer obligations: opt-in initial reserve; exact existing-evidence recovery only; durable own Committed/Cancelled before financial ACK; recovery cleanup includes already Committed/Finished/Cancelled entries, not only Pending. Abandoned is not admitted payment. After ACK/result replacement, own durable paid journal preserves entitlement without new token/charge/refund. First native application/restoration still requires CoreReady and retains original absolute expiry when disabled. On explicit input blocked by outstanding confirmation, display a localized explanation such as «Попередня божественна дія ще підтверджується»; do not claim insufficient Favor/cooldown or inflate Held. Shared Pantheon remains visible and other patrons independent under their normal rules.

Canonical ledger3A53EADF…E482E/patron7251F259…45B9A equal approved and frozen compiled candidates (integration-final.csv).135 focused actual-source assertions, existing Gold125/receipts,2 read-only protocol/persistence reviews, client/server native builds0errors/provenance32+4 and39 compiledPASS entries/variant passed. Combat100, Utility100 and UX exact-source acceptances received. Evidence/API contract: validation/gold-result-ack-fence-20261005/REPORT_UA.md. This shared dependency is released; Combat consumer/bootstrap/native effects are separately owned/reviewed. No installation, production migration or LIVE verification. Real crash windows, save order, restart/reconnect, two-player authority, disabled settings and multi-patron UI remain LIVE_TEST_REQUIRED.
