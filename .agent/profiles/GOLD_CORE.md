# Gold Core / Pantheon
Profile ID: GOLD_CORE; task directory: .agent/tasks/gold/.
## Owned scope
Shared100 infrastructure: milestone/ACK, Favor/Pantheon/exhaustion, ledger, receipts/idempotency, transaction protocol, shared Gold persistence/UI.
## Explicitly out of scope
Specific100 gameplay effects/cost policy without action-owner review;35/70; generic UI framework. No work outside active human task.
## Read canonical documentation
AGENTS.md; docs/architecture/CONTEXT_AND_STATUS.md; docs/architecture/SCOPE_MAP.md; docs/architecture/SHARED_SYSTEMS.md; docs/KNOWN_ISSUES.md; docs/gold/PANTHEON_CORE.md. Read only relevant shared sections.
## Known source/class pointers
GoldCraftingLedger.cs, GoldFavorModel.cs, GoldFavorService.cs, GoldFavorTelemetry.cs, GoldReceiptPolicy.cs, GoldCharacterSave.cs, GoldPantheonUi.cs, GoldInspirationStatus.cs; protocol portions GoldCraftingService.cs/GoldDivineTransactions.cs.
Source in src-modern unless tools/QC/project named. Actual source filenames from198-file inventory; mixed-file class boundaries in SHARED_SYSTEMS. Check classes and #if before editing. Source presence is not LIVE VERIFIED.
## Shared systems to inspect when relevant
MasteryPlugin/MasteryRuntime; MasteryEvents/dispatch; PerkRuntimeServices (PerkHitContext); MasteryStateStore; NetworkSync/OwnerSkillAuthority; catalog/UI/feedback. Gold protocol only where used.
## Technical regression tags
GOLD_LEDGER RECEIPT_IDEMPOTENCY CRASH_RECOVERY SAVE_ORDER RPC_AUTHORITY; add HEADLESS RECONNECT SERVER_RESTART where state/runtime affected. Tags never create a gameplay owner.
## Required test dimensions
Near-cap/999→1000; ACK/receipt retry; auth; player+world durable consistency; reconnect/restart; separately authorized disposable crash tests; no split rollback. Tests only when task authorizes them; current gameplay regression DEFERRED.
## Cross-domain review triggers
Shared-system mutation; mixed file; event/RPC/persistence format; generated-hit/XP coupling; Gold transaction/action boundary; UI framework vsdomain content; changed compile gate. Record SHARED SYSTEM CHANGE before mutation, affected-owner review, root integration.
## Deployment permissions
May inspect and modify owned source/build client+server/static QC only as authorized by active task. No automatic runtime deployment. Release packaging always separate.
## Subagent policy
Follow AGENTS.md: SIMPLE root-only; NORMAL optional bounded read-only scout/reviewer; COMPLEX max2, third reviewer only justified. No overlapping parallel writes/shared integration delegation. Every assignment exact goal/files/permissions/non-goals/output.

## Mechanic-specific presentation
GOLD_CORE owns presentation semantics tied to its shared ACK/Favor/Pantheon/ceremony behavior; action-specific VFX/SFX/timing/HUD content remains with the relevant100 gameplay owner. The gameplay owner of a mechanic also owns its mechanic-specific presentation; complexity creates no new gameplay domain. Consult CORE_UX_TUTORIALS for common UI/presentation framework; shared components require root/affected-owner coordination.
Follow [VANILLA_ASSET_WORKFLOW](../../docs/architecture/VANILLA_ASSET_WORKFLOW.md): native resources only, verified choices/tuning, meaningful user decisions, no silent selected-asset replacement; live evidence separate from static.

