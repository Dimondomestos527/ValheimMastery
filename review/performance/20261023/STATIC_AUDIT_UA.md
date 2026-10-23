# Valheim Mastery: незалежний статичний аудит продуктивності та складності
Дата: 2026-10-23. Автор: ChatGPT, зовнішній read-only reviewer.
Основа: GitHub Dev 6deb6cae3ee464ff241ecdabd676effb13a18521 (2026-10-18), src-modern (509 .cs у каталозі). Installed-source provenance новіший і окремий. **Це вибірковий аналіз ключових runtime-шляхів усіх основних доменів, а НЕ построкова перевірка всіх 509 файлів і НЕ профілювання поточної DLL.**
Forest Guard V2 розробляє Codex. Ніяких змін або висновків про його ще не опублікований source тут немає. Всі гіпотези нижче потребують звірки з compiled/installed code.

## 1. Критерії та статуси
- SOURCE CONFIRMED: наведений патерн існує у зазначеному Dev-файлі; це не підтвердження впливу на FPS.
- HYPOTHESIS: напрям для замірів, без заяви про поточну вартість у мілісекундах.
- DEFER: джерело вже має throttling/cache/ownership, або ризик регресії вищий за очікуваний виграш.
- Оцінка P1/P2/P3 означає пріоритет профілювання, а не терміновий дозвіл переписати код.
- Виняток: save/receipt/inventory/ownership guards не можна прибирати заради швидкості без доказу збереження гарантій.

## 2. Пріоритетні знахідки

| ID | Пріоритет | Джерело (Dev) і перевірений факт | Потенційний ефект | Дія, яку треба довести |
| --- | --- | --- | --- | --- |
| PERF01 | P1 | src-modern/CrossbowTurretRuntime.cs:91-153; FixedUpdate із кроком 0.2 с, пошук цілі через Character.GetAllCharacters() кожні 0.5-1 с для кожної активної турелі; raycast/LOS у кандидатів | CPU масштабується як O(турелі × персонажі), плюс visibility | виміряти час пошуку та виклики LOS при 0/1/5/10 турелях, НЕ міняти прицілювання без доказу |
| PERF02 | P1 | src-modern/Ashlands70World.cs:56-95; Chain перевіряє Character.GetAllCharacters() до 10 разів/с для кожного активного Chain-state | O(активні Chain × персонажі), CPU | спочатку довести compiled inclusion і скільки Chain одночасно активні, далі точкове обмеження кандидатів |
| PERF03 | P1 | src-modern/Pickaxes70SuperHit.cs:58-95; відповідний MineRock5.RPC_Damage робить m_hitAreas.ToArray() і new float[...], далі перебирає hit areas | GC/CPU на частих ударах, особливо великих покладах | виміряти bytes/удар, p95 удару, кількість hit areas; не ламати XP episode reconciliation |
| PERF04 | P2 | src-modern/Bows70WeakPointPerk.cs:25-41,110-133; раз на 0.20 с поки гравець натягує лук сканує Character.GetAllCharacters() і може робити Linecast | CPU, переважно в бою | виміряти саме під час bow draw; перевірити distance/angle cheap-first й не змінити target priority |
| PERF05 | P2 | src-modern/WorkshopNetwork.cs:49-92; граф верстаків має вкладений обхід O(N²), але ТІЛЬКИ коли _dirty | одноразові піки при змінах мережі, не постійні FPS-просідання | vm_workshopperf на базах різного розміру, довести тригер/вартість перед spatial rewrite |
| PERF06 | P2 | src-modern/MasterIdolEffectZones.cs:39-73; сервер кожні 2 с збирає projected zones, HashSet/Dictionary і обчислює topology equality; кодує/хешує пакет тільки при зміні | server CPU/GC, можливі мережеві піки при великих мережах | vm_idol_perf projection.tick і peer-count, виміряти allocations; не порушувати authority freshness |
| PERF07 | P2 | src-modern/MasterIdolSocial.cs:40-75, MasterIdolResidentWork.cs, MasterIdolResidentCargoAgent.cs: різні per-NPC Update, ownership/context checks; cargo фізично сканує предмети з interval ~2 c та nonalloc physics | CPU накопичується з кількістю NPC; частина захищена інтервалами | метрики частоти умов/шляхів і allocations при 0/4/8 NPC, не створювати новий global NPC scheduler за замовчуванням |
| PERF08 | P2 | src-modern/EnchantmentAreaVisual.cs:47-82; активні композиції перебираються у кожному Plugin.Update, а BlackForest activation має до 3 burst × 60 emit, плюс budgets | короткі GPU/CPU піки під час effects | A/B у грі з VFX ON/OFF, GPU/CPU frame breakdown; gameplay не повинен залежати від VFX |
| PERF09 | P2 | src-modern/WeaponGlowService.cs:95-119; ItemDrop.Awake додає WorldSpearGlow компонент до кожного spear ItemDrop, Update раз/0.25 с, створює Light за потреби, shadows=None | багато копій скрипта на світі, динамічні lights і GPU | виміряти реальну кількість компонентів/активних Lights, не вимикати погоджений візуал без дозволу |
| PERF10 | P3 | src-modern/Skeleton35Travel.cs:166-200; глобальний Character scan раз/2 с, followers bounded; src-modern/BloodSkeleton35Service.cs: follow/search | CPU пропорційно кількості summons, але не кожний кадр | профіль тільки якщо summons реально домінують |
| PERF11 | P3 | src-modern/WorkshopPreparation.cs:22-65; клієнтський tick 0.1 с під час crafting/build; LINQ ToDictionary для підготовки персонального ресурсу | періодичні allocations під час crafting | виміряти лише під час відкритого crafting UI та активної preparation |
| PERF12 | P3 | src-modern/MasteryPlugin.cs:54-69 викликає чимало Tick кожний frame, але багато підсистем throttle-ять власну роботу; GoldCraftingService має 0.2/1/2/15 s gates | dispatch overhead або дубльовані контексти, не автоматичний FPS винуватець | profiler call tree до будь-якої зміни framework |

