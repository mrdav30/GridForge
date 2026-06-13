# Testing and Benchmarking

This page explains how GridForge validates behavior and performance today.

The short version is:

- use the xUnit suite to protect correctness
- use the benchmark project when changing performance-sensitive or allocation-sensitive code
- treat `GridWorld` boundaries as part of the validation story

## The Two Validation Tracks

| Track | Project | Best for |
| --- | --- | --- |
| Tests | `tests/GridForge.Tests` | Behavioral correctness, regressions, edge cases, and event semantics |
| Benchmarks | `tests/GridForge.Benchmarks` | Allocation trends, warm-vs-cold pool effects, and throughput-sensitive changes |

## Test Harness Shape

The test suite uses xUnit v3 and a shared collection fixture:

- `GridForgeFixture` sets `GridForgeLogger.MinimumLevel` to `Error`
- `GridForgeFixture` no longer creates the active runtime world by default
- `[Collection("GridForgeCollection")]` is still used where tests intentionally interact with shared compatibility state

New tests should prefer explicit `GridWorld` creation. The test project includes helpers such as `GridWorldTestFactory` to keep that setup small and consistent.

## What The Tests Cover

The test folders mirror the main subsystem boundaries:

- `tests/GridForge.Tests/Grids` covers world behavior, manager behavior, voxel/grid state, scan cells, and neighbor logic
- `tests/GridForge.Tests/Blockers` covers blocker application, stacking, removal, and reapply behavior
- `tests/GridForge.Tests/Utility` covers tracing and logging
- `tests/GridForge.Tests/Spatial` covers index and provider semantics

Phase 4 also added explicit isolation coverage for:

- multiple worlds loaded simultaneously with overlapping local coordinates
- stale identity rejection after world teardown
- blockers, occupants, tracing, and scans staying inside their owning world

Sparse storage coverage includes:

- dense storage behavior after storage extraction
- sparse construction from configured indices and masks
- sparse hex construction from topology-local axial `(q, layer, r)` indices
- missing in-bounds sparse voxel lookup
- storage-neutral tracing, blockers, occupants, partitions, scans, and neighbor lookup
- explicit runtime sparse voxel add/remove safety rules

Hex-prism topology coverage includes:

- flat-top and pointy-top projection and inverse projection
- exact cube-coordinate rounding at hex boundaries
- mixed rectangular/hex world lookup
- topology-specific rectangular and hex neighbor APIs
- mixed-topology voxel contact queries for pointy-top and flat-top hex grids
- hex line tracing and conservative bounds coverage
- hex blocker apply/remove behavior
- hex occupant registration and radius scans
- sparse hex tracing, scan-cell coverage, blockers, occupants, scans, and runtime mutation

## Standard Test Commands

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

## Coverage Notes

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --collect:"XPlat Code Coverage"
```

## Test Design Expectations

Good GridForge tests usually:

- create their own `GridWorld`
- use deterministic positions and explicit expectations
- assert exact snapped values instead of fuzzy tolerances
- dispose or reset the world when the scenario finishes

## Benchmark Runner Basics

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- all
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- sparse-voxel-grid --filter '*SparseVoxelGridBenchmarks*'
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- hex-prism-topology --filter '*HexPrismTopologyBenchmarks*'
```

The `sparse-voxel-grid` alias covers sparse construction density, configured and
missing lookup, empty and clustered coverage, blocker apply/remove, occupant
registration, radius scans, neighbor lookup, and dense comparison scenarios.

The `hex-prism-topology` alias covers rectangular baseline lookup, pointy/flat
hex lookup, projection, construction, line tracing, bounds coverage, blockers,
occupants, radius scans, and mixed rectangular/hex world lookup.

## Benchmark Environment Behavior

`BenchmarkEnvironment`:

- suppresses logging by setting `GridForgeLogger.MinimumLevel` to `None`
- creates or resets an explicit `GridWorld` between iterations
- optionally clears GridForge and shared SwiftCollections pools
- configures the world with the benchmark's requested voxel and spatial-hash settings
