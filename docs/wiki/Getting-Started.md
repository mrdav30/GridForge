# Getting Started

This page is the shortest reliable path from package install to your first successful grid lookup.

The default and recommended model is:

- create a `GridWorld`
- add one or more grids to that world
- resolve positions and apply mutations against that same world

## Before You Start

Keep these rules in mind before writing any code:

- `GridWorld` is the runtime owner of grid state.
- `GridConfiguration` is input data. Bounds are snapped when a world normalizes and registers the grid.
- Core grid math uses `FixedMathSharp` types such as `Fixed64` and `Vector3d`.
- Dispose or reset a world explicitly when you need a clean boundary.

## Install The Package

```bash
dotnet add package GridForge
```

GridForge brings in its core math and collection dependencies through NuGet, so you do not need to wire those up manually in a normal .NET project.

## Minimal Namespaces

Most first-use examples will start with these imports:

```csharp
using System;
using FixedMathSharp;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using SwiftCollections.Diagnostics;
```

## 1. Create A World

`GridWorld` owns voxel size, spatial hashing, active grids, and top-level world-space lookup for one isolated world.

```csharp
using GridWorld world = new GridWorld();
```

This uses the library defaults:

- Voxel size: `1`
- Spatial hash cell size: `50`

## 2. Create Your First Grid

`GridConfiguration` defines the world-space bounds of the grid plus the scan-cell size used for scan overlays.

```csharp
GridConfiguration config = new GridConfiguration(
    new Vector3d(-10, 0, -10),
    new Vector3d(10, 0, 10));

if (!world.TryAddGrid(config, out ushort gridIndex))
    throw new InvalidOperationException("Failed to add grid.");

VoxelGrid grid = world.ActiveGrids[gridIndex];

Console.WriteLine($"Grid {grid.GridIndex}");
Console.WriteLine($"Bounds: {grid.BoundsMin} -> {grid.BoundsMax}");
Console.WriteLine($"Dimensions: {grid.Width} x {grid.Height} x {grid.Length}");
```

Notes:

- Bounds are inclusive. With a voxel size of `1`, a grid from `-10` to `10` spans `21` voxels on that axis.
- The default `scanCellSize` is `8`, and it is measured in voxels, not world units.
- The world normalizes and snaps bounds during registration using that world's voxel size.

## 3. Resolve A Grid And Voxel From World Space

Once a grid is registered, the most common runtime lookup is resolving a world position into both the containing grid and the voxel at that position.

```csharp
Vector3d queryPosition = new Vector3d(2, 0, -3);

if (world.TryGetGridAndVoxel(queryPosition, out VoxelGrid resolvedGrid, out Voxel voxel))
{
    Console.WriteLine($"Grid: {resolvedGrid.GridIndex}");
    Console.WriteLine($"Voxel index: {voxel.Index}");
    Console.WriteLine($"Voxel world position: {voxel.WorldPosition}");
    Console.WriteLine($"Blocked: {voxel.IsBlocked}");
    Console.WriteLine($"Occupied: {voxel.IsOccupied}");
}
```

Use these helpers based on what you need:

- `world.TryGetGrid(...)` when you only need the containing grid
- `world.TryGetVoxel(...)` when you only need the voxel
- `world.TryGetGridAndVoxel(...)` when you need both in one lookup

## 4. Customize Voxel And Scan Granularity

When you need finer spatial resolution, configure the world up front and then create grids against that setup.

```csharp
using GridWorld fineWorld = new GridWorld((Fixed64)0.5, spatialGridCellSize: 64);

GridConfiguration fineGrid = new GridConfiguration(
    new Vector3d(-4, 0, -4),
    new Vector3d(4, 0, 4),
    scanCellSize: 4);
```

Practical rule of thumb:

- Voxel size controls world-space precision.
- Scan cell size controls how many voxels are grouped together for scan queries.

## 5. Configure Logging When You Need Visibility

GridForge logging is routed through `GridForgeLogger`. By default, the library emits `Warning` and `Error` level messages.

```csharp
GridForgeLogger.MinimumLevel = DiagnosticLevel.Info;

GridForgeLogger.LogHandler = (level, message, source) =>
{
    Console.WriteLine($"{level} [{source}] {message}");
};
```

For diagnostics in library or tool code, use interpolated helper calls such as `GridForgeLogger.Channel.Warn($"...")`; disabled diagnostic levels skip formatted expression evaluation.

## 6. Reset Or Dispose Explicitly

Because a `GridWorld` owns mutable runtime state, cleanup should be deliberate.

```csharp
world.Reset(); // Clears registered grids but keeps the world active
world.Reset(deactivate: true); // Clears state and marks the world inactive
```

In most application code and tests, `using GridWorld world = new GridWorld();` is simpler and safer.

## Common Early Mistakes

- Reusing one world across unrelated scenarios without a matching reset or disposal
- Forgetting that bounds are snapped to the world's voxel size
- Treating `scanCellSize` as a world-unit measurement instead of a voxel count
- Using `TryGetGrid(...)` when you actually need `TryGetGridAndVoxel(...)`
- Holding onto pooled query results longer than the immediate operation

## Where To Go Next

- [Home](Home) for the project-wide mental model
- [Core Concepts](Core-Concepts) for the vocabulary of worlds, grids, voxels, scan cells, blockers, occupants, and partitions
- [Common Workflows](Common-Workflows) for task-oriented examples beyond the first grid
- [Architecture Overview](Architecture-Overview) when you want to understand where behavior lives in the codebase
