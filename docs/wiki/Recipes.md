# Recipes

This page collects end-to-end patterns built from the rest of the wiki. The goal is not to show every API, but to show how the pieces fit together in realistic workflows.

## Recipe 1: Team-Aware Proximity Queries

```csharp
using System.Linq;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;

using GridWorld world = new GridWorld();

world.TryAddGrid(
    new GridConfiguration(
        new Vector3d(0, 0, 0),
        new Vector3d(32, 0, 32)),
    out _);

UnitOccupant ally = new UnitOccupant(new Vector3d(10, 0, 10), 1);
UnitOccupant enemyA = new UnitOccupant(new Vector3d(12, 0, 10), 2);
UnitOccupant enemyB = new UnitOccupant(new Vector3d(16, 0, 14), 2);

GridOccupantManager.TryRegister(world, ally);
GridOccupantManager.TryRegister(world, enemyA);
GridOccupantManager.TryRegister(world, enemyB);

IVoxelOccupant[] enemiesInRange = GridScanManager.ScanRadius(
    world,
    ally.Position,
    (Fixed64)6,
    groupCondition: groupId => groupId == 2)
    .ToArray();
```

## Recipe 2: Author A Structure Footprint That Survives Grid Streaming

```csharp
using FixedMathSharp;
using GridForge.Blockers;

BoundingArea footprint = new BoundingArea(
    new Vector3d(40, 0, 40),
    new Vector3d(48, 0, 48));

BoundsBlocker structureBlocker = new BoundsBlocker(
    world,
    footprint,
    cacheCoveredVoxels: true);

structureBlocker.ApplyBlockage();
structureBlocker.RemoveBlockage();
```

## Recipe 3: Paint Terrain Metadata Across A Region

```csharp
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;

public sealed class TerrainPartition : IVoxelPartition
{
    public WorldVoxelIndex WorldIndex { get; private set; }
    public string TerrainType { get; }

    public TerrainPartition(string terrainType)
    {
        TerrainType = terrainType;
    }

    public void SetParentIndex(WorldVoxelIndex parentVoxelIndex)
    {
        WorldIndex = parentVoxelIndex;
    }

    public void OnAddToVoxel(Voxel voxel) { }
    public void OnRemoveFromVoxel(Voxel voxel) { }
}

Vector3d roadMin = new Vector3d(4, 0, 4);
Vector3d roadMax = new Vector3d(12, 0, 6);

foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(world, roadMin, roadMax))
{
    foreach (Voxel voxel in covered.Voxels)
        voxel.TryAddPartition(new TerrainPartition("Road"));
}
```

## Recipe 4: Bootstrap And Tear Down A Chunked Server-Side World

```csharp
using System.Collections.Generic;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld();

List<ushort> loadedChunks = new List<ushort>();

GridConfiguration[] chunkConfigs =
{
    new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(31, 0, 31)),
    new GridConfiguration(new Vector3d(32, 0, 0), new Vector3d(63, 0, 31)),
    new GridConfiguration(new Vector3d(0, 0, 32), new Vector3d(31, 0, 63))
};

foreach (GridConfiguration config in chunkConfigs)
{
    if (world.TryAddGrid(config, out ushort gridIndex))
        loadedChunks.Add(gridIndex);
}

for (int i = loadedChunks.Count - 1; i >= 0; i--)
    world.TryRemoveGrid(loadedChunks[i]);
```

## Recipe 5: Build A Deterministic Test Scenario

```csharp
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld((Fixed64)0.5, spatialGridCellSize: 64);

GridConfiguration config = new GridConfiguration(
    new Vector3d(-2, 0, -2),
    new Vector3d(2, 0, 2),
    scanCellSize: 4);

world.TryAddGrid(config, out ushort gridIndex);
VoxelGrid grid = world.ActiveGrids[gridIndex];

// Run assertions here.
```
