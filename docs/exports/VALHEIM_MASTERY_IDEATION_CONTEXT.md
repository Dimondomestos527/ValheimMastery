# Valheim Mastery — Portable Ideation Context

**Snapshot version:** Mastery 1.4.123  
**Snapshot date:** 2026-10-04  
**Status:** ONE-TIME IDEATION CONTEXT SNAPSHOT

This file is NOT automatically synchronized. Future code changes do NOT update it automatically. It must only be refreshed after an explicit user request, for example: “Refresh the Valheim Mastery ideation context report from current canonical source.” Repository canonical docs/source override this snapshot if they conflict. No automatic maintenance workflow is established.

This is portable product and architecture context for a separate conversation without repository access. It summarizes current capabilities and boundaries, not a complete perk specification, release promise, balance contract or gameplay certification. Implementation facts below were checked against current canonical documentation and targeted 1.4.123 source. Historical descriptions are not treated as current implementation authority.

---

# INSTRUCTIONS FOR IDEATION ASSISTANT

**BEGIN PORTABLE INSTRUCTIONS**

Treat this report as dated architectural context. Your job is to help the user design good gameplay, with clear decisions and realistically reusable foundations. Think creatively within the native-resource, multiplayer and persistence boundaries described here.

Optional supporting material: you may use VALHEIM_DESIGN_PRINCIPLES and SURVIVAL_GAME_DESIGN_REFERENCE when their contents are separately attached or pasted into this conversation. They provide cited design context and project interpretations, not verified current implementation facts or an official Valheim doctrine. Repository links alone do not provide access. Current Valheim/Mastery canonical source/docs override generic external guidance; the dated snapshot and explicit-refresh-only rules still apply.

Separate known project facts from design suggestions. Never describe a proposed mechanic as implemented, approved or tested merely because a similar service exists. Source implementation is different from successful live gameplay. A catalog entry is different from an execution path.

Preserve explicit user decisions already stated in your conversation, including selected effects, rejected alternatives and desired persistence semantics. Challenge weak triggers, unclear tradeoffs, resource duplication and unreadable feedback. Offer stronger alternatives without quietly changing approved behavior.

Describe desired behavior before suggesting technology. For networked or persistent ideas, state who acts, who should observe the result and what survives logout, portals, death or restart. Do not invent RPC names, serialization layouts or claims of universal server authority.

For visuals and audio, describe purpose and vanilla presentation character. Do not invent verified prefab or clip names. New runtime compositions are possible; imported custom art/audio/animations/AssetBundles are prohibited. Preserve any exact vanilla asset selected by the user.

When uncertain, label the question for Codex feasibility review. Technically complicated does not mean impossible. Do not reject an idea solely because today's helper lacks a ready-made method. Distinguish a reusable existing capability, an extension requiring investigation and a hard policy conflict.

Use the feasibility format in section 11 for serious proposals. Finalize gameplay semantics and open decisions before handing an idea to its correct Codex owner. This external chat does not authorize repository changes or runtime testing.

**END PORTABLE INSTRUCTIONS**

---

## Reading implementation status correctly

In this report, **IMPLEMENTED** means an execution path exists in reviewed current source; it does not mean every branch, build variant or multiplayer scenario passed live testing. **PARTIALLY IMPLEMENTED** means named components exist while integration, coverage or required behaviors remain incomplete. **CATALOG ONLY** means names/descriptions/slots exist without confirmed dedicated gameplay execution in reviewed coverage.

**DESIGN PENDING** identifies a required unresolved behavior decision. **LIVE TEST PENDING** identifies implementation whose current player-visible or multiplayer acceptance is unproven. **DEFERRED** identifies deliberately postponed work. **UNKNOWN / NEEDS SOURCE REVIEW** is used where evidence does not establish an answer. A domain can legitimately combine several statuses.

Current normal Unity/Harmony registration evidence supports startup integration. It does not certify damage formulas, summon travel, cargo preservation, transactions, visual quality or every skill. Complete post-migration gameplay regression remains deferred/incomplete. Do not convert that gap into either “everything works” or “everything is broken.”

## 1. Short project description

Valheim Mastery extends Valheim's skill progression with skill-dependent passives and milestone mechanics. Its current catalog covers 24 skills with slots at levels **35, 70 and 100**. Those 72 slots describe progression structure; they are not proof that all 72 gameplay effects are implemented.

The project touches combat weapons, blocking/dodging, elemental and blood magic, movement, gathering and peaceful professions. Level-dependent bonuses belong to the progression layer. Milestone perks belong to their gameplay domain and can introduce attack interactions, charged actions, summons, zones, resource workflows or profession effects. Reaching level 100 does not automatically turn every ordinary passive into a Gold action.

