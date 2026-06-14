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

Callers should treat grouped voxel lists as transient and consume them immediately inside the enumeration.
