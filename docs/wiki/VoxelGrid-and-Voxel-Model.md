# VoxelGrid and Voxel Model

This page explains the two core storage types at the center of GridForge:

- `VoxelGrid`, which owns one grid's runtime state
- `Voxel`, which represents one mutable cell inside that grid

If `GridWorld` is the world coordinator, these are the types that actually hold the world data.

## `VoxelGrid` In One Sentence

`VoxelGrid` is the runtime container for one snapped, registered region of world space.

It owns:

- the grid's bounds and dimensions
- the storage strategy for physical voxels
- the scan-cell overlay for configured voxels
- neighboring grid links
- per-grid occupancy and obstacle summary state
- per-grid versioning

## Construction Flow

When a grid is initialized, `VoxelGrid.Initialize(...)`:

1. stores identity and configuration
2. computes dimensions from snapped bounds and per-grid topology metrics
3. initializes the grid's voxel storage and scan-cell overlay

Dense rectangular grids configure every voxel in the grid address space. Sparse
grids use the same bounds as an address space and configure only the explicitly
provided physical voxels. Reads never materialize missing sparse voxels.

Use `VoxelGrid.EnumerateVoxels()` when code needs to iterate physical voxels
without depending on a dense array layout, and use `ConfiguredVoxelCount` when
code needs the physical-cell count. Dense grids report `Size`; sparse grids
report the configured voxel count.

## Dense And Sparse Storage

`VoxelGrid.StorageKind` identifies whether the grid is dense or sparse.

Sparse grids preserve the same public grid model as dense grids:

- `TryGetGrid(...)` can resolve the grid bounds even when the addressed sparse
  voxel is missing.
- `TryGetVoxel(...)` and `TryGetGridAndVoxel(...)` require a configured
  physical voxel.
- `ContainsVoxel(...)` checks whether a physical voxel exists at a local index.
- `TryAddVoxel(...)` and `TryRemoveVoxel(...)` are explicit sparse-only runtime
  mutation APIs.

Runtime sparse removal is intentionally conservative. It rejects voxels with
occupants, obstacle tokens, partitions, or active voxel event subscribers so
state is not silently discarded.

## What A `Voxel` Owns

`Voxel` is the actual cell model.

A voxel carries:

- `WorldIndex` and `Index`
- `WorldPosition`
- `ScanCellKey`
- `ObstacleTracker` and `ObstacleCount`
- `OccupantCount`
- partition storage through `PartitionProvider<IVoxelPartition>`
- boundary awareness
- cached neighbors
- `CachedGridVersion`

## Neighbor Model

Neighbor handling spans both `VoxelGrid` and `Voxel`.

- `VoxelGrid.Neighbors` stores neighboring grid ids by topology-local neighbor slot.
- `Voxel` exposes rectangular lookup through `TryGetRectangularNeighbor(...)` and `GetRectangularNeighbors(...)`.
- `Voxel` exposes hex lookup through `TryGetHexNeighbor(...)` and `GetHexNeighbors(...)`.

If a local voxel lookup fails at the edge of a grid, the voxel resolves the matching world-space neighbor through its owning world.

Rectangular full-neighbor lookup uses the 26-cell rectangular-prism
neighborhood. Hex full-neighbor lookup uses the 20-cell hex-prism neighborhood:
6 same-layer planar neighbors, 7 neighbors on the layer below, and 7 neighbors
on the layer above. `RectangularDirectionUtility` and `HexDirectionUtility`
also expose deterministic subsets such as `Primary`, `Planar`, `Vertical`,
layer groups, and vertical diagonals.

For sparse grids, missing local neighbors are absent even when their indices are
inside the grid bounds. Boundary neighbor lookup can still cross into
configured voxels on neighboring grids.

## Reset And Reuse

Both grids and voxels are pooled, so reset behavior is part of the architecture.

The architecture depends on reset being thorough because reused pooled objects come back through the same types later.