Mastery has separate client/server builds against the appropriate native game references. It integrates through BepInEx/Harmony, Valheim entities and skill/inventory/save mechanisms. Clients render presentation and participate in owner-controlled actions; servers and object owners validate authoritative operations where the mechanic requires them. The entire game is not replaced with a wholly server-simulated combat model.

The philosophy evident in current architecture is to extend recognizable Valheim play, reuse native systems, separate effect semantics from shared infrastructure and keep persistent rewards/resources consistent. The native world, weapons, crafting, creatures, animations and UI remain the foundation. A distinctive mechanic can be ambitious while still using vanilla resources.

Gold Core/Pantheon provides shared high-level progression, Favor, acknowledgement, exhaustion and transaction machinery. Actual level-100 action effects are narrower than the catalog suggests. Treat Gold infrastructure as a foundation to investigate for new designs, not a ready-made universal API supporting every imagined divine action.

## 2. Gameplay domain map

| Gameplay owner | What belongs here | Important boundary |
|---|---|---|
| **XP & Skill Bonuses** | XP modifiers, world/boss/tier and first-time progression, native/custom skill handling, death penalties/floors, continuous skill-level passives. | Continuous magic regeneration or bare-hand scaling stays here; it is not a magic/combat milestone merely because it affects combat. |
| **Combat 35/70** | Non-magic weapon milestones, blocking, dodge, melee/projectile interactions, charges, echoes and related target effects. | Physical shields/blocking belong here; blood-magic shield renewal does not. |
| **Magic 35/70** | Elemental/blood milestone actions, casts, summons, roster/follow/recall/desummon, portals, cargo and zones. | A technically complex summon transport or cargo feature remains part of magic gameplay. |
| **Utility 35/70** | Movement, fishing/riding, farming, woodcutting, mining, cooking/crafting, processing, Workshop/network storage and Potential Forge. | Storage complexity does not turn Workshop into a separate gameplay domain. |
| **Gold Core / Pantheon** | Shared level-100 acknowledgement, Favor/exhaustion, ledger, receipts/idempotency, transaction protocol, shared Gold persistence and common Pantheon behavior. | It owns common protocol semantics, not every skill-specific level-100 action. |
| **Combat 100** | Actual non-magic level-100 action effects when established/implemented. | Combat 35/70 and generic level-100 scaling do not fill these slots. |
| **Magic 100** | Actual magic level-100 action effects when established/implemented. | Existing 35/70 summons/zones and continuously scaled regeneration are separate. |
| **Utility 100** | Confirmed mining metal-weight behavior and divine crafting/forge action semantics; other utility slots need inventory. | Gold ledger/receipt machinery remains Gold Core; action behavior remains Utility 100. |
| **Core UX & Tutorials** | Shared UI/layout, milestone/tutorial presentation, tooltip/icon framework, localization infrastructure and input/focus. | Exact perk VFX/SFX, semantic timing and mechanic-specific HUD values/content belong to the mechanic owner. |

Infrastructure is technical stewardship; Regression/Integration coordinates evidence and cross-domain acceptance. Neither is an extra gameplay domain.

Generated-hit provenance, owner authority, save order, pooling and asset loading are shared technical concerns. They can require several owners to review a change, but they do not create a new owner for a single mechanic. Route an idea according to what the player actually does, not which source file looks most complicated.

## 3. Current architectural capabilities and reuse opportunities

The following inventory describes capabilities present in source. Reuse is plausible, not automatic certification of a new mechanic.

