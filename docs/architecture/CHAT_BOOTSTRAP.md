# New chat bootstrap
Provide only:
PROFILE: .agent/profiles/<PROFILE>.md
ACTIVE TASK: .agent/tasks/<domain>/<unique-task>.md

Read order:
1. Root/canonical AGENTS.md (and applicable parent instructions).
2. Specified profile.
3. Specified task.
4. .agent/progress/<unique-task>.md.
5. .agent/handoffs/<unique-task>.md if present.
6. Named canonical docs (progressive disclosure).
7. Relevant source and actual compile gates.
8. Git/worktree status only once Git enabled; do not initialize Git.

Check task authorization/status and exact file reservations before writes. Inspect applicable shared systems, not all198 files. Missing handoff/progress is reported and prepared only within task scope.
Use unique task IDs across domains, e.g. COMBAT100_CROSSBOW_TURRET_20261004. The example is not an approved feature.
On stop: update progress with evidence and handoff exact next action. Do not depend on chat history. Profile files do not spawn agents/chats.

