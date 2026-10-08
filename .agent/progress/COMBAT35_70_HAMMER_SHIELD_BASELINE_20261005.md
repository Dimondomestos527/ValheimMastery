# COMBAT35_70_HAMMER_SHIELD_BASELINE_20261005 progress

## Weighted phases

1. Scope, active build, and evidence inventory — 15%
   - Exit: active source/gates and candidate current client/server logs identified with timestamps/version evidence.
2. Hammer/club source and log correlation — 35%
   - Exit: charge, spend, delayed hit, generated-hit context, and result mapped for every usable log sequence; gaps named.
3. ShieldRush collision and exit-state audit — 30%
   - Exit: wall/rock/door/slope and post-rush state paths traced; available live evidence classified.
4. Cross-check and status classification — 15%
   - Exit: active vs gated/historical behavior and client/server authority/duplication risks separated.
5. Durable handoff — 5%
   - Exit: handoff records evidence, unresolved tests, and exact next action.

## Current progress

PROGRESS: 85%
Completed:
- Phase 1 (15/15): active 1.4.123 gates, deployed client/server DLL hashes, log timestamps, and evidence mismatch recorded.
- Phase 2 (35/35): all usable hammer/mace client sequences mapped to source; missing correlation identifiers and absent matching server events explicitly classified.
- Phase 3 (15/30): ShieldRush collision and cleanup source paths traced; wall/rock/door/slope expectations classified. Live cases and final character state remain untested.
- Phase 4 (15/15): active paths separated from disabled legacy counterblow/shockwave branches; static QC separated from live evidence.
- Phase 5 (5/5): durable task findings and handoff recorded.
Current:
- Waiting for authorization/evidence for a controlled matching client/server ShieldRush run and correlated combat telemetry.
Remaining:
- Phase 3 live half (15%): wall, rock, door, slope, and post-rush state observations on the exact matching build.
Blocker:
- Existing client/server logs are not from the same combat session, ShieldRush emits no diagnostic events, and no launch/use of protected runtime state is authorized.

## Evidence summary

- Client runtime: `C:\ValheimModDev\BepInEx\LogOutput.log`, modified 2026-10-05 10:46:02.
- Server runtime: `C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\BepInEx\LogOutput.log`, modified 2026-10-04 20:56:13; startup/self-test only.
- Active source/build: Build123 compiles clubs35, clubs70, and shield-rush experiment gates in both variants.
- Static QC: `validation/clean-qc-20261004/{client,server}.patch123.log`, 24 source/model assertions; explicitly not LIVE.

## Reserved files

- Write: this task/progress/handoff only.
- Source/logs: read-only.