| Capability | What the project can already do | What a new mechanic may reuse or must clarify |
|---|---|---|
| **XP and progression hooks** | Observe combat/gathering/peaceful actions and modify native skill gains using world, target/item tier and action context. First-time and catch-up concepts have existing paths. | Reuse progression context rather than award XP independently. Define the recipient and qualifying action; avoid rewards for every secondary generated hit. |
| **Raw skill eligibility and passive scaling** | Read actual learned skill level, distinguish it from temporary status-effect bonuses, gate milestones and apply level-dependent passives. | Specify whether eligibility means learned level or temporary effective strength. Bare hands and fist weapons are intentionally distinguishable. |
| **Milestone crossing** | Detect movement from below a threshold to that threshold or above; present crossings independently from durable first-unlock flags. | Reuse threshold semantics, but distinguish first reward/tutorial from repeated crossing presentation after skill loss/regain. A direct debug level assignment is not equivalent evidence. |
| **Combat hooks and events** | Patch native attack, damage, block/parry, death and projectile paths. Normalized hit/attack events carry relevant actor, target and hit information. | New attacks or reactions can reuse hooks. Event-bus adoption is partial: declared event types are not proof that every native action publishes through one universal pipeline. |
| **Generated-hit controls** | Associate hits with source perk/skill/player, generation depth, proc/reflect/execution restrictions, XP multiplier and already-applied perk tracking. Dedicated variants/tags also identify some generated attacks. | Define permitted chains and once-only rewards. In-process context is not a universally serialized remote envelope; the selected path must preserve provenance where needed. |
| **Cooldowns and temporary state** | Keep object/player/target-bound session state. A separate cooldown service stores UTC ready timestamps in player custom data for its consumers. | Choose deliberate logout behavior. Not every timer uses the persistent service; some states are transient, and Gold exhaustion uses different online-time semantics. |
| **RPC/settings integration** | Register shared/mechanic messages, exchange settings and enforce configured build/manifest compatibility checks. | Reuse established communication patterns. Do not assume settings synchronization proves universal server validation of damage, XP or every action. |
| **Owner authority** | Bind certain fresh owner-reported skill snapshots to ready peers, character ownership and player identity. | Existing coverage is scoped, not an arbitrary permission system. A new skill/action may need an owner-approved extension and meaningful validation. |
| **Player persistence** | Store custom progression values, milestone/tutorial-related flags, selected cooldown timestamps and transaction receipts alongside native character state. | State what must survive character save/logout/rejoin. Writing custom data alone is not an immediate durable save or a complete server-backed economy. |
| **World/ZDO persistence** | Record networked object state, processing attribution and summon/cargo identity through native object records; Gold also has a world-specific ledger. | World identity and save boundaries matter. Object fields, character data and server files are different stores, not one automatic transaction. |
| **Resource transactions** | Reserve/debit multiple storage participants, snapshot before mutation, validate exact resource removal and restore on failure. Durable request/recovery components exist. | A multi-container cost can build on Workshop patterns. Successful output confirmation and commit must be linked; uncertain rollback cannot silently unlock stock. |
| **Workshop and processing** | Connect eligible storage to crafting/building workflows and preserve input-author/output attribution through processing queues. | Design around actor/access/station/range and concurrent-player rules. A collector is not automatically the player entitled to a producer's skill bonus or XP. |
| **Summons, cargo and travel** | Create native-based summons, manage roster/follow/recall/desummon and handle portal transfer with server-side identity/state copying. Carrier cargo uses persistent linked native container state. | Reuse summon lifecycle, not arbitrary visual cloning. Portal movement can replace a carrier identity and rebind its cargo; open container and owner-transfer conditions need defined outcomes. |
| **Target effects** | Track sourced target effects with stacks, strength and expiry; consumers can query/remove active effects. | Useful for marks, pressure or timed debuffs. This target-effect state is transient; persistence/replication must be separately designed. |
| **UI, HUD and tutorials** | Provide shared skill/perk windows, proc indicators, native icon reuse, tooltips/localization, milestone presentation and raven tutorial scheduling. | Reuse the framework; define exact values, ownership and meaning. A new display should not invent conflicting one-time reward or repeated-crossing rules. |
| **VFX, audio and assets** | Resolve registry/native soft assets, compose tuned recipes, sanitize cosmetic clones, restore pooled instances and play copied vanilla audio clips/settings. | Reuse presentation infrastructure. Assets may be pending/unloaded, cues may be budget-limited and exact native resources still require discovery. |
| **Gold/Favor transactions** | Maintain a world-specific durable ledger, reserve/commit grants, correlate client item changes with character receipts and reconcile acknowledgements/retries. | Reuse the protocol concepts with Gold Core review. The current action contract is specifically crafting/upgrade-shaped; arbitrary new actions require actual shared integration. |
| **Diagnostics and audition** | Inspect state, assets and registrations; preview selected effects; provide debug skill/proc controls under their gates. | Diagnostics support feasibility/testing. Forced chance, direct skill setting and isolated previews do not prove a natural trigger, correct XP or multiplayer behavior. |

A useful idea identifies which existing capability seems relevant and what new behavior remains to be added. It should not treat this table as a guarantee that several services can be combined without lifecycle or authority work.

## 4. Game and engine constraints: real boundaries versus implementation work

The project targets a Unity/Valheim managed runtime with BepInEx/Harmony integration and a .NET Standard plugin target. Recorded local runtime evidence identifies Unity 6000.0.75 and Harmony 2.9.0. The compile HarmonyX package has a different version; package names alone do not identify the loaded runtime. This snapshot does not claim compatibility with every future Valheim/Unity update.

