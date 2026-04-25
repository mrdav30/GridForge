# GridForge Agent Guide

This document is for both human contributors and AI coding agents working in this repository. It is intentionally practical: it describes what the project is, how the codebase is organized, which invariants matter, and how to make changes without breaking deterministic grid behavior.

## Project Summary

GridForge is a deterministic voxel-grid library for spatial partitioning, simulation, and game-development use cases. The core library is framework-agnostic and centers on:

- explicit `GridWorld` ownership and world-scoped grid registration
- voxelized world-space bounds
- scan-cell overlays for fast spatial queries
- obstacle and occupant tracking
- deterministic fixed-point math through `FixedMathSharp`
- allocation-conscious collections and pools through `SwiftCollections`

The repository currently contains one library project and two validation projects:

- `src/GridForge` - main library
- `tests/GridForge.Tests` - xUnit test suite
- `tests/GridForge.Benchmarks` - BenchmarkDotNet performance and allocation benchmarks

## Technology and Build Facts

- **Language:** C# 11
- **Library target frameworks:** `netstandard2.1`, `net8.0`
- **Validation target frameworks:** `net8.0`
- **Test framework:** xUnit v3
- **Benchmark framework:** BenchmarkDotNet
- **Main dependencies:** `FixedMathSharp` `2.1.0`, `SwiftCollections` `2.0.0`
- **Build behavior:** `dotnet build` on the library also produces NuGet packages because `GeneratePackageOnBuild` is enabled in `src/GridForge/GridForge.csproj`
- **CI:** runs on Ubuntu and Windows via `.github/workflows/dotnet.yml`
- **Versioning:** CI uses GitVersion; local builds without GitVersion fall back to version `0.0.0`

## Repository Layout

- `README.md` - external-facing project overview and usage examples
- `GridForge.slnx` - solution entry point
- `src/GridForge/GridForge.csproj` - library project configuration and package metadata
- `src/GridForge/Configuration` - grid configuration types and bounds identity
- `src/GridForge/Grids` - core grid, voxel, scan-cell, manager, and pooling logic
- `src/GridForge/Spatial` - occupant, partition, and index abstractions
- `src/GridForge/Blockers` - area/blocking abstractions built on top of grid coverage
- `src/GridForge/Utility` - tracing and logging helpers
- `tests/GridForge.Tests` - unit tests organized by subsystem
- `tests/GridForge.Benchmarks` - benchmark scenarios for pooling, tracing, caching, and registration performance
- `.github/workflows` - CI and release automation
- `.assets/scripts` - PowerShell helpers for versioned build and release packaging

Ignore these when reading or editing unless the task is specifically about build outputs or IDE state:

- `.vs`
- `bin`
- `obj`
- `TestResults`

## Architecture At A Glance

### Core Types

- **`GridWorld`:** primary runtime owner for one world's mutable state: setup values, active grids, spatial hash, world-space lookups, and world-level events.
- **`GlobalGridManager`:** temporary compatibility facade over a default `GridWorld`. Prefer explicit `GridWorld` usage in new code unless the task is specifically about the facade.
- **`VoxelGrid`:** represents a single configured grid with voxels, scan cells, neighbor relationships, occupancy state, and versioning.
- **`Voxel`:** holds world position, grid indices, obstacle state, occupancy state, partition attachments, and cached neighbor data.
- **`ScanCell`:** secondary overlay used to accelerate neighborhood and area queries over voxels.
- **`GridTracer`:** converts lines and bounding regions into covered voxel sets inside an explicit `GridWorld`.
- **`BoundsBlocker` and `Blocker`:** apply or remove obstacle state across covered voxels returned by tracing logic.
- **`PartitionProvider`, `IVoxelPartition`, and `IVoxelOccupant`:** extension points for custom metadata and occupant systems.

### Important Design Characteristics

