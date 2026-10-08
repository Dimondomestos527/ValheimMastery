# XP_COOKING_NEXT_PATCH_20261007
- Owner/profile: XP_SKILL_BONUSES; root integration task COOKING_XP_NEXT_PATCH_20261007.
- Authorization: direct human source-thread message01a11802-ae7f-7143-a222-5834ea7bfffc says «Додай це в наступний патч» after Cooking2/3/4 proposal; verified with read_thread. Candidate + dualbuild/static checks allowed by active root task; no installation.
- Goal: isolated Cooking-only curve from exact installed resident-patch snapshot; fractional pre-award raw level clamped0..100,2 through35,linear3 at70,linear4 at100; one shared XP scaling application.
- Acceptance: single source delta ExperienceContext cooking branch; unchanged all other inputs/skill branches/modifier order/action routes; dual builds/provenance; compiled curve and Scale/RaiseSkill routing evidence including fractional/clamp/boundary/no-double-scale; frozen hashes + manual tests + handoff.
- Exact reservations: ONLY .agent/tasks/xp/XP_COOKING_NEXT_PATCH_20261007.md; .agent/progress/XP_COOKING_NEXT_PATCH_20261007.md; .agent/handoffs/XP_COOKING_NEXT_PATCH_20261007.md; validation/cooking-xp-next-patch-20261007/** (exclusive isolated snapshot/src-modern/ExperienceContext.cs, copied installed inputs/build tool dependencies, task-local QC/evidence/frozen outputs).
- Source baseline: validation/resident-patch-no-assets-deploy-20261007/snapshot; verify source-pinned manifest and installed clientED95E19946D258FF4C709455DE3907FABA538F76FFB7AF15A6CB3D5576E28796/serverA9D9B6948F260A58550FB40CA58B670851E4C015E2F6D83FFD7BFCAC8BB6DC9C.
- Non-goals: canonical/fullcanonical writes, existing installed snapshot modification, immediate build deployment, launch, save/config mutation, Assets or pending other-domain overlays, new output/food stats/time/recipe weighting/award ownership fixes, external memory.
- Complexity:NORMAL; root-only.
- Relevant docs: AGENTS/profile/bootstrap/templates; root active task/progress/handoff; XP_SYSTEM; SHARED_SYSTEMS; installed deployment report.
- Relevant classes: ExperienceContext.GetSmoothSkillMultiplier/Scale; RaiseSkillPatch.Prefix; PeacefulXp.AwardCooking; native Skills.RaiseSkill/Humanoid.RaiseSkill.
- Required regression: compiled fractional thresholds and clamp; single curve application in sharedScale; other skills unchanged; modifier order preserved; fractional pre-award vs after crossing; disabled/remote attribution limits; dual client/server static contracts. LIVE UNTESTED.
- Deployment:NONE; approved for NEXT coordinated patch after root/Utility acceptance.
- Dependencies: Utility read-only cooking action audit separately; no parallel shared writes.
- Status:COMPLETE owner candidate; READY_FOR_ROOT_REVIEW_NEXT_PATCH; integration/live pending.

## SHARED SYSTEM CHANGE
- File/system: exclusive isolated snapshot/src-modern/ExperienceContext.cs:GetSmoothSkillMultiplier Cooking case only.
- Reason: explicit human Cooking progression tuning2→3→4.
- Affected domains: XP_SKILL_BONUSES, UTILITY_35_70 Cooking actions; shared RaiseSkill modifiers/logging.
- Regression: fractional/clamp/boundary, native+mod award once, other skills and modifier order, preserve recipe/resource/firstunique/world/boss semantics.
- Integration: root ONLY after affected-owner read-only review. No canonical shared mutation here.

## PRESENTATION / VANILLA ASSET PLAN
N/A; no presentation or asset changes.