## 3. Що вже оптимізовано та НЕ варто переписувати без доказів
- MasterIdolWorldIndex.cs: повна ZDO-енумерація при початковій побудові індексу/інвалідації; подальші події підтримують індекс. Можливий startup spike, а не щокадровий world scan.
- MasterIdolWorkBudget.cs: до 4 дозволених path queries за кадр; MasterIdolResidentBudget.cs обмежує scans.
- MasterIdolLighting.cs: цикл через 0.5 с, до 8 перевірок за прохід, окрема квота writes. Тримати fairness та native authority.
- MasterIdolResidentVisibility.cs: per-object refresh ~0.5 с, sample ~0.12 с, bounded polling; перш ніж спрощувати, перевірити native visibility contract.
- VfxPool.cs: lease/cache + reset між повторними ефектами; NativeVfxSafeFrame.cs: bounded pending/per-frame preparation; повний reset може спричинити новий GC/спам.
- WorkshopNetwork.cs: rebuilding під _dirty, а не при кожному Crafting query.
- CrossbowTurretRuntime.cs: уже має CachedRevision та throttle; прибирати без нового profiled hotspot не можна.
- Skeleton35Travel.cs: interval 2 с та bound on Followers.
- MasterIdolPerf.cs: є read-only aggregates CPU+GC по етапах, але тільки коли ввімкнено UIDebugLogging; це НЕ FPS/GPU і може додавати overhead під час діагностики.
- WorkshopPerformanceProbe.cs: vm_workshopperf start 60 alloc / status / stop дає час і allocations побудови графа.
- ValheimMasteryPoC.csproj: Magic70IceStorm, Magic70Surtling та Magic70BloodDome виключаються без MASTERY_MAGIC70_EXPERIMENT; для будь-якого файлу підтверджувати compile symbols/installed DLL.

## 4. Межі та архітектурні ризики
- Forest Guard V2 — ВИКЛЮЧЕНО з цієї гілки. Codex займається ним; жодних запропонованих змін ForestGuard/Resident persistence/owner lifecycle тут немає.
- GPU-завантаження, особливо shadows і динамічні lights, не доводиться аналізом C#; попереднє покращення FPS після зниження shadows підтверджує доречність окремого GPU A/B.
- Закладені мережеві перевірки RPC, uid, world, owner, revision, receipts — це частина безпеки даних, а не «зайві if».
- Проєкт змінився після Dev 2026-10-18. Перевірити exact Client/Server hashes, source gate, compiled flags до призначення будь-якого коду на оптимізацію.
- Код виду GetAllCharacters — не сам по собі проблема: вартість залежить від викликів/с, entity count і внутрішньої складності фільтрів.
- Не оптимізувати довільно interval, radius, VFX density або кількість ally targets: це gameplay і presentation contracts.

## 5. Практичний порядок
1. Codex завершує Forest Guard V2 незалежно; цей audit PR — docs-only і НЕ потребує його аналізу для V2.
2. Infrastructure зчитує exact installed-source provenance, compile symbols і парність Client/Server.
3. Один спільний baseline протокол із TEST_PLAN_UA.md, без змін source.
4. Вибрати 1-2 найдорожчі за профайлером механіки, підтвердити hotspot хоча б у двох порівнянних прогонах.
5. ChatGPT готує алгоритмічне оптимізаційне рішення/ізольовану модель чи patch на окремій task branch. Codex тільки native інтеграція + вузькі перевірки.
6. Без покращення frame-time/CPU/alloc не залишати складніший код. Жодного масового рефакторингу.

## 6. Покриття доменів
Перевірено репрезентативні runtime-пути: XP/progression, Combat35/70, Combat100 пасиви/загальне, Magic, Utility35/70, Utility100/Gold, idols/residents, workshop, network, UX, VFX/SFX. Це первинне triage по основних механіках, не гарантована повнота всіх специфічних варіантів 24×35/70/100. Для повного coverage потрібні exact compiled type inventory та automated invocation map, що НЕ є дозволом самовільно змінювати код.

## 7. Статус
SOURCE CONFIRMED: source patterns і throttles вище. FPS root cause: UNKNOWN. Current installed-source performance: NOT MEASURED. This audit branch is review-only; DO NOT MERGE INTO Dev AS GAMEPLAY FIX.
