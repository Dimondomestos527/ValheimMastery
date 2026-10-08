# SELECTIVE_PATCH_DEPLOY_20261005
- Owner REGRESSION_INTEGRATION/root; infrastructure deployment policy.
- User2026-10-05: "Встановлюй патчі по всьому крім 100 рівнів та пантеону". Install accepted pending non100/nonPantheon patches into modded client and dedicated server; preserve excluded domains at installed baseline.
- Allowed pending implementations: MAGIC35_70_FROST_CHARGE_SHIELD_20261005 (14exact owned files), COMBAT35_70_ECHO_TOOLTIP_20261005 (3exact files). XP_FARMING_SAP_REVIEW_20261005 read-only/no source patch. Existing UI/Utility35/70 retained.
- Excluded: any new GOLD_CORE/Pantheon/COMBAT100/MAGIC100/UTILITY100 changes, including GoldPantheonUi/PerkDebugService diagnostic mock and pending Gold ledger/cooldown/masterwork changes. Never build raw current global checkout while excluded chats active.
- Snapshot: start immutable previously installed gameplay snapshot validation/integrated35-70-update-no-ux-20261005/snapshot (old no-UX source), overlay all accepted Magic owned files (contains latest installed UI + approved magic-only updates) and Combat3. All other source remains baseline; no canonical source edits.
- Acceptance: exact allowlisted approved hashes/source overlay; excluded and unrelated compiled types preserved versus installed UI baseline; dual isolated build/provenance and relevant current QC PASS; stale assertions classified/reconciled task-locally; dual backups/install hash verify; running server gracefully stopped and restarted with same invocation, startup checked.
- Complexity NORMAL; root-only. No gameplay implementation/agents/other-chat messages.
- Exact write reservations:
  - .agent/tasks/regression/SELECTIVE_PATCH_DEPLOY_20261005.md
  - .agent/progress/SELECTIVE_PATCH_DEPLOY_20261005.md
  - .agent/handoffs/SELECTIVE_PATCH_DEPLOY_20261005.md
  - validation/selective-patch-deploy-20261005/** (immutable selective snapshot, caches/builds/QC/native lifecycle scripts/manifests/rollback/log evidence/report)
  - C:/ValheimModDev/BepInEx/plugins/ValheimMastery/ValheimMastery.dll and ValheimMastery.SELECTIVE_PATCH_DEPLOY_20261005.pending
  - C:/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server/BepInEx/plugins/ValheimMastery/ValheimMastery.dll and ValheimMastery.SELECTIVE_PATCH_DEPLOY_20261005.pending
  - docs/architecture/BUILD_RUNTIME_BASELINE.md; docs/validation/1.4.123_POST_MIGRATION_BASELINE.md: dated integration/deployment supplements
- Allowed runtime operation: existing dedicated server process4808 normal CTRL-C shutdown for world save, replace DLL while absent, restart exact captured executable/arguments/cwd. Arguments may contain secrets: keep only in process memory; never echo/store raw invocation in evidence. Normal server logs/config/saves may update through its own lifecycle, no manual state edits. Client absent, no client launch needed.
- Rollback: exact currently installed variants; atomic replacement, restore if install fails; restart old installed server if update cannot complete after shutdown. Never force-kill or kill unrelated processes.
- Concurrency: Gold/Utility100 ACTIVE; source/framework/owner outputs READ-ONLY. Build only snapshot; allowed Magic/Combat handoffs released.
- Shared-system mutation NONE by coordinator; approved Magic-specific content in mixed localization/tooltip files retained; no excluded-domain overlay.
- Regression: source/hash allowlist; strict compiled unrelated type preservation incl allGold/100; Magic focus/native/flight/shield and Combat focus/tooltip; existing retained Workshop/movement/cargo/roster/contracts; registration startup on dedicated Unity. Natural visuals/gameplay still LIVE_TEST_REQUIRED.
- Status ACTIVE. Scope/readiness complete; snapshot/build/QC/install pending.
## PRESENTATION / VANILLA ASSET PLAN
N/A installation-only. Native identities/tuning chosen by owners unchanged. No new assets or presentation selection.
Runtime plan update BEFORE deployment: original server process ended externally during preparation; confirmed zero Valheim processes. No coordinator shutdown/restart required; preserve current stopped state. Installation acceptance excludes new startup/Unity registration, which remains UNTESTED. Deployment lifecycle phase units now backups / process-absence check / install / final integrity and process-state verification.
## Human scope amendment before installation —2026-10-05
- User explicitly permits level100 patches as well. Prior Pantheon exclusion remains.
- Include released UTILITY100_MASTERWORK_BUTTON_20261005 five action/UI files plus required accepted GOLD_CORE_COST_TIER_COOLDOWN_20261005 policy+ledger pair. No Pantheon interface, mock/status bars or multi-patron wallet update.
- Additional accepted overlay: GoldDivineTransactions.cs, GoldMasterworkTooltip.cs, GoldMasterworkButton.cs, GoldMasterworkRules.cs, GoldMasterworkPresentation.cs, GoldCooldownPolicy.cs, GoldCraftingLedger.cs.
- Keep unrelated/Pantheon compiled types identical to installed baseline; compare changed100 compiled types to final owner synchronized candidate. Combat100/Magic100 have no new released implementation patches; existing mechanics retained.
- Supersedes earlier blanket exclusion of all Gold/100 files and old selective hashes. Earlier non100 build was preparation only, not installed.
- Same exact write reservations and stopped-runtime policy; no canonical source/QC mutations. Revised QC includes current Gold100 assertions and accepted model checks. Weighted phases unchanged; installation not started.
FINAL STATUS COMPLETE installation / STATIC VERIFIED. All exact reservations RELEASED. Weighted phases20+45+25+10=100%; relevant LIVE tests UNTESTED and migration baseline INCOMPLETE. Latest human amendment and stopped-runtime amendment supersede initial exclusions/lifecycle. Full evidence/report/handoff readback verified.
