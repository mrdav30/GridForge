# GridTracer and Coverage

`GridTracer` is the utility layer that translates world-space shapes into grid-space coverage.

Several higher-level systems depend on the same answer:

"Which cells does this world-space region touch in this world?"

## The Core Outputs

`GridTracer` exposes world-scoped entry points such as:

- `TraceLine(GridWorld world, Vector3d start, Vector3d end, ...)`
- `TraceLine(GridWorld world, Vector2d start, Vector2d end, ...)`
- `GetCoveredVoxels(GridWorld world, Vector3d boundsMin, Vector3d boundsMax, ...)`
- `GetCoveredScanCells(GridWorld world, Vector3d boundsMin, Vector3d boundsMax, ...)`

## The Common Coverage Pipeline

```text
world-space input
  -> snap to voxel-aligned bounds or points
  -> find candidate grids through GridWorld
  -> enumerate covered voxels or scan cells
  -> group or yield results
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

## Result Lifetime And Pooling

This is one of the most important practical details:

- `GridVoxelSet.Voxels` is backed by pooled storage
- the tracer releases those pooled lists after yielding each grouped result

Callers should treat grouped voxel lists as transient and consume them immediately inside the enumeration.
