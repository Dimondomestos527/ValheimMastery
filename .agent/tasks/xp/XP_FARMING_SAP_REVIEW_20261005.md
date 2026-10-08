# XP_FARMING_SAP_REVIEW_20261005
- Owner/profile: XP_SKILL_BONUSES; read-only cross-domain review requested by user. Canonical sap file navigation: UTILITY_35_70; observed bonus is level-dependent with no milestone gate, reviewed here as an XP/passive cross-domain boundary. No ownership records reassigned.
- Goal: перевірити, чи поточний код/встановлена 1.4.123 справді додає бонусний сік Іґґдрасіля за фермерство; пояснити умови, обмеження та конкретні ризики.
- Acceptance criteria: простежено SapCollector extract → фактичний spawn → бонус/recipient/рівень; перевірено build gates та native API; встановлені DLL і чинні докази перевірено read-only; висновок розрізняє source/compiled/live.
- Scope/allowed files; exact write reservations: .agent/tasks/xp/XP_FARMING_SAP_REVIEW_20261005.md; .agent/progress/XP_FARMING_SAP_REVIEW_20261005.md; .agent/handoffs/XP_FARMING_SAP_REVIEW_20261005.md. All source/native assemblies/build/runtime/config/logs are read-only.
- Non-goals: gameplay fixes, build, deployment, launching client/server, production saves/config mutation, external memory update, full Utility audit.
- Complexity: NORMAL; root-only bounded review.
- Relevant canonical docs: AGENTS.md; XP profile; CHAT_BOOTSTRAP; CONTEXT_AND_STATUS; SCOPE_MAP; SHARED_SYSTEMS; BUILD_RUNTIME_BASELINE; docs/perks/UTILITY_35_70.md; existing Utility baseline progress/handoff.
- Relevant source/classes and build gates: SapHarvestBonus; Farming35BonusHarvestPatch; FarmingPerkService; PerkRuntimeService; CustomSkillStore; PeacefulXpPatches; ValheimMasteryPoC.csproj; tools/Build123.ps1; native SapCollector and selected Unity Instantiate overloads.
- Shared systems touched: NONE (read-only).
- Required regression: static execution-path and native-signature analysis; live level34/35/70, collector/owner, zero output, multiple/full inventory, multiplayer/reconnect/restart scenarios recorded only. Live testing outside current read-only scope.
- Deployment requirement: NONE.
- Status: COMPLETE (read-only source/native/installed review; LIVE_TEST_REQUIRED).
- Known dependencies/overlapping tasks: Utility baseline COMPLETE, its reservations released; this task reserves only three unique documentation files.
- PRESENTATION / VANILLA ASSET PLAN: N/A, no presentation changes.

