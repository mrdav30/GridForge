# Runtime Counter Lifetimes And Exhaustion Plan

**Opened:** 2026-11-26

## Purpose And Status

**Completed:** 2026-11-27. Local develop source verification resolved GF-Issue-015.
This work protects retained identities and observations in long-running worlds.
GridForge owns allocation and mutation order. Chronicler.Timing owns elapsed
time. The [world observation plan](world-observation-lifetimes-plan.md) covers
the lifetime of diagnostic event subscriptions.

## Confirmed v10.2.0 findings

The checks used GridForge develop `c5d0f809ac90ed058453f766068ab6dded5d249e`
(v10.2.0). In a disposable process, seed both private world fields
`_changeSequence` and `_publishedChangeSequence` at `ulong.MaxValue - 1`.
Seeding only the first would simulate an outstanding publication and test a
different contract. [GF-Issue-015](../issue-tracker.md#gf-issue-015) records the
verified resolution.

| Operation | v10.2.0 observation | Ordinary control |
| --- | --- | --- |
| Register an empty one-cell sparse grid, capture a bounded snapshot, then call `TryAddVoxel`. | Registration consumed the final stamp. Addition threw `OverflowException`, yet a fresh query changed from zero to one voxel. No change event fired. Both sequences stayed at the maximum and checking the held proof returned `Completed`. | Addition returned true, one event fired, the old proof at 1 returned `Stale`, and the new query returned one voxel at 2. |
| Register one dense grid with one obstructed voxel. Seed the fields after setup, then call `Reset()`. | The reset had one remaining stamp for two notifications. It threw, the resident grid count dropped to zero and the observer received only `OnReset`. Checking the world's current sequence returned `WorldNotReady`. | The observer received `OnReset`, `ObstaclesCleared` and `WorldReset` in order. The current bounded snapshot returned `Completed`. |
| Dispose the exhausted sparse world. | `Dispose()` threw and left `IsActive == true` with zero resident grids. Teardown could not complete through the ordinary reset path. | Ordinary disposal deactivates the world, clears the token and handlers, and releases its lock. |

These observations were reproduced with the Release local source graph. An
independent ReleaseLean package-mode probe corroborated the exhaustion failure.
On the unfixed Release source build, 12 of 13 focused regression cases failed.
The world-local allocation generation exhaustion control passed.

## Counter owners and decisions

| Counter and owner | Lifetime, advance and consumers | Disposition |
| --- | --- | --- |
| `GridWorld.SpawnToken` | The world constructor issues a process-wide positive `long`. Ordinary reset preserves it, deactivation clears it, and a new world gets its own token. World-stamped voxel indices, cursors, navigation and diagnostics use it. | Retain checked issuance. |
| `VoxelGrid.SpawnToken` | Each world owns a positive `long` generation counter, incremented under the world write/commit locks before a slot is published. Pool release clears the grid's copy. `WorldVoxelIndex` includes world token, grid slot, grid generation and local address. Rebuilding the same configuration at the same slot issues a new generation. | Retain checked issuance across ordinary reset and slot reuse. |
| `ObstacleToken` | World allocation uses a separate process-wide positive `long`. Zero is invalid. A blocker obtains its token before applying coverage. Individual voxel attachments use that identity for stacking and removal. | Retain process-wide issuance. A token can be consumed even when no attachment is made. |
| `OccupantTicket.Generation` | `ScanCell.AddOccupant` issues a process-wide positive `long` under the grid occupant lock before renting or populating a bucket. The world registry and scan-cell lookup/removal validate the address, live slot and generation. A default ticket is invalid. | Retain checked issuance across reset, pooled buckets, shared occupant IDs and different worlds. |
| `GridWorld.Version` | A world starts at 1. Grid add, removal, reset and a manual significant increment advance the unsigned 32-bit structural revision under the world write lock. It cycles through zero. Only the manual operation omits a committed change. | Retain the public type and wrap behavior. Manual-only full cycles can repeat a held revision. |
| `VoxelGrid.Version` and `Voxel.CachedGridVersion` | Active grid revisions run from 1 through `uint.MaxValue`, then return to 1. Pool release sets the grid copy to zero. Neighbor links, sparse storage, obstacles and explicit bumps advance it. Voxels and event payloads carry copies. | Retain a per-grid revision. Handle validity uses allocation tokens. Exact mutation proofs additionally bind a committed sequence. |
| `GridWorld.ChangeSequence`, `_publishedChangeSequence`, `GridChangeStamp`, `VoxelGrid.LastChangeSequence` | A new world starts at zero. Under the change lock each committed grid, obstacle or sparse change receives a nonzero `ulong` sequence. The same value identifies the cause. The field on the affected grid follows its event, and the published sequence advances after synchronous delivery. Ordinary reset adds a commit. Returning a grid to its pool clears its local copy. | GridForge now rejects an unstampable primitive before mutation and preserves terminal teardown. Preserve strict increasing order, sequence zero before the first commit, normal no-op results and callback order. |
| SwiftCollections slot map and traversal state | The provider's private `uint` slot-map version guards live enumeration. Its integer slots are reusable. GridForge supplies independent generation checks on retained handles. Bounded cursor ordinals and per-call budgets describe finite traversal within a captured revision. | Keep existing provider and cursor types. No inspected retained proof uses the slot-map version as an identity. |

The four identity issuers share
[RuntimeIdentityAllocator](../../../src/GridForge/Support/RuntimeIdentityAllocator.cs).
It reads the current value and issues its successor with compare/exchange.
Negative state and `long.MaxValue` reject before writing. The boundary checks
used the release assembly in a separate process. Starting at `long.MaxValue - 1`,
world construction, grid registration, obstacle allocation and occupant
registration each issued the maximum once, then threw `InvalidOperationException`.
The rejected grid add left one resident grid at sequence 1. Ordinary reset
retained the world token, advanced to sequence 2 and left the generation
allocator exhausted. The rejected occupant had no registered ticket and the
voxel still contained exactly one occupant. Same-slot replacement after reset
rejected both the old address and old registration. Existing negative-state,
pool reuse and default checks live in the
[world](../../../tests/GridForge.Tests/Grids/GridWorld.Tests.cs),
[scan-cell](../../../tests/GridForge.Tests/Grids/ScanCell.Tests.cs) and
[grid](../../../tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs) tests.

## Revision and proof consumers

A one-cell grid's covered-address cursor began at world version 2 and sequence
1. One public `IncrementGridVersion(index, significant: true)` yielded (3, 1)
and a different run stamp. Advancing only the private world-version field to
`uint.MaxValue`, then performing three public significant increments, yielded
(2, 1) again. A fresh run stamp equaled the original and the cursor's freshness
predicate accepted the original. The fixture's geometry stayed fixed. This
bounded experiment establishes a manual full-cycle alias. It neither requires
billions of mutations nor demonstrates incorrect geometry. No call to this
manual API was found in the inspected Trailblazer, Gravitas or Unity runtime
sources. A host requiring permanent invalidation from manual bumps needs a
distinct contract. No production mutation-rate evidence supports a time-to-wrap
forecast.

- GridForge's [covered-address cursor](../../../src/GridForge/Grids/Managers/GridWorld.CoveredAddresses.cs)
  checks world token, world version and sequence. It validates each selected
  grid generation and last change sequence. Boundary-contact cursors use the
  same world guards and check affected grid stamps. The
  [bounded-voxel proof](../../../src/GridForge/Grids/Managers/GridWorld.BoundedVoxels.cs)
  checks the owning world is ready and the supplied sequence still matches.
  This last check exposed the sparse mutation defect directly.
- Trailblazer develop `ac2564bc9b2c094c7ad30880f929bb9a7fdb4875` subscribes
  to committed changes. `NavigationPlanarRouteWork.ResolveSnapshotStatus` and
  `TrailblazerPathingService.ResolvePlanarSnapshotStatus` compare the captured
  graph reference plus world token, version and sequence. Navigator2D's
  serialized guidance restoration also compares clock lifetime and frame
  around resolution of the current grid. Clock lifetime belongs to that host.
- Gravitas develop `7702d822c94cbeab43f9ebd9676b9a3e3c7cf98a` compares a
  cached world revision in `GravitasCollisionService.QueryPlanarColliderCandidates`.
  It first calls `GravitasWorldContext.RefreshQueryPartitions`. The context
  subscribes to committed changes, tracks their sequence and rebuilds physical
  partitions for changed topology. Scalar wrap alone did not establish stale
  collision results in this audit.
- GridForge-Unity main `9d26685bac2928a4bc27a49b7b75a78028ebfb04` saves authored
  configurations and sparse addresses in `GridConfigurationSaver`. Loading
  uses `TryAddGrid`, and `GridDebugger` resolves diagnostic cells against
  its current world and replaces its session on world changes. This was a
  source inspection of the adapter's `Build/Base` owner.

## Restore and validation scope

A serialized grid configuration can reproduce geometry. Registration issues
fresh runtime generations under its owning world. GridForge exposes no
live-world restore of its private allocation high-water marks or committed
sequence. A persisted observation therefore has to rebind to the current
world and grid. The existing
[runtime identity contract](2026-07-17-runtime-identity-hardening-plan.md)
and the downstream restore checks above preserve that distinction. Independent
branches of an application rollback require separate host ownership of their
observations. This audit covers public GridForge registration and rebind paths.

The v10.2.0 local source graph used Chronicler
`0281469ecc40e65a05867f473bc10f030e7f69f8`, FixedMathSharp
`8fdf889322f1bb82501c2afec63d6bb2f304ace2` and SwiftCollections
`4d22e574c8de08b944b6796027c9f253ddde91a7`. Develop source validation
selects `UseLocalLsfStack=true`, globally passes
`DisableTransitiveProjectReferences=true` to restore/build/test, and uses
serial builds (`-m:1`). CI uses its pinned sibling revisions on develop.
Package mode remains the main/release workflow. No public width, serialized
format or upstream dependency change is needed.

## Implementation and verification

### Mutation and teardown

GridForge checks stamp capacity under the world change lock before each
would-be mutation. Duplicate operations and invalid inputs retain their normal
no-op results. Removing a grid reserves its removal stamp and one clear for
each blocked voxel, including stacked-token cases. Detached cleanup preserves
the removed generation's identity and reservations through partition callbacks.
The world tracks blocked voxel count separately from total token attachments.

Ordinary reset checks that existing clears and the reset marker fit before
calling reset observers. If an observer spends the remaining headroom, the
admitted reset retains its marker and suppresses the detailed clears as a
group. The marker invalidates prior observations. Deactivation or disposal can
release the world, pools and subscriptions when the remaining sequence cannot
describe teardown. A terminal release emits the pre-reset lifetime notification
without inventing a sequence. See [world changes and composition](../../api/guides/World-Changes-and-Composition.md).

Each voxel attachment or removal by a blocker commits individually. A blocker
interrupted by exhaustion retains its token for the attachments that survived
apply, rollback or removal. Its world subscription lets deactivation clear that
ownership. Recovery also required correcting the world slot lookup. For a live
grid in slot 2, removing slots 0 and 1 leaves a resident count of one. Resolution
now checks the supported slot range and actual allocation. The
[blocker contract](../../api/guides/Blockers-and-Obstacles.md) describes what
callers can observe after an interruption.

### GF-Issue-016

The counter investigation also exposed
[GF-Issue-016](../issue-tracker.md#gf-issue-016) at ordinary sequence values.
On v10.2.0 ReleaseLean, an observer called `RemoveBlockage()` on the same
blocker during its first attachment. The outer call attached the second cell
and returned with an invalid public token. Both cells retained the original
token, and another removal left the world at sequence 3. Cached and uncached
coverage reproduced the defect. The follow-up made removal cancel the
in-flight application and roll back its committed attachments. The
[two-cell regression](../../../tests/GridForge.Tests/Blockers/BlockerTests.cs)
checks exact voxel event order, blocker notifications and a fresh application.
Both cache modes failed before the follow-up fix and passed in the 52-case
focused run on 2026-11-28.

### GF-Issue-017

An overlapping grid registration exposed a second path into the same
blocker. The reproduction used the local Release build of develop
`c5d0f809` with the counter fix applied and ordinary sequence values.
A blocker covered two adjacent cells at world X coordinates 0 and 1. In the
first `ObstacleAdded` callback, the observer saved the token and registered a
second grid containing one cell at X = 1. The grid-added watcher immediately
started another application on the same blocker.

The observer saw an obstacle addition at sequence 2, grid registration at 3,
two obstacle additions at 4 and 5, two removals at 6 and 7, and another
addition at 8. Both original cells retained the saved token while the new cell
remained unblocked. `IsBlocking` was true with an invalid token. A later
`RemoveBlockage()` left both attachments in place. Cached and uncached runs
agreed. [GF-Issue-017](../issue-tracker.md#gf-issue-017)

The fix makes the watcher request a new pass. The outer application rolls back
its attachments and retraces the new topology under the same token. An
explicit removal cancels the pending pass. Three of four overlap regressions
failed with only the initial GF-Issue-016 change. The uncached case with a
queued removal already passed. On 2026-11-28 all 56 focused checks passed,
including the eight cancellation and overlap cases. The
[blocker regressions](../../../tests/GridForge.Tests/Blockers/BlockerTests.cs)
check both original cells, the overlapping cell, the exact token and removal.

Independent review also reproduced a reset from the first `ObstacleRemoved`
callback during ordinary removal and during rollback for an overlapping grid.
Both cached cases threw when reset cleared the list being enumerated. Cleanup
now walks recorded identities by index and stops when reset retires them.
The four reset cases verify that the blocker can apply to a replacement grid
with a fresh token. All 60 focused blocker tests passed on November 28.

The follow-up passed 1,296 tests in each of Release and ReleaseLean on the
local develop stack. Both coverage gates reported 9,593 of 9,593 lines,
4,339 of 4,339 branches and 1,175 of 1,175 executable methods. The Release
blocker apply/remove benchmark reported the same allocations before and after
the follow-up (20.42 MB uncached and 20.52 MB cached per benchmark operation).

### Regressions and review

- On the v10.2.0 Release source baseline, 12 of the first 13 counter regressions
  failed. The existing world-local generation guard passed. After the primitive
  fix, six of eight initial blocker cases still failed at the compound
  operation. A separate high-slot regression failed before the lookup fix.
  The final suites include the [counter](../../../tests/GridForge.Tests/Grids/GridWorld.CounterExhaustion.Tests.cs),
  [teardown](../../../tests/GridForge.Tests/Grids/GridWorld.TeardownExhaustion.Tests.cs)
  and [blocker](../../../tests/GridForge.Tests/Blockers/BlockerCounterExhaustionTests.cs)
  regressions.
- Independent review replayed a guarded nested ordinary reset. Both
  subscribers observed the original live grid before the nested teardown, and
  both reset markers published in the same order as v10.2.0. Two- and
  three-cell interrupting callback cases retained the surviving attachment in
  both cache modes, then cleared ownership on world deactivation. Both modes
  also preserved the surviving token for slot 2 when slots 0 and 1 were free.
  These probes used a private copy of the local Release assembly before the
  final deactivation refinement, SHA-256
  `3088A78A5EC1755A07C6FD05D088108E21EF3522628DFE7502DC349C1D3FCF59`.
- The final lifecycle implementation coalesces a recursive deactivation request
  in the world and releases the lock after pre-teardown observers return if a
  callback requested disposal. Ordinary nested resets still deliver their
  callbacks. Four red-then-green cases cover a first observer disposing during
  ordinary reset (normal and terminal sequence) and a first observer requesting
  both deactivation and disposal during an outer deactivation or disposal. A
  second observer checks that the grids remain live for the appropriate
  callback. Another regression attempts disposal from a partition callback
  while ordinary reset owns the write lock. The expected lock-recursion
  failure leaves the world active. A later independent deactivation leaves
  the lock usable until the caller explicitly disposes it. Final independent
  source review checked the pending-dispose flag and its cleanup on failure.

### GridForge source matrix

Both target frameworks built in Release and ReleaseLean. Each net10.0 suite
passed the repository's strict coverage gate using its own fresh results.

| Configuration | Tests passed | Reported lines | Reported branches | Executable methods |
| --- | ---: | ---: | ---: | ---: |
| Release | 1,284 | 9,579 / 9,579 | 4,327 / 4,327 | 1,176 / 1,176 |
| ReleaseLean | 1,284 | 9,579 / 9,579 | 4,327 / 4,327 | 1,176 / 1,176 |

The affected downstream source checks covered Trailblazer's
`NavigationStructuralCompositionCarryoverTests` (40 passing in each
configuration) and Gravitas's `QueryPartitionReadinessTests` (20 passing in
each). They exercised navigation carryover and the physics partition
publication boundary. Unity coverage here is limited to the adapter source
inspection recorded above. The GridForge DocFX build passed with warnings
treated as errors, with zero warnings and zero errors.

Reproduce the source matrix from the GridForge root with the sibling revisions
listed above. Run configurations serially.

```powershell
$sourceProps = @('-p:UseLocalLsfStack=true', '-p:DisableTransitiveProjectReferences=true', '-m:1')
foreach ($configuration in 'Release', 'ReleaseLean') {
    $results = "artifacts/counters/$configuration"
    dotnet restore GridForge.slnx "-p:Configuration=$configuration" @sourceProps
    dotnet build GridForge.slnx -c $configuration --no-restore @sourceProps
    dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c $configuration --no-build @sourceProps --settings tests/GridForge.Tests/coverlet.runsettings --collect:"XPlat Code Coverage" --results-directory $results
    .assets/scripts/assert-full-coverage.ps1 -CoverageDirectory $results
}
```

### Mutation costs

BenchmarkDotNet 0.15.8 executed both cases before and after, in Release on
Windows 11 x64, Core i7-9700K, .NET SDK 10.0.302 and runtime 10.0.10.
The in-process ShortRun used one launch, three warmups and three measured
iterations with one invocation each. Both runs used the local dependency graph.
These are complete benchmark operations, including their existing setup scope.

| Sparse voxel workload | v10.2.0 mean ± SD | Changed mean ± SD | Allocated per operation, before / after |
| --- | ---: | ---: | ---: |
| Apply and remove cached sparse blocker, 256 rounds | 146.637 ± 2.113 ms | 143.481 ± 1.903 ms | 39.56 / 39.56 MB |
| Construct and remove sparse 64×64 low-density grid, 64 rounds | 4.237 ± 0.091 ms | 4.585 ± 0.255 ms | 1.30 / 1.30 MB |

The final sparse construction mean was 8.2% higher and the cached blocker
mean was 2.2% lower. The reported confidence intervals overlap for both
comparisons. Three measured iterations leave the throughput changes
inconclusive. BenchmarkDotNet also flagged the final sparse construction
case's shortest iteration at 4.366 ms against its recommended 100 ms minimum.
Reported allocation matched at the displayed precision.

A separate warm comparison ran each assembly in its own process. Calling the
baseline A and the changed library B, the process order was ABBA BAAB. Each
process ran three warmup batches followed by nine measured batches. The
intervals below span the four process medians for each build.

| Operation | Cycles per batch | v10.2.0 ns/cycle | Changed ns/cycle | Warm bytes/cycle, both builds |
| --- | ---: | ---: | ---: | ---: |
| Sparse voxel add and remove | 100,000 | 701.5–746.4 | 721.4–838.5 | 32 |
| Obstacle add and remove | 100,000 | 817.8–918.5 | 782.1–855.0 | 32 |
| Reset obstructed grid, register grid and add obstacle | 10,000 | 3,011.5–3,690.8 | 2,811.0–3,454.6 | 1,688 |

The measured warm allocations matched exactly. Timing ranges overlap on every
workload. These results apply to the measured operations on this machine.
