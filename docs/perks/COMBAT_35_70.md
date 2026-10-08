# Canonical source-derived index

As of2026-10-04,1.4.123. Read CONTEXT_AND_STATUS and KNOWN_ISSUES. This is a concise source navigation/status placeholder, not a full gameplay specification. Exact mechanics require current source and compile gates; no new design approval. Gameplay regression DEFERRED. No source presence promoted to LIVE VERIFIED.

## Status: MIXED
IMPLEMENTED IN SOURCE: Non-magic milestone gameplay: Swords/Axes/Clubs/Knives/Spears/Polearms/Bows/Crossbows/Fists/Blocking/Dodge paths listed below; build-gated exceptions must be checked.
STATIC VERIFIED: current variant build/registration evidence; not mechanical acceptance.
LIVE VERIFIED: no complete current1.4.123 domain matrix. Historical user reports/checkpoints are reference, not current PASS.
## Owner / source
COMBAT_35_70. Swords70Perk.cs, Knife70ShadowStrike.cs, SpearThrowLifecycle.cs, Spear70Hook.cs, Polearm35AutoSpin.cs, Bows70WeakPointPerk.cs, Overdraw70Perk.cs, Crossbows70Perk.cs, Fists70Maul.cs, Mace35CorpseProjectile.cs, Hammer35Epicenter.cs, Clubs70Reservation.cs, ShieldRush70.cs, Dodge70Refund.cs.
## Required regression
Light/secondary and partial/full/overheld charge; cancel/resume/expiry; bosses/large targets; world/mob collision; owner transfer; native baseline; client/server effects.
## Boundaries / current unresolved work
Crossbows70Perk is behind !MASTERY_RELEASE_SAFE; current Build123 defines MASTERY_RELEASE_SAFE, so that source path is excluded. Determine any alternate compiled implementation before claiming enabled perk. Hammer/mace charges/echo and shield rush123 source updates need live verification. Physical Blocking is NOT Shield35Renewal blood channel.
Exact content/description owned here; UX framework changes need UX review. Shared systems require root record/review. Do not fix known issues during architecture work.

## Crossbows35/70 source/install update —2026-10-05
[Current task](../../.agent/tasks/combat_35_70/CROSSBOW35_70_FIELD_TURRET_20261005.md); [source/static/install report](../../validation/crossbow-field-20261005/deploy/RESULT_UA.md). Supersedes Crossbows uncertainty above, not a domain-wide LIVE claim.
35: existing skillcontribution85%, activeactualreloadarmor1.4current+20/staggercapacity2x; oldmovement/sprintpatches removed.
70: new CrossbowTurret{Rules,Stock,Custody,Handoff,ContainerGuards,Commands,Runtime,Projectile,Presentation}.cs execution path implemented andinstalled both1.4.123variants. Secondary withcrossbow70 createsoneplatform; nativeoneactualweapon/<=10compatiblebolts supply;emptyhandssecondary activates/retires;nativecargo retrieval thenemptysecondaryclears. SAMEpersistentnativecontainer,server-onlyACTIVE,nativehostile-onlybolts/XP0/generatedvariant2070;death/portal/disconnect/unloadpause,enemydestructionRecoverywithoutdrops. Actualweaponmesh/material onnativeBallistabase;userchosefx_turret_fireheavyBallistasound. OldCrossbows70Perk !MASTERY_RELEASE_SAFE still excluded, no piercingactivated.
STATIC VERIFIED:37QCgroups/418contracts0errorspervariant,294purechecks,32/4referenceprovenance; exact installedSHAs in BUILD_RUNTIME_BASELINE. LIVE_TEST_REQUIRED: trueitem GUI/handshake/aim/cadence/hostile-allied/return/destruction/restart/multiplayer/visual/audio matrix, oldlogs insufficient. Ordinarynative supply/return save guarantees humanapproved; nofictionalatomicprofile/world/projectile guarantee.

