# Feature Roadmap Overview

> **For agentic workers:** Read this overview before implementing the linked battle plans. Each detailed plan remains the source of truth for its own phases, tests, risks, and documentation work.

**Goal:** Coordinate the first major GridForge extensibility roadmap across 2D-friendly APIs, sparse voxel storage, and hex-prism topology without losing the current deterministic `GridWorld` / `VoxelGrid` / `Voxel` mental model.

**Architecture:** Implement the roadmap as layered capability work. Start with the lowest-risk public API ergonomics, then lock shared semantics around topology and storage, then land sparse and hex as separate internal boundaries that continue to compose through the existing world/grid/query workflows.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`, `SwiftCollections`, xUnit v3, BenchmarkDotNet, optional MemoryPack package variant guards.

---

## Roadmap Documents

- [Vector2d Query API Battle Plan](done/2026-06-11-vector2d-query-api-plan.md)
- [Sparse Voxel Grid Battle Plan](done/2026-06-11-sparse-voxel-grid-plan.md)
- [Hex Prism Grid Battle Plan](done/2026-06-11-hex-prism-grid-plan.md)
- [Hex Prism Follow-Up Plan](2026-06-13-hex-prism-follow-up-plan.md)

## North Star

All three features should preserve this user-facing model:

```text
GridWorld
  -> VoxelGrid
  -> Voxel
  -> queries, blockers, occupants, partitions, tracing, and scans
