# Valheim Mastery canonical development instructions

Scope: this directory and descendants. Source authority: src-modern/ + ValheimMasteryPoC.csproj + current Build123.ps1 compiled symbols. Baseline1.4.123, 198 active C# files. Never infer implementation from catalog/localization or old handoffs.

## Read discipline
Read specified profile/task, matching progress/handoff, named canonical docs and relevant source only. Do not load every document. Root/environment instructions still apply. docs/imported and old checkpoints are reference, not automatic authority. Source names are not a class-level ownership boundary; see SCOPE_MAP and SHARED_SYSTEMS.

Consult [REFERENCE_INDEX](docs/references/REFERENCE_INDEX.md) selectively when external engine/design guidance helps the active task. Do not automatically ingest the full library or require every task to read every reference. Actual current Valheim/Mastery source and canonical project policy override generic external guidance.

## Authority and deployment
A profile describes capability, NOT a standing authorization to perform work. Execute only active human task scope. Gameplay chats may modify their scoped source/build/static-QC when authorized; never automatically deploy/start client/server. Infrastructure deployment still requires explicit task scope. Release packaging is always separate. Production worlds/characters/config/Workshop/Gold state protected. Keep ValheimMasteryPoC.csproj name, namespace/class names, client/server variants distinct. No Git initialization/cleanup/migration by inference.
Current architecture task: docs only; gameplay regression postponed; no character/client/server launch or fixes.

## Shared changes and concurrency
Root owns task interpretation, architecture, shared decisions/integration, final changes/build/review/canonical docs.
Before any shared-system mutation record in task:
SHARED SYSTEM CHANGE:
- file/system
- reason
- affected domains
- regression required
Obtain affected-owner review before integration; root makes shared/core changes. Mixed files require class-level boundaries, never parallel edits. Chat profiles do not create isolated checkouts: all chats may share files. Reserve exact files in task/progress before write; if another task holds overlapping files or shared systems, stop that write and coordinate. No global CURRENT_TASK.

## Subagents
SIMPLE: root only. NORMAL: optional bounded read-only scout/reviewer. COMPLEX: maximum2 bounded subagents; a third read-only reviewer requires explicit task justification. Higher-level tool restrictions take precedence; optional delegation is not mandatory.
SCOUT/REVIEWER read-only by default. IMPLEMENTATION only isolated non-overlapping components/files, never shared integration. Parallel writes prohibited when sets overlap OR either touches shared systems.
Every assignment specifies exact goal, allowed files/scope, read/write permission, non-goals, required output. Example: read only native Turret target/ammo/ownership/persistence/network/prefab paths; return evidence, no changes.
Use no agents merely to fill domain profiles. Persistent profiles are files, not running agents.

## Anti-overthinking
SIMPLE inspect→modify→build/test. No broad research/agents.
NORMAL at most2 plausible approaches, choose one, implement.
COMPLEX deeper runtime/architecture analysis allowed; stop research once safe evidence sufficient. Reuse documented decisions; verify drift cheaply.
Never claim LIVE VERIFIED from source/static/menu-only results. Status vocabulary is defined in docs/architecture/CONTEXT_AND_STATUS.md.

## Task lifecycle
.agent/tasks/<domain>/<unique-task>.md; .agent/progress/<unique-task>.md; .agent/handoffs/<unique-task>.md.
Use templates and CHAT_BOOTSTRAP.md. Weighted measurable phases total100%; report phase evidence, no intuitive percentages. Blocked required step stays incomplete; defer needs explicit human scope decision.
Tests/test instructions and handoffs should be Ukrainian for this user; code identifiers unchanged.
Do not update external Codex memory without explicit request. Durable project context lives here.

## Global vanilla-only asset policy
No custom visual/audio assets may ship: no imported textures/meshes/animations/clips, custom AssetBundles or external art files. Use existing Valheim resources and Unity/runtime-native components/primitives only. Inspect real assets before proposing exact names; document identity/evidence, choice and tuning. The mechanic's gameplay owner owns its VFX/SFX; complexity creates no new domain. Reuse shared presentation infrastructure and coordinate shared changes. Obtain the user's decision for meaningful visual/audio choices among alternatives; honor prior explicit selection and never silently substitute it. Keep static implementation, runtime resolution and live visual/audio verification separate.
Canonical detail: [VANILLA_ASSET_WORKFLOW](docs/architecture/VANILLA_ASSET_WORKFLOW.md); investigated assets: [VANILLA_ASSET_CATALOG](docs/architecture/VANILLA_ASSET_CATALOG.md).

