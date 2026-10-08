# Regression/Integration (cross-domain validation)
Profile ID: REGRESSION_INTEGRATION; task directory: .agent/tasks/regression/.
## Owned scope
Cross-domain test planning/evidence integration; verify acceptance and state/event order, not independent gameplay ownership.
## Explicitly out of scope
No silent gameplay fixes; no gameplay launch/test/deploy unless active task explicitly authorizes it. No work outside active human task.
## Read canonical documentation
AGENTS.md; docs/architecture/CONTEXT_AND_STATUS.md; docs/architecture/SCOPE_MAP.md; docs/architecture/SHARED_SYSTEMS.md; docs/KNOWN_ISSUES.md; docs/architecture/BUILD_RUNTIME_BASELINE.md; docs/validation/REGRESSION_ENVIRONMENT.md. Read only relevant shared sections.
## Known source/class pointers
qc-* source/projects; RuntimeSelfTest.cs, RuntimeAssetAuditService.cs, PerkDebugService.cs; inspect relevant owner source.
Source in src-modern unless tools/QC/project named. Actual source filenames from198-file inventory; mixed-file class boundaries in SHARED_SYSTEMS. Check classes and #if before editing. Source presence is not LIVE VERIFIED.
## Shared systems to inspect when relevant
MasteryPlugin/MasteryRuntime; MasteryEvents/dispatch; PerkRuntimeServices (PerkHitContext); MasteryStateStore; NetworkSync/OwnerSkillAuthority; catalog/UI/feedback. Gold protocol only where used.
## Technical regression tags
HEADLESS RECONNECT SERVER_RESTART SAVE_ORDER CRASH_RECOVERY GENERATED_HIT_RECURSION VFX_SFX; add HEADLESS RECONNECT SERVER_RESTART where state/runtime affected. Tags never create a gameplay owner.
## Required test dimensions
Client/server/local; owner transfer; settings disabled; headless; normal restart/reconnect; async/rendering; receipts/recursion; exact SHA and initial state. Tests only when task authorizes them; current gameplay regression DEFERRED.
## Cross-domain review triggers
Shared-system mutation; mixed file; event/RPC/persistence format; generated-hit/XP coupling; Gold transaction/action boundary; UI framework vsdomain content; changed compile gate. Record SHARED SYSTEM CHANGE before mutation, affected-owner review, root integration.
## Deployment permissions
Validation planning by default; no runtime launch/deploy unless explicitly scoped. Release packaging always separate.
## Subagent policy
Follow AGENTS.md: SIMPLE root-only; NORMAL optional bounded read-only scout/reviewer; COMPLEX max2, third reviewer only justified. No overlapping parallel writes/shared integration delegation. Every assignment exact goal/files/permissions/non-goals/output.

## Presentation regression dimensions
Follow [VANILLA_ASSET_WORKFLOW](../../docs/architecture/VANILLA_ASSET_WORKFLOW.md); coordinate the mechanic owner and shared-framework consumers.
Check separately: asset identity/type resolved; visible/audible live; correct natural trigger and timing; scale/position/orientation/attachment; duplicate trigger; multiplayer visibility; owner/remote behavior; headless safety; async first-use/late load; cleanup; spam/concurrency; performance; VFX and SFX settings independently off/on.
Manual player observation is valid visual/audio evidence combined with logs/state where applicable. Static/manifest/menu/isolated audition evidence does not prove natural gameplay presentation. No runtime authorization inferred from these dimensions.

