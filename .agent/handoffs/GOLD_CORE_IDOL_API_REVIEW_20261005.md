# GOLD_CORE_IDOL_API_REVIEW_20261005 handoff

## Updated bounded guard verdict 2026-10-05

Current MasterIdolPlacement SHA256 ECB71042920D4B274F5B17E395867CCE1BE61D75FA28E06916F39481514BE71D. Gold-only source review: GoldWorldReady now rejects a previous-session cached ledger using ReferenceEquals(private GoldCraftingService.Session, current ZNet), and rejects a wrong-world ledger using its normalized private _path compared with ConfigPath/ValheimMasteryGold/currentWorldUID.bin. Enabled, native server/world, IsAvailable and unchanged ledger reference are also required. Those private field names/types/path construction match current canonical Gold source. Reflection exceptions return false. No Gold Tick, initialization or writes occur in the adapter.

The previously identified stale cached ledger blocker is CLOSED at SOURCE REVIEW level for the fresh kind1 admission and ServerHold call sites, provided execution stays on the native synchronous main-thread path. The helper returns a boolean then callers read ServerLedger again: it is not a synchronized ledger lease and does not establish cross-thread safety. Do not reuse that shape for async producers or claim all shared consumers are repaired. A future Gold-owned world/session-bound accessor remains the maintainable shared API; this reflection adapter is a temporary private-contract dependency, requiring rereview when Gold lifecycle/fields/path change.

Existing kind1 issued-permit replay still bypasses fresh eligibility but verifies the original live session/player/character/RPC permit; ServerHold now checks current Gold world readiness at execution. Skill eligibility remains frozen at original issue. Recovery/settlement remains independent of new eligibility as previously reviewed. No change to the no-Favor or FAILED/UNKNOWN character-save verdict below.

This is narrow source acceptance, NOT compile/QC/runtime or full native/Workshop/payment acceptance. Owner must verify missing/wrong private fields, unavailable/disabled ledger, old-session ledger before first Gold Tick, wrong-world path, valid current ledger, issued permit execution and recovery after disable. No tests/builds/launch/deploy performed by Gold for this rereview. Other candidate files were not recertified. Earlier verdict/hash sections below are historical where superseded by this update.

2026-10-05; ValheimMastery1.4.123. COMPLETE / SOURCE REVIEW COMPLETE; review documents RELEASED. Root read-only, no candidate/canonical modifications, builds, production save/config/runtime/deployment or Utility chat messages. Utility requested owner-readable result; root messages authorization remains Combat100/UX coordination only.

## Verdict

Gold API call shape and no-Favor contract are compatible. **Fresh-admission authority integration is not fully approved until current Gold ledger world/session readiness is explicitly bound or its ordering guarantee is proved.** This is narrow Gold source review, not the separate native placement/Workshop/resource/cancellation/world persistence acceptance.

Reviewed exact snapshot hashes:
- MasterIdolPlacement174AE7F7621A9F948448A09D8392E7D2CC8F229792ACF6E09694359430FB4C60.
- MasterIdolJournal00377FC8BE6C2D63192533B46631A814097CC6C37FE3AA23F196081EBD7F3438.
- MasterIdolAdmissionStoreFB2CE5F042170CDDAC5317EC32C901AA03028DDB59D29E8C8A8D5AA9C1092193.
- MasterIdolWorldRegistry98032D4887E812E38CC7D0C88CDD879692564AF09A23A4A923556232EF975C7B.
Source root validation/utility100-idol-framework-20261005/snapshot/src-modern/. Changed hashes need updated bounded review; no source restoration from this snapshot.

Concurrent owner drift at final readback: Placement changed to1035EAA613BD5D4EAFD77240B983656DD7F276D868B19AEC0F67F310E93D57FE, other3 fingerprints unchanged. Root reread Gold enabled/unlock/current100 predicates, existing permit/confirmation branches and journal save call boundaries in that new source: Gold findings/caveats below still apply. This bounded recheck does not certify other changed native/Workshop lines or full consumer integration. No writes by Gold to either candidate version.

