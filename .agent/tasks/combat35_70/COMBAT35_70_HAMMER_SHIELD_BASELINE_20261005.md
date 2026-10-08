# COMBAT35_70_HAMMER_SHIELD_BASELINE_20261005
- Owner/profile: COMBAT_35_70
- Goal: Establish current 1.4.123 behavior for hammer/club charge consumption and delayed hits from matching client/server evidence, and for tower-shield rush collision/exit state against walls, rocks, doors, and slopes.
- Acceptance criteria (observable):
  - Identify the exact active source paths and compile gates for hammer/club charge, spend, delayed hit, and result handling.
  - Correlate each available client/server log event to a concrete attack/result, or explicitly mark correlation impossible and state which identifiers are missing.
  - Trace ShieldRush70 collision and cleanup/state restoration paths for wall, rock, door, and slope cases.
  - Separate source/static conclusions, matching live evidence, historical evidence, and untested cases.
- Scope/allowed files; exact write reservations: read-only inspection of `src-modern/Hammer35Epicenter.cs`, `src-modern/Hammer35DustRing.cs`, `src-modern/Mace35CorpseProjectile.cs`, `src-modern/Clubs70Reservation.cs`, `src-modern/Clubs70EchoService.cs`, `src-modern/ShieldRush70.cs`, `src-modern/ShieldRushImpactVisual.cs`, directly called combat/shared services, Build123/project gates, and current client/server logs. Writes reserved only for this task's task/progress/handoff files.
- Non-goals: no gameplay fixes, rebalance, deployment, process launch, config/world/character mutation, or status promotion without matching evidence.
- Complexity: COMPLEX
- Relevant canonical docs: `AGENTS.md`, `.agent/profiles/COMBAT_35_70.md`, `docs/architecture/CONTEXT_AND_STATUS.md`, `docs/architecture/SCOPE_MAP.md`, `docs/architecture/SHARED_SYSTEMS.md`, `docs/KNOWN_ISSUES.md`, `docs/perks/COMBAT_35_70.md`.
- Relevant source/classes and build gates: Hammer35Epicenter, Hammer35DustRing, Mace35CorpseProjectile, Clubs70Reservation, Clubs70EchoService, ShieldRush70, ShieldRushImpactVisual, PerkHitContext and directly invoked event/RPC paths; `tools/Build123.ps1`; `ValheimMasteryPoC.csproj`.
- Shared systems touched: NONE. Shared services may be inspected read-only; any future mutation requires a recorded SHARED SYSTEM CHANGE and affected-owner review.
- Required regression (static/live, variants, state): current client/server log correlation; partial/full/overheld charge; spend/cancel/resume/expiry; delayed/generated-hit recursion and result; rush collision with wall/rock/door/slope; post-rush movement, gravity, animation, input, stamina/cooldown; owner/server duplication. Missing live evidence remains UNTESTED.
- Deployment requirement: NONE. No client/server start or deployment authorized by this task.
- Status: BLOCKED — read-only source/log audit complete; matching server evidence and controlled ShieldRush live cases are absent, and no client/server launch or protected-character/world use is authorized.
- Known dependencies/overlapping tasks: `MANUAL35_70_INTAKE_ROUTING_20261005`; KI09, KI11, KI14; current logs must be bound to exact build/version/topology where possible.

## PRESENTATION / VANILLA ASSET PLAN

Presentation required: NO for this baseline audit. Existing VFX/SFX may be inspected only where needed to distinguish a gameplay result from presentation.

## Baseline findings

- Active build gates: Build123 defines `MASTERY_CLUBS35_EXPERIMENT`, `MASTERY_CLUBS70_EXPERIMENT`, and `MASTERY_SHIELD_RUSH_EXPERIMENT`; these are active source paths. `Clubs35CounterblowService` and the old Clubs70 shockwave resolve to disabled/replaced branches under those gates.
- Exact deployed pair: client SHA-256 `839D03955995ECBC5E3B80698D0E8BB9CE86A6C5072183F38868D74B15EFD409`; server SHA-256 `95DEB8ADD0629FC7E37D1C42F9CFBFDED53688113274CB265164A4259B47C241`. Both match the current post-quarantine Build123 outputs.
- Log mismatch: current client log was updated 2026-10-05 10:46:02; current dedicated-server log ended at startup on 2026-10-04 20:56:13 and contains no matching combat events. Therefore client/server hit correlation is impossible from the retained pair.
- Hammer client evidence: five `area-dispatch` records are present. One sequence contains immediate radius 4.00/0.10 presentation followed by two radius 2.00 traces, consistent with a full-charge hammer's two delayed half-radius echoes. This is an inference from source order, not proof of damage, spend, or target result because logs have no attack/episode ID, charge fraction, consume record, echo record, damage-before/after, or target ZDOID.
- Direct-hit evidence: one hammer dispatch produced five `target-hit` callbacks naming `SeekerBrood(Clone)`; another produced one naming `SeekerBrute(Clone)`. Only the first real hit is captured as the delayed-hit template, but logging occurs before that duplicate-capture guard. Current evidence cannot show whether five callbacks were distinct damage applications/colliders or the resulting HP/stagger.
- Reservation behavior in active source: charge begins after one second of valid block, reserves actual stamina up to half natural maximum, pauses/resumes on block release/re-hold, locks for 15 seconds when released/full, attaches charge to a concrete attack, and spends without refund only on the first real non-generated enemy hit. A whiff retains charge; generated echoes cannot consume it.
- Echo behavior in active source: hammer schedules at +0.75 s, half radius, 50% damage/push and unchanged stagger multiplier; full charge schedules a second echo +0.75 s later. Charged mace secondary schedules one echo at +0.5 s, 75% damage with counter-scaled stagger multiplier. Generated hits use variant 1270, no XP, no self/other perk proc, and solid-geometry line checks.
- ShieldRush active source: tower/heavy shield uses 8.25 m over 0.30 s; light shield uses 16.5 m over 0.60 s. A forward capsule cast checks `terrain`, `static_solid`, `piece`, and `Default`. Non-character contacts with `normal.y <= 0.65` stop the rush, so walls, rock faces, and closed-door colliders are expected stops. Surfaces with `normal.y > 0.65` are ignored as traversable slopes; steeper/edge normals stop as walls.
- ShieldRush cleanup in source: restores ignored hostile-body collisions, clears episode/hit state, clears the ZDO expiry, and zeros horizontal velocity while preserving vertical velocity. It does not explicitly clear `Player.m_blocking`; native input/state update must do that after the rush. Stamina 25 and the 20-second cooldown are consumed at start and are not refunded after immediate obstacle collision. No current log proves the post-rush animation/input/gravity/blocking state.
- Existing patch123 QC is STATIC only: it checks echo native-path/geometry rules and that shield contacts sweep actual movement. It does not simulate wall/rock/door/slope traversal or post-rush character state.

## Required evidence to unblock

- One controlled matching-build run with the same client/server session and retained logs.
- Hammer/mace telemetry per attack: episode ID, weapon/intent, charge fraction, reserve before/after, commit, consume/refund/expiry reason, direct target ZDOID and HP/stagger before/after, delayed echo index/time/targets/results, generated-hit context, and observer role.
- ShieldRush telemetry per episode: shield class, start position/direction, capsule contact collider/layer/normal/distance, stop reason, position/velocity/grounded/blocking/attack/dodge/stagger states before start and after stop.
- Live matrix: tower shield into constructed wall, natural rock/cliff, closed/open door, gentle slope, threshold-adjacent slope, steep slope, downhill edge, and recovery attempt (move/attack/block/dodge) immediately after stop.

