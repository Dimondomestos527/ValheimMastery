# UX_FISTS_ATGEIR100_PATRON_ASCENSION_20261005
- Owner/profile: CORE_UX_TUTORIALS; root final integration.
- Human authorization: 2026-10-05 «Все вірно, тоді реалізовуй, будемо тестувати». Approved design: separate first-time level-100 celebrations after the standard perk unlock presentation; Týr for Fists, Odin for Atgeir; no repeated divine recognition.
- Goal: add two distinct, concise, non-blocking patron-recognition sequences after the existing level-100 milestone frame, while preserving ordinary first/repeated milestone semantics and one-time raven ordering.
- Acceptance criteria: standard milestone completes before patron sequence; Fists uses Týr copy/theme and Polearms uses Odin copy/theme; only durable first unlock schedules patron recognition; repeated crossing never schedules it; raven waits until all presentation ends; no input capture; UA and English fallback; null icon safe; client/server builds pass; no deployment or LIVE VERIFIED claim.
- Complexity: NORMAL; root-only shared UX integration. No subagents requested or authorized.
- Exact reservations HELD before source write: src-modern/MasteryPatches.cs only class Milestones call site; src-modern/PerkVisualService.cs only PlayMilestone; src-modern/MasteryRavenTutorials.cs only presentation gate; NEW src-modern/PatronAscensionPresentation.cs; this task and matching progress/handoff; NEW validation/ux-patron-ascension-20261005/**. MasteryMilestonePresentation.cs is READ-ONLY.
- Non-goals: no Combat100 effect/balance/Favor/ledger changes; no perk renaming; no custom assets; no Raven lesson expansion; no version bump; no DLL installation, game/server launch, config/world/character/save mutation.
- Source authority: Valheim Mastery 1.4.123; current source and Build123 client/server gates. Git is not enabled in this repository and was not initialized.
- Dependencies: Combat100 patron/mechanic semantics are taken from current COMBAT100_FISTS_ATGEIR review/implementation records. Exact mechanics remain Combat100-owned. Shared UX behavior is root-integrated; affected-owner acceptance of new player-facing copy remains required before final integration acceptance.
- Regression: first 99→100 versus repeated crossing; durable MarkEverUnlocked semantics; save/reconnect reasoning; UA/fallback; long text/UI scale; no raycasts/focus capture; missing icon; VFX/messages toggles; raven order; headless/server compile.
- Status: SOURCE IMPLEMENTED / STATIC VERIFIED / LIVE TEST AND COMBAT100 OWNER ACCEPTANCE PENDING. Deployment not authorized.

## SHARED SYSTEM CHANGE
- file/system: Milestones in MasteryPatches.cs; PerkVisualService.PlayMilestone; MasteryRavenTutorials presentation gate; new PatronAscensionPresentation shared UX component.
- reason: sequence patron-specific first-time recognition after the existing shared milestone instead of overlaying or replaying it.
- affected domains: CORE_UX_TUTORIALS and COMBAT_100; shared milestone/tutorial consumers must remain behaviorally unchanged for every other skill.
- regression required: all existing milestone thresholds; first/repeated distinction; durable flag and reconnect; tutorial queue order; message/VFX settings; client/server build; no gameplay or Gold transaction mutation.

## PRESENTATION / VANILLA ASSET PLAN
- Presentation: runtime-native Canvas/TMP/UI components and the existing skill icon only. No imported texture, mesh, animation, sound, bundle or external asset.
- Týr identity: restrained iron/bronze-red frame, firm scale settle, concise death-edge resolve narrative. It deliberately does not claim that unfinished Second Breath execution is available.
- Odin identity: cold blue/gold frame, wider reveal, concise All-Father recognition narrative. It deliberately does not claim that the currently unwired Muster action is available.
- Standard milestone remains the first scene. Patron recognition waits for MasteryMilestonePresentation to finish and never triggers on repeated crossing.
- Input/focus: overlay is non-interactive and blocks no raycasts; no cancel/close ownership.
- Async/fallback: missing skill icon hides the image without blocking copy; local player/session loss destroys pending/active UI.
- Audio/world VFX: unchanged. Exact Combat100 mechanic VFX/SFX stay outside this task.

## SUPERSEDING LIVE CONTRACT / RESERVATION TRANSFER — 2026-10-06
- Human live-test decision reported by Combat100 root: remove the second blue/red patron Canvas frames. Keep the main Gold milestone; follow it with a crafting-like yellow center recognition message during native celebration VFX/SFX.
- Exact reservation `src-modern/PatronAscensionPresentation.cs` RELEASED by CORE_UX_TUTORIALS to Combat100 root at canonical SHA256 `529BB1E44987F965ED05DA36B80AB7C1736037D3C55AEB52050C08E94F4A4735` for the isolated revision/review workflow.
- CORE_UX_TUTORIALS has no overlapping edit in that file. Old static-verified Canvas design is superseded by the new live contract and must not be treated as accepted presentation.
- Existing reservations for Milestones call site, PerkVisualService.PlayMilestone and Raven presentation gate are not transferred by this note; Combat100 reported no canonical shared write until separate affected-UX review/integration.
- Follow-up human contract selects separate COMBAT VFX/SFX, not crafting effects; only crafting-like sequencing and yellow center recognition are shared. Exact `src-modern/PerkVisualService.cs` slot `PlayMilestone` first-100 `Schedule` gate RELEASED to Combat100 root at whole-file base SHA256 `8BF64228EC97D3425FED73BCABE22072D7C9854D8F28AF414078F65366284FC6`. This transfer permits only the gate change needed to honor VFX/SFX independently when milestone messages are disabled; no other method or shared feedback behavior is released. UX has no overlapping edit.
