# <unique-task> progress
Define task-specific weighted phases BEFORE execution; weights sum100.
Example (adjust before work, not after to inflate):
1. Scope/evidence20%
2. Implementation40%
3. Static/build verification20%
4. Required acceptance/regression15%
5. Handoff/docs5%
Each phase has measurable exit criteria, completed units/total units, linked evidence.
Progress=sum(weight × completed accepted units/total). No intuition. A failed/blocked required check contributes0 for that check. Optional live tests explicitly outside scope do not imply LIVE VERIFIED.
Track status, reserved files, changes, commands/results, blockers and exact next action. Completed code is not completed acceptance.

