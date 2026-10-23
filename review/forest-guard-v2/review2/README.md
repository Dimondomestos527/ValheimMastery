# Forest Guard V2 — review 2 successor

Source-only review snapshot, not a full build tree, release or installation. The repository root remains historical ancestry. Parent source snapshot: 5d35b5b0a62ec628d638c31ddb74be06e1d6801e. Previous stage-b is immutable.

Only production change: Controller BD30E245 ->1517208F. Compare ../stage-b/after/src-modern/MasterIdolForestGuardLifecycleController.cs against src-modern/MasterIdolForestGuardLifecycleController.cs. All other role inputs remain unchanged, including Owner/Visibility/Cargo and wire v14. Full role predecessor pins and original installed-stage lineage42C/0CD3 are in MANIFEST.json; predecessor local candidates are Client508F/ServerCC83. Inherited Git root is NOT their source tree. Required successor full pair is separately owned by Infra; no successor DLL hashes are claimed here.

## Three findings

1. Stale dispatch: capture four Entry references/context before Commit; validate current entry/context/intent/epoch before each request and after Cargo/diagnostic/native callbacks, including before later flag writes. Targeted P1 PASS; same P1 with old BD30 caused NullReferenceException in Cargo/native/diagnostic reset callbacks and expected FAIL. This establishes the specific causal regression, not every possible reentrancy ordering.
2. Inactive ACK: actual Client/Server native metadata shows FindInstance uses canonical ZDO/m_instances, no activeSelf gate; ZNetView declares no OnEnable/OnDisable. Unchanged actual Owner/Visibility passed isolated inactive four-body CurrentApplication -> Stamp -> Report -> Applied (host/local owner). Controlled scene/Unity boundaries are explicit in tests/Stubs.cs. This is NOT engine execution or remote RPC delivery; backend remains unchanged.
3. Old four ALIVE slots /zero residents with missing/nonmodule/family/nonce/born-mismatch ZDO remain RECOVERY BLOCKED. Scoped actual helper produced no UID/Death changes, native writes, owner dispatch or duplicate bodies. Full Recruitment factory was not run; unchanged birth guards remain. Real saved-world ZDO availability/root cause UNKNOWN. Safe blocking is not resident recovery.

The former pending/excluded outer-controller guard is now explicitly human-authorized and INCLUDED at1517 within this scope. All12 LIVE acceptances remain UNRUN. Existing36+5+1/Cargo/native evidence is reused for unchanged boundaries, not claimed as a newly executed successor suite.

## Test source and limitations

TargetedCases.cs contains the three executed groups. ExistingCases.cs retains existing setup/helpers in its partial class; LegacyMain and other historical cases are NOT run by this entry point. Stubs.cs models Unity/Registry/loading/position/Cargo/cosmetics; no game assemblies are shipped. Checks.csproj uses actual helper source from immutable ../stage-b and only the new Controller. The project paths were made relative for external reading; this portable export project has not been rerun by the publisher. Owner evidence was executed on the equivalent pinned source inputs recorded in the original freeze.

Optional external reproduction: dotnet run --project tests/Checks.csproj. To inspect the old causal failure, pass -p:ControllerSource=../../stage-b/after/src-modern/MasterIdolForestGuardLifecycleController.cs and -- legacy-guard-repro; controller paths are relative to the test project. That run is expected to fail, not a supported runtime mode. No production game/world is required.

## Recovery proposal — separate, not implemented

Only in a separately authorized task, inspect a test-world copy after native chunk loading for exact Registry/native-load birth identity and independent Cargo bindings. Use existing Rebind only with one unambiguous complete proof. Missing/ambiguous proof remains BLOCKED for human decision. Never clear ALIVE slots, infer death from a missing Character, edit arbitrary historical UID targets or spawn replacements under this publication task.

No DLLs, raw logs, native IL dumps, private world/actor IDs, configs, saves or assets included. No merge to Dev/Release, PR3 edits or installation. Next: external review and Root/Infra final local candidate gates; stop after this scoped handoff.
