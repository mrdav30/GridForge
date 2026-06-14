# GridForge Wiki

GridForge is a deterministic, framework-agnostic voxel-grid library for spatial partitioning, simulation, and game-development workflows.

The core runtime unit is an explicit `GridWorld`. A `GridWorld` owns spatial hashing, active grids, tracing, blocker reactivity, and world-space lookup for one isolated world instance.

With GridForge as "a world primitive," multiple worlds can exist in the same process without leaking grid identity, voxel identity, blockers, occupants, or scan queries across boundaries.

## What GridForge Provides

- Deterministic voxel-grid spatial partitioning using `FixedMathSharp`
- Explicit world-scoped registration and lookup through `GridWorld`
- Snapped world-space bounds through `GridConfiguration` normalization at registration time
- Fast proximity and coverage queries via voxels and scan cells
- 2D-friendly XZ projection helpers for flat simulations without a separate runtime model
- Rectangular-prism and hex-prism topology behind the same `VoxelGrid` public model
- Dense and sparse storage behind the same `VoxelGrid` query model
- Obstacle, blocker, occupant, and partition workflows
- Allocation-conscious internals backed by pooling and `SwiftCollections`
- Cross-target support for `netstandard2.1` and `net8.0`
- Standard and lean package variants for `MemoryPack` and no-`MemoryPack` dependency profiles

## Who This Wiki Is For

- Library consumers who need to get a world online quickly and safely
- Contributors who need a map of the codebase and its invariants
- Maintainers who need a shared documentation source of truth for future pages

## Wiki Navigation

| Page | Focus |
| --- | --- |
| [Getting Started](Getting-Started.md) | Installation, first world setup, basic queries, and logging setup |
| [Core Concepts](Core-Concepts.md) | Worlds, grids, voxels, scan cells, occupants, blockers, partitions, and snapped bounds |
| [Common Workflows](Common-Workflows.md) | Create a world, register a grid, resolve a voxel, scan nearby space, apply a blocker |
| [Architecture Overview](Architecture-Overview.md) | How the major subsystems fit together and where responsibilities live |
| [VoxelGrid and Voxel Model](VoxelGrid-and-Voxel-Model.md) | Grid generation, voxel state, neighbor relationships, and query behavior |
| [Sparse Grid Storage](Sparse-Grid-Storage.md) | Dense versus sparse semantics, configured voxels, runtime mutation, and query behavior |
| [Scan Cells and Query Flow](Scan-Cells-and-Query-Flow.md) | Scan-cell overlay structure, neighborhood lookups, and query performance |
| [GridTracer and Coverage](GridTracer-and-Coverage.md) | Line and bounds tracing, covered voxel sets, and multi-grid implications |
| [Blockers and Obstacles](Blockers-and-Obstacles.md) | `Blocker`, `BoundsBlocker`, obstacle propagation, stacked blockers, and removals |
| [Occupants and Partitions](Occupants-and-Partitions.md) | `IVoxelOccupant`, `IVoxelPartition`, `PartitionProvider`, and lifecycle rules |
| [Diagnostics and Logging](Diagnostics-and-Logging.md) | `GridForgeLogger`, verbosity, tracing support, and safe debugging patterns |
| [Repository Layout and Build](Repository-Layout-and-Build.md) | Solution structure, package generation, CI expectations, and release notes |
| [Testing and Benchmarking](Testing-and-Benchmarking.md) | xUnit layout, explicit-world fixtures, benchmark usage, and validation strategy |
| [Determinism, Snapping, and Pooling](Determinism-Snapping-and-Pooling.md) | Core invariants that must remain true across framework targets |
| [Recipes](Recipes.md) | End-to-end usage patterns for gameplay, simulation, and server-side systems |
| [FAQ and Troubleshooting](FAQ-and-Troubleshooting.md) | Common mistakes, debugging checklists, and "why is this voxel result odd?" guidance |

## Quick Technical Snapshot

- Language: C# 11
- Main library: `src/GridForge`
- Test suite: `tests/GridForge.Tests`
- Benchmarks: `tests/GridForge.Benchmarks`
- Target frameworks: `netstandard2.1`, `net8.0`
- Test framework: xUnit v3
- Benchmark framework: BenchmarkDotNet
- Key packages: `FixedMathSharp`, `SwiftCollections`, `SwiftCollections.FixedMathSharp`, and optional `MemoryPack`
- Packaging note: `GeneratePackageOnBuild` is enabled, so library builds also emit NuGet packages

## The Core Mental Model

The library is easiest to reason about in this order:

1. Create a `GridWorld`.
2. Define world-space bounds with `GridConfiguration`.
3. Register the grid through `GridWorld.TryAddGrid(...)`.
4. Resolve world positions into a `VoxelGrid` and `Voxel`.
5. Use scan cells and tracing helpers for broader spatial queries.
6. Apply blockers, occupants, or partitions to mutate world state.
7. Reset or dispose the world explicitly when tests or tools need a clean boundary.

