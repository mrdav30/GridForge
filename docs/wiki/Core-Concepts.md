# Core Concepts

This page defines the vocabulary of GridForge.

## The Concept Stack

GridForge is easiest to reason about as a stack of layers:

1. `GridWorld` owns world lifecycle, registration, lookup, and world-scoped
   identity.
2. `GridConfiguration` defines input bounds, topology metrics, storage kind, and
   scan-cell size for a grid.
3. `VoxelGrid` owns one grid's physical voxels, scan cells, neighbors, and
   versioned state.
4. `Voxel` is the per-cell unit of occupancy, obstacles, partitions, and
   adjacency.
5. `ScanCell` is the query-oriented overlay used to accelerate occupant scans.
6. Managers and utilities such as `GridScanManager`, `GridObstacleManager`,
   `GridOccupantManager`, and `GridTracer` mutate or query those structures.

## World Scope Is The Architectural Boundary

One of the most important concepts in GridForge is that runtime state is
coordinated through an explicit `GridWorld`.

`GridWorld` is responsible for:

- owning optional ordinary-lookup tuning for one world instance
- registering and removing grids
- adaptive indexing for fast grid lookup across ordinary and oversized grids
- resolving world positions or `WorldVoxelIndex` values back to active grids and
  voxels
- issuing a process-unique world token and world-local grid generations
- publishing world-scoped grid lifecycle events

## World Space, Grid Space, And Snapping

GridForge constantly moves between three different coordinate views:

| Coordinate View             | What It Represents                                                             | Common Type           |
| --------------------------- | ------------------------------------------------------------------------------ | --------------------- |
| World space                 | Absolute positions in your simulation or game world                            | `Vector3d`            |
| 2D XZ-plane query input     | Flat query coordinates projected to world X/Z with an explicit world Y layer   | `Vector2d` + `layerY` |
| Grid-local space            | Integer voxel coordinates inside one grid, interpreted by that grid's topology | `VoxelIndex`          |
| World-scoped voxel identity | A voxel coordinate plus its owning world and grid instance                     | `WorldVoxelIndex`     |

For 2D-friendly lookup APIs, GridForge treats `Vector2d(x, z)` as a convenience
projection over the same 3D runtime model: `Vector2d.X` maps to world X,
`Vector2d.Y` maps to world Z, and `layerY` maps to world Y. 2D radius scans use
XZ distance and reject occupants from other resolved Y layers. Flat simulation
occupants can store `Vector2d` position plus `Fixed64` height and expose
`IVoxelOccupant.Position` as the world-space projection GridForge consumes.
These overloads are intentionally not a separate 2D topology or grid type.

Snapping is a core behavior:

- `GridConfiguration` preserves ordered input bounds.
- `GridWorld` normalizes and snaps those bounds during registration.
- Voxel lookup converts a world position into a zero-based `VoxelIndex`.

Topology determines how that `VoxelIndex` should be read:

| Topology                            | `VoxelIndex` Meaning                                      |
| ----------------------------------- | --------------------------------------------------------- |
| `GridTopologyKind.RectangularPrism` | local rectangular `(x, y, z)` coordinates                 |
| `GridTopologyKind.HexPrism`         | axial `q` in `x`, vertical layer in `y`, axial `r` in `z` |

`HexOrientation.PointyTop` and `HexOrientation.FlatTop` change the fixed-point
projection between axial coordinates and world XZ. They do not imply a renderer
orientation or any engine-specific coordinate convention.

## `GridConfiguration`

`GridConfiguration` is the input contract for creating a grid.

It defines:

- `BoundsMin`
- `BoundsMax`
- derived `GridCenter`
- `ScanCellSize`
- `TopologyKind`
- `TopologyMetrics`
- `StorageKind`

Important details:

- bounds are ordered during construction, but not snapped until a world
  registers the grid
- `GridCenter` uses the full-domain nearest-even Q32.32 midpoint and does not
  saturate same-sign endpoints before halving
- `ScanCellSize` is expressed in voxels, not world units
- `TopologyKind` chooses rectangular-prism or hex-prism cells for this grid
- `TopologyMetrics` owns rectangular cell width/layer height/length or hex
  radius/layer height/orientation
- `StorageKind.Dense` allocates every in-bounds topology-local voxel
- `StorageKind.Sparse` allocates only explicitly configured topology-local
  voxels
- `ToBoundsKey()` creates an exact bounds geometry key
- `ToGridKey()` creates an exact snapped-bounds-and-topology configuration key

## `VoxelGrid`

`VoxelGrid` is the main container for a single registered grid.

Useful mental model:

- `GridWorld` answers "which grid?"
- `VoxelGrid` answers "which cell inside that grid?"
- `VoxelGrid.EnumerateVoxels()` iterates the physical voxels configured in the
  grid without exposing storage layout.