- Deterministic math matters. The library uses `Fixed64`, `Vector2d`, and `Vector3d` from `FixedMathSharp`.
- Grid state is world-scoped. Most runtime APIs should be anchored to an explicit `GridWorld`.
- Bounds are snapped to voxel size. Many APIs normalize or snap incoming coordinates.
- Object pooling is used heavily for grids, voxels, scan cells, arrays, and temporary collections.
- Performance-sensitive code favors explicit control flow over abstraction-heavy patterns.

## Critical Invariants

Treat the following as core rules of the system:

- Create a `GridWorld` before using world-scoped grid APIs.
- Dispose a `GridWorld` or call `Reset()` when a test or tool run needs a clean world state.
- Treat `GlobalGridManager` as a temporary default-world facade, not as the target architecture.
- Use fixed-point types for grid math. Avoid introducing `float` or `double` into core simulation logic unless there is a clear boundary conversion reason.
- Assume bounds and positions may be snapped to the configured voxel size. When debugging odd query results, check snapped values first.
- Respect pooling. If a type or collection comes from a pool, verify whether it is safe to retain beyond the immediate operation.
- Preserve deterministic behavior across target frameworks. If a change behaves differently between `netstandard2.1` and `net8.0`, treat that as a bug.
- Maintain thread-safety assumptions where locking already exists. Do not remove synchronization from global or shared mutable state without a strong reason.

## Code Style and Conventions

Match the surrounding code instead of imposing a new style on untouched files.

- `ImplicitUsings` is disabled. Add explicit `using` directives.
- `Nullable` is disabled. Be careful introducing nullable annotations or nullability-dependent patterns.
- `.editorconfig` explicitly disables the implicit `new(...)` style. Prefer explicit type construction.
- Public API surface is expected to have XML documentation. The build currently emits warnings when public members are undocumented.
- Source files use a mix of block-scoped and file-scoped namespaces. Follow the local file style when editing an existing file.
- Existing code uses `#region` blocks in many source files. Preserve them where already present.
- Logging goes through `GridForgeLogger`, not ad hoc console output.

## Working In Specific Areas

### `src/GridForge/Configuration`

- Holds configuration and identity types such as `GridConfiguration` and `BoundsKey`.
- `GridConfiguration` normalizes bounds and derives center/scan-cell settings.
- `BoundsKey` has framework-conditional implementations. Keep cross-target compatibility in mind when editing it.

### `src/GridForge/Grids`

- This is the center of the library.
- `GridWorld` owns grid registration, spatial hashing, setup/reset, and world-space lookup helpers.
- `GlobalGridManager` is the migration facade for a default world and should be treated carefully.
- `VoxelGrid` owns dimensions, scan-cell generation, voxel generation, and neighbor relationships.
- `Voxel` owns obstacle/occupant/partition state and neighbor caching.
- `Pools` defines reusable object and array pools. Changes here can have broad memory and lifetime effects.
- `GridScanManager`, `GridOccupantManager`, and `GridObstacleManager` are behavior-heavy modules that should usually be covered by tests when changed.

### `src/GridForge/Spatial`

- Contains extension interfaces and identity structs used across the grid system.
- Keep interfaces small and deterministic.
- Avoid leaking engine-specific concerns into this layer.

### `src/GridForge/Blockers`

- Blockers translate world-space bounds into obstacle changes on covered voxels.
- Any changes here should be checked against multi-grid coverage, stacked blockers, edge voxels, and removal behavior.

### `src/GridForge/Utility`

- `GridTracer` is a core query primitive. Small logic changes here can affect many systems.
- `GridForgeLogger` is the central logging hook and is also touched by tests via verbosity settings.

## Testing Expectations

Run tests whenever behavior changes in the library:

```bash
dotnet test GridForge.slnx --configuration Debug
```

Useful local commands:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

