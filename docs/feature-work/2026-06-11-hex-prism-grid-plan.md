# Hex Prism Grid Battle Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-grid topology support so one `GridWorld` can own rectangular-prism grids and hexagonal-prism grids while keeping world queries, tracing, blockers, occupants, partitions, and scan flows storage-neutral and topology-neutral from the caller's perspective.

**Architecture:** Keep `GridWorld` as the explicit world owner and `VoxelGrid` as the public grid type. Move shape-specific coordinate math into focused topology strategies: rectangular-prism topology preserves current behavior, while hex-prism topology maps flat XZ hex cells with optional vertical Y layers. Storage layout remains a separate concern so dense/sparse storage can compose with rectangular or hex topology over time.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`, `SwiftCollections`, xUnit v3, BenchmarkDotNet, optional MemoryPack package variant guards.

---

## Status

- Started: 2026-06-11
- Release posture: Likely breaking if `GridConfiguration`, `GridWorld.VoxelSize`, `SpatialDirection`, or `VoxelGrid.Voxels` public semantics change. The plan should bias toward additive compatibility where it does not preserve the wrong architecture.
- Backwards compatibility: Rectangular-prism grids must behave equivalently after topology extraction.
- Current state: Planning.

## Locked Decisions

- This feature is about grid topology, not changing `Voxel` into separate cell classes.
- `Voxel` remains the mutable cell model for obstacles, occupants, partitions, event hooks, identity, and cached neighbor data.
- Rectangular grids are treated as rectangular-prism topology: square or rectangular footprint on XZ, optional Y layers.
- Hex grids are treated as hex-prism topology: hexagonal footprint on XZ, optional Y layers.
- General 3D honeycomb or arbitrary polyhedral cells are out of scope.
- Hex topology supports both `FlatTop` and `PointyTop` orientation through configuration.
- Hex indexing uses axial coordinates in the horizontal plane: `VoxelIndex.x` is `q`, `VoxelIndex.z` is `r`, and `VoxelIndex.y` is the vertical layer.
- No floating-point math may be introduced into runtime topology math. Any required constants, including `Sqrt3`, must come from deterministic fixed-point code.
- Query APIs should not force user code to branch on topology. The query result may contain a grid whose topology is rectangular or hex, but lookup, scan, trace, blocker, occupant, and partition workflows should remain world/grid/voxel based.

## Non-Goals

- Do not add arbitrary rotation, custom skew, custom polygon cells, or user-supplied topology plugins in the first release.
- Do not add engine-specific coordinate assumptions to the core library.
- Do not make `GridWorld` a renderer or visual layout system.
- Do not introduce floating-point conversions in core spatial math.
- Do not make hex support depend on Unity, XNA, MonoGame, Unreal, or any engine adapter.
- Do not implement sparse hex storage in the first topology pass unless the sparse-grid plan has already landed its storage abstraction.

## Target Mental Model

Current rectangular grid:

```text
GridWorld
  -> VoxelGrid with rectangular-prism topology
  -> VoxelIndex(x, y, z)
  -> world position by rectangular grid spacing
```

Hex-prism grid:

```text
GridWorld
  -> VoxelGrid with hex-prism topology
  -> VoxelIndex(q, y, r)
  -> world position by axial hex projection and vertical layer height
```

The world-level pipeline should stay familiar:

```text
world-space input
  -> coarse GridWorld spatial hash lookup
  -> candidate VoxelGrid
  -> topology-specific position/index conversion
  -> storage lookup
  -> query or mutation
  -> version, event, and cache updates
```

## Architecture Direction

### Topology Boundary

Introduce an internal topology boundary owned by `VoxelGrid`.

Likely files:

- Create: `src/GridForge/Grids/Topology/IGridTopology.cs`
- Create: `src/GridForge/Grids/Topology/GridTopologyKind.cs`
- Create: `src/GridForge/Grids/Topology/RectangularPrismTopology.cs`
- Create: `src/GridForge/Grids/Topology/HexPrismTopology.cs`
- Create: `src/GridForge/Grids/Topology/HexOrientation.cs`
- Create: `src/GridForge/Grids/Topology/GridTopologyMetrics.cs`
- Modify: `src/GridForge/Configuration/GridConfiguration.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Modify: `src/GridForge/Utility/GridTracer.cs`
- Modify: `src/GridForge/Spatial/SpatialAwareness.cs`
- Modify: `src/GridForge/Spatial/SpatialDirection.cs`

