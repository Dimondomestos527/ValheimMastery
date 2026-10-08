# MAGIC100_BASELINE_20261004_OWNER
- Owner/profile: persistent MAGIC_100; .agent/profiles/MAGIC_100.md.
- Goal: інвентар фактичного Magic100 gameplay у authoritative source 1.4.123; кожен слот класифікований за execution evidence.
- Acceptance criteria: усі Magic100 catalog slots визначені; runtime callers/threshold/effect/compile gates простежені; класифікація IMPLEMENTED EXECUTION PATH / CATALOG ONLY / DESIGN ONLY / UNKNOWN / DEFERRED з evidence; 2 bounded read-only reviews; 3/3 records readback; source/project/build hashes unchanged.
- Scope/allowed files; exact write reservations: .agent/tasks/magic100/MAGIC100_BASELINE_20261004_OWNER.md; .agent/progress/MAGIC100_BASELINE_20261004_OWNER.md; .agent/handoffs/MAGIC100_BASELINE_20261004_OWNER.md. Лише ці три Markdown records; inventory у task. Hold до acceptance; source/shared/canonical-doc reservations NONE.
- Non-goals: додавання mechanics/design approval; Magic35/70 summons/zones/cargo; generic level100 passive scaling; shared Gold/Favor/Pantheon protocol; deployment/launch; packaging; Git initialization; external memory updates.
- Complexity: COMPLEX (execution trace у mixed source/build variants); максимум2 bounded read-only scouts/reviewers.
- Relevant canonical docs: AGENTS; MAGIC_100 profile; CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; SCOPE_MAP; SHARED_SYSTEMS; BUILD_RUNTIME_BASELINE; KNOWN_ISSUES; docs/gold/MAGIC_100.md.
- Relevant source/classes/gates: src-modern read-only; catalog/narrative/localization лише slot/content pointers; MasteryPlugin/Runtime/Events/dispatch; PerkRuntimeServices/PerkHitContext; MasteryStateStore; NetworkSync/OwnerSkillAuthority; ValheimMasteryPoC.csproj; tools/Build123.ps1. Exact trace буде доданий.
- Shared systems touched: NONE (read-only). Перед future mutation: SHARED SYSTEM CHANGE із file/system; reason; affected domains; regression required. Affected-owner review; root owns architecture/integration.
- Required regression: source/compile-evidence/static inventory, integrity і readback. Gameplay regression DEFERRED; source presence не LIVE VERIFIED. Existing client/server build evidence перевірити; без source change нова build не потрібна.
- Deployment requirement: NONE; worlds/characters/config/Workshop/Gold state protected.
- Status: COMPLETE (baseline inventory only); BASELINE / INVENTORY FIRST залишається чинним; implementation authorization відсутня.
- Dependencies/overlap: Combat100/Gold/Utility35_70 active reservations перевірено, overlap відсутній; Magic35_70 bootstrap COMPLETE. docs/gold/MAGIC_100.md read-only; Git не enabled.

## Inventory / evidence
Дата: 2026-10-04. Authority: current src-modern + current csproj + actual Build123 compiler inputs; не installed gameplay claim.

| Magic100 slot | Classification | Execution-path result | Slot/content pointer only |
|---|---|---|---|
| elementalmagic_100 (Skills.SkillType.ElementalMagic) | CATALOG ONLY | Немає підтвердженого caller -> threshold100 -> gameplay effect у current source/variants. Cast-copy/recursive echo consumer не знайдений. | PerkCatalog.cs:54,72-78; PerkLocalization.Ukrainian.cs:254-256 |
| bloodmagic_100 (Skills.SkillType.BloodMagic) | CATALOG ONLY | Немає підтвердженого caller -> threshold100 -> gameplay effect у current source/variants. Killed-enemy shadow resurrection/lifetime/nova consumer не знайдений. | PerkCatalog.cs:55,72-78; PerkLocalization.Ukrainian.cs:263-265 |

Counts: IMPLEMENTED EXECUTION PATH 0; CATALOG ONLY 2; DESIGN ONLY 0; UNKNOWN 0; DEFERRED 0 slots. Усі2 catalog magic slots охоплено. DESIGN ONLY не призначено: task не встановлює затверджений дизайн; localization claims не є approved design. DEFERRED стосується live regression/future implementation work, не змінює source classification слотів. Висновок про відсутність знайденого path є bounded static inference із поточного source/compiled evidence, а не доказ усіх можливих third-party runtime modifications.

