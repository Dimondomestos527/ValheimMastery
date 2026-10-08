# Canonical source-derived index

As of2026-10-04,1.4.123. Read CONTEXT_AND_STATUS and KNOWN_ISSUES. This is a concise source navigation/status placeholder, not a full gameplay specification. Exact mechanics require current source and compile gates; no new design approval. Gameplay regression DEFERRED. No source presence promoted to LIVE VERIFIED.

## Status: MIXED
IMPLEMENTED IN SOURCE: ExperienceContext modifier calculation; MasteryPatches native RaiseSkill integration; WorldProgressionXpService world/boss mapping; CustomSkillStore fallback; tier/first-unique/legacy catchup hooks.
STATIC VERIFIED: prior clean-QC structure within linked validation evidence, not every gameplay formula.
LIVE VERIFIED: no full XP regression established here.
## Owner / pointers
XP_SKILL_BONUSES. ExperienceContext.cs, MasteryPatches.cs, CustomSkillStore.cs, WorldProgressionXp*.cs, TierDatabase.cs, MasteryClassificationService.cs, LegacyCatchUpService.cs, GatheringProgressionService.cs, CookingCraftingProgression.cs, PeacefulXpPatches.cs.
Inspect death penalty/SkillFloorPatch in MasteryPatches; milestone presentation belongs UX, eligibility belongs mechanic.
## Contracts to preserve
Raw level vsSE bonuses; recipient and source owner; first-time durable keys; custom/native distinction; generated-hit XP allowlist; death-floor persistent flags. Direct vm_skill/vanilla CheatRaiseSkill does not emulate natural RaiseSkill crossing.
## Required regression
Isolated/combined modifiers; exact accumulator delta and level crossings; recipient ownership; multiplayer sync/restart; no repeated first-time rewards. Current XP diagnostic lacks per-line recipient correlation; no new logger implemented.