Responsibilities:

- normalize grid bounds for one topology
- compute dimensions or address ranges
- map world position to topology-local `VoxelIndex`
- map `VoxelIndex` to voxel center `WorldPosition`
- determine whether a world position is inside the grid's actual topology coverage
- compute scan-cell keys or topology-local query blocks
- enumerate deterministic neighbor offsets
- identify boundary voxels and affected boundary ranges
- append topology-aware line and bounds coverage into caller-owned result lists

`VoxelGrid` should keep identity, world ownership, grid registration state, versioning, neighbors, obstacle count, active scan-cell set, and public query entry points. The topology object owns shape math.

### Storage Boundary Coordination

Topology and storage should remain orthogonal.

The sparse-grid plan introduces a storage boundary. This hex plan introduces a topology boundary. They should meet through small contracts:

- topology converts world positions and coverage regions into `VoxelIndex` or scan-cell ranges
- storage resolves whether those indices physically exist
- `VoxelGrid` coordinates identity, versioning, and mutation workflows

Target matrix:

| Topology | Dense Storage | Sparse Storage |
| --- | --- | --- |
| RectangularPrism | required baseline | covered by sparse-grid plan |
| HexPrism | first hex target | later target after both boundaries are stable |

Do not couple hex math to dense-only storage decisions.

### Hex Coordinate Model

Hex-prism topology should use axial coordinates in the XZ plane.

- `VoxelIndex.x` => axial `q`
- `VoxelIndex.z` => axial `r`
- `VoxelIndex.y` => vertical layer

The implicit cube coordinate is:

```text
s = -q - r
```

Neighbor offsets in the XZ plane:

```text
(+1,  0)
(+1, -1)
( 0, -1)
(-1,  0)
(-1, +1)
( 0, +1)
```

Vertical neighbors:

```text
(0, +1, 0)
(0, -1, 0)
```

The first release should define and document whether hex diagonal/edge-adjacent vertical neighbors are exposed. Recommended default: expose 6 planar neighbors plus above/below as the primary neighbor set, then add optional expanded neighbor sets only if a concrete query needs them.

### Hex Orientation

Support both orientations through an enum:

```csharp
public enum HexOrientation
{
    FlatTop,
    PointyTop
}
```

The orientation changes only world projection, inverse projection, and bounds expansion:

Pointy-top axial center projection on XZ:

```text
worldX = radius * Sqrt3 * (q + r / 2)
worldZ = radius * 3 / 2 * r
```

Flat-top axial center projection on XZ:

```text
worldX = radius * 3 / 2 * q
worldZ = radius * Sqrt3 * (r + q / 2)
```

All formulas must use `Fixed64` and deterministic constants.

### FixedMathSharp Dependency

GridForge should not own approximations of mathematical constants that belong in the deterministic math layer.

Likely FixedMathSharp files:

- Modify: `../FixedMathSharp/src/FixedMathSharp/Core/FixedMath.cs`
- Modify: `../FixedMathSharp/src/FixedMathSharp/Numerics/Scalars/Fixed64.cs`
- Test: `../FixedMathSharp/tests/FixedMathSharp.Tests/**/*`

Required support:

- add or verify a deterministic `Sqrt3` constant
- prefer a raw fixed-point payload, not `FromDouble(...)`, for public constants
- add exact raw-value tests and approximate value tests in FixedMathSharp
- update GridForge only after the constant is available through the package or project reference

The current FixedMathSharp checkout already has deterministic `FixedMath.Sqrt(Fixed64)` and raw-backed constants such as `Fixed64.Pi`. This plan assumes `Fixed64.Sqrt3` or an equivalent fixed constant is added before hex topology consumes it.

