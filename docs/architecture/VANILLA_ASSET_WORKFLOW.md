# Vanilla-only asset / presentation workflow

Canonical policy for all Valheim Mastery domains. Source review: 1.4.123, 2026-10-04.
Use with [asset catalog](VANILLA_ASSET_CATALOG.md), [shared systems](SHARED_SYSTEMS.md), [ownership map](SCOPE_MAP.md) and [status contract](CONTEXT_AND_STATUS.md). This workflow defines design/implementation requirements; it does not certify existing gameplay or authorize launch/deployment.

## Policy and ownership

Mastery must not ship custom visual/audio assets: no custom AssetBundles, imported textures/meshes/animations/audio, or external art files. Reuse existing Valheim prefabs, VFX/SFX, meshes, materials/shaders/textures, icons/sprites and clips; Unity/runtime-native components and primitives may compose them at runtime. No new external resources, authored replacement shaders or imported animations are authorized by composition.

The gameplay owner also owns mechanic-specific VFX/SFX, semantic trigger/timing and HUD values/content. Complexity does not create a separate gameplay domain. Core UX owns shared UI/layout, milestone/tutorial presentation, tooltip/icon framework and input/focus; consult it for framework issues. Root integrates shared presentation changes with affected-owner review.

## A. Presentation intent

Write what the player must understand before choosing an asset: ready, triggered, marked target, charge progress/complete, hit success/failure, cooldown, active buff, affected area, summon creation or Gold event. Specify observer (owner/target/nearby player), authoritative state/event, primary cue and required duration. Communicate mechanics; spectacle is not acceptance.

## B. Asset discovery and evidence

Inspect real installed assets: ZNetScene registry; SoftRef manifest/manifest_extended and path→AssetID index; current NativePerkAssetResolver mappings; native inventory/audit output; VfxRecipes; actual native source/references/usages; known vanilla objects using the resource. Match exact name, case, path and type. A manifest entry does not imply registry presence or loaded/renderable asset. Never invent a name or clip.

| Asset status | Required evidence |
|---|---|
| CANDIDATE | Investigated plausible resource; identity/existence/type still uncertain. State the uncertainty; no undocumented guess presented as fact. |
| SOURCE VERIFIED | Exact identity/path/reference established in current source and/or installed native manifest/static evidence. A string in Mastery code alone records intended use, not existence; annotate unconfirmed availability. |
| RUNTIME RESOLVED | Authorized client/runtime lookup returned the matching asset/type, with game/variant/SHA/scene and resolution evidence. This proves loading, not appearance/audio. |
| LIVE VERIFIED | Real named presentation observed/heard in an authorized scenario; record trigger, tuning, observer, setting, build/state and log/visual/player observation. Does not prove all mechanics/network cases. |

Status is per asset/type/variant/context. Record date, Mastery version, native game identity (or explicitly unknown), source/manifest fingerprint and evidence. Never promote menu/static/catalog/self-test evidence to visible/audible LIVE VERIFIED. Missing observations remain UNTESTED.

## C. Proposal when no exact asset is selected

Prefer 2–4 realistic vanilla options after discovery. If fewer are evidenced, offer fewer and explain the gap.
For each option give: exact name/path if known; type; verified vanilla use/context or explicitly unknown; observed appearance/sound or clearly marked expectation; fit to intent; likely scale/timing changes; technical availability/status; client/headless behavior; performance/spam risk; live audition required or existing exact evidence.

User approval is required for a meaningful visual/audio design choice among alternatives, before implementation. Prior explicit selection or approved design remains authorization; do not ask again. A documentation/catalog task does not choose a new effect. Preparing options/parameters is reversible work and may proceed before the decision.

## D. User-selected VFX

Preserve the selected identity. Do not silently substitute another effect when loading is pending, scene is unready or a fallback looks preferable. Explain unavailable/unsafe resources and obtain a meaningful replacement decision. Keep optional already-approved fallbacks explicit.

Document: scale and world/local scaling; position offsets; rotation/orientation; socket/attachment point; owner vs target; world/local simulation; lifetime; fade in/out; particle density/rate; safe brightness/intensity; trigger moment/delay/repetition; follow vs detach; cleanup; visibility distance; overlaps/spam. State unsupported parameters instead of pretending the current API implements them.

Never mutate shared vanilla prefab, shared material, texture or native source globally. Tune safe inactive clones/instances, owned material copies or MaterialPropertyBlock. Restore pooled state between leases; release only owned resources. Preserve native shader/texture/mesh/UV/property bindings.

## E. SFX design

