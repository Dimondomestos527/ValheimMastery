# UTILITY35_70_PICKAXE_DESIGN_20261005
- Owner/profile: UTILITY_35_70, root integration.
- Goal: дослідити надмірно часте повне руйнування жили й семантику VFX/SFX; запропонувати обмежений redesign для рішення користувача.
- Acceptance criteria: джерельні шанси/вибір сегментів; native support interaction; межі collapse witness; два практичні варіанти з ризиками; handoff.
- Scope/reservations: ONLY this task, matching .agent/progress/UTILITY35_70_PICKAXE_DESIGN_20261005.md and .agent/handoffs/UTILITY35_70_PICKAXE_DESIGN_20261005.md. RESERVED for documentation. Source/native/logs read-only.
- Non-goals: gameplay edits, probability tuning implementation, builds, runtime launch/deployment, state/config changes, global support patch, external memory.
- Complexity: NORMAL; existing utility_prep_review bounded read-only native scout, no writes.
- Relevant canonical docs: AGENTS/profile; CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; SCOPE_MAP; SHARED_SYSTEMS; KNOWN_ISSUES; UTILITY_35_70.
- Source authority:1.4.123; src-modern/Pickaxes70SuperHit.cs, PerkRuntimeServices.cs, PerkVisualService.cs, VfxRecipes.cs, NativePerkAssetResolver.cs; actual assembly_valheim MineRock5.
- Shared systems touched: NONE (read-only).
- Future regression: ordinary versus forced proc; native support cascade; one cue per actual collapse; ForceProc off/on; owner/client/server; vein already nearly depleted; concurrent strikes; generated hit XP/drop recursion.
- Deployment: NONE; production state protected.
- Status: COMPLETE (read-only investigation/design), DESIGN ONLY; proposals not approved. Documentation reservations RELEASED.
- Dependencies: KI10 design pending; prior Run/Workshop candidate unchanged.
## Presentation
- Current source recipe: pickaxes_70 rock hit + lightning weapon accent / rock-destroyed sound; collapse rock-destroyed-large + Eikthyr stomp + native thunder flash + lightning hit / thunder (volume .65, throttle .8s).
- SOURCE VERIFIED identity/routing only; runtime resolution and visible/audible acceptance unverified in this task.
- Intent: strongest cue accompanies real whole-vein collapse once; local proc gets modest fracture cue. Asset changes require user choice, no substitutions implemented.
## Proposed alternatives (not approved)
- A: retain guaranteed breaks but reduce ordinary proc20% to10%, exactly1 extra neighbour, forced-collapse conditional chance20% to5% (nominal .5% per eligible damaging hit). Minimal change; support-fragile veins can still collapse frequently on proc. Lowering forced-collapse chance alone is insufficient.
- B (recommended): retain20% ordinary proc, replace guaranteed1-2 extra breaks with50% of incoming strike damage to each selected neighbour; conditional forced-collapse5% (nominal1% per eligible damaging hit). Tuning starting values, not measured balance. Lethal bonus damage can still remove support; no promise to prevent vanilla cascades.
- Presentation: modest fracture cue for actual ordinary extra damage; intended strongest existing collapse recipe for confirmed whole-vein destruction, once. Bind attribution to actual damage/support resolution rather than arbitrary2s health polling. Do not attribute all later vanilla collapses indefinitely to an old proc; exact causal window/owner semantics must be designed before implementation.
- Debug caveat: ForceProc overrides both rolls; near-always forced collapse during tests may be debug state, not normal probabilities. Current live state unknown.
