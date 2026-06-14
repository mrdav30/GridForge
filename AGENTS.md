# GridForge Agent Guide

## Purpose

GridForge is a framework-agnostic deterministic voxel-grid library for games,
simulations, tools, and server runtimes. It sits in the LSF stack after:

1. `FixedMathSharp` - deterministic fixed-point math.
2. `SwiftCollections` - low-allocation collections, pools, and query-friendly
   containers.
3. `GridForge` - explicit voxel worlds, conjoined grids, spatial queries,
   blockers, occupants, and voxel-local partitions.

The core design goal is to make grid-backed spatial systems scalable without
forcing every consumer to reinvent ownership, snapping, neighbor resolution,
coverage queries, or occupant indexing.

Current priorities:

1. Preserve deterministic behavior across supported target frameworks.
2. Keep runtime APIs anchored to explicit `GridWorld` ownership.
3. Prefer optimized, low time-complexity code. No band-aid solutions.
4. Keep hot paths allocation-conscious and pooling-safe.
5. Keep the core library engine-agnostic. Unity integration belongs in the
   separate `GridForge-Unity` repository.
6. Prefer proven lower-stack primitives from `FixedMathSharp` and
   `SwiftCollections` before adding local math, collection, pool, or helper
   implementations.
7. Keep README, wiki pages, tests, benchmarks, package metadata, and workflow
   behavior aligned when architecture or public API changes.

## Start Here

Read these in order before making non-trivial changes:

1. [`README.md`](README.md) for package orientation and the public-facing mental
   model.
2. [`docs/wiki/Home.md`](docs/wiki/Home.md), then the matching wiki page for the
   area being changed.
3. [`src/GridForge/GridForge.csproj`](src/GridForge/GridForge.csproj) for target
   frameworks, package variants, dependencies, and build behavior.
4. [`src/GridForge/Grids/Managers/GridWorld.cs`](src/GridForge/Grids/Managers/GridWorld.cs),
   [`src/GridForge/Grids/VoxelGrid.cs`](src/GridForge/Grids/VoxelGrid.cs),
   [`src/GridForge/Grids/Nodes/Voxel.cs`](src/GridForge/Grids/Nodes/Voxel.cs),
   and [`src/GridForge/Utility/GridTracer.cs`](src/GridForge/Utility/GridTracer.cs).
5. The relevant source folder under [`src/GridForge`](src/GridForge).
6. The matching test folder under [`tests/GridForge.Tests`](tests/GridForge.Tests).
7. [`tests/GridForge.Benchmarks`](tests/GridForge.Benchmarks) when changing
   pooling, tracing, scanning, registration, or other performance-sensitive
   behavior.

When sibling repositories are available, also check their AGENTS/README files
when a change touches shared stack assumptions:

- `../FixedMathSharp`
- `../SwiftCollections`
- `../GridForge-Unity`

## Source Of Truth

When code, README, and wiki content disagree, prefer the code, project files,
and tests. Then update the docs that drifted.

Keep these aligned whenever behavior, public API, package shape, or developer
workflow changes:

- [`README.md`](README.md)
- [`docs/wiki`](docs/wiki), especially pages covering world ownership, tracing,
  scan cells, blockers, occupants, partitions, determinism, testing, and build
  workflow.
- [`AGENTS.md`](AGENTS.md)
- [`src/GridForge/GridForge.csproj`](src/GridForge/GridForge.csproj)
- [`tests/GridForge.Tests`](tests/GridForge.Tests)
- [`tests/GridForge.Benchmarks`](tests/GridForge.Benchmarks) when performance
  claims or hot paths change.
- Workflow files under [`.github/workflows`](.github/workflows), especially
  `build-and-test.yml`, `coverage.yml`, `sync-wiki.yml`, and
  `publish-nuget.yml`.

`docs/wiki` is the source content for the GitHub wiki. Keep source Markdown
repo-friendly and let the publish helper perform the narrow GitHub wiki link
rewrite.

