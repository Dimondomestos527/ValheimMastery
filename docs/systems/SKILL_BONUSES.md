# Canonical source-derived index

As of2026-10-04,1.4.123. Read CONTEXT_AND_STATUS and KNOWN_ISSUES. This is a concise source navigation/status placeholder, not a full gameplay specification. Exact mechanics require current source and compile gates; no new design approval. Gameplay regression DEFERRED. No source presence promoted to LIVE VERIFIED.

## Status: IMPLEMENTED IN SOURCE / live matrix pending
Level-dependent passives, not milestone perks. Owner XP_SKILL_BONUSES.
Pointers: PerkBalance.cs, MasteryRuntime.cs, FistsPassiveScaling.cs, MagicSkillPassives.cs, VanillaBonusFeedback.cs; mixed native hooks in MasteryPatches/perk patch files require class-level trace.
Bare hands vsfist weapons must remain distinct. Vanilla retained bonus coefficient is not a percentage of total final damage. Do not recalculate approved balance from old prose.
Current Build123 carries MASTERY_CLUB_PASSIVES. MasteryRuntime applies local/server values and manifest; check raw config versus effective transformed values.
Required: levels1/34/35/69/70/99/100, native base/effective/final, food/mead/block/regen/weight cases where relevant; owner sync, settings disabled.
[SkillScaling] diagnostic is a sampled/deduplicated factor trace, not complete final-value observability.