## Public API Direction

Candidate additions:

```csharp
public enum GridTopologyKind
{
    RectangularPrism,
    HexPrism
}

public enum HexOrientation
{
    FlatTop,
    PointyTop
}
```

Possible configuration shape:

```csharp
public readonly partial struct GridConfiguration
{
    public readonly GridTopologyKind TopologyKind;
    public readonly GridTopologyMetrics TopologyMetrics;
}
```

Possible metrics shape:

```csharp
public readonly struct GridTopologyMetrics
{
    public readonly Fixed64 CellRadius;
    public readonly Fixed64 CellWidth;
    public readonly Fixed64 CellLength;
    public readonly Fixed64 LayerHeight;
    public readonly HexOrientation HexOrientation;
}
```

The exact names can change during Phase 0, but the architecture should preserve these facts:

- rectangular grids default to current behavior
- hex grids require a positive horizontal radius and positive layer height
- `GridWorld.VoxelSize` becomes either a rectangular default metric or a legacy compatibility shortcut
- topology identity must be part of duplicate-grid detection; two grids with the same bounds but different topology or metrics are not equivalent
- XML docs must state whether `VoxelIndex` is rectangular `(x, y, z)` or hex axial `(q, y, r)` for a given topology

## Behavior Matrix

| Operation | Rectangular-Prism Grid | Hex-Prism Grid |
| --- | --- | --- |
| `TryGetGrid(position)` | resolves by coarse spatial hash and rectangular bounds | resolves by coarse spatial hash and hex topology coverage |
| `TryGetVoxel(position)` | snaps rectangular world position to `(x, y, z)` | projects world XZ to axial `(q, r)`, rounds deterministically, and resolves layer `y` |
| `TryGetVoxel(VoxelIndex)` | interprets index as rectangular local coordinates | interprets index as axial `q`, layer `y`, axial `r` |
| `Voxel.WorldPosition` | rectangular cell center | hex-prism center |
| neighbor lookup | current 6/20/26 direction model | hex planar neighbors plus vertical layer neighbors |
| `GridTracer.TraceLine(...)` | rectangular line coverage | hex line coverage through axial/cube interpolation and deterministic rounding |
| `GridTracer.GetCoveredVoxels(...)` | rectangular bounds coverage | conservative hex coverage over intersecting hex centers or cells |
| blockers | apply to covered rectangular voxels | apply to covered hex-prism voxels |
| radius scans | scan-cell candidate coverage plus exact distance check | topology-aware scan-cell candidate coverage plus exact distance check |

## Phase 0: Lock Topology Semantics And API Shape

Intent: agree on names, metrics, and compatibility posture before touching hot paths.

Likely files:

- `docs/feature-work/2026-06-11-hex-prism-grid-plan.md`
- `src/GridForge/Configuration/GridConfiguration.cs`
- `src/GridForge/Grids/Managers/GridWorld.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- `src/GridForge/Spatial/SpatialDirection.cs`
- `src/GridForge/Spatial/SpatialAwareness.cs`

Checklist:

- [ ] Decide final names: `GridTopologyKind`, `RectangularPrism`, `HexPrism`, `HexOrientation`, and topology metrics.
- [ ] Decide whether `GridWorld.VoxelSize` remains a rectangular default only, becomes obsolete, or is replaced by default topology metrics.
- [ ] Decide whether `GridConfiguration` directly carries topology fields or points to a separate topology configuration struct.
- [ ] Decide whether `VoxelIndex.x/y/z` remains the single topology-local coordinate type for the first release.
- [ ] Decide the default hex orientation for examples. Recommended default: `PointyTop` for common war-game and location-centered usage, while fully supporting `FlatTop`.
- [ ] Decide the primary hex neighbor set for first release. Recommended default: 6 planar neighbors plus above/below.
- [ ] Decide whether rectangular diagonal neighbor APIs remain rectangular-only or become topology-provided expanded neighbor sets.
- [ ] Record the decisions in this plan before implementation starts.

Exit criteria:

- [ ] There is one agreed meaning for rectangular and hex-prism topology.
- [ ] There is one agreed API surface for topology configuration.
- [ ] There is no unresolved ambiguity around orientation, vertical layers, `VoxelIndex`, neighbors, tracing, blockers, or scan cells.

## Phase 1: Add FixedMathSharp Support For Hex Constants

Intent: keep deterministic constants in the deterministic math library instead of GridForge.

Likely files:

- Modify: `../FixedMathSharp/src/FixedMathSharp/Core/FixedMath.cs`
- Modify: `../FixedMathSharp/src/FixedMathSharp/Numerics/Scalars/Fixed64.cs`
- Test: `../FixedMathSharp/tests/FixedMathSharp.Tests/Numerics/Scalars/Fixed64.Tests.cs`
- Test: `../FixedMathSharp/tests/FixedMathSharp.Tests/Core/FixedMath.Tests.cs`
- Docs if needed: `../FixedMathSharp/docs/wiki/fixed64-representation.md`

Checklist:

- [ ] Add a deterministic raw fixed-point constant for `sqrt(3)`.
- [ ] Expose it as `Fixed64.Sqrt3` or another approved public constant.
- [ ] Add tests that assert the exact raw payload.
- [ ] Add tests that assert the value is within a fixed tolerance of `FixedMath.Sqrt(new Fixed64(3))`.
- [ ] Build and test FixedMathSharp in `Debug`, `Release`, and `ReleaseLean` if package variants are affected.
- [ ] Update GridForge dependency guidance once the constant is available.

Exit criteria:

- [ ] GridForge can consume `Sqrt3` without local approximation or floating-point conversion.
- [ ] FixedMathSharp documents and tests the constant as deterministic fixed-point data.

Validation:

```bash
dotnet restore ../FixedMathSharp/FixedMathSharp.slnx
dotnet build ../FixedMathSharp/FixedMathSharp.slnx --configuration Debug
dotnet test ../FixedMathSharp/FixedMathSharp.slnx --configuration Debug --no-build
```

## Phase 2: Extract Rectangular Topology Without Behavior Changes

Intent: introduce the topology boundary while preserving current rectangular behavior.

Likely files:

- Create: `src/GridForge/Grids/Topology/IGridTopology.cs`
- Create: `src/GridForge/Grids/Topology/GridTopologyKind.cs`
- Create: `src/GridForge/Grids/Topology/GridTopologyMetrics.cs`
- Create: `src/GridForge/Grids/Topology/RectangularPrismTopology.cs`
- Modify: `src/GridForge/Configuration/GridConfiguration.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Modify: `src/GridForge/Utility/GridTracer.cs`
- Test: `tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/GridWorld.Tests.cs`
- Test: `tests/GridForge.Tests/Utility/GridTracer.Tests.cs`

Checklist:

- [ ] Move rectangular dimension calculation out of `VoxelGrid.Initialize(...)` and into `RectangularPrismTopology`.
- [ ] Move world-position to `VoxelIndex` conversion into topology.
- [ ] Move `VoxelIndex` to world-position conversion into topology.
- [ ] Move rectangular floor/ceil snap behavior behind topology while preserving current public results.
- [ ] Keep current `GridWorld.SnapBoundsToVoxelSize(...)`, `FloorToVoxelSize(...)`, and `CeilToVoxelSize(...)` behavior during this phase, either as compatibility wrappers or rectangular helpers.
- [ ] Ensure bounds normalization still uses owning world/default rectangular metrics for existing callers.
- [ ] Ensure `BoundsTracker` duplicate keys include any new topology identity while preserving current duplicate behavior for default rectangular grids.
- [ ] Add dense rectangular regression tests for dimensions, exact bounds, snapped positions, voxel lookup, scan-cell keys, tracing, blockers, and neighbor traversal.

Exit criteria:

- [ ] Rectangular grids behave exactly as before.
- [ ] No hex behavior exists yet beyond topology-neutral interfaces.
- [ ] Existing tests pass under `Debug`.

Validation:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

## Phase 3: Add Hex-Prism Topology Construction And Lookup