## Gold unlock/readiness

Fresh Receive(kind1) resolves available WorkshopActor from authenticated RPC, compares requested PlayerId to actor, packet world to current net world; checks GoldCraftingService.Enabled, ServerLedger.IsAvailable, Get(player).Unlocked and live OwnerSkillAuthority.Has(Crafting100) on RPC/character/player identity (local branch uses local player). Get(...).Unlocked is canonical Völundr alias, appropriate for Crafting100, not unlock of arbitrary patron. Reads do not debit/grant Favor; own admissionStore.Reserve/Issue is world-piece permit and must remain separate from Gold ReserveAction/cooldown.

Required readiness proof/guard: ServerLedger is currently a raw cached Ledger accessor; IsAvailable is loaded&&!faulted, not a world assertion. Gold Tick replaces the ledger on ZNet-instance transition and loads a current-world file. Idol Receive independently validates current net packet world but does not verify that cached Gold ledger belongs to that same current session/world at the moment of a fresh request. Explicitly guard current Gold service session/world/initialized ledger before granting, or document/test actual lifecycle ordering that makes a stale ledger impossible for every RPC/local path. No observed live exploit claimed; this is a missing source-level cross-service readiness invariant, relevant to required world UID/reconnect regression. Do not blindly call full Gold.Tick from inside RPC as a workaround. Gold-owned CoreReady/world accessor separation is the natural shared integration point; reserve/review any shared change separately.

ServerHold relies on a matching previously issued live permit plus current enabled/ledger/unlock checks; it does not repeat Has(Crafting100). Document whether that boundary is permit execution (eligibility frozen at issue) or fresh skill eligibility. Do not describe every boundary as independently checking100. Existing kind3 settlement precedes fresh Gold eligibility and confirms actual matching ZDO; avoiding new eligibility checks for recovery of an admitted operation is consistent with receipt semantics. It does not prove payment/world durability.

## Character save helper

GoldCharacterSave.Persist requires actual Player.m_localPlayer and available Game profile, rejects nested saving and requires observed synchronous FileWriter completion; remote dedicated-server player returns false. Reuse for the local owner journal is valid if canonical GoldCharacterWriterPatch is registered. It is not server proof of a remote character disk save.

MasterIdolJournal.Save restores previous live player.m_customData value and receipt Phase/Output on Persist(false): correct memory compensation. False is FAILED/UNKNOWN persistence outcome, not proof that no file/profile buffer changed. SavePlayerData/profile.Save may already have advanced serialized profile state or a partial writer before failure. No guarantee of restoring disk/native cached profile, resource inventory, created world object or admission store follows from those assignments. Current placement paths retain uncertainty/hold on relevant failures; preserve that fail-closed rule. Do not refund/resend a new token/release issued hold on Save(false) alone.

Persist(true) only establishes the local character writer result. It cannot atomically pair paid character resources/journal with world ZDO/admission file saves. Receipt applied3+output is not by itself native world-save proof; actual ZDO identity/token/type/creator/point confirmation and durable world admission remain independently reviewed requirements. No Gold ledger/receipt schema change necessary for this zero-Favor framework.

## Exact followup

Utility/root: retain own Native/Workshop reviews and final source tests; add/verify current-world Gold readiness invariant before fresh admission integration. Clarify issued permit vs100 recheck at ServerHold. Regression: disabled Crafting/global settings, missing/invalid Gold ledger, wrong player/RPC/world/character, session transition before first Gold tick, admitted receipt recovery after disable, writer false/nested/partial save without refund/release, both save orders/reconnect/restart. Tests here NOT RUN; Native/Workshop reviewer results not independently certified by Gold. No live/crash acceptance claim.

Minimum next context: this handoff, Utility task,4candidate files, current Gold service readiness/save helper. No gameplay powers/rates or remote raw amount RPC inferred.
