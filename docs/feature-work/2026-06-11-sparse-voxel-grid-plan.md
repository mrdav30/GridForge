# Sparse Voxel Grid Battle Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add sparse voxel-grid support while keeping GridForge query workflows anchored to `GridWorld`, `VoxelGrid`, `Voxel`, `ScanCell`, `GridTracer`, blockers, occupants, and partitions.

**Architecture:** Keep `VoxelGrid` as the public grid type and move physical voxel storage behind an internal storage strategy. Dense grids keep the current "bounds define every voxel" behavior. Sparse grids use bounds as an address space and only configured voxels exist; reads and queries must not materialize missing voxels.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`, `SwiftCollections`, xUnit v3, BenchmarkDotNet, optional MemoryPack package variant guards.

---

## Status

- Started: 2026-06-11
- Release posture: Additive if the initial scope stays within the current dimension limits; potentially breaking if large sparse address spaces require public `Size` semantics to change.
- Backwards compatibility: Dense-grid behavior must remain equivalent.
- Current state: Phase 4 complete; ready for performance hardening and benchmark coverage.
- Shared foundation decisions: Completed 2026-06-11 in coordination with the hex-prism topology plan.

## Locked Decisions

- Sparse means configured voxels only. A missing in-bounds voxel is intentionally absent.
- Query APIs should not care whether a grid is dense or sparse, but sparse queries only return configured voxels.
- `TryGetGrid(...)` may resolve a sparse grid by bounds, while `TryGetGridAndVoxel(...)` fails when the addressed sparse voxel is not configured.
- `GridTracer.GetCoveredVoxels(...)` returns only configured voxels for sparse grids.
- `BoundsBlocker` and other blocker workflows apply obstacle state only to configured sparse voxels covered by the blocker bounds.
- Reads must not allocate or configure sparse voxels. Configuration happens through explicit construction or explicit mutation APIs.
- The first sparse release is static: sparse voxels are configured at grid creation time. Runtime sparse add/remove is deferred to the runtime sparse mutation roadmap slice.
- `GridConfiguration` directly carries storage intent through `GridStorageKind`; do not introduce a separate sparse configuration wrapper in the first release.
- Initial sparse support stays within the current `int` dimension envelope. Construction must validate `Width`, `Height`, `Length`, and `Size` overflow before storage initialization.
- `ConfiguredVoxelCount` is the public physical-cell count. Dense grids report `Size`; sparse grids report the de-duplicated configured voxel count.
- Replace public dense-array assumptions with storage-neutral deterministic enumeration APIs. Dense array access can remain internal to dense storage.
- Keep the core library engine-agnostic. Unity integration belongs outside this repository.

## Non-Goals

- Do not replace dense grids.
- Do not add octrees, hierarchy, sectors, save-game registries, or streaming orchestration to the core API.
- Do not add compatibility wrappers that preserve weak storage assumptions.
- Do not make missing sparse voxels behave like default dense voxels.
- Do not introduce wall-clock scheduling, background loading, nondeterministic iteration, or engine-specific hooks.

## Target Mental Model

Dense grid:

```text
bounds -> every snapped coordinate exists as a Voxel
```

Sparse grid:

```text
bounds -> address space
configured voxel set -> actual Voxel instances
```

The same world-level pipeline should still hold:

```text
world-space input
  -> snap or normalize against a GridWorld
  -> world-level candidate grid lookup
  -> per-grid storage lookup
  -> query or mutation
  -> version, event, and cache updates
