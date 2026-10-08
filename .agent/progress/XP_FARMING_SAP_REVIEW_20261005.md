# XP_FARMING_SAP_REVIEW_20261005 progress
Status: COMPLETE (read-only review); gameplay LIVE_TEST_REQUIRED. Date:2026-10-05.
Weighted phases defined before inspection (total100):
1. Authorization/domain/prior evidence 15%;1/1: instructions and existing Utility baseline read; user authorized checking only.
2. Current source/native execution trace 45%;1/1: scoped source/native trace and build gates below.
3. Installed/evidence correlation 25%;1/1: both installed DLLs inspected with Mono.Cecil, hashes match latest canonical installed baseline; live evidence gap explicitly recorded.
4. Findings/test matrix/final readback 15%;1/1: final records written and reopened; conclusions limited to static scope.
Progress:100% of read-only review, no live gameplay PASS. Exact reservations: matching task/progress/handoff only, released on final readback. No source/shared system changes.

## Висновок
IMPLEMENTED IN SOURCE AND PRESENT IN INSTALLED CLIENT/SERVER: бонус соку існує і native шлях до спостерігача виходу підтверджений. Статичний аналіз не виявив безумовного блокування стандартного extract. LIVE VERIFIED не встановлено; у двох перевірених актуальних LogOutput.log немає [SapBonus]. Відсутність рядка не доводить, що механіка зламана або тестувалась.
Навігаційна карта відносить sap до UTILITY_35_70, але перевірений бонус не має milestone gate: це level-dependent passive. Цей огляд не змінює канонічні межі й не присвоює суміжний файл XP-власнику для implementation.

## Джерельний/native trace
- src-modern/SapHarvestBonus.cs:27-46 Awake реєструє VM_SapHarvestSkill та VM_SapBonusFx; :48-53 Interact prefix надсилає raw Farming рівень фактичного локального гравця, не builder/bystander/collector owner. Enabled=true, не repeat, collector valid, stored level>0.
- :30-38 collector network owner приймає лише скінченний допустимий рівень та sender, для якого ResolveSender повертає ненульовий player ID. Claim keyed by sender, expiry10s.
- :55-63 RPC_Extract prefix consume once claim того самого caller; missing/expired claim => vanilla extraction без бонусу і без [SapBonus]. Ownership/RPC arrival order and sender resolution remain live dimensions.
- :95-103 scoped ThreadStatic Active збережено/відновлено postfix і finalizer.
- :18-24 / :105-107 ItemDrop.SetStack postfix спостерігає відповідний предмет у межах1m; unique ItemDrop increment Actual один раз, а не +=stack.
- Native client and server SapCollector.RPC_Extract verified via Mono.Cecil: Instantiate<ItemDrop> IL_006e; Game.ScaleDrops(...,1) IL_0091; ItemDrop.SetStack IL_0096; ResetLevel IL_00a4. Each stored collector unit creates one drop. Spawn offset insideUnitSphere*0.2 fits1m guard in ordinary native path. GetLevel reads ZDO s_level; ResetLevel writes0. Extract routes RPC_Extract through ZNetView.InvokeRPC(string,object[]).
- Native parameter bindings both variants: Interact(character,repeat,alt); RPC_Extract(caller); SetStack(stack). Installed Harmony attributes target correct Awake/Interact/RPC_Extract/SetStack methods. Actual Unity Harmony registration was not executed by this review.
- :65-82 after emptying collector, each min(Actual,Units) gets RollChance(Clamp01(rawLevel*.005)); bonus drops physically instantiate at spawnPoint and SetStack(amount), then FX broadcast. Does not add directly to collector/player inventory. Ground pickup is not exclusive to harvester; harvester level determines roll only.
- PerkRuntimeServices.cs:33-49 GetActualSkillLevel reads raw native m_skillData levels without SE bonuses; :57-60 RollChance uses Random.value unless ForceProc debug override. No Farming35/70/cultivator gate in sap path.
- Project includes src-modern/**/*.cs, no sap exclusions; SapHarvestBonus.cs has no #if gate. Build123 source inspected only, not run.

