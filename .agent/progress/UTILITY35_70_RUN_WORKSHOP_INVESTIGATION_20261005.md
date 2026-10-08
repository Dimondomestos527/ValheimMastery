# UTILITY35_70_RUN_WORKSHOP_INVESTIGATION_20261005 progress
Phase: BASELINE / READ-ONLY. Status: COMPLETE for this investigation.

Weighted phases, total 100:
1. Scope and current-source trace 25%: 1/1 complete.
2. Run phase-2/phase-3 parameter comparison 20%: 1/1 complete.
3. Workshop latency instrumentation and available evidence assessment 25%: 1/1 complete.
4. Actual-debit visual attribution trace and implementation contract 20%: 1/1 complete.
5. Final readback/handoff 10%: 1/1 complete.

Progress: 100% of the authorized read-only investigation, not gameplay acceptance or implementation.
Reservations: matching task/progress/handoff Markdown only; released after final readback. No source/shared files reserved.

## 1. Run phase 2 versus phase 3

Active path: Stride70Service calls PerkFeedbackService.Play("run_70_stack" + stacks), which reaches the recipe service. RoadRhythmPhaseVisual.Set has no caller in current source; its Clear call remains, so its private emission path must not be treated as the active implementation.

Both active recipes resolve the same legacy native `fx_perfectdodge` presentation and differ only by tuning:

| Parameter | Phase 2 | Phase 3 | Phase-3 change |
|---|---:|---:|---:|
| Native-root scale | 0.50 | 0.70 | +40.0% |
| Particle-size multiplier | 0.90 | 1.00 | +11.1% |
| Particle-speed multiplier | 1.20 | 1.40 | +16.7% |
| Emission multiplier | 1.75 | 2.50 | +42.9% |
| Audio volume | 0.80 | 1.00 | +25.0% |

Current 1.4.123 client log contains three natural phase-2 and three phase-3 traces. The principal particle system reported 49 particles for every phase-2 sample and 63 for every phase-3 sample: +14 particles, +28.6%. Phase 3 reaches the aggregate 64-particle safety ceiling (`cappedInstantPrediction=64`), so raising emission alone has little/no headroom. Recorded bounds and first-particle sizes overlap; one phase-2 bound was wider than all phase-3 bounds. Samples were taken at different positions/routes/camera states, so they do not answer perceptual distinguishability.

Decision status: the user's wording is correct. This is a visibility concern, not a confirmed break. A valid comparison must use one client/build/hash, fixed camera/FOV/distance, same flat point, lighting/weather and VFX settings, then alternate `vm_vfx recipe run_70_stack2` and `run_70_stack3` at the same point for repeated captures. Existing `vm_vfx` supports this without a gameplay-code change, but running the client/test scene is outside current authorization.

## 2. Workshop latency

The code already contains opt-in buffered telemetry; adding another timer before collecting it would be wasteful and risks measuring the instrumentation. Client config currently has `WorkshopTiming=true` and `VerboseLogging=true`, but the current client log contains no `[WorkshopTiming]` or Workshop70 transaction sample. No current server config/log was found at the canonical server path. Therefore there is zero usable latency dataset and no defensible optimization target.

Existing stage split is sufficient for the first measurement:

- client: T0 request; diagnostic echo RTT; grant received/dispatched; T8 native action/output observed; ACK sent; T9 settlement confirmed;
- server: S0 received; journal admitted; T1 network resolved; T2 containers resolved; T3 availability proposal; remote leases sent/all ACKed; T4 ownership ready; T5 final plan; T6 removal start; T7 removal persisted; escrow persisted; grant sent; settlement;
- counters: eligible, leased and actually debited chest counts, material kinds and successful output.

Required measurement set before optimization: host and remote-client successful crafts; warm/server-owned versus remote-owner lease; one and multiple contributing chests; personal/chest split; plus denial/cancel/refund controls. Compare per-stage deltas on each host's monotonic clock; never subtract client timestamps from server timestamps. Use echo RTT only to separate network round-trip from server work. Optimize the dominant repeated stage only after a reproducible distribution exists; preserve the T5 final replan, T6/T7 exact debit/persistence and ACK/settlement gates.

## 3. Chest-to-station flow attribution

Current behavior does not meet the new contract. WorkshopConnectionVisual.Update runs once per second while a recipe is selected, before crafting succeeds. It uses client preview stock, subtracts personal inventory, walks candidate chests in distance/ZDO order and emits up to six connection prefabs. It is narrower than “all nearby chests,” but it still shows predicted contributors, not authoritative final debit participants. The authoritative server may replan after ownership transfer or stock/permission changes, and failed/cancelled/preserved crafts can still have shown preview beams.

Authoritative contributor evidence already exists:

- remote/dedicated path: `CommitRequest` builds `lines` from the final server plan and de-duplicates actual contributor ZDOs into `ServerAction.Chests`; commit occurs only after client reports successful output and the server accepts the ACK;
- listen-host direct path: `WorkshopHostCrafting.Attempt.Plan` contains exact debit containers; `Finish` knows successful output/payment and whether Crafting35 preserved resources.

Implementation contract for a future explicitly authorized task:

1. Stop emitting chest-to-station streams from recipe preview. Preview counts/UI may remain, but particle flow must not imply a debit.
2. Build the visual source set only from the final debit plan, de-duplicated by stable chest/ZDO identity.
3. Emit only after successful output and confirmed commit. Emit nothing for denial, cancellation, rollback, ambiguous output, personal-only crafting, or Crafting35 resource preservation (`Preserve`), because in those cases no chest supplied committed resources.
4. For remote craft, carry exact contributor IDs and station/action destination through the transaction and release the cosmetic event after server settlement. A lost cosmetic event is preferable to false feedback.
5. For listen-host/direct host craft, invoke the same Utility-owned visual service after `Debit.Commit()` with the exact containers from `Attempt.Plan`.
6. If the six-beam safety cap remains, every shown source must belong to the actual contributor set; order deterministically. Never substitute nearby candidates.

This future RPC/payload mutation is a cross-domain review trigger. Before implementation record a SHARED SYSTEM CHANGE covering workshop settlement payload/RPC, Utility gameplay feedback, shared VFX pool consumption, host/remote parity, malformed/stale packets, reconnect and exactly-once settlement regression. Root integrates it.

## Status and next action

No gameplay/source/build/config/runtime/state was changed. No build, client/server launch or deployment occurred. The next correct action is an explicitly authorized runtime-measurement task: controlled phase-2/phase-3 capture plus WorkshopTiming collection on both client and server. Optimization and visual implementation remain blocked by the declared BASELINE / READ-ONLY phase, not by missing design direction.