Identify the exact vanilla source prefab and actual clip/ZSFX selection when known; mark unresolved clip identity explicitly.
Specify trigger; local-only vs nearby observers; 2D vs 3D/spatial behavior; volume relative to a comparable vanilla sound; pitch/range; min/max distance and rolloff; concurrency; cooldown/anti-spam; loop/one-shot; start/stop/cleanup and rapid repeated-proc behavior.
Use copied native clip/settings through shared audio infrastructure. Never instantiate an attack/gameplay prefab for its sound. Do not play audio on headless servers. Avoid accumulated repeated sounds; define per-cue/actor/global limits and pending/dropped-cue handling. Existing service knobs are not proof of looping or remote delivery support.

## F. Runtime composition

Allowed: safe vanilla prefab tuning; native particle lifetime/rate; vanilla mesh with vanilla material; Unity primitive/component geometry; LineRenderer/TrailRenderer/ParticleSystem/Light; vanilla VFX/audio layers and native material property overrides. A composed runtime cue may be new while its resource provenance stays native.

Reuse recipes rather than creating one-off loaders/pools in perks. Document every donor resource, composed layer, event, instance owner and cleanup. Distinguish Mastery recipe/alias/GameObject names from vanilla asset names.
This permission does not allow importing art/audio, synthesizing replacement textures/meshes/animations as custom art, or exporting custom assets into a release.

## G. Items and world objects

Record source prefab/mesh; world and equipped appearance; socket/attachment, scale/rotation/offsets; material ownership and safe tuning; reuse of verified vanilla icon/sprite. No source-object global mutation.
Document network identity, ownership, persistence and gameplay collision separately. Presentation clones must not inherit gameplay/network controllers or collision; an actual gameplay object may have collision only under its approved gameplay contract. Cosmetic cloning does not authorize a new item/interaction.

## H. Reuse current architecture

| Current file / actual classes | Responsibility |
|---|---|
| [NativePerkAssetResolver.cs](../../src-modern/NativePerkAssetResolver.cs) / NativePerkAssetResolver | Registry-first lookup, exact soft paths, environment thunder/wind references; aliases are not canonical prefab identities. |
| [NativeSoftVisualAssets.cs](../../src-modern/NativeSoftVisualAssets.cs) / NativeSoftVisualAssets | Native path/AssetID index, held LoadAsync references, pending→loaded check, release on scene change/destroy. Exact current name, not SoftVisualAssets. |
| [VfxRecipes.cs](../../src-modern/VfxRecipes.cs) / VfxLayer, VfxRecipe, VfxRecipeService | Shared composition/tuning, cue IDs/anchors/audio. Exact service name is VfxRecipeService; not a class named VfxRecipes. |
| [PerkNativeFeedback.cs](../../src-modern/PerkNativeFeedback.cs), [NativeVfxSafeFrame.cs](../../src-modern/NativeVfxSafeFrame.cs) | Inactive cosmetic clone sanitation and bounded deferred readiness/retry. |
| [VfxPool.cs](../../src-modern/VfxPool.cs) / VfxPool, VfxPoolBaseline, VfxPoolLease | Leases/cache/native baseline restore and lifetime cleanup. |
| [PerkVisualService.cs](../../src-modern/PerkVisualService.cs), [PerkFeedbackService.cs](../../src-modern/PerkFeedbackService.cs), [PerkAudioService.cs](../../src-modern/PerkAudioService.cs) | Shared semantic dispatch and isolated vanilla-clip playback. |
| [PhaseAVfx.cs](../../src-modern/PhaseAVfx.cs) / MasteryVfxMaterial, MasteryOwnedVfxMaterials | Native donor material copies and owned-material disposal; mixed combat/framework file. |
| [NativeSnowVisualAssets.cs](../../src-modern/NativeSnowVisualAssets.cs), [NativeStormWeatherVisual.cs](../../src-modern/NativeStormWeatherVisual.cs), [NativeWindSwirl.cs](../../src-modern/NativeWindSwirl.cs) | Domain consumer compositions using native snow/ice/wind resources; not new general-purpose loaders. |
| [PerkUiIconService.cs](../../src-modern/PerkUiIconService.cs) | Reuse actual native skill-row sprites/fallback; framework does not certify each icon. |
| [VfxAuditionService.cs](../../src-modern/VfxAuditionService.cs), [RuntimeAssetAuditService.cs](../../src-modern/RuntimeAssetAuditService.cs), [PerkDebugService.cs](../../src-modern/PerkDebugService.cs) | Discovery/isolated audition/debug entry points; previews do not prove natural mechanic triggers. |

