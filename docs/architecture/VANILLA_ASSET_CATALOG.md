# Curated vanilla asset catalog

Canonical companion to [workflow](VANILLA_ASSET_WORKFLOW.md). Reviewed2026-10-04 against current1.4.123 source and both installed native extended manifests. Curated12 assets already investigated/used by Mastery; not a full game inventory.

## Evidence and status boundary

All12 entries are **SOURCE VERIFIED** at exact source/path identity scope. No entry is promoted to RUNTIME RESOLVED or LIVE VERIFIED by this task. Current client/server loading, renderer/audio output, remote delivery and live performance remain **UNTESTED**. Asset stage definitions are in the workflow; stage is distinct from implementation/test result.

Native input evidence:
- Client: C:/ValheimModDev/valheim_Data/StreamingAssets/SoftRef/manifest_extended, SHA25675D44C508602A0AF85E7881BB8F99D3EA9E77F7DD39FF305070FDE7A2FEB1B64.
- Server: C:/Program Files (x86)/Steam/steamapps/common/Valheim dedicated server/valheim_server_Data/StreamingAssets/SoftRef/manifest_extended, SHA256E2E68ADD2932110DF89022E38C2C518EE8A3566875A2A5226E8BE09A965E2E91.
- Exact paths below extracted from both binary manifests and cross-checked against current source. This establishes manifest membership, not resolved AssetID/object or ZNetScene membership.
- Protected before/after hashes: [task evidence](../../validation/vanilla-assets-workflow-20261004/protected-before.csv); [task](../../.agent/tasks/regression/VANILLA_ASSET_WORKFLOW_20261004.md).

Last verified Mastery/source version:1.4.123 (all rows), review date2026-10-04. Native game version label was not independently established; manifest hashes above are the exact installed input identity. Prior report game labels do not certify these assets' live behavior. Native callsites/appearance/audibility were not audited; path context below is explicitly path-level evidence or source lookup, not a claimed observed vanilla mechanic.

Availability for every row: **client extended manifest YES; server extended manifest YES; SoftRef path present; runtime resolution UNKNOWN; ZNetScene registration UNKNOWN**. NativeSoftVisualAssets deliberately declines batch/headless loading; server manifest presence is not server presentation readiness. Native weather/world/gameplay prefabs must not be instantiated wholesale for cosmetic previews.

## Investigated assets

