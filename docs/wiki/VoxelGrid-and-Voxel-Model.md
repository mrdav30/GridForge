# VoxelGrid and Voxel Model

This page explains the two core storage types at the center of GridForge:

- `VoxelGrid`, which owns one grid's runtime state
- `Voxel`, which represents one mutable cell inside that grid

If `GridWorld` is the world coordinator, these are the types that actually hold the world data.

## `VoxelGrid` In One Sentence

`VoxelGrid` is the runtime container for one snapped, registered region of world space.

It owns:

- the grid's bounds and dimensions
- the 3D voxel array
- the scan-cell overlay
- neighboring grid links
- per-grid occupancy and obstacle summary state
- per-grid versioning

## Construction Flow

When a grid is initialized, `VoxelGrid.Initialize(...)`:

1. stores identity and configuration
2. computes dimensions from snapped bounds and world voxel size
3. generates scan cells and voxels

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

- `VoxelGrid.Neighbors` stores neighboring grid ids by `SpatialDirection`
- `Voxel` exposes directional neighbor lookup through `TryGetNeighborFromDirection(...)` and `GetNeighbors(...)`

If a local voxel lookup fails at the edge of a grid, the voxel resolves the matching world-space neighbor through its owning world.

## Reset And Reuse

Both grids and voxels are pooled, so reset behavior is part of the architecture.

The architecture depends on reset being thorough because reused pooled objects come back through the same types later.