## Математика і підтверджені обмеження
p=Clamp01(L*.005) per observed base collector unit, bonus B is binomial(n,p) in ordinary independent RNG case, n=min(Actual,Units). No promise of a bonus on each collection.
L20=>10%;L35=>17.5%;L70=>35%;L100=>50%. For n10,L100 expected bonus5, not guaranteed5; probability zero=(1-.5)^10=1/1024≈0.09765625%.
Confirmed behavior: world resource scaling increases each native SetStack stack, but observer counts ItemDrop objects and bonus spawn skips Game.ScaleDrops. Therefore bonus is based on base stored units; it is not automatically50% of world-scaled native output at Farming100. Whether to change this is an explicit balance decision, not an automatically confirmed design bug.
The [SapBonus] log records planned bonus before delivery; bonus>0 alone is insufficient to prove actual delivered count. Final spawned item amount/FX count or inventory/ground count must also be observed in live acceptance.

## Installed evidence
Both installed assembly versions1.4.123.0 inspected read-only; SapHarvestBonus and all four patches, RPC registration, Send/Before/ObserveOutput/After method bodies present with expected current logic.
Client C:/ValheimModDev/BepInEx/plugins/ValheimMastery/ValheimMastery.dll SHA25623A8CF38D29D8165B3976B16B46A7B2AB81B11DE79EA80212E4C01015A177F41.
Server parent runtime BepInEx/plugins/ValheimMastery/ValheimMastery.dll SHA256DDFBFB93E30C1BD07E2A231E442C937C499B689A9E20D2C0443AF18C02EC8ABD.
Match docs/architecture/BUILD_RUNTIME_BASELINE.md latest UI supplement; installed presence is not runtime gameplay proof.
Both domestos.valheim.mastery.cfg files Enabled=true on disk; actual in-process settings not inspected.
Server LogOutput.log last write2026-10-05T13:35:34.6639790+03:00; client13:35:46.7602727+03:00. Both load Valheim Mastery1.4.123; [SapBonus] count0. No historical/other-log search promoted to current acceptance.
Source SapHarvestBonus.cs SHA256B22810256929036877B2B0413A6BD63689BD0B6F6AF60034FEDCC65F01B11CA9; PerkRuntimeServices.cs SHA256572DBD860C6FD1815A47EAE67B7ACD264747D4739C3640E181B84E0AE811E523. Source untouched.

## Матриця наступної live-перевірки (НЕ виконано)
| Сценарій | Обов'язковий доказ |
|---|---|
| Farming0/1/34/35/69/70/99/100, без debug ForceProc, resource multiplier1 | stored units, actual units, raw farming, planned bonus, реально spawned extra; плавний шанс без35/70 порога |
| Harvester100 vs builder/bystander0, потім навпаки | рівень і sender збирача визначають p; чужий рівень не підставляється |
| Owner=client/self, owner=інший client, dedicated owner | accepted claim before native extract, matching sender/caller, [SapBonus], exact outputs |
| 0stored/повторна взаємодія/double-click/concurrent users/owner transfer | нуль повторних бонусів, claim one-shot, native results once |
| Немає/expired claim, reconnect, server restart | base extract без stale повторної виплати; новий claim працює |
| resource multiplier1/2/3 | world-scaled native stacks окремо від unscaled bonus; зафіксувати очікуваний balance contract |
| Full inventory, auto-pickup off/on, neighbour near drops | bonus існує на землі незалежно від місткості; final count, не лише FX/log |
Найкоротший діагностичний live-case: звичайний збір з ненульового collector на Farming100 при multiplier1; перевірити [SapBonus] у network owner log і рахувати додаткові spawned предмети. Один нульовий випадковий результат не доводить несправність. Missing [SapBonus] => спочатку claim/registration; actual0 => output observation; bonus>0 без предметів => delivery.

## Межі й наступна дія
No fresh build/QC execution, client/server launch, deployment, state/config edits or source fixes. Shell sandbox ACL helper fails before startup; reviewed escalated commands used. Немає unresolved approval rejection.
Точна наступна дія: окремо авторизований bounded live-case вище з matching installed hashes; implementation лише якщо факт збою або погоджена зміна balance contract встановлені.
