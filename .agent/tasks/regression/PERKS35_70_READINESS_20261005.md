# PERKS35_70_READINESS_20261005

- Owner/profile: REGRESSION_INTEGRATION.
- Goal: відповісти, які35/70 механіки незавершені або не мають current gameplay confirmation; bounded read-only status reconciliation.
- Acceptance: current version/gates звірені; concrete readiness gaps відокремлені від UNTESTED; historical pass/fail не перенесені автоматично; owner/next acceptance визначені; жодних gameplay fixes.
- Exact write reservations:
  - .agent/tasks/regression/PERKS35_70_READINESS_20261005.md
  - .agent/progress/PERKS35_70_READINESS_20261005.md
  - .agent/handoffs/PERKS35_70_READINESS_20261005.md
- Scope: тільки ці три lifecycle records; всі canonical/source/runtime/state read-only.
- Non-goals: full48-slot semantic audit; implementation, build, deploy, runtime launch, new balances, canonical-baseline rewrite.
- Complexity: SIMPLE; root only.
- Relevant docs: AGENTS/profile/CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; KNOWN_ISSUES; COMBAT/MAGIC/UTILITY35_70; REGRESSION_ENVIRONMENT;1.4.123_POST_MIGRATION_BASELINE; relevant owner handoffs and PERKS123_CANDIDATE_TEST_UA.
- Relevant source/gates: Build123 release-safe/magic/carrier constants; csproj version; Crossbows70Perk/PerkNarrativeService release-safe description; Polearm35AutoSpin MobilityUntil and consumers.
- Shared systems touched: NONE. Shared review tags only: GENERATED_HIT_RECURSION, VFX_SFX, HEADLESS, RECONNECT, SERVER_RESTART, SAVE_ORDER, CARGO_PERSISTENCE, OWNER_AUTHORITY, INVENTORY_ONCE.
- Required regression: source/document status checks only; gameplay evidence remains UNTESTED / DEFERRED.
- Deployment: NONE.
- Status: COMPLETE; three reservations released.
- Evidence limitation: owner bootstrap and registration are not gameplay PASS. Combat35_70 named owner handoff was not found under guessed name; use canonical/baseline/current source instead. No origin classification guessed for polearm branch.
## PRESENTATION / VANILLA ASSET PLAN
Presentation required: NO / N/A. Existing AV cases assessed for evidence status only; no asset change.


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