```

The user should not need to branch on grid storage, grid topology, or 2D versus 3D input for ordinary query workflows. Differences belong behind focused internal boundaries:

- 2D support lives behind a projection helper and layer-locked scan logic.
- Sparse support lives behind voxel storage.
- Hex support lives behind grid topology.

## Recommended Execution Order

### 1. Vector2d Query API - Done

Implement the 2D-friendly API first.

Status: completed on 2026-06-11. The plan is archived under
`docs/feature-work/done`.

Why:

- It is the smallest and lowest-risk feature.
- It gives users immediate value for flat XZ simulations.
- It locks the GridForge convention that `Vector2d(X, Y)` means world `(X, Z)` with an explicit `layerY`.
- It clarifies scan semantics before sparse and hex make query behavior more complex.

Target outcome:

- `Vector2d` lookup, tracing, coverage, scan, obstacle, and blocker helpers work as convenience APIs over the existing 3D runtime.
- `ScanRadius(Vector2d, ...)` is a true layer-locked XZ circle scan, not a 3D sphere scan.
- Documentation explains that flat consumers can store `Vector2d` position plus `Fixed64` height and expose `IVoxelOccupant.Position` as the world-space bridge GridForge consumes.

### 2. Shared Foundation Decisions - Done

Before implementing sparse or hex behavior, finish Phase 0 decisions from both detailed plans together.

Status: completed on 2026-06-11. The sparse and hex Phase 0 sections now carry
the shared decisions below.

Why:

- Sparse and hex both touch `GridConfiguration`, `VoxelGrid`, `GridWorld`, `GridTracer`, scan cells, blockers, and neighbors.
- Sparse wants a storage boundary.
- Hex wants a topology boundary.
- If either feature is implemented in isolation, it can accidentally bake in assumptions that make the other harder.

Target outcome:

- Sparse semantics are locked: configured voxels only, no read-time materialization, blockers affect configured voxels only.
- Topology semantics are locked: rectangular-prism and hex-prism grids share the same `VoxelGrid` public model.
- `VoxelIndex`, per-grid topology metrics, storage counts, and missing-neighbor behavior have explicit decisions.

Locked shared decisions:

- Storage and topology are orthogonal boundaries. Topology maps world-space input to topology-local coordinates or coverage ranges; storage decides whether physical voxels exist.
- `GridConfiguration` directly carries `GridStorageKind`, `GridTopologyKind`, and `GridTopologyMetrics` fields with dense rectangular defaults. Do not add sparse- or hex-specific wrapper configurations for the first releases.
- Sparse grids are configured-voxel only. They support creation-time configured indices or masks plus explicit runtime sparse add/remove APIs.
- Sparse grids stay within current `int` dimension and `Size` limits at first. Construction must validate overflow instead of silently wrapping.
- `ConfiguredVoxelCount` is the public physical-cell count. For dense grids it equals `Size`; for sparse grids it equals the number of configured voxels.
- Public dense storage exposure through `VoxelGrid.Voxels` should be replaced by storage-neutral deterministic enumeration APIs. Dense array access can remain internal to dense storage.
- `GridWorld` does not own cell geometry after topology extraction. Replace `VoxelSize`, `DefaultVoxelSize`, and `VoxelResolution` with per-grid `GridConfiguration.TopologyMetrics` during the breaking API cleanup.
- Rectangular default behavior is expressed by default rectangular topology metrics, not by a world-level voxel-size scalar. A rectangular convenience factory can set width, layer height, and length to the same fixed value for current cubic-grid behavior.
- World-level snapping helpers named around `VoxelSize` should be replaced by topology-normalization helpers or rectangular-specific helper names during rectangular topology extraction.
- `VoxelIndex` remains the single topology-local coordinate type for the first topology release: rectangular `(x, y, z)` and hex axial `(q, y, r)`.
- Hex examples default to `PointyTop`; `FlatTop` remains fully supported.
- Hex full-neighbor lookup uses the 20-cell hex-prism neighborhood; `HexDirectionUtility.Primary` keeps the 6 planar axial neighbors plus above/below available as the face-adjacent subset.
- Missing sparse voxels and unsupported cross-topology neighbor bridges are intentional absence, not default empty cells.

### 3. Rectangular Topology Extraction

Extract the current rectangular behavior behind a topology boundary before adding sparse storage.

Status: completed on 2026-06-12.

Why:

- The current dense grid implicitly mixes topology math and storage layout.
- Sparse storage should not be forced to understand rectangular coordinate math directly.
- Hex support will be easier if rectangular behavior is already proven through the same topology interface.

Target outcome:

- Current rectangular grids behave exactly as before.
- World-to-index, index-to-world, bounds snapping, neighbor offsets, and coverage hooks are routed through rectangular topology.
- No hex behavior is required yet.

### 4. Static Sparse Grid Support

Implement sparse construction, lookup, and query/mutation compatibility for configured voxels.

Status: completed on 2026-06-12. Static sparse construction, storage-neutral
query flows, explicit runtime sparse mutation, blocker reconciliation,
benchmarks, README/wiki/XML docs, and release metadata alignment are complete.
Runtime mutation was pulled forward from item 6 because the work stayed within
the rectangular storage boundary and did not depend on hex-prism topology
decisions.

Why:

- Sparse storage has clear value once topology math has an explicit owner.
- Static sparse construction established the storage boundary first; runtime
  mutation was added after blocker, occupant, partition, scan, and neighbor
  behavior were storage-neutral.
- Query behavior can be proven against dense rectangular behavior first.

Target outcome:

- Dense grids remain unchanged.
- Sparse grids use bounds as an address space and configured voxels as the actual physical cells.
- `TryGetGrid(...)` can resolve sparse grid bounds, while `TryGetGridAndVoxel(...)` fails for missing configured cells.
- `GridTracer`, blockers, occupants, partitions, scans, and neighbor lookup work without caller-side storage branching.

Completed runtime follow-through:

- Sparse voxel add/remove APIs are explicit and sparse-only.
- Removing a configured voxel is rejected when occupants, blockers, partitions, or active voxel event handlers make removal unsafe.
- Adding a voxel under active blocker coverage triggers sparse mutation reconciliation.

### 5. Hex Prism Grid Support

Implement hex-prism topology after rectangular topology and static sparse storage are stable.

Status: completed on 2026-06-13. The plan is archived under
`docs/feature-work/done`. Hex-prism construction, fixed-point projection,
inverse projection, cube rounding, world/grid/voxel lookup, typed
rectangular/hex neighbor APIs, topology-aware tracing, coverage, blockers,
occupants, scans, benchmark-backed performance hardening, docs, and release
alignment are complete. Sparse hex-prism validation was completed in the hex
follow-up plan; mixed-topology neighbor bridging remains deferred unless
explicitly designed.

Why:

- Hex changes coordinate projection, inverse projection, neighbor rules, coverage, tracing, and scan-cell coverage.
- Hex projection uses a deterministic GridForge-local `Sqrt3` constant added alongside hex construction.
- It benefits from already-separated topology and storage responsibilities.

Target outcome:

- One `GridWorld` can own rectangular-prism and hex-prism grids.
- Hex grids use axial coordinates in the XZ plane: `VoxelIndex.x = q`, `VoxelIndex.z = r`, `VoxelIndex.y = layer`.
- Both `FlatTop` and `PointyTop` orientations are supported.
- Query workflows remain world/grid/voxel based.
- Mixed rectangular/hex grids can coexist, with cross-topology neighbor bridging deferred unless explicitly designed.
- Sparse hex grids are supported as explicitly configured axial cells.

### 6. Runtime Sparse Mutation

Runtime sparse voxel add/remove was pulled forward and completed during sparse
storage work. Keep this roadmap item as the historical dependency marker and
use the sparse plan for implementation details.

Why:

- Runtime mutation interacts with blockers, obstacle state, occupant state, partition state, scan-cell lifetime, storage release, and neighbor-cache invalidation.
- Those behaviors are easier to specify once topology and storage responsibilities are stable.

Target outcome:

- Sparse voxel add/remove APIs are explicit.
- Removing a configured voxel is rejected when occupants, blockers, partitions, or other active state make removal unsafe.
- Adding a voxel under active blocker coverage has a documented reconciliation rule.

## Dependency Shape

```text
Vector2d API
  -> Shared sparse/topology decisions
  -> Rectangular topology extraction
      -> Static sparse storage + runtime sparse mutation
      -> Hex-prism topology
