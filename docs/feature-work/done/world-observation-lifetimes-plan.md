# World Observation Lifetimes Plan

**Opened:** 2026-11-26

**Completed:** 2026-11-28 on develop.

## Outcome

A diagnostic session now observes one active world through five world-owned
subscriptions. Host code can explicitly dispose it while the world stays
active. Dropping both objects releases them to the GC, and deactivation
detaches the session even when the host retains the old world. The public
session API and intentional cross-world manager events are preserved.
[Diagnostics guide](../../api/guides/Grid-Diagnostics-and-Geometry.md)

The [runtime counter work](runtime-counter-lifetimes-plan.md) established the
fresh-world handoff at permanent change-sequence exhaustion. The old session
can read its terminal reset marker. A fresh session on the replacement world
observes only the new generation. Occupant events consume no change stamps.

## GF-Issue-018

Against local-stack Release revision `f9500aa307a38e55156f156ef7488e24002682d6`,
create two dense one-cell worlds at the origin and call
`gridA.TryAddVoxelOccupant(voxelB, occupant)`. The call succeeded. A held the
ticket, B's voxel count increased, and the event identified B. Removing through
B failed. A could remove the misplaced registration. A legitimately registered
occupant in B served as the foreign-removal control.

The first two red runs produced 13 failures across 19 cases, including
the callback-lifetime and session controls below. The ownership regressions cover
foreign worlds, sibling grids, and a foreign object carrying the receiver's address. Admission now verifies the
registered grid, exact physical reference, address and materialization under the
world change lock. The grid occupant lock precedes the world registry lock.
The event captures the address, occupant, ticket and resulting count before
publication. Ordinary add/remove admission reads host identity getters before
taking the mutation locks and publishes its diagnostics and callbacks after
releasing them. Grid-scoped calls preserve their receiver generation
across host getters, and bulk removal checks each captured ticket so reentrant
replacement registrations survive.
[Ownership regressions](../../../tests/GridForge.Tests/Grids/GridOccupantManager.Ownership.Tests.cs) ·
[Registry regressions](../../../tests/GridForge.Tests/Grids/OccupancyRegistryTests.cs)

## GF-Issue-019

On the same baseline, a global occupant add or remove handler replaced a dense
grid and subscribed on the rerented voxel. The new listener received its
predecessor's event. A sparse remove/rematerialize at the same address
reproduced this even with identical world, grid, voxel index and object
reference. Sparse obstacle remove and clear delivered sequence 4 to a new
subscriber after the world had reached sequence 6.

Runtime sparse materialization records the existing sparse-add commit sequence.
Dense and initially configured sparse voxels begin at zero. Local delivery
captures the invocation list for the event, then checks its live address and
materialization against the original occupant mutation or obstacle commit.
This also handles queued obstacle events whose target was recycled before
global delivery began. A handler failure logs the payload address if the
handler itself retires the voxel. The private partition provider also serves as its voxel's partition lock.
[Occupant regressions](../../../tests/GridForge.Tests/Grids/GridOccupantManager.Ownership.Tests.cs) ·
[Obstacle regressions](../../../tests/GridForge.Tests/Grids/GridObstacleManager.Ownership.Tests.cs)

## Publication and lifetime

Global manager subscribers run first. The owning world then publishes exact
cell dirt before eligible voxel-local subscribers. An obstacle mutation later
produces broad grid dirt while the generation is still live. Previously a
global observer's view of session dirt depended on subscription order. Two
one-cell regressions pin the chosen ordering. A voxel callback which reads and
clears sees only the exact change and leaves the later grid change pending. A
voxel callback which removes its grid leaves the exact change and grid removal
visible. Detached teardown clears cannot dirty a replacement grid.

Ordinary reset retains the session. A rejected reset preserves prior dirt.
An admitted reset supersedes it through the pre-teardown notification. Grid
lifecycle events can accompany the pending marker, while cell and broad changes
resume after acknowledgement. Explicit disposal clears the session's pending
buffer. Session creation checks the active world and subscribes atomically with
respect to deactivation. The Unity debugger can continue using its existing
session disposal and world-replacement paths.
[Session regressions](../../../tests/GridForge.Tests/Diagnostics/GridDiagnosticSession.Lifetime.Tests.cs) ·
[Dirty output and coalescing](../../../tests/GridForge.Tests/Diagnostics/GridDiagnosticSessionTests.cs) ·
[World teardown](../../../tests/GridForge.Tests/Grids/GridWorld.TeardownExhaustion.Tests.cs)

## Verification