Intent: create hex-prism grids that resolve world positions and indices without changing user-facing query workflows.

Likely files:

- Create: `src/GridForge/Grids/Topology/HexOrientation.cs`
- Create: `src/GridForge/Grids/Topology/HexPrismTopology.cs`
- Create: `src/GridForge/Spatial/HexCoordinateUtility.cs`
- Modify: `src/GridForge/Configuration/GridConfiguration.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Test: `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Spatial/HexCoordinateUtility.Tests.cs`

Checklist:

- [ ] Add `HexOrientation.FlatTop` and `HexOrientation.PointyTop`.
- [ ] Add hex metrics validation for positive radius and positive layer height.
- [ ] Implement axial-to-world projection for flat-top and pointy-top using `Fixed64.Sqrt3`.
- [ ] Implement world-to-axial inverse projection for flat-top and pointy-top using fixed-point math.
- [ ] Implement deterministic cube-coordinate rounding for projected axial coordinates.
- [ ] Generate hex-prism voxel centers from `VoxelIndex(q, y, r)`.
- [ ] Resolve `TryGetVoxel(position)` through hex projection and layer selection.
- [ ] Resolve `TryGetVoxel(VoxelIndex)` as axial `q`, layer `y`, axial `r`.
- [ ] Define hex grid address ranges from normalized bounds without relying on rectangular `Width * Height * Length` assumptions.
- [ ] Ensure `TryGetGrid(position)` rejects positions inside the coarse AABB but outside the actual hex topology coverage.
- [ ] Add tests for flat-top projection, pointy-top projection, inverse projection, center lookup, edge lookup, outside lookup, exact layer lookup, and invalid metrics.

Exit criteria:

- [ ] A `GridWorld` can own rectangular and hex-prism grids at the same time.
- [ ] `TryGetGridAndVoxel(...)` works for both topologies without caller branching.
- [ ] Hex orientation differences are covered by exact deterministic tests.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "HexPrismGrid|HexCoordinateUtility|VoxelGrid|GridWorld"
```

## Phase 4: Topology-Aware Neighbors And Conjoined Grids

Intent: make adjacency correct when grids of the same or different topology coexist.

Likely files:

- Create: `src/GridForge/Spatial/GridNeighborKind.cs`
- Create: `src/GridForge/Spatial/GridNeighborOffset.cs`
- Modify: `src/GridForge/Spatial/SpatialAwareness.cs`
- Modify: `src/GridForge/Spatial/SpatialDirection.cs`
- Modify: `src/GridForge/Grids/Managers/GridDirectionUtility.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Nodes/Voxel.cs`
- Test: `tests/GridForge.Tests/Grids/Voxel.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`

Checklist:

- [ ] Add topology-provided neighbor offset enumeration.
- [ ] Preserve existing rectangular `SpatialDirection` ordering for rectangular grids.
- [ ] Add deterministic hex neighbor ordering for 6 planar neighbors plus above/below.
- [ ] Decide and implement how `Voxel.GetNeighbors(...)` exposes topology-specific neighbor identity without forcing hex into the 26-direction rectangular enum.
- [ ] Invalidate boundary neighbor caches through topology-aware boundary ranges.
- [ ] Validate same-topology conjoined neighbors for rectangular-to-rectangular and hex-to-hex grids.
- [ ] Validate mixed-topology overlap behavior. Recommended first release: allow world-level coexistence, but only same-topology conjoined voxel-neighbor bridging unless a mixed-topology mapping is explicitly designed.
- [ ] Add tests for missing neighbors, same-grid hex planar neighbors, vertical hex neighbors, hex grid boundary neighbors, and mixed rectangular/hex coexistence.

Exit criteria:

