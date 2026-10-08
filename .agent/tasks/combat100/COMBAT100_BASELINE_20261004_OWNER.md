# COMBAT100_BASELINE_20261004_OWNER
- Owner/profile: COMBAT_100; .agent/profiles/COMBAT_100.md. Постійний owner chat Combat100.
- Goal: зафіксувати source-derived інвентаризацію 11 non-magic combat100 slots для 1.4.123.
- Authorization: прямий запит користувача 2026-10-04: BASELINE / INVENTORY FIRST; inventory та design/review only. Implementation потребує окремого явного дозволу.
- Acceptance criteria: bootstrap і canonical docs прочитані; project/plugin version та фактичні build symbols звірені; 11/11 slots класифіковано з source evidence; networking/persistence/generated-hit/lifecycle review зафіксовано; task/progress/handoff перечитані; source/build fingerprints незмінні.
- Scope/allowed files; exact write reservations: .agent/tasks/combat100/COMBAT100_BASELINE_20261004_OWNER.md; .agent/progress/COMBAT100_BASELINE_20261004_OWNER.md; .agent/handoffs/COMBAT100_BASELINE_20261004_OWNER.md. Тільки ці три Markdown-файли; резервування утримується до final verification.
- Non-goals: source/config/build-script/runtime/state changes; implementation; build/test execution; gameplay/client/server launch; deployment/release; Git initialization; historical design reconstruction; native API/prefab research без конкретного mechanic question.
- Complexity: NORMAL; root owns final evidence and architecture. Один bounded read-only reviewer для ризиків; scout native mechanics не потрібний, поки не знайдено конкретного execution path чи authorized feature.
- Relevant canonical docs: AGENTS.md; COMBAT_100 profile; CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; SCOPE_MAP; SHARED_SYSTEMS; BUILD_RUNTIME_BASELINE; KNOWN_ISSUES; docs/gold/COMBAT_100.md.
- Source authority: MasteryDev/ValheimMastery/src-modern + ValheimMasteryPoC.csproj + tools/Build123.ps1; 1.4.123. Catalog/localization/narrative використовуються лише для slot navigation, не implementation evidence.
- Shared systems touched: NONE (read-only inspection; тільки owner task documents). Перед будь-якою майбутньою shared mutation: SHARED SYSTEM CHANGE з file/system, reason, affected domains, regression; affected-owner review; root integration.
- Required regression: тут readback/source integrity only. Gameplay DEFERRED by current scope; не LIVE VERIFIED. Future risk tags: GENERATED_HIT_RECURSION PROJECTILE_OWNER GOLD_LEDGER RPC_AUTHORITY; HEADLESS RECONNECT SERVER_RESTART коли state/runtime affected.
- Deployment requirement: NONE. Worlds/characters/config/Workshop/Gold state protected.
- Known dependencies/overlapping tasks: ARCHITECTURE_20261004 COMPLETE. MAGIC35_70_BASELINE_20261004_OWNER резервує власні три документи; overlap відсутній. Canonical docs не редагуються.
- Status: COMPLETE (baseline inventory only; owner mode remains BASELINE / INVENTORY FIRST).

## Per-slot inventory — 1.4.123, 2026-10-04
Класифікація стосується окремого combat100 action/effect. CATALOG ONLY означає: slot metadata є, але concrete combat100 execution path не знайдено в поточному source/compile-gate trace. Це не runtime PASS і не твердження про відсутність level-dependent scaling чи 35/70 effects на персонажі level100.

