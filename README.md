# GridForge

![GridForge Icon](https://raw.githubusercontent.com/mrdav30/GridForge/main/icon.png)

[![Build](https://github.com/mrdav30/GridForge/actions/workflows/build-and-test.yml/badge.svg?branch=main)](https://github.com/mrdav30/GridForge/actions/workflows/build-and-test.yml)
[![Coverage](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fmrdav30.github.io%2FGridForge%2FSummary.json&query=%24.summary.linecoverage&suffix=%25&label=coverage&color=brightgreen)](https://mrdav30.github.io/GridForge/)
[![NuGet](https://img.shields.io/nuget/v/GridForge.svg)](https://www.nuget.org/packages/GridForge)
[![NuGet Downloads](https://img.shields.io/nuget/dt/GridForge.svg)](https://www.nuget.org/packages/GridForge)
[![License](https://img.shields.io/github/license/mrdav30/GridForge.svg)](https://github.com/mrdav30/GridForge/blob/main/LICENSE)
[![Frameworks](https://img.shields.io/badge/frameworks-netstandard2.1%20%7C%20net8.0-512BD4.svg)](https://github.com/mrdav30/GridForge)

**GridForge** is a deterministic voxel-world library for building fast spatial systems in games, simulations, tools, and server runtimes.

It gives you the low-level grid infrastructure that many systems end up reinventing: snapped voxel bounds, world-scoped grid registration, conjoined-grid neighbor awareness, scan-cell query overlays, blocker and obstacle state, occupant tracking, and fixed-point spatial math.

The interesting part is the shape of the model. GridForge does not force you into one giant grid, a hand-managed tile map, or a premature hierarchy. It gives you an explicit `GridWorld` that can own one grid, many conjoined grids, or a streamed set of active grids, while still leaving room for higher-level sector, region, planet, or shard systems above it.

## Why GridForge?

Grid-based systems tend to split into three common approaches:

| Approach | Best fit | Tradeoff |
| --- | --- | --- |
| Single large grid | Small or fixed worlds | Simple, but can become wasteful or awkward to stream |
| Multiple conjoined grids | Large worlds, loaded regions, tiled simulation spaces | More flexible, but needs reliable ownership and neighbor handling |
| Hierarchical grids | Very large or multi-scale worlds | Powerful, but easy to overbuild too early |

GridForge is designed around the most flexible foundation: **conjoined grids inside explicit worlds**.

That means you can start with a single grid, add neighboring grids as the world grows, remove inactive grids as regions unload, and build hierarchy above the library only when your game or simulation actually needs it.

## What It Provides

- Explicit `GridWorld` ownership for isolated runtime state
- Multiple active grids per world, with conjoined boundary neighbor lookup
- Deterministic fixed-point math through `FixedMathSharp`
- Fast world-space lookup through snapped bounds and a spatial hash
- Dense and sparse voxel storage behind the same `VoxelGrid` query model
- Scan-cell overlays for efficient radius and occupant queries
- Region coverage through `GridTracer`
- Blocker and obstacle workflows for world-space blocked areas
- Occupant and partition extension points for gameplay or simulation metadata
- Allocation-conscious internals built on `SwiftCollections` pools and containers
- Standard and lean package variants for different dependency needs

## Install

```bash
dotnet add package GridForge
```

GridForge targets `netstandard2.1` and `net8.0`.

### Package Variants

GridForge is published in two build variants:

| Package | Use when |
| --- | --- |
| `GridForge` | You want the default package with `MemoryPack`, `FixedMathSharp`, and `SwiftCollections`. |
| `GridForge.Lean` | You want the same core voxel-grid API without the direct `MemoryPack` dependency, using `FixedMathSharp.Lean` and `SwiftCollections.Lean`. |

Install the lean package with:

```bash
dotnet add package GridForge.Lean
```

Source builds also expose matching configurations:

- `Release` builds the standard `GridForge` package.
- `ReleaseLean` builds the `GridForge.Lean` package.

Unity-specific integration lives in the separate [GridForge-Unity](https://github.com/mrdav30/GridForge-Unity) repository.

## Quick Start

```csharp
using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

using GridWorld world = new GridWorld();

GridConfiguration configuration = new GridConfiguration(
    new Vector3d(-10, 0, -10),
    new Vector3d(10, 0, 10),
    scanCellSize: 8);

if (!world.TryAddGrid(configuration, out ushort gridIndex))
    throw new InvalidOperationException("Failed to add grid.");

VoxelGrid grid = world.ActiveGrids[gridIndex];
Vector3d position = new Vector3d(2, 0, -3);

if (world.TryGetGridAndVoxel(position, out VoxelGrid resolvedGrid, out Voxel voxel))
{
    Console.WriteLine($"Grid: {resolvedGrid.GridIndex}");
    Console.WriteLine($"Voxel: {voxel.Index}");
    Console.WriteLine($"World position: {voxel.WorldPosition}");
}
```

For flat XZ simulations, lookup APIs also accept `Vector2d` positions. In
GridForge, `Vector2d(x, z)` maps to world `Vector3d(x, layerY, z)`, with
`layerY` defaulting to `0`. Tracing, coverage, radius scans, obstacle helpers,
and bounds blockers use the same XZ-plane projection and stay locked to the
selected layer. This is a convenience layer over the 3D voxel runtime, not a
separate 2D grid type.

The key mental model is:

1. Create a `GridWorld`.
2. Register one or more `VoxelGrid` instances from `GridConfiguration`.
3. Resolve world-space positions into grids and voxels.
4. Layer scans, blockers, occupants, partitions, or higher-level systems on top.
5. Reset or dispose the world when that simulation boundary is done.

Dense grids are the default: every topology-local voxel inside the registered
bounds exists. Sparse grids use the same bounds as an address space, but only
explicitly configured voxels exist. Missing sparse voxels are intentional
absence for lookup, coverage, blockers, occupants, partitions, and neighbors.

## Conjoined Grids And Dynamic Worlds

The library is built for worlds that grow, stream, and split responsibility cleanly:

- A small game can use one `GridWorld` with one `VoxelGrid`.
- A streamed world can load and unload grids around the player or active simulation area.
- A server can run multiple isolated `GridWorld` instances in one process.
- A larger architecture can put sectors, planets, regions, or hierarchy above GridForge without reworking the voxel layer.

GridForge tracks world-local grid slots, grid spawn tokens, and `WorldVoxelIndex` values so systems can reason about identity even as grids are removed, reused, or replaced.

## Core Concepts

| Concept | Role |
| --- | --- |
| `GridWorld` | Owns spatial hashing, active grids, lifecycle, events, and top-level lookup for one isolated world. |
| `VoxelGrid` | Owns one grid's snapped bounds, topology metrics, physical voxel storage, scan cells, neighbor relationships, obstacle summary state, and versioning. |
| `Voxel` | Represents one snapped cell with obstacle, occupant, partition, boundary, and cached neighbor state. |
| `ScanCell` | Groups voxels into query buckets so radius scans can skip empty regions. |
| `GridTracer` | Converts lines and bounds into covered voxels or scan cells across the active grids in a world. |
| `BoundsBlocker` | Applies and removes obstacle state over traced world-space bounds. |
| `IVoxelOccupant` | Represents dynamic entities that can be registered, deregistered, and scanned. |
| `IVoxelPartition` | Attaches typed voxel-local metadata or behavior directly to a voxel. |

## Documentation

Start with the wiki:

- [Wiki Home](https://github.com/mrdav30/GridForge/wiki/Home)
- [Getting Started](https://github.com/mrdav30/GridForge/wiki/Getting-Started)
- [Core Concepts](https://github.com/mrdav30/GridForge/wiki/Core-Concepts)
- [Sparse Grid Storage](https://github.com/mrdav30/GridForge/wiki/Sparse-Grid-Storage)
- [Common Workflows](https://github.com/mrdav30/GridForge/wiki/Common-Workflows)
- [Architecture Overview](https://github.com/mrdav30/GridForge/wiki/Architecture-Overview)
- [Recipes](https://github.com/mrdav30/GridForge/wiki/Recipes)
- [FAQ and Troubleshooting](https://github.com/mrdav30/GridForge/wiki/FAQ-and-Troubleshooting)

The source for those pages lives in [`docs/wiki`](docs/wiki).

## Local Validation

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

CI validates both `Release` and `ReleaseLean` on Ubuntu and Windows.

For benchmark discovery:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
```

Benchmarks are especially useful when changing pooling, tracing, grid registration, scan flow, or other allocation-sensitive paths. The `sparse-voxel-grid` alias covers sparse construction, lookup, coverage, blocker, scan, and dense comparison scenarios.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines and workflow details.

When working on core behavior, keep the library deterministic, world-scoped, allocation-conscious, and free of engine-specific assumptions.

## Community And Support

For questions, discussions, or general support, join the official Discord community:

**[Join the Discord Server](https://discord.gg/mhwK2QFNBA)**

For bug reports or feature requests, please open an issue in this repository.

## License

GridForge is licensed under the MIT License. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [COPYRIGHT](COPYRIGHT) for details.
