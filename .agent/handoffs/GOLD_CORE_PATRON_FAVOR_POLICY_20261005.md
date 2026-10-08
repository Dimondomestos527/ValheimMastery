# GOLD_CORE_PATRON_FAVOR_POLICY_20261005 handoff
- Owner GOLD_CORE/root. Status COMPLETE documentation decision; reservations RELEASED.
- Approved human rule2026-10-05: separate Favor balances/scales by patron; associated skills fill and spend the one pool of their patron. One UI scale per patron, not per skill. No cross-player/world pooling implied.
- Canonical decision docs/gold/PANTHEON_CORE.md appendix Approved patron Favor policy —2026-10-05. Status DESIGN ONLY, not implemented/live verified. Current runtime1.4.123 single Favor service/player ledger remains until separately scoped implementation.
- Changed files only canonical doc appendix + own task/progress/this handoff. No source/build/protocol/schema/config/world/character/runtime or external memory edits. No agents/messages to other chats.
- Future root-owned requirements: stable PatronId, skill/action routing, patron-specific balance/state identity, patron frozen in admitted transaction/receipt, atomic shared-patron spends, explicit existing Völundr ledger/receipt migration. Affected gameplay/UI owners review shared mutations before integration.
- Required tests: associated skills contribute/spend same pool; another patron unchanged; one scale per patron; simultaneous cross-skill insufficient funds/no overdraw/double debit; cap, replay, world/player identity, reconnect/crash/save/rollback. Synthetic Tyr UI mock is insufficient authority evidence.
- Open decisions: exhaustion/cooldown global or patron-specific; skill-level/unlock award eligibility; rates/budgets/caps/new patron unlock semantics. Do not infer these from Favor pool decision.
- Exact next action: future patron/skill owner reads approved contract; Gold Core creates scoped multi-patron implementation task after explicit authorization, chooses migration/transaction design and affected-owner review. Current mode BASELINE / READ-ONLY; prior bounded UI exception complete.
- Minimal next-session files: root AGENTS/GOLD_CORE profile; this task/progress/handoff; approved appendix; only relevant source once implementation task exists.
- Integrity context: 8/8 source aggregate unchanged; after8-file aggregate SHA256 FD2B580C5E182112B5059DECFC2C875125173EA194F450E78675727C19F58226.