| Slot / catalog ID | Classification | Current source trace; excluded false positive |
|---|---|---|
| Swords / swords_100 | CATALOG ONLY | PerkCatalog.cs:43,78 — slot only; Swords70Perk.cs:148,233 — threshold70; :218,259 — threshold35. Окремого100 branch не знайдено. |
| Axes / axes_100 | CATALOG ONLY | PerkCatalog.cs:44,78; PerkGameplayPatches70Combat.cs:51,92 — threshold70; PerkGameplayPatches35Objects.cs:34 — threshold35; :83-87 geometry continuous scaling не100 milestone. |
| Clubs / clubs_100 | CATALOG ONLY | PerkCatalog.cs:45,78; Clubs70Reservation.cs:106,157,181 та Clubs70EchoService.cs:117 — threshold70; charge/echo не100; PerkBalance.cs:70-100 — skill stagger/scaling. |
| Knives / knives_100 | CATALOG ONLY | PerkCatalog.cs:46,78; Knife70ShadowStrike.cs:239 — threshold70; ShadowStep35.cs:38 — threshold35; PerkGameplayPatches35.cs:88 continuous backstab scaling не100 milestone. |
| Spears / spears_100 | CATALOG ONLY | PerkCatalog.cs:47,78; Spear70Hook.cs:117,193 — threshold70; SpearThrowLifecycle.cs:41,50 — threshold35; PerkGameplayPatches35.cs:113-130 та Spear70Hook.cs:133 — level scaling. |
| Polearms / polearms_100 | CATALOG ONLY | PerkCatalog.cs:48,78; PerkGameplayPatches70Polish.cs:230,297 — threshold70; Polearm35AutoSpin.cs:111,146 — threshold35. |
| Bows / bows_100 | CATALOG ONLY | PerkCatalog.cs:49,78; Overdraw70V2.cs:29,65 — threshold70; Bows70WeakPointPerk.cs:29,53,75 фактично threshold35, попри filename70. |
| Crossbows / crossbows_100 | CATALOG ONLY | PerkCatalog.cs:50,78; PerkGameplayPatches35Missing.cs:218,237 та PerkGameplayPatches35Objects.cs:362 — threshold35. Crossbows70Perk.cs:1 #if !MASTERY_RELEASE_SAFE excluded current build; :27 threshold70 навіть у legacy branch. Crossbow100 execution path не знайдено. |
| Unarmed / fists_100 | CATALOG ONLY | PerkCatalog.cs:51,78; Fists70Maul.cs:142,238 — threshold70; PerkGameplayPatches35Missing.cs:88,134,166 — threshold35; FistsPassiveScaling.cs:21,43 та PerkGameplayPatches35Missing.cs:200-201 — generic level scaling -> XP_SKILL_BONUSES. |
| Blocking / blocking_100 | CATALOG ONLY | PerkCatalog.cs:52,78; ShieldRush70.cs:30 та Blocking70Reflection.cs:19,96 — threshold70; BlockingStoredPressure.cs:20 та PerkGameplayPatchesA1.cs:72 — threshold35. Magic shield належить MAGIC_35_70. |
| Dodge / dodge_100 | CATALOG ONLY | PerkCatalog.cs:53,78; Dodge70Refund.cs:44,78 — threshold70; PerkGameplayPatches35Objects.cs:210,253 — threshold35; learned ZDO flag у MovementPerks.cs:35 теж70. |

Totals: IMPLEMENTED EXECUTION PATH 0; CATALOG ONLY 11; DESIGN ONLY 0; UNKNOWN 0 within completed source trace; DEFERRED 0 slots. Gameplay validation та implementation work DEFERRED окремо; не маскувати catalog slots статусом DEFERRED. Historical designs не досліджувалися; DESIGN ONLY=0 не означає відсутність будь-яких старих design texts.

## Evidence and compile authority
- Project ValheimMasteryPoC.csproj Version=1.4.123; MasteryPlugin.cs:13 Version=1.4.123. Compile Include=src-modern/**/*.cs; current disk198 source files.
- tools/Build123.ps1:4 declares MASTERY_RELEASE_SAFE; MASTERY_CLUB_PASSIVES; MASTERY_SPEAR35_EXPERIMENT; MASTERY_WORKSHOP_REMOTE_EXPERIMENT; MASTERY_SPEAR70_EXPERIMENT; MASTERY_MAGIC70_EXPERIMENT; MASTERY_CLUBS35_EXPERIMENT; MASTERY_CLUBS70_EXPERIMENT; MASTERY_SHIELD35_EXPERIMENT; MASTERY_SHIELD_RUSH_EXPERIMENT; MASTERY_CARRIER70_EXPERIMENT.
- Existing validation/tooling-blockers-20261004/Perks123Client.build.log and Perks123Server.build.log actual compiler /define lists match these symbols. Both name198 source files; source filename sets compared with current disk: difference0 in each variant. File included in compiler input does not imply every #if class is emitted.
- Existing publicizer-provenance.csv:4 client/server rows, MvidMatch/ReferenceGraphMatch/CompilerPathMatch all True. Existing reference-il-comparison.json:1.4.123,2963 methods per variant, Differences=[],ReferenceDifferences0. These are preserved evidence, not checks rerun by this task.
- Current client DLL SHA256 839D03955995ECBC5E3B80698D0E8BB9CE86A6C5072183F38868D74B15EFD409; current server DLL SHA256 95DEB8ADD0629FC7E37D1C42F9CFBFDED53688113274CB265164A4259B47C241. Disk fingerprints rechecked; existing reports identify the same hashes. A hash match alone does not establish source-to-binary equivalence or gameplay behavior.
- Existing Unity/controlled-client smoke evidence establishes Harmony registration only. No Combat100 gameplay PASS inferred. No fresh build, binary scan or gameplay regression executed here.

