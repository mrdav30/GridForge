# GridForge

![GridForge Icon](https://raw.githubusercontent.com/mrdav30/GridForge/main/icon.png)

[![.NET CI](https://github.com/mrdav30/GridForge/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/mrdav30/GridForge/actions/workflows/dotnet.yml)
[![Coverage](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fmrdav30.github.io%2FGridForge%2FSummary.json&query=%24.summary.linecoverage&suffix=%25&label=coverage&color=brightgreen)](https://mrdav30.github.io/GridForge/)
[![NuGet](https://img.shields.io/nuget/v/GridForge.svg)](https://www.nuget.org/packages/GridForge)
[![NuGet Downloads](https://img.shields.io/nuget/dt/GridForge.svg)](https://www.nuget.org/packages/GridForge)
[![License](https://img.shields.io/github/license/mrdav30/GridForge.svg)](https://github.com/mrdav30/GridForge/blob/main/LICENSE)
[![Frameworks](https://img.shields.io/badge/frameworks-netstandard2.1%20%7C%20net8.0-512BD4.svg)](https://github.com/mrdav30/GridForge)

**GridForge** is a deterministic, high-performance voxel-grid library for spatial partitioning, simulation, and game-development workflows.

The core unit is an explicit `GridWorld`. That lets you run multiple isolated worlds in one process without leaking grid registration, tracing, blockers, occupants, or scan queries across world boundaries.

## Install

```bash
dotnet add package GridForge
```

GridForge targets `netstandard2.1` and `net8.0` and builds on `FixedMathSharp`, `SwiftCollections`, and `MemoryPack`.

### Unity

Unity-specific integration lives in the separate [GridForge-Unity](https://github.com/mrdav30/GridForge-Unity) repository.

## Quick Start

```csharp
using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld();

GridConfiguration configuration = new(
    new Vector3d(-10, 0, -10),
    new Vector3d(10, 0, 10),
    scanCellSize: 8);

if (!world.TryAddGrid(configuration, out ushort gridIndex))
    throw new InvalidOperationException("Failed to add grid.");

VoxelGrid grid = world.ActiveGrids[gridIndex];
Vector3d position = new(2, 0, -3);

if (world.TryGetGridAndVoxel(position, out VoxelGrid resolvedGrid, out Voxel voxel))
{
    Console.WriteLine($"Grid: {resolvedGrid.GridIndex}");
    Console.WriteLine($"Voxel: {voxel.Index}");
    Console.WriteLine($"World position: {voxel.WorldPosition}");
}
```

Key ideas:

- `GridWorld` owns runtime state such as voxel size, spatial hash size, active grids, tracing, blocker reactivity, and world-space lookup.
- `VoxelGrid` is world-local. `GridIndex` is unique only within its owning world.
- `WorldVoxelIndex` is the cross-system identity for a voxel and includes world scope.

## Why Explicit Worlds

Having `GridWorld` own world state makes it practical to build:

- multi-world simulations with overlapping local coordinates
- streamed loading and unloading without cross-world state leakage
- save and load flows keyed by world identity
- higher-level orchestration such as galaxies, sectors, or planet registries above the library

## Start With The Wiki

- [Wiki Home](https://github.com/mrdav30/GridForge/wiki/Home)
- [Getting Started](https://github.com/mrdav30/GridForge/wiki/Getting-Started)
- [Core Concepts](https://github.com/mrdav30/GridForge/wiki/Core-Concepts)
- [Common Workflows](https://github.com/mrdav30/GridForge/wiki/Common-Workflows)
- [Architecture Overview](https://github.com/mrdav30/GridForge/wiki/Architecture-Overview)
- [Recipes](https://github.com/mrdav30/GridForge/wiki/Recipes)
- [FAQ and Troubleshooting](https://github.com/mrdav30/GridForge/wiki/FAQ-and-Troubleshooting)

## Local Validation

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

For benchmark discovery:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and workflow details.

## Community & Support

For questions, discussions, or general support, join the official Discord community:

👉 **[Join the Discord Server](https://discord.gg/mhwK2QFNBA)**

For bug reports or feature requests, please open an issue in this repository.

## License

GridForge is licensed under the MIT License. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [COPYRIGHT](COPYRIGHT) for details.
