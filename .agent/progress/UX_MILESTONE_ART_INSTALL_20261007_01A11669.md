# UX_MILESTONE_ART_INSTALL_20261007_01A11669

COMPLETE for latest human scope PATCH PREPARATION ONLY.100/100 measured:15baseline+20approval/exports+20implementation/rootreview+25dual/QC+20verified package/handoff. Runtime installation removed from this chat's scope by direct human instruction; main chat installs later. No claim installed or LIVE VERIFIED.

Patch: C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\MasteryDev\ValheimMasteryVisualAssets\01a11669-39c8-7f70-aec7-170ba12660ed\exports\vm_milestone_patch\v001\ValheimMastery_MilestoneArt_PATCH_v001.zip
Handoff: C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\MasteryDev\ValheimMasteryVisualAssets\01a11669-39c8-7f70-aec7-170ba12660ed\handoffs\vm_milestone_install_v001\MAIN_CHAT_HANDOFF_UA.md
Canonical changes:MasteryMilestonePresentation.Show/layout;NEWMasteryMilestoneArtwork;59PNGembedded/ImageConversionreference in csproj. Canonical and isolated source changed; actual installed DLLs unchanged. Latest exact write reservations released after final readback; snapshot/package immutable. Main chat must use current baseline and its own deployment task/reservations.

# Патч повідомлень майстерності — для мейн-чату

Підготовлено рівно погоджений набір: 24 навички × рубежі 35/70/100, 56 різних іконок, 3 затверджені рамки. Верхній центр екрана, відступ10 reference pixels; панель640×360 при опорній роздільності1920×1080. Тло рамок непрозоре. Окремі іконки й рамки вбудовані у DLL; назви навичок та перків беруться з локалізації мода. Старий вигляд — fallback, якщо PNG не завантажиться.

## Дозвіл і стан

Користувач у цьому visual-asset чаті погодив показаний native-verified v001 набір словами «Клас, тепер це треба замінити на місце теперішніх». Остання вказівка: «Просто підготуй патч, мейн потім встановить». Отже, мейн може інтегрувати й встановити саме цей погоджений патч; це не глобальний дозвіл на інші custom assets чи генерацію в інших чатах.

Установлені DLL НЕ замінено. Клієнт/сервер не запускались і не зупинялись агентом. Конфігурації, світи, персонажі й Gold state не редагувались. Нові source-файли та точкова зміна проєкту вже є у канонічному робочому коді; це не означає встановлення в гру. Не накладати source patch повторно без перевірки.

## Що містить ZIP

- source/ValheimMastery_milestone_art_v001.patch — точковий diff двох C# файлів і ресурсної секції проєкту.
- source/src-modern/ — повні два змінені файли для огляду.
- runtime-assets/ — 59 runtime PNG, таблиця72 прив’язок і хеші джерел.
- candidate/Client та candidate/Server — перевірені окремі DLL на точній встановленій базі resident-patch-no-assets.
- evidence/ — прийняття користувача, перевірки, базові SHA та склад пакета.

## Перед встановленням

1. Звірити актуальні встановлені Client/Server SHA з PATCH_MANIFEST.json. Якщо база відрізняється — НЕ ставити готові DLL поверх новішого мода. Перенести тільки точкові зміни з source patch на актуальний встановлений source snapshot, зберігши всі інші патчі; заново зібрати й перевірити обидва варіанти.
2. Перевірити ownership/reservations і джерела. Справжні ресурси залишаються в окремій visual-assets папці: exports/vm_milestone_runtime/v001. Канонічний csproj уже посилається на неї. Для іншого snapshot вказати MSBuild-властивість MasteryMilestoneAssetRoot на runtime-assets із цього ZIP або на зазначену локальну папку. LogicalName має бути ValheimMastery.Milestones.%(Filename)%(Extension).
3. Зберегти чинні DLL й точну source mapping для відкату; закрити клієнт/сервер звичайним способом. Лише після цього встановити за штатним процесом мейн-чату, звірити SHA та перевірити відсутність дубльованих DLL/staging.
4. Не запускати гру автоматично. Після запуску користувачем перевірити природні й повторні34→35,69→70,99→100, верхнє розташування, довгі назви й роздільності, вимкнення повідомлень та відсутність впливу на VFX/SFX.

## Перевірки

Обидві збірки:0помилок;34dependency fingerprints/4generated origins;5QC груп кожна,531Harmony contracts без помилок;72compiled mapping checks і59побайтових resource checks кожна.1352сторонні compiled types та MasteryMilestoneAnimator незмінні. Зміни не зачіпають механіки, proc/buff/cooldown UI, tutorial/reward, мережу чи схему збережень. Максимум2ownedtextures/sprites на повідомлення; звільненняOnDestroy, disabled/headless guard і fallback.

Це STATIC VERIFIED / PATCH READY. Реальний вигляд у Unity і природні crossing-тести — LIVE UNTESTED. PNG-прев’ю та перевірка IL не замінюють цю перевірку.

Зайві старі ассети в цьому етапі не видалено: актуальні файли й залежності треба зберегти до успішної інтеграції та підтвердження відкату.
