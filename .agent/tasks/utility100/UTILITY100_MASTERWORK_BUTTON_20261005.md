# UTILITY100_MASTERWORK_BUTTON_20261005
- Owner/profile: UTILITY_100; .agent/profiles/UTILITY_100.md.
- Human scope2026-10-05: replace masterwork activation with additional thematic crafting button UNDER ordinary craft; remove visible masterwork stamp; q5/no recipe resources/500Favor unchanged; success animation at actual completion; remove station LEVEL requirement only, retain required TYPE (both choices confirmed); Potential Forge excluded.
- Authorization: user-directed gameplay changes in this task; no deployment/gameplay launches/production state edits. Prior BASELINE remains default outside exact task.
- Status: COMPLETE / STATIC VERIFIED for authorized source/build/QC scope; root canonical integration and readback complete. Live gameplay/UI and deployment outside scope, not performed.
- Complexity: COMPLEX; root owns final integration; one bounded read-only transaction/metadata reviewer; native UI evidence inspected by root.
- Exact write reservations requested/held: src-modern/GoldDivineTransactions.cs (masterwork action and admission gate only; preserve forge), src-modern/GoldMasterworkTooltip.cs (disable/remove visible stamp consumer), src-modern/GoldMasterworkButton.cs (new domain UI), qc-harmony-smoke/Gold100Checks.cs (updated assertions), matching own task/progress/handoff, validation/utility100-masterwork-button-20261005/** isolated snapshot/build/QC evidence. No GoldPantheonUi/PerkDebugService edits; those are reserved by Gold Core active task. GoldCraftingService edit only if justified and reserved after review.
- Non-goals: new master idols/Phase100-B; Favor/cost/cooldown/ledger/receipt schema changes; forge activation/mechanics change; native UI recipe filtering for different station types; hotkey reassignment if masterwork has own button; deploy/client/server launch; corruption/crash injection; release/Git changes.
- Acceptance: dedicated ordinary craft button requires no hammer/arming; known eligible recipe/required station type maintained, level bypass applies masterwork only; selected recipe/variant/cancel timer stays coherent; q5 object has no VM_Masterwork/VM_MasterworkPatron stamp or visible legacy label; GUID/grant receipt metadata preserved; completion presentation only after durable success and once, cosmetic failure cannot trigger rollback; server/local validation agrees; forge regression and transaction review; both variant isolated builds and meaningful static/model QC; UI live appearance deferred without launch authorization.
- Known overlap: Gold Core GOLD_CORE_STATUS_MULTIBAR_20261005 active owns GoldPantheonUi.cs, PerkDebugService.cs; avoid those files. Utility35/70 Workshop UI tasks own adjacent hooks; root keeps no edits there.

SHARED SYSTEM CHANGE:
- file/system: GoldDivineTransactions.cs mixed action/admission and InventoryGui hooks; affected Gold100Checks.
- reason: distinguish button-triggered Kind1 masterwork admission from armed Hammer-bound Kind2 forge; bypass level while preserving station type; remove mark and move success presentation.
- affected domains: UTILITY_100 action, GOLD_CORE request admission/settlement, CORE_UX native crafting button integration, UTILITY_35_70 Workshop hook interactions.
- regression required: masterwork no hammer/no arm; forge existing hammer/arm+idol behavior; server malicious/wrong kind/station requests; known recipe/DLC/capacity; timer cancellation/selection changed; no vanilla/Workshop debit; one500Favor charge after APPLIED; no stamp; receipt/save/rollback unaffected; disabled/headless/reconnect; both builds. Gold Core review before shared integration. Root edits serially.

## PRESENTATION / VANILLA ASSET PLAN
- Intent: UI shows one-action q5 creation cost500Favor, disabled reason; success cue only after item+receipt durable save, owner-local, never on rejection/replay. Not an unlock ceremony.
- UI donor: actual InventoryGui.m_craftButton existing native Button/Image/font clone with listeners stripped and owned controller/lifecycle. Need exact native layout/hierarchy inspection; placement choice pending user.
- Animation donor: inspect native InventoryGui completion EffectList/trigger source; prefer actual vanilla craft completion cue. No custom assets or guessed prefab names. New cosmetic subsystem forbidden; reuse native effect list/shared infrastructure. Meaningful alternate VFX choice requires user decision.
- Budgets: at most1 button per InventoryGui; one cosmetic cue per successful local action, no replay; native donor budgets as existing, no additional continuous scans; headless no UI/VFX; final layout/appearance live acceptance not claimed.

## Phases fixed before implementation (100%)
1. Scope/native path and owner review20%: 2 units (native/current scope; affected-owner review).
2. Implementation40%: 3 units (button/timer; action/admission/metadata; success presentation).
3. Build/static/model review30%: 3 units (client build; server build; bounded review+meaningful QC).
4. Handoff/evidence10%: 1 unit (readback/exact next action/current limitations).
No intuitive progress; pending choice/review/build counts incomplete.
## Finalization reservations / synchronization
- Add exact owned documentation reservation docs/gold/UTILITY_100.md: current action status/source navigation only, no Gold Core policy invention.
- Newest Tick lifecycle amendment: on session reset, stop only matching masterwork timer BEFORE clearing transient intent. Durable journal unchanged. Bounded reviewer PASS; final Gold affected-boundary review pending.
- New Gold Core human policy task GOLD_CORE_COST_TIER_COOLDOWN_20261005 supersedes old cooldown duration. It owns policy/ledger edits. Utility100 must copy only their accepted core into its isolated snapshot before final builds; no write to canonical Gold core files. Actual current consumers remain Völundr-only; other-patron exception is not a newly implemented Utility action.
- Initial snapshot dual Build123/provenance PASS; 27 Gold100 compiled assertions per variant and37 policy/receipt/ledger checks PASS. Two initial QC failures were assertion representation mistakes (ReferenceEquals optimized to beq), corrected against actual IL; no production/plugin defect or forced corruption.
- Final acceptance remains STATIC VERIFIED only; no live UI clipping/navigation, timer/persistence/transport failure injection or effect/audio runtime acceptance claimed.
## Final acceptance / release
- Gold Core last Tick review SOURCE REVIEW APPROVED for exact GDT CA7B7424C9760E87A8409A3A5747003045399C06899054BB916A97E77988A276. Bounded reviewer final lifecycle PASS. No open source-review blockers.
- Synchronized latest source-approved Gold ledger/policy in isolated snapshot; masterwork-core-sync dual Build123/provenance,27 compiled checks/variant,42 model checks PASS. Shared core canonical files never written by Utility100. No action cooldown duplicate introduced.
- Root integrated only five action/UI source files plus own QC after unchanged original hashes, current dependency hash checks and full readback. integration-manifest.csv records old/candidate/canonical hashes. No foreign source drift at snapshot comparison; exact core snapshot matches canonical.
- Final progress units2/2,3/3,3/3,1/1 =>100% of authorized task. Exact reservations RELEASED after evidence/readback; no implied deploy/live permission.
- Current report validation/utility100-masterwork-button-20261005/REPORT_UA.md and owned docs/gold/UTILITY_100.md updated. Next authorized scope would be controlled live UI/action regression listed in report; protect all production state.
## Additional exact reservations / independent preparation
- src-modern/GoldMasterworkRules.cs: new pure admission/station-kind policy; validation folder modelQC links actual rules.
- src-modern/GoldMasterworkPresentation.cs: new domain success consumer using native CraftingStation/InventoryGui.m_craftItemDoneEffects through existing VfxPool/PerkAudioService, not new loaders or assets.
- Native source evidence saved validation/utility100-masterwork-button-20261005/native-inventory-crafting-il.txt: Player.SetCraftingStation calls HideHandItems(false,true); native OnCraftPressed copies selected recipe/variant/timer; UpdateRecipe calls DoCrafting before timer=-1; cancel/Hide reset timer; DoCrafting uses native station/gui m_craftItemDoneEffects.
- Reviewer preparation accepted: Kind1-only branching, no shared Workshop helper edits, exact context intent and cancel clearing only transient UI, stamp Remove on clone, protected post-save presentation catch. Gold Core affected-owner review still pending.
- UI placement choice still pending; no UI implementation before reply. Isolated snapshot draft only until affected-owner review/shared-write release.

## Decisions and review2026-10-05
- User explicitly selected button UNDER normal craft button, native Valheim style with restrained gold accent. All dependent choices resolved. No masterwork hotkey introduced; legacy Forge activation remains.
- Gold Core affected owner design review: .agent/handoffs/GOLD_CORE_UTILITY100_ADMISSION_REVIEW_20261005.md, CONDITIONALLY APPROVED DESIGN ONLY; source reservations released. Final candidate must be reviewed before canonical integration.
- Extra current source count is >198 due other owners; don't compare yesterday's inventory as current. Snapshot freezes current source; only exact owned files integrated after drift checks.
- Success presentation selects actual native completion EffectList donor at matching station/gui; reuse VfxPool sanitized cosmetic clones and PerkAudioService.PlayPrefab, max4 entries,2s pooled lease,0.5s audio throttle, client-local/headless guard; no new named assets/imports. Static native provenance only; loading/appearance/audio live unverified.
