# Gold owner review — Muster payment/recovery

FINAL COMPLETE / READ-ONLY REVIEW / all own review reservations RELEASED. No source integration/deployment/native gameplay approval. Exact frozen reviewed sources:

- Combat100Runtime.cs A79E09238B5B587CB7A5CC531A4E3F1CAC2221287B11753D129BA03643F047BF.
- Combat100ActionJournal.cs5DAFAEF6C70CB96FA2267A5DD0E18599C2C06266068C4D7BA6B4E5B95B96F877.
- Gold ledger/state/cooldown exact hashes in validation/gold-muster-payment-review-20261005/source-hashes.csv, unchanged shared accepted schema4.

Verdict: bounded accounting/recovery engine SOURCE COMPATIBLE for serialized Muster-only Odin use. NOT approval for allowing other Odin consumers to overwrite an unpublished financial outcome, native authentication/effects/Delivery ingress, bootstrap or LIVE. Preserve the constraints below before integration expands.

## Confirmed recovery hazard — other Odin action before dual commit

Gold stores one latest result per player/patron. Sequence reproduced with actual frozen Gold ledger+Combat journal IO:

1. Persist Muster Planned; Gold Reserve+Committed succeeds, but Combat journal is not yet Committed (crash window).
2. Another Odin action is admitted and settles after the native cooldown permits it; its result replaces the original Odin result.
3. Original Muster Recover returns Unknown; Delivery remains null; original journal stays Pending and blocks future Muster. Original750 payment is not reconstructed/refunded/repeated.

Evidence validation/gold-muster-payment-review-20261005/probe-final.log. This is correct fail-closed financial behavior, but unresolved paid-action liveness. Required integration rule: before allowing ANY additional Odin admission for that player, reconcile pending original Muster into a durable Combat terminal/Committed record and preserve its exact financial outcome. Apply a common per-player/patron admission/recovery fence across consumers, or separately design durable outcome retention/ACK. A fence only inside Muster Start does not cover another Odin consumer. Do not broaden the current core ledger silently; any retention/schema protocol change requires a new shared-system declaration/review.

When the exact result is already gone, do NOT infer payment from Generation, a generic SettledToken, available funds or a timer. Remain Unknown; no new token, duplicate debit, automatic refund or effect. Existing code does this correctly. Before native wiring, test the fence with crash after Gold commit, journal unavailable, other Odin admission attempt, restart and exact original recovery. Once the Combat journal is already durably Committed, later Gold result replacement is safe for that retained paid entitlement; Delivery need not require the old latest Gold result forever.

## Never-admitted generation — current Abandoned fix accepted

The first source read had Cancelled-only semantics; source changed before freeze. Current exact reviewed5DA/A79 has Abandoned for never-admitted plans, distinct from admitted Unused Cancelled. Prepare/Parse skip only Abandoned generation occupancy and retain its original token permanently. Root's initial suspicion of a permanent next-generation collision does NOT apply to these reviewed hashes.

Actual frozen-source probe confirms: never-admitted Planned with GoldGeneration0 -> Recover Cancelled result/journal Abandoned -> a NEW original token at generation1 commits once -> reopen journal with Abandoned+Committed generation1 succeeds -> original Abandoned token remains undisclosed, no double payment. Retain the distinction and tests. Old pre-Abandoned schema2 Cancelled files do not prove never-admission; if any such files are deployed, migration/recovery needs an explicit reviewed decision, not automatic reinterpretation/deletion.

## Accepted shared protocol seams

- Frozen original request/roster before Gold reserve; journal clones its input.
- Expected ledger/session/world and fresh-vs-recovery accessor identity validated at each coordinator boundary; Core OFF does not authorize new settlement/delivery but terminal paid outcome reconciliation remains available.
- Exact claim/result compares full frozen grant. Missing result after advanced generation remains Unknown. Planned recovery may abandon only with current available Gold generation strictly below the proposed one.
- Planned/Reserved states disclose no effect. Expired exact nonprepared hold can SettleUnused and permanently Cancel; no prepared Tyr timeout/refund permission inferred.
- Publish requires exact Gold Committed first, then durable Combat Reserved/Committed. Delivery requires retained Combat Committed/current fresh core/unexpired absolute deadline.
- No generic token/result inference and no automatic recovery reissue with a new token/generation.

## Evidence and limits

18 focused assertions on frozen actual ledger/journal/coordinator, controlled local files. They confirm current Abandoned fix and reproduce the overwritten-result hazard. Native Gold readiness/session accessors are explicitly stubbed here; actual native ingress, RPC, effects and clocks are unwired. This is not a new verification of those seams or LIVE crash timing. Existing Combat payment suite initially33PASS, then current37PASS was read, not claimed as root's independent rerun. Root edited only own review records/probe files; no Combat/Gold canonical source writes.

Combat owner retains Runtime/Journal ownership and implements the admission fence/retention decision and native delivery idempotency. Root owns any future shared ledger/API change. Required native cases remain original token/recipient effect ACK replay, character/session/world/death/disconnect, settings OFF, journal/Gold storage uncertainty, expiry before delivery, two-player authority and coupled rollback/save order.

## Core OFF / durable paid recipient receipt — shared authority rule

Combat follow-up asks whether a receipt saved BEFORE effect permits restoration while Core OFF. Paid/Accepted is not proof that the first native effect already started. Crash after durable receipt but before ReceiveCommitted leaves the same receipt; treating every restore as prior-effect continuation would create a first effect while fresh core authorization is unavailable.

Shared gate: require current authenticated CoreReady before FIRST recipient effect or reinstating an in-memory effect from an Accepted-only saved receipt. Keep the original paid receipt/token/absolute expiry while OFF, without refund/new charge/duration extension; retry only when core-ready and still unexpired. A marker written before effect is not application proof. Do not delete/renew the entitlement merely due to a config toggle.

An already installed, currently running native window can remain a continuation if the action owner's domain/global-setting policy permits it; shared financial recovery alone does not force undo of already applied state. That exception does NOT authorize recreating a missing native window from a save-first Accepted receipt while OFF. Simplest bounded alternative: gate every ReceiveCommitted/reinstatement by CoreReady, retain the already installed window and absolute deadline according to domain policy. Actual receipt/native delivery code not supplied here, so this is an authoritative shared boundary, not approval of its future implementation. Test crash after recipient receipt save before first effect, OFF before delivery/reconnect, ON before original expiry, and expired receipt with no new effect/debit/time extension.
