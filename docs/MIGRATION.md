# Migrating From GridForge v6 To v7

This guide is for projects moving from `v6.0.6` to the v7 release line.
The v7 release is intentionally breaking because GridForge now supports
per-grid topology, sparse storage, hex-prism grids, 2D-friendly projection
helpers, topology-aware neighbor queries, closest-voxel helpers, and
engine-agnostic diagnostics.

The short version:

1. Move voxel cell geometry from `GridWorld` to `GridConfiguration`.
2. Replace dense-array access with storage-neutral traversal.
3. Replace `SpatialDirection`/cached neighbor APIs with contact queries or
   topology-specific directed lookup.
4. Decide whether each grid is dense or sparse, rectangular or hex.
5. Move debug/render adapter loops to `GridDiagnostics`.
6. Re-run coverage, blocker, scan, and neighbor tests because sparse and hex
   behavior is more precise than the old dense rectangular model.

## Package And Build Notes

GridForge still targets `netstandard2.1` and `net8.0`.

Package dependencies moved to the v5 lower-stack family:

- `FixedMathSharp` / `FixedMathSharp.Lean` `5.0.1`
- `SwiftCollections` / `SwiftCollections.Lean` `5.0.1`
- `SwiftCollections.FixedMathSharp` / `.Lean` `5.0.1`
- `System.Text.Json` `10.0.9` for the `netstandard2.1` target

If you build GridForge from source, the repository now includes `global.json`
with the .NET 10 SDK line and uses platform-scoped `obj` folders through
`Directory.Build.props`. Package consumers do not need to mirror the repo
layout; they only need compatible target frameworks and package dependencies.

## Grid Cell Size Moved Out Of `GridWorld`

In v6, a `GridWorld` owned one voxel size for every grid in that world:

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld(voxelSize: new Fixed64(2));
GridConfiguration config = new GridConfiguration(boundsMin, boundsMax);
```

In v7, each grid owns its topology metrics through `GridConfiguration`.
Default dense rectangular grids still use 1x1x1 cells, so many simple
configurations continue to work unchanged.

For a rectangular grid with a custom cubic cell size:

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;

using GridWorld world = new GridWorld();

GridConfiguration config = new GridConfiguration(
    boundsMin,
    boundsMax,
    topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(2)));

world.TryAddGrid(config, out ushort gridIndex);
```

For rectangular cells with independent X, Y, and Z edges:

```csharp
GridConfiguration config = new GridConfiguration(
    boundsMin,
    boundsMax,
    topologyMetrics: GridTopologyMetrics.Rectangular(
        cellWidth: new Fixed64(2),
        layerHeight: Fixed64.One,
        cellLength: new Fixed64(3)));
```

Replace these v6 world-level APIs:

| v6 | v7 |
| --- | --- |
| `new GridWorld(voxelSize: ...)` | `new GridWorld(spatialGridCellSize: ...)` plus per-grid `GridTopologyMetrics` |
| `GridWorld.DefaultVoxelSize` | `GridWorld.DefaultRectangularCellSize` |
| `world.VoxelSize` | `grid.Configuration.TopologyMetrics` |
| `world.VoxelResolution` | derive from the relevant grid topology metrics only if your app still needs it |
| `world.FloorToVoxelSize(...)` / `CeilToVoxelSize(...)` | `grid.FloorToGrid(...)` / `grid.CeilToGrid(...)` after resolving a grid |
| `world.SnapBoundsToVoxelSize(...)` | let `GridWorld.TryAddGrid(...)` normalize bounds through the selected topology |

`GridConfiguration.ToBoundsKey()` still exists, but v7 duplicate-grid identity
uses topology-aware `GridConfigurationKey` internally. Two grids with matching
bounds but different topology or metrics are not the same grid configuration.
If you accessed `world.BoundsTracker` directly, its key type is now
`GridConfigurationKey`; use `configuration.ToGridKey()` for matching
topology-aware identity.

## Dense Storage Is No Longer The Public Model

`VoxelGrid.Voxels` is no longer public. It was dense-only and does not describe
sparse grids or future storage strategies.

Replace direct dense-array access:

```csharp
// v6
for (int x = 0; x < grid.Width; x++)
{
    for (int y = 0; y < grid.Height; y++)
    {
        for (int z = 0; z < grid.Length; z++)
        {
            Voxel voxel = grid.Voxels[x, y, z];
            Process(voxel);
        }
    }
}
```

with storage-neutral traversal:

```csharp
// v7
foreach (Voxel voxel in grid.EnumerateVoxels())
{
    Process(voxel);
}
```

Use these properties and methods instead of assuming dense storage:

| Need | v7 API |
| --- | --- |
| physical voxel count | `grid.ConfiguredVoxelCount` |
| address-space dimensions | `grid.Width`, `grid.Height`, `grid.Length`, `grid.Size` |
| storage kind | `grid.StorageKind` |
| physical voxel check | `grid.ContainsVoxel(index)` |
| physical voxel lookup | `grid.TryGetVoxel(index, out Voxel? voxel)` |
| physical voxel traversal | `grid.EnumerateVoxels()` |

For dense grids, `ConfiguredVoxelCount == Size`. For sparse grids,
`Size` is the address-space size and `ConfiguredVoxelCount` is the number of
physical voxels that exist.

## Sparse Grid Storage

Dense storage remains the default. Sparse storage is opt-in:

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;

GridConfiguration sparseConfig = new GridConfiguration(
    boundsMin,
    boundsMax,
    storageKind: GridStorageKind.Sparse);

VoxelIndex[] configuredVoxels =
{
    new VoxelIndex(0, 0, 0),
    new VoxelIndex(2, 0, 1),
    new VoxelIndex(4, 0, 4)
};

world.TryAddGrid(sparseConfig, configuredVoxels, out ushort gridIndex);
```

Sparse grids use bounds as an address space. Only configured voxels physically
exist.

Important behavior changes when you choose sparse storage:

- `world.TryGetGrid(position, out grid)` may succeed for an in-bounds sparse
  address.
- `world.TryGetGridAndVoxel(position, out grid, out voxel)` returns `false`
  when that sparse address is not configured.
- `GridTracer.GetCoveredVoxels(...)` returns configured sparse voxels only.
- blockers affect covered configured voxels only.
- occupants and partitions require a configured physical voxel.
- missing sparse neighbors are absent.
- reads never materialize missing sparse voxels.

Runtime sparse mutation is explicit:

```csharp
if (grid.TryAddVoxel(new VoxelIndex(3, 0, 3), out Voxel? added))
{
    // The physical voxel now exists.
}

bool removed = grid.TryRemoveVoxel(new VoxelIndex(2, 0, 1));
```

Removal fails when the voxel has occupants, obstacle state, partitions, or
active voxel event subscribers. Adding a sparse voxel under an active blocker
reconciles blocker coverage so the new voxel receives the correct obstacle
state.

For sparse authoring masks, use:

```csharp
bool[,,] mask = new bool[gridWidth, gridHeight, gridLength];
mask[0, 0, 0] = true;
mask[2, 0, 1] = true;

world.TryAddGrid(sparseConfig, mask, out ushort maskGridIndex);
```

The mask dimensions must match the normalized grid dimensions.

## Hex-Prism Topology

Rectangular-prism topology remains the default. Hex-prism grids are configured
per grid:

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;

GridConfiguration hexConfig = new GridConfiguration(
    boundsMin,
    boundsMax,
    topologyKind: GridTopologyKind.HexPrism,
    topologyMetrics: GridTopologyMetrics.Hex(
        cellRadius: new Fixed64(2),
        layerHeight: Fixed64.One,
        hexOrientation: HexOrientation.PointyTop));
```

Hex-prism `VoxelIndex` values use axial coordinates in the XZ plane:

| Field | Meaning |
| --- | --- |
| `VoxelIndex.x` | axial `q` |
| `VoxelIndex.y` | vertical layer |
| `VoxelIndex.z` | axial `r` |

Both `HexOrientation.PointyTop` and `HexOrientation.FlatTop` are supported.
Orientation affects fixed-point world projection only. It is not a renderer or
engine setting.

A single `GridWorld` can own rectangular and hex grids together. Lookup,
tracing, coverage, blockers, occupants, scans, closest queries, and diagnostic
queries stay world/grid/voxel based.