```

## Architecture Direction

### Storage Boundary

Introduce an internal storage boundary owned by `VoxelGrid`.

Likely files:

- Create: `src/GridForge/Grids/Storage/IVoxelGridStorage.cs`
- Create: `src/GridForge/Grids/Storage/DenseVoxelGridStorage.cs`
- Create: `src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs`
- Create: `src/GridForge/Grids/Storage/SparseVoxelBlock.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Support/Pools.cs`

Responsibilities:

- allocate and release physical voxels
- allocate and release scan cells
- resolve voxels by `VoxelIndex` and world position
- resolve scan cells by key and index
- enumerate physical voxels deterministically
- append covered voxels and scan cells to caller-owned result lists
- invalidate boundary voxel neighbor caches
- expose physical voxel count separately from logical address-space dimensions

`VoxelGrid` remains the public owner of identity, dimensions, neighbors, obstacle summary state, active scan cells, versioning, and world ownership. The storage object owns the physical layout.

### Sparse Physical Layout

Prefer scan-cell-aligned sparse blocks over a single flat per-voxel dictionary.

Recommended first shape:

- sparse storage maps scan-cell keys to `SparseVoxelBlock`
- each block owns configured voxels that fall within that scan-cell region
- each block has one `ScanCell`
- each block stores voxels by deterministic local index inside the block
- block iteration follows local coordinate order, not dictionary insertion order

Why this beats a flat dictionary:

- coverage queries already operate through scan-cell ranges
- radius scans already use scan-cell candidates
- blockers can skip entire absent scan-cell blocks
- storage locality follows existing query locality
- missing blocks make absence cheap

If benchmarks show that many sparse blocks are nearly full, add an adaptive block representation later. Do not start with adaptive complexity before proving it is needed.

### Public Configuration Shape

Keep `GridConfiguration` focused on bounds and scan-cell sizing, then add sparse intent deliberately.

Candidate public additions:

- `GridStorageKind` enum with `Dense` and `Sparse`
- `GridConfiguration.StorageKind`, defaulting to `Dense`
- `GridWorld.TryAddGrid(GridConfiguration configuration, IEnumerable<VoxelIndex> configuredVoxels, out ushort allocatedIndex)` for sparse setup
- `GridWorld.TryAddGrid(GridConfiguration configuration, bool[,,] configuredVoxels, out ushort allocatedIndex)` as a mask convenience for rectangular local dimensions
- `VoxelGrid.ConfiguredVoxelCount`
- `VoxelGrid.StorageKind`
- `VoxelGrid.EnumerateVoxels()` or equivalent storage-neutral deterministic physical-voxel enumeration

Design rule:

- dense construction may ignore configured voxel input and generate all voxels
- sparse construction must validate, de-duplicate, and sort configured indices before storage initialization
- sparse mask construction must validate exact `[x, y, z]` dimensions against the normalized grid dimensions, then flow through the same configured-voxel initialization path
- configured indices outside the normalized grid bounds must fail the grid add operation, not be silently ignored

Large address-space support is deferred. The current grid exposes `Width`, `Height`, `Length`, and `Size` as `int`, and the initial sparse phase stays within that dimension envelope. Construction must validate overflow before storage initialization. A later phase can introduce `long AddressableVoxelCount` or a breaking replacement for `Size` if sparse grids need address spaces larger than `int.MaxValue` cells.

## Behavior Matrix

| Operation | Dense Grid | Sparse Grid |
| --- | --- | --- |
| `TryGetGrid(position)` | true when position is inside grid bounds | true when position is inside grid bounds |
| `TryGetVoxel(position)` | true for every in-bounds snapped coordinate | true only for configured voxels |
| `TryGetGridAndVoxel(position)` | true for every in-bounds snapped coordinate | false for in-bounds missing voxels |
| `GridTracer.GetCoveredVoxels(...)` | returns every covered in-bounds voxel | returns covered configured voxels only |
| `GridTracer.GetCoveredScanCells(...)` | returns covered scan cells from the overlay | returns covered scan cells that exist for configured sparse blocks |
| `BoundsBlocker.ApplyBlockage()` | applies to all covered voxels | applies to covered configured voxels only |
| occupant registration | target voxel must exist and have vacancy | target configured voxel must exist and have vacancy |
| neighbor lookup | adjacent in-bounds voxels always exist | missing sparse neighbors are absent |
| partition attach | target voxel must exist | target configured voxel must exist |

## Phase 0: Lock Semantics And API Surface

Intent: prevent ambiguous sparse behavior from leaking into implementation.

Likely files:

- `docs/feature-work/2026-06-11-sparse-voxel-grid-plan.md`
- `docs/wiki/VoxelGrid-and-Voxel-Model.md`
- `docs/wiki/GridTracer-and-Coverage.md`
- `docs/wiki/Blockers-and-Obstacles.md`
- `src/GridForge/Configuration/GridConfiguration.cs`
- `src/GridForge/Grids/VoxelGrid.cs`

Checklist:

- [x] Lock first-release sparse grids as creation-time configured only; defer runtime `TryAddVoxel` and `TryRemoveVoxel`.
- [x] Use `GridConfiguration.StorageKind`; do not add a separate `SparseGridConfiguration` wrapper.
- [x] Keep initial sparse support within current `int` dimension limits.
- [x] Use `ConfiguredVoxelCount` as the public physical voxel count.
- [x] Replace public dense-array assumptions with storage-neutral enumeration APIs.
- [x] Record the decisions in this plan before implementation starts.

Decision record:

- Sparse grids are creation-time configured only in the first sparse release. Runtime `TryAddVoxel` and `TryRemoveVoxel` remain deferred to roadmap item 6.
- `GridConfiguration` directly carries `GridStorageKind StorageKind`, defaulting to `Dense`; sparse setup supplies configured indices through an explicit `GridWorld.TryAddGrid(...)` overload.
- Sparse grids stay within current `int` dimensions and `Size` semantics initially. Overflow or invalid address-space dimensions must fail construction.
- `ConfiguredVoxelCount` is the public physical-cell count. For dense grids it equals `Size`; for sparse grids it equals the sorted, de-duplicated configured voxel count.
- `VoxelGrid.Voxels` should not remain the storage-neutral public surface. Add a deterministic physical-voxel enumeration API and keep dense-array access internal to dense storage.
- Missing sparse voxels are intentional absence for lookup, tracing, blockers, occupants, partitions, and neighbors. Reads must not materialize them.

Exit criteria:

- [x] There is one agreed meaning for absent sparse voxels.
- [x] There is one agreed construction path for sparse grids.
- [x] There is no unresolved ambiguity around blockers, occupants, partitions, tracing, or missing neighbors.

## Phase 1: Extract Dense Storage Without Behavior Changes

Intent: create the storage boundary while proving dense behavior remains unchanged.

Likely files:

- Create: `src/GridForge/Grids/Storage/IVoxelGridStorage.cs`
- Create: `src/GridForge/Grids/Storage/DenseVoxelGridStorage.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Support/Pools.cs`
- Test: `tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Utility/GridTracer.Tests.cs`
- Test: `tests/GridForge.Tests/Blockers/BlockerTests.cs`

Checklist:

- [x] Add an internal storage interface that covers dense grid responsibilities currently embedded in `VoxelGrid`.
- [x] Move dense voxel generation into `DenseVoxelGridStorage`.
- [x] Move dense scan-cell generation into `DenseVoxelGridStorage`.
- [x] Route `VoxelGrid.TryGetVoxel(...)`, `TryGetScanCell(...)`, `GetActiveScanCells()`, and boundary invalidation through storage.
- [x] Add storage-neutral deterministic voxel enumeration and keep dense array access internal to dense storage.
- [x] Preserve exact dense neighbor behavior, scan-cell keys, voxel world positions, obstacle behavior, occupant behavior, and pooling cleanup.
- [x] Add focused regression tests around dense construction, reset, reuse, boundary invalidation, tracing, blocker apply/remove, and occupant registration.

Exit criteria:

- [x] Dense grids produce the same public behavior as before.
- [x] No sparse code exists yet beyond neutral storage boundaries.
- [x] Existing tests pass under `Debug`.

Validation:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

## Phase 2: Add Sparse Construction And Lookup

Intent: support sparse grids with explicit configured voxels and no read-time materialization.

Likely files:

- Create: `src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs`
- Create: `src/GridForge/Grids/Storage/SparseVoxelBlock.cs`
- Modify: `src/GridForge/Configuration/GridConfiguration.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Support/Pools.cs`
- Test: `tests/GridForge.Tests/Grids/SparseVoxelGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/GridWorld.Tests.cs`

Checklist:

- [x] Add sparse grid configuration or sparse `TryAddGrid` overloads.
- [x] Validate sparse configured indices against normalized bounds and dimensions.
- [x] De-duplicate sparse configured indices deterministically.
- [x] Initialize configured sparse voxels with correct `WorldVoxelIndex`, world position, scan-cell key, boundary state, and grid version.
- [x] Allocate sparse scan-cell blocks only for scan cells that contain configured voxels.
- [x] Make `TryGetVoxel(...)` return `false` for missing in-bounds sparse coordinates.
- [x] Make `TryGetGridAndVoxel(...)` return `false` for missing in-bounds sparse coordinates while `TryGetGrid(...)` still resolves the grid.
- [x] Ensure reset releases every configured voxel, sparse block, scan cell, neighbor cache, obstacle tracker, and pooled collection.
- [x] Add tests for empty sparse grids, invalid configured indices, duplicate configured indices, exact-bound configured voxels, missing in-bounds voxels, and pooled grid reuse.

Exit criteria:

- [x] Sparse grids can be created and queried.
- [x] Missing sparse voxels do not allocate, mutate, or appear in query results.
- [x] Dense-grid behavior remains unchanged.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "SparseVoxelGrid|VoxelGrid|GridWorld"
```

