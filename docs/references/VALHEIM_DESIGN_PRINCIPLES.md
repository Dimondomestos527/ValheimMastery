# Valheim Design Principles

Curated 2026-10-04 for Mastery 1.4.123 / installed Valheim 1.0.16. Applied guidance; not authority for current gameplay implementation. Consult only relevant topics. Resolve conflicts using [REFERENCE_INDEX](REFERENCE_INDEX.md).

This is not an official Valheim design doctrine. The left column records narrow **IRON GATE / SOURCE-SUPPORTED PRINCIPLES**; the right column is explicitly **PROJECT INTERPRETATION**. A mod design still needs user approval and the correct gameplay owner.

## Evidence and application

| Area | IRON GATE / SOURCE-SUPPORTED PRINCIPLE | PROJECT INTERPRETATION for Mastery |
|---|---|---|
| Co-op / PvE | Iron Gate describes solo/co-op play, a PvE focus and consensual PvP. [D01](https://www.valheimgame.com/faq/) | A perk should remain useful solo and understandable to teammates; define friendly-target/access rules instead of importing competitive-match assumptions. |
| Adventure/exploration | Biomes, voyages and discoveries are central to the official overview. [D02](https://www.valheimgame.com/) | Utility efficiency should support expeditions. Review whether unlimited production, transport or remote access removes the reason to leave a base. |
| Progression | The FAQ connects biome challenge/resources with boss-driven advancement. [D01](https://www.valheimgame.com/faq/) | Connect skill mastery to existing activities and progression context. New options can be stronger without erasing resource/biome/station relevance. |
| Survival preparation | Food improves health/stamina rather than imposing starvation death. [D01](https://www.valheimgame.com/faq/) | Preserve preparation choices; do not assume every survival idea needs another draining meter. Cooking value can come from relevant choices and feedback. |
| Visual identity/readability | Iron Gate describes stylized low-detail art combined with modern lighting/effects. [D01](https://www.valheimgame.com/faq/) | Native art is a foundation for readable composition. A stronger perk need not flood the screen; keep target/attack silhouettes and danger cues legible. “Readability” is our application, not an official quantified rule. |
| Multiplayer experience | Iron Gate frames a small-group co-op experience with hosted/dedicated options. [D01](https://www.valheimgame.com/faq/) | Specify what owner and observers see, concurrency, interruption and recovery. Do not make support effects depend on one client's cosmetic state. |
| Integrated identity | Building, crafting, survival, cooperation and exploration are presented together. [D02](https://www.valheimgame.com/) | Extend weapons, staffs, stations, creatures and travel rather than bolt on an unrelated menu economy/minigame. A detached system needs a concrete benefit and integration argument. |
| Respect invested worlds | Iron Gate explains avoiding world-content changes that would damage player-built structures. [D01](https://www.valheimgame.com/faq/) | Treat persistent builds, containers, cargo and earned items as player investment; plan interrupted/restart behavior before introducing destructive transformation. This is not a blanket ban on changing a world. |
| Development expectations | Jonathan Smårs' GDC 2024 abstract concerns patch-by-patch development and managing expectations. [D03](https://www.gdcvault.com/play/1034494/Independent-Games-Summit-Valheim-Vikings) | Label source implementation, design-only content and live acceptance separately. The abstract supports the topic; it does not establish a hidden official balance doctrine. |

## Native anchors and project boundaries

The current installed client/server assemblies identify Valheim 1.0.16. Bounded inspection confirmed Player food/save/teleport, Inventory save/removal and ZNetView ownership entry points; this is API evidence, not an exhaustive design audit or a new gameplay test. For claims about current vanilla behavior, inspect the actual action, settings and native state, then use Iron Gate commentary for context. The public FAQ contains some historically worded sections, so do not take unrelated roadmap/platform details as current implementation evidence.

Mastery's 35/70/100 milestones, vanilla-only imported-asset restriction, domain ownership and Gold rules are **project decisions**, not Iron Gate rules. Source/canonical guidance is in [SCOPE_MAP](../architecture/SCOPE_MAP.md), [SHARED_SYSTEMS](../architecture/SHARED_SYSTEMS.md) and [VANILLA_ASSET_WORKFLOW](../architecture/VANILLA_ASSET_WORKFLOW.md). Level-100 catalog content does not prove an active mechanic.

## Use during ideation/review

Ask what the player does differently; which existing Valheim loop improves; what choice/risk remains; how a teammate understands the effect; and whether it preserves progression and earned state. These are review prompts, not fixed bans on ambitious ideas, automation, new runtime compositions or meaningful power growth.

A weak proposal says “add another currency and flashy panel” without explaining why the base activities become better. A stronger proposal identifies a native action, a new decision and its cost/feedback, then defines multiplayer and persistence semantics. Detailed balance and exact native assets remain user/owner feasibility work.

Use [SURVIVAL_GAME_DESIGN_REFERENCE](SURVIVAL_GAME_DESIGN_REFERENCE.md) for optional comparative lenses. Another game's success does not establish that its rules belong in Valheim.
