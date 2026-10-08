# Reference Index — Valheim Mastery

Curated **2026-10-04** for **Mastery 1.4.123**, installed **Valheim 1.0.16** / **Unity 6000.0.75f1**. This library is selective applied guidance, not current implementation authority, a generic tutorial syllabus or a gameplay test result.

## Selective consultation

Start with the active task, relevant canonical docs and source. Open a reference only for a concrete question it helps answer. Do not automatically ingest this index's entire library or require every task to read all files.

| Task question | Start here |
|---|---|
| Lifecycle, native composition, loading, performance or patches | Relevant topic in [ENGINE_IMPLEMENTATION_REFERENCE](ENGINE_IMPLEMENTATION_REFERENCE.md) |
| Whether a proposed mechanic fits Valheim | [VALHEIM_DESIGN_PRINCIPLES](VALHEIM_DESIGN_PRINCIPLES.md) |
| Resource loop, crafting, pressure, progression or co-op tradeoff | Relevant lens in [SURVIVAL_GAME_DESIGN_REFERENCE](SURVIVAL_GAME_DESIGN_REFERENCE.md) |
| What Mastery currently implements/owns | Current canonical domain source/docs; external references cannot answer this |

External ideation chats may use the two design summaries if their contents are attached/pasted. Repository-relative links alone do not give those chats repository access. Preserve the ideation export's dated, explicit-refresh-only snapshot rules.

## Priority rule

Lower number wins for an applicable factual conflict. Policy/user decisions still govern authorized work; an external API tutorial cannot override Mastery's asset policy.

1. **Current Valheim 1.0.16 runtime/assemblies/source inspection** — actual signatures, execution, ownership and save behavior.
2. **Current Mastery canonical source/docs** — project behavior, policy, compile gates, architecture and ownership. Source resolves stale implementation prose.
3. **Version-matched Unity 6 documentation** — here 6000.0; inspect installed assemblies for patch-specific availability.
4. **BepInEx 5 documentation** — use the installed Mono plugin model.
5. **Stable Harmony 2.x documentation** — general guidance; inspect actual loaded Harmony/HarmonyX behavior.
6. **Microsoft C#/.NET documentation** — check Mono/netstandard2.1 and API compatibility.
7. **Established game-development/design material** — original papers, developer commentary/talks; context and interpretation, not engine truth.
8. **Historical/general articles** — inspiration only; label age and limitations.

Compare applicable evidence, not merely a source's brand. A design talk cannot overrule a native ownership check. A Unity example cannot prove a Valheim prefab exists. Source presence is not live verification; record the distinction.

## Highest-priority local evidence

| Source | Category | Authority | Useful for / when to consult | Type / evidence limits |
|---|---|---|---|---|
| Installed client/server assembly_valheim, assembly_utils, UnityEngine.CoreModule, SoftReferenceableAssets and actual runtime | Native APIs/version | 1 | Inspect the affected class, method, IL and loaded version before patch/load/authority changes. | Native evidence. This task inspected selected metadata/Version IL in both variants; not an exhaustive native audit. |
| [Recorded runtime log](../../validation/unity-smoke-20261004/runtimes/server/BepInEx/LogOutput.log) and [runtime baseline](../architecture/BUILD_RUNTIME_BASELINE.md) | Runtime provenance | 1 for recorded runtime facts; 2 for canonical summary | Resolve version/package drift before choosing documentation. | Existing evidence, not rerun. New source inspection also found 1.0.16/network 40 in both native variants. |
| [SCOPE_MAP](../architecture/SCOPE_MAP.md), [SHARED_SYSTEMS](../architecture/SHARED_SYSTEMS.md), relevant domain docs and src-modern | Current project | 2 | Owner boundaries, reusable services, actual implementation and build gates. | Canonical source/docs; catalog and historical prose are not execution proof. |
| [Vanilla asset workflow](../architecture/VANILLA_ASSET_WORKFLOW.md) / [catalog](../architecture/VANILLA_ASSET_CATALOG.md) | Presentation policy/evidence | 2 | Discover/reuse native assets, stage evidence, review safe composition. | Canonical policy and scoped asset evidence; not an exhaustive whitelist or live-pass claim. |

## Curated external sources

All linked originals were opened or their publisher-hosted abstracts/relevant text inspected on the curation date. Grouped links point to related API pages, not a demand to read a whole documentation tree. Stable BepInEx pages identify docs v5.4.21; installed runtime is 5.4.23.5. Harmony links deliberately use the v2 branch.

