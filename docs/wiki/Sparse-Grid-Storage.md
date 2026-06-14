# Sparse Grid Storage

Sparse storage lets a `VoxelGrid` use its registered bounds as an address space
without allocating every possible voxel inside those bounds.

## Dense Versus Sparse

Dense grid:

```text
bounds -> every topology-local voxel exists
```

Sparse grid:

```text
bounds -> address space
configured indices -> physical voxels that actually exist
```

This distinction is intentional. A missing in-bounds sparse voxel is not an
empty dense voxel. It is absent for lookup, tracing, blockers, occupants,
partitions, scan cells, and neighbor resolution.

## Create A Sparse Grid

Set `GridConfiguration.StorageKind` to `GridStorageKind.Sparse`, then pass the
configured local voxel indices when registering the grid.

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;

using GridWorld world = new GridWorld();

GridConfiguration config = new GridConfiguration(
    new Vector3d(0, 0, 0),
    new Vector3d(7, 0, 7),
    storageKind: GridStorageKind.Sparse);

VoxelIndex[] configuredVoxels =
{
    new VoxelIndex(0, 0, 0),
    new VoxelIndex(2, 0, 2),
    new VoxelIndex(7, 0, 7)
};

if (!world.TryAddGrid(config, configuredVoxels, out ushort gridIndex))
    throw new InvalidOperationException("Could not add sparse grid.");

VoxelGrid grid = world.ActiveGrids[gridIndex];
```

For rectangular authoring data, you can also pass a `bool[,,]` mask. The mask
uses `[x, y, z]` indexing and must match the grid's normalized
`Width`, `Height`, and `Length`.

```csharp
bool[,,] mask = new bool[8, 1, 8];
mask[0, 0, 0] = true;
mask[2, 0, 2] = true;
mask[7, 0, 7] = true;

world.TryAddGrid(config, mask, out ushort maskGridIndex);
```

Configured indices outside the normalized grid dimensions fail grid
registration. Duplicate configured indices are de-duplicated deterministically.
Configured indices are topology-local: rectangular grids read them as
`(x, y, z)`, while hex-prism grids read them as `(q, layer, r)`.

Sparse hex grids use the same storage rules. The grid bounds remain a
world-space address space, while the configured indices identify the physical
axial cells that exist. Choose bounds large enough for the authored axial range;
configuration normalization resolves the exact grid dimensions.

```csharp
GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(
    new Fixed64(2),
    Fixed64.One,
    HexOrientation.PointyTop);
GridConfiguration sparseHexConfig = new GridConfiguration(
    Vector3d.Zero,
    new Vector3d(24, 0, 16),
    topologyKind: GridTopologyKind.HexPrism,
    topologyMetrics: hexMetrics,
    storageKind: GridStorageKind.Sparse);

VoxelIndex[] configuredHexes =
{
    new VoxelIndex(0, 0, 0),
    new VoxelIndex(1, 0, 0),
    new VoxelIndex(4, 0, 4)
};

world.TryAddGrid(sparseHexConfig, configuredHexes, out ushort sparseHexGridIndex);
```

## Lookup Semantics

`TryGetGrid(...)` answers whether a world position falls inside a registered
grid's bounds. Sparse grids still have bounds, so this can succeed for missing
sparse cells.

`TryGetVoxel(...)` and `TryGetGridAndVoxel(...)` require a physical voxel. They
return `false` when the addressed sparse voxel was not configured.

```csharp
Vector3d missingPosition = new Vector3d(1, 0, 1);

bool gridFound = world.TryGetGrid(missingPosition, out VoxelGrid resolvedGrid);
bool voxelFound = world.TryGetGridAndVoxel(missingPosition, out _, out _);

Console.WriteLine(gridFound);  // true
Console.WriteLine(voxelFound); // false
```

Useful grid-level helpers:

- `grid.StorageKind` tells whether the grid uses dense or sparse storage.
- `grid.ConfiguredVoxelCount` reports physical voxels. Dense grids report
  `Size`; sparse grids report the configured count.
- `grid.ContainsVoxel(index)` checks whether a physical voxel exists at the
  local index.
- `grid.EnumerateVoxels()` iterates physical voxels in deterministic order
  without exposing the storage layout.

## Runtime Sparse Mutation

Sparse grids support explicit runtime configuration changes:

```csharp
if (grid.TryAddVoxel(new VoxelIndex(3, 0, 3), out Voxel? added))
    Console.WriteLine(added.WorldPosition);

grid.TryRemoveVoxel(new VoxelIndex(2, 0, 2));
```

These APIs are sparse-only. Dense grids already contain every in-bounds voxel.

Removal is deliberately conservative. It fails when the target voxel is missing
or carries state that would make removal unsafe, such as occupants, obstacle
tokens, partitions, or active voxel event subscribers. Successful add/remove
operations update grid versioning, keep stateless neighbor lookup current, and
notify active world-grid watchers.

When a sparse voxel is added under an active blocker, blocker reconciliation
reapplies the overlapping blocker state so the new voxel is not left
incorrectly unblocked.

## Query Behavior

Sparse behavior is storage-neutral from the caller's point of view:

| Workflow | Sparse behavior |
| --- | --- |
| `GridTracer.GetCoveredVoxels(...)` | Returns covered configured voxels only |
| `GridTracer.GetCoveredScanCells(...)` | Returns scan cells that exist for configured sparse blocks |
| `BoundsBlocker.ApplyBlockage()` | Applies obstacle state only to covered configured voxels |
| Occupant registration | Requires the target configured voxel to exist and have vacancy |
| Partitions | Attach to configured voxels only |
| Neighbor lookup | Missing sparse neighbors are absent |
| Radius scans | Inspect covered configured scan cells, then active occupant buckets |

Sparse scan cells are block buckets for configured voxels, not proof that every
local coordinate inside the block is configured. Use `grid.ContainsVoxel(...)`
or `grid.TryGetVoxel(...)` when code needs to know whether a specific sparse
voxel physically exists.

Coverage result lifetime is the same as dense coverage: grouped
`GridVoxelSet.Voxels` lists are backed by pooled storage and should be consumed
inside the enumeration that produced them.

## When To Choose Sparse

Use sparse storage when:

- the address space is much larger than the number of cells you need
- most coverage queries should skip unconfigured regions
- construction memory matters more than dense contiguous layout
- missing cells should mean "not part of the grid," not "empty"

Use dense storage when:

- most cells inside the bounds are meaningful
- you need every in-bounds coordinate to resolve to a voxel
- hot paths benefit from contiguous dense layout

The benchmark suite includes a `sparse-voxel-grid` alias covering sparse
construction, configured and missing lookup, coverage, blockers, scans, and
dense comparison scenarios:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- sparse-voxel-grid --filter '*SparseVoxelGridBenchmarks*'
```

## Read This Next

- [VoxelGrid and Voxel Model](VoxelGrid-and-Voxel-Model.md) for physical voxel ownership
- [GridTracer and Coverage](GridTracer-and-Coverage.md) for coverage result behavior
- [Blockers and Obstacles](Blockers-and-Obstacles.md) for blocker reconciliation
- [Occupants and Partitions](Occupants-and-Partitions.md) for runtime state rules
