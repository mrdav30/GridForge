# Architecture Overview

This page is the high-level map of how GridForge is put together.

If you only need one sentence, it is this:

GridForge is a deterministic voxel-grid system where `GridWorld` owns one
isolated world's runtime state, `VoxelGrid` owns per-grid state, `Voxel` is the
core mutable cell model, `ScanCell` accelerates occupant queries, and managers
plus tracers provide mutation and query workflows on top of that state.

## Architectural Shape

GridForge is organized as a small set of cooperating layers:

| Layer                      | Main Types                                                                                                                   | Primary Responsibility                                                                      |
| -------------------------- | ---------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| World coordination         | `GridWorld`                                                                                                                  | World lifecycle, registration, adaptive top-level lookup, world events                      |
| Configuration and identity | `GridConfiguration`, `GridConfigurationKey`, `BoundsKey`, `ObstacleToken`, `OccupantTicket`, `VoxelIndex`, `WorldVoxelIndex` | Configuration and geometry values plus exact transient runtime identity                     |
| Per-grid storage           | `VoxelGrid`, `Voxel`, `ScanCell`, dense/sparse storage strategies                                                            | Core spatial data, local lookup, grid neighbors, occupancy and obstacle state               |
| Mutation services          | `GridObstacleManager`, `GridOccupantManager`, `Blocker`                                                                      | Safe state changes, events, and higher-level world-space mutations                          |
| Query services             | `GridScanManager`, `GridTracer`                                                                                              | Radius scans, filtered retrieval, line tracing, coverage enumeration                        |
| Extension and diagnostics  | `IVoxelOccupant`, `IVoxelPartition`, `PartitionProvider`, `GridForgeLogger`, `GridDiagnostics`                               | Domain integration, metadata hooks, logging, diagnostic cell projection, and dirty tracking |

## Repository Layout By Responsibility

| Path                           | Architectural Role                                                                    |
| ------------------------------ | ------------------------------------------------------------------------------------- |
| `src/GridForge/Configuration`  | Grid creation inputs and bounds identity                                              |
| `src/GridForge/Grids/Managers` | World orchestration and mutation/query manager APIs                                   |
| `src/GridForge/Grids/Nodes`    | Concrete runtime storage types: `Voxel` and `ScanCell`                                |
| `src/GridForge/Grids/Storage`  | Dense and sparse physical voxel storage strategies                                    |
| `src/GridForge/Grids/Topology` | Per-grid topology metrics, snapping, dimensions, and world/index projection           |
| `src/GridForge/Grids/Support`  | Pooled resources and event payload types                                              |
| `src/GridForge/Spatial`        | Shared coordinate, direction, occupant, and partition abstractions                    |
| `src/GridForge/Blockers`       | World-space obstacle application on top of tracer coverage                            |
| `src/GridForge/Diagnostics`    | Engine-agnostic diagnostic descriptors, topology geometry, and dirty adapter sessions |
| `src/GridForge/Support`        | Cross-cutting query result groupings like `GridVoxelSet`                              |
| `src/GridForge/Utility`        | Tracing and logging infrastructure                                                    |

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
- that world's optional ordinary-lookup cell size
- the active grid bucket
- exact-bounds duplicate tracking
- the adaptive index used for coarse grid lookup
- world versioning and grid-level events
- a process-unique runtime world token
- world-local grid generations
- active-gated allocation of process-unique obstacle registrations

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
- topology-aware neighbor query behavior

### `ScanCell` owns

- a grid-local cell key
- occupant-plus-generation buckets grouped by voxel
- generation-aware tickets used for O(1) retrieval and stale-slot rejection
- fast "is there anything here?" state for scan-oriented queries

## Why `GridWorld` Sits At The Top

GridForge is not architected as "a single process-wide world." It is architected
as "one or more isolated worlds, each of which may own many grids."

That is why `GridWorld` sits above everything else:

- it maps snapped bounds to a reusable world-local grid slot
- it maintains an adaptive coarse index for candidate grids
- it links neighboring grids when overlap is valid
- it resolves world-space and world-scoped voxel identities back to active
  runtime objects

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
  -> adaptive world-index registration
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
`VoxelIndex.z = r`, and `VoxelIndex.y = layer`. `FlatTop` and `PointyTop` change
only the fixed-point projection. Mixed rectangular/hex grids can live in one
`GridWorld`; direct mixed voxel bridging is exposed as a contact query over
world-space voxel footprint AABBs rather than as rectangular or hex direction
slots.

## Query Architecture

GridForge uses two different query scales:

### Coarse query scale

Handled through the world's adaptive top-level index.

### Fine query scale

Handled locally through voxels and scan cells inside a `VoxelGrid`.

That split is one of the library's most important performance decisions.

## Mutation Architecture

| Mutation Type   | Main Entry Point                            | State Touched                                                                          |
| --------------- | ------------------------------------------- | -------------------------------------------------------------------------------------- |
| Obstacles       | `GridObstacleManager`                       | Process-unique voxel obstacle tokens/counts, grid obstacle count, grid version, events |
| Occupants       | `GridOccupantManager`                       | Voxel occupant counts, generation-aware scan-cell buckets, active scan cells, events   |
| Region blockers | `Blocker` / `BoundsBlocker` / `AreaBlocker` | Traced coverage across one or more grids, obstacle application/removal                 |
| Partitions      | `Voxel.TryAddPartition(...)`                | Typed metadata or behavior attached directly to a voxel                                |

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
- tagging voxel state with the grid version it was created or last synchronized
  against

