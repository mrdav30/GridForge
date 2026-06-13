# Architecture Overview

This page is the high-level map of how GridForge is put together.

If you only need one sentence, it is this:

GridForge is a deterministic voxel-grid system where `GridWorld` owns one isolated world's runtime state, `VoxelGrid` owns per-grid state, `Voxel` is the core mutable cell model, `ScanCell` accelerates occupant queries, and managers plus tracers provide mutation and query workflows on top of that state.

## Architectural Shape

GridForge is organized as a small set of cooperating layers:

| Layer | Main Types | Primary Responsibility |
| --- | --- | --- |
| World coordination | `GridWorld` | World lifecycle, registration, spatial hashing, top-level lookup, world events |
| Configuration and identity | `GridConfiguration`, `BoundsKey`, `VoxelIndex`, `WorldVoxelIndex` | Stable input, snapped bounds, and cross-system identity |
| Per-grid storage | `VoxelGrid`, `Voxel`, `ScanCell`, dense/sparse storage strategies | Core spatial data, local lookup, grid neighbors, occupancy and obstacle state |
| Mutation services | `GridObstacleManager`, `GridOccupantManager`, `Blocker` | Safe state changes, events, and higher-level world-space mutations |
| Query services | `GridScanManager`, `GridTracer` | Radius scans, filtered retrieval, line tracing, coverage enumeration |
| Extension and diagnostics | `IVoxelOccupant`, `IVoxelPartition`, `PartitionProvider`, `GridForgeLogger` | Domain integration, metadata hooks, logging and debugging |

## Repository Layout By Responsibility

| Path | Architectural Role |
| --- | --- |
| `src/GridForge/Configuration` | Grid creation inputs and bounds identity |
| `src/GridForge/Grids/Managers` | World orchestration and mutation/query manager APIs |
| `src/GridForge/Grids/Nodes` | Concrete runtime storage types: `Voxel` and `ScanCell` |
| `src/GridForge/Grids/Storage` | Dense and sparse physical voxel storage strategies |
| `src/GridForge/Grids/Topology` | Per-grid topology metrics, snapping, dimensions, and world/index projection |
| `src/GridForge/Grids/Support` | Pooled resources and event payload types |
| `src/GridForge/Spatial` | Shared coordinate, direction, occupant, and partition abstractions |
| `src/GridForge/Blockers` | World-space obstacle application on top of tracer coverage |
| `src/GridForge/Support` | Cross-cutting query result groupings like `GridVoxelSet` |
| `src/GridForge/Utility` | Tracing and logging infrastructure |

## The Main Runtime Loop

Most operations in the library follow the same broad path:

```text
world-space input
  -> optional snap / normalize against a GridWorld
  -> world-level candidate grid lookup
  -> per-grid voxel or scan-cell lookup
  -> query or mutation
  -> version / event / cache updates
```

## Core Data Ownership

### `GridWorld` owns

- active/inactive lifecycle for one world instance
- that world's spatial hash cell size
- the active grid bucket
- exact-bounds duplicate tracking
- the spatial hash used for coarse grid lookup
- world versioning and grid-level events

### `VoxelGrid` owns

- one grid's snapped bounds and dimensions
- its per-grid topology metrics
- its dense or sparse physical voxel storage
- its scan-cell overlay for configured voxels
- the set of active scan cells
- neighboring grid relationships
- per-grid obstacle and occupancy summary state
- per-grid versioning

### `Voxel` owns

- local and world-scoped identity
- cell-level occupancy state
- cell-level obstacle state
- attached partitions
- boundary awareness
- cached voxel-neighbor relationships

### `ScanCell` owns

- a grid-local cell key
- occupant buckets grouped by voxel
- the tickets used to retrieve specific occupants
- fast "is there anything here?" state for scan-oriented queries

## Why `GridWorld` Sits At The Top

GridForge is not architected as "a single process-wide world." It is architected as "one or more isolated worlds, each of which may own many grids."

That is why `GridWorld` sits above everything else:

- it maps snapped bounds to a reusable world-local grid slot
- it builds the coarse spatial hash used to find candidate grids quickly
- it links neighboring grids when overlap is valid
- it resolves world-space and world-scoped voxel identities back to active runtime objects

## Registration And Construction Flow

