# Common Workflows

This page is a task-oriented companion to [Getting Started](Getting-Started) and [Core Concepts](Core-Concepts). The examples here are intentionally small and practical.

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

## Workflow 2: Resolve World Space Into A Grid And Voxel

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

## Workflow 3: Move Between World Space And Grid-Local Coordinates

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

## Workflow 4: Register Occupants And Scan For Nearby Results

```csharp
using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;

public sealed class UnitOccupant : IVoxelOccupant
{
    public Guid GlobalId { get; } = Guid.NewGuid();
    public Vector3d Position { get; set; }
    public byte OccupantGroupId { get; }

    public UnitOccupant(Vector3d position, byte occupantGroupId)
    {
        Position = position;
        OccupantGroupId = occupantGroupId;
    }
}

UnitOccupant ally = new UnitOccupant(new Vector3d(1, 0, 1), 1);
UnitOccupant enemy = new UnitOccupant(new Vector3d(3, 0, 3), 2);

GridOccupantManager.TryRegister(world, ally);
GridOccupantManager.TryRegister(world, enemy);

foreach (IVoxelOccupant occupant in GridScanManager.ScanRadius(world, new Vector3d(0, 0, 0), (Fixed64)5))
{
    Console.WriteLine(occupant.Position);
}
```

## Workflow 5: Apply And Remove A Bounds-Based Blocker

```csharp
using FixedMathSharp;
using GridForge.Blockers;

BoundingArea blockedArea = new BoundingArea(
    new Vector3d(1, 0, 1),
    new Vector3d(3, 0, 3));

BoundsBlocker blocker = new BoundsBlocker(world, blockedArea);

blocker.ApplyBlockage();
blocker.RemoveBlockage();
```

## Workflow 6: Trace Covered Voxels For Custom Processing

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

Consume each `GridVoxelSet` immediately inside the enumeration. The `Voxels` list is backed by pooled storage and should be treated as transient query data.

## Workflow 7: Reset Or Tear Down A World Between Scenarios

```csharp
using GridForge.Grids;

using GridWorld scenarioWorld = new GridWorld();

// Add grids and run the scenario.

scenarioWorld.Reset(deactivate: true);
```
