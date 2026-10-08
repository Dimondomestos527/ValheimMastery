# UX_GOLD_IDOL_NATIVE_IDENTITY_REVIEW_20261007 progress
NEEDS_CHANGES / READ-ONLY.

## Static pass: restored state
- Historical admission output is consistently used for switch-store read/write, quota selection, component coverage and network coverage after exact native-load identity matching.
- The server mirrors the restored durable state into the current ZDO `SwitchKey` and force-sends it. Existing hover text and glow consume that mirrored current-ZDO state, so reconnect/reload does not require a new UI payment or a new interaction path.
- `EffectsSettled` now waits for native load readiness. Periodic proof probing invalidates the mirror when identity moves from not-ready/mismatch to `ok`.
- Temporary pre-settlement OFF/not-settled presentation is conservative and acceptable. Live reload remains unverified.

## Finding UX-1 — blocker for identity diagnostics
`MasterIdolLoadIdentity.Output(zdo)` returns the frozen admission output only when `Matches` succeeds; otherwise it returns the current runtime UID. `MasterIdolWorldRegistry` labels that safe resolver value as `historical` in proof logs and `Describe()`.

Result: on the exact mismatch being diagnosed, `output` and `historical` can both print the current UID while the real recorded historical output is hidden. The label is false and the diagnostic loses its most important comparison.

Required bounded repair: diagnostics only should print separate fields such as `current=<zdo uid>`, `recorded=<Store.Find(token)?.Output or missing>`, `resolved=<MasterIdolLoadIdentity.Output(zdo)>`, and `identityMatch=<Matches>`. Continue using only the safe resolved output for functional switch/financial operations.

## Finding UX-2 — misleading dismantle denial
The new `identity-or-native-state` branch combines identity mismatch with missing Piece/WearNTear/view mismatch and `CanBeRemoved` failure, then always tells the player the idol has not yet been recognized after returning to the world and to wait.

That promise is false for permanent/non-load failures. Split the branch: keep the return-to-world message only for identity/not-ready failure; use a neutral concise denial such as `Це творіння зараз не можна розібрати.` for native-removal-state failure. Do not expose technical identity terms to the player.

No source/build/install/runtime mutation performed by UX.
