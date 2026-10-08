# Scope map — current1.4.123

Nine gameplay owners only:
- XP_SKILL_BONUSES: XP calculation; boss/world/tier/first-time/resource modifiers; native/custom skills persistence; death penalties/floors; level-dependent passives (not milestone gameplay).
- COMBAT_35_70: Non-magic milestone gameplay: Swords/Axes/Clubs/Knives/Spears/Polearms/Bows/Crossbows/Fists/Blocking/Dodge.
- MAGIC_35_70: Elemental/Blood milestone gameplay; summons/roster/follow/portals/desummon; summon cargo; zones; cast/input lifecycle.
- UTILITY_35_70: Run/Jump/Sneak/Swim/Fishing/Ride/Farming/WoodCutting/Pickaxes/Cooking/Crafting35/70; workshop/network storage; processing attribution; Potential Forge.
- GOLD_CORE: Shared100 infrastructure: milestone/ACK, Favor/Pantheon/exhaustion, ledger, receipts/idempotency, transaction protocol, shared Gold persistence/UI.
- COMBAT_100: Only actual non-magic combat100 execution paths; action semantics remain here, shared Gold protocol stays GOLD_CORE.
- MAGIC_100: Only actual magic100 execution paths; Gold action effects not shared Gold ledger infrastructure.
- UTILITY_100: Actual utility100 effects: confirmed Pickaxes metal-weight source branch; Crafting100 divine crafting/forge action semantics; untraced slots remain unresolved.
- CORE_UX_TUTORIALS: Milestone/repeated crossing presentation framework; one-time Huginn/Muninn tutorials; shared UI/tooltips/layout/localization infrastructure/input focus.
INFRASTRUCTURE is technical stewardship, REGRESSION_INTEGRATION is cross-domain validation, neither a tenth gameplay owner.

## Mechanic routing (one gameplay owner per mechanic)
- Vanilla/custom skill XP, death penalty/floors, every level-dependent passive → XP_SKILL_BONUSES, including magic regen and bare-fist scaling.
- Every non-magic weapon/Blocking/Dodge milestone35/70 → COMBAT_35_70.
- Every magic35/70 spell, cast, summon, summon portal and Torba cargo → MAGIC_35_70. Shield35Renewal is blood magic.
- Remaining milestone35/70 skills incl Run/Jump/Sneak/Swim/Cooking/Crafting/processing/sap/workshop/forge → UTILITY_35_70.
- Shared Gold ACK/Favor/exhaustion/ledger/receipt/protocol/common Pantheon UI → GOLD_CORE.
- Specific actual100 effects → corresponding COMBAT_100/MAGIC_100/UTILITY_100. Crafting100 action effect and Pickaxes100 metal weight → UTILITY_100; shared Gold transaction stays GOLD_CORE.
- Tutorial/presentation/shared UI/localization/input framework → CORE_UX_TUTORIALS. Exact perk wording owned by its mechanic domain; UI delivery infrastructure owned by UX.

## Technical tag routing
SUMMON_PORTAL/SUMMON_ROSTER/CARGO_PERSISTENCE/ZONE_OWNER → MAGIC_35_70.
WORKSHOP_STORAGE/INVENTORY_CAS/PROCESSING_ATTRIBUTION/AUTO_OUTPUT → UTILITY_35_70 (Gold action CAS additionally relevant Utility100).
GOLD_LEDGER/RECEIPT_IDEMPOTENCY/CRASH_RECOVERY → GOLD_CORE; action-effect regression co-reviewed by100 owner.
XP_RECIPIENT → XP_SKILL_BONUSES.
PROJECTILE_OWNER → relevant combat or magic projectile owner.
GENERATED_HIT_RECURSION → effect-owning Combat/Magic/Utility domain + XP recipient review.
TUTORIAL_SEEN_FLAGS/LOCALIZATION_STRUCTURE/UI_LAYOUT → CORE_UX_TUTORIALS; content owner reviews meaning.
OWNER_AUTHORITY/RPC_AUTHORITY/VFX_SFX/ASYNC_ASSETS/INPUT_CANCEL/ANIMATION_EXIT/SAVE_ORDER/HEADLESS/RECONNECT/SERVER_RESTART → affected mechanic owner, root shared integration and regression coordination.
A tag does not spawn an owner/chat.

## Complete source navigation inventory
198 C# files present. This index assigns navigation/stewardship, not a blanket write permit or proof of implementation. Shared mixed files route individual classes/effects by the rules above. No source moved or split. Build #if may exclude source paths.

### COMBAT_35_70