### Ordered committed changes

`GridWorld.OnChangeCommitted` is the storage-neutral feed for systems that must
reconcile grid state without rereading mutable live objects. Every grid
lifecycle, sparse-presence, and obstacle mutation receives a monotonically
increasing world-local `GridChangeStamp`. The matching exact obstacle event and
generic committed event carry the same cause ID and immutable post-mutation
counts. Handlers run after the mutation locks are released, and reentrant
mutations are queued and delivered in ascending sequence order.

Consumers that install addressed metadata can call
`TrySubscribeNavigationChanges(...)`. It attaches the handler and captures a
`GridNavigationBaseline` in one critical section. The request supplies the
exact normalized `GridConfigurationKey` and a strictly ascending, unique span
of topology-local `VoxelIndex` values. The baseline returns only those
addresses, the exact active grid generation, sparse presence, obstacle count,
the grid-local `GridHighWaterSequence`, and the same high-water sequence used by the feed.
The grid-local high-water changes only with that grid and lets a chunked consumer
detect target-grid mutation without restarting for unrelated world churn. Apply baseline state first,
then only committed events whose sequence is greater than that high-water mark.

Use `TryCaptureNavigationBaseline(...)` only when the caller already owns an
equivalent subscription protocol. Neither baseline API enumerates unrelated
grids or unrequested physical voxels. `GridIndex` is included for diagnostics;
the world token, grid generation, and normalized configuration key are the
identity boundary.

Consumers that already own one committed-change subscription and reconcile at
a deterministic maintenance boundary can use
`ExecuteNavigationMaintenanceSnapshot(...)` to detach their fixed event prefix
and capture all required address baselines against one frozen world state. Keep
the callback short and non-mutating; it runs while GridForge excludes grid and
voxel mutations. `TryCaptureNavigationBaseline(...)` is safe inside that
callback and does not recurse through the world locks.

Exact cross-grid composition uses the same gate through
`GridWorld.BeginBoundaryContacts(...)` and
`GridWorld.AdvanceBoundaryContacts(...)`. Grid registration maintains an exact
cell-prism-envelope BVH plus reciprocal sorted incident pair rows. A two-level
source bitset gives the cursor canonical pair discovery without scanning
recyclable grid slots, and target-envelope-derived address ranges avoid whole-
grid source scans. Upper/lower directory visits, pair selection, source
addresses, and exact target probes are all charged to the caller's candidate
budget. The cursor retains only scalar/value state between chunks and rejects
mixed committed generations as `Stale`.
The key-filtered begin path reads only the selected grid's reciprocal incident
rows, merging incoming then outgoing pairs in canonical order; row and pair
fetches remain separately probe-debited, independent of unrelated pair count.
Its value-only `GridBoundaryContact` output captures both canonical
configuration keys, and `GridBoundaryContactRunStamp` identifies the committed
world revision shared by a multi-cursor batch.

## Neighbor Architecture

Neighbor handling is split into two related but distinct problems:

- `VoxelGrid` tracks same-topology neighboring grids by topology-local neighbor
  slot, and each slot can contain more than one grid.
- `Voxel.GetNeighborsInto(...)` asks which physical voxels touch the source
  voxel, with `VoxelNeighborScope` selecting source-grid, same-topology grid,
  mixed-topology grid, or all contacts.
- `Voxel.TryGetNeighbor(...)` exposes exact directed lookup through
  `RectangularDirection` and `HexDirection` overloads.
- Rectangular full-neighbor lookup covers 26 directions. Hex full-neighbor
  lookup covers 20 directions, with `Primary`, `Planar`, `Vertical`, layer, and
  vertical-diagonal subsets exposed through the direction utilities.
- Hex direction names describe axial offsets (`QPositive`, `RNegative`, etc.)
  rather than world compass directions so pointy-top and flat-top grids share
  one unambiguous API.
- `Voxel.GetRectangularNeighborsInto(...)` and `Voxel.GetHexNeighborsInto(...)`
  fill caller-owned storage with direction-labeled same-topology results.

`VoxelGrid.Neighbors` remains a same-topology grid-slot accelerator, but public
contact discovery is resolved by `VoxelNeighborResolver`. Contact queries use
the world's adaptive index, derive a topology-aware candidate range per target
grid, and final-filter by fixed-point AABB overlap. This avoids ambiguous
direction slots, keeps sparse target grids configured-only, and reflects grid
load/unload or sparse mutation without per-voxel neighbor caches.

## Coverage Architecture

`GridTracer` is the architectural bridge between world-space geometry and
cell-level data.

It turns:

- lines into voxel coverage
- bounds into voxel coverage
- bounds into scan-cell coverage

That same utility underpins blockers, custom coverage queries, and scan-region
enumeration.

Rectangular coverage uses rectangular index ranges. Hex-prism line tracing uses
axial/cube interpolation and deterministic rounding; hex bounds coverage uses a
conservative candidate range followed by cell-center reach checks. Callers still
use the same tracer APIs for both topologies.
