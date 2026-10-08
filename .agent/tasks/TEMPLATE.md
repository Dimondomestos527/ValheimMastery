# <unique-task>
- Owner/profile:
- Goal:
- Acceptance criteria (observable):
- Scope/allowed files; exact write reservations:
- Non-goals:
- Complexity: SIMPLE / NORMAL / COMPLEX
- Relevant canonical docs:
- Relevant source/classes and build gates:
- Shared systems touched: NONE or explicit SHARED SYSTEM CHANGE block (file/system; reason; affected domains; regression).
- Required regression (static/live, variants, state):
- Deployment requirement: NONE unless explicit human task authorization; target/rollback/state protection if authorized.
- Status: PROPOSED / ACTIVE / BLOCKED / COMPLETE / DEFERRED
- Known dependencies/overlapping tasks:

## PRESENTATION / VANILLA ASSET PLAN

Use [VANILLA_ASSET_WORKFLOW](../../docs/architecture/VANILLA_ASSET_WORKFLOW.md); section may be N/A for non-presentation tasks.

Presentation required: YES/NO
Player-facing intent:

VFX:
- Selected/proposed vanilla asset names/paths; evidence/status (CANDIDATE / SOURCE VERIFIED / RUNTIME RESOLVED / LIVE VERIFIED):
- Scale (world/local), offsets, orientation:
- Attachment/socket; owner vs target; follow/detach:
- Trigger/timing/delay/repetition:
- Lifetime/fade, cleanup, density/intensity, visibility/overlap:
- Multiplayer behavior:

SFX:
- Selected/proposed vanilla source prefab/clip names/paths; evidence/status:
- Volume relative to vanilla reference; pitch/range:
- Spatial behavior; distance/rolloff; local vs nearby:
- Trigger/delay; loop/one-shot/start/stop/cleanup:
- Cooldown/concurrency/rapid repeated proc:

Runtime composition / native donor provenance:
Shared presentation systems touched / SHARED SYSTEM CHANGE if required:
Headless behavior:
Async preload/pending/retry/timeout/fallback/scene-exit behavior:
Performance risks / frequency/concurrency/particle/allocation/sound budgets:
Live validation required / cases and evidence:
User decisions pending / meaningful alternatives and explicit selected identity:

