# REGRESSION_INTEGRATION_BASELINE_20261004
- Owner/profile: REGRESSION_INTEGRATION; .agent/profiles/REGRESSION_INTEGRATION.md.
- Goal: bootstrap постійного координатора; створити незавершений evidence baseline 1.4.123, правила класифікації та bounded cross-domain regression plan.
- Authorization: запит користувача 2026-10-04 на regression coordination/task lifecycle і майбутній baseline. Поточний root scope — документація; gameplay regression DEFERRED.
- Acceptance criteria: canonical bootstrap прочитано; exact current DLL hashes звірено; наявні reports розділено за SHA/host/date/scope; owner dependencies і known issues інтегровано без gameplay status promotion; matrix та evidence intake визначено; bounded read-only reviewer reconciled; 4/4 Markdown readback, local links та protected hashes перевірено.
- Scope/allowed files; exact write reservations:
  - .agent/tasks/regression/REGRESSION_INTEGRATION_BASELINE_20261004.md
  - .agent/progress/REGRESSION_INTEGRATION_BASELINE_20261004.md
  - .agent/handoffs/REGRESSION_INTEGRATION_BASELINE_20261004.md
  - docs/validation/1.4.123_POST_MIGRATION_BASELINE.md
- Non-goals: gameplay implementation/fixes; broad source audit; build/QC execution; launch/deploy/client character creation; production/config/world/Workshop/Gold edits; packaging; Git initialization; messaging інших chats.
- Complexity: NORMAL; один bounded read-only evidence reviewer, жодної implementation delegation.
- Relevant canonical docs: AGENTS.md; CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; SCOPE_MAP; SHARED_SYSTEMS; BUILD_RUNTIME_BASELINE; KNOWN_ISSUES; REGRESSION_ENVIRONMENT. Evidence reports linked у baseline.
- Relevant source/classes/build gates: src-modern/MasteryPlugin.cs version only; ValheimMasteryPoC.csproj inclusion/version; tools/Build123.ps1 symbols/variant/reference isolation. Source inventory 198, не semantic validation.
- Shared systems touched: NONE; documentation only. Shared mutation потребує попереднього SHARED SYSTEM CHANGE, affected-owner review і root integration.
- Required regression: тут документаційна acceptance та SHA integrity only. Майбутні matrices: local/client/server; two-player authority/owner transfer; disabled settings/headless; reconnect/restart/save order; receipts/recursion; async/rendering; disposable crash recovery з окремим scope.
- Deployment requirement: NONE; launch/deploy не авторизовано.
- Status: COMPLETE (bounded documentation bootstrap; four reservations released after acceptance). Migration baseline INCOMPLETE; relevant gameplay results UNTESTED / execution DEFERRED.
- Known dependencies/overlapping tasks: architecture COMPLETE/released; owner tasks reserve only власні task/progress/handoff. Combat100/Magic100/Utility100 inventories ACTIVE на snapshot. Ці 4 exact paths були відсутні перед reservation; чужі records та canonical known-issues не редагуються.


