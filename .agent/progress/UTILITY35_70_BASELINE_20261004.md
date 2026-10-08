# UTILITY35_70_BASELINE_20261004 progress
Phase: BASELINE / READ-ONLY. Status: COMPLETE (bounded bootstrap only).
Weighted phases defined before execution-path inspection, total 100:
1. Bootstrap/authorization/boundaries 20%; units 1/1 completed: AGENTS/profile/bootstrap and named canonical docs read; no prior Utility task/progress/handoff.
2. Source baseline 50%; units 4/4 completed: (a) project/plugin both 1.4.123, 198 C# files and src-modern inclusion; (b) Build123 symbols and two variant configurations; (c) entry-point navigation for all 11 skills; (d) workshop/processing/forge entry points.
3. Durable baseline/unresolved-work record 20%; units 1/1 completed: below evidence and matching task/handoff.
4. Final readback/source-integrity verification 10%; units 1/1 completed: source digest matched exactly for 198 files; three final records reopened and checked.
Progress: 100% of bounded bootstrap/documentation scope; not gameplay implementation/regression.

## Source/config evidence, 2026-10-04
- Authority: src-modern + ValheimMasteryPoC.csproj; project Version 1.4.123; MasteryPlugin.cs:13 Version 1.4.123. Project includes src-modern/**/*.cs, excludes three magic70 files unless MASTERY_MAGIC70_EXPERIMENT.
- Count/digest before baseline source inspection: 198 C#; SHA256 CA14B161D2157BDAF662A0983D635AEA32FEA5F8DDE72D97FB69F42A7C3ADE77. Digest = SHA256 of UTF8 newline-joined sorted relative source paths plus per-file SHA256. This verifies source bytes, not runtime behavior.
- tools/Build123.ps1:4 configures MASTERY_RELEASE_SAFE, MASTERY_CLUB_PASSIVES, MASTERY_SPEAR35_EXPERIMENT, MASTERY_WORKSHOP_REMOTE_EXPERIMENT, MASTERY_SPEAR70_EXPERIMENT, MASTERY_MAGIC70_EXPERIMENT, MASTERY_CLUBS35_EXPERIMENT, MASTERY_CLUBS70_EXPERIMENT, MASTERY_SHIELD35_EXPERIMENT, MASTERY_SHIELD_RUSH_EXPERIMENT, MASTERY_CARRIER70_EXPERIMENT.
- Build123 targets Perks123Client and Perks123Server with distinct native-managed references and isolated intermediates. Script read only; no fresh build or installed-assembly verification. Existing clean-QC report lines 87/92 is historical same-day evidence, not a new PASS.
- WorkshopRemoteCraft.cs:120-130 Enabled is gated by MASTERY_WORKSHOP_REMOTE_EXPERIMENT, configured on by Build123. Gates in the inspected mixed files surround other mechanic classes; do not assign entire mixed files to Utility.
- MasteryPlugin.Awake calls Harmony.PatchAll; Update wires WorkshopRemoteCraft/Recovery/ChestLease, FarmingPerkService, WoodCuttingChainService. Navigation identifies runtime entry points; no registration test executed here.

## Bounded execution-path map (IMPLEMENTED IN SOURCE navigation)
All paths relative to canonical repository. This is entry-point evidence, not an exhaustive specification or correctness audit.
| Mechanic | Current source/class entry points |
|---|---|
| Running | PerkGameplayPatches35.cs:29 Running35EmergencyStaminaPatch; Stride70Perk.cs:12 Stride70Service, :117 update hook, run/swim/slope/liquid hooks; RoadRhythmPhaseVisual.cs:10 presentation. Source rhythm phases at 3/5/7 seconds; temporary water support and cleanup. |
| Jumping | PerkGameplayPatchesA1.cs:8 Jumping35SafeFallPatch, :20 Jumping70FallProtectionPatch; MovementPerks.cs:52 Jump70AirJumpPatch and stable-ground arming. |
| Swimming | MovementPerks.cs:79 Swim35DrainPatch, :91 SwimSpeedPatch, :103 Swim70RestPatch; PerkGameplayPatches35.cs:407 Swimming70EmergencyStaminaPatch, :420 Swimming35WetDurationPatch. |
| Sneaking | MovementPerks.cs:121 Sneak35DrainPatch, :141 noise, :147 visibility; VeiledPerk.cs:11 VeiledService with sight/hearing/retention and attack/damage break hooks. |
| Fishing | PerkGameplayPatches35.cs:173 Fishing35PreserveBaitPatch, :217 catch request; Fishing70Perk.cs:52 TryToHook observation and :62 FixedUpdate stamina patch; three-second hook window, 0.30 multiplier with postfix/finalizer restoration. |
| Riding | PerkGameplayPatches35Objects.cs:160 Riding35StaminaRegenPatch; PerkGameplayPatches70Foundation.cs:8 Riding70MountMitigationPatch; RidingPerkAuthority.cs:5 rider authority and :20 pushback hook. |
| Farming | PerkGameplayPatches35.cs:248 Farming35BonusHarvestPatch; FarmingPerks.cs:19 service with CaptureHarvest/Tick/TryReplant, placement/spacing/roof/taming hooks. SapHarvestBonus.cs RPC_Extract/output observation remains related Utility mechanics. |
| Woodcutting | PerkGameplayPatches35Objects.cs:259 WoodCutting35Service with tree/log/stump patches; WoodCuttingChainService.cs:25 spawned-resource chain; WoodInventoryCapacity.cs:11 owned expanded-stack behavior; PerkProfessionService in PerkRuntimeServices.cs:162 has WoodCutting70 weight branches. |
| Pickaxes | PerkRuntimeServices.cs:162 PerkProfessionService has Pickaxes35 nonteleportable/nonquest weight branch via PerkGameplayPatchesA1.cs:47 MasteryInventoryWeightPatch. Pickaxes70SuperHit.cs:18 DamageArea hook, generated-depth guard, per-hit observation, neighbour versus forced collapse, :116 delayed native-collapse witness. Pickaxes35LegacyMineRock.cs is a passive/resource hook despite filename; do not treat it as milestone35 ownership proof. |
| Cooking | Cooking35Perk.cs:8 service with UpdateFood/CookItem/SpawnItem; CookingAuthorNetwork.cs slot-author RPC and food persistence; Cooking70Perk.cs feast creation/author/serving patches. Prepared food, mead and feast metadata require separate regression. |
| Crafting | PerkGameplayPatches35.cs:353 transaction and :395 resource consumption; CraftingOutcomeSnapshot; StationBulkLoad.cs:8 ExtraToQuarterCapacity and ore/fuel hooks; CraftingBuildReuseService; PerkGameplayPatches70Polish.cs:355 Crafting70Service and persisted structure-support hooks; Workshop paths below. |