No new loading/resolution/pooling subsystem inside a perk while shared infrastructure supports the requirement. A necessary shared runtime change requires a scoped task, affected-owner review and root integration:
```
SHARED SYSTEM CHANGE:
- system/file:
- reason:
- affected domains:
- regression required:
```
Documentation integration alone changes no runtime system and requires no gameplay fix.

### Source limits verified in 1.4.123

- MasteryPlugin.Awake calls MakeAllAssetsLoadable only on non-batch client before native first load; indexing is not eager loading every bundle.
- NativeSoftVisualAssets returns null on batch/unready scene/pending/absent/error; readiness cannot be inferred from null alone. Prefab short-name index keeps the first matching path; use exact path for ambiguity.
- NativeVfxSafeFrame: pending64, preparation12/frame, retry0.1s, timeout1.5s; scene/local-player loss clears pending work. These are infrastructure limits, not design performance acceptance.
- VfxPool:48 active,12 cached per prefab, lease0.15–5s; no overall cached-prefab-count cap established. Deferred lease readiness can abort after2s. Longer ceremonies/loops need an explicit supported lifecycle.
- PerkNativeFeedback accepts fx_/vfx_ cosmetic prefab names; sanitized clones remove gameplay/network/audio/collision components, disable animation events/root motion; particle cap256 per system, light range≤10/intensity≤3/shadows off. Later tuning still needs its own budget.
- PerkAudioService cap32 active and perk+prefab throttling; copies vanilla AudioSource/ZSFX settings into owned one-shot sources. Pitch0.1–3, delay0–2s, duration0.05–8s are current code clamps, not recommended design defaults.
- VfxLayer lists multiple anchor enum values, but ResolveAnchor does not implement every bone/projectile follow mode. Delay currently extends lease calculation; SpawnResolved has no general delay scheduler. Do not claim arbitrary delayed/following effects are supported.

## I. Gameplay/network authority

Server/owner decides authoritative gameplay under the mechanic contract; clients render/audio. Replicate meaningful gameplay state/event, not individual particles/audio when it already suffices.
Define which observer renders once. Test local prediction, RPC echo, generated-hit and owner/server double-processing duplicates. Cosmetic success/failure must never change damage, costs, cooldowns, XP, transactions or authoritative state.

## J. Headless safety

Use appropriate Application.isBatchMode/headless and scene/local-player guards. Dedicated servers cannot assume renderers, audio devices or client-loaded soft assets. No presentation exception may interrupt gameplay.
Classify client-only asset tests separately; headless missing/null is not a client visual defect. Preserve raw self-test result and explain applicability. In particular fx_land client appearance/audio remains separate from prior headless failure.

## K. Async / SoftRef lifecycle

For each asset define preload need; scene readiness; pending state; retry/backoff; bounded timeout; approved fallback or safe omitted cue; first-use and late-load behavior; cancellation; scene exit/shutdown release.
Not loaded yet is not necessarily missing. Avoid blocking waits/synchronous bundle loading in gameplay. A late cosmetic callback must not replay expired/wrong-target actions. Existing retry infrastructure does not guarantee every consumer automatically retries.

## L. Performance budgets

Task plan must give explicit budgets or mark them pending: spawn frequency, active/concurrent instances, per-system/aggregate particle count, light/renderer/material allocations, cached resources, lease duration and cleanup. Include sound concurrency/rate and remote-player clutter.
Prefer pooling/baseline restore; own and dispose material clones; avoid repeated scene/FindObjectsOfType scans and Instantiate/Destroy per rapid hit. Rare milestone/Gold cues and high-frequency perks need different budgets. Source caps are safeguards, not measured FPS proof; performance acceptance requires authorized observation/profiling.

## M. Quality and acceptance

Use one readable primary cue and limited related secondary feedback. Check vanilla style, visual hierarchy, brightness, screen obstruction, particle density, repetitive noise, multiplayer clutter, large targets and close/first-person camera where relevant.

Record separate static/asset-loading and LIVE visual/audio results. Presentation acceptance dimensions: resolved identity/type; visible/audible; correct natural trigger/timing; scale/position/orientation/attachment; duplicate prevention; owner/remote and multiplayer visibility; headless; async cold first use; cleanup; spam/concurrency/performance; VFX/SFX off/on independently. Manual player observation is valid, combined with logs/state where applicable. Audition proves only the isolated selected cue.

Complete the task template's PRESENTATION / VANILLA ASSET PLAN before presentation implementation. N/A is valid for non-presentation tasks. Pending user choices are decisions, not permission to implement substitutions. Build/deploy/runtime tests require their own active authorization.