The most important architectural reality is that GridForge is world-scoped. If something feels "global," that is usually either:

- state owned by one explicit `GridWorld`
- a static manager API that still requires a `GridWorld` argument
- a convenience layer over world-owned grids, voxels, scan cells, blockers, or occupants

For flat 2D simulations, `Vector2d` APIs are a convenience projection over this
same 3D runtime. `Vector2d.X` maps to world X, `Vector2d.Y` maps to world Z, and
`layerY` selects world Y with a default of `0`.

For sparse worlds, the registered bounds still identify the grid address space,
but only configured voxels exist. `TryGetGrid(...)` can resolve an in-bounds
sparse grid while `TryGetGridAndVoxel(...)` fails when the addressed sparse
voxel was not configured.

For topology choices, `GridConfiguration` selects rectangular-prism or
hex-prism cells per grid. Rectangular grids use `VoxelIndex(x, y, z)`. Hex
grids use axial XZ coordinates: `VoxelIndex.x = q`, `VoxelIndex.z = r`, and
`VoxelIndex.y = layer`. `PointyTop` and `FlatTop` orientations affect only the
fixed-point world projection; query, blocker, occupant, scan, and trace
workflows stay world/grid/voxel based.

## Architecture At A Glance

| Type | Role |
| --- | --- |
| `GridWorld` | Owns one world's spatial hash, active grids, events, and top-level lookups |
| `VoxelGrid` | Owns a single grid's dimensions, topology metrics, physical voxel storage, scan cells, neighbor relationships, and versioned state |
| `Voxel` | Represents one snapped cell and tracks occupants, obstacles, partitions, and neighbor queries |
| `ScanCell` | Overlay node used to accelerate neighborhood and area queries |
| `GridTracer` | Converts lines and bounds into covered voxel sets across one or more grids in a world |
| `GridObstacleManager` | Applies and clears obstacle state on voxels |
| `GridOccupantManager` | Adds, removes, and queries occupant state |
| `GridScanManager` | Performs scan-driven spatial queries |
| `Blocker` / `BoundsBlocker` | Turns traced world-space regions into obstacle mutations |
| `WorldVoxelIndex` | World-scoped voxel identity with world token, grid slot, grid spawn token, and voxel coordinate |

## Repository Map

| Path | Purpose |
| --- | --- |
| `src/GridForge/Configuration` | Grid configuration and identity types such as `GridConfiguration` |
| `src/GridForge/Grids/Managers` | World-level orchestration plus mutation and query managers |
| `src/GridForge/Grids/Nodes` | `Voxel` and `ScanCell` node types |
| `src/GridForge/Grids/Storage` | Dense and sparse physical voxel storage strategies |
| `src/GridForge/Grids/Topology` | Per-grid topology metrics, snapping, dimensions, and world/index projection |
| `src/GridForge/Grids/Support` | Event info types and pools |
| `src/GridForge/Spatial` | Shared indices, directions, occupants, partitions, and awareness abstractions |
| `src/GridForge/Blockers` | World-space blocker abstractions built on grid coverage |
| `src/GridForge/Utility` | Tracing and logging helpers |
| `tests/GridForge.Tests/Grids` | Grid and manager behavior tests |
| `tests/GridForge.Tests/Blockers` | Blocker coverage and removal tests |
| `tests/GridForge.Tests/Spatial` | Spatial type and index tests |
| `tests/GridForge.Tests/Utility` | Logger and tracer tests |

## Non-Negotiable Invariants

- Create a `GridWorld` before using world-scoped grid APIs.
- Reset or dispose a `GridWorld` when tests or tools need isolated state.
- Keep core spatial logic in fixed-point math. Do not casually introduce `float` or `double`.
- Expect bounds and incoming positions to be normalized through each grid's topology metrics during registration and lookup.
- Treat pooled objects and collections as short-lived unless ownership is explicit.
- Preserve deterministic behavior across both target frameworks.
- Respect existing synchronization around shared mutable state.
- Route logging through `GridForgeLogger`, not direct console output.

## Build And Validation

Useful commands when working in the repository:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
```

Benchmarks are most valuable when changing tracing, registration, pooling, caching, sparse storage, or other allocation-sensitive paths.

## Recommended Reading Order

If you are new to the project, read in this order:

1. `README.md`
2. This `Home` page
3. `src/GridForge/GridForge.csproj`
4. `src/GridForge/Grids/Managers/GridWorld.cs`
5. `src/GridForge/Grids/VoxelGrid.cs`
6. `src/GridForge/Grids/Nodes/Voxel.cs`
7. `src/GridForge/Utility/GridTracer.cs`
8. The closest matching test file under `tests/GridForge.Tests`

## Documentation Approach

The wiki is not trying to restate every public API member in prose. Its job is to make the library understandable quickly: what the system does, where behavior lives, which invariants matter, and where someone should go next.

When the implementation changes, update `Home` first so the rest of the wiki has a stable anchor.
