# Forest Guard V2 — Stage A design review

This is the entry point for `refactor/forest-guard-v2`: `review/forest-guard-v2/`.

**Design only.** No V2 implementation, candidate DLL, runtime migration, install, release or LIVE acceptance is part of this commit. The inherited repository-root code is historical and must not be treated as the installed source. Baseline Client42C and Server0CD3 are pinned separately in `BASELINE.json`; their exact source commits are unknown. PR #2 / `review/forest-guard/` is diagnostic reference, not a merge-ready implementation.

Read `CONTRACT_UA.md` for the exact accepted provider/Cargo/native/presentation contract. Its SHA256 is pinned in `BASELINE.json`. The document preserves its original proposal wording, but Root has adopted its decisions and affected Core design review is accepted; Utility/Cargo may work in their isolated Stage B scope. Their unreviewed working code is not included here.

## Structural change

| Concern | Existing approach | Accepted V2 target |
|---|---|---|
| Authority | Six lifecycle phases coupled to cue preparation/completion | Registry life, server desired activity and native availability are independent; one controller/owner writer |
| ACTIVE | Camera/assets/appearance completion can delay gameplay admission | Exact current server intent + native-owner applied revision; presentation cannot hide/freeze the ACTIVE real body |
| Cargo dormancy | Legacy phase2/removal retirement coupling | Neutral witnessed settlement; current inventory owner/save/reclaim/ACK protections retained; no unnecessary body ACK/save or3s retirement |
| Native dormant backend | Delicate claimed hide/restore | Initially retain only necessary current-identity claims; no speculative global friend/heal filtering |
| Updates | Cue/retry-driven repeated authority work | Event-driven changes + bounded reconciliation over at most four known subjects; frequency is an implementation target |
| Network | Existing v13 semantics | Separate paired v14 cohort/codec2 and four_v2 RPCs; no simultaneous V1 writers |
| Data | Registry schema5 and persistent native Cargo | Retained; unresolved identity is not death or permission to clone/erase |

This is not a claim that independent axes have only two total states, nor a measured FPS/GC improvement. The simplification is removing cue authority and duplicated transition responsibility, not adding a generic manager/framework.

## Required evidence / rollback

Later scoped checks must cover current owner/stale RPC, absent versus dead body, identity/HP/inventory continuity, neutral Cargo settlement, partial Awake/load and claimed native restore, and ACTIVE without camera/VFX. The known Client native premise rejects an unproved component-only/IsEnemy=false shortcut; it is not Server/LIVE verification. Implementation, pair build, migration safety in game and performance are not established by this document.

Preserve exact current role source/artifacts. `fix/forest-guard` at `df2fb762d803f5e0cf7d827ecde1360c1a214a84` remains the prior Git review reference; switching source is not permission to restore stale worlds, inventory or Registry bytes. No automatic DLL swap, migration, Dev merge or Release update.

Next: independently review the accepted contract/ownership/rollback boundaries. The implementation will be a separate logical publication after its scoped owner freeze. Do not infer current code from the inherited root or ask for private preflight snapshots.