## Repository Map

| Path | Purpose | Notes |
| --- | --- | --- |
| [`src/GridForge`](src/GridForge) | Main library project | Multi-targets `netstandard2.1` and `net8.0`. |
| [`src/GridForge/Configuration`](src/GridForge/Configuration) | Grid creation input and bounds identity | `GridConfiguration` is normalized by the owning world. |
| [`src/GridForge/Grids`](src/GridForge/Grids) | Core world, grid, voxel, scan-cell, manager, storage, topology, and pool logic | Highest-risk runtime area. |
| [`src/GridForge/Grids/Storage`](src/GridForge/Grids/Storage) | Dense and sparse physical voxel storage | Keep storage-specific layout behind `VoxelGrid`. |
| [`src/GridForge/Grids/Topology`](src/GridForge/Grids/Topology) | Topology metrics, snapping, dimensions, and world/index projection | Keep coordinate math deterministic and storage-neutral. |
| [`src/GridForge/Spatial`](src/GridForge/Spatial) | Shared coordinates, directions, occupants, partitions, and awareness abstractions | Keep engine-neutral and deterministic. |
| [`src/GridForge/Blockers`](src/GridForge/Blockers) | Bounds-based obstacle application over tracer coverage | Test stacked, edge, removal, and multi-grid cases. |
| [`src/GridForge/Support`](src/GridForge/Support) | Shared support types such as `BoundsKey` and `GridVoxelSet` | Watch pooled result lifetimes. |
| [`src/GridForge/Utility`](src/GridForge/Utility) | `GridTracer` and `GridForgeLogger` | Tracing changes can affect many systems. |
| [`tests/GridForge.Tests`](tests/GridForge.Tests) | xUnit v3 test project | Mirrors subsystem boundaries. |
| [`tests/GridForge.Benchmarks`](tests/GridForge.Benchmarks) | BenchmarkDotNet project | Covers allocation and throughput-sensitive scenarios. |
| [`docs/wiki`](docs/wiki) | Developer-facing usage and architecture documentation | Keep current with public API and workflow changes. |
| [`.assets/scripts`](.assets/scripts) | Versioned build and release packaging helpers | Used for release archive generation. |
| [`.github/workflows`](.github/workflows) | CI, coverage, wiki sync, release, and publish automation | Keep workflow names in sync across triggers. |

Ignore generated output when reviewing or editing unless the task is explicitly
about build artifacts:

- `.vs/`
- `bin/`
- `obj/`
- `TestResults/`
- `artifacts/`
- `BenchmarkDotNet.Artifacts/`

## Technology And Build Facts

- Language: C# 11
- Library target frameworks: `netstandard2.1`, `net8.0`
- Validation target framework: `net8.0`
- Test framework: xUnit v3
- Benchmark framework: BenchmarkDotNet
- Main dependencies: `FixedMathSharp`, `SwiftCollections`, and optional
  `MemoryPack`
- Library nullable context: enabled
- Test and benchmark nullable context: disabled
- Implicit usings: disabled
- XML documentation: generated for the library project
- Package generation: `GeneratePackageOnBuild` is enabled
- Configurations: `Debug`, `Release`, `ReleaseLean`

Package variants:

- `Release` builds the standard `GridForge` package with `MemoryPack`,
  `FixedMathSharp`, and `SwiftCollections`.
- `ReleaseLean` builds `GridForge.Lean` with `GRIDFORGE_DISABLE_MEMORYPACK`,
  `FixedMathSharp.Lean`, and `SwiftCollections.Lean`.

Versioning:

- CI and release workflows use GitVersion.
- Local builds without GitVersion fall back to version `0.0.0`.

## Runtime Architecture Snapshot

The runtime is built around explicit world ownership:

- `GridWorld` owns one world's spatial hash settings, active grid bucket,
  bounds tracker, spatial hash, maximum topology cell edge, versioning,
  lifecycle, and world-level events.
- `GridConfiguration` carries per-grid storage and topology intent through
  `GridStorageKind`, `GridTopologyKind`, and `GridTopologyMetrics`.
- `VoxelGrid` owns one grid's snapped bounds, dimensions, topology instance,
  dense or sparse physical voxel storage, scan-cell overlay, active scan-cell
  set, neighbor relationships, obstacle count, occupancy summary, and grid
  version.
- Dense storage materializes every in-bounds topology-local voxel. Sparse
  storage uses bounds as an address space and materializes only configured
  voxels; missing sparse voxels are intentional absence.
- `Voxel` owns local and world-scoped identity, obstacle state, occupant count,
  partitions, boundary awareness, world position, and neighbor query entrypoints.
- `ScanCell` stores occupant buckets grouped by `WorldVoxelIndex` and ticketed
  occupant entries for efficient removal and exact lookup.
- `GridTracer` converts lines and bounds into covered voxels or scan cells
  across the active grids in one `GridWorld`.
- `GridObstacleManager` mutates obstacle state and emits obstacle events.
- `GridOccupantManager` owns world-scoped occupant registration tracking,
  add/remove flows, ticket lookup, and occupant events.
- `GridScanManager` performs radius and typed scans through the scan-cell
  overlay.
- `Blocker` and `BoundsBlocker` translate world-space bounds into obstacle
  application and removal.
- `PartitionProvider`, `IVoxelPartition`, and `IVoxelOccupant` are the primary
  extension points for domain-specific behavior.

The library supports a single grid, many conjoined grids, and dynamic
load/unload patterns. Higher-level hierarchy should live above GridForge unless
there is a concrete reason to add it to the core API.

## Determinism Rules

Any change that affects snapping, lookup, iteration order, identity, tracing,
storage layout, topology projection, sparse mutation, neighbor resolution,
blocker coverage, occupant registration, scan ordering, or pooled lifetime is
high risk.

Always prefer:

- `Fixed64`, `Vector2d`, and `Vector3d` over `float`, `double`, or
  `System.Numerics` in deterministic runtime paths.
- Existing `FixedMathSharp` helpers for deterministic math and conversions
  before adding local equivalents.
- Stable ordering when traversing grids, voxels, scan cells, occupants,
  blockers, partitions, or pooled collections.
- Explicit `GridWorld` ownership over hidden process-global state.
- `WorldVoxelIndex` for cross-system voxel identity.
- Exact assertions in tests for snapped coordinates, identity, and event data.

Avoid introducing:

- wall-clock time, background scheduling, or nondeterministic random behavior in
  runtime logic.
- platform-specific hash-order dependencies.
- floating-point conversions in core spatial math unless the boundary is
  explicit and tested.
- engine-specific assumptions in the core library.

## Performance And Pooling Guidance

Always prefer optimized, low time-complexity code. No band-aid solutions.

Likely hotspots include:

- `GridWorld.TryAddGrid`, `TryRemoveGrid`, lookup, spatial-hash registration,
  and neighbor updates.
- `VoxelGrid` generation, reset, same-topology neighbor linking, and scan-cell
  generation.
- Dense and sparse voxel storage construction, lookup, enumeration, and runtime
  sparse add/remove.
- Topology normalization, world/index conversion, and coverage math.
- `GridTracer` line, bounds, and scan-cell coverage.
- `GridScanManager` radius scans and caller-owned result paths.
- `GridOccupantManager` registration tracking, active scan-cell bookkeeping,
  and remove flows.
- `Blocker` apply/remove paths and covered voxel caching.

Rules:

- Choose data structures by access pattern and time complexity.
- Prefer `SwiftCollections` concrete collections over `System.Collections`
  concrete collections in runtime code and tests that mirror runtime hot paths.
  Arrays are still appropriate for fixed-size or contiguous indexed storage.
