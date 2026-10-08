# Utility100
Profile ID: UTILITY_100; task directory: .agent/tasks/utility100/.
## Owned scope
Actual utility100 effects: confirmed Pickaxes metal-weight source branch; Crafting100 divine crafting/forge action semantics; untraced slots remain unresolved.
## Explicitly out of scope
Gold ledger/receipt protocol ownership; other slots absent path; utility35/70. No work outside active human task.
## Read canonical documentation
AGENTS.md; docs/architecture/CONTEXT_AND_STATUS.md; docs/architecture/SCOPE_MAP.md; docs/architecture/SHARED_SYSTEMS.md; docs/KNOWN_ISSUES.md; docs/gold/UTILITY_100.md. Read only relevant shared sections.
## Known source/class pointers
PerkRuntimeServices.cs:PerkProfessionService metal HasPerk(Pickaxes,100); action portions GoldCraftingService.cs/GoldDivineTransactions.cs; GoldMasterworkTooltip.cs.
Source in src-modern unless tools/QC/project named. Actual source filenames from198-file inventory; mixed-file class boundaries in SHARED_SYSTEMS. Check classes and #if before editing. Source presence is not LIVE VERIFIED.
## Shared systems to inspect when relevant
MasteryPlugin/MasteryRuntime; MasteryEvents/dispatch; PerkRuntimeServices (PerkHitContext); MasteryStateStore; NetworkSync/OwnerSkillAuthority; catalog/UI/feedback. Gold protocol only where used.
## Technical regression tags
INVENTORY_CAS GOLD_LEDGER RECEIPT_IDEMPOTENCY SAVE_ORDER; add HEADLESS RECONNECT SERVER_RESTART where state/runtime affected. Tags never create a gameplay owner.
## Required test dimensions
Weight stacking/item types; Crafting100 ACK/inspiration/craft/forge commit; item metadata; Favor action integration; no forced corruption tests without explicit scope. Tests only when task authorizes them; current gameplay regression DEFERRED.
## Cross-domain review triggers
Shared-system mutation; mixed file; event/RPC/persistence format; generated-hit/XP coupling; Gold transaction/action boundary; UI framework vsdomain content; changed compile gate. Record SHARED SYSTEM CHANGE before mutation, affected-owner review, root integration.
## Deployment permissions
May inspect and modify owned source/build client+server/static QC only as authorized by active task. No automatic runtime deployment. Release packaging always separate.
## Subagent policy
Follow AGENTS.md: SIMPLE root-only; NORMAL optional bounded read-only scout/reviewer; COMPLEX max2, third reviewer only justified. No overlapping parallel writes/shared integration delegation. Every assignment exact goal/files/permissions/non-goals/output.

## Mechanic-specific presentation
The gameplay owner of a mechanic also owns its mechanic-specific VFX/SFX design, exact semantic trigger/timing and HUD values/content. VFX/SFX complexity does not create a separate gameplay domain. Consult CORE_UX_TUTORIALS for shared UI/presentation framework issues; coordinate shared-component changes with affected owners and root.
Follow [VANILLA_ASSET_WORKFLOW](../../docs/architecture/VANILLA_ASSET_WORKFLOW.md): native resources only, evidence-backed asset identity, documented choice/tuning, meaningful user choices and separate live verification.

