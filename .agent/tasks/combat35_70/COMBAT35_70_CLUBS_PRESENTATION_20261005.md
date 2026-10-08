# COMBAT35_70_CLUBS_PRESENTATION_20261005
- Owner: COMBAT_35_70; complexity COMPLEX; status AWAITING_LIVE_ACCEPTANCE.
- Human authorization: latest request explicitly asks faster mace charging, Valheim-style bar, charging block threshold +50%, hammer/mace presentation improvements, and tower-shield verification. Scoped source/static builds authorized; deployment/launch still not authorized.
- Source baseline: 1.4.123, current Build123 gates; prior baseline task remains evidence, not live acceptance.
- Exact write reservations: src-modern/Clubs70Reservation.cs; src-modern/Clubs70ChargeHud.cs; src-modern/Clubs70EchoService.cs; src-modern/ShieldRush70.cs; this task and matching progress/handoff; validation/combat-clubs-20261005/**. No overlapping active reservations found. Other owners' Torba source changes are preserved.
- Scope: mace charge 15->22.5 stamina/s (owner tuning assumption, +50%; hammer stays10), native HUD style while preserving existing fill arithmetic, block-only charging stagger threshold x1.5 with guaranteed restoration, bounded echo and shield contact diagnostics. Current echo damage contract preserved while unobserved results are investigated.
- Acceptance: separate client/server candidate builds; native BlockAttack threshold path inspected; exception-safe restoration; no shared ItemData/asset mutation; echo scheduling/target diagnostics; tower shield remains gated active; native style donor reuse; no runtime PASS inferred.
- Non-goals: deployment, launch, world/config/character changes, shared presentation redesign, unapproved exact VFX/SFX substitutions, general slope traversal redesign.
- Shared files mutated: NONE. Additional block hook affects Character threshold transiently only inside owned charged-club BlockAttack; Ashlands feast x1.3 remains multiplicative. No utility file edits. New presentation is mechanic-local.
- Required regression: partial/full/pause/resume/expiry; block threshold normal/charge/exception/feast; owner-generated echoes and geometry; wall/rock/door/slope contact and post-stop state; client/server/static then actual user live run.
- Deployment: NONE.

## PRESENTATION / VANILLA ASSET PLAN
Intent: readable stored stamina, ready/expiry, distinguish charge and direct vs delayed impacts.
HUD: reuse sprites/materials from actual Hud.m_staminaBar2Fast/m_staminaBar2Root and native font; no imported assets. Preserve fraction=reserve/(naturalMax*0.5).
Current charge: native vfx_HitSparks gnista emitter, max48 particles, forge crafting clip loop .04-.16 volume. Current impact: block_wave emitter from fx_sledge_demolisher_hit and native weapon hit audio; presence is not visual/audio acceptance.
User choice received: physical ground pressure for hammer and localized sparks/contact for mace, with physical hit audio. Existing native donors retained; no magical or imported substitutes.
Diagnostics: verbose only; one record per queue/replay/target/stop, no frame/tick spam. Owner/target IDs and delay/charge included; dispatch != applied HP result on remote owner.

## User evidence correction
User did not observe hammer echo, or its visible/audible confirmation. Prior radius2 traces are insufficient to confirm any echo damage/presentation. Mace filling itself is positively reported; preserve arithmetic/lifecycle.

## Before presentation write
- User explicitly selected physical wave/sparks/impacts. Preserve current native donors `fx_sledge_demolisher_hit` (block_wave only), `vfx_HitSparks`, weapon-native hit clips and existing `sfx_club_hit` fallback; no new asset substitutions. Clip identity unresolved until runtime.
- Additional exact reservations: src-modern/Hammer35DustRing.cs (mechanic-local NativeGroundPressure/NativeContactSparks only); src-modern/Hammer35Epicenter.cs (impact audio volume only).
- Ground-wave budget: one particle, native texture/material/size curve, max one main+two delayed hammer rings, lease .95s; no dust/rocks/lighting/attack components. Explicit emission does not depend on native t0 burst. Contact sparks remain capped32; charge max48 unchanged.
- Audio: native source settings, owner-side spatial source copied by PerkAudioService; echoes .85-.90 source volume, pitch .85-.95, throttle .18s. No remote replication infrastructure changed.

## Source/static handoff 2026-10-05
- Evidence outputs additionally scoped to validation/combat-clubs-20261005-build1/** and validation/combat-clubs-20261005-build2/**; no overlap with other task outputs. Reservations released at this handoff; future writes must reacquire.
- Implemented: mace charge22.5 stamina/s vs15 (+50% rate, one-third less filling time; initial1s unchanged); hammer10 unchanged. Native stamina HUD sprites/material/font copied into mechanic-local panel; fill arithmetic preserved.
- Charging block factor x1.5 is transient in Humanoid.BlockAttack, restored by Harmony finalizer, exceptions propagated. Nominal .4->.6 max HP; existing Ashlands feast modifier stays multiplicative. Full/ready-but-not-charging state does not receive boost.
- Physical wave: selected native block_wave explicitly emits1, activates emitter ancestry. Zero inner radius stays zero (no unintended tiny inner ring). Contact sparks select gnista,16 direct/24 secondary capped32. Hammer impact audio .75; echoes .85/.90. Visibility/audibility remain unverified.
- Echo queue initializes scene before enqueue (prevents first subsequent Tick clearing a newly queued echo); pending capture deduplication retained. Attack IDs correlate commit/release/hammer dispatch/queue/replay/generated target dispatch. Damage amounts are requested payloads, NOT applied HP results.
- Shield: tower code already active under MASTERY_SHIELD_RUSH_EXPERIMENT (8.25m/.30s vs light16.5m/.60s). Added episode/contact/reason and pre/post-stop velocity/action state diagnostics. No slope/animation movement behavior redesign; reported hill issue remains open.
- Final builds: build2 client0 errors/9 warnings; server0 errors/7 warnings. Both: Harmony contracts403/errors0; patch12324 STATIC checks; combat-input STATIC PASS; focused candidate18 IL-shape checks PASS. Failed initial QC invocation used wrong DLL basename and was rerun successfully against confirmed ValheimMastery.dll; final logs supersede that invocation.
- Candidate client SHA256 D7D9332A93FAC27A60B79474FB3F6F80B2A9AAEAECE4D953BBF50E8D230B8F13; server65DB70D64AC3A63C7D2E46471C23DF53370DF74DD9C510D27804DA76C3254921. Built from current shared source, including preserved Torba-owner changes; not a combat-only packaged release.
- Installed hashes unchanged: client839D03955995ECBC5E3B80698D0E8BB9CE86A6C5072183F38868D74B15EFD409; server95DEB8ADD0629FC7E37D1C42F9CFBFDED53688113274CB265164A4259B47C241. No deployment, game/server launch, config/world/character edits.
- Not claimed: PatchAll in Unity, live exception execution/restoration, client/server proc authority, damage acceptance, large/boss/door/slope runtime passes, VFX/SFX acceptance. Earlier client/server logs lack correlated applied outcomes; this task cannot retrospectively reconstruct them.
- Next authorized gate: deployment/live testing requires explicit human authorization. Use protected test world; record matching client+server session, charge commit/release and attack IDs; measure target-owner HP result; test first/partial/full/whiff/pause/resume/cancel/expiry and large/boss targets. For tower/light shield test wall, rock, closed/open door, small hill/slope, then movement/block/dodge/attack and collision restoration after stop. No shared-system files changed.
