# GOLD_CORE_PANTHEON_INPUT_REVIEW_20261005 handoff

2026-10-05; canonical ValheimMastery1.4.123. COMPLETE / SOURCE REVIEW COMPLETE; review-document reservations RELEASED. Gold reviewer read only; no candidate/lifecycle/source changes, build reruns, runtime launch/deploy or external replies.

Reviewed candidate: src-modern/PantheonActiveInput.cs SHA256 1EB50A4C6F04A4D078D6252D9AD77A5B6CA52889EC11DF58D1F9FDDFE2895870. Combat100 task COMBAT100_IMPLEMENT_FISTS_ATGEIR_20261005 holds this file. MasteryPlugin has no PantheonActiveInput initialization/tick call; candidate is unwired. Existing Crafting F7 remains separate.

Verdict: contextual single manual-action router concept is compatible with Gold foundation. THIS SOURCE HASH IS NOT APPROVED FOR INTEGRATION. UX owner review and corrected candidate verification still required; this is Gold financial/input-contract review, not UX/native acceptance.

## Findings and required contract

1. Historical compiler blocker at reviewed hash Tick: Hud.instance.InRadial() invokes a static native member through an instance. Existing isolated owner log validation/combat100-implementation-20261005/snapshot/validation/combat100-input-trinket-reviewed/Perks123Client.build.log reports CS0176 at line49. During final readback Combat owner changed canonical source to SHA256 02C8A1F87F102F5563B5E7AD723B01083C02113E8736A5CA0E5B0C7BDA05E978. Gold reviewer read that updated source: Hud.InRadial() is corrected and the separate Hud.instance guard preserved; other input/dispatch contract findings below remain. Corrected dual build results are not established by the historical failure log or this read-only check. Neither hash is integration-approved by this review.
2. Selected callbacks must be pure context selectors: held item/action identity only, no payment/reservation/effect and no selection based solely on affordability or cooldown. Otherwise a blocked intended action can select a different patron action, defeating no-fallthrough semantics. Skill unlock/admission may reject in the chosen coordinator; define any eligibility used for selecting explicitly. Registry must be immutable during dispatch and initialization/registration exactly once or have reviewed teardown/reinit; current Initialize does not clear Actions and duplicate RegisterManual throws.
3. Tick's LastDispatchFrame prevents normal repeated Tick in one frame, but Dispatch is internal and has no execution/reentrancy guard. Before lifecycle integration, make runtime entry exclusive and guard dispatch reentry (try/finally), or demonstrate/enforce that callbacks and other callers cannot recursively invoke Dispatch. This is an open contract risk, not an observed double debit in the unwired candidate. A second tryStart after an exception or false result must never be attempted.
4. TryStart(false/exception) is not evidence that no durable payment/effect happened. Coordinator must own frozen Gold action/generation/token, authoritative availability/cooldown/world checks, durable settlement/recovery. Router must not auto-refund, reissue token or retry another action. Current router correctly performs no direct ledger mutation and invokes at most one tryStart in ordinary dispatch.
5. Tick checks local ownership, enabled setting, headless/Hud, pause/radial and TakeInput. Dead/teleport/session/readiness checks must be enforced at actual coordinator admission; shared Gold Crafting already explicitly checks death/teleport. Dispatch alone only checks nonnull Player and must not be advertised as an authorization boundary. UI/input guard completeness requires actual native focus cases, not a claim from these source conditions.

Preserved constraints: one configurable common manual key; reactive Tyr excluded; F7 Crafting unchanged until a separately reviewed migration. F8 appears as candidate default; vanilla-free verification was reported by Combat owner, not independently inspected by this Gold reviewer. Arbitrary remaps/other mods cannot be guaranteed conflict-free.

## Verification required before integration

- Corrected exact hash; client/server build PASS; owner UX review and root lifecycle placement/config-init review.
- Managed dispatch tests:0/1/2 selections; selected/tryStart exception; chosen rejection with no fallthrough; repeat/reentrant dispatch; duplicate registration/init lifecycle.
- Coordinator tests: insufficient/held Favor,250/500 boundaries, same/foreign patron cooldown, disabled/not-ready/world change; replay with same token and no second debit/effect.
- Separately authorized live cases: chat/console/inventory/crafting/Pantheon/radial focus, pause/death/teleport, headless, key remapping/F7 coexistence, host+remote owner, reconnect and rapid key presses.

Exact next action: Combat owner fixes its reserved candidate and supplies updated source/tests; UX reviews focus/key experience; Gold/root reviews the corrected hash and actual coordinator/lifecycle before integration. No source approval transfers to changed hashes. Minimum next context: this handoff, Combat task, candidate, relevant coordinator and accepted Gold API.
