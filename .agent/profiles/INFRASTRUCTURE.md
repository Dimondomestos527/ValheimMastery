# Infrastructure (technical, not gameplay owner)
Profile ID: INFRASTRUCTURE; task directory: .agent/tasks/infrastructure/.
## Owned scope
Project/build/QC/dependency provenance/deploy tooling; variant gates and package boundaries.
## Explicitly out of scope
Gameplay balancing/mechanics; archive migration/cleanup; automatic install or release packaging. No work outside active human task.
## Read canonical documentation
AGENTS.md; docs/architecture/CONTEXT_AND_STATUS.md; docs/architecture/SCOPE_MAP.md; docs/architecture/SHARED_SYSTEMS.md; docs/KNOWN_ISSUES.md; docs/architecture/BUILD_RUNTIME_BASELINE.md. Read only relevant shared sections.
## Known source/class pointers
ValheimMasteryPoC.csproj; NuGet.config; tools/Build123.ps1, BuildReferenceAudit.targets, BuildDependencyProvenance.ps1, Install123.ps1; qc-* source/projects.
Source in src-modern unless tools/QC/project named. Actual source filenames from198-file inventory; mixed-file class boundaries in SHARED_SYSTEMS. Check classes and #if before editing. Source presence is not LIVE VERIFIED.
## Shared systems to inspect when relevant
MasteryPlugin/MasteryRuntime; MasteryEvents/dispatch; PerkRuntimeServices (PerkHitContext); MasteryStateStore; NetworkSync/OwnerSkillAuthority; catalog/UI/feedback. Gold protocol only where used.
## Technical regression tags
HEADLESS OWNER_AUTHORITY RPC_AUTHORITY ASYNC_ASSETS; add HEADLESS RECONNECT SERVER_RESTART where state/runtime affected. Tags never create a gameplay owner.
## Required test dimensions
Separate client/server SHA/reference provenance; clean build/static QC/Harmony; no generated-output dependency leakage; explicit rollback and protected state. Tests only when task authorizes them; current gameplay regression DEFERRED.
## Cross-domain review triggers
Shared-system mutation; mixed file; event/RPC/persistence format; generated-hit/XP coupling; Gold transaction/action boundary; UI framework vsdomain content; changed compile gate. Record SHARED SYSTEM CHANGE before mutation, affected-owner review, root integration.
## Deployment permissions
Own tooling, but execute deployment only if explicitly in active task; preserve rollback/state and distinct variants. Release packaging always separate.
## Subagent policy
Follow AGENTS.md: SIMPLE root-only; NORMAL optional bounded read-only scout/reviewer; COMPLEX max2, third reviewer only justified. No overlapping parallel writes/shared integration delegation. Every assignment exact goal/files/permissions/non-goals/output.

