# Current known-issues register
As of2026-10-04,1.4.123. No fixes in architecture task. Distinguish confirmed source facts from unverified old live reports. Owners are gameplay/content owners; root integrates shared changes.
| ID | Category | Evidence / issue | Owner | Next review |
|---|---|---|---|---|
| KI01 | BUG (structural) | Base vm_perk_spears_35_desc missing compiled structure in clean QC; Ukrainian override/narrative masks; runtime Catalog0 missing not structural proof | COMBAT_35_70 content + UX infrastructure | fallback/key trace, no automatic wording rewrite |
| KI02 | BUG (branch), live impact pending | Polearm35AutoSpin MobilityUntil never assigned; HasMobility reads it, other conditions may mask | COMBAT_35_70 | target branch/design and live movement predicate |
| KI03 | COVERAGE GAP | WorkshopPlanner Owned=true path not sufficiently covered by current QC | UTILITY_35_70 | bounded ownership matrix |
| KI04 | COMPATIBILITY WATCH | deprecated Unity/TMP APIs in build warnings | UX/effect owner + infrastructure | exact warning/callsite trace before upgrade |
| KI05 | STALE TEST | historical ExtraToHalfCapacity assertion, current ExtraToQuarterCapacity; other old QC assertions not blanket proof failures | Regression + UTILITY_35_70 | compare assertion with current contract |
| KI06 | LIVE TEST PENDING | fx_land headless missing; client async/landing rendering not exercised | utility landing trigger + shared AV/UX review | authorized actual client scene |
| KI07 | COVERAGE GAP | Crossbows70Perk #if !MASTERY_RELEASE_SAFE excluded by current Build123; alternate compiled active path not established | COMBAT_35_70 | compiled implementation inventory |
| KI08 | COVERAGE GAP | combat/magic100 catalog slots lack confirmed execution path; catalog != gameplay | COMBAT_100/MAGIC_100 | per-entry trace; distinguish catalog/design/unknown |
| KI09 | COVERAGE GAP | XP log lacks recipient correlation; bonus final values/generated-hit context not universal | XP + affected perk owners + regression | propose bounded read-only QC observer first |
| KI10 | DESIGN PENDING | pickaxe70 redesign deferred; current approved neighbor/collapse and native support cascades unresolved | UTILITY_35_70 | proc/support tracing then user decision |
| KI11 | LIVE TEST PENDING | pre123 reports skeleton portals/cargo/run phase visuals/charge/echo issues;123 source changes not current PASS nor reconfirmed failure | MAGIC/UTILITY/COMBAT35_70 | exact matching-build reproductions |
| KI12 | STALE TEST / evidence | Gold checkpoint includes installed118 facts; latest near-cap/arming/debt mechanics require current trace | GOLD_CORE/UTILITY_100/UX | no old screenshot assumed current contract |
| KI13 | COMPATIBILITY WATCH / diagnostic drift | Clubs SkillScaling wording may describe obsolete35 bonus | XP + COMBAT_35_70 | diagnostic versus current mechanic, no gameplay change inferred |
| KI14 | LIVE TEST PENDING | complete post-migration gameplay regression deliberately postponed; no test character/client scene | Regression/Integration | explicit future test task |

Evidence: current src-modern; validation/clean-qc-20261004/CLEAN_STATIC_HARMONY_QC_REPORT_UA.txt; validation/regression-preparation-20261004/POST_MIGRATION_REGRESSION_ENVIRONMENT_UA.txt; docs/current checkpoints as reference only.
No issue was silently fixed, no historical conflicting description overwritten. Status promotion requires named evidence and current variant hash.


## Manual evidence supplement — 2026-10-05
[User observations and owner routing](../validation/manual35-70-intake-20261005/USER_OBSERVATIONS_AND_OWNER_ROUTING_UA.md), task MANUAL35_70_INTAKE_ROUTING_20261005. Historical rows above retain their original scope/date; current manual positives qualify KI11 and do not erase unresolved cases.

