# UX_SETTINGS_OPTIMIZATION_RELOCATION_20261007
- Owner/profile: CORE_UX_TUTORIALS. Root integrates shared config/runtime and any canonical source.
- Human scope: move the existing `ExperimentalOptimization` control from Mastery into native `Valheim.SettingsGui` Accessibility; preserve the exact process-local default-OFF config/runtime contract; add bounded change-only logging; audit icon report read-only. No deploy/launch/config/save/asset/Gold mutation.
- Installed source authority: `validation/performance-pilot-a-deploy-20261007/snapshot`; client DLL `02A103FD...`, server DLL `39130910...`.
- Exact candidate reservations: this task/progress/handoff; `validation/ux-settings-optimization-relocation-20261007/**`. Canonical `src-modern/**` and installed DLL/config remain read-only. Candidate may change only copied `MasteryWindow.cs`, copied/reworked `ExperimentalOptimizationToggle.cs`, and a new narrowly named SettingsGui integration file; shared `MasteryPlugin.cs` contract must remain byte-identical.
- Non-goals: icon fixes, BuildAuthorHoverCleanup behavior change, native `ShowBuildPieceAuthor` option/creator ZDO change, gameplay/runtime optimization change, custom assets/Canvas, remote-server control, FPS claim.
- Required dimensions: native Accessibility lifecycle, repeated open, Save/Cancel/close semantics, mouse/keyboard/controller focus, UI scale/long UA copy, late SettingsGui creation, main thread/headless, config persistence/current value, single log per actual change, no Mastery duplicate.
- Status: ACTIVE.

## SHARED SYSTEM CHANGE
- Native settings integration: add/remove the Mastery-owned row within Valheim SettingsGui Accessibility without modifying native assets or native setting ownership.
- `MasteryWindowController.Build`: remove only the former optimization row and restore its viewport allocation.
- `ExperimentalOptimizationToggle`: preserve the same `MasteryPlugin.Settings.ExperimentalOptimization` entry but follow native settings Apply/Cancel lifecycle and bounded actual-change logging.
- Affected domains: CORE_UX_TUTORIALS, Utility100 Pilot A runtime, root config integration. Regression: default OFF, process-local client/server independence, runtime cache reset through the unchanged ConfigEntry, headless, repeated menu open, focus/controller, Save/Cancel, no duplicated control.

## Read-only icon triage boundary
- Verify ICON-01 mapping, ICON-03 blocking cooldown aliases, ICON-05 cold-cache risk against the installed snapshot only.
- Record proposed owner scope/tests; do not include any icon source edit in this relocation candidate.