## Search method and ownership exclusions
1. Whole canonical source search for numeric100/100f, _100 and named100 paths, excluding localization/narrative as implementation evidence. Combat100 IDs found in content/catalog, no dedicated effect consumer found.
2. Enumerated HasPerk, OwnerSkillAuthority.Has, IsUnlocked, raw/effective skill level and milestone/catalog consumers. Concrete combat guards traced to35/70;100 guards in current effect source belong Crafting Gold or Pickaxes weight.
3. PerkRuntimeServices.cs:51-54 generic HasPerk is eligibility utility, not a concrete action. :183,199 Pickaxes100 metal weight belongs UTILITY_100.
4. MasteryPatches.cs:81-105 saved milestone flags/death floors belong XP_SKILL_BONUSES; :110-141 milestone presentation belongs CORE_UX_TUTORIALS. MarkEverUnlocked/Gold-colored HUD/tutorial at100 does not establish a combat action.
5. GoldCraftingService.cs:143-145 HasGold hard-codes Crafting100. GoldDivineTransactions.cs:10-37 action Kind1/2 represents crafting/masterwork/upgrade, not combat effects. Shared Favor/Pantheon/ledger/receipt protocol remains GOLD_CORE; crafting action semantics UTILITY_100.
6. Bows filename70 with threshold35 and Crossbows70 compiled exclusion demonstrate why filename/catalog classification is insufficient.
7. Native mechanics/prefabs/API scout not invoked: no concrete Combat100 mechanic implementation found and no authorized feature design to resolve. Future bounded scout must name exact native mechanics/classes/prefabs and stop after sufficient evidence.

## Risk review — completed, root reconciled
Один bounded read-only reviewer combat100_risk_review; source-only, no writes/build/native/gameplay execution. Root повторно перевірив ключові authority/generated-context/persistence paths. Reviewer незалежно не знайшов non-magic combat100 action consumer. Нижче future integration requirements, не оголошені нинішні Combat100 bugs.

| Risk | Source evidence and future acceptance |
|---|---|
| RPC_AUTHORITY / PROJECTILE_OWNER | OwnerSkillAuthority.cs:47-66,93-112,161-178: remote non-magic snapshot містить Crafting/Spears/Unarmed/Ride; Crossbows/Bows/Swords/Axes/Clubs/Knives/Polearms/Blocking/Dodge не передаються цим protocol. Для майбутнього Crossbow100 server action потрібне явне authority рішення та shared-change declaration; peer-character-player binding, snapshot freshness, range/target/ownership validation, stale/replay/foreign reject, headless matrix. Не вважати client-provided skill snapshot самостійним server-owned skill ledger. |
| Persistence / GOLD_LEDGER | MasteryStateStore.cs:7-24 weak-table state transient. PerkRuntimeServices.cs:115-147 cooldown customData/UTC; GoldCraftingService.cs:104-125 exhaustion online/game time. Спершу визначити lifetime/clock/save/restore. Favor reserve/execute/settle/recovery проектувати з GOLD_CORE; crafting receipts автоматично не узагальнюються на combat effect. Death/logout/reconnect/server restart/crash acceptance потрібні, якщо state/cost має переживати їх. |
| GENERATED_HIT_RECURSION / XP | PerkRuntimeServices.cs:9-24,29,61-83 context process-local; variant/tag detection існує окремо. MasteryEventDispatchPatches.cs:29-40 constructs event from GetHitContext; default bool false не активує ?? fallback, а event не викликає IsPerkGenerated(hit). Transport-restored tag/variant сам по собі не гарантує context flags. Перед новим consumer визначити/перевірити normalized source/depth/self-proc/cross-proc/reflect/execution/payload/XP policy. Включити35/70 generated hits, new100 hits, RPC serialization і once-only attribution. Конкретну нову shared mutation ще не запропоновано; автоматично нічого не виправляти. |
| Lifecycle / HEADLESS / RECONNECT / SERVER_RESTART | OwnerSkillAuthority.cs:68-82; GoldCraftingService.cs:94-105,127-130; GoldDivineTransactions.cs:361-376 reset existing state. MasteryEvents/MasteryEventsExtended static buses потребують explicit unsubscribe/cleanup нових consumers; MasteryPlugin.cs:57 не universal teardown нового feature. Future controller/projectile acceptance: death, teleport, weapon change, owner migration, despawn, disconnect/reconnect, scene/world/session change, settings disabled/headless. |

## Design/review conclusion
- Baseline establishes11 CATALOG ONLY slots,0 dedicated execution paths. Дизайн нових механік цією задачею не затверджено.
- Сильніший наступний крок: окремий human-authorized design task для однієї конкретної100 механіки; визначити trigger/input/target/effect/cost/authority/state/cleanup/recursion до implementation. Crossbow100 може бути великим isolated feature цього owner chat після explicit implementation authorization.
- Root owns final architecture/integration. Bounded native scout потрібний лише для конкретного native question, exact classes/prefabs/APIs; reviewer — networking/persistence/generated-hit/lifecycle; overlap/shared writes ніколи не делегуються паралельно.
- Жодного SHARED SYSTEM CHANGE зараз: shared reads only; mutations NONE.



## Final verification
203/203 protected files checked against the pre-inventory SHA256 snapshot:198 C# source files + project/NuGet/build script + current client/server DLLs. Changes0, missing0, extra0 in this set. Three reserved documents read back with11/11 catalog-only rows and required handoff/risk sections. Only these three owner documents written; shared-system/gameplay mutations NONE. Reservations RELEASED. Baseline documentation scope COMPLETE; implementation/gameplay regression not authorized and not performed.