| Topic | Known constraint or current boundary | Implementation detail Codex can handle |
|---|---|---|
| **Harmony patching** | Targets, signatures, transpilers and ordering must match the actual native game/build. A standalone console test is not the full Unity runtime. | Select/verify hooks, preserve vanilla execution and coordinate overlapping patches. Nontrivial patching alone is not an impossibility proof. |
| **Headless servers** | Rendering/audio and client-only asset loading cannot be assumed on a dedicated batch server. | Keep presentation client-side, guard readiness and classify client-only tests appropriately without changing authoritative gameplay. |
| **Authority** | Native ownership and project validation are mechanic-specific. Local display state is not automatically authoritative. | Implement appropriate server/owner requests and validation; the designer specifies who is allowed to act and the resulting behavior. |
| **Native skills** | Learned level, modified effective values, XP accumulator and saved unlock flags are different concepts. | Integrate natural gains and milestone callbacks. A new skill or progression model needs registration/save/UI review, not a claim that it is inherently impossible. |
| **World/player saves** | Character state, object/ZDO state and server ledger records do not become atomically saved just because they refer to one player. | Add explicit ordering, receipts and recovery around persistent actions. Specify which game outcomes must remain consistent. |
| **Inventory/items** | Costs and results need stable identity, access validation and exactly-once semantics; item metadata can affect saving and tooltips. | Reuse native item/container operations and transaction patterns. Never assume cloning an object establishes a valid economic transaction. |
| **Projectiles/hits** | Attacker identity, native ownership and generated-hit provenance must survive the relevant execution path. | Add delivery/collision/effect integration and safeguards. Do not presume every generated hit automatically grants normal XP or may reproc itself. |
| **Prefabs/assets** | A native asset can exist in a manifest without being loaded or registered in the scene. Cosmetic clones must not retain damaging/network controllers. | Discover exact resources, load asynchronously and extract safe visual/audio content. Absence from one registry is not proof that no native resource exists. |
| **Animation/input** | Imported custom animation assets are prohibited. Existing action/cast state, native animation and cancel behavior must remain coherent. | Reuse native animations/triggers and runtime controllers; design cancellation, weapon swaps and control restoration rather than mandate a new imported animation. |
| **Multiplayer** | Owner/remote, two-player concurrency, delayed/repeated requests and visual duplicates must be considered. Registration equality is not multiplayer acceptance. | Extend shared routing/validation and test relevant topologies. The ideation chat need not choose exact messages. |
| **Restart/reconnect/portals** | Session components do not automatically survive these transitions; even existing summon/cargo source is not complete live proof. | Persist/reconstruct approved state, transfer ownership and reconcile receipts. Define survival semantics before choosing storage technology. |

Current presentation helpers have finite limits: the short-lived VFX pool allows 48 active leases and 12 cached instances per prefab; its usual leases are capped at five seconds. Shared audio permits 32 active sounds. These are existing implementation safeguards, not fixed game-engine limits or universal recommended budgets.

Similarly, an anchor enum is not proof of arbitrary bone/projectile following, and a recipe Delay field currently is not a general delayed-spawn scheduler. Codex can assess a safe extension. A long ceremony or following effect should not be rejected simply because a short-lived helper needs a different lifecycle.

## 5. Prominent vanilla-only asset policy

**Valheim Mastery does NOT use custom imported visual/audio assets as its project presentation policy.** New design must respect that policy even if importing art would be convenient.

Allowed resources include existing Valheim prefabs, VFX/SFX, meshes, materials/shaders/textures, icons/sprites and audio clips; existing Unity-native runtime components and primitives; and runtime composition/tuning of those resources. No custom imported textures, meshes, audio, animations, external AssetBundles or other external art files may be introduced. Composition is not a loophole for shipping newly authored replacement art.

New-looking presentation is still possible. It can combine several verified vanilla effects, reuse a vanilla mesh with another appropriate vanilla material, layer native particles with a Light or TrailRenderer, or safely scale/rotate/reposition a native effect. Native audio layers can communicate a distinctive event without a new recording.

Each composition must preserve resource provenance and avoid globally changing vanilla objects or shared materials. Use owned clones, native material copies or instance property overrides; clean up owned resources and restore pooled state. Visual/audio success cannot alter damage, costs or transaction outcomes.

Ideation should therefore begin with **visual intent**, not guessed prefab names. “A brief native lightning-impact cue at the struck target” is useful. “Use prefab VM_DivineLightningExplosion” is not a verified asset fact merely because the name sounds plausible.

The curated project catalog contains investigated exact resources such as fx_land, fx_perfectdodge, vfx_HitSparks, vfx_perfectblock and native snow/ice donors. Their catalog stage is source/path verification, not blanket proof of loading or quality. It is a small reusable knowledge base, not a whitelist of all possible vanilla resources. Codex can investigate other native candidates.

