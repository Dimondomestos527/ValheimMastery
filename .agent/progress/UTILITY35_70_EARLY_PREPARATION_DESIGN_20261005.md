# UTILITY35_70_EARLY_PREPARATION_DESIGN_20261005 progress
BASELINE / READ-ONLY. COMPLETE for design/source assessment. No code/config/build/runtime/state changes.
Measurable scope phases sum100: current timer/reservation source trace30 (1/1); cancellation/concurrency design40 (1/1); variants/recommendation20 (1/1); lifecycle/readback10 (1/1). Total100% design; no implementation acceptance implied.

## Source evidence
- WorkshopRemoteCraft.AllowCraft currently intercepts DoCrafting at completion of the ordinary timer, then BeginSelectedCraft(true) starts a new request. Therefore the existing chest transaction is late by design; station-only prewarm exists while browsing recipes, but full resource/ownership preparation does not.
- Tick defers remote Reply execution until craftTimer<0. CraftCancellation rejects active native timer. BeginSelectedCraft accepts timed state; simply moving the existing call earlier would also move the real server chest debit earlier and extend escrow until the timer/cancel resolves. The cancelled/resumed attempt can then mix old pending replies/receipts without a new protocol distinction.
- CancelPendingCraft records Refunded and drops the client pending attempt. WorkshopBuildRay has its cancellation hook. Future prewarm caches must not reuse this transaction receipt as an authorization token.
- WorkshopBuildBridge.BeforePlace starts BeginBuild at actual placement, with exact piece/ray/position guards on execution. There is no inherent craft timer to hide preparation behind; menu/selected-piece/ghost dwell is the available lead time.
- WorkshopResourceReservation.TryReserve is host-local-only, reserves a whole component and has no current source callers (rg WorkshopResourceReservation. returns none). Its filename is not evidence of a usable dedicated-server per-resource reservation subsystem. Do not reuse its coarse component lock for background menu prep.
- Prior timing evidence: cold ownership74–170ms, first craft534ms vs second220ms; actual warm removal0.13–0.20ms; baseline RTT varies. Hiding work under the craft timer can reduce post-timer waiting; metadata cache alone cannot guarantee removal of final server/client round-trip.

## Terminology / strength of promises
1. Stock/plan cache: immutable counts/source hints; resources stay in chests and available to everyone; each use revalidates exact bytes, rights and eligibility. Cannot promise those resources still exist later.
2. Ownership warm lease: server simulation owner is prepared; no resource quantity is reserved. Others may open/access through existing release protocol, revoking preparation. This can hide the measured ownership handshake.
3. Quantity reservation: exact amounts committed to one pending attempt; all competing consumers must honor reservations, including native TakeAll/stack/open/destruction/other mod writers as applicable. A dictionary flag is not a guarantee. Could require short chest lock or durable escrow. This is a new correctness subsystem, not merely a cache optimization.
4. Actual predebit/escrow: resources already removed, needing explicit durable cancel/refund/recovery. Do not call it read-only preparation.

## Recommended craft design, first implementation stage
- Observe actual craft-start edge (native hook identity must be verified before editing), freeze recipe/quality/amount/variant/station and current shortfall into preparation generation. Start background stock/source lookup and required ownership handoff while native timer advances.
- Separate Prepared state from existing action Requested/Uncertain/Committed/Refunded receipt lifecycle. Prepared state gives no output/debit permission. Throttle/coalesce identical requests; callbacks accept only current generation/session/actor and still-selected attempt.
- At timer completion validate identity, rights, skill, station, output capacity as supported, exact personal/chest portions and current stock; run actual debit/output/ACK exactly once. If prep is missing/stale, fall back to current safe path. This first stage removes repeat discovery and ownership handoff from the late critical path, but final action round-trip can remain.
- Cancel invalidates attempt token immediately. Keep reusable read-only count/source cache10–30s and reversible ownership warmth5–10s initially, with global/per-player limits and early release when another actor opens/needs the chest. On same recipe restart create a fresh attempt token but reuse valid cache. On changed recipe retain old metadata entries in bounded LRU; they are not spendable credits.
- If true quantity reservation later becomes necessary, reserve only costs of one actively crafting recipe; expiry anchored to actual timer plus small network grace; cancellation releases quantities immediately by default while cached preparation remains. A short3–5s post-cancel quantity hold is optional only after fairness tests: repeated cancel must not renew it indefinitely, and waiting contenders trigger release. Never retain a whole component lock for cancellation grace.
- Avoid actual predebit at craft start in this first stage. This would need more crash/refund/save-order work than warm preparation.

## Recommended build design
- Menu opening: prepare network index/count hints, not an arbitrary fixed basket of reserved wood/stone/marble. Read active building category and recent pieces to prioritize2–3 likely material types; skip unrelated components/distant chests. Trigger once per menu session and throttle further changes.
- Selected piece / stable placement ghost: fetch concrete costs/source hints and prewarm needed contributor ownership before click. Debounce100–200ms provisional; cap a small contributor set, initially4–8 chests per actor, subject to existing global128 warm limit. If actual menu-to-click dwell is shorter than handshake, current safe fallback applies.
- Keep last32 piece-preparation entries30s LRU across A→B→A, and exact-byte-invalidated stock hints; immediately recompute shortfall when personal stock/recipe amount changes. Do not cache final placement authorization: position/range/wards/station and actual resources rechecked at click.
- Idle/closed menu: stop speculative work; release ownership warmth on short inactivity/grace or competitor open; retain bounded read-only metadata briefly. No quantity reservation merely because menu remains open.
- Very hot repeated placement may later use a one-next-piece quantity reservation, proportional to observed consumption and a short expiry, not a large baseline resource basket. Dedicated native-writer coverage/fairness needed before this promises resources.

## Alternatives / tradeoffs
| Variant | Improvement | Cost/limit |
|---|---|---|
| Read-only counts/index cache | Saves repeated decode/navigation | Does not hide ownership or guarantee stock |
| Early selected-action ownership preparation | Moves74–170ms cold ownership work before timer completion/click | Needs revocation, bound/session/cancel guards; final RPC remains |
| Exact active-recipe quantity reservation | Protects inputs during timer | Contention/native-consumer integration; short scope/expiry essential |
| Small server escrow buffer / batch action credits | Can remove per-placement resource round-trip for authorized sequence | New persistent economic protocol, conservation/rollback/replay/save/crash; defer unless earlier stages are insufficient |

Recommended order: stage1 early craft and selected-piece preparation + bounded caches; measure start-to-output and timer-complete-to-output separately; add quantity reservations only if races/replanning dominate; consider escrow/batch credits last. Warm8→30s alone addresses repeats, while timing-overlap specifically addresses the first user-visible wait.
Do not claim zero delay: current safe final transaction validation may require RTT. Do not release output locally from preview counts. Preserve Crafting35 no-debit branch and exact cancellation/refund semantics.

## Next action / reservations
Design complete; human has asked to think/propose variants, not implementation. Keep initial BASELINE / READ-ONLY. Implementation phase would require explicit scoped authorization and new task/reservations/shared-system declaration/review. Three documentation reservations released after final readback.
