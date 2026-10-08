# UX_DESCRIPTIONS_RAVEN_REVIEW_20261005
- Owner/profile: CORE_UX_TUTORIALS; root final integration.
- Goal: audit every perk mechanic changed on 2026-10-05, including source-only/not-installed candidates, update player-facing descriptions only where the current text is materially false or incomplete, and decide whether Huginn/Muninn need additional mechanic guidance.
- Acceptance criteria (observable): changed-mechanic inventory separates installed vs source-only state; each affected perk has current mechanic-to-text verdict; English/UA/fallback descriptions remain structurally aligned; raven advice is either unchanged with evidence or updated without duplicating perk descriptions; both Build123 variants and bounded localization/tutorial checks pass; no deployment inferred.
- Copy contract from user: concise narrative text, not a numeric specification. State what the player gained, the essential activation/control, and any boundary needed to prevent a false expectation; leave secondary discovery to play. Do not add numbers unless omitting them would materially mislead a decision.
- Scope/allowed files; exact write reservations: src-modern/PerkLocalization.cs; src-modern/PerkLocalization.Ukrainian.cs; src-modern/PerkNarrativeService.cs; src-modern/MasteryItemTooltipPatch.cs; src-modern/MasteryRavenTutorials.cs; .agent/tasks/ux/UX_DESCRIPTIONS_RAVEN_REVIEW_20261005.md; .agent/progress/UX_DESCRIPTIONS_RAVEN_REVIEW_20261005.md; .agent/handoffs/UX_DESCRIPTIONS_RAVEN_REVIEW_20261005.md; new validation/ux-descriptions-20261005/** evidence only.
- Non-goals: no perk mechanic changes; no gameplay balance decisions; no release/version bump; no DLL installation, game/server launch, config/world/character/save mutation; no claim of LIVE VERIFIED from source/static checks.
- Complexity: NORMAL; root-only because shared localization/tutorial integration crosses domains. No subagents requested.
- Relevant canonical docs: AGENTS.md; .agent/profiles/CORE_UX_TUTORIALS.md; docs/architecture/CHAT_BOOTSTRAP.md; docs/architecture/CONTEXT_AND_STATUS.md; docs/architecture/SCOPE_MAP.md; docs/architecture/SHARED_SYSTEMS.md; docs/KNOWN_ISSUES.md; docs/architecture/CORE_UX_TUTORIALS.md; relevant 2026-10-05 domain task/progress/handoff records.
- Relevant source/classes and build gates: current changed mechanic source; PerkLocalization; PerkNarrativeService; MasteryRavenTutorials; Build123 client/server symbols.
- Required regression: LOCALIZATION_STRUCTURE, UI_LAYOUT/long text review, TUTORIAL_SEEN_FLAGS semantics, UA/fallback, duplicate/missing keys, both client/server compile variants. Live layout/scale/focus/reconnect remains separate unless explicitly authorized.
- Deployment requirement: NONE. Source/static update only; installation requires separate explicit authorization and fresh integrated build.
- Status: COMPLETE for authorized source/static scope; runtime layout remains LIVE_TEST_REQUIRED and deployment was not authorized.
- Known dependencies/overlapping tasks: combat/magic/utility source candidates remain independently LIVE_TEST_REQUIRED; their source reservations were reported released at handoff. No exact active reservation found for the five UX files at task creation/expansion. Exact perk wording was reviewed against owning-domain source/task evidence; shared integration was root-owned.

## EXACT CONTENT SLOT TRANSFER — 2026-10-06
- Previous UX reservations were already RELEASED at completion. For clarity, the exact six `fists_100`/`polearms_100` name/desc/flavor entries in `src-modern/PerkLocalization.cs` EN fallback and `src-modern/PerkLocalization.Ukrainian.cs` UA are RELEASED to Combat100 root for the human-requested passive names/descriptions.
- Current whole-file bases: PerkLocalization.cs `199149A1CCC740A20A372DE2E949AEE97FB0B46AC40BAFD3D11EE3437A18A988`; PerkLocalization.Ukrainian.cs `DFCE6B2865373785F86520759657503BFD85F1DA13CCF954174DDA167FD252CF`.
- UX has no overlapping edits. Other active tasks reserve different clubs/crossbows/magic/pickaxes content slices in the same physical files; root must serialize whole-file integration and preserve those entries. No conflicting exact fists/polearms100 slice was found.

## SHARED SYSTEM CHANGE
- file/system: PerkLocalization.cs; PerkLocalization.Ukrainian.cs; PerkNarrativeService.cs; MasteryItemTooltipPatch.cs. MasteryRavenTutorials.cs was reserved and audited but deliberately left unchanged.
- reason: synchronize shared player-facing descriptions/tutorial guidance with mechanics changed on 2026-10-05, including source-only candidates.
- affected domains: COMBAT_35_70, MAGIC_35_70, UTILITY_35_70, CORE_UX_TUTORIALS; potentially all consumers of localization/tutorial infrastructure.
- regression required: key/fallback parity; exact threshold/skill mapping; long-text/layout risk review; one-time tutorial seen-flag behavior unchanged unless explicitly necessary; client/server build; no mechanics inferred from catalog alone.

## PRESENTATION / VANILLA ASSET PLAN

Presentation required: NO new VFX/SFX/assets. Text/tutorial presentation only.
Player-facing intent: descriptions state durable mechanic facts; raven advice teaches cross-cutting controls/state rules that a perk tooltip cannot communicate efficiently.

Runtime composition / native donor provenance: N/A.
Shared presentation systems touched / SHARED SYSTEM CHANGE if required: declared above.
Headless behavior: server build must compile; tutorials remain client presentation only.
Async preload/pending/retry/timeout/fallback/scene-exit behavior: no asset behavior change.
Performance risks / frequency/concurrency/particle/allocation/sound budgets: none expected from static text; tutorial queue behavior must not be expanded blindly.
Live validation required / cases and evidence: long-text, UI scale, focus, reconnect/seen flags remain LIVE_TEST_REQUIRED if source changes affect those paths.
User decisions pending / meaningful alternatives and explicit selected identity: none at task start; evidence decides whether raven content changes.