### Current source trace і false positives
Шляхи нижче relative до canonical root; line references перевірено по current source.
- PerkCatalog.cs:54-55,72-78 реєструє magic skills та generic 35/70/100 definitions. PerkRuntimeServices.cs:51-54 HasPerk лише порівнює raw level з milestone; effect dispatcher із catalog відсутній.
- Elemental: FireStaff35Charge.cs:66-68 gate35; native Input/Attack.Start hooks :178-183, projectile/burst patches :191-208 працюють із existing charged projectile. Magic70IceStorm.cs:37 і Magic70Surtling.cs:82,128-129 gate70; native input hooks :190-192 / :654-656. PerkGameplayPatches35.cs:157-168 free-cast branch gate35. PerkGameplayPatches70Combat.cs:224-242 Elemental70 observer gate70. Ці paths не є100.
- Blood: Magic70BloodDome.cs:53,121-123 gate70; input :529-531. Magic70CarrierRuntime.cs:141,314-315 gate70; input :741-751. BloodSkeleton35Service.cs:149 gate35. BloodMagic35Perk.cs:116-120 legacy summon death-nova Harmony patch має Prepare()=>false; dormant body :139-141 gate35. Наявність nova body у compiled assembly не означає activated patch або Blood100 shadow mechanic.
- MagicSkillPassives.cs:9-10,25-26,50-51: continuous Eitr/healing scaling, clamp100; XP_SKILL_BONUSES. IceStaff35Trail.cs:49,56,114:35 effect з level-dependent duration, MAGIC_35_70. Neither establishes a100 milestone action.
- MasteryPlugin.cs:31 PatchAll; :44-55 registered ticks include magic70. NetworkSync.cs:16-43 RPC registrations magic35/70 only; :221-253 generic ClientAbility dispatcher має інші35/70 consumers, Magic100 branch не знайдено.
- OwnerSkillAuthority.cs:27-28,109-112 VM_OwnerMagicV1 gated MASTERY_MAGIC70_EXPERIMENT. Generic Has API :56-64 може отримати довільний level, але actual magic callers BloodDome.cs:123, CarrierRuntime.cs:315, Surtling.cs:129,263 перевіряють70. Snapshot :19,38,63,71 transient; не Magic100 durable mechanic state.
- MasteryEvents.cs:52-60; MasteryEventsExtended.cs:28-69 generic event buses. MasteryEventDispatchPatches.cs:42-45 publish EnemyKilled/HitResolved, :63-76 AttackStarted; EnemyKilled capture attacker Player. Source-wide subscriber search не знайшов Magic100 gameplay subscriber; SkillXp subscription GoldCraftingService.cs:55 -> Crafting/Favor.
- MasteryStateStore.cs:19-25 generic weak-table transient storage, Magic100 consumer/state type не знайдено. PerkRuntimeServices.cs:9-24,61-82 generated-hit payload/recursion metadata не є echo/shadow implementation; actual GenerationDepth/AllowShadowRecursion assignment у Knife70ShadowStrike.cs:146,151. Generic durable cooldown service :112-148 не має Magic100 caller/key.
- PerkCatalog.cs:112-127 generic milestone m_customData keys; MasteryPatches.cs:82-88,101-105 generic milestone recognition/death floor100; :112-139 -> PerkVisualService.PlayMilestone. PerkVisualService.cs:10-17 -> raven tutorial/presentation; MasteryRavenTutorials.cs:25-35 queues generic100/gold tutorial. Це source execution спільної progression/UX, не Magic100 action.
- GoldCraftingService.cs:144-145 authority Crafting100; :269-272 і GoldFavorService.cs:89-104 XP/Favor пропускає Crafting. GoldDivineTransactions.cs:23-35 permits action kinds1/2; :306-325 craft Masterwork/upgrade. GoldCraftingLedger.cs:38 kinds1 craft/2 upgrade. Magic100 action branch/receipt consumer не знайдено; shared protocol залишається GOLD_CORE.
- PerkDebugService.cs:34-38 vm perk <arbitrary label> force лише виставляє global ForceProc=true; PerkRuntimeServices.cs:56-58 forces chance rolls existing handlers. Успішний command label не перевіряє існування механіки.

### Search coverage / відтворення
Search universe: усі198 current src-modern C#; project/build gates; relevant source callers, native cast/projectile/death hooks, catalog consumers, event subscribers, state/authority/Gold action boundaries. Старі proposals/imports/metadata не використовувались як implementation evidence.
Reproduce from canonical root:
```powershell
rg -n -i 'elementalmagic_100|bloodmagic_100|magic100|magic_100|elemental.{0,25}100|blood.{0,25}100|100.{0,25}(magic|blood|elemental)' src-modern
rg -n '(HasPerk|Has|IsUnlocked|GetActualSkillLevel|Milestone|milestone).{0,80}(100|ElementalMagic|BloodMagic)' src-modern
rg -n '(GenerationDepth|AllowSelfProc|AllowShadowRecursion|AppliedPerks)' src-modern
rg -n 'MasteryEvents\.|MasteryEventsExtended\.' src-modern
```
Після broad search читати matching class/callers та actual #if/Prepare; відсутність exact ID сама по собі недостатня для absent-path claim.

