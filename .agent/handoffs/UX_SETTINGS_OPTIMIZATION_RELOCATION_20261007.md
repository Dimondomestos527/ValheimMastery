# UX handoff — Settings optimization relocation
REVIEW READY / STATIC VERIFIED / LIVE UNTESTED / NO INSTALL.

- Installed source authority: `validation/performance-pilot-a-deploy-20261007/snapshot`.
- Goal achieved in isolated candidate: one native Accessibility control, no Mastery creation call, unchanged default-OFF process-local ConfigEntry/runtime contract.
- Exact source delta: `MasteryWindow.cs` `908CAFEAE697C689BBAA89C80EA1091E206A71E715DAD6F4AA5C88BDCADD70B2`; new `ExperimentalOptimizationAccessibilityPatch.cs` `B3E536B46EF706421E62A0509F9641B7DDF10BB64C8EBE71B30980B64B5BC7E8`.
- Candidate DLLs: client `3C846B46FE569B73B4B7FF6BD8A747270A660F641B8D8B0ECF39916C1DABC11F`; server `0AD7D0FA37AB20EF4696A0FB3BBB253A1F58D587426D4D7F65F2CF5CA239D3BE`.
- Evidence: `validation/ux-settings-optimization-relocation-20261007/REPORT_UA.md`, `NATIVE_SETTINGS_EVIDENCE_UA.md`, `TEST_UA.md`, `focused-checks.csv`, `preservation.csv`, `source-delta.csv`.
- Root review gates: native row clone compatibility, SettingsTooltip raw copy, OK/Back ordering, controller chain, exact installed-source rebase. Reopen/Initialize refreshes the actual current ConfigEntry; OnTabOpen wires navigation only and cannot discard a staged choice. Installation and live client test require separate authorization.
- Icon triage is read-only and excluded from candidate: ICON-01/03 confirmed, ICON-05 structural risk confirmed/live unreproduced. Builder option is vanilla; installed hover-suppression patch conflicts with it but remains unchanged pending a separate decision.

## Final combined Pilot B acceptance

FINAL STATIC UX PASS / EXACT DLL RELEASE / LIVE UNTESTED.

- Reviewed frozen package: `validation/performance-pilot-b-deploy-20261007/snapshot`.
- Exact accepted UX hashes retained: `MasteryWindow.cs` `908CAFEAE697C689BBAA89C80EA1091E206A71E715DAD6F4AA5C88BDCADD70B2`; `ExperimentalOptimizationAccessibilityPatch.cs` `B3E536B46EF706421E62A0509F9641B7DDF10BB64C8EBE71B30980B64B5BC7E8`.
- Released combined DLLs for the already authorized root install only: client `DE5D6A7362C5AB4A46955B4C7552E0A6D1090F2AE1C6922234E679648BF18F3F`; server `7FBB4AFF998F68F6F95ECE41437316B314BC7E9B768B88C54D50DFAC795C524C`.
- Evidence independently rechecked: client/server build 0 errors (13/11 prior warnings), 32 dependency fingerprints and 2 exact plugin outputs, 10/10 QC groups, 526/526 Harmony contracts per variant, preservation 1310 unrelated + 25 accepted types per variant.
- Compiled lifecycle PASS: native Accessibility Initialize refreshes current setting; tab open preserves staged value and rewires navigation; OK applies through the single ConfigEntry setter with one change-only log; Back cancels by restoring current value. Both variants contain the same five integration types.
- No scope creep: exact source delta remains three declared Utility100 Pilot B files plus these two UX files. `MasteryPlugin`, Pilot A reset/runtime contract, old unreachable toggle, BuildAuthor and icon service hashes remain unchanged. Assets, icons, localization, save data and existing config state are excluded.
- Root may now perform stopped-runtime backup/install/hash verification using only the two released DLL hashes. Do not report live UI, controller, performance or headless success until those are actually exercised.

POST-INSTALL: root installed and verified the released hashes exactly (client `DE5D6A73...`, server `7FBB4AFF...`). Rollback hashes, ten protected config/journal samples, single-DLL/no-pending checks pass; assets were not changed and `ExperimentalOptimization = false` remains. The wrapper emitted a false failure by testing `$LASTEXITCODE` after a pure PowerShell script; direct manifest/hash/integrity checks prove installation succeeded. Runtime behavior remains UNTESTED.