- [ ] Rectangular neighbor behavior remains unchanged.
- [ ] Hex neighbor behavior is deterministic and documented.
- [ ] Mixed topology grids can coexist in one world without accidental neighbor corruption.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "Voxel|VoxelGrid|HexPrismGrid"
```

## Phase 5: Topology-Aware Coverage, Tracing, Blockers, And Scans

Intent: make query and mutation workflows seamless over rectangular and hex-prism grids.

Likely files:

- Modify: `src/GridForge/Utility/GridTracer.cs`
- Modify: `src/GridForge/Blockers/Blocker.cs`
- Modify: `src/GridForge/Grids/Managers/GridScanManager.cs`
- Modify: `src/GridForge/Grids/Managers/GridObstacleManager.cs`
- Modify: `src/GridForge/Grids/Managers/GridOccupantManager.cs`
- Test: `tests/GridForge.Tests/Utility/GridTracer.Tests.cs`
- Test: `tests/GridForge.Tests/Blockers/BlockerTests.cs`
- Test: `tests/GridForge.Tests/Grids/ManagerCoverage.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/ScanCell.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`

Checklist:

- [ ] Move covered-voxel enumeration behind topology-aware methods.
- [ ] Move covered-scan-cell enumeration behind topology-aware methods.
- [ ] Implement hex line tracing through axial/cube interpolation and deterministic rounding.
- [ ] Implement conservative hex bounds coverage for world-space bounds.
- [ ] Ensure blockers apply to all covered hex-prism voxels and remove by token correctly.
- [ ] Ensure cached blocker removal via `WorldVoxelIndex` works for hex grids.
- [ ] Ensure occupant registration, deregistration, ticket lookup, and radius scan work on hex grids.
- [ ] Ensure radius scans still apply exact fixed-point distance checks after scan-cell candidate enumeration.
- [ ] Add tests for line traces across flat-top and pointy-top hex grids.
- [ ] Add tests for blockers spanning rectangular and hex grids in the same world.

Exit criteria:

- [ ] `GridTracer`, blockers, occupants, and scans do not require caller-side topology branching.
- [ ] Hex coverage is deterministic, documented, and benchmark-ready.
- [ ] Rectangular coverage remains unchanged.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

## Phase 6: Performance Hardening And Benchmarks

Intent: prove hex support is fast enough for large-scale simulation workloads.

Likely files:

- Modify: `tests/GridForge.Benchmarks/**/*`
- Modify: `src/GridForge/Grids/Topology/**/*`
- Modify: `src/GridForge/Utility/GridTracer.cs`

Benchmark scenarios:

- rectangular topology lookup baseline before and after extraction
- hex flat-top construction and lookup
- hex pointy-top construction and lookup
- hex world-to-index projection
- hex index-to-world projection
- hex line tracing
- hex bounds coverage over small, medium, and large regions
- blocker apply/remove over hex grids
- occupant registration and radius scans over hex grids
- mixed rectangular/hex world lookup

Checklist:

- [ ] Add BenchmarkDotNet benchmarks for topology conversion hot paths.
- [ ] Compare rectangular baseline before and after topology extraction.
- [ ] Compare flat-top and pointy-top projection cost.
- [ ] Validate no per-query allocations in hot topology conversion paths.
- [ ] Optimize hex rounding and coverage only after benchmark evidence.
- [ ] Decide whether lookup tables or cached per-grid constants are justified.

Exit criteria:

- [ ] Rectangular hot paths are not materially regressed.
- [ ] Hex lookup and coverage performance is documented with realistic tradeoffs.
- [ ] Any added caches are deterministic, reset-safe, and pooling-safe.

Validation:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*Hex*'
```

## Phase 7: Documentation And Release Alignment

Intent: make topology behavior clear without turning the README into a geometry textbook.

Likely files:

- Modify: `README.md`
- Modify: `docs/wiki/Home.md`
- Modify: `docs/wiki/Core-Concepts.md`
- Modify: `docs/wiki/VoxelGrid-and-Voxel-Model.md`
- Modify: `docs/wiki/Architecture-Overview.md`
- Modify: `docs/wiki/GridTracer-and-Coverage.md`
- Modify: `docs/wiki/Blockers-and-Obstacles.md`
- Modify: `docs/wiki/Scan-Cells-and-Query-Flow.md`
- Modify: `docs/wiki/Occupants-and-Partitions.md`
- Modify: `docs/wiki/Determinism-Snapping-and-Pooling.md`
- Modify: `docs/wiki/Testing-and-Benchmarking.md`
- Modify: `src/GridForge/GridForge.csproj` if package metadata or XML docs need new wording

