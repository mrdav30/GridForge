# Common Workflows

This page is a task-oriented companion to [Getting Started](Getting-Started.md) and [Core Concepts](Core-Concepts.md). The examples here are intentionally small and practical.

Unless a snippet shows setup explicitly, assume you already have a valid `GridWorld world` and, where relevant, a `VoxelGrid grid` from that world.

## Workflow 1: Create A Grid And Keep A Handle To It

```csharp
using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld();

GridConfiguration config = new GridConfiguration(
    new Vector3d(-10, 0, -10),
    new Vector3d(10, 0, 10),
    scanCellSize: 8);

if (!world.TryAddGrid(config, out ushort gridIndex))
    throw new InvalidOperationException("Could not add grid.");

VoxelGrid grid = world.ActiveGrids[gridIndex];
```

## Workflow 2: Create A Sparse Grid

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;

GridConfiguration sparseConfig = new GridConfiguration(
    new Vector3d(0, 0, 0),
    new Vector3d(7, 0, 7),
    storageKind: GridStorageKind.Sparse);

VoxelIndex[] configured =
{
    new VoxelIndex(0, 0, 0),
    new VoxelIndex(2, 0, 2),
    new VoxelIndex(7, 0, 7)
};

if (!world.TryAddGrid(sparseConfig, configured, out ushort sparseGridIndex))
    throw new InvalidOperationException("Could not add sparse grid.");

VoxelGrid sparseGrid = world.ActiveGrids[sparseGridIndex];
```

Sparse grid bounds still participate in world-level grid lookup, but only
configured voxels exist. `world.TryGetGrid(...)` can resolve an in-bounds sparse
position while `world.TryGetGridAndVoxel(...)` fails when the local sparse
voxel is missing.

## Workflow 3: Create A Hex-Prism Grid

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;

GridConfiguration hexConfig = new GridConfiguration(
    new Vector3d(16, 0, 0),
    new Vector3d(32, 0, 16),
    scanCellSize: 4,
    topologyKind: GridTopologyKind.HexPrism,
    topologyMetrics: GridTopologyMetrics.Hex(
        new Fixed64(2),
        Fixed64.One,
        HexOrientation.PointyTop));

if (!world.TryAddGrid(hexConfig, out ushort hexGridIndex))
    throw new InvalidOperationException("Could not add hex grid.");

VoxelGrid hexGrid = world.ActiveGrids[hexGridIndex];
```

Hex grids use the same `GridWorld`, `VoxelGrid`, `Voxel`, blocker, occupant,
scan, and trace workflows as rectangular grids. The local index is axial:
`VoxelIndex.x = q`, `VoxelIndex.y = layer`, and `VoxelIndex.z = r`.

## Workflow 4: Resolve World Space Into A Grid And Voxel

```csharp
using FixedMathSharp;
using GridForge.Grids;

Vector3d position = new Vector3d(2, 0, -3);

if (world.TryGetGridAndVoxel(position, out VoxelGrid resolvedGrid, out Voxel voxel))
{
    Console.WriteLine($"Grid: {resolvedGrid.GridIndex}");
    Console.WriteLine($"Voxel: {voxel.Index}");
}
```

Choose the lookup based on what you need:

- `world.TryGetGrid(...)`
- `world.TryGetVoxel(...)`
- `world.TryGetGridAndVoxel(...)`

For flat XZ simulations, these lookup helpers also accept `Vector2d` positions.
`Vector2d.X` maps to world X, `Vector2d.Y` maps to world Z, and `layerY`
selects the world Y layer. Omitting `layerY` resolves on world Y `0`.

```csharp
Vector2d flatPosition = new Vector2d(2, -3);

if (world.TryGetVoxel(flatPosition, out Voxel defaultLayerVoxel))
{
    Console.WriteLine($"Default-layer voxel: {defaultLayerVoxel.Index}");
}

if (world.TryGetGridAndVoxel(flatPosition, (Fixed64)1, out VoxelGrid flatGrid, out Voxel flatVoxel))
{
    Console.WriteLine($"Grid: {flatGrid.GridIndex}");
    Console.WriteLine($"Voxel: {flatVoxel.Index}");
}
```

## Workflow 5: Move Between World Space And Grid-Local Coordinates

