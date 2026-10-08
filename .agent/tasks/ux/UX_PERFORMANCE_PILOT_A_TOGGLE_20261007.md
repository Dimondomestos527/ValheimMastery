# UX_PERFORMANCE_PILOT_A_TOGGLE_20261007
- Owner/profile: CORE_UX_TUTORIALS; Utility100 owns Pilot A runtime branches; Regression root owns shared config integration, combined build and deployment.
- Human authorization: prepare Pilot A and a native in-game mod toggle; later explicit authorization installs the combined client/server patch without assets or game/server launch.
- Goal: add a native-Valheim-styled checkbox/radio control labelled `Експериментальна оптимізація`, default OFF, directly bound to the real process-local config entry.
- Frozen source baseline: `validation/utility100-idol-native-reload-identity-20261007/snapshot/src-modern`, installed candidate DLL identities client `E9D802544FDFFE495E143FEE6EA4A79C472B1D9FB56893EC3A782C2FAF0A9E57`, server `904EE306DD9C84096A5A55119FA745BD1163D782BD6785C0865246C67B784460`.
- Exact write reservations: this task/progress/handoff; `validation/performance-pilot-a-ux-toggle-20261007/**`; new candidate class `ExperimentalOptimizationToggle`; candidate-only `MasteryWindowController.Build` layout hook. Canonical `src-modern/MasteryPlugin.cs` and `MasteryConfig` are ROOT-ONLY and remain read-only. Canonical `src-modern/MasteryWindow.cs` also remains unchanged until root serial integration.
- Shared flag contract accepted from root: `MasteryPlugin.Settings.ExperimentalOptimization`, readonly `ConfigEntry<bool>`, bound as `Performance/ExperimentalOptimization`, default `false`. UI reads/writes `.Value`; Utility100 observes transitions. Each process owns its own config; no RPC or remote-server claim.
- Non-goals: no graphics-quality controls, gameplay rates/AI/physics changes, Gold changes, Pilot B, custom assets/Canvas, config/save writes during preparation, deployment or launch.
- Test dimensions: default OFF; actual binding/persistence contract; ON/OFF/external reset/reopen; mouse/keyboard navigation; focus clear on close/disable; local-client vs host wording; UI scale/long label; headless no construction; no duplicate row; both compiled variants.
- Status: COMPLETE for source/static UX ownership; LIVE UNTESTED. Root retains deployment and post-install integrity ownership.

## SHARED SYSTEM CHANGE
- `MasteryWindowController.Build`: reserve bottom row for the experimental toggle and shrink the existing scroll viewport to avoid overlap. Affected domains: all users of the shared Skills window.
- New `ExperimentalOptimizationToggle`: process-local config control and focus lifecycle. Affected domains: UX and Utility100 Pilot A runtime.
- Root-only `MasteryConfig` proposal: add the exact bool field/binding above. Regression required: default OFF, client/server config independence, headless, reopen/persistence, runtime transition reset, no network-authority claim.