Dense grids configure every voxel in the normalized address space. Sparse grids
use the same bounds as an address space but only configured voxels physically
exist. Missing sparse voxels are intentional absence for lookup, tracing,
blockers, occupants, partitions, scan cells, and neighbor resolution.
Closest-voxel lookup is center-based and only considers physical voxels, while
closest-grid lookup is based on registered grid bounds. `GridWorld` closest
query methods can also take an optional `GridTopologyKind` filter when
mixed-topology worlds should resolve only rectangular-prism or hex-prism grids.

A single `GridWorld` can own rectangular-prism and hex-prism grids together.
Ordinary world/grid/voxel queries do not require callers to branch on topology.
Voxel contact queries use one primary `GetNeighborsInto(...)` API with
`VoxelNeighborScope` flags for source-grid, same-topology grid, mixed-topology
grid, or all contact neighbors. Directed lookup stays topology-specific through
`TryGetNeighbor(...)` overloads that accept `RectangularDirection` or
`HexDirection`, so rectangular and hex direction slots stay unambiguous. Hex
directions use axial labels such as `QPositive`, `QPositiveRNegative`, and
`RNegative` so the same directed API reads correctly for both `PointyTop` and
`FlatTop` grids.

## `Voxel`

`Voxel` is the core cell unit in GridForge.

A voxel tracks:

- its local coordinate through `VoxelIndex`
- its world-scoped coordinate through `WorldVoxelIndex`
- its world-space position
- obstacle count and obstacle tokens
- occupant count
- attached partitions
- boundary and contact-neighbor query behavior
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

Blockers are higher-level world-space objects that apply obstacle state to many
voxels at once.

Important blocker concepts:

- `Blocker` is the abstract base behavior
- `BoundsBlocker` blocks world-space `FixedBoundBox` regions
- `AreaBlocker` blocks X/Z-plane `FixedBoundArea` regions on one world Y layer
- blockers are bound to a `GridWorld`
- blockers use traced coverage to find the voxels they affect
- each active blocker registration owns a distinct process-unique
  `ObstacleToken`

### Occupants

Occupants are dynamic entities that live in voxels and are indexed through scan
cells.

`IVoxelOccupant` requires:

- a durable host-owned `GlobalId`
- a world-space `Position`
- an `OccupantGroupId`

## Partitions

Partitions are attachable pieces of typed metadata or behavior that live on a
voxel.

## Identity Types

GridForge uses several deliberately different identity categories:

| Category                 | Types                                                | Contract                                                                                   |
| ------------------------ | ---------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| Value keys               | `GridConfigurationKey`, `BoundsKey`                  | Describe configuration or geometry by value; they do not identify one allocation lifetime. |
| Recyclable slots         | `GridIndex`, `OccupantTicket.Slot`                   | Locate current storage efficiently, but can be reused after removal.                       |
| Exact runtime identities | `WorldVoxelIndex`, `ObstacleToken`, `OccupantTicket` | Include the generation or token needed to reject stale and foreign runtime state.          |
| Durable host identity    | `IVoxelOccupant.GlobalId`                            | Supplied and owned by the host for occupant ownership across runtime registrations.        |

World, grid, obstacle, and occupant generations are transient safety metadata.
They are not serialized state, durable save IDs, or authoritative ordering
inputs. Process-unique means unique for allocations in the current process, not
stable across later processes.

### `ObstacleToken`

`ObstacleToken` is an opaque transient identity for one obstacle registration
lifetime. Direct callers obtain tokens from `GridWorld.AllocateObstacleToken()`;
blockers allocate them internally. Tokens are process-unique, but their
allocation remains gated by an active owning world. They are not bounds, save
IDs, or authoritative ordering values.

### `OccupantTicket`

`OccupantTicket` combines a recyclable O(1) bucket `Slot` with a nonzero
process-unique registration `Generation`. GridForge issues the value when an
occupant is registered; stale, default, cross-world, pooled-cell, and pre-reset
tickets cannot resolve a later registration that reuses the slot.

### `IVoxelOccupant.GlobalId`

`GlobalId` is the host-owned durable occupant identifier used by GridForge's
world-local occupancy registry. It identifies the occupant, while
`OccupantTicket` identifies one transient scan-cell registration lifetime.

### `VoxelIndex`

`VoxelIndex` is the local coordinate of a voxel inside one grid. Rectangular
grids interpret it as `(x, y, z)`. Hex grids interpret it as axial
`(q, layer, r)`, stored as `(x, y, z)` so existing world/grid/voxel identity
types stay compact and deterministic.

### `WorldVoxelIndex`

`WorldVoxelIndex` ties a voxel coordinate to:

- the owning world instance (`WorldSpawnToken`)
- the world-local grid slot (`GridIndex`)
- the concrete runtime grid allocation (`GridSpawnToken`)
- the voxel's local coordinate (`VoxelIndex`)

The world token is process-unique and the grid generation is world-local. The
combined value is an exact current-runtime identity, but it must be revalidated
after removal or reset and should not be persisted as content identity.

As a rule:

- local work uses `VoxelIndex`
- cross-system references use `WorldVoxelIndex`