Completed validation:

```bash
dotnet test GridForge.slnx --configuration Debug --filter "SparseVoxelGrid"
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "SparseVoxelGrid|VoxelGrid|GridWorld"
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build --filter "SparseVoxelGrid|VoxelGrid|GridWorld"
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- --filter '*VoxelGridMemoryBenchmarks*'
```

Fast follow-up completed on 2026-06-12:

- Removed the unused internal dense initializer overload from `VoxelGrid`.
- Removed storage construction from `IVoxelGridStorage`; dense and sparse now initialize through concrete storage paths before assigning the active storage reference.
- Replaced the sparse block-capacity `System.Collections.Generic.Dictionary` with a pooled `SwiftDictionary<int, int>`.

Fast follow-up validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "SparseVoxelGrid|VoxelGrid|GridWorld"
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build --filter "SparseVoxelGrid|VoxelGrid|GridWorld"
```

## Phase 3: Make Coverage, Blockers, Occupants, And Partitions Storage-Neutral

Intent: ensure every user-facing query and mutation workflow treats dense and sparse grids through one public model.

Likely files:

- Modify: `src/GridForge/Utility/GridTracer.cs`
- Modify: `src/GridForge/Blockers/Blocker.cs`
- Modify: `src/GridForge/Grids/Managers/GridObstacleManager.cs`
- Modify: `src/GridForge/Grids/Managers/GridOccupantManager.cs`
- Modify: `src/GridForge/Grids/Managers/GridScanManager.cs`
- Modify: `src/GridForge/Grids/Nodes/Voxel.cs`
- Test: `tests/GridForge.Tests/Utility/GridTracer.Tests.cs`
- Test: `tests/GridForge.Tests/Blockers/BlockerTests.cs`
- Test: `tests/GridForge.Tests/Grids/ManagerCoverage.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/Voxel.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/ScanCell.Tests.cs`

Checklist:

- [x] Route `GridTracer` covered-voxel enumeration through storage-aware append methods instead of assuming dense coordinate coverage.
- [x] Route covered scan-cell enumeration through storage-aware append methods.
- [x] Confirm blocker apply/remove affects only covered configured sparse voxels.
- [x] Confirm cached blocker removal using `WorldVoxelIndex` works after sparse grid removal and re-add.
- [x] Confirm occupant registration fails cleanly for missing sparse voxels.
- [x] Confirm occupant scans only inspect sparse scan cells that exist and are covered.
- [x] Confirm partition APIs work on configured sparse voxels and fail naturally when lookup fails for missing sparse coordinates.
- [x] Confirm sparse neighbor lookup treats missing local or cross-grid voxels as absent.
- [x] Add conjoined dense-to-sparse and sparse-to-sparse neighbor tests.

Exit criteria:

- [x] Public query and mutation workflows do not branch in user code based on grid storage kind.
- [x] Blockers, occupants, partitions, tracing, and neighbor traversal have explicit sparse regression coverage.
- [x] Sparse blocker behavior is documented in tests and comments where needed.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

Phase 3 validation:

```bash
dotnet test GridForge.slnx --configuration Debug --filter "FullyQualifiedName~GridTracerTests|FullyQualifiedName~BlockerTests|FullyQualifiedName~ManagerCoverageTests|FullyQualifiedName~VoxelTests"
# Passed: 105/105
dotnet build GridForge.slnx --configuration Debug
# Succeeded: 0 warnings, 0 errors
dotnet test GridForge.slnx --configuration Debug --no-build
# Passed: 247/247
dotnet build GridForge.slnx --configuration ReleaseLean
# Succeeded: 0 warnings, 0 errors
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
# Passed: 249/249
```

## Phase 4: Runtime Sparse Voxel Mutation

Intent: add explicit runtime configuration APIs only after static sparse grids are stable.

Likely files:

- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs`
- Modify: `src/GridForge/Blockers/Blocker.cs`
- Modify: `src/GridForge/Grids/Support/GridEventInfo.cs`
- Test: `tests/GridForge.Tests/Grids/SparseVoxelGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Blockers/BlockerTests.cs`