## Technical subsystems stay inside Utility35/70
- WorkshopNetworkStorage and WorkshopResourcePlan: chest eligibility and resource plan. WorkshopHostCrafting.cs Begin/ObservePayment/Finish uses actual output snapshot plus payment observation before commit, otherwise rollback; Supported excludes upgrader stations. WorkshopAtomicDebit.cs:37 Begin aggregates per stable storage identity, locks before validation/capture, checks exact removed counts; :90 Commit; Dispose restores/flushes and retains failed-rollback locks. This is local state-machine evidence, NOT exactly-once across crashes/concurrent clients proof.
- WorkshopStorageLockPatch.cs:6 CheckAccess blocks lease/atomic-lock/escrow access. WorkshopLeaseRules.cs:7 compares replicated and acknowledged snapshots byte-for-byte and validates ownership/readiness. Remote craft registers request/reply/ACK, recovery/lease/station proof/timing. Durable requests/receipts/settlement remain one gameplay ownership with root shared integration; crash/retry correctness not audited.
- ProcessingPersistentBatches.cs:10/11 versioned ZDO output/input receipt keys; Record/Advance/Complete preserve author/level/bonus receipts with queue positions. ProcessingStationProgression.cs:60 input owner hook calls Record; :82 output hook reads ledger and groups receipts by author. No producer-versus-collector/persistence PASS claimed.
- PotentialForgeSafetyPatch.cs:20 DoCrafting prefix/postfix/finalizer restores shared upgrader-resource break/success chances; protection applies 1 <= currentQuality < resourceUpgradeCap. PotentialForgeRepair.cs repair/recipe/UI hooks. Utility100 action and Gold protocol boundaries remain separate responsibilities; no Gold code inspected or changed beyond canonical boundary docs.

## Risks and authorization limits
- KI03: Owned=true WorkshopPlanner QC coverage gap. KI05: historical HalfCapacity assertions stale; current source ExtraToQuarterCapacity confirmed. KI06/KI11: landing/run-phase current scene evidence pending. KI10: pickaxe70 proc versus native support cascades unresolved; redesign DEFERRED. KI14: complete gameplay regression explicitly postponed.
- Required future matrix: exactly-once debit/result; concurrent players; local/remote/owned/unowned storage; full inventory; cancel/retry; producer versus collector; processing persistence/reconnect/restart; sprint phase/jump/water transitions; crafting/forge caps/failure; pickaxe proc/support collapse.
- No SHARED SYSTEM CHANGE: no source/shared/build mutation. Future shared mutation needs declaration BEFORE change, affected-owner review and root integration.
- Baseline only: no fixes/builds/QC execution/deployment/runtime launch/packaging/state edits/Git initialization. No LIVE VERIFIED status.
- No scouts needed for this SIMPLE bounded bootstrap. Future authorized native/concurrency investigation uses bounded read-only assignments under root policy.
- Environment: ordinary shell fails before process creation with helper_unknown_error: apply deny-read ACLs. Reviewed escalated calls work. No automatic approval rejection remains outstanding.
- Exact documentation reservations: three matching task/progress/handoff files only; released after final verification.
- Exact next action: receive an explicitly scoped human task. Phase remains BASELINE / READ-ONLY until human switches it.

