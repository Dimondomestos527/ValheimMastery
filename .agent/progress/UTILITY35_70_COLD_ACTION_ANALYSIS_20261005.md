# UTILITY35_70_COLD_ACTION_ANALYSIS_20261005 progress
Phase BASELINE / READ-ONLY. Status COMPLETE for evidence/design investigation.
Weighted phases, sum100: current scope/version/log pairing20 (1/1); timing calculation/source attribution40 (1/1); cache and chosen native Run design30 (1/1); final handoff/readback10 (1/1). Total100% of analysis, not implementation or live acceptance.
Reservations: matching three lifecycle documents only; released after readback. No source/shared writes.

## Нові докази 2026-10-05
Попередній звіт про відсутність WorkshopTiming samples тепер історичний: користувач виконав нові тести. У нових логах знайдено 9 пар client/server transactions: 2 craft + 7 build. Обидва логи оголошують 1.4.123. Версія не доводить незмінність binary; hashes поточних DLL прочитані, але стартового hash саме цього run немає. Native game version/кадровий профіль/пінг поза echo невідомі.
- Client log: C:\ValheimModDev\BepInEx\LogOutput.log; 32603 bytes; SHA256 7282756F28AE62F9CF20B304C1C0651E7A9CF077132572737CB9EB7427A8E3FF.
- Server log: server-root/BepInEx/LogOutput.log; 21383 bytes; SHA256 22DF1B5C94BD65B8871C7464B2B0C213DD1EF2F9897A2674DD5F5C274ADA9B02.
Source authority: поточні src-modern + csproj/plugin 1.4.123. Source/build/config/runtime/state не редаговані цією задачею.

## Розрахунок
Кожна остання trace line індексується за (transaction id, side), рецепт прив'язаний до server received id. Не віднімаємо clocks різних hosts.
- результат для гравця = client T8.actionObserved - T0.request;
- підтвердження settlement = client T9.confirmed - T0.request;
- сервер до grant = Sgrant.sent - S0.received;
- інтервал валідації до станка = Sstation.begin - Sjournal.admitted;
- proposal = T3.availabilityProposal - T2.containersResolved;
- ownership = T4.ownershipReady - T3.availabilityProposal;
- final plan = T5.finalPlan - T4.ownershipReady;
- debit persistence = T7.removalPersisted - T6.removalStart.
Echo RTT вимірюється client-side; окремий RPC може містити frame scheduling і server stalls, тому це не чиста мережна затримка. Не віднімаємо RTT як гарантований additive network component.

| Transaction id prefix | Дія | Result ms | Settlement ms | Server grant ms | Ownership ms | Proposal ms | Final plan ms | Debit ms | Leased / debited chests |
|---|---|---:|---:|---:|---:|---:|---:|---:|---|
| 99da1470 | BoltBone перший | 533.715 | 837.247 | 395.476 | 170.086 | 77.655 | 14.275 | 9.449 | 1 / 1 |
| 3869aa5d | BoltBone наступний | 219.824 | 333.508 | 109.915 | 0.009 | 38.124 | 7.274 | 0.179 | 0 / 1 |
| c60469d8 | stone_wall_2x1 перший | 236.826 | 441.948 | 137.159 | 74.129 | 9.798 | 4.741 | 0.136 | 1 / 1 |
| e202ca75 | stone_wall_2x1 повтор | 171.855 | 267.550 | 53.514 | 0.007 | 4.711 | 3.906 | 0.201 | 0 / 1 |
| 62a04731 | stone_wall_2x1 повтор | 86.926 | 184.199 | 8.832 | 0.005 | 2.590 | 3.873 | 0.133 | 0 / 1 |
| 2b5fc922 | stone_wall_2x1 повтор | 166.495 | 331.092 | 59.181 | 0.009 | 5.487 | 5.273 | 0.126 | 0 / 1 |
| aff23d9d | stone_wall_4x2 зміна | 358.784 | 711.624 | 257.265 | 142.758 | 37.654 | 17.148 | 0.208 | 1 / 2 |
| 3234069d | stone_wall_2x1 повернення | 208.955 | 437.263 | 108.935 | 0.009 | 50.482 | 9.954 | 0.126 | 0 / 1 |
| a5632dce | stone_wall_2x1 повтор | 135.391 | 246.155 | 41.830 | 0.004 | 27.608 | 11.669 | 0.139 | 0 / 1 |

В усіх 9 client traces successfulOutput=1, server result=committed. Це narrow happy-path evidence: не доводить правильні до/після stock counts, rollback, конкуренцію чи exactly-once crash recovery. Reconciled-world-escrow lines після committed самі по собі не доводять повторне списання.

## Висновки щодо швидкості
Перший cold крафт має додаткове ownership очікування170 ms і proposal78 ms. Повторний не передає ownership. Cold build має74 ms ownership; switch to 4x2 —143 ms ownership і дві фактично використані скрині. Отже changing piece не обов'язково скидає підготовку: новий матеріал/джерело може потребувати ще однієї remote-owner скрині. Return to old2x1 все ще leased=0.
Sstation.ready - Sstation.begin для першого craft =1.830 ms, для всіх build <0.04 ms: station proof вже прогрітий/не потрібний і не є головною затримкою цих samples. Не оптимізувати station-proof TTL навмання.
Sjournal.admitted - S0.received first craft=28.867 ms, subsequent=2.236 ms. Read/flush journal cold може бути частиною цього інтервалу, але він містить іншу admission роботу. Не прибирати write-ahead durable flush.
Sjournal→Sstation для шести дій ~40–69 ms, для двох повторів <0.1 ms. Поточний WorkshopWorldRecords.Refresh раз/сек перебудовує metadata всіх ZDO, station levels і connected components; eligibility/capability звертаються до нього у цьому інтервалі. Це source-backed кандидат, але бракує окремого Refresh timer для доказу точного contribution.
Proposal2.6–77.7 ms: WorkshopChestLease.Acquire для candidates створює WorkshopRecordStore, який робить Inventory.Load для перевірених скринь до знаходження достатнього stock. Eligible=38 у всіх samples; leased count не означає decode усіх38. Високі variance/JIT/GC/frame causes не відокремлені telemetry. FinalPlan4–17 ms декодує потрібні скрині повторно. Warm debit persistence0.126–0.208 ms — не ціль скорочення безпекових перевірок.

