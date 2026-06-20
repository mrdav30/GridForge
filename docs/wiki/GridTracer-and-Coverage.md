# GridTracer and Coverage

`GridTracer` is the utility layer that translates world-space shapes into grid-space coverage.

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

The result is intentionally practical for blockers and scans: coverage may
include every hex cell touched by a world-space region without asking callers to
branch on `GridTopologyKind`.

## 2D XZ Projection

The `Vector2d` overloads are convenience APIs over the same 3D world model:

- `Vector2d.X` maps to world X
- `Vector2d.Y` maps to world Z
- `layerY` maps to world Y and defaults to `0`

`TraceLine(Vector2d, Vector2d, ...)` keeps the existing positional `padding`
and `includeEnd` argument order. Supply `layerY` by name when using the 2D trace
overload with nonzero layers.

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

Coverage often crosses more than one grid. Returning grouped results preserves that reality.

`GridVoxelSet` keeps:

- the `VoxelGrid` that owns the covered cells
- the list of covered voxels for that grid

Allocation-sensitive callers can use `GetCoveredVoxelsInto(...)` instead. It
clears and fills a caller-owned `SwiftList<Voxel>` with the same covered voxels
as the grouped enumerable path. Pass a reusable `GridTraceScratch` when the
caller also wants to own the temporary processed-grid and duplicate-voxel sets.
The flat result lets hot paths avoid enumerable and pooled grouped-list lifetime
costs while still resolving the owning grid from `voxel.GridIndex` when needed.

## Traversal Padding And Duplicate Suppression

Consumers that build their own GridForge-backed broad phases can use
`GridTraversal` and `GridTraversalState` for duplicate-safe voxel traversal.
`GridTraversal.TryGetUniquePartition(...)` suppresses repeated voxel visits by
voxel spawn token before resolving a typed partition.

`GridTraversalState` caches the selected topology edge per grid while walking
voxels. Use `GridTraversalPaddingMode.MaxCellEdge` for full 3D padding and
`GridTraversalPaddingMode.PlanarMaxCellEdge` for X/Z-plane systems that should
not inherit vertical layer height. `GridTopologyMetricUtility` exposes the same
3D, planar, and representative cell-edge measurements for callers that only
need the metrics.

## How Blockers Use Coverage

`Blocker` and `BoundsBlocker` delegate region-to-voxel logic to the tracer.

```text
blocker bounds
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
- the tracer releases those pooled lists when the enumeration is disposed or completes
- `GetCoveredVoxelsInto(...)` writes directly to caller-owned storage
- `GridTraceScratch` can be reused across calls but should not be shared between concurrent queries

Callers should treat grouped voxel lists as transient and consume them immediately inside the enumeration.
