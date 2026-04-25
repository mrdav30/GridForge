# Core Concepts

This page defines the vocabulary of GridForge.

## The Concept Stack

GridForge is easiest to reason about as a stack of layers:

1. `GridWorld` owns world lifecycle, registration, lookup, and world-scoped identity.
2. `GridConfiguration` defines input bounds and scan-cell size for a grid.
3. `VoxelGrid` owns one grid's voxels, scan cells, neighbors, and versioned state.
4. `Voxel` is the per-cell unit of occupancy, obstacles, partitions, and adjacency.
5. `ScanCell` is the query-oriented overlay used to accelerate occupant scans.
6. Managers and utilities such as `GridScanManager`, `GridObstacleManager`, `GridOccupantManager`, and `GridTracer` mutate or query those structures.

## World Scope Is The Architectural Boundary

One of the most important concepts in GridForge is that runtime state is coordinated through an explicit `GridWorld`.

`GridWorld` is responsible for:

- owning voxel size and spatial hash settings for one world instance
- registering and removing grids
- spatial hashing for fast grid lookup
- resolving world positions or `WorldVoxelIndex` values back to active grids and voxels
- publishing world-scoped grid lifecycle events

## World Space, Grid Space, And Snapping

GridForge constantly moves between three different coordinate views:

| Coordinate View | What It Represents | Common Type |
| --- | --- | --- |
| World space | Absolute positions in your simulation or game world | `Vector3d` |
| Grid-local space | Integer voxel coordinates inside one grid | `VoxelIndex` |
| World-scoped voxel identity | A voxel coordinate plus its owning world and grid instance | `WorldVoxelIndex` |

Snapping is a core behavior:

- `GridConfiguration` preserves ordered input bounds.
- `GridWorld` normalizes and snaps those bounds during registration.
- Voxel lookup converts a world position into a zero-based `VoxelIndex`.

## `GridConfiguration`

`GridConfiguration` is the input contract for creating a grid.

It defines:

- `BoundsMin`
- `BoundsMax`
- `ScanCellSize`

Important details:

- bounds are ordered during construction, but not snapped until a world registers the grid
- `ScanCellSize` is expressed in voxels, not world units
- `ToBoundsKey()` creates the exact identity key used after normalization

## `VoxelGrid`

`VoxelGrid` is the main container for a single registered grid.

Useful mental model:

- `GridWorld` answers "which grid?"
- `VoxelGrid` answers "which cell inside that grid?"

## `Voxel`

`Voxel` is the core cell unit in GridForge.

A voxel tracks:

- its local coordinate through `VoxelIndex`
- its world-scoped coordinate through `WorldVoxelIndex`
- its world-space position
- obstacle count and obstacle tokens
- occupant count
- attached partitions
- cached neighbor relationships
- whether it is a boundary voxel

## `ScanCell`

`ScanCell` is a query acceleration layer built on top of voxels.

A scan cell:

- belongs to exactly one grid and one world
- is identified by a grid-local `CellKey`
- tracks occupants bucketed by `WorldVoxelIndex`
- knows whether it currently contains any occupants

## Obstacles, Blockers, And Occupants

### Obstacles

Obstacle state lives on voxels and is managed through `GridObstacleManager`.

### Blockers

Blockers are higher-level world-space objects that apply obstacle state to many voxels at once.

Important blocker concepts:

- `Blocker` is the abstract base behavior
- `BoundsBlocker` is the concrete bounds-driven implementation in this repo
- blockers are bound to a `GridWorld`
- blockers use traced coverage to find the voxels they affect

### Occupants

Occupants are dynamic entities that live in voxels and are indexed through scan cells.

`IVoxelOccupant` requires:

- a stable `GlobalId`
- a world-space `Position`
- an `OccupantGroupId`

## Partitions

Partitions are attachable pieces of typed metadata or behavior that live on a voxel.

## Identity Types

### `VoxelIndex`

`VoxelIndex` is the local coordinate of a voxel inside one grid.

### `WorldVoxelIndex`

`WorldVoxelIndex` ties a voxel coordinate to:

- the owning world instance (`WorldSpawnToken`)
- the world-local grid slot (`GridIndex`)
- the concrete runtime grid allocation (`GridSpawnToken`)
- the voxel's local coordinate (`VoxelIndex`)

As a rule:

- local work uses `VoxelIndex`
- cross-system references use `WorldVoxelIndex`
