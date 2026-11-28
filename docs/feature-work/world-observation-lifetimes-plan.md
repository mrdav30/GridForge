# World Observation Lifetimes Plan

**Opened:** 2026-11-26

## Purpose and status

**Reviewed 2026-11-28 on develop at `f9500aa`.** The runtime counter work is
complete. At permanent change-sequence exhaustion, the host can rebuild into a
fresh world and bind fresh observations. A successful ordinary reset advances
the existing world's sequence. This plan covers how a diagnostic session
observes one world until the host releases or replaces it. The observation
migration is still pending. [Counter closeout](done/runtime-counter-lifetimes-plan.md) ·
[World lifetime contract](../api/guides/World-Changes-and-Composition.md#when-the-change-sequence-is-exhausted)

## What still needs to change

`GridDiagnosticSession` subscribes to four instance events on its world and
five static manager events (obstacle add, remove and clear, occupant add and
remove). A static publisher holds the session, which holds the world and its
dirty buffers. Filtering by world token prevents foreign records from entering
the output, but every session still receives those global callbacks. Explicit
session disposal unsubscribes correctly. World deactivation clears the world's
own handlers and leaves the five static subscriptions intact.
[Session subscriptions](../../src/GridForge/Diagnostics/GridDiagnosticSession.cs) ·
[World teardown](../../src/GridForge/Grids/Managers/GridWorld.cs)

On the committed local LSF Release build, all 12 forgotten active sessions
and their 12 worlds survived six forced collection rounds. All 12 pairs also
survived after explicit world deactivation. Disposing the sessions through
weak references and repeating the collections released every pair in both
cases. With a deliberately live world, all 12 explicitly disposed sessions
were collected. An instance-only event cycle with an observer pointing back
to the world also left zero survivors. Each scenario ran in its own .NET
10.0.10 process with non-inlined setup and explicit live-world controls.

### Obstacle delivery has two observable stages

An obstacle addition, removal or clear on a live voxel produces an exact
`ObstacleChanged` cell record, then a separate `GridChanged` record for the
owning grid. For these changes the world invokes the static manager handlers,
eligible voxel-local subscribers, the world grid event, then
`OnChangeCommitted`. The session currently records the cell during static
delivery and the grid during `OnActiveGridChange`.
[Exact dispatch](../../src/GridForge/Grids/Managers/GridObstacleManager.cs) ·
[World publication](../../src/GridForge/Grids/Managers/GridWorld.cs)

Two one-cell probes illustrate why both stages matter.

- A voxel callback read and cleared the session during `OnObstacleAdded`. It
  saw only `ObstacleChanged`. After publication the session held only
  `GridChanged`.
- A voxel callback removed its grid during the same event. After return the
  session held both the old generation's `ObstacleChanged` and `GridRemoved`.
  Moving cell capture to the committed callback loses the former when the
  generation check fails. Conversely, recording every teardown clear without
  that check would introduce cell changes for grids already detached.

Keep the existing world grid events for broad, sparse and lifecycle records.
Deliver exact obstacle dirtiness at the existing exact publication stage
before voxel callbacks. The implementation needs to choose and document its
position relative to static subscribers. Today a static callback can see the
session before or after it records the cell depending on subscription order.
Moving the session to a separate multicast delegate makes that ordering a
compatibility decision. Pin callback-time reads, clears and disposal before
changing publishers.

### Occupant delivery needs the mutation's owner and original recipients

Occupant add/remove changes a cell without advancing the world's committed
sequence. On successful mutation the manager invokes its static callbacks,
then looks up the target voxel's current subscribers. Bulk grid teardown
clears registrations through its lifecycle paths. A world-local session
needs successful occupant changes and the existing grid/reset invalidations.
[Occupant manager](../../src/GridForge/Grids/Managers/GridOccupantManager.cs) ·
[Occupant event payload](../../src/GridForge/Grids/Support/OccupantEventInfo.cs)

The following defects affect which world or voxel should receive a notification.

- **Physical ownership.** With one cell in each of two worlds, passing world
  B's voxel to world A's grid accepted an occupant addition. A's registry
  held the ticket, B's voxel count increased, and the event identified B.
  Validate the exact physical voxel against the receiver before mutation,
  including the direct remove overload. The obstacle manager already checks
  the owning generation and the voxel instance resolved at the address.
  [GF-Issue-018](issue-tracker.md#gf-issue-018)
- **Pooled callback recipients.** A static occupant callback removed a grid,
  created a replacement and subscribed a handler on the rerented voxel. The
  new handler received the old event, for both add and remove. Removing and
  rematerializing a sparse voxel at the same address during an occupant
  removal also reproduced it with the *same* `WorldVoxelIndex` and object
  reference. During obstacle removal and clear at the same sparse address,
  the replacement handler received sequence 4 after the world had reached
  sequence 6. Capture the original local recipients before invoking user code
  and preserve the existing suppression of retired targets.
  [GF-Issue-019](issue-tracker.md#gf-issue-019)

## First implementation slice

1. Turn the ownership and recycled-subscriber probes into failing regressions.
   Capture the owning world, exact address, occupant reference, ticket and
   post-mutation count while the registration is stable. Publish outside the
   grid and registry locks. Preserve ordering among existing manager and
   eligible voxel subscribers, their exception isolation and the distinction
   between successful mutation and notification failure.
2. Add the smallest internal world-owned notification surface that supplies
   the session's exact cell changes. Reuse the existing mutation payloads
   and publication stages. Keep intentional public static manager and blocker
   events available to cross-world consumers. Keep occupant updates unstamped.
3. Move the five static subscriptions off `GridDiagnosticSession`. Keep
   sorted, coalesced cell and grid output, pre-teardown `OnReset`, the pending
   reset marker and stale-generation rejection. Clear the new world-owned
   handlers on deactivation. Explicit session disposal must still clear
   pending changes and stop observation while the world stays alive.
4. Independently review the first working boundary before expanding.
   GridForge-Unity's `GridDebugger` already disposes on disable, destruction
   and world replacement. Its existing session API suffices for this slice.
   The [partition diagnostics plan](partition-diagnostics-plan.md) can reuse
   the boundary if its first consumer needs it.

## Verification still due

All 29 existing `GridDiagnosticSessionTests` and
`GridWorldTeardownExhaustionTests` passed in local-stack Release on this
revision. The probes above are additional baseline evidence. The table lists
the checks to run against the implementation.

| Concern | Existing evidence | Remaining acceptance check |
| --- | --- | --- |
| Dirty output | [Session tests](../../tests/GridForge.Tests/Diagnostics/GridDiagnosticSessionTests.cs) cover sorted occupant cells, obstacle cell plus grid, sparse ranges, reset, world filtering and repeated disposal. | Preserve both obstacle stages, including acknowledgement inside a callback, removal before broad delivery and detached clears. Check that one world's occupant mutations do not dispatch to another world's session. |
| Reset and handoff | [Teardown regressions](../../tests/GridForge.Tests/Grids/GridWorld.TeardownExhaustion.Tests.cs) cover rejected ordinary reset, an admitted reset's reserved marker and terminal deactivation without a final commit. | A rejected reset leaves session dirt intact. An admitted reset supersedes earlier dirt via `OnReset`. Before acknowledgement, check cell suppression and new grid lifecycle records separately. After acknowledgement, confirm capture resumes. Hold an old session across terminal teardown and bind a new one to the replacement world. |
| Callback lifetime | [Manager tests](../../tests/GridForge.Tests/Grids/ManagerCoverage.Tests.cs) cover captured occupant values and subscriber failure isolation. | Exercise direct occupant add/remove, wrong-world and same-world foreign voxels, same-address reuse, an earlier handler disposing the session, and a throwing subscriber followed by a surviving one. Check that occupant mutation leaves `ChangeSequence` unchanged. |
| Reachability | Forgotten sessions and their worlds survived the baseline probe even after deactivation. | Repeat the controls after migration. Dropping all external references should collect both objects. A deliberately live deactivated world should release its session. A live active publisher can retain its observer until session disposal. |
| Cost | [Occupant wave](../../tests/GridForge.Benchmarks/Memory/OccupantWaveBenchmarks.cs) exercises registration. [Grid diagnostics](../../tests/GridForge.Benchmarks/Memory/GridDiagnosticsBenchmarks.cs) exercises cell queries. | Capture a pre-change baseline and repeat on the migrated path for zero, one and several sessions, including sessions in another world. Measure warm allocations and time per occupant/obstacle mutation and per dirty read/clear. Include burst, reset and subscriber failure cases. |

Use the local develop stack with `-p:UseLocalLsfStack=true`,
`-p:DisableTransitiveProjectReferences=true`, `-p:BuildInParallel=false` and
`-m:1` for restore, build and test. Build Release and ReleaseLean for
`netstandard2.1` and `net10.0`, run both full net10.0 suites and the existing
100% reported line, branch and executable-method coverage gates. Update XML
and the [diagnostics guide](../api/guides/Grid-Diagnostics-and-Geometry.md)
for the adopted callback contract. Verify Unity's lifecycle scenarios if the
adapter changes. Record measured results and final independent review here.
