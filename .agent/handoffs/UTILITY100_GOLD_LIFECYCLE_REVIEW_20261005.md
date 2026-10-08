# Utility100 lifecycle compatibility review

QC-only amendment SOURCE REVIEW APPROVED: Gold100Checks.cs D8809DB971A13A14EBAEA348F3B10EE644F8537D8DA03CFAF77C5BC904A38FAE. Diff against root before/ copy reviewed: replaces obsolete direct Enabled/ServerLedger IL assumptions with CraftingEnabled+CoreReady, current-world recovery/fresh accessors and deferred Apply readiness. Other checks preserved; callback success/failure expect IsServerLedgerCurrent and requestWorld, failure check precedes Validating.Remove. These are structural assertions, not complete control-flow proof; retain reported focused behavioral checks and native runtime limits. No QC source writes by Utility; Gold owns serial integration. Approval independent of active idol-build fix task.

## Revised candidate — SOURCE REVIEW APPROVED

2026-10-05 bounded rereview: GCS3DFFE0BAE7948D7A9EA049B01A6CCE0FA6F0C153AC9847C54450888ADA19B292 та GDT2A26B82CD29D2DCBC7E61E8AA901AB3AB66D2E8D28249054EF6DE33DE41A2B5E match exact files. Попередній blocker CLOSED at source level: ApplyOnce тепер вимагає DivineActionsReady, що включає local CraftingEnabled, CoreReady і remote ServerReady; remote CoreReady включає ServerCoreReady та LocalStateCurrent з world/session/player/8sec freshness. При false немає Apply/Reject/видалення Requested — дія чекає. ClearSettled, ResendReceipt та Reject залишаються перед цим gate; terminal recovery Tick/Receive не отримали нового fresh-readiness обмеження.

AnyUnlocked тепер перевіряє CoreReady + LocalStateCurrent, тому вже отриманий server-core-off не лишає UI активним. No new blockers in this bounded Utility compatibility amendment. Other unchanged file hashes/source caveats below retain scope; Polearms full acceptance remains Combat/network-owned. Root reports126 focused assertions PASS; Utility не запускав ці тести або збірки й не підтверджує LIVE. Final combined builds, source drift check та integration — Gold root. Exact Utility review-document reservations RELEASED; no source write reservation held. Historical initial blocker below superseded by this verdict.

INITIAL VERDICT: BLOCKER / do not integrate as approved by Utility yet. Read-only source review, no builds/runtime/source writes. Gold root owns all candidate/canonical mutations; Utility reserves only own review documents.

Exact reviewed hashes: GoldCraftingService54DA5E06D7BD02AB5A5E516B55CB27B097B27C0DBD90E374BC33F81909B19BA6; GDT B2D2DA0936CC9E09DC4155CF4B2712BC5C4DE79E33AE36015A477BAA618B982A; status D3A7ED794BB65B966FC48C44FD02441BE0DD5BF6432613382E0E9AC6A193F5AF; OwnerSkill C800E0B70A78328E445FEAF4ADF5C4760F728E4AEA45E2A096A6A565475F1E33.

## Required correction

GDT Grant deferred closure ends with ApplyOnce && GoldCraftingService.CraftingEnabled. CraftingEnabled is LOCAL config/global enable only; on a remote client it does not include received ServerReady/ServerCoreReady or snapshot freshness. Repro: local flags stay enabled; an admitted grant is queued; server disables crafting/core and client consumes a newer snapshot (legacy crafting-ready false/core-ready false); closure still calls Apply because CraftingEnabled remains true. Thus known remote disable is ignored at the new-effect boundary, despite CanStart correctly using DivineActionsReady.

Use fresh current server crafting readiness (e.g. DivineActionsReady after source review) ONLY for ApplyOnce/new effect, preserving Requested journal pending/deferred rather than turning disable into rejection/refund. Terminal ClearSettled/ResendReceipt/Rejected settlement must remain independent of fresh readiness. Test queued grant + ready=false with local flags=true, coreReady=false, expired snapshot, enable later, and Applied/Rejected/settled replay while disabled. This concerns a disable snapshot already received, not a claim of instantaneous distributed revocation before notification arrives.

## Other reviewed boundaries

TryGetRecoveryLedger binds native current session/reference+world+SessionWorld+LoadedWorld+IsAvailable; TryGetServerLedger adds CoreEnabled. GDT fresh receive still requires CraftingEnabled (includes core), and deferred station admission revalidates ledger/current actor/eligibility before Reserve. Deferred Grant checks session/world/character before reading/writing receipt. Tick reaches GDT recovery without craft/core enable early-return; MasteryPlugin.Update directly calls Gold.Tick. Prices500/250, masterwork5, Forge same-item+3/required inventory idol and transaction payment sections unchanged in diff.

Idol private Session name/type and ledger _path construction retained. Existing MasterIdolPlacement.GoldWorldReady also checks legacy Enabled and qualified ServerLedger; core/craft off closes fresh idol creation. Its settlement does not route through Gold spending. Existing switch/visual gate Enabled remains crafting-only. No source migration of Idol adapter required for compatibility; future replacement with public accessor is separate.

OwnerSkillAuthority Crafting branches unchanged; new Polearms branch/channel does not replace V2 crafting state. Full Polearms authority acceptance belongs to affected Combat/network reviewers, not this verdict. Shared status now may display exhaustion while Crafting disabled; unchanged Völundr/hammer wording/icon could be misleading for non-craft exhaustion, a presentation caveat for UX owner rather than financial blocker.

No LIVE/reconnect/crash/source-build validation performed by this review. Await root correction+exact hash, then bounded rereview.
