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
```

## Benchmark Environment Behavior

`BenchmarkEnvironment`:

- suppresses logging by setting `GridForgeLogger.MinimumLevel` to `None`
- creates or resets an explicit `GridWorld` between iterations
- optionally clears GridForge and shared SwiftCollections pools
- configures the world with the benchmark's requested voxel and spatial-hash settings