Candidate APIs:

- `VoxelGrid.TryAddVoxel(VoxelIndex index, out Voxel? voxel)`
- `VoxelGrid.TryRemoveVoxel(VoxelIndex index)`
- `VoxelGrid.ContainsVoxel(VoxelIndex index)`
- `VoxelGrid.EnumerateVoxels()` or `GetConfiguredVoxels()`

Checklist:

- [x] Add explicit sparse-only voxel configuration APIs, or decide they are out of scope for the first sparse release.
- [x] Reject `TryRemoveVoxel` when the voxel has occupants, obstacles, partitions, or active event handlers that would make removal unsafe.
- [x] Define whether adding a sparse voxel under an active blocker should immediately apply blocker state. Recommended behavior: yes, by having sparse voxel add trigger grid-change reconciliation for overlapping active blockers.
- [x] Add deterministic events or version increments for sparse voxel add/remove.
- [x] Invalidate affected local and neighboring boundary caches when a sparse voxel is added or removed.
- [x] Release empty sparse blocks and scan cells when the last configured voxel is removed.

Exit criteria:

- [x] Runtime sparse mutation is either implemented and tested or explicitly deferred.
- [x] Active blocker reconciliation is not ambiguous.
- [x] Sparse add/remove paths remain pooling-safe.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "SparseVoxelGrid|Blocker|Voxel"
```

Phase 4 validation:

```bash
dotnet build GridForge.slnx --configuration Debug
# Succeeded: 0 warnings, 0 errors
dotnet test GridForge.slnx --configuration Debug --no-build --filter "SparseVoxelGrid|Blocker|Voxel"
# Passed: 158/158
dotnet test GridForge.slnx --configuration Debug --no-build
# Passed: 253/253
dotnet build GridForge.slnx --configuration ReleaseLean
# Succeeded: 0 warnings, 0 errors
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
# Passed: 255/255
```

## Phase 5: Performance Hardening And Benchmarks

Intent: prove sparse grids add value without regressing dense hot paths.

Likely files:

- Modify: `tests/GridForge.Benchmarks/**/*`
- Modify: `tests/GridForge.Tests/**/*`
- Modify: `src/GridForge/Grids/Storage/**/*`

Benchmark scenarios:

- dense grid construction and reset at current baseline sizes
- sparse grid construction with low, medium, and high configured density
- random voxel lookup in dense and sparse grids
- bounds coverage over empty sparse regions
- bounds coverage over sparse regions with clustered configured voxels
- blocker apply/remove on sparse regions
- occupant registration and radius scans on sparse regions
- conjoined dense/sparse neighbor lookup

Checklist:

- [ ] Add BenchmarkDotNet coverage for sparse construction, lookup, coverage, blocker, and scan flows.
- [ ] Compare dense baseline before and after storage extraction.
- [ ] Validate sparse missing-region coverage skips absent blocks without per-voxel allocation.
- [ ] Validate sparse scan paths remain deterministic and allocation-conscious.
- [ ] Decide whether adaptive sparse blocks are justified by benchmark data.

Exit criteria:

- [ ] Dense hot paths are not materially regressed.
- [ ] Sparse grids show clear memory or construction-time wins for low-density workloads.
- [ ] Sparse query performance is explained in docs with realistic tradeoffs.

Validation:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*Sparse*'
```

