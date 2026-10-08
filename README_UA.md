# Valheim Mastery — Dev

Гілка `Dev` містить поточні вихідні файли моду, документацію, профілі/історію задач, інструменти і вихідний код QC. Тут можуть бути незавершені зміни. Не вважайте весь Dev перевіреною ігровою збіркою.

`distribution/client.json` і `server.json` вказують тільки на готові перевірені пакети. Поточний пакет **1.4.123-20261008** відповідає встановленим DLL: Client `F10AD55CC838217D1782C23EE18A8F80F254D3B3ECC1DD8A3194AAB3707D5751`, Server `BA25EFA8736BC329F50CA369537F4E03A7D28CB46EAC70CB06CFEC829D048C1F`. Відповідні вихідні файли: `baselines/installed-20261008`. Static/installation PASS; ігрові тести, збереження/перепідключення/перезапуск і macOS — UNTESTED.

[Завантажити оновлювач](distribution/ValheimMastery-Updater.zip) і [інструкція](updater/README_UA.md). Оновлюються тільки готові DLL/ассети; поточні іконки вбудовані в DLL. Скрипт запускається вручну, без фонового стеження; потрібен Python 3.9+.

Для збірки потрібні .NET SDK, NuGet, BepInEx 5 і власні поточні Valheim managed assemblies. Вони не розповсюджуються в цьому репозиторії. Проєкт: `ValheimMasteryPoC.csproj`, compile gates у `tools/Build123.ps1`. Сценарії інструментів історично використовують локальні Windows-папки — адаптуйте їх для своєї інсталяції. Assets root проєкту перенесений на `assets/milestones` тільки в цьому експорті. У baseline використовується та сама спільна папка ассетів.

Наступний готовий пакет: `python tools/PackageUpdate.py --client <reviewed-client.dll> --server <reviewed-server.dll> --version <unique-version> --source <reviewed-source-path-or-commit> --validation <evidence-summary>`. За потреби окремих зовнішніх ассетів додайте `--assets <directory>`. Після перевірок опублікуйте JSON і ZIP обох варіантів одним комітом у Dev. Скрипт пакування сам не перевіряє ігрову механіку й не публікує файли.

Не завантажені: сейви/персонажі, конфіги, Gold/Idol журнали стану, приватні runtime логи, Valheim/Unity DLL чи витягнуті нативні текстури, NuGet/cache/bin/obj і локальні validation snapshots. Посилання в історичних документах можуть вести до цих виключених локальних доказів; вони не є доступними GitHub-артефактами. Окремі DLL в `distribution/*.zip` — явний виняток для готового моду. Код моду й документація експортовані без зміни механік.