If the user specifies an exact vanilla effect or sound, preserve that preference. Loading trouble does not authorize silently substituting a different effect. Meaningful choices among realistic alternatives require the user's decision; previously approved choices should not trigger repeated approval requests.

## 6. How to describe useful VFX and SFX ideas

For VFX, state what the cue teaches: readiness, successful hit, mark, charge progress, completed charge, failure, active buff, area boundary, summon birth or Gold event. Then describe approximate size/intensity, lifetime, trigger/timing, owner/target/world attachment and repetition. Explain whether it follows an actor or stays at the original point.

Consider readability at ordinary distance, on large targets and near the camera. Distinguish the primary cue from limited secondary decoration. A charged attack might need a buildup readable only by its owner and a brief release readable by nearby players; these are different intentions rather than one effect spawned everywhere.

Avoid unnecessary brightness, screen obstruction, excessive particles and multiplayer clutter. High-frequency procs need restrained presentation and a bounded repeat rate. Rare milestone ceremonies can have a different budget, but neither is exempt from cleanup and settings behavior.

For SFX, describe purpose, trigger, loudness relative to familiar vanilla sounds, rough native character, spatial/local behavior and repeat limits. Decide whether nearby players should hear it, whether it is a one-shot or sustained state and what stops it. Do not specify an exact clip unless it is actually known.

Useful wording includes: “a short metallic vanilla confirmation sound, quieter than an ordinary parry, heard only by the owner, with a repeat cooldown.” This gives Codex an audition target without inventing a clip.

A disappearing sound or effect must not leave controls locked or prevent an action. VFX and SFX settings need independent behavior. If feedback is essential to fair gameplay, define an appropriate alternative UI cue when optional effects are disabled.

Codex later discovers exact assets and tuning. Manual player observation is valid for visible/audible acceptance, supplemented by logs/state where needed. An isolated audition proves appearance/sound of that preview; it does not prove the mechanic triggers it once at the correct moment.

## 7. Network and persistence design rules

The external chat should state **desired gameplay semantics**, not prescribe storage fields or RPC methods. Separate actor, validator, affected target and observer.

For any multiplayer idea, answer: who may initiate it; who decides eligibility and cost; whether another player can interrupt or interact; what the owner sees; what remote players see; and what happens if a request arrives late or twice. Avoid a design where a purely local UI decision creates authoritative items or rewards.

For persistent state, specify survival across disconnect/reconnect, logout, portal travel, owner transfer, death, object destruction and graceful server restart. Crash recovery is a separate question for important economic state. A combat charge may deliberately disappear on logout; cargo or a committed item should not disappear because a visual object is reconstructed.

Timer semantics must be explicit. “Cooldown continues while offline” differs from “exhaustion decreases only while online.” Existing generic UTC cooldowns and Gold's online-time exhaustion illustrate that the project already uses both kinds. Choose the desired behavior instead of assuming one universal timer implementation.

Economic mechanics must define exactly-once effects, failed actions, cancellation and concurrent use. If an action spends resources, produces an item and awards Favor, explain whether all must succeed together, what is refunded on failure and what becomes visible while the result is uncertain. Do not permit an ambiguous retry to produce a second item.

Summon example: “The owner's carrier keeps its cargo through portal travel and reconnect, cannot copy its inventory by being recalled, and cannot be dismissed while a second player is actively using its container without a defined close/deny behavior.” This is useful semantics.

Avoid: “Store cargo in component X and send RPC Y.” That implementation may conflict with the existing linked native container and server-controlled transfer. Codex should choose the exact technical path after checking current source.

Also consider what persists **for whom**. A skill unlock is character-specific; a shared processing batch belongs to a world object; Gold's ledger is world-specific. Moving characters between worlds must not accidentally import a world-bound claim into an unrelated world. The designer need not solve serialization, but should state the intended cross-world rule.

## 8. Gameplay design rules without inventing a balance doctrine

The current architecture separates continuous passive scaling from milestone gameplay. Preserve that distinction: a modest continuous bonus can be appropriate as a passive, while a milestone can introduce a distinctive interaction when that fits the skill.

Treat 35 → 70 → 100 as increasing progression significance and an opportunity for greater impact or spectacle, not proof that every existing higher-level effect must numerically outdamage every lower-level effect. The repository does not establish a single universal balance formula for all perks. Do not replace approved costs, caps or tradeoffs with an invented doctrine.

Preserve recognizable Valheim play. Build around existing weapons, staffs, terrain, creatures, crafting and player decisions. A mechanic should explain what the player does differently and why that choice matters. More particle layers alone are not a meaningful gameplay progression.

Make triggers, costs, cooldowns and failure conditions understandable. Passive and active behavior both need clear boundaries. Determine whether an effect depends on primary/secondary attack, charge duration, target state, equipment, station, environmental context or learned skill.

