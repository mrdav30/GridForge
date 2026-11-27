# World Observation Lifetimes Plan

**Opened:** 2026-11-26

## Purpose And Status

**Status: planned; source review only. No implementation, tests or measurements
have been performed for this plan.**

Make world-local observation follow an explicit `GridWorld` lifetime. The first
production consumer is `GridDiagnosticSession`, which currently combines world
instance events with process-wide obstacle and occupant subscriptions. Preserve
intentional global observation while removing the need for that consumer to
remain rooted by static publishers.

The audit baseline is GridForge `ea52599521f3b86a80d6c3c5d1f2dacb36fc6c82`.
Refresh affected callers and source contracts before implementation. This is
separate from the corrected world-owned occupant registry; no registry
regression or new runtime defect is claimed. The companion
[partition diagnostics plan](partition-diagnostics-plan.md) may reuse the
selected world-local observation boundary.

## Existing Owners And Evidence

| Owner                                                                                                   | Current behavior and consequence                                                                                                                                                                                     |
| ------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/GridForge/Grids/Managers/GridWorld.cs`                                                             | Owns grid lifecycle events, `OnChangeCommitted`, reset and disposal. Ordinary reset retains subscriptions; deactivation clears instance handlers.                                                                    |
| `Grids/Managers/GridOccupantManager.cs`, `Grids/Managers/GridObstacleManager.cs`, `Blockers/Blocker.cs` | Publish static typed notifications across all worlds. Subscribers remain strongly referenced until explicitly removed.                                                                                               |
| `Diagnostics/GridDiagnosticSession.cs`                                                                  | Retains its world and subscribes instance methods to both world and static manager events. `Dispose` unsubscribes and clears dirty storage. Forgetting it can retain the session and world through static delegates. |
| `Grids/Support/GridEventKind.cs`, `GridEventInfo.cs`                                                    | Committed changes include obstacle snapshots and topology/lifecycle changes, but do not supply occupant notifications. One existing event cannot silently replace all current subscriptions.                         |
| GridForge-Unity `Build/Base/Runtime/Utility/Debugging/GridDebugger.cs`                                  | Already disposes sessions on disable, destruction and world replacement. Its behavior is a real lifecycle consumer to preserve, not evidence that every host disposes correctly.                                     |

Source paths without a repository prefix are under `src/GridForge`. Keep Unity
runtime edits at its authoritative `Assets/Packages/Build/Base` owner if needed;
no adapter change has been made or verified by this plan.

## Scope And Dispositions

**Selected direction:** observe one world through that world. Prefer extending
the existing owner with the missing typed occupant notifications and reuse its
existing obstacle/lifecycle publication where the semantics match. Do not add a
parallel process-wide routing registry or a wrapper service with duplicate
state.

Preserve existing static manager/blocker events as explicitly global APIs during
the first slice. Their subscribers still own unsubscription. Removing or
deprecating them is a separate public API decision requiring caller evidence;
the current proposal does not silently change cross-world delivery.

Disposable subscription tokens are a convenience, not a cure for forgotten
disposal. The initial consumer already has `IDisposable`; do not build a generic
token framework merely to wrap its existing lifetime. Reopen token design only
for a concrete host needing composite subscription ownership.

Weak-event delivery is not selected. Garbage-collection timing must not decide
authoritative event delivery. World ownership, explicit teardown and captured
notification semantics provide the relevant boundary.

No simulation clock, scheduler, global reset API, registry rewrite or event bus
is part of this work. Diagnostic-only changes must not increment obstacle or
navigation-proof stamps to manufacture a convenient notification.

## First Production Slice

1. Trace add/remove/clear publication from the mutation owner through manager,
   voxel, world and diagnostic callbacks. Record existing callback order,
   immutable snapshots, exception isolation and reentrant teardown behavior.
2. Select the smallest missing world-local occupant surface and an existing
   world obstacle surface. Reuse `OccupantEventInfo`/`GridEventInfo` where their
   contracts fit; exact public names remain subject to this source audit.
3. Route `GridDiagnosticSession` through these world-owned sources and preserve
   its sorted/coalesced dirty output, reset marker and stale-identity rejection.
   Remove its obsolete static subscriptions once their last need is gone.
4. Prove the path with one world, two independent worlds, occupant add/remove,
   obstacle add/remove/clear and session teardown. Review this working slice
   before adding conveniences or migrating additional callers.

New world-local delivery must not reorder the existing static/voxel callback
sequence or turn notification failure into mutation rollback. Document its
placement and whether an already captured invocation can run after unsubscribe.
Use captured world ownership and value event data; do not rediscover a world
from a pooled voxel after callbacks can remove or replace it.

## Lifetime And Ordering Contract

- Ordinary `Reset()` keeps the world active and subscriptions usable. Pending
  cell changes are superseded by the reset marker, and old grid/voxel identities
  cannot identify new allocations after reset.
- Deactivation/disposal clears the world's owned handlers. A forgotten session
  must not remain rooted by a process-wide source after this migration. Explicit
  session disposal remains necessary to release its buffers promptly while a
  live publisher remains reachable.
- Disposal and unsubscribe are idempotent. Specify external synchronization with
  disposal; do not imply that an existing delegate snapshot can be revoked.
- A callback can dispose an observer, remove a grid, reset/deactivate the world,
  or throw. Later captured callbacks must not resurrect observation or resolve
  stale pooled state. Preserve existing logging and failure isolation.
- Notifications remain synchronous mutation observations; they do not schedule
  simulation, assign wall-clock order or introduce a second committed sequence.
- A global observer may intentionally outlive one world. Disposing that world
  must not clear unrelated global subscribers or notifications for other worlds.

## Verification And Cost

Extend the nearest existing behavior tests:

- `tests/GridForge.Tests/Diagnostics/GridDiagnosticSessionTests.cs`: ordinary
  reset, world isolation, sorting/coalescing, disposal during an earlier
  callback, sparse changes and repeated disposal.
- Existing `GridWorld`, occupant, obstacle and blocker tests: callback ordering,
  mutation success despite observer failure, reset/deactivation and captured
  event identities. Preserve current static event behavior explicitly.
- Add bounded reachability controls for an undisposed session after all external
  world/session references are dropped, after deactivation, and for explicit
  session disposal while the world stays alive. Isolate roots in helper scopes;
  do not infer retention from one unreliable GC observation or use production
  finalizers as a remedy.
- If Unity is affected, run its supported disable/re-enable, world-replacement
  and destruction EditMode scenarios; inspect result XML rather than launcher
  success alone. No current adapter verification result is claimed here.

Measure the affected occupant/obstacle and diagnostic paths before and after:
subscriber count zero, one and several; two worlds; callback failures; burst
mutation and reset. Charge delegate storage, invocation allocations and retained
session buffers. Reuse existing diagnostics/occupancy/blocker benchmarks and
allocation tests. A local dispatch result is not a simulation-frame gain.

Build `Release` and `ReleaseLean` for `netstandard2.1` and `net10.0`; run
focused tests before both full net10.0 suites. Collect fresh coverage with the
existing `coverlet.runsettings` and run
`.assets/scripts/assert-full-coverage.ps1` for exact 100% reported line, branch
and executable-method coverage. Do not add exclusions. Validate affected
consumers under their own repository gates.

Use `-p:UseLocalLsfStack=true -p:DisableTransitiveProjectReferences=true -m:1`
when validating coordinated sources. One coordinator owns shared-output builds,
coverage and timings. Record exact revisions, configurations, commands and
baseline versus introduced failures; verify loaded benchmark dependencies.

## Phases And Closeout

1. **Bound the contract:** audit publication/callers, choose new delivery
   placement, reproduce the static-root chain with a disposable probe, and
   capture baseline behavior/cost. Retain explicit disposal if the proposed
   world-local change cannot preserve required delivery simply.
2. **Implement one consumer:** add only missing world-local publication, migrate
   `GridDiagnosticSession`, remove superseded subscriptions and obtain
   independent `ponytail:ponytail-review` review of the first working boundary.
3. **Verify and close:** finish lifecycle/callback/reachability and cost checks,
   run both package variants/targets and required coverage, and independently
   review final source, net runtime growth, callers and deletion opportunities.
   Update XML and the diagnostic/lifecycle guides for actual adopted behavior.

Keep concise decisions and evidence here. Implementation and verification close
this feature; package release readiness is separate. If upstream source changes
are required, validate provider before consumer and remind the maintainer to
align CI dependency pins after upstream lands. Leave work unstaged/uncommitted.
