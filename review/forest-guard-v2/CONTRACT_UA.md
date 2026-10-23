# Stage A — exact provider/Cargo/runtime contract for final agreement

Proposal incorporates Root decisions23Oct; no implementation yet. One detailed private StageA result: STAGE_A_D1_UA.md (contains private preflight identities, do not publish raw). This API artifact contains no actor/inventory contents. Primary Utility; Root integration, Cargo affected settlement, Core protocol/presentation. Baselines Client42C/gateDFD182, Server0CD3/gateC773, not stale Dev.

## Single provider, no second Active/life database

Existing `MasterIdolForestGuardLifecycle.CanOperate(ZDO body)` remains the one pure, bounded policy facade. V2 grants only authenticated current server Desired=ACTIVE +exact logical/native identity/positive native health/current native-owner applied revision. No caller==owner restriction, no camera/cue/assets/native phase5/completion term. Unknown denies NEW operations; existing cleanup/reclaim/close/cancel/consent/done remains legal.

Proposed exported signatures inside existing classes:

```csharp
// Utility provider: pure current proof reads, no Cargo calls/native writes.
internal static bool TryReadCargoQuiesce(ZDO body, out ForestGuardDrainWitness expected);
internal static ForestGuardDrainIntent StillCurrentCargoQuiesce(in ForestGuardDrainWitness expected);
// ForestGuardDrainIntent: Unknown, CurrentDormant, Superseded.

// Cargo class: authoritative existing settlement core only.
internal static ResidentCargoDrainStatus MasterIdolResidentCargo.TryDrainForDormancy(
    in ForestGuardDrainWitness expected, out ResidentCargoDrainReason reason);
// ResidentCargoDrainStatus: Pending, Ready, Rejected.
```

`ForestGuardDrainWitness` is an immutable EPHEMERAL tuple, not a cache/store/protocol of its own:

- Existing accepted State row: UID/family/Born/Nonce/logical home/current owner+OwnerRevision/intent revision/cycle/attempt and V2 Desired=DORMANT.
- Existing outer proof: exact ZNet session, ZDOMan, world, load generation, accepted epoch.
- Exact current persistent body **ZDO reference** (not Character), native home string and captured linked Cargo ZDOID.

No duplicated item/HP/name snapshot or new ACK/save revision. Native body Character LOADED/nativephase2/body-owner drain ACK are NOT entry prerequisites. Known current ZDO+Registry identity can be drained even when no local Scene body exists, using existing Cargo owner/index/receipts. None Cargo link is not absence proof: existing complete index must exclude a linked/ambiguous store. Body owner may differ from actual Cargo owner/opener.

TryRead succeeds only current authoritative DORMANT intent/exact living logical slot/native ZDO and NEW action denial. StillCurrent returns Unknown when proof unavailable, CurrentDormant for the SAME full witness, Superseded for known changed world/generation/intent/revision/identity/home/link/owner revision. No state is advanced by these reads.

Cargo: Pending for unavailable proof/index/owner/lease/saved ACK/busy/loading/in-flight work; Ready only exact existing settlement completed and all current witness/index/link checks repeat after callbacks; Rejected ends only a known stale/conflicting attempt, never an entity or items. ON/new desired revision invalidates old dormant witness; already admitted atomic commit and cleanup are not interrupted. Current-owner native DORMANT application occurs only after Ready, or waits for native availability. Ready is not proof of saved body/native presence.

Factor exact settlement core from `MasterIdolResidentCargoStore.BeforeRemoval`, preserving existing debit/lease/Pending/Executing/container.Save/reclaim/saved-ACK protections. Legacy BeforeRemoval keeps retirement for actual removal/death/replay. V2 neutral entry does NOT set Retired3s or require V1 phase2/world/cycle wrapper. No extra body-save/checkpoint, journal or crash atomicity requirement.

## Runtime / presentation / wire

Root adopted smallest existing claimed native hide/restore backend, retaining narrowly necessary SetActive and own removed-reference claims. No new IsEnemy/friend-query/CanSense framework. Inactive existing Character remains LOADED; absence is not death. Scope≤4 known exact Registry bodies; preserve independent persistent Cargo. Full/partial Awake must be safe; no suspension before required native cache readiness. Restore only actually claimed references/flags under exact current identity/world/generation/intent, never on Destroy/world exit. Both native-role and owner-transfer/load/claim-restore checks required before candidate.

Server Desired=ACTIVE/DORMANT; one V2 controller/owner writer. Registry ALIVE/confirmedDEAD independent; native LOADED/UNLOADED/UNRESOLVED independent, contradictions RECOVERY reason only. Old V1 controller/actions/ingress/cue-completion writes not registered in V2. UID alias only existing unique native-load witness preserving Born/Nonce/name/home, never copied markers/family-only adoption.

Once ACTIVE applied, BirthPresentation may cancel only owned cosmetics, never suppress original actor renderers/collider/physics/movement or delay application/admission. Camera/assets/10s cue failure cannot gate birth/ACTIVE. Retain physical ground/collision/path/zone authority. Existing legacy gameplay birth-hold facade is decoupled from cosmetics; no social/work/healing algorithm rewrite.

Exact Root cohort: `1.4.123+rpc-20261023-forest-guard-v2-v14`; ingress `VM_BF_LifecycleAsk_v2`, `VM_BF_LifecycleSnapshot_v2`, `VM_BF_LifecycleOwner_v2`, `VM_BF_LifecycleOwnerAck_v2`; semantic codec2. Root sole isolated NetworkSync edit. Both candidate roles same wire; V13 rejects, no dual registration. Existing Registry schema5 and native phase1/2/3 receipt meanings unchanged for rollback. Unknown old records untouched. No deploy/Dev merge/Release authorized; StageB waits Root+affected final agreement.
