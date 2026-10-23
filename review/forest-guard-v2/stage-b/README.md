# Forest Guard V2 — source review snapshot

This is a scoped external-review export, NOT a complete build tree and NOT an installed or LIVE-accepted release. The repository-root source is historical ancestry, not the implementation baseline. Do not merge this export as a replacement for the module.

Read `MANIFEST.json`, then compare `before/Client/src-modern/` or `before/Server/src-modern/` with `after/src-modern/` using the corresponding role's explicit file list. Only listed files belong to that role. In particular LifecycleClientTrace is Client-only: Server's unchanged EffectZones has no ObserveCurrent. Do not transplant Client EffectZones or unrelated policy onto Server.

Baseline source commits are UNKNOWN. Authority is the pinned role build stages: Client DLL42C54732 /source gateDFD182BC; Server DLL0CD3FD01 /source gateC773B0C2. Full hashes and role-specific before/after pins are in the sanitized manifest. Source bytes are preserved; no DLLs, runtime logs, private preflight, saves, inventories, configs or assets are included.

Scope: Registry life identity retained; desired ACTIVE/DORMANT separated from native availability and Applied; restore precedes owner receipt; cosmetics/camera do not gate ACTIVE; neutral Cargo drain witness; four versioned lifecycle RPCs and coordinated v14 cohort. Shared after files apply only where the role manifest lists them. Unchanged dependencies and project/build symbols are not exported.

Evidence reused: affected source review SOURCE_ACCEPT_SCOPED; controlled helper36 +family5 +old circularity reversal1 passed; focused actual role API binding Client/Server0errors with actual Cargo dependencies. These are not production/full-module or LIVE tests. Final combined candidate/cohort/protected-state gates and all12 LIVE acceptance checks are separate.

Explicit unresolved limitation: additional outer-controller per-row callback guard/test is BLOCKED/pending human, excluded and untested. Internal post-SetActive/preStamp guards are included; existing ingress checks do not establish arbitrary outer-loop reentrancy safety. Publication does not approve that extra implementation, deployment, Dev/Release merge or public release.

Review question: check exact role deltas for authority, identity/death preservation, restore-before-receipt, neutral Cargo settlement and stale callbacks. Do not infer a missing Character is dead or authorize a duplicate factory. This is source-only Stage B publication on refactor/forest-guard-v2; diagnostic fix/forest-guard remains unchanged.
