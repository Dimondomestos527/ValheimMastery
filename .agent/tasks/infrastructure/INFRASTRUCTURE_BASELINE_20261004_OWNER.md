# INFRASTRUCTURE_BASELINE_20261004_OWNER
- Owner/profile: INFRASTRUCTURE; .agent/profiles/INFRASTRUCTURE.md. Постійний technical owner chat Valheim Mastery Infrastructure / Build / Deploy; root інтегрує інфраструктуру.
- Goal: виконати bootstrap, зафіксувати актуальний infrastructure baseline, межі повноважень та наступну дію.
- Authorization: прямий запит користувача 2026-10-04 на встановлення owner chat і task/progress/handoff lifecycle. Це документаційний bootstrap; нової build/deploy/cleanup/release задачі не задано.
- Acceptance criteria: canonical bootstrap/profile/docs прочитані; поточні project/build gates звірені; installed client/server/Harmony та обидві rollback копії перевірені SHA256; task/progress/handoff перечитані; source/tooling/build/runtime fingerprints до/після збігаються.
- Scope/allowed files; exact write reservations:
  - .agent/tasks/infrastructure/INFRASTRUCTURE_BASELINE_20261004_OWNER.md
  - .agent/progress/INFRASTRUCTURE_BASELINE_20261004_OWNER.md
  - .agent/handoffs/INFRASTRUCTURE_BASELINE_20261004_OWNER.md
- Non-goals: gameplay/source/tooling fixes; збірка/QC execution; client/server launch; deployment/rollback execution; production config/world/character/Workshop/Gold changes; cleanup/archive moves; release packaging/publication; Git init.
- Complexity: SIMPLE; root-only bootstrap. Для substantive build/runtime investigation використовувати bounded read-only scouts/reviewers за user request, з точними goal/files/permissions/non-goals/output. Root володіє final integration.
- Relevant canonical docs: AGENTS.md; CHAT_BOOTSTRAP.md; CONTEXT_AND_STATUS.md; SCOPE_MAP.md (technical stewardship); SHARED_SYSTEMS.md (project/QC/lifecycle/network boundaries); BUILD_RUNTIME_BASELINE.md; docs/KNOWN_ISSUES.md.
- Relevant source/build gates: ValheimMasteryPoC.csproj; NuGet.config; tools/Build123.ps1; BuildReferenceAudit.targets; BuildDependencyProvenance.ps1; Install123.ps1 лише для майбутньої explicitly authorized deployment task.
- Shared systems touched: NONE. Перед future shared mutation записати SHARED SYSTEM CHANGE (file/system, reason, affected domains, regression), отримати affected-owner review; shared/core integration робить root.
- Required regression: bootstrap readback + SHA integrity; gameplay DEFERRED за поточним human scope. Future technical dimensions: client/server hashes/reference provenance, clean build/static QC/actual Unity Harmony, no generated-output leakage, rollback/protected-state evidence; HEADLESS OWNER_AUTHORITY RPC_AUTHORITY ASYNC_ASSETS, RECONNECT SERVER_RESTART коли task зачіпає state/runtime.
- Deployment requirement: NONE. Деплой лише за explicit active-task authorization. Production worlds/state захищені; будь-який виняток має бути прямо у задачі.
- Known dependencies/overlapping tasks: ARCHITECTURE_20261004 COMPLETE; перевірені reservations інших owner tasks стосуються їхніх власних трьох records. Нові exact paths перевірені absent; overlap немає. Canonical docs/profile не редагуються.
- Status: COMPLETE bootstrap; режим очікування explicit scoped infrastructure task. Final acceptance: 3/3 readback PASS; 230/230 protected SHA256 unchanged, membership differences 0. Reservations released.

## Ownership precedence
Прямий user request розширює responsibility профілю: build tooling, reference/publicizer isolation, dependency provenance, QC infrastructure, deployment/rollback tooling, release packaging, cleanup tooling, archive/release organization, future repository/Git infrastructure. Profile виключає archive/cleanup та automatic install/release; user scope має пріоритет щодо stewardship. Жодна capability не створює standing authorization на виконання. Permanent purge та release publication завжди окремі explicit tasks. Gameplay semantics належать gameplay owners; stale assertions не стають gameplay contracts, gameplay не змінюється заради tooling PASS.

## Verified current fingerprints (2026-10-04; read-only)
- Canonical root: MasteryDev/ValheimMastery; project version 1.4.123; 198 src-modern C# files.
- Client installed/build baseline SHA256: 839D03955995ECBC5E3B80698D0E8BB9CE86A6C5072183F38868D74B15EFD409.
- Server installed/build baseline SHA256: 95DEB8ADD0629FC7E37D1C42F9CFBFDED53688113274CB265164A4259B47C241.
- Both installed runtime Harmony SHA256: 1A21CC03424FC82C3DD1346905D16494536B9595AE4162228D99FB7C285C1031; historical runtime metadata 2.9.0.0. Compile HarmonyX package 2.10.2 is distinct.
- Client rollback: validation/controlled-client-smoke-20261004/rollback-20261004T172339Z/ValheimMastery.dll; current SHA256 08DB58D47592B0F6A9729570D81D962DF6411E3419419E0D3B5B68A6D88BA93C.
- Server rollback: validation/regression-preparation-20261004/rollback-20261004T175528Z/ValheimMastery.dll; current SHA256 75903BE4B1B073273C33145605E24AB7A34C7D0D8AC2D6500E4ABDBAF3AD106B.
- Get-Process valheim/valheim_server: 0 at bootstrap snapshot. Canonical .git absent; Git not initialized.

## Baseline evidence and limitations
- validation/tooling-blockers-20261004/TOOLING_BLOCKERS_RESOLUTION_REPORT_UA.txt: clean separate client/server builds; corrected CandidateAssemblyFiles contamination, exact raw/generated/compiler provenance; 2963-method IL comparison per variant, 0 differences. This is historical evidence, no fresh build in this task.
- tools/Build123.ps1 currently has 11 feature constants, separate GameManaged/output/isolated intermediates, generated access-checks path, CandidateAssemblyFiles disabled, default None disabled, RAR cache disabled, reference audit and provenance verifier. Reference artifacts are provenance only.
- validation/unity-smoke-20261004/UNITY_RUNTIME_SMOKE_REPORT_UA.txt: server registration PASS; entire original disposable-runtime task remained BLOCKED due client isolation. Do not promote that report to full PASS.
- validation/controlled-client-smoke-20261004/CONTROLLED_CLIENT_UNITY_SMOKE_REPORT_UA.txt: subsequent actual client registration PASS; both variants 233 originals/230 prefixes/212 postfixes/4 transpilers/46 finalizers, 0 duplicate tuples, 0 semantic tuple differences. Historical matching-binary registration evidence, not new runtime execution.
- validation/regression-preparation-20261004/POST_MIGRATION_REGRESSION_ENVIRONMENT_UA.txt: server deployment/rollback evidence, protected-state comparison; preparation PARTIAL, gameplay scene not verified.
- validation/clean-qc-20261004/CLEAN_STATIC_HARMONY_QC_REPORT_UA.txt remains historical partial/FAIL evidence. Later build-provenance and actual-registration evidence closes those specific gaps, not all QC modes.
- Open: stale QC expectations, owned=true fixture coverage, localization structural key, deprecated Unity/TMP calls, live fx_land/async scene rendering; gameplay/network/persistence regression remains DEFERRED.