- Use existing pools where the surrounding code already does.
- Check `FixedMathSharp` and `SwiftCollections` for existing primitives before
  writing custom math, collection, pooling, sorting, hashing, or capacity
  helpers.
- Use bit flags or bit masking for compact combinable state when it improves
  performance or clarity; avoid binary enums that cannot grow or compose.
- Apply `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to tiny hot-path
  helpers, properties, and forwarding methods when it matches surrounding code.
- Release rented collections in `finally` blocks when enumeration or user code
  can exit early.
- Do not retain pooled arrays, lists, sets, scan cells, voxels, or `GridVoxelSet`
  results beyond their documented lifetime.
- Benchmark changes that touch pooling, tracing, scan flow, registration, or
  other allocation-sensitive paths.
- Avoid LINQ in hot paths unless the surrounding code already accepts the cost
  and benchmarks support it.

## Code Style And API Guidance

Match the surrounding file style instead of imposing a new one.

- Add explicit `using` directives. `ImplicitUsings` is disabled.
- The library project has nullable enabled; tests and benchmarks currently have
  nullable disabled. Follow the local project context.
- `.editorconfig` disables implicit `new(...)`; prefer explicit construction.
- Public API surface should have XML documentation.
- Preserve existing `#region` organization in files that already use it.
- Route diagnostics through `GridForgeLogger`, not ad hoc console output.
- Keep interfaces small, deterministic, and engine-agnostic.
- Do not add compatibility adapters or wrapper APIs just to avoid fixing a weak
  design.

## Testing Expectations

Run tests whenever behavior changes:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

CI validates both `Release` and `ReleaseLean` on Ubuntu and Windows:

```bash
dotnet test GridForge.slnx --configuration Release --no-build
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
```