## Phase 6: Documentation And Release Alignment

Intent: make sparse behavior obvious to users who choose it.

Likely files:

- Modify: `README.md`
- Modify: `docs/wiki/Home.md`
- Modify: `docs/wiki/Core-Concepts.md`
- Modify: `docs/wiki/VoxelGrid-and-Voxel-Model.md`
- Modify: `docs/wiki/GridTracer-and-Coverage.md`
- Modify: `docs/wiki/Blockers-and-Obstacles.md`
- Modify: `docs/wiki/Scan-Cells-and-Query-Flow.md`
- Modify: `docs/wiki/Occupants-and-Partitions.md`
- Modify: `docs/wiki/Testing-and-Benchmarking.md`
- Modify: `src/GridForge/GridForge.csproj` if package metadata or XML docs need new wording

Checklist:

- [ ] Explain dense versus sparse grid semantics in the README without crowding the front door.
- [ ] Add a sparse grid wiki section or page that states: missing sparse voxels are intentional absence, not default voxels.
- [ ] Document blocker behavior over sparse grids: blockers apply to covered configured voxels only.
- [ ] Document occupant behavior over sparse grids: occupants can register only into configured voxels.
- [ ] Document neighbor behavior over sparse grids: missing neighbors are absent.
- [ ] Document result-lifetime and pooling expectations for sparse coverage results.
- [ ] Update XML docs for new public APIs.
- [ ] Run wiki link rewrite tests if links change.

