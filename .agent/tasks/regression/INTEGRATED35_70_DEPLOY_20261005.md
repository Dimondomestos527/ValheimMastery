# INTEGRATED35_70_DEPLOY_20261005
- Owner: REGRESSION_INTEGRATION/root; INFRASTRUCTURE policy applied to build/deploy.
- Human authorization: 2026-10-05 "Встанови зміни з чатів магії утіліті та бойових перків на гру та сервер". Explicit dual installation overrides prior docs-only/default no-deploy scope for this task.
- Goal: build current integrated shared checkout, verify changed-domain static acceptance/provenance, install distinct client/server DLLs with rollback and hash verification.
- Acceptance: idle owners/released reservations; all changed source present in fresh build; dual builds/provenance and relevant QC pass; unchanged source fingerprint before/after build; backups match old targets; installed hashes match fresh correct variants; no production state/config modification; durable deployment report.
- Complexity: NORMAL, root only. No gameplay implementation or agents.
- Source READ-ONLY: src-modern/**, project, Build123/validation tooling. No version bump, release packaging or source changes.
- Named owner handoffs: MAGIC35_70_TORBA_20261005, UTILITY35_70_EARLY_PREPARATION_IMPL_20261005, COMBAT35_70_CLUBS_PRESENTATION_20261005. Chats verified idle; source reservations released. Magic task retains live acceptance gap only.
- Exact write reservations:
  - .agent/tasks/regression/INTEGRATED35_70_DEPLOY_20261005.md
  - .agent/progress/INTEGRATED35_70_DEPLOY_20261005.md
  - .agent/handoffs/INTEGRATED35_70_DEPLOY_20261005.md
  - validation/integrated35-70-deploy-20261005/** (new unique build/QC logs, source/deployment manifests, rollback originals, install script and report)
  - bin/Perks123Client/**; bin/Perks123Server/**; obj/isolated123/Perks123Client/**; obj/isolated123/Perks123Server/**
  - Existing project-local .nuget/**, .dotnet-home/** and validation/temp/** generated caches as required by canonical build
  - C:/ValheimModDev/BepInEx/plugins/ValheimMastery/ValheimMastery.dll and ValheimMastery.INTEGRATED35_70_DEPLOY_20261005.pending
  - C:/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server/BepInEx/plugins/ValheimMastery/ValheimMastery.dll and ValheimMastery.INTEGRATED35_70_DEPLOY_20261005.pending
  - docs/architecture/BUILD_RUNTIME_BASELINE.md (dated installed provenance supplement)
  - docs/validation/1.4.123_POST_MIGRATION_BASELINE.md (dated deployment evidence supplement; keep incomplete)
- Targets: modded client C:/ValheimModDev (current BepInEx/current123 log and installed plugin verified); dedicated server current workspace parent. Steam vanilla Valheim has no installed Mastery and is not the configured modded client.
- Runtime processes: both absent before task; recheck immediately before install. No production launch/restart required for an installation request.
- Shared systems: no new mutation; integrate existing owner changes only. Utility protocol4/legacy3 and transaction acceptance reviewed in owner handoff.
- Required regression: fresh dual Build123/reference provenance; contracts, carrier119/roster, patch123/combat-input/18 focused IL checks, Torba42 checks, preparation27/boundaries60 and atomic61. Static results are not live gameplay PASS.
- Rollback: archive exact old client/server DLLs under task evidence before replacement; staged per-variant files verified; restore both originals if replacement/verification fails.
- Protected state: no worlds/characters/config/Gold/Workshop saves written; no native runtime launch. Backups outside loader plugin directories, no duplicate DLL loading.
- Non-goals: unimplemented run/chest-flow visuals/hill collision; normal gameplay edits; close Torba visual diagnosis from source alone; declare migration baseline complete.
- Status: COMPLETE. Acceptance100%; dual installation verified. All write reservations RELEASED. Gameplay acceptance remains UNTESTED; see final report/progress/handoff.
## PRESENTATION / VANILLA ASSET PLAN
N/A deployment-only; carry owner's vanilla-only selected presentation unchanged. Live Torba geometry, combat sound/visuals, resource-preparation latency and physical collisions remain owner/user acceptance.