| Asset ID / canonical name | Type and exact native path | Vanilla context evidence / uncertainty | Current Mastery use + source | Known tuning / limits / performance |
|---|---|---|---|---|
| A01 fx_land | VFX prefab; Assets/Characters/character_effects/land/fx_land.prefab | Character landing effect path; native trigger/callsite unreviewed | [Resolver](../../src-modern/NativePerkAssetResolver.cs), [NativeLandingBurst](../../src-modern/NativeLandingBurst.cs), run_35/jump_70_save recipes | Recipe root scales0.9/0.95; landing scheduler aggregate budget64. Async exact-path pending; do not substitute dodge. Visibility needs natural client landing; headless null is expected loader boundary. |
| A02 fx_perfectdodge | VFX prefab; Assets/Effects/fx_perfectdodge.prefab | Named effect; vanilla dodge callsite not audited | Resolver.ResolveLegacyRun; knife/dodge/run compositions in [recipes](../../src-modern/VfxRecipes.cs) | Knife ready scale0.28/step0.72; dodge35 scale0.65. Native settings/pooled restore preserved. Legacy run route uses this identity rather than its fx_land recipe label; no new substitution approval implied. Rapid movement demands frequency/overlap budget. |
| A03 vfx_HitSparks | VFX prefab; Assets/Effects/vfx_HitSparks.prefab | Effect path; native weapon/hit usage unreviewed | [PerkVisualService](../../src-modern/PerkVisualService.cs) fists cue; multiple recipes; [WeaponGlowService](../../src-modern/WeaponGlowService.cs) material donor | Fist cue scale0.30+stacks×0.10 in current source. Native donor material clones, no global mutation. Frequent combat cue needs aggregate count/material/brightness review. |
| A04 vfx_perfectblock | VFX prefab; Assets/Characters/character_effects/vfx_perfectblock.prefab | Character effect path; native parry trigger not independently traced | PerkVisualService blocking cues; blocking35/70 recipes | Service activation/pulse scales3.40/1.05; recipe values differ by cue. Do not treat one scale as universal. Large-target/close-camera/stacked-block visibility UNTESTED. |
| A05 sfx_perfectblock | SFX source prefab; Assets/Audio/sfx/sfx_perfectblock.prefab | Audio path; exact clip identity/native playback callsite UNKNOWN | Blocking recipes/service through [PerkAudioService](../../src-modern/PerkAudioService.cs) | Source selection AudioSource/ZSFX; blocking service throttles1.55/1.20s. No prefab attack instantiation. Spatial/volume/audible/remote behavior still requires source inspection plus audition. |
| A06 sfx_unarmed_hit | SFX source prefab; Assets/GameElements/Items/weapons/_res/sfx_unarmed_hit.prefab | Weapons resource path; actual vanilla clip/trigger unreviewed | PerkVisualService fists35_rhythm | Throttle0.28s, volume scale0.7, pitch1+stacks×0.05; service clamps apply. These are implementation parameters, not live loudness verification. Repeated combo overlap needs audition. |
| A07 SnowStorm | Native weather prefab donor; Assets/Effects/weather/SnowStorm.prefab | Weather directory; emitter refs traced by [NativeSnowVisualAssets](../../src-modern/NativeSnowVisualAssets.cs), not native weather trigger | [NativeStormWeatherVisual](../../src-modern/NativeStormWeatherVisual.cs) cloned selected native emitters; shared soft catalog bootstrap | Cosmetic emitter extraction only; do not install weather/controller. Storm aggregate particle448 (flake352/cluster96); cold soft load/readiness and native material binding UNTESTED live. |
| A08 snow_flake | Material; Assets/Effects/materials/snow_flake.mat | Effect material; source matches native emitter sharedMaterial name | NativeSnowVisualAssets Snow/CreateSnow/CreateIceSurfaceMaterial | Existing native shader/texture/streams reused; CreateSnow capacity clamp1–2048, caller budget still required. No imported texture or opaque-card replacement. Material existence does not prove texture/render readiness. |
| A09 cave_ice_floor | Material; Assets/world/Props/Caverocks/materials/cave_ice_floor.mat | Cave-rock material path; native render use inferred from donor renderer lookup, not scene observation | NativeSnowVisualAssets.CreateIceSurfaceMaterial/AcceptIce | Owned copy of snow material takes native ice texture/UV transform; texture-name check icefloor_d is source predicate, not independently cataloged texture asset. Native donor readiness required; dispose composed material. |
| A10 Ice_floor | World prefab/renderer donor; Assets/world/Props/Caverocks/Ice_floor.prefab | Cave-rock world path; game placement/collision contract unreviewed | NativeSnowVisualAssets reads donor renderer/material through resolver | Read resource references; do not spawn cave object for effect. Material/world-render/collision not automatically approved.3s fallback lookup throttle in current source, avoid per-proc scene scan. |
| A11 fx_eikthyr_stomp | VFX/SFX-bearing prefab donor; Assets/Characters/Eikthyr/fx/fx_eikthyr_stomp.prefab | Eikthyr effect path; exact native attack/sound clip unreviewed | Pickaxes collapse VFX layer; Resolver thunder alias fallback extracts native AudioSource/ZSFX clips | Collapse layer scale0.42. Visual safe clone vs audio-only extraction are separate routes; source prefab not an authorized gameplay attack. Actual thunder donor/clip may vary by environment. |
| A12 fx_lightningweapon_hit | VFX prefab; Assets/GameElements/Items/weapons/_res/Niedhogg/vfx/fx_lightningweapon_hit.prefab | Named weapon-resource effect path; native trigger unreviewed | Pickaxes70/collapse recipe layers | Scales0.50/1.1; offsets y0.15/0.35. Multi-layer brightness/concurrency need live review; no lightning damage inferred from cosmetic name. |

These values document current source choices, not new designs or recommended universal defaults. Per-cue approval and actual template settings remain required.

## Non-asset identities and deliberate gaps

- VM_ThunderFlash is a Mastery resolver alias, not a newly confirmed vanilla prefab. Resolver uses environment Thunder.m_flashEffect; selected exact donor is unknown until runtime evidence.
- sfx_thunder is a semantic resolver request in current code; environment Thunder.m_thunderEffect or fx_eikthyr_stomp fallback provides the actual donor. Do not fabricate a fixed clip/path or silently replace an explicitly selected sound.
- Mastery recipe IDs and VM_* runtime wrapper/material names are composition identifiers, not vanilla assets.
- Native wind material is obtained from actual GlobalWind/environment particle references; no stable exact material path was established. No invented material row.
- PerkUiIconService reuses native skill-row Image.sprite or caller fallback. Exact canonical icon/sprite identity was not established; no invented icon rows.
- Unknown audio clip names, native object callsites, registry membership and visual descriptions stay explicit gaps. Source comments/historical handoffs alone cannot close them.
- Source limits, generated primitive/component geometry and owned native material clones are allowed runtime composition; external asset imports remain prohibited. This bounded review is not a whole-project legacy-asset compliance certification.

## Maintenance

Add only an investigated asset with exact name/path/type, known vanilla context or explicit uncertainty, client/server/SoftRef/registry availability, named Mastery consumer, status and evidence, tuning/limits/performance notes, date and game/Mastery identity. Link the task/case and distinguish loaded asset from observed natural cue. Meaningful new choices require the user's decision under the workflow. Do not dump the native manifest or promote every recipe string.