Checklist:

- [ ] Explain topology as rectangular-prism or hex-prism cells owned by the same `VoxelGrid` public model.
- [ ] Explain that hex grids use axial `q/r` coordinates through `VoxelIndex.x/z` and vertical layer through `VoxelIndex.y`.
- [ ] Explain flat-top versus pointy-top orientation without implying engine-specific rendering behavior.
- [ ] Document that `GridWorld` can own mixed topology grids.
- [ ] Document mixed-topology neighbor limitations for the first release.
- [ ] Document blocker and trace behavior over hex grids.
- [ ] Document deterministic fixed-point constraints and the `Sqrt3` dependency from FixedMathSharp.
- [ ] Update XML docs for topology configuration, orientation, metrics, and topology-local `VoxelIndex` meaning.
- [ ] Run wiki link rewrite tests if docs links change.

Exit criteria:

- [ ] README, wiki, XML docs, tests, benchmarks, and package metadata tell the same topology story.
- [ ] Users can choose rectangular or hex-prism grids without needing engine-specific assumptions.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py
git diff --check
```

## Test Matrix

Minimum topology coverage before release:

- rectangular grid dimensions and lookup unchanged
- rectangular tracing and blocker behavior unchanged
- `GridWorld` can own rectangular and hex grids together
- duplicate-grid detection includes topology and metrics
- hex flat-top index-to-world projection
- hex flat-top world-to-index projection
- hex pointy-top index-to-world projection
- hex pointy-top world-to-index projection
- cube-coordinate rounding at hex boundaries
- vertical layer lookup
- invalid radius and layer-height rejection
- hex planar neighbor order
- hex vertical neighbor order
- hex boundary invalidation
- hex line tracing
- hex bounds coverage
- hex blocker apply/remove/reapply
- hex cached blocker removal
- hex occupant registration and radius scan
- mixed rectangular/hex world lookup
- mixed rectangular/hex same-world blocker coverage
- Release and ReleaseLean build and test coverage

## Risk Register

| Risk | Mitigation |
| --- | --- |
| Topology leaks into every caller API | Keep topology inside `VoxelGrid`, `GridTracer`, and manager internals; public workflows remain world/grid/voxel based. |
| Rectangular hot paths regress | Extract rectangular topology first and benchmark before adding hex behavior. |
| Floating-point constants sneak into hex math | Add `Sqrt3` to FixedMathSharp as raw fixed data and reject runtime `double` conversions in GridForge topology code. |
| `SpatialDirection` cannot represent hex neighbors cleanly | Introduce topology-aware neighbor descriptors instead of forcing hex into the 26-direction rectangular enum. |
| Bounds coverage over hex cells is too slow | Start conservative and correct, then benchmark coverage algorithms before optimizing. |
| Mixed topology neighbor bridging becomes ambiguous | Permit mixed topology coexistence first; defer cross-topology voxel-neighbor bridging until a concrete mapping is designed. |
| `GridWorld.VoxelSize` becomes misleading | Decide whether it is a default rectangular metric, legacy convenience, or replacement candidate during Phase 0. |
| `VoxelIndex` meaning becomes unclear | Document topology-local coordinate meaning in XML docs and wiki, and keep exact tests for both topologies. |
| ReleaseLean breaks through new dependency usage | Keep MemoryPack and package-variant guards consistent with existing project patterns. |

## Recommended Implementation Order

1. Finish Phase 0 decisions in this document.
2. Add or verify `Fixed64.Sqrt3` in FixedMathSharp.
3. Extract rectangular topology and prove behavior did not move.
4. Add hex-prism construction and lookup for both orientations.
5. Add topology-aware neighbor behavior.
6. Add topology-aware tracing, coverage, blockers, occupants, and scans.
7. Run benchmarks and optimize only where evidence points.
8. Complete docs and release alignment.