Exit criteria:

- [ ] README, wiki, XML docs, tests, and package metadata tell the same sparse-grid story.
- [ ] Users can understand when to choose dense versus sparse without reading implementation code.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py
git diff --check
```

## Test Matrix

Minimum sparse coverage before release:

- dense grid behavior unchanged after storage extraction
- sparse empty grid returns no voxels but resolves grid bounds
- sparse configured voxel lookup by position and `VoxelIndex`
- missing sparse voxel lookup by position and `VoxelIndex`
- sparse duplicate configured indices de-duplicated deterministically
- sparse invalid configured indices rejected
- sparse edge and exact max-bound configured voxels
- sparse reset and pooled reuse
- sparse blocker apply/remove/reapply
- sparse cached blocker removal
- sparse occupant registration, deregistration, ticket lookup, and radius scan
- sparse partition attach/remove
- sparse local neighbor lookup with missing neighbors
- dense-to-sparse conjoined neighbor lookup
- sparse-to-sparse conjoined neighbor lookup
- Release and ReleaseLean build and test coverage

## Risk Register

| Risk | Mitigation |
| --- | --- |
| `VoxelGrid` becomes an abstraction dumping ground | Keep storage-specific responsibilities in focused storage files and keep world/grid ownership in `VoxelGrid`. |
| Dense hot paths regress | Extract dense storage first and benchmark before adding sparse behavior. |
| Sparse iteration becomes nondeterministic | Use coordinate-ordered traversal for query results and sorted sparse setup inputs. |
| `Size` semantics become misleading | Decide and document addressable versus configured voxel counts before code changes. |
| Blockers miss newly configured runtime sparse voxels | Defer runtime voxel mutation until blocker reconciliation is explicitly designed. |
| Sparse scan cells drift from configured voxel storage | Align sparse blocks to scan-cell keys and test coverage/scan behavior together. |
| Public docs imply missing sparse voxels behave like empty dense voxels | Use explicit wording in README/wiki examples and sparse tests. |
| ReleaseLean breaks through MemoryPack references | Keep new serialization attributes guarded consistently with existing package-variant patterns. |

## Recommended Implementation Order

1. Finish Phase 0 decisions in this document.
2. Implement Phase 1 and commit once dense tests pass.
3. Implement Phase 2 and commit once sparse construction tests pass.
4. Implement Phase 3 and commit once query/mutation tests pass.
5. Decide whether Phase 4 belongs in the first sparse release.
6. Run Phase 5 benchmarks before polishing docs.
7. Complete Phase 6 docs and release alignment.
