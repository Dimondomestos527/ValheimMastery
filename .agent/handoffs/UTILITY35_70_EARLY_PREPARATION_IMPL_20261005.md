# Handoff — рання підготовка крафту/будівництва
- COMPLETE source/static candidate; LIVE_TEST_REQUIRED. Owner/root UTILITY_35_70. Human approved implementation; install/start was not authorized or performed. All reservations released.
- Changed: WorkshopPreparation/WorkshopPreparationCache (new); WorkshopChestLease; WorkshopRemoteCraft; standalone qc-workshop-preparation; matching task/progress and Utility canonical note. No version/compile-gate/project rename; no Run or chest-flow VFX edits.
- Craft: read vanilla m_craftRecipe/quality/amount/station during active timer. Separate optional token prepares contributing ownership; no debit/output/admission/receipt. Cancel stops further refresh; completed warmth lasts10s. In-flight ownership is allowed to reconcile rather than being aborted unsafely.
- Build: selected piece/ghost shortfall prewarm,150ms debounce;32 scheduling hints retained30s; ownership30s;4s refresh, stops after30s stable inactivity; actual placement/change resumes. No arbitrary wood/stone/marble basket or quantity reservation.
- Bounds:8 prep contributors/actor;128 global warmth/unfinished prep cap;64 candidate decodes per optional request;500ms server actor rate. Capacity/legacy misses fall back to ordinary actual request.
- Protocol4 enables preparation; clients retry legacy3 if old server rejects4. New server still accepts3. Only negotiated4 chest owners receive prep handoff op2/op3. Actual craft/build packet and economic receipt format unchanged.
- Real action queues one actor-bound waiter before ledger admission while preparation completes. It always rechecks actor, current personal/chest stock, station, ward, placement and exact native bytes/CAS. No cached stock is spendable credit. Crafting35 preservation and existing output/rollback remain on actual execution path.
- Failure handling: partial transferred entries retain origin identity and authentic ACK/native-byte reconciliation. Client retries positive snapshot ACK after relinquishment; never reports ordinary refusal after owner transfer. Confirmed disconnected origin leaves current native world items intact.20s uncertain retirement becomes persisted logged prep quarantine;5s recovery accepts exact acknowledged bytes or disconnected origin once server owns record, without restoring any snapshot. Recovery/late handback uses explicit WarmOpen/Container.Load; original-start+18s limits direct handback within existing20s freeze. Opening retries until existing4s deadline.
- Auto-review rejected60s freeze extension; that patch did not apply. Safer18s direct-handback cutoff was implemented. No remaining blocked action.

## Перевірено
- Final evidence: validation/utility-early-preparation-20261005-accepted/. Dual Build1230 errors;32 dependency fingerprints/provenance PASS.27 preparation checks per variant,60 Workshop boundary checks per variant,61 atomic managed checks; all qc-results.csv exits0.
- Native evidence in validation/utility-early-preparation-20261005/native-*.txt: OnCraftPressed freezes recipe/timer; Disconnect removes ZDO/routed peers and disposes socket/RPC; Container.Load reads native items on data revision change.
- Bounded read-only utility_prep_review reviewed initial hazards and final source; no remaining integration blocker. Root integrated all writes. No source changes after accepted build.
- Candidate client: bin/Perks123Client/ValheimMastery.dll SHA256 F9F41E639A2ED33D9B6E369EDDFE88B1634A3DBF25CD468E35E1D28D90FCB4BE.
- Candidate server: bin/Perks123Server/ValheimMastery.dll SHA256 7893F14F8AC2F9D7DE3EC08B18B2DF80682F7D056F543E98334CF1DB903D9E06.
- Both include current other-owner source; do not replace with older global source snapshots. Hashes must be rechecked before any later installation because candidate paths are shared build outputs.

## Наступна live перевірка після окремо погодженого встановлення
1. На однаковій мережі скринь порівняти перший крафт і першу placement; відокремити час native timer від timer-complete→output. Очікувати early preparation ready до actual request; фінальний RTT може лишитися.
2. Cancel до ACK/після ACK, швидкий retry, інший рецепт, A→B→A; до успішного actual output баланс інвентарів не змінюється.
3. Натиснути build/дочекатися craft completion під час handoff: дія очікує, а не отримує prep-busy denial; debit/result рівно один.
4. Два гравці/одна скриня, private/unowned/ward, змінений запас, особиста кількість, full inventory, Crafting35 preserve: поточне фактичне списання та результат/rollback правильні.
5. Одна скриня приймає, інша відмовляє; disconnect після relinquishment; запізнілий ACK/native replication; відкриття підготовленої/відновленої скрині завантажує current items і не втрачає їх. Після quarantine перевірити журнал і documented recovery; не стирати markers/запас вручну.
6. Idle/закриття інструмента, teleport/death, reconnect/restart; optional prep не лишає невидимий Held, кеш не переносить дозвіл між персонажами.
- Runtime outcomes and first-action speed for this candidate are not LIVE VERIFIED; historical534/237ms cold traces are baseline evidence only.
