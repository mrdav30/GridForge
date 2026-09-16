# GridTracer and Coverage

`GridTracer` is the utility layer that translates world-space shapes into
grid-space coverage.

Several higher-level systems depend on the same answer:

"Which cells does this world-space region touch in this world?"

## The Core Outputs

`GridTracer` exposes world-scoped entry points such as:

- `TraceLine(GridWorld world, Vector3d start, Vector3d end, ...)`
- `TraceLine(GridWorld world, Vector2d start, Vector2d end, ..., layerY: ...)`
- `GetCoveredVoxels(GridWorld world, Vector3d boundsMin, Vector3d boundsMax, ...)`
- `GetCoveredVoxels(GridWorld world, Vector2d boundsMin, Vector2d boundsMax, layerY: ...)`
- `GetCoveredVoxelsInto(...)` for caller-owned flat voxel result storage
- `GetCoveredScanCells(GridWorld world, Vector3d boundsMin, Vector3d boundsMax, ...)`
- `GetCoveredScanCells(GridWorld world, Vector2d boundsMin, Vector2d boundsMax, layerY: ...)`
- `GetCoveredScanCellsInto(...)` for caller-owned scan-cell result storage
- `TraceNavigationBodyInto(...)` for bounded, allocation-free swept upright-body prism unions
- `TraceIntervalsInto(...)` for bounded canonical segment intervals, including missing addresses
- `TryTraceRectangularSlabsInto(...)` for observing complete X slabs of a qualified original segment

## Exact Segment Intervals And Completed Slabs

`TraceIntervalsInto(...)` writes exact closed cell-prism intervals into a
caller-owned `SwiftList<GridTraceInterval>`, using reusable
`GridTraceIntervalScratch`. It includes missing sparse addresses, orders results
canonically, assigns simultaneous-coverage ties, and reports continuous address
and physical coverage separately. Grid, address, output, and combined candidate
work ceilings are independent. Address admission charges the original full
candidate box even when the rectangular collector omits planar misses.

`TryTraceRectangularSlabsInto(...)` adds a narrow synchronous observation path.
The caller supplies an exact `GridCoveredAddressGeneration`, an ordinal span
at least as long as `outputLimit`, and a `Func<GridTraceSlab, bool>` observer.
It supports exactly one active matching rectangular single-layer grid, nonzero
original X/Z deltas, and representable extreme prisms and slab arithmetic.
Unsupported input returns `false` before chargeable discovery, clears results,
and invokes no observer; ignore that report and use `TraceIntervalsInto(...)`.
Once qualified it returns `true` for every terminal outcome, including ceilings;
do not restart a failed qualified trace as an uncharged fallback.

Each packet identifies all newly appended intervals for one fully enumerated
X slab, in ascending X order—even when no interval exists. Missing physical
addresses still produce intervals. The original segment and admission box never
shrink. `SeparatesEndpoints` means only that the original endpoints lie strictly
beyond the actual opposing X faces with more than one raw parameter unit of
margin on each side. It does not assert an obstruction or navigation validity.

Return `true` to continue. Full completion uses the same canonical sort, ties,
and coverage as the eager tracer. The source ordinal at final position `i`
identifies that interval's original emission position, so a consumer can reorder
its own associated records without mapping them again. Return `false` to stop
immediately after the completed slab: the status is
`StoppedAfterCompleteSlab`, `IsComplete` is false, ties remain unassigned, and
both coverage flags are false. **Partial output is emission order, not a
canonical ray prefix.**

The observer runs under the world read lock. Do not reenter world/grid
operations or modify supplied results or scratch. Reading world identity and
change-stamp properties for freshness checks is allowed. The lock protects grid
lifetime, not concurrent obstacle updates; packets carry the original world
stamp captured before generation qualification, and consumers must perform
their own final freshness fence. Same-scratch tracing or `Clear()` during a
callback throws before clearing the active output. Scratch is not thread-safe.
Normal completion/stop releases temporary grid references. Failures and
exceptions also clear results and used ordinals; observer exceptions propagate.

## The Common Coverage Pipeline

```text
world-space input
  -> snap to voxel-aligned bounds or points
  -> find candidate grids through GridWorld
  -> enumerate covered voxels or scan cells through each grid's topology
  -> group or yield results
```

## Topology-Aware Coverage

`GridTracer` keeps the public workflow topology-neutral: callers pass a
`GridWorld` plus world-space input and receive grouped grid/voxel or scan-cell
results.