### Build / compiled evidence (READ-ONLY)
- ValheimMasteryPoC.csproj:8 version1.4.123; :20 explicit src-modern/**/*.cs; :21 conditional removal of3 Magic70 files. tools/Build123.ps1:4 constants, :5-8 distinct client/server targets. Нових builds не виконано.
- Actual latest compiler logs: validation/tooling-blockers-20261004/Perks123Client.build.log:38; Perks123Server.build.log:14. Both commands contain198 src-modern C# paths and the same11 feature symbols as current Build123: MASTERY_RELEASE_SAFE; MASTERY_CLUB_PASSIVES; MASTERY_SPEAR35_EXPERIMENT; MASTERY_WORKSHOP_REMOTE_EXPERIMENT; MASTERY_SPEAR70_EXPERIMENT; MASTERY_MAGIC70_EXPERIMENT; MASTERY_CLUBS35_EXPERIMENT; MASTERY_CLUBS70_EXPERIMENT; MASTERY_SHIELD35_EXPERIMENT; MASTERY_SHIELD_RUSH_EXPERIMENT; MASTERY_CARRIER70_EXPERIMENT. No Magic100-specific symbol/path established.
- Both validation/tooling-blockers-20261004/*.build-result.csv:2 ExitCode0; correct distinct Managed inputs and isolated intermediates. Latest tooling report supersedes older independent-build/clean-QC provenance limitations; older QC failures are not blanket gameplay status.
- Current bin/Perks123Client/ValheimMastery.dll: AssemblyVersion1.4.123.0, SHA256839D03955995ECBC5E3B80698D0E8BB9CE86A6C5072183F38868D74B15EFD409.
- Current bin/Perks123Server/ValheimMastery.dll: AssemblyVersion1.4.123.0, SHA25695DEB8ADD0629FC7E37D1C42F9CFBFDED53688113274CB265164A4259B47C241.
- Root read-only Mono.Cecil0.11.5 ReadAssembly inspection of BOTH current binaries:2963 methods each;21 direct constant-skill PerkRuntimeService.HasPerk magic callsites each (12 threshold35,9 threshold70,0 threshold100); all6 Magic100 ldstr tokens each only in PerkLocalization.AddUkrainianPerkWords. No plugin execution. Inspection counts include dormant Prepare()=>false bodies. Direct-call pattern inspection is bounded (constant skill/threshold immediately preceding HasPerk); it is not by itself an exhaustive proof against variable/dynamic calls. Source/caller/reviewer trace covers that gap.
- Current198 source hashes compared with validation/independent-build-20261004/immutable-dev-before.csv:198/198 unchanged,0 differences. Existing tooling report plus reference-il-comparison.json establishes later binary comparison2963/2963,0 IL/reference differences; root did not rerun historic full QC.

### Bounded scout/reviewer reconciliation
1. native_magic100_scout: READ-ONLY current magic cast/projectile/death hooks, mixed classes, shared generation fields, event/subscriber and debug false positives. Returned both CATALOG ONLY, gates35/70; dormant Blood35 nova excluded; no writes/build/launch.
2. magic100_authority_reviewer: READ-ONLY registration/RPC/owner authority, event subscriptions, transient/durable state and Gold action consumers. Returned both CATALOG ONLY; generic snapshot/milestone/floors/cooldowns and Crafting action kinds excluded; no writes/build/launch.
Root independently reconciled with source, actual compiler symbols, current DLL metadata/direct-call inspection. No conflicting execution evidence remains; no new architecture/design decision or shared-system mutation.

### Acceptance / continuation
- Source/project/build integrity:200/200 protected files unchanged during task,0 differences; DLL identity checked above.
- Gameplay regression explicitly DEFERRED; LIVE VERIFIED is not established or claimed. Full domain completion/feature readiness is not implied by100% baseline documentation.
- Changes: exactly3 task/progress/handoff records; runtime/source/build/config/world/character/Workshop/Gold state unmodified by this task. Canonical docs/gold/MAGIC_100.md remains unchanged.
- Exact next action: on next substantive human request, read this task/progress/handoff and current source/gates; create unique task with exact reservations. Until explicit implementation authorization, continue evidence/inventory only. After authorization root owns mechanic-specific design, integration and validation; declare shared changes before mutation and obtain bounded affected-owner review.