Sparse hex grids are supported too. Configure sparse hex voxels with
topology-local axial `(q, layer, r)` `VoxelIndex` values.

## Neighbor API Changes

v6 exposed rectangular-only neighbor APIs around `SpatialDirection`,
`SpatialAwareness`, cached enumerable results, and public `useCache` arguments.

v7 splits neighbor queries by intent:

- Contact queries answer: which physical voxels touch this voxel?
- Directed lookups answer: which same-topology voxel exists in this direction?

### Contact Queries

Use `GetNeighborsInto(...)` for topology-neutral contact discovery:

```csharp
using GridForge.Spatial;
using SwiftCollections;

SwiftList<Voxel> neighbors = new SwiftList<Voxel>();

voxel.GetNeighborsInto(
    ownerGrid,
    neighbors,
    VoxelNeighborScope.All);
```

`VoxelNeighborScope` lets you choose source-grid, same-topology-grid,
mixed-topology-grid, or all contact candidates. Results are written into
caller-owned storage and are deterministic.

### Directed Same-Topology Lookup

For exact rectangular-prism directions:

```csharp
if (voxel.TryGetNeighbor(
        ownerGrid,
        RectangularDirection.East,
        out Voxel? east))
{
    Use(east);
}
```

For exact hex-prism directions:

```csharp
if (voxel.TryGetNeighbor(
        ownerGrid,
        HexDirection.QPositive,
        out Voxel? qPositive))
{
    Use(qPositive);
}
```

For direction-labeled no-allocation enumeration:

```csharp
SwiftList<(RectangularDirection Direction, Voxel Voxel)> rectangular = new();
voxel.GetRectangularNeighborsInto(ownerGrid, rectangular);

SwiftList<(HexDirection Direction, Voxel Voxel)> hex = new();
voxel.GetHexNeighborsInto(ownerGrid, hex);
```

### Direction Type Replacements

| v6 | v7 |
| --- | --- |
| `SpatialDirection` | `RectangularDirection` for rectangular grids |
| `SpatialAwareness` | `RectangularDirectionUtility` |
| mutable direction arrays | `ReadOnlySpan` utility properties |
| `GridDirectionUtility` | `VoxelGrid.GetRectangularNeighborDirection(...)` or `VoxelGrid.GetHexNeighborDirection(...)` |
| `voxel.GetNeighbors(ownerGrid, useCache)` | `voxel.GetNeighborsInto(ownerGrid, results, scope)` |
| `voxel.TryGetNeighborFromDirection(...)` | `voxel.TryGetNeighbor(ownerGrid, RectangularDirection/HexDirection, out voxel)` |
| public neighbor cache controls | removed; caching is internal implementation detail only when justified |

Hex direction names are axial and orientation-neutral. For example,
`HexDirection.QPositive` and `HexDirection.RNegative` describe axial offsets
for both pointy-top and flat-top grids.

## 2D-Friendly Query APIs

v7 adds `Vector2d` convenience overloads for flat XZ simulations. GridForge is
still a 3D runtime internally.

The mapping is:

```text
Vector2d.X -> world X
Vector2d.Y -> world Z
layerY     -> world Y
```

Examples:

```csharp
using FixedMathSharp;
using GridForge;
using GridForge.Blockers;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;

Vector2d position = new Vector2d(10, 20);
Fixed64 layerY = Fixed64.Zero;

world.TryGetVoxel(position, layerY, out Voxel? voxel);

foreach (GridVoxelSet covered in GridTracer.TraceLine(
             world,
             new Vector2d(0, 0),
             new Vector2d(10, 10),
             layerY: layerY))
{
    // Consume covered.Voxels immediately; it is pooled query storage.
}

SwiftList<IVoxelOccupant> occupants = new SwiftList<IVoxelOccupant>();
GridScanScratch scratch = new GridScanScratch();

GridScanManager.ScanRadiusInto(
    world,
    position,
    new Fixed64(5),
    occupants,
    scratch,
    layerY);

BoundsBlocker blocker = new BoundsBlocker(
    world,
    new Vector2d(0, 0),
    new Vector2d(5, 5),
    layerY);
```

`GridPlane2d` is available when you need explicit projection helpers:

```csharp
Vector3d worldPosition = GridPlane2d.ToWorld(position, layerY);
Vector2d flatPosition = GridPlane2d.FromWorld(worldPosition);
```

`GridTracer.TraceLine(Vector2d, Vector2d, ...)` preserved the old `padding` and
`includeEnd` positional argument order. The new `layerY` argument is appended
at the end, so prefer named `layerY:` calls when you use it.

2D radius scans are layer-locked XZ circle scans. They reject occupants on
other world-Y layers before applying the XZ distance check.

## Closest Grid And Voxel Queries

Sparse grids make strict lookup and closest lookup different in useful ways.

Use strict lookup when the position must resolve to an existing physical voxel:

```csharp
bool found = world.TryGetGridAndVoxel(position, out VoxelGrid? grid, out Voxel? voxel);
```

Use closest lookup when the query should snap to nearest bounds or nearest
physical voxel:

```csharp
world.TryGetClosestGrid(position, out VoxelGrid? closestGrid);
world.TryGetClosestVoxel(position, out Voxel? closestVoxel);
world.TryGetClosestGridAndVoxel(position, out VoxelGrid? resolvedGrid, out Voxel? resolvedVoxel);
```

Closest queries can take an optional `GridTopologyKind` filter when a mixed
rectangular/hex world should consider only one topology family.

## Tracing, Coverage, Blockers, And Scans

These workflows are now topology-aware and storage-neutral:

- rectangular dense grids preserve the old behavior
- sparse grids return configured voxels only
- hex grids use topology-aware projection, line tracing, and conservative
  bounds coverage
- mixed rectangular/hex worlds can be queried through the same world APIs

For downstream broad-phase code that previously called
`SnapBoundsToVoxelSize(...)`, `FloorToVoxelSize(...)`, or
`CeilToVoxelSize(...)`, prefer the highest-level API that fits the job:

- Use `GridTracer.GetCoveredVoxels(...)` or `GetCoveredVoxelsInto(...)` when
  you need the actual covered physical voxels across a `GridWorld`.
- Use `VoxelGrid.NormalizeBounds(...)` when you already own a specific grid and
  need topology-aligned bounds for cache keys, diagnostics, or custom traversal.
- Use `VoxelGrid.FloorToGrid(...)` and `CeilToGrid(...)` for individual
  topology-aligned positions.

Hot-path callers should prefer `GetCoveredVoxelsInto(...)` with a reusable
`SwiftList<Voxel>` and `GridTraceScratch` to avoid enumerable and pooled
grouped-list lifetime costs.

If your v6 tests asserted exact trace or blocker coverage near grid
intersections, boundaries, or sparse-style missing regions, re-run them. v7
includes fixes for trace-line candidate filtering, clamped boundary voxel
inclusion, and snapping intersected grids from the global trace start.

`GridVoxelSet.Voxels` is still pooled query storage. Consume it inside the
enumeration that produced it.

## Diagnostics And Adapter Migration

v7 adds `GridForge.Diagnostics` for tools, tests, editor overlays, and renderer
adapters. Diagnostics are descriptors and geometry helpers only. Rendering
stays outside GridForge core.

Replace dense debug loops like this:

```csharp
// v6-style dense debugger loop
for (int x = 0; x < grid.Width; x++)
{
    for (int y = 0; y < grid.Height; y++)
    {
        for (int z = 0; z < grid.Length; z++)
        {
            if (grid.TryGetVoxel(x, y, z, out Voxel? voxel))
            {
                Draw(voxel);
            }
        }
    }
}
```

with a diagnostic query:

```csharp
using GridForge.Diagnostics;

GridDiagnosticScratch scratch = new GridDiagnosticScratch();
GridDiagnosticQuery query = GridDiagnosticQuery.AllPhysical();
DrawCellVisitor visitor = new DrawCellVisitor();

GridDiagnostics.VisitCells(world, query, ref visitor, scratch);
```

Example visitor:

```csharp
using System;
using FixedMathSharp;
using GridForge.Diagnostics;

private struct DrawCellVisitor : IGridDiagnosticCellVisitor
{
    public bool Visit(in GridDiagnosticCell cell)
    {
        Span<Vector3d> vertices = stackalloc Vector3d[
            GridDiagnosticGeometry.HexPrismVertexCount];

        int vertexCount = GridDiagnosticGeometry.WriteVertices(in cell, vertices);
        ReadOnlySpan<GridDiagnosticEdge> edges =
            GridDiagnosticGeometry.GetEdges(cell.TopologyKind);

        WriteAdapterCell(in cell, vertices.Slice(0, vertexCount), edges);
        return true;
    }

    private static void WriteAdapterCell(
        in GridDiagnosticCell cell,
        ReadOnlySpan<Vector3d> vertices,
        ReadOnlySpan<GridDiagnosticEdge> edges)
    {
        // Convert into adapter-owned buffers here.
    }
}
```

Use `GetCellsInto(...)` when a test or tool wants a materialized list:

```csharp
using GridForge.Diagnostics;
using SwiftCollections;

SwiftList<GridDiagnosticCell> cells = new SwiftList<GridDiagnosticCell>();
GridDiagnosticScratch scratch = new GridDiagnosticScratch();

GridDiagnostics.GetCellsInto(
    world,
    GridDiagnosticQuery.AllPhysical(),
    cells,
    scratch);
```

For sparse-hole visualizers, opt in explicitly and bound the query:

```csharp
GridDiagnosticQuery query = new GridDiagnosticQuery(
    addressMode: GridDiagnosticAddressMode.PhysicalAndMissing,
    boundsMin: boundsMin,
    boundsMax: boundsMax,
    maxCells: 4096);
```

Missing sparse address cells are descriptors only. They are not runtime
`Voxel` instances and do not resolve through `TryResolvePhysicalCell(...)`.

For incremental adapters, use `GridDiagnosticSession` to capture grid, sparse,
obstacle, and occupant changes:

```csharp
using GridForge.Diagnostics;
using SwiftCollections;

using GridDiagnosticSession session = new GridDiagnosticSession(world);
SwiftList<GridDiagnosticChange> changes = new SwiftList<GridDiagnosticChange>();

session.GetDirtyChangesInto(changes);
session.ClearDirtyChanges();
```

`GridEventInfo` now also exposes `ChangeKind`, `VoxelIndex`,
`AffectedBoundsMin`, and `AffectedBoundsMax`. Existing subscribers can keep
using grid identity and bounds, while adapters that track sparse add/remove or
dirty regions can use the new fields.

## Serialization Notes

`GridConfiguration` now serializes:

- `TopologyKind`
- `TopologyMetrics`
- `StorageKind`

If you persist `GridConfiguration` values from v6, add defaults during your
own save migration:

```csharp
GridTopologyKind topologyKind = GridTopologyKind.RectangularPrism;
GridTopologyMetrics topologyMetrics =
    GridTopologyMetrics.Rectangular(GridWorld.DefaultRectangularCellSize);
GridStorageKind storageKind = GridStorageKind.Dense;
```

For sparse grids, persist your configured voxel set separately from
`GridConfiguration`, then pass it to the sparse `TryAddGrid(...)` overload when
reconstructing the world.

## Recommended Migration Order

1. Update package references and restore.
2. Replace `GridWorld` voxel-size construction with per-grid topology metrics.
3. Replace `VoxelGrid.Voxels` access with `EnumerateVoxels()`, `TryGetVoxel(...)`,
   `ContainsVoxel(...)`, or diagnostics.
4. Replace `SpatialDirection` and cached neighbor APIs with contact queries and
   topology-specific directed lookup.
5. Keep existing grids dense rectangular unless you intentionally opt into
   sparse storage or hex topology.
6. Migrate debug/render adapters to `GridDiagnostics.VisitCells(...)`.
7. Re-run blocker, trace, scan, occupant, sparse, and neighbor tests in Debug,
   Release, and ReleaseLean.

## Related Documentation

- [Sparse Grid Storage](wiki/Sparse-Grid-Storage.md)
- [VoxelGrid and Voxel Model](wiki/VoxelGrid-and-Voxel-Model.md)
- [GridTracer and Coverage](wiki/GridTracer-and-Coverage.md)
- [Scan Cells and Query Flow](wiki/Scan-Cells-and-Query-Flow.md)
- [Grid Diagnostics and Geometry](wiki/Grid-Diagnostics-and-Geometry.md)