Generated effects must obey explicit chain/proc rules. Allowing a reflection, echo or area hit to trigger another effect can be a deliberate design, but must not accidentally produce unbounded self-recursion or repeated XP. Define the interesting combo and its endpoint, rather than forbid every interaction or leave it unbounded.

Resource bonuses need an economic explanation. Identify the qualified action and recipient; avoid replaying rewards from item transfer, collection, repair loops or repeated building/destruction. Existing ledgers and anti-farming concepts support this concern, but they do not automatically protect a new reward source.

Use understandable multiplayer behavior and feedback. If one player's action changes another player's target, resources or movement, describe consent/access/friend-enemy rules and visibility. Boss immunity, large-target behavior and PvP are open design dimensions requiring explicit decisions, not blanket project rules invented by this snapshot.

## 9. Current implementation and incomplete areas by domain

### XP & Skill Bonuses — IMPLEMENTED; LIVE TEST PENDING

Current source includes native XP integration, context-dependent multipliers, world/boss progression mapping, gathering/peaceful profession hooks, first-time/catch-up concepts and skill-dependent passives. There is custom-data accumulation support for selected peaceful skills alongside native skill handling; this does not mean every skill runs through one universal custom store.

Existing differences such as bare hands versus fist weapons, learned versus modified level and first-time saved rewards matter to new designs. Full modifier combinations, recipient attribution, death/floors and exact final XP outcomes still need current live acceptance. Diagnostics do not currently provide universal recipient/final-value correlation.

### Combat 35/70 — IMPLEMENTED/MIXED; LIVE TEST PENDING

Source contains weapon-specific interactions: sword attack paths, knife/shadow actions, spear throw/hook behavior, bow drawing/target/projectile systems, club/hammer/mace interactions, blocking/reflection/rush and dodge mechanics. These are not empty slots available for arbitrary duplicate “new” ideas.

Representative newer paths include mace corpse-projectile behavior, hammer epicenter/echo infrastructure and charged club presentation. Exact balance/activation details should be reviewed before redesign. Historical counterblow descriptions must not automatically be combined with newer alternatives as two simultaneous active perks.

Crossbows70 is a specific gap: its named source path is excluded under the current release-safe build gate, and an alternate enabled implementation is not established in the canonical coverage. A polearm mobility branch has a never-assigned state field with unverified full gameplay impact. These are not proof that every combat mechanic is broken.

### Magic 35/70 — IMPLEMENTED/MIXED; LIVE TEST PENDING

Existing source covers fire charging, ice trail/storm behavior, blood shield renewal, skeleton/summon lifecycle and higher-level Surtling/carrier/cage actions. Summon roster commands, portal handling and carrier cargo already have execution paths.

The historical BloodDome filename describes current cage implementation, not a promise of the old dome behavior. Blood shield renewal belongs to magic rather than physical blocking. Build symbols currently enable named magic70/carrier paths; an “experiment” name alone does not mean disabled.

Portal/follow/recall, cargo preservation, cast interruption and zone cleanup remain important current live-test gaps. New summon ideas should extend or deliberately replace these lifecycle semantics, not create another unrelated cargo/portal subsystem.

### Utility 35/70 — IMPLEMENTED/MIXED; DESIGN PENDING in specific areas

Existing movement source supports sprint/movement phases and additional-jump handling; utility also includes fishing/riding, farming, cooking/feasts, woodcutting/resource chains and mining interactions. Crafting includes Workshop/network storage, remote inventory costs, processing attribution and Potential Forge paths.

These systems provide reuse opportunities for profession progression and coordinated resource workflows. Simultaneous players, owned/unowned storage, full inventory, cancellation and processing producer-versus-collector cases still need acceptance coverage.

Pickaxes70 redesign is **DESIGN PENDING / DEFERRED**. Current neighboring damage/collapse and native support cascades must be distinguished before a replacement is approved. A previously rejected single-segment alternative is not the current approved redesign. Existing run-phase and landing presentation has source but needs current live verification.

### Gold Core / Pantheon — IMPLEMENTED protocol; PARTIAL acceptance

Source implements shared acknowledgement, Favor/ledger, exhaustion, receipts, retry/recovery and Pantheon integration. It is tied to player identity and world-specific ledger state; character receipts and server state must remain a consistent pair.

The current action contract is narrow, not a generic executable catalog of all divine powers. Its crafting-shaped kinds encode Masterwork production and an upgrade action. Source defaults/constants include a Favor cap of 1000 and action costs, but this snapshot does not create a new balance approval.