```csharp
using FixedMathSharp;
using GridForge.Spatial;

Vector3d worldPosition = new Vector3d(1.25, 0, 3.75);

if (grid.TryGetVoxelIndex(worldPosition, out VoxelIndex index))
{
    Vector3d snappedDown = grid.FloorToGrid(worldPosition);
    Vector3d snappedUp = grid.CeilToGrid(worldPosition);
}
```

For rectangular grids, the returned `VoxelIndex` is `(x, y, z)`. For hex grids,
it is `(q, layer, r)` stored in the same `x`, `y`, and `z` fields.

## Workflow 6: Register Occupants And Scan For Nearby Results

```csharp
using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;

public sealed class UnitOccupant : IVoxelOccupant
{
    private Vector2d _position2d;
    private Fixed64 _height;

    public Guid GlobalId { get; } = Guid.NewGuid();
    public byte OccupantGroupId { get; }

    public Vector2d Position2d
    {
        get => _position2d;
        set => _position2d = value;
    }

    public Fixed64 Height
    {
        get => _height;
        set => _height = value;
    }

    public Vector3d Position
    {
        get => _position2d.ToVector3d(_height);
        set
        {
            _position2d = new Vector2d(value.X, value.Z);
            _height = value.Y;
        }
    }

    public UnitOccupant(Vector2d position2d, Fixed64 height, byte occupantGroupId)
    {
        _position2d = position2d;
        _height = height;
        OccupantGroupId = occupantGroupId;
    }
}

UnitOccupant ally = new UnitOccupant(new Vector2d(1, 1), Fixed64.Zero, 1);
UnitOccupant enemy = new UnitOccupant(new Vector2d(3, 3), Fixed64.Zero, 2);

GridOccupantManager.TryRegister(world, ally);
GridOccupantManager.TryRegister(world, enemy);

foreach (IVoxelOccupant occupant in GridScanManager.ScanRadius(world, new Vector3d(0, 0, 0), (Fixed64)5))
{
    Console.WriteLine(occupant.Position);
}
```

For flat XZ scans, pass a `Vector2d` center and optional `layerY`. The scan
uses XZ distance and rejects occupants on other Y layers.

```csharp
foreach (IVoxelOccupant occupant in GridScanManager.ScanRadius(world, new Vector2d(0, 0), (Fixed64)5, layerY: Fixed64.Zero))
{
    Console.WriteLine(occupant.Position);
}
```

For flat simulations, an occupant can store its native state as
`Vector2d Position2d` plus `Fixed64 Height` and expose `Position` as the
world-space projection that GridForge uses for registration and scan filtering.

## Workflow 7: Apply And Remove A Bounds-Based Blocker

```csharp
using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Blockers;

FixedBoundArea blockedArea = new FixedBoundArea(
    new Vector3d(1, 0, 1),
    new Vector3d(3, 0, 3));

BoundsBlocker blocker = new BoundsBlocker(world, blockedArea);

blocker.ApplyBlockage();
blocker.RemoveBlockage();
```

For flat XZ blockers, pass `Vector2d` bounds and optional `layerY`:

```csharp
BoundsBlocker flatBlocker = new BoundsBlocker(
    world,
    new Vector2d(1, 1),
    new Vector2d(3, 3),
    layerY: Fixed64.Zero,
    cacheCoveredVoxels: true);

flatBlocker.ApplyBlockage();
flatBlocker.RemoveBlockage();
```

## Workflow 8: Trace Covered Voxels For Custom Processing

```csharp
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;

Vector3d areaMin = new Vector3d(-2, 0, -2);
Vector3d areaMax = new Vector3d(2, 0, 2);

foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(world, areaMin, areaMax))
{
    foreach (Voxel voxel in covered.Voxels)
        Console.WriteLine(voxel.WorldPosition);
}
```

For flat XZ workflows, pass `Vector2d` bounds and an optional `layerY`:

```csharp
Vector2d flatMin = new Vector2d(-2, -2);
Vector2d flatMax = new Vector2d(2, 2);

foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(world, flatMin, flatMax, layerY: Fixed64.Zero))
{
    foreach (Voxel voxel in covered.Voxels)
        Console.WriteLine(voxel.WorldPosition);
}
```

Consume each `GridVoxelSet` immediately inside the enumeration. The `Voxels` list is backed by pooled storage and should be treated as transient query data.

## Workflow 9: Reset Or Tear Down A World Between Scenarios

```csharp
using GridForge.Grids;

using GridWorld scenarioWorld = new GridWorld();

// Add grids and run the scenario.

scenarioWorld.Reset(deactivate: true);
```
