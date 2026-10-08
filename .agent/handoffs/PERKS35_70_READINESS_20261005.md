# PERKS35_70_READINESS_20261005 handoff

Статус COMPLETE — bounded readiness/status reconciliation, не gameplay baseline closure.
Current source version1.4.123 / Build123 gates звірені. Тільки matching task/progress/handoff written.
Evidence: CONTEXT_AND_STATUS, KNOWN_ISSUES, three35/70 canonical indexes, existing post-migration baseline/environment reports, Magic/Utility owner handoffs, current candidate cases; narrow source Crossbows70Perk/PerkNarrativeService/Polearm35AutoSpin/Build123/csproj.
## Readiness reconciliation — 2026-10-05, current source1.4.123

Повний current35/70 gameplay matrix у наявних records НЕ закритий. Поточні позитивні build/Unity patch-registration results не gameplay acceptance. Нижче bounded current backlog, не full48-slot audit.

| Компонент | Result / classification | Owner | Що потрібно закрити |
|---|---|---|---|
| Crossbows70 | Active effect відсутній у release-safe path; DESIGN PENDING / BUILD DIFFERENCE gate; gameplay UNTESTED | COMBAT_35_70 | approved behavior і bounded implementation task, потім acceptance. Current Crossbows70Perk #if!RELEASE_SAFE; narrative прямо no-effect. |
| Pickaxes70 | IMPLEMENTED existing path, redesign DESIGN PENDING / DEFERRED; live UNTESTED | UTILITY_35_70 | відокремити proc neighbor/collapse від native support cascade; user decision щодо redesign. Не застосовувати rejected alternative. |
| Polearm35 mobility subbranch | Source branch defect: MobilityUntil declared/read, no assignment found; full gameplay UNTESTED; migration origin not established | COMBAT_35_70 | desired timing/design predicate, secondary/active-spin masking, live movement case. Не заявляти whole perk broken. |
| Mace35 corpse launch / Clubs70 charge / Hammer35 and charged echoes | IMPLEMENTED IN SOURCE; LIVE UNTESTED, old reports historical | COMBAT_35_70 | natural triggers, charge reserve/resume/expiry, primary/secondary differences, spatial/delayed echo and recursion/XP once; owner/remote VFX/SFX. |
| Blocking70 shield rush | IMPLEMENTED IN SOURCE; LIVE UNTESTED | COMBAT_35_70 | small/tower collision/stagger/side-push/wall stop; collision restoration; multiplayer. |
| Spear35/70 throws/return/hook | IMPLEMENTED IN SOURCE; current LIVE UNTESTED | COMBAT_35_70 | natural throw/return/hook and friend-server behavior, no extra animation/proc/input regression. |
| Skeleton35 lifecycle | IMPLEMENTED IN SOURCE; LIVE UNTESTED, historical failures not current FAIL | MAGIC_35_70 | portal/follow/recall/desummon, roster/mixed summons, no duplication/healing/loot. |
| Carrier70 / Torba cargo | IMPLEMENTED IN SOURCE; LIVE UNTESTED | MAGIC_35_70 | items/count/quality/custom data across close/portal/recast/reconnect/restart/death; cargo no duplication/loss. |
| Other magic35/70 including Surtling/IceStorm/Cage | Source paths exist; complete current LIVE acceptance UNTESTED | MAGIC_35_70 | cast/cancel/cost/zone cleanup/targeting plus owner/remote behavior. Prior Surtling/storm positives historical, preserve design/selected visuals. |
| Run35/70 phase VFX and jump/landing presentation | IMPLEMENTED IN SOURCE; current LIVE UNTESTED | UTILITY_35_70 + shared AV review | phase progression/visibility/cold load/cleanup. Headless fx_land expectation TEST ERROR does not prove client asset failure. |
| Crafting35/70 Workshop/processing/Potential Forge | IMPLEMENTED IN SOURCE; LIVE UNTESTED; Owned=true static COVERAGE GAP | UTILITY_35_70 | owned/unowned access, concurrent/cancel/retry once-only debit/output, attribution, forge rules and reconnect/save. |
| Spear35 base description | Recorded structural FAIL / PRE-EXISTING BUG; visible impact UNTESTED | COMBAT_35_70 content + UX | base/fallback key; UA/narrative masks possible. Not gameplay effect missing or migration loss. |

Решта35/70 — no complete current per-case LIVE PASS integrated in baseline; це evidence gap, не висновок про поломку.
Historical122 user positive double-jump and dismiss збережено як historical confirmation; не привід їх переробляти.
STALE DOC/TEST: старий HalfCapacity assertion проти current QuarterCapacity; не gameplay failure.
Gameplay MIGRATION REGRESSION не доведена цим status check.

Рекомендований порядок bounded tasks: carrier cargo data safety → summon lifecycle → combat charge/echo/shield → run presentation → Workshop concurrency; crossbow decision, pickaxe design and polearm branch handled owner-first. Обсяг runtime tasks потребує explicit request; нічого не запускається цим inquiry.

Integrity:200 inputs (198 C# + project + Build123) fingerprint before/after equal. No build/QC execution/deploy/runtime/source/asset/state edits. Existing baseline залишено INCOMPLETE; no status promotion.
Exact next action: NONE — deliver readiness answer. Named owner implementation/manual regression лише за окремим user request.
Reservations: three task-local records RELEASED.

