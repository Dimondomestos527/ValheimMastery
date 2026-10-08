# Core UX & Tutorials
Profile ID: CORE_UX_TUTORIALS; task directory: .agent/tasks/ux/.
## Owned scope
Milestone/repeated crossing presentation framework; one-time Huginn/Muninn tutorials; shared UI/tooltips/layout/localization infrastructure/input focus.
## Explicitly out of scope
Perk exact content/mechanics (gameplay owner); Gold reward protocol; runtime deployment. No work outside active human task.
## Read canonical documentation
AGENTS.md; docs/architecture/CONTEXT_AND_STATUS.md; docs/architecture/SCOPE_MAP.md; docs/architecture/SHARED_SYSTEMS.md; docs/KNOWN_ISSUES.md; docs/architecture/CORE_UX_TUTORIALS.md. Read only relevant shared sections.
## Known source/class pointers
MasteryMilestonePresentation.cs, MilestoneCrossingRule.cs, MasteryRavenTutorials.cs, MasteryWindow.cs, SkillsTooltipPatch.cs, MasteryItemTooltipPatch.cs, PerkNarrativeService.cs, PerkProcHudService.cs, PerkUiIconService.cs, PerkLocalization*.cs.
Source in src-modern unless tools/QC/project named. Actual source filenames from198-file inventory; mixed-file class boundaries in SHARED_SYSTEMS. Check classes and #if before editing. Source presence is not LIVE VERIFIED.
## Shared systems to inspect when relevant
MasteryPlugin/MasteryRuntime; MasteryEvents/dispatch; PerkRuntimeServices (PerkHitContext); MasteryStateStore; NetworkSync/OwnerSkillAuthority; catalog/UI/feedback. Gold protocol only where used.
## Technical regression tags
TUTORIAL_SEEN_FLAGS LOCALIZATION_STRUCTURE UI_LAYOUT INPUT_CANCEL ASYNC_ASSETS; add HEADLESS RECONNECT SERVER_RESTART where state/runtime affected. Tags never create a gameplay owner.
## Required test dimensions
First/repeated34→35/69→70/99→100; animation every crossing vsone-time tutorial/reward; UA/fallback; resolutions/scale; focus/close; reconnect flags. Tests only when task authorizes them; current gameplay regression DEFERRED.
## Cross-domain review triggers
Shared-system mutation; mixed file; event/RPC/persistence format; generated-hit/XP coupling; Gold transaction/action boundary; UI framework vsdomain content; changed compile gate. Record SHARED SYSTEM CHANGE before mutation, affected-owner review, root integration.
## Deployment permissions
May inspect and modify owned source/build client+server/static QC only as authorized by active task. No automatic runtime deployment. Release packaging always separate.
## Subagent policy
Follow AGENTS.md: SIMPLE root-only; NORMAL optional bounded read-only scout/reviewer; COMPLEX max2, third reviewer only justified. No overlapping parallel writes/shared integration delegation. Every assignment exact goal/files/permissions/non-goals/output.

## Presentation ownership boundary
Core UX owns shared UI framework/layout, shared milestone/tutorial presentation, common tooltip/icon framework and input/focus behavior. Gameplay owners own the exact perk VFX/SFX, timing tied to mechanic semantics and mechanic-specific HUD values/content. Gold shared protocol semantics remain GOLD_CORE. VFX/SFX complexity does not create a new gameplay domain.
Cross-domain shared presentation components require affected-owner coordination and root integration.
Follow [VANILLA_ASSET_WORKFLOW](../../docs/architecture/VANILLA_ASSET_WORKFLOW.md); keep exact gameplay content/asset decisions with its owner and live visual/audio evidence separate from static verification.