| ID / source | Category | Authority | Useful for / when to consult | Type / review scope |
|---|---|---|---|---|
| U01: [Unity Awake / OnEnable / Start](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Awake.html) / [OnEnable](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnEnable.html) / [Start](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.Start.html) | Lifecycle | 3 | Initialization and pooled activation; consult when attaching components or depending on another singleton. | Official Unity 6.0 API |
| U02: [Unity execution order](https://docs.unity3d.com/6000.0/Documentation/Manual/execution-order.html) | Lifecycle/order | 3 | Callback phases; consult for frame/physics/animation timing. Read the linked callback-specific API too. | Official Unity 6.0 manual |
| U03: [Object.Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html) | Runtime clones | 3 | Clone hierarchy/activation semantics; consult before cosmetic or network-object spawning. | Official Unity 6.0 API |
| U04: [Transform.SetParent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Transform.SetParent.html) | Spatial composition | 3 | World/local placement; consult for attachments, rescaling or teleport visual drift. | Official Unity 6.0 API |
| U05: [Particle simulationSpace](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule-simulationSpace.html) | Particles | 3 | Local/world/custom simulation; consult for following effects and particle resets. | Official Unity 6.0 API |
| U06: [TrailRenderer.Clear / LineRenderer space / Light](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TrailRenderer.Clear.html) / [LineRenderer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LineRenderer-useWorldSpace.html) / [Light](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light.html) | Runtime presentation | 3 | Trail reuse, line coordinates, light controls; consult for layered vanilla compositions. | Official Unity 6.0 API |
| U07: [Renderer.material](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-material.html) | Material ownership | 3 | Per-renderer material instantiation/cleanup; consult before tinting or replacing materials. | Official Unity 6.0 API |
| U08: [AudioSource.spatialBlend](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spatialBlend.html) | Sound | 3 | 2D/3D spatial mix; consult for owner-only versus nearby-player cues. | Official Unity 6.0 API |
| U09: [ObjectPool<T>](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Pool.ObjectPool_1.html) | Pooling | 3 | Acquire/release/reset principles; consult before extending Mastery's existing pool, not to replace it by default. | Official Unity 6.0 API |
| U10: [Managed-memory optimization](https://docs.unity3d.com/6000.0/Documentation/Manual/performance-optimizing-code-managed-memory.html) | Allocation/GC | 3 | Allocation pressure; consult when measurements identify repeated temporary allocations. | Official Unity 6.0 manual |
| U11: [Object.Destroy / OnDestroy](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Destroy.html) / [OnDestroy](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnDestroy.html) | Lifetime | 3 | Deferred destruction and callback limits; consult for pooling, scene exit and owned resources. | Official Unity 6.0 API |
| U12: [Dedicated Server optimizations](https://docs.unity3d.com/6000.0/Documentation/Manual/dedicated-server-optimizations.html) | Headless | 3 | Dedicated build differences; consult alongside actual Valheim batch/server evidence, not as proof of its build options. | Official Unity 6.0 manual |
| U13: [Async programming / continuation](https://docs.unity3d.com/6000.0/Documentation/Manual/async-await-support.html) / [Continuation](https://docs.unity3d.com/6000.0/Documentation/Manual/async-awaitable-continuations.html) | Async/main thread | 3 | Thread resumption and API restrictions; consult before adding tasks/callbacks to asset or state work. | Official Unity 6.0 manual |
| B01: [BepInEx basic plugin](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/2_plugin_start.html) | Plugin lifecycle | 4 | BaseUnityPlugin/config/logger entry points; consult on initialization/shutdown changes. | Official BepInEx 5.4.21 docs |
| B02: [BepInEx logging](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/3_logging.html) | Diagnostics | 4 | Logger/ManualLogSource; consult for actionable plugin diagnostics. | Official BepInEx 5.4.21 docs |
| B03: [BepInEx configuration](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/4_configuration.html) | Configuration | 4 | Config.Bind/ConfigEntry; consult for new options, reload or persistence behavior. | Official BepInEx 5.4.21 docs |
| H01: [Harmony Prefix / Postfix](https://harmony.pardeike.net/v2/articles/patching-prefix.html) / [Postfix](https://harmony.pardeike.net/v2/articles/patching-postfix.html) | Patching | 5 | Arguments/results/skip semantics; consult when selecting a native interception point. | Official stable Harmony 2.x docs |
| H02: [Harmony Transpiler](https://harmony.pardeike.net/v2/articles/patching-transpiler.html) | IL patches | 5 | Small composable IL edits; consult only when simpler interception cannot express the change. | Official stable Harmony 2.x docs |
| H03: [Harmony Finalizer](https://harmony.pardeike.net/v2/articles/patching-finalizer.html) | Exception cleanup | 5 | Failure-path cleanup/exception preservation; consult for context restoration after exceptions. | Official stable Harmony 2.x docs |
| H04: [Harmony priorities / edge cases](https://harmony.pardeike.net/v2/articles/priorities.html) / [Edge cases](https://harmony.pardeike.net/v2/articles/patching-edgecases.html) | Patch compatibility | 5 | Ordering, inlining/generics/static initialization; consult for conflicts or missing hook execution. | Official stable Harmony 2.x docs |
| C01: [Microsoft C# allocation/copy performance](https://learn.microsoft.com/en-us/dotnet/csharp/advanced-topics/performance/) | C# hot paths | 6 | Measure first; ref/value semantics. Check APIs against Mono/netstandard2.1 before adopting examples. | Official Microsoft documentation |
| C02: [Microsoft event subscription](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/events/how-to-subscribe-to-and-unsubscribe-from-events) | Events/lifetime | 6 | Subscriber references/unsubscription; consult for static publishers and scene-bound consumers. | Official Microsoft documentation |
| D01: [Iron Gate Valheim FAQ](https://www.valheimgame.com/faq/) | Valheim identity | 7 | Solo/co-op, PvE, exploration, food and biome progression; consult when checking whether a proposal fits the base game. | Official developer description |
| D02: [Iron Gate Valheim overview](https://www.valheimgame.com/) | Valheim identity | 7 | Adventure/build/craft/cooperate framing; consult for player-experience goals, not implementation. | Official developer description |
| D03: [Valheim: Vikings, Roadmaps & Buying a Horse (Jonathan Smårs, GDC 2024)](https://www.gdcvault.com/play/1034494/Independent-Games-Summit-Valheim-Vikings) | Development/expectations | 7 | Development change/expectations context. Watch the talk for detailed claims; this library does not claim its full contents were reviewed. | Developer commentary; session abstract reviewed |
| D04: [Astroneer: Mining Your Own Design (Biddlecom/O'Rear, 2020)](https://media.gdcvault.com/gdcsummer2020/presentations/Biddlecom-Aaron-MiningYourOwnDesign.pdf) / [Session](https://gdcvault.com/play/1026846/Mining-Your-Own-Design-Crafting) | Crafting/systems | 7 | Incentives versus intent, resource sinks and integrated redesign; consult for Workshop/processing/resource expansions. | Developer presentation; relevant slides reviewed |
| D05: [Pacific Drive gameplay (Blake Dove, Ironwood, 2023)](https://blog.playstation.com/2023/02/09/ironwood-studios-returns-with-a-first-look-at-the-gameplay-of-pacific-drive/) | Expedition risk/reward | 7 | Preparation, maintenance and return loop; consult for cargo/travel/resource-risk ideas. Historical design example, not current release specification. | Developer-authored commentary |
| D06: [The Long Dark: Survival Mode](https://www.thelongdark.com/survival-mode/) | Survival tradeoffs | 7 | Time/energy/resource decisions; consult for pressure and informative feedback. Some marketing details are dated; do not import its rules into Valheim. | Official developer description |
| D07: [The Long Dark: A Long Dark Road (van Lierop, GDC 2018)](https://www.gdcvault.com/play/1024896/A-Long-Dark-Road-Blending) | Sandbox expansion | 7 | Authored content versus sandbox expectations. Full talk required before attributing detailed conclusions. | Developer commentary; session abstract reviewed |
| D08: [MDA (Hunicke, LeBlanc, Zubek, 2004)](https://users.cs.northwestern.edu/~hunicke/MDA.pdf) | Systems/design framework | 7 | Mechanics → emergent dynamics → player experience; consult for a feature that seems correct alone but changes the larger loop. | Original established design paper; relevant text reviewed |
| D09: [Don't Juice It or Lose It (Folmer Kelly, GDC Europe 2014)](https://www.gdcvault.com/play/1020861/don-t-juice-it-or) | Feedback/game feel | 7 | Presentation coherence; consult for excessive/disconnected polish. No full-talk claim. | General design commentary; session abstract reviewed |
| D10: [The Long Dark: Tireless Menace (2016 archive)](https://www.thelongdark.com/time-capsule/tireless-menace/) | Feedback/UI | 8 | Reducing UI interruption while keeping survival information visible; consult for UX burden, not current TLD mechanics. | Historical developer release commentary |

## Limits and use in reviews

Citations support narrow statements; each applied recommendation remains a project interpretation. No official universal Iron Gate balance doctrine is inferred. Original sources may change, and historically worded marketing material may be stale. Recheck the relevant original when its exact API wording or design claim matters.

No automatic ingestion, scheduled refresh or build dependency is established. These references neither authorize gameplay changes/runtime tests nor complete the post-migration baseline. Use the smallest relevant evidence set and stop research once the task's concrete uncertainty is resolved.
