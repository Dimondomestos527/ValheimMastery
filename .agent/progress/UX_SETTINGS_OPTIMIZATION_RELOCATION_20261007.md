# UX_SETTINGS_OPTIMIZATION_RELOCATION_20261007 progress
REVIEW READY / 100 of 100 / LIVE UNTESTED.

- Scope/reservations 10: complete. Exact isolated candidate and read-only icon audit boundaries recorded.
- Native SettingsGui evidence 25: complete. Actual Valheim 1.0.16 `Initialize`/`OnOkAsync`/`OnBack`/navigation IL inspected; `ShowBuildPieceAuthor` verified vanilla and its conflict with installed hover-suppression patch recorded without mutation.
- Candidate implementation 30: complete. One native Accessibility row, staged OK/Back semantics, process-local copy, change-only log, headless/duplicate guards; Mastery creation call removed and viewport restored.
- Dual build/static lifecycle checks 25: complete. Client 0 errors/13 existing warnings; server 0 errors/11 existing warnings; 0 owned warnings; provenance PASS; 18/18 focused checks; 1328 unrelated compiled types preserved per variant. Initialize refreshes current ConfigEntry, while tab switching preserves the staged selection.
- Icon triage/handoff 10: complete. ICON-01 and ICON-03 statically confirmed; ICON-05 structural risk confirmed/live unreproduced; separate owner scopes/tests recorded; no icon fix bundled.

Candidate hashes: MasteryWindow `908CAFEA...`, Accessibility integration `B3E536B4...`, client DLL `3C846B46...`, server DLL `0AD7D0FA...`. No canonical source, installed DLL, config, save, asset or runtime mutation.

## Combined Pilot B deployment review

FINAL STATIC UX PASS / RELEASED FOR AUTHORIZED ROOT INSTALL / LIVE UNTESTED.

- Combined frozen snapshot: `validation/performance-pilot-b-deploy-20261007/snapshot`.
- Exact UX sources are byte-identical to the accepted candidate: `MasteryWindow.cs` `908CAFEAE697C689BBAA89C80EA1091E206A71E715DAD6F4AA5C88BDCADD70B2`; `ExperimentalOptimizationAccessibilityPatch.cs` `B3E536B46EF706421E62A0509F9641B7DDF10BB64C8EBE71B30980B64B5BC7E8`.
- Combined DLLs: client `DE5D6A7362C5AB4A46955B4C7552E0A6D1090F2AE1C6922234E679648BF18F3F`; server `7FBB4AFF998F68F6F95ECE41437316B314BC7E9B768B88C54D50DFAC795C524C`.
- Dual build PASS: client 0 errors/13 prior warnings; server 0 errors/11 prior warnings. Dependency provenance contains 32 fingerprints/2 exact plugin outputs. Ten focused QC groups and 526 Harmony contracts per variant pass.
- Combined preservation PASS: 1310 unrelated compiled types unchanged and 25 accepted types per variant. Exact source delta against installed Pilot A is the declared five files: three Utility100 Pilot B files plus the two accepted UX files.
- Independent IL check PASS in both variants: five Accessibility integration types present; Initialize calls `Ensure(..., true)`; tab open calls `Ensure(..., false)` then navigation; OK calls `Apply`; Back calls `Cancel`; Apply contains the single ConfigEntry setter and bounded change-only log.
- `MasteryPlugin.cs` `E188BD3215AD02C7C834F1CF0BF1DDE8243EDCD2F4FC333328607A5C6FA4BE5E`, `MasterIdolPilotA.cs` `039531CB724F1472E5BEE48B2987A82BFC2944714260576B930F6E8A6EDE4E2B`, legacy unreachable toggle `CBEBA6F0D7D5A8916EB5D584405488D218FD237C8D21E44471593C3645211AD1`, BuildAuthor and icon service remain unchanged.
- Release is limited to these exact combined DLL hashes. No asset, icon, BuildAuthor, localization, save/config-state, or live-runtime mutation is approved by this UX review. Installation remains root-owned; visible layout/controller navigation/OK-Back behavior and actual performance remain live tests.

Root installation completed and was independently rehashed: client `DE5D6A7362C5AB4A46955B4C7552E0A6D1090F2AE1C6922234E679648BF18F3F`; server `7FBB4AFF998F68F6F95ECE41437316B314BC7E9B768B88C54D50DFAC795C524C`. Rollback DLLs verified; ten protected config/journal samples unchanged; one DLL per plugin root; no pending residue; assets excluded; current flag remains `false`; client/server were stopped. Live UI, controller, gameplay and FPS remain UNTESTED.
