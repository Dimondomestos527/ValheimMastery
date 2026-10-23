# Valheim Mastery: план контрольованого performance-тесту
Дата: 2026-10-23. Read-only тестовий протокол, НЕ вимога негайного запуску.
Ціль: перевірити, яка механіка реально споживає CPU/GPU/GC на конкретній тестовій збірці. Без змін production data.

## Preflight
1. Зафіксувати installed Client DLL SHA-256, Server DLL SHA-256, build source gate/commit, compile flags, wire identity, список інших модів і налаштування.
2. Тестувати на КОПІЇ світу/персонажа або ізольованому disposable середовищі; ніколи не скидувати чи очищати журнали Gold/Idols/Workshop у живому світі.
3. Зіставити Client і Server протокол. Визначити роль: local host, joined client чи dedicated server.
4. Однакові роздільна здатність, shadows, lighting, VSync/FPS cap, погода, біом, відстань камери, кількість гравців, число сутностей.
5. Спершу A/B вимір у тому самому місці й складі світу; не переносити висновок із іншої DLL чи сезону гри.
6. Три повтори або ABBA, розміри вибірок та час прогріву вказати явно. Зміни по одному фактору за раз.

## Сценарії
| ID | База A | Експеримент B | Що вимірювати |
| --- | --- | --- | --- |
| S1 | та сама сцена без active turret | 1/5/10 діючих турелей (на disposable world) | main-thread per-turret scan, LOS count, frame median/p95/p99, network changes |
| S2 | idle без Chain | 1/3 active Ashlands70 Chain на тій самій кількості NPC (ЛИШЕ якщо клас компільовано) | loop CPU, frame p95/GC, кількість victim |
| S3 | звичайна копія рудника | 20 однакових ударів Pickaxes70 | allocated bytes/hit, duration, GC collections, resource/XP parity |
| S4 | поза боєм | Bows weakpoint під час натягування на 10/40 NPC | scan count, raycasts, CPU під час draw |
| S5 | сталі верстати без перебудови | додати/прибрати 1 верстак на великій базі | rebuild CPU, graph opportunities, allocations |
| S6 | 0 residents | 4 і 8 residents із тим самим activity (після окремого стабільного Forest V2) | path calls, update CPU, scene+work+inventory, network |
| S7 | Mastery VFX disabled | enabled, однакова сцена зі світінням та activation effects | GPU time, render thread, overdraw, CPU frame |
| S8 | без summons | 4/8 active summons | follow/portal scan CPU, pathfinding, wire traffic |
| S9 | тотожне місце, нормальні shadows | лише shadows знижено | GPU/render і FPS, не приписувати Mastery без контрольного порівняння |

## Existing diagnostics
- Для Workshop на відповідному процесі: vm_workshopperf start 60 alloc → vm_workshopperf status → vm_workshopperf stop. У виході: maxMs, totalMs, allocBytes, maxStations, graphLoopOpportunities.
- Для idols: тимчасово дозволити UIDebugLogging ЛИШЕ на disposable профілі, виконати vm_idol_perf, потім повернути налаштування. Порівнювати етапи; вимірювач сам додає overhead і не дає GPU/FPS.
- Якщо вимірювань для PERK01/PERF02/PERF03 нема, Codex може запропонувати одне bounded on-demand measurement per owning domain, не загальну систему телеметрії.
- Усі логи приватно: не комітити raw Player/LogOutput, saves, configs, IDs, secrets у public GitHub.

## Мінімальний звіт одного сценарію
| Поле | Значення |
| --- | --- |
| Test ID / дата / місце / role | |
| Client & Server SHA; build symbols | |
| Entity/NPC/turret/VFX counts | |
| Sample duration, warm-up, repeats | |
| Median / p95 / p99 frame ms | |
| Main thread / rendering / GPU ms (де доступно) | |
| GC allocated bytes/s / spikes | |
| CPU of target stage (ms, calls/s) | |
| RPC calls/bytes per second, якщо network case | |
| Effect parity, regressions, assumptions | |
| Точний висновок: supported / inconclusive | |

## Stop conditions
- Розбіжність Client/Server version або непридатний disposable world.
- Неузгоджені або небезпечні save data/owner proofs.
- Помилка, що робить A/B поведінку нерівною.
- Не запускати другий паралельний gameplay refactor під час Forest Guard V2.

## Acceptance на оптимізацію
Перед patch: показати hotspot і baseline. Після patch: повторити ідентичний тест; жодна зміна damage/XP/cargo/favor/receipt/inventory/visual intention; при відсутності вимірюваного покращення зміна лишається PROPOSED, не merge.
