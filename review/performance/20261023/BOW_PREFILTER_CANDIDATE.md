# Bows 35 weak-point targeting: source-only prefilter candidate

Base: Dev commit 6deb6cae3ee464ff241ecdabd676effb13a18521; this is NOT known to be the installed DLL source.

## Change
Exactly one method: `src-modern/Bows70WeakPointPerk.cs`, `Bows70WeakPointService.FindTarget`.
The expensive `BaseAI.IsEnemy(player,candidate)` call is moved *after* the existing forward-depth (0 < depth <= 80) and aim-angle/tie checks. The original score, tie-breaking, linecast, visual marker, damage and skill eligibility are not modified. The source filename is legacy named Bows70 but its perk gate is Bows35.

## Rationale
The previous code classified every loaded living nonplayer candidate by faction, including those behind/outside the view cone. The new code classifies only candidates that could improve the current best aim score. It is safe under the assumption that `BaseAI.IsEnemy` and the position getters are read-only within one scan; exact runtime parity has not been established. Cost of acquiring position/angle for friendly candidates is slightly higher: this remains a candidate pending measurement.

## Independent model check (not Unity runtime)
1000 reproducible pseudo-random ordered candidate lists (0-320 each, seed 20261023; 139139 valid candidates); same geometry, enemy flags and obstruction flags; comparing original vs reordered logic: **0 selection mismatches, 0 linecast-call-count mismatches**. `IsEnemy` calls: **139139 -> 2495** in the synthetic distribution (~98.2% fewer). This number is scenario-dependent and is NOT a real-world FPS/CPU improvement.

## Guardrails
- No runtime builds, installs, server changes, network/persistence modifications, or Forest Guard edits.
- No algorithmic source edits other than this one candidate; do not merge until current installed-source drift is checked and the user accepts it.
- If the actual code or Unity enemy classification has side effects, or CPU profile does not show value, drop this optimization rather than preserving complexity.
- Native compile/live regression **not run** (game references unavailable here).
