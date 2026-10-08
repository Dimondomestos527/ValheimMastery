# UX handoff — Gold idol native identity overlay7
Verdict: **NEEDS_CHANGES for UX diagnostics; restored ON/OFF path STATIC PASS; LIVE UNTESTED.**

1. Fix the false `historical=` diagnostic label. The current safe resolver falls back to the live UID on mismatch, so it cannot serve as the recorded historical field. Log current, raw recorded admission output, safe resolved output and match result separately. Diagnostic-only change; functional identity/financial calls remain untouched.
2. Split the player-facing dismantle denial. Identity/not-ready may use the return-to-world wording; missing native components/view/removability must use a neutral denial without `wait` as a promised remedy.
3. Preserve the overlay boundary: no Gold schema/payment/refund changes, no new UI payment, no pending Gold10/Combat merge.

After an exact candidate hash is provided, UX should review only these diagnostic/copy deltas plus confirm the seven original functional hashes or an explicit replacement manifest. Regression root retains deployment authority.