| ID | Category | Evidence / issue | Owner | Next review |
|---|---|---|---|---|
| KI15 | FAIL — visual defect; origin/provenance UNTESTED | Giant geometry reported spawning on Torba; screenshot shows scene obstruction. User suspects a Torba appearance element, exact mesh/material/attachment unknown | MAGIC_35_70 | Diagnose renderer/hierarchy/scale at spawn and teleport; compare matching builds before migration attribution |
| KI16 | UNTESTED — presentation difference | Run particles present, phase2→3 amount appears unchanged or weakly increased; report is explicitly uncertain | UTILITY_35_70 | Controlled phase comparison; emission/visibility and intended cue |

Scoped manual positives: inventory preservation, skeleton/Torba teleport, summon animation, run particle presence, satisfactory jump effects, restored spear throw animation. Tested DLL/SHA/settings/topology not yet bound; full persistence and multiplayer matrix remains UNTESTED. Hammer log verification and tower-shield/dash collisions remain pending.
Torba HP×5/AI/regen and chest-crafting optimization/contributor particles are owner change/design requests, not established migration regressions.

## Player feedback2026-10-06 — current test baseline
Confirmed currentclient: Knife70 Wraith donor unavailable25times→impact-onlyfallback; playerreportsunreadable. NPCwelcome5battle-skips/2starts/2ambiguousends; sourceconsumesonce markerbeforealertgate, alertclearingnotimplemented inleash. No migrationattribution yet.
See validation/player-feedback-triage-20261006/TRIAGE_UA.md for exactSHA/loglines/ownerboundedtasks and runtime/design/untested distinctions. GeneralCombat100AV playerapproved; Odinchange designrequest. Adrenalinerate discrepancyUNTESTED, globalUnarmedmultiplierconfirmedcode; networkrefreshalreadyimplemented.
2026-10-06 feedback patch49F14083/765E266E installed: reviewed Knife Visual-child warmup and NPC calm/pending-newwelcome candidates address previous logs; actual runtime FIX acceptance stillUNTESTED. Oldconsumedwelcomehistory deliberately retained. Horn replacement requiresavailableasset/humanselection; expansioncause/performance unknown. See validation/feedback-patches-deploy-20261006/DEPLOYMENT_REPORT_UA.md.
Current2026-10-06 idolreload FAIL: OFFatload/switchdeniedstate-or-binding on49F14083/765E266E, actualoneidolbut0zones. See validation/idol-reload-failure-20261006/TRIAGE_UA.md; exactbinding/gatereasonUNKNOWN, do notresetreceipts/recreatewithoutownerreview.

## Installed PilotA/PilotB presentation triage —2026-10-07
- ICON01/P2: special Bows/Spears/Pickaxes proc IDs fall back to generic icon; source mismatch confirmed, natural appearanceUNTESTED. CoreUX report validation/ux-settings-optimization-relocation-20261007/ICON_TRIAGE_UA.md.
- ICON02/P2: ShadowStep35 accepted dodge consumes readiness but does not remove ready statusmarker; STATIC FAIL, liveHUDUNTESTED. Combat35/70 validation/icon02-icon04-combat-audit-20261007/HANDOFF_UA.md.
- ICON03/P2: Mastery Blocking35/70 rows use perkId instead of active blocking_35_guardbreak/shieldrush70 keys; source mismatchconfirmed, liveUNTESTED.
- ICON04/P3: Axe10s buff vs15s singlemarker is UX ambiguity/DESIGN PENDING; actual buffdurationSTATIC PASS, not evidence of15sdamagebonus.
- ICON05/P3: cold nativeiconcache depends on SkillsDialog opening; structuralriskconfirmed, liveUNTESTED.
- Native ShowBuildPieceAuthor is genuine vanilla setting; Mastery forcedhoverhide conflicts with it. Retained per conditionaluserrequest; removingnativeUI/creatorstate not authorized.
- Current newresident healing/combat/distribution/path-refill and broadsocialscenes remain separate ownercandidate work; absence of scenes not automaticallymigrationFAIL. See validation/idol-behavior-settings-icon-triage-20261007/TRIAGE_UA.md.
No fixes for these icon/builder points bundled into performance update; migrationcausality unknown.