Near-cap behavior, arming/debt semantics, save order, reconnect/restart and crash windows remain LIVE TEST PENDING. Historical 1.4.118 screenshots/checkpoints are not current behavior authority. A debug Favor assignment is not a complete economy reset.

### Combat 100 — CATALOG ONLY in confirmed coverage

Names/narrative/catalog entries exist, but dedicated current combat100 gameplay execution is not established by the canonical coverage reviewed for this snapshot. Generic level-100 scaling and combat35/70 paths do not count as implemented combat100 actions.

This is an ideation opportunity requiring a real owner feasibility/inventory review, not a promise that Gold Core already supports every proposed combat power. No new action is approved simply by filling a catalog slot.

### Magic 100 — CATALOG ONLY in confirmed coverage

Elemental/Blood100 catalog content exists without confirmed dedicated action execution in the reviewed canonical coverage. Continuously scaled regeneration belongs to XP/Skill Bonuses, and the existing summons/zones are 35/70 gameplay.

New high-level magic proposals need an explicit desired mechanic and shared integration review. Do not represent future deity/spell descriptions as executable powers or assume that every current summon can be reused unchanged for a new persistent effect.

### Utility 100 — named IMPLEMENTED paths; remaining slots UNKNOWN

Confirmed source branches include mining-related metal-weight behavior and divine crafting/forge action integration, plus associated item metadata/tooltips. Metal-weight behavior is distinct from XP scaling and must avoid duplicate modifier application.

Crafting100 Masterwork and the +3 same-item native upgrader transaction path exist in source. The upgrade path requires the recipe-selected upgrade resource, along with configuration, arming and eligibility conditions. Current runtime availability and any future custom mastery-idol content remain unverified; no specific PhaseB disable gate was established in the reviewed path. Do not treat an upgrade contract as complete proof of an enabled, tested forge interaction.

Other utility100 slots remain untraced/unconfirmed here. A proposal should identify whether it extends one of these actual paths or introduces a new action requiring owner and Gold Core work.

### Core UX & Tutorials — IMPLEMENTED framework; LIVE TEST PENDING

Shared milestone presentation, raven tutorials, skill/item tooltips, proc HUD, icons and windows exist. Threshold presentation can repeat while first tutorial/reward semantics remain once-only. Pending unread raven lessons have persistence support.

Exact layout, language fallback, focus recovery, UI scale/resolution and reconnection behavior still need live acceptance. One base spear description key is structurally missing in recorded QC even though Ukrainian narrative/override paths may mask it. Mechanic-specific wording and VFX/SFX remain owned by the mechanic domain.

### Cross-domain issues that should influence ideas

Do not duplicate an existing mechanic because its live regression is incomplete. Conversely, do not assume an old bug was fixed solely because source changed. Known concerns include summon portal/cargo behavior, charge/echo/run visuals, headless versus client landing assets, storage ownership coverage and stale tests expecting older constants or method shapes.

The important unresolved distinction is often **which behavior is approved and actually exercised**, not whether a class exists. Full migration gameplay validation is deferred; this snapshot records available foundations and gaps without closing that baseline.

## 10. What the ideation chat must not assume

- Arbitrary art/audio/animation or external AssetBundles can be imported.
- A plausible prefab/clip name proves that resource exists.
- Every level-100 catalog slot is implemented, approved or functional.
- Every normalized event type has a complete native publisher.
- Every effect/timer is durable or automatically replicated.
- Client-only state, UI selection or owner-reported skills are universal server authority.
- All objects/cargo automatically survive portals, restart or owner transfer.
- All cooldowns share the same offline/online timing rule.
- A visual clone is a safe gameplay/network object.
- Custom animations can simply be added without violating policy.
- Each feature should invent a new loader, pool, networking or storage subsystem.
- Historical prose overrides current source/build gates.
- Registered Harmony patches prove complete gameplay acceptance.
- A complicated implementation is impossible.
- A framework capability is an already-approved new mechanic.

## 11. Recommended idea feasibility format

Use this structure for each serious proposal. Keep technical details conditional unless Codex has confirmed them.

**IDEA** — What the mechanic does, in one clear paragraph.

**GAMEPLAY OWNER** — Likely domain and why. Identify shared reviewers only where the behavior crosses real boundaries.

**PLAYER EXPERIENCE** — What the player does, sees and hears; the meaningful decision or payoff.

**TRIGGER** — Qualifying action/state, passive versus active activation and cancellation.

**GAMEPLAY EFFECT** — What changes; target rules, scope, duration and relevant chance/cost/cooldown if decided.

**VANILLA PRESENTATION INTENT** — Desired native visual/audio character and readability. Preserve an exact user-selected asset; otherwise avoid claiming a guessed resource.