Run benchmarks when changing pooling, tracing, registration, or other performance-sensitive paths:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*'
```

Benchmark reports are emitted under `BenchmarkDotNet.Artifacts/results/`.

Repo-specific testing guidance:

- Use the existing xUnit suite in `tests/GridForge.Tests` as the reference for expected behavior.
- Use `tests/GridForge.Benchmarks` to validate allocation-sensitive changes and pooling or caching regressions.
- Prefer explicit `GridWorld` creation in new tests.
- Keep `GlobalGridManager`-based tests only when validating the compatibility facade itself.
- Many tests use `[Collection("GridForgeCollection")]` plus explicit setup/teardown to avoid leaked default-world state.
- Prefer deterministic coordinates and explicit assertions over fuzzy tolerances.
- If you change behavior in tracing, blockers, occupancy, scan cells, or grid registration, update or add tests in the matching folder.

As of April 25, 2026, the library project builds successfully and the test suite passes locally with:

- 212 tests passed

## Common Change Patterns

### Adding a New Core Grid Behavior

- Start in `src/GridForge/Grids`
- Identify whether the change belongs at world level, per-grid level, per-voxel level, or scan-cell/query level
- Add or update tests under `tests/GridForge.Tests/Grids` or `tests/GridForge.Tests/Utility`
- Check for interactions with pooling, versioning, and neighbor caches

### Adding a New Blocking Rule

- Start in `src/GridForge/Blockers`
- Reuse `GridTracer.GetCoveredVoxels(...)` or existing blocker patterns where possible
- Test single-grid, multi-grid, edge, stacked, and removal scenarios

### Adding a New Partition or Occupant Integration

- Start in `src/GridForge/Spatial`
- Verify the change works with `Voxel` partition or occupancy lifecycles
- Add tests around attach/remove behavior and grid mutation side effects

## Pitfalls To Avoid

- Do not assume the default-world facade is the real architectural boundary. The target model is instance-based through `GridWorld`.
- Do not edit build outputs under `bin`, `obj`, or `TestResults`.
- Do not introduce engine-specific code or Unity-only assumptions into the core library.
- Do not bypass snapping or fixed-point conversions in core spatial logic.
- Do not retain pooled collections or arrays unless the code clearly establishes ownership and lifetime.
- Do not add public APIs without XML documentation unless you are intentionally accepting documentation warnings.
- Do not treat README examples as the full contract; verify behavior in source and tests.

## Release and Packaging Notes

- `src/GridForge/GridForge.csproj` packages the library on build.
- Debug or Release builds will emit `.nupkg` and `.snupkg` artifacts under `src/GridForge/bin/<Configuration>/`.
- `.assets/scripts/set-version-and-build.ps1` is the repo helper for version-aware builds and zipped release artifacts.
- CI uses GitVersion during build, but local builds fall back gracefully when GitVersion variables are absent.

## Recommended Workflow For Agents

When working on a task in this repository:

1. Read `README.md`, the relevant project file, and the nearest source and test files first.
2. Determine whether the change affects global state, deterministic math, or pooling lifetimes.
3. Make the smallest coherent change that fits existing architecture.
4. Update or add tests in the closest matching subsystem folder.
5. Run `dotnet build` and `dotnet test` before finishing whenever behavior changed.
6. Mention any warnings, global-state considerations, or untested edge cases in your handoff.

## Recommended Workflow For Human Contributors

- Use `README.md` for the high-level external overview.
- Use this file for repo-specific implementation guidance.
- Start debugging behavioral issues from the smallest relevant layer in this order: configuration and bounds snapping, world lookup, voxel lookup, tracer or scan logic, then blocker, occupant, or partition mutation.
- If a bug looks spatial, log or inspect snapped positions and grid bounds before changing algorithms.

## If You Need More Context

The most useful files to read first are usually:

- `README.md`
- `src/GridForge/GridForge.csproj`
- `src/GridForge/Grids/Managers/GridWorld.cs`
- `src/GridForge/Grids/Managers/GlobalGridManager.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- `src/GridForge/Grids/Voxel.cs`
- `src/GridForge/Utility/GridTracer.cs`
- `tests/GridForge.Tests/Grids/GridWorld.Tests.cs`
- `tests/GridForge.Tests/Grids/GlobalGridManager.Tests.cs`
- `tests/GridForge.Tests/Blockers/BlockerTests.cs`

Keep this document current when the solution layout, build flow, or core architecture changes.
