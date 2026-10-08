# UTILITY100_BASELINE_20261004_OWNER
- Owner/profile: UTILITY_100; .agent/profiles/UTILITY_100.md. Постійний owner chat Utility100.
- Goal: bootstrap 1.4.123 та обмежений source-derived baseline підтверджених Pickaxes100/Crafting100 paths, regression design і transaction/item-metadata risk register.
- Authorization: прямий запит користувача 2026-10-04; BASELINE / READ-ONLY. Дозволено читання й власні task/progress/handoff записи; gameplay changes потребують явного дозволу.
- Acceptance criteria: bootstrap/profile/canonical docs прочитані; version та build gates звірені; обидва підтверджені paths простежені від hooks до effects; Crafting100 costs/metadata/modifiers/save/refund boundaries описані; bounded read-only review виконано; Pickaxes100 regression matrix визначена без запуску; три записи перечитані; source/project/build integrity незмінна.
- Scope/allowed files; exact write reservations: лише .agent/tasks/utility100/UTILITY100_BASELINE_20261004_OWNER.md; .agent/progress/UTILITY100_BASELINE_20261004_OWNER.md; .agent/handoffs/UTILITY100_BASELINE_20261004_OWNER.md. Резервування до final readback; без global CURRENT_TASK.
- Non-goals: source/config/build edits; build/QC/gameplay execution; deployment/client/server launch; production state доступ для змін; crash/corruption experiments; release packaging; Git init/cleanup; повна інвентаризація непідтверджених Utility100 slots або inference з catalog/descriptions.
- Complexity: NORMAL. Один bounded read-only reviewer для Crafting100 persistence/transaction/item-metadata risk; root перевіряє evidence і володіє фінальними записами. Ніяких parallel source writes.
- Relevant canonical docs: AGENTS.md; profile; CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; SCOPE_MAP; SHARED_SYSTEMS; BUILD_RUNTIME_BASELINE; KNOWN_ISSUES; docs/gold/UTILITY_100.md.
- Source authority: src-modern/ + ValheimMasteryPoC.csproj + tools/Build123.ps1; project/plugin Version 1.4.123; 198 source C# files. Наявність не доводить live behavior.
- Relevant source/classes: MasteryInventoryWeightPatch; PerkProfessionService; PerkRuntimeService.HasPerk; GoldCraftingService action eligibility/arming; GoldAction; GoldDivineTransactions action methods/patches; GoldMasterworkTooltip; bounded consumers of VM_Masterwork. Gold ledger/receipt/Favor/save implementation читається лише для action boundary.
- Build gates: project включає src-modern/**/*.cs; Build123 задає MASTERY_RELEASE_SAFE та experiment symbols і окремі Perks123Client/Perks123Server. Подробиці paths/gates у evidence після trace; build не запускається.
- Shared systems touched: NONE (read-only inspection). Перед future shared mutation: SHARED SYSTEM CHANGE з file/system, reason, affected domains, regression required; Gold Core affected-owner review для protocol; root integration.
- Required regression: тут readback/integrity і regression design only. Gameplay DEFERRED. Technical tags: INVENTORY_CAS GOLD_LEDGER RECEIPT_IDEMPOTENCY SAVE_ORDER; HEADLESS RECONNECT SERVER_RESTART для майбутнього state/runtime scope. Tests/crash experiments лише в окремому authorized task.
- Deployment requirement: NONE. Worlds/characters/config/Workshop/Gold state захищені.
- Known dependencies/overlapping tasks: Gold Core/Combat100/Magic35_70/Utility35_70 owner records мають окремі file reservations. Canonical/shared files тут не резервуються для змін. GoldCraftingService/GoldDivineTransactions — змішані, Gold Core owns shared protocol; Utility100 owns action effect/cost semantics.
- Integrity before: 200 files (198 src-modern C# + project + Build123); sorted absolute-path|SHA256 lines joined LF, UTF-8 aggregate SHA256 17A8C98317AA07B70EFB79AB0427F45D47313DC756CA01A4DDFB59EC5DDD8BF5.
- Status: COMPLETE (bounded bootstrap/baseline). Owner mode залишається BASELINE / READ-ONLY.
## Source-derived inventory (2026-10-04)
Status: IMPLEMENTED IN SOURCE для двох простежених mechanics; compile inclusion перевірено за project/Build123, binary/runtime gameplay тут не перевірено. Інші Utility100 slots UNKNOWN / не інвентаризовані цим task. Catalog/localization не використовуються як implementation proof.

### Pickaxes100: weight execution path
- src-modern/PerkGameplayPatchesA1.cs:46-55: Harmony Inventory.GetTotalWeight postfix → local player → subtract profession + feast discounts → clamp >=0.
- src-modern/PerkRuntimeServices.cs:194-220: тільки той самий player.GetInventory(); Pickaxes100 перевіряється через HasPerk (51-54), raw m_level >=100 (32-48), без SE skill bonus. MetalMaterials (164-168) — explicit case-insensitive allowlist; ItemPrefabName (92-97) прибирає (Clone)/trim, null prefab → empty.
- Allowlist (18): CopperOre, TinOre, IronScrap, IronOre, SilverOre, BlackMetalScrap, FlametalOre, FlametalOreNew, Copper, Tin, Bronze, Iron, Silver, BlackMetal, Flametal, FlametalNew, CopperScrap, BronzeScrap. Включення у список не доводить availability prefab у грі.
- Pickaxes100 metal fraction=.75. Pickaxes35 nonteleportable nonquest fraction=.75; overlaps через Max, не addition. WoodCutting70 є adjacent-domain fraction: Wood .90, інше listed wood .50. Ніякої persistent зміни ItemData weight цей hook не робить.
- BlackForest Master Feast: PerkRuntimeServices.cs:178-192 додає .25 від залишкової ваги listed metal/wood. Для metal base W при100: W-.75W-.25*(1-.75)W=.1875W. Без feast=.25W. Theme read з MasterFeastThemePatches.cs:8; це інтеграція Cooking70, не новий Utility100 slot.
- Ownership/locality: чужий inventory/chest та headless без local player не отримують цю знижку від hook. Global enabled setting не перевіряється в цьому конкретному hook/HasPerk; це source observation, не reconfirmed gameplay bug або дозвіл на fix.
- Compile gate: ці класи не під локальним #if; preprocessor blocks у сусідніх PerkRuntimeServices combat methods / A1 Blocking не виключають weight classes. Build123 MASTERY_RELEASE_SAFE не вимикає цей path.

### Crafting100: action execution path and ownership
- Lifecycle: MasteryPlugin.cs:25 Initialize; :31 PatchAll; :46 Tick. NetworkSync.cs:34 → GoldCraftingService.Register → GoldDivineTransactions.Register (:62-65), VM_Gold_Action / VM_Gold_Grant. GoldCraftingService.cs:107 pumps deferred action Tick.
- Gates: GoldCraftingService.cs:14-16 costs/readiness; :47 enabled config + global setting; :67-72 equipped/hidden Hammer; :85-90 ToggleArm; :143-169 server Crafting100 eligibility/unlock/arm. GoldDivineTransactions.cs:54-55 Eligible = equipable, maxQuality>1, maxStackSize=1. Не будь-який рецепт/предмет.
- Natural99→100 recognition pointer: GoldCraftingService.cs:156-160 sync → HasGold → Ledger.Unlock; :251-267 Ascend writes VM_Gold_Crafting_Ascended and shows ceremony. Це shared Gold recognition, не доказ виконаного live ACK acceptance. Action ACK описано окремо нижче.
- Entry: GoldDivineStartPatch InventoryGui.OnCraftPressed (:399-403), priority First+100 → Start (:71-90), native timer/effects; GoldDivineCraftPatch DoCrafting (:406-410), First+100 → Intercept (:120-149). GoldDivineButtonPatch UpdateRecipe (:412-416) → UpdateButton (:378-395).
- Intercept skips vanilla тільки для qualifying divine path. Ordinary resource upgrade на non-upgrader station проходить vanilla (:127); enabled/armed/eligibility gaps повертають true. Divine path відмовляє при Outstanding / WorkshopRemoteCraft.ClientBusy / WorkshopRecovery.Outstanding; не викликає vanilla DoCrafting, ConsumeResources чи Workshop debit.
- Kind1 masterwork: NewQuality=5; cost500 Favor (GoldAction.Cost :15 + GoldCraftingService:14). LocalValid (:168-174) перевіряє known recipe, station level1/availability/capacity; не звичайні resource requirements. Apply (:306-315) клонує template; встановлює q5, variant, stack1, worldLevel, crafter ID/name, повну durability, VM_Masterwork=true, VM_MasterworkPatron=Volundr, VM_Gold_Grant=token, новий VM_Gold_ItemIdentity; AddItem тільки один.
- Kind2 divine forge: NewQuality=OldQuality+3, cost250 Favor. Потрібен m_upgrader station, той самий identified inventory target/old quality/variant і selected required idol prefab/quality/stack (LocalValid :176-180). Apply (:317-326) забирає рівно1 idol, змінює ТОЙ САМИЙ ItemData q+3, refill durability, grant token; решта metadata/crafter/variant зберігається, зокрема masterwork flags якщо існували. Нема RNG success/break у цьому Apply. Не приписувати Crafting70 failure policy divine branch.
- GoldAction.Decode (:23-38) перевіряє contract version1, kind1 q5 / kind2 +3, identity, quality upper bound10000; звичайний m_maxQuality cap не є target ceiling цього divine Apply. Native stat scaling поза поточним audit.
- LocalValid (:156-180) повторюється перед Apply; Target (:150-154) шукає identity+prefab; ServerValid (:182-196) перевіряє actor/hammer/recipe/station/ward/idol resource definition. Ownership security/receipt protocol design лишається Gold Core.
- Compile gate: GoldCraftingService/GoldDivineTransactions/GoldMasterworkTooltip/GoldCharacterSave не мають локальних #if; project include включає їх в обидва Build123 variants. Runtime config/readiness/authority gates окремі від compile inclusion.

### Metadata and double modifiers
- Пошук VM_Masterwork / VM_MasterworkPatron у поточному src-modern: producer GoldDivineTransactions.cs:311; consumer GoldMasterworkTooltip.cs:13-23. Tooltip потребує exact true+Volundr і не додає label вдруге. Окремий stat multiplier за masterwork marker не знайдений; q5/+3 і native quality getters не дорівнюють такому множнику.
- Divine Apply deferred у Tick (:270-291,361-376), а не synchronous всередині DoCrafting. Суміжні void prefixes/postfix/finalizers можуть бути викликані попри cancellation original; не заявляти повне виключення всіх Harmony patches без runtime evidence.
- Crafting35TransactionPatch (PerkGameplayPatches35.cs:352-402) resource preservation застосовується через Player.ConsumeResources; divine direct idol RemoveItem його не викликає. Crafting35 completion вимагає ConsumptionSkipped + successful q(old+1) snapshot; divine q(old+3) не є цим outcome.
- SkillCraftBonusPatch (CookingCraftingProgression.cs:74-119) змінює/відновлює native bonus context/amount; divine direct AddItem stack1 не використовує vanilla bonus output.
- CraftingXpPatch (PeacefulXpPatches.cs:72-90) вимагає CraftingOutcomeSnapshot q1 або q(old+1) increase; direct deferred divine q5/q+3 не має explicit AwardCraft тут. Natural XP/Favor grant або подвійний grant не LIVE VERIFIED.
- PotentialForgeSafetyPatch.cs:24-85 тимчасово змінює upgrade resource break/success fields та відновлює postfix/finalizer; divine Apply не читає ці probabilities. WorkshopHostCraftActionPatch priority Last та WorkshopRemoteCraftReentryPatch First — shared hooks для майбутньої bounded integration regression, без права змінювати їх тут.

### Transaction, failure/refund and persistence boundaries
| Boundary | Source evidence | Action implication / owner |
|---|---|---|
| REQUESTED durable character receipt before send | GoldDivineTransactions.cs:145-148,106-113 | No item effect before request saved; identity assignment earlier still observable. Gold Core receipt protocol. |
| Server admission/reservation | :244-257; GoldCraftingLedger.cs:262-273 | Pending saved; Favor NOT debited at Reserve. Station proof/authority required. Gold Core. |
| Deferred client grant | :262-291; GoldReceiptPolicy.cs:7-14 | Exact saved contract required; Applied/Rejected phases resend, not reapply. Bounded queue8; no global exactly-once claim beyond traced policy. |
| Effect and APPLIED save | :306-330; GoldCharacterSave.cs:11-25 | Inventory + player journal serialized together; send APPLIED only after Persist true. Utility100 effect; Gold Core save contract. |
| Commit | :232-238; GoldCraftingLedger.cs:279-295 | After APPLIED ACK, debit500/250, set20min online game-time exhaustion, settle token, persist; ClearArm/NotifyFavor. Gold Core. |
| Transport failure after save | :198-211,361-376 | Keep durable item+receipt, retry every2s; no rollback on transport-only error. |
| Application/save failure | :336-359 | Remove created object OR restore upgrade old quality/durability/prior grant token and idol stack/membership; persist REJECTED then send. Failure to persist rollback → quarantine/unresolved reservation. |
| Server rejection | :237-238; GoldCraftingLedger.cs:299-307 | Clear pending and persist. No numerical Favor refund because Reserve did not debit. Item/idol rollback is separate action responsibility. |
| Settled journal cleanup | :279-283 | Remove journal and save; if save fails restore it in memory. |
- GoldCharacterSave observes synchronous FileWriter.Finish (:26-37), requires actual writer status2, SawWriter and no Failed. Це source-level save contract, не current cloud/offline durability PASS.
- Receipts keyed by world UID (GoldDivineTransactions.cs:51), item identity metadata travels with item; session resets/deferred retry/reconnect/crash windows потребують authorized acceptance. Ledger receipt window bounded64 (GoldCraftingLedger.cs:15,292-293); stale replay не оголошувати globally solved.

## Pickaxes100 regression design — DEFERRED execution
Усі cases нижче — SOURCE-DERIVED expected oracles, не виконані tests. Для live використати disposable test character/world, matching client/server SHA, settings/log, без production state.
| Case | Setup / observation | Expected source oracle |
|---|---|---|
| Threshold | raw99.999 /100, SE skill bonus | 100 activates metal branch; bonus alone insufficient. Below100 normal Pickaxes35 can mask result: use listed teleportable metal fixture or instrument fraction only in separately authorized QC. |
| Types | each18 allowlist names; unlisted prefab, missing dropPrefab, armor/weapon/tool | Listed materials receive metal .75; unlisted alone no metal100 discount. Confirm actual prefab availability separately. |
| Quest/teleport flags | listed metal with teleportable/quest permutations; nonmetal nonteleportable nonquest | Metal100 does not check quest/teleportable; Pickaxes35 does. Nonmetal discount belongs35. |
| Stacks | same material stack1/large, split/recombine, mixed inventory | Discount proportional to item.GetWeight(m_stack), no per-stack rounding specified; sum individual weights. |
| Combined perks | Pickaxes35+100; WoodCutting70; metal+wood+other items | Metal remains .75 via Max; wood receives its own .90/.50 or larger35 fraction; no .75+.75. |
| Feast | BlackForest active/inactive and theme changes | Listed metal at100=.25W, with BlackForest=.1875W. Listed wood feast applies to residual weight too. |
| Inventory identity | local player/chest/other player's inventory/null local/headless | Only exact local player inventory receives these discounts; server native weights need separate observation. |
| Idempotence | repeated GetTotalWeight, UI refresh, move/drop/pickup | Query result derived again; no persistent item weight write by this hook. Final clamp nonnegative. |
| Lifecycle/settings | raw level drop/death/reconnect; global settings off/on | Re-read raw skill/inventory; this hook has no explicit global enabled gate. Record observed contract before proposing a fix. |

## Crafting100 future acceptance — DEFERRED execution
- Natural99→100 recognition and action arming/ACK; eligible vs ineligible items, station/hammer loss during timer/grant; Favor499/500 and249/250, exhaustion, inventory full, ordinary upgrade fallback.
- Masterwork q5/stack1/metadata/crafter/worldLevel; forge same reference identity/q+3/full durability/one idol; preserve custom keys/variant/existing masterwork flags; no bonus item/resource preservation/double XP/Favor.
- Repeated click/replayed grant/Applied retry/late commit; workshop busy; target moved or quality/variant/idol changed before grant; duplicate identity observation on disposable fixtures only if authorized.
- REQUESTED→reserve→effect+APPLIED save→server commit→journal cleanup; controlled disconnect/reconnect/server restart. Failure injection/crash/corruption forbidden without explicit scope.
- Acceptance tags: INVENTORY_CAS GOLD_LEDGER RECEIPT_IDEMPOTENCY SAVE_ORDER; HEADLESS RECONNECT SERVER_RESTART when authorized. Action oracle Utility100; ledger/Favor/receipt oracle Gold Core; source shared integration root.

## Bounded reviewer
One read-only utility100_risk_review assigned exact transaction/save/metadata files, no writes/tests/deploy. Final findings incorporated below; exact evidence checked by root. Root owns evidence resolution and final records. No shared-system changes or Gold protocol proposals authorized here.

## Bounded risk review — incorporated by root
Reviewer: utility100_risk_review, read-only source review; жодних writes/tests/state/runtime. Root зіставив findings із прочитаними methods; це risk register, не live bugs/exploits або зміна протоколу.
| ID | Fact / risk | Evidence | Owner / next review |
|---|---|---|---|
| U100-R1 | Target first matching identity+prefab; duplicate GUID uniqueness не перевіряється. Wrong-target outcome — hypothesis. | GoldDivineTransactions.cs:150-154 | Utility100 identity/metadata regression; Gold Core якщо змінюється contract. |
| U100-R2 | Identity створюється до final validation та UpdateButton; rejection не повертає попередню відсутність identity. | :137-144,388-395,342-345 | Utility100 acceptance decision; жодного fix тут. |
| U100-R3 | q5/+3 може перевищити native maxQuality; wire bound10000 не gameplay acceptance. | :31-36,54-55,176-180,309,325 | Utility100 native stats/durability/idol/tooltip matrix. |
| U100-R4 | Masterwork marker stat multiplier не знайдений; deferred divine action і adjacent Harmony void prefixes/finalizers не доводять відсутність side effects. | :270-291,306-328; GoldMasterworkTooltip.cs:15-23; adjacent hooks above | Utility100 + Utility35/70 + XP, bounded runtime regression later. |
| U100-R5 | Character effect/receipt save та server ledger — різні persistence domains; synchronous writer guard не crash proof. | GoldCharacterSave.cs:11-31; GoldDivineTransactions.cs:328-358; GoldCraftingLedger.cs:262-307 | Gold Core save/crash protocol; Utility100 effect invariant review. |
| U100-R6 | Pending не автоматично expires; settled window64. Abandoned receipt / stale backups можуть вимагати recovery policy, не доведений exploit. | GoldCraftingLedger.cs:15,270-278,292-293; GoldDivineTransactions.cs:363-376 | Gold Core recovery/replay; no cleanup або replay experiment тут. |
| U100-R7 | Parsing exception receipt fail-closes (return true,phase=-1), але parsed numeric phase поза1..3 повертає false; Outstanding тоді не блокує нову дію. Corrupt/stale-data effect не перевірено. | GoldDivineTransactions.cs:53,92-102 | Gold Core receipt validation review. Не оголошувати malformed receipts universally fail-closed. |
No protocol changes proposed/integrated. Future protocol changes require Gold Core affected-owner review and root final integration. Reviewer не повідомляв інші chats; відповідні owner boundaries зафіксовані локально.

## Final acceptance and durable next action
- Completed bounded bootstrap/baseline only; IMPLEMENTED IN SOURCE paths Pickaxes100/Crafting100 + compile inclusion traced. No new STATIC VERIFIED gameplay / LIVE VERIFIED status.
- Gameplay regression DEFERRED; other Utility100 slots UNKNOWN until scoped source trace; disabled/future idols не оголошуються implemented.
- Changed files: тільки три task/progress/handoff UTILITY100_BASELINE_20261004_OWNER records. Shared-system mutation NONE; canonical docs/source/build/config/production state не редагувалися.
- Before/after200-file manifest matched: 17A8C98317AA07B70EFB79AB0427F45D47313DC756CA01A4DDFB59EC5DDD8BF5. Це source integrity, не test mechanics.
- Final three-record readback required by completion script; reservation release only on PASS.
- Next action: отримати конкретний human task, створити/use unique utility100 task + matching progress/handoff; перевірити overlap та read-only authorization. Для transaction/persistence/metadata ризику bounded reviewer; для Gold protocol — Gold Core review до root integration. Gameplay/build/deploy tests запускати лише в явно дозволеному scope.