Run benchmarks when changing pooling, tracing, scan cells, occupant
registration, blocker application, grid registration, neighbor caching, or
other performance-sensitive paths:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*'
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- sparse-voxel-grid --filter '*SparseVoxelGridBenchmarks*'
```

Test guidance:

- Prefer explicit `GridWorld` creation in new tests.
- Use `GridWorldTestFactory` when it keeps setup clear and consistent.
- Many tests use `[Collection("GridForgeCollection")]` to isolate shared logger
  or compatibility state.
- Prefer deterministic coordinates and exact assertions over fuzzy tolerances.
- If you change tracing, blockers, occupancy, scan cells, grid registration,
  neighbor handling, snapping, storage, topology, sparse mutation, or identity
  behavior, update or add tests in the closest matching folder.

## Documentation Expectations

Update docs in the same change when user-facing behavior, public API, package
shape, or workflow behavior changes.

High-value pages:

- [`docs/wiki/Home.md`](docs/wiki/Home.md)
- [`docs/wiki/Getting-Started.md`](docs/wiki/Getting-Started.md)
- [`docs/wiki/Core-Concepts.md`](docs/wiki/Core-Concepts.md)
- [`docs/wiki/Architecture-Overview.md`](docs/wiki/Architecture-Overview.md)
- [`docs/wiki/Sparse-Grid-Storage.md`](docs/wiki/Sparse-Grid-Storage.md)
- [`docs/wiki/GridTracer-and-Coverage.md`](docs/wiki/GridTracer-and-Coverage.md)
- [`docs/wiki/Scan-Cells-and-Query-Flow.md`](docs/wiki/Scan-Cells-and-Query-Flow.md)
- [`docs/wiki/Blockers-and-Obstacles.md`](docs/wiki/Blockers-and-Obstacles.md)
- [`docs/wiki/Occupants-and-Partitions.md`](docs/wiki/Occupants-and-Partitions.md)
- [`docs/wiki/Repository-Layout-and-Build.md`](docs/wiki/Repository-Layout-and-Build.md)
- [`docs/wiki/Testing-and-Benchmarking.md`](docs/wiki/Testing-and-Benchmarking.md)

Keep README engaging and concise; push deep subsystem detail into the wiki.
Keep wiki source links repo-relative and let the sync helper adapt copied pages
for GitHub wiki publishing.

## Common Change Patterns

### Adding Core Grid Behavior

- Start in `src/GridForge/Grids`.
- Decide whether the behavior belongs at world, grid, voxel, scan-cell, query,
  storage, topology, or manager level.
- Check interactions with snapping, spatial hashing, pooling, versioning,
  events, neighbor resolution, storage kind, topology metrics, and identity tokens.
- Add or update tests under `tests/GridForge.Tests/Grids` or
  `tests/GridForge.Tests/Utility`.

### Adding Blocking Or Coverage Behavior

- Start in `src/GridForge/Blockers` or `src/GridForge/Utility/GridTracer.cs`.
- Reuse existing tracer and blocker patterns where possible.
- Test single-grid, multi-grid, edge, stacked, cached, uncached, apply, remove,
  sparse configured-only coverage, runtime sparse add reconciliation, and
  reapply cases.

### Adding Occupant Or Partition Behavior

- Start in `src/GridForge/Spatial`, `src/GridForge/Grids/Managers`, or
  `src/GridForge/Grids/Nodes/Voxel.cs`, depending on the responsibility.
- Verify registration, removal, scan-cell ticketing, active scan-cell tracking,
  callback failure behavior, and blocked voxel behavior.
- Add tests around attach/remove behavior and grid mutation side effects.

## Release And Packaging Notes

- `src/GridForge/GridForge.csproj` packages the library on build.
- Build outputs include `.nupkg` and `.snupkg` under the configured output path.
- `.assets/scripts/set-version-and-build.ps1` builds both `Release` and
  `ReleaseLean` and writes release archives under `artifacts/releases`.
- `.github/workflows/publish-nuget.yml` validates release tag version, builds
  both package variants, checks for exactly four package artifacts, uploads the
  package artifact, and publishes `.nupkg` files to NuGet.
- `.github/workflows/coverage.yml` and `.github/workflows/sync-wiki.yml` depend
  on the `build-and-test` workflow name. If the build workflow name changes,
  update those triggers and README badges together.

## Pitfalls To Avoid

- Reintroducing hidden process-wide grid state.
- Treating `GridIndex` alone as durable cross-system identity.
- Assuming pooled collections or query results can be retained indefinitely.
- Bypassing snapping or fixed-point conversions in core spatial logic.
- Reintroducing world-level cell geometry instead of per-grid topology metrics.
- Assuming `VoxelGrid` storage is always dense or that `VoxelGrid.Voxels` is a
  public storage-neutral surface.
- Treating missing sparse voxels as empty dense voxels.
- Breaking `ReleaseLean` by referencing `MemoryPack` without a guarded path.
- Adding Unity or engine-specific code to the core library.
- Changing synchronization around shared mutable state without tests and a clear
  reason.
- Editing generated output under `bin`, `obj`, `TestResults`, `artifacts`, or
  `BenchmarkDotNet.Artifacts`.
- Updating README examples without checking source and tests.

## Recommended Workflow

1. Read the README, the relevant wiki page, the project file, and nearby source
   and tests.
2. Decide whether the change affects determinism, global state, identity,
   pooling, storage, topology, package variants, or docs.
3. Make the smallest coherent change that fits existing architecture.
4. Check lower-stack libraries for existing primitives before adding local
   helpers.
5. Add or update focused tests for behavior changes.
6. Run build and test commands appropriate to the risk.
7. Run benchmarks for performance-sensitive changes.
8. Mention any warnings, global-state considerations, package-variant risk, or
   untested edge cases in the handoff.

Keep this document current when solution layout, build flow, package variants,
wiki publishing, storage/topology boundaries, or core architecture changes.