## Конкретний bounded cache proposal
1. Продовжити вже існуючий WorkshopChestLease.KeepWarm TTL з8 до30 seconds після використання. Поточний limit128 зберегти; негайний release при відкритті, втраті актора/з'єднання/прав, death/teleport, виході за8m action point; escrow/quarantine/pending заборони лишаються. Наявна структура per-ZDO вже зберігає кілька скринь, не лише останній piece. Це допоможе A→B→A в одному місці, але не прибере handshake першої remote-owner скрині за життя session.
2. Підготовка кількох останніх building pieces: TTL30s, LRU32 per actor, невеликий global cap; лише static prefab/requirements lookup та список candidate source ids як hints. Вартість/режим/станок/wards/current actor й stock перевіряються на кожну реальну дію. Перемикання piece не очищує решту entry. Session/ObjectDB/world lifecycle change очищує кеш; видалений object/owner/stock change invalidates відповідну підготовку.
3. Snapshot-count cache для ownership proposal: immutable item-count map keyed by ZDO identity + exact native item bytes. TTL30s, global256 entries з memory cap8MiB; byte snapshot comparison і current identity/permissions/ownership/locks на кожен read; при змінах eviction/redecode. Це пропозиція для скорочення repeated Inventory.Load, не кеш дозволу списати. Final transactional adapter/Capture/CanWrite/CAS/rollback перевірки зберегти.
4. Для справді першої дії: попередньо підготувати metadata/count hints при виборі recipe/build piece, throttled і bounded, до кліку; це не debit і не захоплення всіх сусідніх скринь. Ownership pre-acquire тільки needed contributors був би окремим складнішим RPC design: може вплинути на відкриття скринь та інших гравців; не включати без окремого review. Перший ownership RTT неможливо гарантовано прибрати простим кешем, якого ще не існує.
5. Рознести Refresh timing і reuse metadata indexes у WorldRecords; не продовжувати TTL доступу/wards/станка сліпо. Оптимізувати cached navigation, не авторизацію. Порівняти same-material і changed-material cold/warm/A-B-A latency після зміни; не обіцяти zero latency при RTT82–330 ms у цих samples.

## PRESENTATION / VANILLA ASSET PLAN: вибір користувача
Intent: 3 чітко відмінні Run phase transitions; одна дискретна подія на фазу, без continuous trail. User selected32/max/same max + vertical double-jump-style ring. Це дизайн-вибір уже отримано, повторний вибір asset не потрібний.
Donor: fx_perfectdodge, Assets/Effects/fx_perfectdodge.prefab (catalog A02), native pixel_additive stretched streaks + ring_gradient ring; NativeMovementPulse вже відтворює ці ж native layers для double jump. Current legacy Run resolves цей prefab, хоча recipe label fx_land. Preserve materials/textures; no custom/imported assets.
Source budgets: NativeLandingBurst Run cap64; NativeMovementPulse jump cap81. Це software safeguards, не engine max. Desired explicit budget: phase1 =32 streaks, no ring; phase2 =64 streaks, no ring; phase3 =64 streaks +1 native ring, aggregate65. Не покладатися лише на emission multiplier: явні caps/counts потрібні через saturation і pooled state.
Phase3 ring — вертикальна площина, нормаль вздовж напрямку руху (upright expanding hoop), центр біля середини тіла; outward expansion у цій площині, одноразова transition cue. Це трактування користувацького «по вертикалі»; не підміняти іншою формою/asset. Renderer alignment/startRotation3D/source ring plane треба перевірити на cloned instance; одного root quaternion недостатньо без перевірки mode. Double-jump горизонтальна геометрія залишається своїм cue.
Зберегти counts2/3 однаковими; розрізнення phase3 забезпечує native ring. Provisional lifetime близько0.5–0.8s і bounded pooled lease; final scale/orientation визначається live preview. Native source/pool не змінювати глобально. Per-actor phase1→2→3 emits only once at3/5/7s, reset clears pending; VFX off/headless skip presentation; late async callbacks не грають expired phase. SFX existing without extra double-jump audio.
Current asset/source identity підтверджені existing catalog/source + попередні runtime traces; новий vertical ring/count contract НЕ IMPLEMENTED/НЕ LIVE VERIFIED. Обсяг перспективної зміни: Utility-owned Run composition/class/recipe entries; shared VfxRecipe/pool infrastructure у разі зміни потребує SHARED SYSTEM CHANGE + affected-owner review/root integration.

## Наступна дія
User relaxed chest-flow implementation priority: не витрачати час на settlement cosmetic RPC зараз; не показувати всі nearby chests як нібито actual debit.
Read-only analysis завершений. Для source implementation потрібне explicit switch початкового BASELINE / READ-ONLY правила. Concrete ready scope: bounded Workshop warm/count/navigation cache + exact32/64/64+vertical native ring; static/QC/build acceptance обох variants; deploy/launch окремо, production state protected.
