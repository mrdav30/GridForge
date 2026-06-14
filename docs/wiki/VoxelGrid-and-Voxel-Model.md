# VoxelGrid and Voxel Model

This page explains the two core storage types at the center of GridForge:

- `VoxelGrid`, which owns one grid's runtime state
- `Voxel`, which represents one mutable cell inside that grid

If `GridWorld` is the world coordinator, these are the types that actually hold the world data.

## `VoxelGrid` In One Sentence

`VoxelGrid` is the runtime container for one snapped, registered region of world space.

It owns:

- the grid's bounds and dimensions
- the topology used to interpret local indices and world positions
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

## Topology Model

`VoxelGrid` keeps topology and storage as separate responsibilities.

| Topology | Local Index Meaning | Metrics |
| --- | --- | --- |
| Rectangular-prism | `VoxelIndex(x, y, z)` | cell width, layer height, and cell length |
| Hex-prism | `VoxelIndex(q, layer, r)` stored as `x`, `y`, `z` | horizontal radius, layer height, and `FlatTop` or `PointyTop` orientation |

Topology controls snapped bounds, dimensions, world-to-index lookup,
index-to-world projection, scan-cell keys, and boundary ranges. Storage controls
whether a physical voxel exists at a valid topology-local index.

One `GridWorld` can own rectangular and hex grids together. Ordinary lookup,
coverage, blocker, occupant, scan, and trace workflows still use
`GridWorld`, `VoxelGrid`, and `Voxel`; callers only need topology-specific
direction APIs when asking for voxel neighbors.

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
- `CachedGridVersion`

## Neighbor Model

Neighbor handling spans both `VoxelGrid` and `Voxel`.

- `VoxelGrid.Neighbors` stores same-topology neighboring grid ids by topology-local neighbor slot.
- `Voxel.GetNeighborsInto(...)` fills caller-owned storage with physical
  contact neighbors from the source grid, same-topology grids, mixed-topology
  grids, or all of them through `VoxelNeighborScope`.
- `Voxel.HasNeighbor(...)` performs the same contact query as a fast boolean
  check.
- `Voxel.TryGetNeighbor(...)` has rectangular and hex overloads for exact
  same-topology directed lookup.
- `Voxel.GetRectangularNeighborsInto(...)` and `Voxel.GetHexNeighborsInto(...)`
  fill caller-owned storage with direction-labeled same-topology neighbors.

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

Mixed rectangular/hex grids can coexist in one `GridWorld`. Direction-based
neighbor APIs remain same-topology only because rectangular and hex direction
sets do not have a stable one-to-one mapping. When behavior intentionally asks
"which physical voxels touch this voxel?", use the contact query:

```csharp
SwiftList<Voxel> neighbors = new SwiftList<Voxel>();
voxel.GetNeighborsInto(grid, neighbors, VoxelNeighborScope.All);
```

The contact query treats voxel footprint AABBs as the deterministic contact
model. It returns zero, one, or many physical voxels in deterministic grid/index
order. Sparse target grids return only configured voxels; missing sparse cells
are not materialized. Per-voxel neighbor result caches are not part of the
public model; sparse mutation and grid load/unload are reflected by resolving
against current world and grid state.

## Reset And Reuse

Both grids and voxels are pooled, so reset behavior is part of the architecture.

The architecture depends on reset being thorough because reused pooled objects come back through the same types later.
