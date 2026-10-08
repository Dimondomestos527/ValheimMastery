# Survival Game Design Reference

Curated 2026-10-04 for Mastery 1.4.123 / installed Valheim 1.0.16. Applied guidance; not authority for current gameplay implementation. Consult only relevant topics. Resolve conflicts using [REFERENCE_INDEX](REFERENCE_INDEX.md).

These are optional design lenses, not balance requirements. **Source lesson** summarizes only inspected material; **Mastery application** is our interpretation. Do not transplant another game's hunger, permadeath, reset cadence or economy into Valheim without a specific approved purpose.

## Curated lessons

| Design concern | Source lesson and evidence | Mastery application / review question |
|---|---|---|
| Meaningful survival decisions | The Long Dark emphasizes balancing supplies against energy/time needed to obtain them. [D06](https://www.thelongdark.com/survival-mode/) | What viable choice changes: leave now, prepare, carry more, retreat, or cooperate? A timer with one mandatory response is weak design. |
| Time/resource pressure | Pacific Drive's developer describes preparation, maintenance, dangerous outings and safe return. [D05](https://blog.playstation.com/2023/02/09/ironwood-studios-returns-with-a-first-look-at-the-gameplay-of-pacific-drive/) | Put pressure around meaningful action windows and consequences; avoid upkeep that merely interrupts play. Valheim does not need a copied fuel meter. |
| Progression | Astroneer's slides identify flat crafting that failed to change play over time. [D04](https://media.gdcvault.com/gdcsummer2020/presentations/Biddlecom-Aaron-MiningYourOwnDesign.pdf) (slides 7, 22–23) | A milestone should open decisions or interactions, not only make the same repetitive action numerically faster. This does not ban useful passive scaling. |
| Faucets/sinks | Astroneer's soil/power sinks had redundant outputs that undermined exploration incentives. [D04](https://media.gdcvault.com/gdcsummer2020/presentations/Biddlecom-Aaron-MiningYourOwnDesign.pdf) (slides 20–23) | Map every resource/Favor/XP inflow (faucet), spend/loss (sink), cap and reuse loop. Check producer versus collector attribution, retries and repair/craft/build loops. |
| Crafting coherence | Astroneer reused recognizable interfaces and consolidated overly similar modules. [D04](https://media.gdcvault.com/gdcsummer2020/presentations/Biddlecom-Aaron-MiningYourOwnDesign.pdf) (slides 38–39) | Prefer extending stations, recipes and Workshop interactions before inventing another detached station/menu; preserve cost/result clarity. |
| Exploration incentives | Pacific Drive links hazardous destinations to resources and situational preparation. [D05](https://blog.playstation.com/2023/02/09/ironwood-studios-returns-with-a-first-look-at-the-gameplay-of-pacific-drive/) | A carrier or resource perk can reduce friction while leaving route, destination and preparation choices. Review whether infinite safe-base outputs dominate venturing out. |
| Risk/reward | The return loop in Pacific Drive connects collected supplies with further preparation. [D05](https://blog.playstation.com/2023/02/09/ironwood-studios-returns-with-a-first-look-at-the-gameplay-of-pacific-drive/) | Decide what can be lost, what is protected and why the risk is worth taking. Persistence uncertainty and duplicate rewards are implementation failures, not designed risk. |
| Avoiding busywork | Hinterland's historical UI redesign aimed to expose survival status/actions with less interruption. [D10](https://www.thelongdark.com/time-capsule/tireless-menace/) | Remove repeated menu/transfer labor while preserving interesting logistics. Count decisions gained versus clicks removed; more steps alone are not depth. |
| Power progression | MDA relates mechanics to emergent behavior and desired experience. [D08](https://users.cs.northwestern.edu/~hunicke/MDA.pdf) | Assess the whole loop: a combat buff also changes resource acquisition, travel risk and co-op roles. Ask what remains relevant after the perk unlocks. |
| Cooperative systems | Iron Gate's co-op framing is the local fit constraint. [D01](https://www.valheimgame.com/faq/); MDA supports considering emergent interaction. [D08](https://users.cs.northwestern.edu/~hunicke/MDA.pdf) | Design complementary contributions without mandatory specialization. Define shared costs, recipient rewards, simultaneous use and remote feedback explicitly. |
| Feedback/game feel | Kelly's GDC abstract challenges polish detached from context. [D09](https://www.gdcvault.com/play/1020861/don-t-juice-it-or) | Make cues explain readiness, success, danger or cost; tune frequency and clutter. Native VFX/SFX should reinforce the action rather than conceal its outcome. |
| Expanding without destabilizing | Astroneer's redesign examined existing incentives and connections; The Long Dark GDC abstract highlights authored-content/sandbox tensions. [D04](https://media.gdcvault.com/gdcsummer2020/presentations/Biddlecom-Aaron-MiningYourOwnDesign.pdf), [D07](https://www.gdcvault.com/play/1024896/A-Long-Dark-Road-Blending) | Map touched loops, saved state and player expectations before changing a core system. Preserve approved semantics; investigate conflicts instead of quietly replacing them. |

## A small applied economy check

For one resource, use **change in stock = inflows − outflows**, tracked per action/time window. Record who earns it, where it persists, repeat frequency, caps and transfers. This is a bookkeeping lens, not a calibrated simulator.

For a proposed mining bonus, examine harvest and crafting consumption together. A reduction in ore weight changes haul throughput even if drop count stays the same. For Workshop, shorter transaction time can alter production throughput and competing demand. For Favor, cap/arming/exhaustion may constrain actions differently from item costs. These are interaction questions, not claims that current paths passed live tests.

Check normal play, an optimized repeat loop and two concurrent players. If a benefit's intended activity is exploring but its best strategy is repeating a safe-base conversion, fix the incentive or explicitly approve that tradeoff. Do not solve runaway faucets by adding arbitrary punitive sinks after implementation.

## Design versus implementation

Describe the desired player decision and the observable outcome first. Leave exact patch/RPC/ZDO/receipt design to current native/Mastery inspection. Reuse appropriate shared systems; a reusable class is not proof of universal authority or durable state.

Before a core-system extension, identify existing dependencies, migration impact, failure recovery and a bounded acceptance example. Use source-backed constraints rather than reject complexity by instinct. Mastery's vanilla-only asset policy remains binding even when the comparison game relies on bespoke assets.

## Evidence limits

Astroneer uses reviewed developer slides (especially 7, 20–23, 38–39); MDA uses reviewed paper text; Pacific Drive uses a developer-authored 2023 article; Hinterland's survival description and archived 2016 UI rationale were read. These materials supply comparative principles, not current specifications for those games.

The Valheim, Long Dark sandbox/story and Kelly GDC entries use their published **session abstracts**, not a claimed full-video review. Consult the original talks before attributing detailed arguments or production results. Historical UI commentary is priority 8; applicable established design/developer material is priority 7. Current Valheim/Mastery evidence always takes precedence.
