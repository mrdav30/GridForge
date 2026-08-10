# GridForge

![GridForge Icon](https://raw.githubusercontent.com/mrdav30/GridForge/main/icon.png)

[![Build](https://github.com/mrdav30/GridForge/actions/workflows/build-and-test.yml/badge.svg?branch=main)](https://github.com/mrdav30/GridForge/actions/workflows/build-and-test.yml)
[![Branch Coverage](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fmrdav30.github.io%2FGridForge%2Fcoverage%2FSummary.json&query=%24.summary.branchcoverage&suffix=%25&label=branch%20coverage&color=brightgreen)](https://mrdav30.github.io/GridForge/coverage/)
[![NuGet](https://img.shields.io/nuget/v/GridForge.svg)](https://www.nuget.org/packages/GridForge)
[![License](https://img.shields.io/github/license/mrdav30/GridForge.svg)](LICENSE)
[![API](https://img.shields.io/badge/docs-API-00a9d6)](https://mrdav30.github.io/GridForge/)
[![Discord](https://img.shields.io/badge/discord-join%20community-5865F2?logo=discord&logoColor=white)](https://discord.gg/mhwK2QFNBA)

**Deterministic voxel worlds for .NET games, simulations, tools, and server
runtimes.**

GridForge gives you explicit world ownership, snapped fixed-point grids,
streamable multi-grid spaces, spatial queries, blockers, occupants, and
diagnostic geometry—without tying the core runtime to a game engine.

## Why GridForge?

- **Worlds that grow with your project.** Start with one grid, join neighboring
  grids, or load and unload regions inside an explicit `GridWorld`.
- **Deterministic spatial math.** Rectangular-prism and hex-prism grids use
  [FixedMathSharp](https://github.com/mrdav30/FixedMathSharp) throughout the
  runtime.
- **Dense or sparse storage.** Model solid voxel volumes or large address spaces
  where only selected cells exist.
- **Queries and state built in.** Trace lines and bounds, scan nearby occupants,
  stack blockers, attach typed partitions, and observe dirty diagnostic regions.
- **Engine-agnostic by design.** Unity authoring and visualization live in the
  separate [GridForge-Unity](https://github.com/mrdav30/GridForge-Unity)
  packages.
- **Allocation-conscious internals.** Hot paths build on
  [SwiftCollections](https://github.com/mrdav30/SwiftCollections) containers and
  pools.

## Install

```bash
dotnet add package GridForge
```

Choose one package variant:

| Package          | Use it when                                                           |
| ---------------- | --------------------------------------------------------------------- |
| `GridForge`      | You want the default package with MemoryPack serialization support.   |
| `GridForge.Lean` | You want the same grid API without the MemoryPack runtime dependency. |

Both variants target `netstandard2.1` and `net8.0`.

## Quick start

```csharp
using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld();

GridConfiguration configuration = new GridConfiguration(
    new Vector3d(-10, 0, -10),
    new Vector3d(10, 0, 10));

if (!world.TryAddGrid(configuration, out _))
    throw new InvalidOperationException("Could not add the grid.");

Vector3d position = new Vector3d(2, 0, -3);
if (world.TryGetGridAndVoxel(
        position,
        out VoxelGrid? grid,
        out Voxel? voxel)
    && grid is not null
    && voxel is not null)
{
    Console.WriteLine($"Grid {grid.GridIndex}, voxel {voxel.Index}");
}
```

That same `GridWorld` can own multiple conjoined grids, mix rectangular and hex
topologies, and combine dense and sparse storage. Higher-level regions, sectors,
or streaming policy remain yours to shape above the voxel layer.

## Learn more

- [Getting started](https://github.com/mrdav30/GridForge/wiki/Getting-Started)
- [Core concepts](https://github.com/mrdav30/GridForge/wiki/Core-Concepts)
- [Common workflows](https://github.com/mrdav30/GridForge/wiki/Common-Workflows)
- [API reference](https://mrdav30.github.io/GridForge/api/GridForge.html)
- [Migration guide](docs/MIGRATION.md)
- [Contributing and local validation](CONTRIBUTING.md)

The [GridForge wiki](https://github.com/mrdav30/GridForge/wiki) covers topology,
sparse storage, tracing, scan cells, blockers, occupants, diagnostics,
determinism, testing, and benchmarks in depth.

Questions and discussion are welcome in the
[Discord community](https://discord.gg/mhwK2QFNBA). GridForge is available under
the [MIT License](LICENSE).
