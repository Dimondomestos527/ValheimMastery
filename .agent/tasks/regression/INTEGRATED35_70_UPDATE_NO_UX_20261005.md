# INTEGRATED35_70_UPDATE_NO_UX_20261005
- Owner: REGRESSION_INTEGRATION/root, infrastructure build/deploy policy applied.
- Authorization: 2026-10-05 user "Встановлюй оновлення(UX поки не чіпай)" — update configured modded client and server, exclude active UX chat changes.
- Goal/acceptance: new Magic/Utility/Combat owner handoffs integrated in an immutable isolated source snapshot; four UX description/tutorial files pinned to previously installed source hashes; fresh dual build/provenance and domain checks PASS; correct variants installed with rollback/hash verification.
- Complexity NORMAL, root only. No gameplay implementation/agents/chat messages.
- Read-only canonical source/tools and owner tasks/handoffs. New accepted owners: MAGIC35_70_JARIK_STORM_20261005, UTILITY35_70_RUN_PREVIEW_REGRESSIONS_20261005, COMBAT35_70_CLUBS_RUSH_REFINEMENT_20261005; all idle/reservations released.
- UX_DESCRIPTIONS_RAVEN_REVIEW_20261005 ACTIVE and four source reservations HELD. Do not write its source/task/progress/handoff or shared bin/obj/caches. Snapshot source takes pinned old UX copies from prior Utility isolated source; hashes must equal previous installed deployment source-before.csv.
- Exact write reservations:
  - .agent/tasks/regression/INTEGRATED35_70_UPDATE_NO_UX_20261005.md
  - .agent/progress/INTEGRATED35_70_UPDATE_NO_UX_20261005.md
  - .agent/handoffs/INTEGRATED35_70_UPDATE_NO_UX_20261005.md
  - validation/integrated35-70-update-no-ux-20261005/**: isolated snapshot source/project/copied canonical tools/cache, generated builds, task-local scripts/manifests/QC/logs/rollback/report
  - C:/ValheimModDev/BepInEx/plugins/ValheimMastery/ValheimMastery.dll and ValheimMastery.INTEGRATED35_70_UPDATE_NO_UX_20261005.pending
  - C:/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server/BepInEx/plugins/ValheimMastery/ValheimMastery.dll and ValheimMastery.INTEGRATED35_70_UPDATE_NO_UX_20261005.pending
  - docs/architecture/BUILD_RUNTIME_BASELINE.md; docs/validation/1.4.123_POST_MIGRATION_BASELINE.md: dated update provenance supplements only
- Snapshot pin UX files: PerkLocalization.cs, PerkLocalization.Ukrainian.cs, PerkNarrativeService.cs, MasteryRavenTutorials.cs. Existing approved mechanic-local combat HUD refinement remains in the gameplay owner's accepted scope; no new Core UX work included.
- Shared system mutations NONE by coordinator. Source/project/version/gates unchanged outside documentary snapshot. Build only isolated copy, not global owner outputs.
- Required regression: snapshot vs canonical non-UX source stable; exact old UX hashes; fresh dual Build123/provenance; new Magic130/Combat104/Utility23 checks, contracts, carrier/roster, patch/combat-input, preparation/boundaries/movement/atomic; post-install hash/metadata/rollback/no duplicates.
- Rollback: archive current installed exact variants outside plugin loader, stage and atomic replace using explicit backup paths; restore originals if installation/verification fails.
- Protected state: no production game/server launch/world/character/config/Gold/Workshop save writes; no source implementation; installation only. Both processes absent at task start and rechecked before replacement.
- Baseline completion is not claimed; live visual/audio/physics/network cases remain UNTESTED.
- Status COMPLETE;100% installation acceptance. All reservations RELEASED. Live gameplay UNTESTED; see final report/progress/handoff.
## PRESENTATION / VANILLA ASSET PLAN
N/A coordinator installation. Preserve owner-selected vanilla presentation; no new assets or presentation choices.

- Static test reconciliation: isolated Patch123Checks Run assertion updated to accepted NativeMovementPulse + natural VfxRecipeService route, excluding old autoring. STALE DOC/TEST evidence archived; canonical QC and gameplay source unchanged.
UX exclusion expanded to five files after concurrent UX owner added MasteryItemTooltipPatch.cs. Snapshot already contained its exact old installed source39EB4079...; verified against prior manifest, no snapshot DLL rebuild necessary. All29 QC groups passed; only drift guard needed updated exclusion. Canonical UX source remains untouched.