- src-modern/AssassinBlink70.cs
- src-modern/AttackIntentService.cs
- src-modern/AxeGeometryAudit.cs
- src-modern/Blocking35CorrelatedProjectiles.cs
- src-modern/Blocking35GuardBreakFallback.cs
- src-modern/Blocking35ProjectileDraft.cs
- src-modern/Blocking70Reflection.cs
- src-modern/BlockingStoredPressure.cs
- src-modern/Bow70ArrowProfileService.cs
- src-modern/Bows70WeakPointPerk.cs
- src-modern/Clubs35Counterblow.cs
- src-modern/Clubs70ChargeHud.cs
- src-modern/Clubs70ChargePresentation.cs
- src-modern/Clubs70EchoService.cs
- src-modern/Clubs70Reservation.cs
- src-modern/ClubsReserveRules.cs
- src-modern/ClubWeaponClasses.cs
- src-modern/Crossbows70Perk.cs
- src-modern/Dodge70Refund.cs
- src-modern/Fists70HitRelay.cs
- src-modern/Fists70Maul.cs
- src-modern/Fists70Parry.cs
- src-modern/Hammer35DustRing.cs
- src-modern/Hammer35Epicenter.cs
- src-modern/Knife70ShadowStrike.cs
- src-modern/Mace35CorpseProjectile.cs
- src-modern/Overdraw70Perk.cs
- src-modern/Overdraw70V2.cs
- src-modern/OverdrawPayloads.cs
- src-modern/OverdrawPenetration.cs
- src-modern/PerfectDodgeStagger.cs
- src-modern/PerkGameplayPatches70Combat.cs
- src-modern/Polearm35AutoSpin.cs
- src-modern/SecondaryAttackDiagnostics.cs
- src-modern/ShadowStep35.cs
- src-modern/ShadowStrikeGuarantee.cs
- src-modern/ShieldRush70.cs
- src-modern/ShieldRushImpactVisual.cs
- src-modern/ShieldWeaponClasses.cs
- src-modern/Spear70Hook.cs
- src-modern/SpearPinned70.cs
- src-modern/SpearThrowLifecycle.cs
- src-modern/Swords70Perk.cs

### MAGIC_35_70

- src-modern/BloodMagic35Perk.cs
- src-modern/BloodSkeleton35Service.cs
- src-modern/CarrierBirthVisual.cs
- src-modern/CarrierPackVisual.cs
- src-modern/Fire35CoreChargeVisual.cs
- src-modern/Fire35PoseDriver.cs
- src-modern/FireStaff35Charge.cs
- src-modern/Ice35Exposure.cs
- src-modern/IceStaff35FlyingPressure.cs
- src-modern/IceStaff35Trail.cs
- src-modern/IceTrailVisual.cs
- src-modern/Magic70BloodDome.cs
- src-modern/Magic70Carrier.cs
- src-modern/Magic70CarrierRuntime.cs
- src-modern/Magic70CastAnimation.cs
- src-modern/Magic70IceStorm.cs
- src-modern/Magic70Surtling.cs
- src-modern/MagicShield35Service.cs
- src-modern/NativeSnowVisualAssets.cs
- src-modern/NativeStormWeatherVisual.cs
- src-modern/Shield35HealingVisual.cs
- src-modern/Shield35Renewal.cs
- src-modern/Skeleton35Travel.cs
- src-modern/SummonRosterCommands.cs

### CORE_UX_TUTORIALS framework; domain owns content

- src-modern/BuildAuthorHoverCleanup.cs
- src-modern/EnemyStaggerHud.cs
- src-modern/MasteryItemTooltipPatch.cs
- src-modern/MasteryMilestonePresentation.cs
- src-modern/MasteryRavenTutorials.cs
- src-modern/MasteryWindow.cs
- src-modern/MilestoneCrossingRule.cs
- src-modern/PerkLocalization.cs
- src-modern/PerkLocalization.Ukrainian.cs
- src-modern/PerkNarrativeService.cs
- src-modern/PerkProcHudService.cs
- src-modern/PerkUiIconService.cs
- src-modern/SkillsTooltipPatch.cs

### UTILITY_35_70

- src-modern/Cooking35Perk.cs
- src-modern/Cooking70Perk.cs
- src-modern/CookingAuthorNetwork.cs
- src-modern/CraftingBuildReuseModel.cs
- src-modern/CraftingBuildReuseService.cs
- src-modern/CraftingOutcomeService.cs
- src-modern/FarmingPerks.cs
- src-modern/Fishing70Perk.cs
- src-modern/MasterFeastService.cs
- src-modern/MasterFeastThemePatches.cs
- src-modern/MovementPerks.cs
- src-modern/NativeMovementPulse.cs
- src-modern/Pickaxes35LegacyMineRock.cs
- src-modern/Pickaxes70SuperHit.cs
- src-modern/PotentialForgeRepair.cs
- src-modern/PotentialForgeSafetyPatch.cs
- src-modern/ProcessingBatchLedger.cs
- src-modern/ProcessingPersistentBatches.cs
- src-modern/ProcessingStationProgression.cs
- src-modern/RidingPerkAuthority.cs
- src-modern/RoadRhythmPhaseVisual.cs
- src-modern/RoadRhythmVisual.cs
- src-modern/RunSpeedWakeVisual.cs
- src-modern/SapHarvestBonus.cs
- src-modern/StationBulkLoad.cs
- src-modern/Stride70Perk.cs
- src-modern/VeiledPerk.cs
- src-modern/WoodBonusResource.cs
- src-modern/WoodCuttingChainService.cs
- src-modern/WoodInventoryCapacity.cs
- src-modern/WorkshopActor.cs
- src-modern/WorkshopAtomicDebit.cs
- src-modern/WorkshopBuildBridge.cs
- src-modern/WorkshopBuildRay.cs
- src-modern/WorkshopChestLease.cs
- src-modern/WorkshopConnectionVisual.cs
- src-modern/WorkshopDurableRequests.cs
- src-modern/WorkshopHostCrafting.cs
- src-modern/WorkshopLeaseRules.cs
- src-modern/WorkshopNetwork.cs
- src-modern/WorkshopNetworkStorage.cs
- src-modern/WorkshopReceipt.cs
- src-modern/WorkshopRecordStore.cs
- src-modern/WorkshopRecovery.cs
- src-modern/WorkshopRemoteCraft.cs
- src-modern/WorkshopRequestLedger.cs
- src-modern/WorkshopResourcePlan.cs
- src-modern/WorkshopResourceReservation.cs
- src-modern/WorkshopSettlementHistory.cs
- src-modern/WorkshopStationPrewarm.cs
- src-modern/WorkshopStationProof.cs
- src-modern/WorkshopStationRadius.cs
- src-modern/WorkshopStorageLockPatch.cs
- src-modern/WorkshopStoragePreview.cs
- src-modern/WorkshopStorageUi.cs
- src-modern/WorkshopTiming.cs
- src-modern/WorkshopWorldRecords.cs