Internally, rectangular-prism grids use rectangular index ranges. Hex-prism
grids use axial coordinates in the XZ plane. Hex line tracing snaps endpoints
through the grid topology, interpolates in axial/cube space, and rounds
deterministically to `VoxelIndex(q, layer, r)`. Hex bounds coverage is
conservative: it expands the broad phase by the hex radius, projects candidate
corners into axial space, then filters candidate voxels by horizontal cell
reach.

Candidate discovery is adaptive. Local queries use the world's internal spatial
index, while queries whose coordinate volume is more expensive than the loaded
world scan active grids and filter exact bounds. Candidates are sorted by
recyclable grid slot before topology traversal, so the selected discovery path
does not change observable ordering. No path enumerates empty coordinate volume
merely because a sparse grid or query spans a large world-space range.

The result is intentionally practical for blockers and scans: coverage may
include every hex cell touched by a world-space region without asking callers to
branch on `GridTopologyKind`.

## Swept Navigation Bodies

`TraceNavigationBodyInto(...)` is the non-allocating coverage entry point for a
direct cylindrical body-foot sweep. Callers provide both the result
`SwiftList<GridNavigationBodyTraceCell>` and reusable
`GridNavigationBodyTraceScratch`; there is intentionally no allocating
convenience overload.

The stationary body at `startFoot` must have closed-set contact with the
declared source prism, and the stationary body at `endFoot` must have closed-set
contact with the declared target prism. Exact planar or vertical tangency is
accepted for these endpoint identity checks; a declared endpoint that the body
never contacts fails as `InvalidOrUnrepresentableGeometry` before candidate
work.

The tracer discovers candidate grids from the swept capsule/body bounds, uses
each grid topology's existing covered-address range, creates exact cell prisms,
and retains only positive interior body overlap. FixedMathSharp evaluates the
strict swept-cylinder relation with exact wide intermediates: planar and
vertical interior overlap must coexist at one shared continuous parameter.
Mere tangency or boundary-only coincidence does not claim a neighboring prism.
A direct rectangular diagonal also retains the deterministic
four- or eight-cell corner closure; a hex vertical-planar diagonal retains its
source, planar peer, vertical peer, and target. Results are sorted by durable
configuration identity, grid generation, and topology-local address, so
reversing the segment endpoints does not reverse the published set.

The union check starts from the source prism and closes every positive-overlap
neighbor in that topology's exact 26-cell rectangular or 20-cell hex lattice.
An adjacent grid participates only when it issues an exactly coincident,
congruent prism in the same aligned topology lattice, which allows aligned maps
to support one large body across their boundary. A missing outer neighbor
rejects a body that penetrates beyond all returned prisms; exact boundary
tangency remains owned by the interior cell. Unaligned or differently shaped
cross-grid coverage is rejected conservatively. In particular, multiple
heterogeneous partial prisms are not combined as a general CSG replacement for
one required source-lattice prism.

Exact duplicate prisms from overlapping grids are alternatives, not additional
requirements. Distinct source and target identities remain pinned even when
their prisms coincide; for other exact duplicates the certificate prefers a
physically present address and then the stable canonical grid/address order.
When every non-endpoint alternative is missing, one is marked
`RequiredCoverage` and the other missing identities are published as
`PhysicalAlternativeDependency`. Those dependency cells are invalidation-only
OR evidence, not jointly required physical coverage.

Unlike ordinary sparse voxel coverage, this API publishes required missing
addresses. Every result carries canonical world/grid generation identity,
physical presence, role, and the owning grid's last committed change sequence.
The report also carries the world run stamp. `IncompletePhysicalCoverage` therefore
gives callers a reusable negative proof whose complete per-grid evidence stales
when any satisfying alternative changes without staling a trace that depends
only on an unrelated grid.

Grid, address, output, and combined candidate-work ceilings are exact and
independent. `GridCandidateLimitExceeded` identifies the grid ceiling;
`CandidateWorkLimitExceeded` identifies the combined ceiling only when it is
strictly tighter. Raw alternatives consume address/work budget. `outputLimit`
is preflighted against the full required plus dependency-evidence result, so a
one-below failure does not publish a partial proof. Checked body-top and query
bounds failures report `ArithmeticOverflow`; invalid or unrepresentable
topology and union geometry remain `InvalidOrUnrepresentableGeometry`.
Capacity, budget, invalid-input, and unrepresentable-geometry failures clear
the result; complete and incomplete-physical results retain their full
canonical evidence.