```text
GridConfiguration
  -> GridWorld normalization and snapped bounds key
  -> duplicate check
  -> pooled VoxelGrid rent
  -> VoxelGrid.Initialize(...)
      -> topology-specific dimension calculation
      -> dense or sparse physical voxel storage initialization
      -> scan-cell storage for configured voxels
  -> spatial hash registration
  -> neighbor linking
  -> world add notification
```

## Topology Architecture

Grid topology is a per-grid strategy. `GridConfiguration.TopologyKind` selects
rectangular-prism or hex-prism cells, and `GridConfiguration.TopologyMetrics`
stores the deterministic cell geometry for that grid.

The topology layer owns:

- bounds normalization and snapping
- dimensions and topology-local index ranges
- world-position to `VoxelIndex` projection
- `VoxelIndex` to world-position projection
- scan-cell key projection
- neighbor offsets and boundary ranges

Storage remains separate. Dense and sparse storage decide which physical voxels
exist after topology has mapped world-space input to a local index or coverage
range.

Hex-prism grids use axial XZ coordinates: `VoxelIndex.x = q`,
`VoxelIndex.z = r`, and `VoxelIndex.y = layer`. `FlatTop` and `PointyTop`
change only the fixed-point projection. Mixed rectangular/hex grids can live in
one `GridWorld`; direct mixed voxel bridging is exposed as a contact query over
world-space voxel footprint AABBs rather than as rectangular or hex direction
slots.

## Query Architecture

GridForge uses two different query scales:

### Coarse query scale

Handled through the world's spatial hash.

### Fine query scale

Handled locally through voxels and scan cells inside a `VoxelGrid`.

That split is one of the library's most important performance decisions.

## Mutation Architecture

| Mutation Type | Main Entry Point | State Touched |
| --- | --- | --- |
| Obstacles | `GridObstacleManager` | Voxel obstacle tokens/counts, grid obstacle count, grid version, events |
| Occupants | `GridOccupantManager` | Voxel occupant counts, scan-cell buckets, active scan cells, events |
| Region blockers | `Blocker` / `BoundsBlocker` | Traced coverage across one or more grids, obstacle application/removal |
| Partitions | `Voxel.TryAddPartition(...)` | Typed metadata or behavior attached directly to a voxel |

## Event And Version Model

GridForge uses both events and version numbers to express change.

### Events are used for

- grid add/remove/reset notifications inside a world
- grid change notifications after meaningful mutations
- voxel obstacle and occupant notifications
- blocker apply/remove notifications

### Version values are used for

- tracking world-level and grid-level mutation history
- helping dependent systems know when cached interpretations may be stale
- tagging voxel state with the grid version it was created or last synchronized against

## Neighbor Architecture

Neighbor handling is split into two related but distinct problems:

- `VoxelGrid` tracks neighboring grids by topology-local neighbor slot, and each slot can contain more than one grid.
- `Voxel` exposes rectangular lookup through `RectangularDirection` and hex lookup through `HexDirection`.
- Rectangular full-neighbor lookup covers 26 directions. Hex full-neighbor lookup covers 20 directions, with `Primary`, `Planar`, `Vertical`, layer, and vertical-diagonal subsets exposed through the direction utilities.
- Mixed rectangular/hex lookup uses `GetMixedTopologyNeighborsInto(...)` and
  `HasMixedTopologyNeighbor(...)`, returning one-to-many contacts from grids
  that use a different topology.

Boundary voxels bridge the two. When grids load or unload, `VoxelGrid.NotifyBoundaryChange(...)` invalidates neighbor caches only on the affected boundary slices instead of on every voxel.

`VoxelGrid.Neighbors` remains a same-topology grid-slot map. Mixed topology
queries use the world's spatial hash, derive a topology-aware candidate range
per target grid, and final-filter by fixed-point AABB overlap. This avoids
ambiguous direction slots and keeps sparse target grids configured-only.

## Coverage Architecture

`GridTracer` is the architectural bridge between world-space geometry and cell-level data.

It turns:

- lines into voxel coverage
- bounds into voxel coverage
- bounds into scan-cell coverage

That same utility underpins blockers, custom coverage queries, and scan-region enumeration.

Rectangular coverage uses rectangular index ranges. Hex-prism line tracing uses
axial/cube interpolation and deterministic rounding; hex bounds coverage uses a
conservative candidate range followed by cell-center reach checks. Callers still
use the same tracer APIs for both topologies.