```

Static sparse storage and hex-prism topology can proceed in adjacent branches after rectangular topology extraction, but they should be integrated carefully because both touch tracing, scan cells, blockers, occupants, and neighbors.

## Release Strategy

Recommended release slices:

1. `Vector2d` query APIs and docs. Done on 2026-06-11.
2. Rectangular topology extraction with no behavior changes.
3. Static sparse rectangular grids, including explicit runtime sparse mutation.
4. Hex-prism grids.

Each slice should include:

- focused xUnit coverage
- `Debug` build and test validation
- `Release` and `ReleaseLean` validation before release
- wiki and README alignment for public API or behavior changes
- benchmarks for tracing, scan cells, blockers, storage, topology, or other hot paths

## Cross-Cutting Rules

- Keep `GridWorld` as the explicit owner of world state.
- Keep `VoxelGrid` as the public grid model.
- Keep `Voxel` as the mutable cell model for obstacles, occupants, partitions, identity, and neighbor cache behavior.
- Keep 2D, sparse, and topology differences behind focused helpers or strategies.
- Do not add engine-specific assumptions to the core library.
- Do not introduce floating-point math in deterministic runtime paths.
- Do not let pooled storage escape documented lifetimes.
- Do not preserve weak designs with compatibility shims when a clean boundary is needed.

## Highest-Risk Intersections

| Intersection | Risk | Recommended Handling |
| --- | --- | --- |
| Sparse + topology | Sparse storage assumes rectangular indexing | Extract rectangular topology before sparse storage. |
| Hex + tracing | Hex coverage becomes slow or nondeterministic | Start conservative and exact, then benchmark before optimizing. |
| 2D + scans | 2D scans accidentally behave like 3D scans | Use layer-locked XZ filtering with explicit tests. |
| Sparse + blockers | Missing sparse voxels look like empty dense voxels | Document configured-only blocker behavior and test it. |
| Hex + neighbors | Hex neighbors are forced into rectangular directions or opaque slot indices | Use separate `RectangularDirection` and `HexDirection` public APIs while keeping compact topology slots internal. |
| Runtime sparse mutation + blockers | Newly added voxels under active blockers miss obstacle state | Implemented through active-grid change reconciliation and sparse blocker tests. |
| Public docs + roadmap drift | Users see contradictory semantics | Update README, wiki, XML docs, tests, and benchmarks with each release slice. |

## Validation Baseline

Use the relevant detailed plan for focused test filters. Before considering a slice complete, run at least:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
git diff --check
```

For release-bound work, also run:

```bash
dotnet test GridForge.slnx --configuration Release --no-build
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py
```

Run BenchmarkDotNet when the slice touches storage, topology conversion, tracing, scan cells, blockers, occupant registration, or radius scans.