## 2D XZ Projection

The `Vector2d` overloads are convenience APIs over the same 3D world model:

- `Vector2d.X` maps to world X
- `Vector2d.Y` maps to world Z
- `layerY` maps to world Y and defaults to `0`

`TraceLine(Vector2d, Vector2d, ...)` accepts positional `padding` followed by
`includeEnd`. Supply `layerY` by name when using the 2D trace overload with
nonzero layers.

```csharp
Vector2d start = new Vector2d(-2, -2);
Vector2d end = new Vector2d(2, 2);

foreach (GridVoxelSet covered in GridTracer.TraceLine(world, start, end, layerY: Fixed64.Zero))
{
    foreach (Voxel voxel in covered.Voxels)
        Console.WriteLine(voxel.WorldPosition);
}
```

## Why Coverage Is Grouped By Grid

Coverage often crosses more than one grid. Returning grouped results preserves
that reality.

`GridVoxelSet` keeps:

- the `VoxelGrid` that owns the covered cells
- the list of covered voxels for that grid

Allocation-sensitive callers can use `GetCoveredVoxelsInto(...)` instead. It
clears and fills a caller-owned `SwiftList<Voxel>` with the same covered voxels
as the grouped enumerable path. Pass a reusable `GridTraceScratch` when the
caller also wants to own the temporary candidate-grid list, processed-grid set,
and duplicate-voxel set. The flat result lets hot paths avoid enumerable and
pooled grouped-list lifetime costs while still resolving the owning grid from
`voxel.GridIndex` while that returned voxel is current. `GridIndex` is a
recyclable slot; longer-lived runtime references should copy `voxel.WorldIndex`
and revalidate it through the owning world.

## Traversal Padding And Duplicate Suppression

Consumers that build their own GridForge-backed broad phases can use
`GridTraversal` and `GridTraversalState` for duplicate-safe voxel traversal.
`GridTraversal.TryGetUniquePartition(...)` suppresses repeated voxel visits by
exact `WorldVoxelIndex` before resolving a typed partition. Callers provide a
reusable `SwiftHashSet<WorldVoxelIndex>`; clear it between independent
traversals. Object hash codes are never treated as unique voxel identities. The
world token and grid generation inside `WorldVoxelIndex` make it exact for the
current runtime, but the value is not a serialized or durable content ID.

`GridTraversalState` caches the selected topology edge per grid while walking
voxels. Use `GridTraversalPaddingMode.MaxCellEdge` for full 3D padding and
`GridTraversalPaddingMode.PlanarMaxCellEdge` for X/Z-plane systems that should
not inherit vertical layer height. `GridTopologyMetricUtility` exposes the same
3D, planar, and representative cell-edge measurements for callers that only need
the metrics.

## How Blockers Use Coverage

`Blocker`, `BoundsBlocker`, and `AreaBlocker` delegate region-to-voxel logic to
the tracer. Use `BoundsBlocker` for 3D `FixedBoundBox` regions and `AreaBlocker`
for `FixedBoundArea` footprints locked to one world Y layer.

```text
blocker bounds or area
  -> GridTracer.GetCoveredVoxels(world, ...)
  -> per-grid covered voxel sets
  -> obstacle mutation on each covered voxel
```

## Sparse Coverage

Sparse grids use their bounds as an address space, but coverage results only
include configured physical voxels.

That means:

- `GetCoveredVoxels(...)` skips missing sparse voxels instead of materializing
  them.
- `GetCoveredScanCells(...)` returns only scan cells that exist for configured
  sparse blocks.
- Empty sparse regions are cheap to cover because absent sparse blocks are
  skipped by scan-cell key.

This behavior is the same for 3D coverage and layer-locked `Vector2d` coverage.

## Result Lifetime And Pooling

This is one of the most important practical details:

- `GridVoxelSet.Voxels` is backed by pooled storage
- the tracer releases those pooled lists when the enumeration is disposed or
  completes
- `GetCoveredVoxelsInto(...)` writes directly to caller-owned storage
- `GridTraceScratch` can be reused across calls but should not be shared between
  concurrent queries
- `GridCoveredAddressCursor.RetainedBytes` reports deterministic logical
  retention for admission budgets, including its configured generation capacity

Callers should treat grouped voxel lists as transient and consume them
immediately inside the enumeration.