### XP_SKILL_BONUSES

- src-modern/CookingCraftingProgression.cs
- src-modern/CustomSkillStore.cs
- src-modern/ExperienceContext.cs
- src-modern/FistsPassiveScaling.cs
- src-modern/GatheringProgressionService.cs
- src-modern/LegacyCatchUpService.cs
- src-modern/MagicSkillPassives.cs
- src-modern/MasteryClassificationService.cs
- src-modern/MasteryPatches.cs
- src-modern/PeacefulXpPatches.cs
- src-modern/PerkBalance.cs
- src-modern/TierDatabase.cs
- src-modern/VanillaBonusFeedback.cs
- src-modern/WorldProgressionXpLoadPatch.cs
- src-modern/WorldProgressionXpService.cs

### GOLD_CORE

- src-modern/GoldAscensionVisual.cs
- src-modern/GoldCharacterSave.cs
- src-modern/GoldCraftingLedger.cs
- src-modern/GoldFavorModel.cs
- src-modern/GoldFavorService.cs
- src-modern/GoldFavorTelemetry.cs
- src-modern/GoldInspirationStatus.cs
- src-modern/GoldPantheonUi.cs
- src-modern/GoldReceiptPolicy.cs

### SHARED / class-level routing in SHARED_SYSTEMS

- src-modern/GoldCraftingService.cs
- src-modern/GoldDivineTransactions.cs
- src-modern/MasteryEventDispatchPatches.cs
- src-modern/MasteryEvents.cs
- src-modern/MasteryEventsExtended.cs
- src-modern/MasteryPlugin.cs
- src-modern/MasteryRuntime.cs
- src-modern/MasteryStateStore.cs
- src-modern/NetworkSync.cs
- src-modern/OwnerSkillAuthority.cs
- src-modern/PerkCatalog.cs
- src-modern/PerkGameplayPatches35.cs
- src-modern/PerkGameplayPatches35Missing.cs
- src-modern/PerkGameplayPatches35Objects.cs
- src-modern/PerkGameplayPatches70Foundation.cs
- src-modern/PerkGameplayPatches70Polish.cs
- src-modern/PerkGameplayPatchesA1.cs
- src-modern/PerkRuntimeServices.cs
- src-modern/PhaseAVfx.cs
- src-modern/TargetEffectService.cs

### UTILITY_100 (content), CORE_UX_TUTORIALS (framework review)

- src-modern/GoldMasterworkTooltip.cs

### SHARED feedback/asset infrastructure; consumer owns trigger

- src-modern/NativeLandingBurst.cs
- src-modern/NativePerkAssetResolver.cs
- src-modern/NativeSoftVisualAssets.cs
- src-modern/NativeVfxSafeFrame.cs
- src-modern/NativeWindSwirl.cs
- src-modern/PerkAudioService.cs
- src-modern/PerkFeedbackService.cs
- src-modern/PerkNativeFeedback.cs
- src-modern/PerkVisualService.cs
- src-modern/VfxPool.cs
- src-modern/VfxRecipes.cs
- src-modern/WeaponGlowService.cs

### REGRESSION_INTEGRATION technical tooling

- src-modern/PerkDebugService.cs
- src-modern/RuntimeAssetAuditService.cs
- src-modern/RuntimeSelfTest.cs
- src-modern/VfxAuditionService.cs

## Reviewed boundaries still requiring task-level precision
Mixed patch files/PhaseAVfx/PerkRuntimeServices: inspect targeted class before write; root serial integration. No competing writers.
GoldCraftingService/GoldDivineTransactions: protocol Gold Core vsutility action100; ownership split by responsibility, not duplicated ownership of one effect.
Combat100/Magic100 dedicated execution paths not established; catalog-only navigation is not implementation approval.
No user-created chat or agent was launched by writing profiles. Shared workspace concurrency needs file reservations; profiles alone do not isolate state.