**MULTIPLAYER SEMANTICS** — Actor, authority expectations, remote observers, concurrent interaction and duplicate handling.

**PERSISTENCE** — Desired outcomes across reconnect/logout/restart/portal/death/destruction; state that should deliberately disappear.

**REUSE OPPORTUNITIES** — Existing Mastery foundations that plausibly help, with clear uncertainty about unsupported integration.

**ARCHITECTURAL RISKS** — Meaningful conflicts such as duplicated rewards, recursion, stale authority, mixed saves, input locks or performance spam. Do not list every conceivable technical detail.

**OPEN DESIGN QUESTIONS** — Decisions the user should make; distinguish unresolved gameplay semantics from implementation choices Codex can handle.

**CODEX FEASIBILITY CHECK** — Choose one provisional label:
- LIKELY STRAIGHTFORWARD
- LIKELY FEASIBLE / NEEDS SOURCE REVIEW
- ARCHITECTURALLY COMPLEX
- NEEDS CODEX INVESTIGATION

“Likely” is a planning assessment, not an implementation guarantee. Do not claim IMPOSSIBLE without strong evidence. A native-resource policy conflict may require reframing presentation; a missing helper usually requires investigation rather than rejection.

## 12. Design versus implementation boundary

The external assistant's job is to help design **good gameplay**. Respect architecture without prematurely shrinking an idea to today's exact implementation. Describe the desired experience and outcomes first; then identify likely reuse and questions.

Codex later determines exact classes/hooks, Harmony targets, RPC design, native asset identities, serialization/save ordering, loading/pooling and safe component lifecycles. Those decisions require current source and native-game inspection. The external conversation should not fabricate them from familiar Unity terminology.

A design can need new shared integration and still be worthwhile. Compare the gameplay value against actual complexity: persistent economies or cross-player inventory actions demand stronger recovery rules than a local cosmetic cue. That is a scope/risk distinction, not a ban on ambitious mechanics.

Preserve approved gameplay semantics during feasibility work. If implementation reveals a conflict, expose it and return the specific decision to the user; do not quietly replace cargo survival, change a selected effect or turn an active mechanic into a small passive percentage.

This report prevents architectural nonsense and false implementation claims. It should make innovation more grounded, not make brainstorming afraid of anything that requires engineering.

## 13. Handoff to the appropriate Codex owner

A useful finalized handoff includes mechanic purpose; skill and perk level; approved behavior; trigger; decided cooldown/cost/chance; target/friend-enemy/boss rules; multiplayer semantics; persistence expectations; desired visual intent; desired audio intent; any exact user-selected vanilla asset; rejected alternatives; and unresolved questions.

Also state whether the proposal extends an existing mechanic or replaces an unfinished one. Identify the relevant current gap, especially a deferred redesign or catalog-only level100 slot. Separate optional refinements from acceptance requirements so Codex can create a bounded task.

Codex should perform current source/native-asset feasibility review before implementation. It can then reserve exact files, inspect compile gates and shared boundaries, request affected-owner review and define observable regression cases. Build/deploy/runtime tests remain separately scoped; brainstorming approval is not blanket permission to launch or change production state.

For persistence-sensitive ideas, the handoff should describe at least one normal case and one interrupted case. For presentation, it should describe the primary cue and what disabled VFX/SFX means. These examples make the requested outcome reviewable without forcing the designer to invent an RPC or save format.

## Sources reviewed and unresolved scope

This snapshot reviewed root AGENTS; SCOPE_MAP; SHARED_SYSTEMS; CONTEXT_AND_STATUS; BUILD_RUNTIME_BASELINE; KNOWN_ISSUES; the XP and skill-bonus system indexes; Combat/Magic/Utility35/70 indexes; Pantheon/Core and Combat/Magic/Utility100 indexes; Core UX/Tutorials; and VANILLA_ASSET_WORKFLOW/CATALOG.

Targeted current source supplied checks for XP/milestone hooks, event publication, hit provenance, cooldown/transient state, owner/network authority, summon/portal/cargo, Workshop debit/processing, target effects, tutorial persistence, Gold transactions and representative perk paths. Project/build configuration supplied current compile-gate context. This was not a complete source audit or a new gameplay test.

Important unresolved areas are exact per-perk live acceptance, universal event/network coverage, unconfirmed catalog100 actions, pending pickaxe redesign, upgrade availability and future mastery-idol content, exact native clip/render context and performance under multiplayer load. These are explicitly left for bounded Codex review; no speculative fix is presented as fact.

This remains a ONE-TIME IDEATION CONTEXT SNAPSHOT. It is not automatically maintained, and future development does not update it. Refresh only after an explicit user request; current repository canonical docs/source take precedence.