The Windows x64 runs used SDK 10.0.302 and .NET 10.0.10 on develop at
`55dc375` plus this change. Sibling revisions were Chronicler `0281469`,
FixedMathSharp `8fdf889` and SwiftCollections `4d22e57`. Build the solution
serially in Release and ReleaseLean using `-p:UseLocalLsfStack=true`,
`-p:DisableTransitiveProjectReferences=true`, `-p:BuildInParallel=false`
and `-m:1`. Run GridForge.Tests for each configuration using
`coverlet.runsettings` and apply the [repository coverage gate](../../../CONTRIBUTING.md#coverage-validation).
These local revisions identify the measured stack. CI selects its own pinned
revisions for develop.

The reachability probe used 12 pairs per scenario, non-inlined construction,
and six forced collections in isolated processes. Before migration, all 12
dropped worlds and sessions survived, both active and deactivated. Explicit
disposal collected all 12. With the change, both forgotten groups had zero
survivors. Holding an active world kept its sessions alive until disposal.
Holding a deactivated world released its sessions. An instance-event cycle
with no other roots also collected.

A single-cell occupant stores its ticket directly in the registry record. A
second simultaneous address promotes it to a SwiftDictionary. After final
removal, the registry can reuse one empty ticket map of up to 32 slots. Larger
maps are released, and reset drops the spare. A scan cell enters the active set
when its occupant count first reaches one. The coalescing dictionary uses grid
generation, scope and local index for hashing and compares all location
components, including bounds, before combining flags.

The cost probes compared frozen Release builds with the same sibling sources,
workstation GC and `DOTNET_TieredCompilation=0`. Four fresh processes per build
ran in ABBA/BAAB order with two warmup batches and seven measured batches. The timed dense scene used 16 cells. Each occupant or
obstacle cycle added and removed on the same cell. The burst traversed all
16 cells, mixed both mutations, then read and cleared the expected 17 records.
The wave added 64 distinct owners across 16 cells before removing all 64.
Separate cases covered an eight-cell occupant, a sorted read and clear,
reset/recreation, throwing global callbacks, and cold dense registration.
Layouts used zero, one or four local sessions, four remote sessions, and one
local plus three remote sessions. Timing is per complete operation.

| Release workload | Baseline | Changed | Allocated bytes per operation, before / after |
| --- | ---: | ---: | ---: |
| Single occupant add/remove, no sessions | 438 ns | 407 ns | 728 / 152 |
| Wave of 64 distinct occupants, no sessions | 26.032 µs | 21.772 µs | 43,040 / 6,176 |
| One occupant across eight cells, no sessions | 2.861 µs | 2.969 µs | 3,024 / 376 |
| One occupant across eight cells, one local session | 5.344 µs | 4.662 µs | 3,536 / 376 |
| Occupant add/remove, four remote sessions | 682 ns | 469 ns | 840 / 152 |
| Obstacle add/remove, four remote sessions | 911 ns | 736 ns | 144 / 32 |
| Mixed 16-cell burst, four local sessions | 86.82 µs | 65.36 µs | 17,536 / 4,736 |
| Read and clear 16 cells, one local session | 3.823 µs | 2.571 µs | 0 / 0 |
| Reset and recreate, four local sessions | 7.623 µs | 7.017 µs | 1,448 / 1,336 |
| Throwing global obstacle callback, one local session | 3.543 µs | 3.603 µs | 464 / 424 |

The first three rows use the longer observer-free checks with 120,000 single
pairs, 3,000 distinct-owner waves, or 20,000 eight-cell waves per batch. The
eight-cell median increased 3.8%. Process medians ranged from 2.794 to 2.977 µs
before and 2.871 to 3.081 µs after. The ranges also overlapped on the
throwing-obstacle case. Both directions should be interpreted at that precision.

Constructing 128 sessions allocated 312,760 bytes before and 228,352 after.
The retained heap delta after dirtying 16 cells per session fell from 1,018,344
to 976,632 bytes. Cold registration of 49,152 dense voxels allocated 12,309,216
bytes before and 11,129,568 after, a saving of 24 bytes per voxel. The baseline
DLL SHA-256 was `1DB2ACE4686229A479FB7CAE89F4D65FBE76A9449B699CBDCFE42334C8B72849`
and the measured candidate was `73BA532AD41FF61EB5A2FA7629F54A76036F75A46F9E056B41D400DB26F7EED4`.

Both release variants built for netstandard2.1 and net10.0. Each net10.0 suite
passed 1,337 tests and the strict 100% reported line, branch and executable-method
coverage gate (1,175 methods). Independent review covered mutation ownership,
callback reentry, the session lifetime and the bounded registry map.
The Debug suite passed 1,334 tests. All 25 diagnostic-session cases also passed
with each netstandard2.1 dependency graph loaded in an isolated .NET 10 test
host, exercising the alternate delegate enumeration path. DocFX built with
zero warnings. The repository's warm occupant-wave and dense rectangular
diagnostic-query benchmarks each returned three measured samples. Both
ShortRuns reported short iteration times. The paired probes above supply the
before/after comparison.
