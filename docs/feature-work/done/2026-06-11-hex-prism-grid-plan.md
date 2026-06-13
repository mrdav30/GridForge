# Hex Prism Grid Battle Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Done

**Goal:** Add per-grid topology support so one `GridWorld` can own rectangular-prism grids and hexagonal-prism grids while keeping world queries, tracing, blockers, occupants, partitions, and scan flows storage-neutral and topology-neutral from the caller's perspective.

**Architecture:** Keep `GridWorld` as the explicit world owner and `VoxelGrid` as the public grid type. Move shape-specific coordinate math into focused topology strategies: rectangular-prism topology preserves current behavior, while hex-prism topology maps flat XZ hex cells with optional vertical Y layers. Storage layout remains a separate concern so dense/sparse storage can compose with rectangular or hex topology over time.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`, `SwiftCollections`, xUnit v3, BenchmarkDotNet, optional MemoryPack package variant guards.

---

## Status

- Started: 2026-06-11
- Release posture: Likely breaking if `GridConfiguration`, world-level cell-size/snapping APIs, rectangular/hex direction APIs, or `VoxelGrid.Voxels` public semantics change. The plan should bias toward clean boundaries over additive compatibility where the old API preserves the wrong architecture.
- Backwards compatibility: Rectangular-prism grids must behave equivalently after topology extraction.
- Current state: Done; rectangular topology extraction, hex-prism construction/lookup, typed rectangular/hex neighbor APIs, topology-aware query flows, benchmark-backed hex performance hardening, docs, and release alignment are complete.
- Shared foundation decisions: Completed 2026-06-11 in coordination with the sparse-grid plan.

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
- Final public names are `GridTopologyKind`, `RectangularPrism`, `HexPrism`, `HexOrientation`, and `GridTopologyMetrics`.
- `GridConfiguration` directly carries `GridTopologyKind` and `GridTopologyMetrics`; do not introduce separate rectangular or hex configuration wrappers in the first release.
- `GridWorld` does not own cell geometry after topology extraction. Per-grid cell geometry comes from `GridConfiguration.TopologyMetrics`.
- Replace `GridWorld.VoxelSize`, `DefaultVoxelSize`, `VoxelResolution`, and `*VoxelSize` snapping helpers during the breaking API cleanup instead of carrying them forward as topology API.
- `VoxelIndex` remains the single topology-local coordinate type for the first topology release.
- Hex examples default to `HexOrientation.PointyTop`; `FlatTop` remains fully supported.
- Hex full-neighbor lookup uses the 20-cell hex-prism neighborhood.
- `HexDirectionUtility.Primary` keeps the 6 planar axial neighbors plus above/below available as the face-adjacent subset.
- Rectangular and hex direction utilities expose matching deterministic neighbor subsets for callers that need face-adjacent, planar, vertical, layer, or diagonal groups.

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
- Existing: `src/GridForge/Grids/Topology/HexOrientation.cs`
- Create: `src/GridForge/Grids/Topology/GridTopologyMetrics.cs`
- Modify: `src/GridForge/Configuration/GridConfiguration.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Modify: `src/GridForge/Utility/GridTracer.cs`
- Modify: `src/GridForge/Spatial/RectangularDirectionUtility.cs`
- Modify: `src/GridForge/Spatial/RectangularDirection.cs`

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
| HexPrism | first hex target | follow-up validation tracked separately |

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

Vertical and layer neighbors:

```text
(0, +1, 0)
(0, -1, 0)
(+1, +1,  0) (+1, +1, -1) ( 0, +1, -1) (-1, +1,  0) (-1, +1, +1) ( 0, +1, +1)
(+1, -1,  0) (+1, -1, -1) ( 0, -1, -1) (-1, -1,  0) (-1, -1, +1) ( 0, -1, +1)
```

`GetHexNeighbors(...)` exposes all 20 directions: 6 same-layer planar
neighbors, 7 neighbors on the layer below, and 7 neighbors on the layer above.
`HexDirectionUtility.Primary` exposes the 8 face-adjacent directions when a
caller only wants planar movement plus direct above/below.

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

### GridForge Hex Constant

GridForge should own the first `Sqrt3` constant locally because hex-prism topology is the first consumer and FixedMathSharp just completed a major release. The later decision about whether `Sqrt3` belongs in FixedMathSharp is tracked in `2026-06-13-hex-prism-follow-up-plan.md`.

Likely GridForge files:

- Place next to the first actual consumer because `HexCoordinateUtility` owns every use.
- Test: `tests/GridForge.Tests/Spatial/HexCoordinateUtility.Tests.cs`

Required support:

- add a GridForge-local deterministic `Sqrt3` constant
- prefer a raw fixed-point payload, not `FromDouble(...)`
- use `const long Sqrt3Raw = 7439101574L` and `static readonly Fixed64 Sqrt3 = Fixed64.FromRaw(Sqrt3Raw)`
- keep the constant internal unless a public GridForge topology API needs it
- add exact raw-value tests and fixed-point tolerance tests against `FixedMath.Sqrt(new Fixed64(3))`

The current FixedMathSharp checkout already has deterministic `FixedMath.Sqrt(Fixed64)` and public raw construction through `Fixed64.FromRaw(long)`. This plan does not require a FixedMathSharp change for hex support.

## Public API Direction

Public additions:

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

Configuration shape:

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

Phase 0 locked these public-shape facts:

- rectangular grids default to current behavior
- hex grids require a positive horizontal radius and positive layer height
- `GridConfiguration.TopologyMetrics` owns cell geometry for every topology
- default rectangular metrics reproduce current cubic-grid behavior by using the same width, layer height, and length
- topology identity must be part of duplicate-grid detection; two grids with the same bounds but different topology or metrics are not equivalent
- XML docs must state whether `VoxelIndex` is rectangular `(x, y, z)` or hex axial `(q, y, r)` for a given topology

## Behavior Matrix

| Operation | Rectangular-Prism Grid | Hex-Prism Grid |
| --- | --- | --- |
| `TryGetGrid(position)` | resolves by coarse spatial hash and rectangular bounds | resolves by coarse spatial hash and hex topology coverage |
| `TryGetVoxel(position)` | snaps rectangular world position to `(x, y, z)` | projects world XZ to axial `(q, r)`, rounds deterministically, and resolves layer `y` |
| `TryGetVoxel(VoxelIndex)` | interprets index as rectangular local coordinates | interprets index as axial `q`, layer `y`, axial `r` |
| `Voxel.WorldPosition` | rectangular cell center | hex-prism center |
| neighbor lookup | 26-direction full neighborhood with subset arrays | 20-direction full neighborhood with subset arrays |
| `GridTracer.TraceLine(...)` | rectangular line coverage | hex line coverage through axial/cube interpolation and deterministic rounding |
| `GridTracer.GetCoveredVoxels(...)` | rectangular bounds coverage | conservative hex coverage over intersecting hex centers or cells |
| blockers | apply to covered rectangular voxels | apply to covered hex-prism voxels |
| radius scans | scan-cell candidate coverage plus exact distance check | topology-aware scan-cell candidate coverage plus exact distance check |

## Phase 0: Lock Topology Semantics And API Shape

Intent: agree on names, metrics, and compatibility posture before touching hot paths.

Likely files:

- `docs/feature-work/done/2026-06-11-hex-prism-grid-plan.md`
- `src/GridForge/Configuration/GridConfiguration.cs`
- `src/GridForge/Grids/Managers/GridWorld.cs`
- `src/GridForge/Grids/VoxelGrid.cs`
- `src/GridForge/Spatial/RectangularDirection.cs`
- `src/GridForge/Spatial/RectangularDirectionUtility.cs`

Checklist:

- [x] Use final names: `GridTopologyKind`, `RectangularPrism`, `HexPrism`, `HexOrientation`, and `GridTopologyMetrics`.
- [x] Move cell geometry ownership out of `GridWorld` and into per-grid `GridConfiguration.TopologyMetrics`.
- [x] Use direct `GridConfiguration` topology fields; do not add a separate topology configuration wrapper.
- [x] Keep `VoxelIndex.x/y/z` as the single topology-local coordinate type for the first release.
- [x] Use `PointyTop` as the default hex orientation for examples while fully supporting `FlatTop`.
- [x] Use the 20-cell hex-prism neighborhood for full hex neighbor lookup.
- [x] Keep the 8 face-adjacent hex directions available through `HexDirectionUtility.Primary`.
- [x] Add matching rectangular and hex direction subsets for face-adjacent, planar, vertical, layer, and diagonal groups.
- [x] Record the decisions in this plan before implementation starts.

Decision record:

- Use `GridTopologyKind.RectangularPrism`, `GridTopologyKind.HexPrism`, `HexOrientation.FlatTop`, `HexOrientation.PointyTop`, and `GridTopologyMetrics`.
- `GridWorld` does not own cell geometry after topology extraction. Per-grid `GridConfiguration.TopologyMetrics` is the source for rectangular cell width, layer height, length, hex radius, hex layer height, and hex orientation.
- Replace `GridWorld.VoxelSize`, `DefaultVoxelSize`, `VoxelResolution`, and public `*VoxelSize` snapping helper names during the breaking API cleanup.
- Default rectangular metrics reproduce current cubic-grid behavior by setting cell width, layer height, and length to the same fixed value.
- `GridConfiguration` directly carries topology kind and metrics fields with rectangular defaults. Topology identity and metrics participate in duplicate-grid detection.
- `VoxelIndex` remains the single topology-local coordinate type: rectangular grids use `(x, y, z)`, while hex grids use axial `(q, y, r)`.
- Documentation and examples default to `PointyTop`; `FlatTop` is first-class and must have exact tests.
- The full hex neighbor surface is 20 deterministic directions: 6 same-layer planar neighbors, 7 below-layer neighbors, and 7 above-layer neighbors.
- `HexDirectionUtility.Primary` carries the 8 face-adjacent directions: the 6 planar axial neighbors plus direct below/above.
- Existing rectangular diagonal APIs remain rectangular-only. Hex should use `HexDirection` rather than overloading the 26-direction rectangular `RectangularDirection` enum.
- Mixed rectangular/hex grids may coexist in one `GridWorld`, but cross-topology voxel-neighbor bridging is intentional absence until an explicit mapping is designed.

Exit criteria:

- [x] There is one agreed meaning for rectangular and hex-prism topology.
- [x] There is one agreed API surface for topology configuration.
- [x] There is no unresolved ambiguity around orientation, vertical layers, `VoxelIndex`, neighbors, tracing, blockers, or scan cells.

## Phase 1: Extract Rectangular Topology Without Behavior Changes

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

- [x] Move rectangular dimension calculation out of `VoxelGrid.Initialize(...)` and into `RectangularPrismTopology`.
- [x] Move world-position to `VoxelIndex` conversion into topology.
- [x] Move `VoxelIndex` to world-position conversion into topology.
- [x] Move rectangular floor/ceil snap behavior behind topology while preserving current public results.
- [x] Move current `GridWorld.SnapBoundsToVoxelSize(...)`, `FloorToVoxelSize(...)`, and `CeilToVoxelSize(...)` behavior behind rectangular topology and replace public `*VoxelSize` names with topology-neutral or rectangular-specific names.
- [x] Ensure bounds normalization uses each grid's topology metrics; default rectangular metrics preserve current cubic-grid results for existing-style callers.
- [x] Ensure `BoundsTracker` duplicate keys include any new topology identity while preserving current duplicate behavior for default rectangular grids.
- [x] Add dense rectangular regression tests for dimensions, exact bounds, snapped positions, voxel lookup, scan-cell keys, tracing, blockers, and neighbor traversal.

Exit criteria:

- [x] Rectangular grids behave exactly as before.
- [x] No hex behavior exists yet beyond topology-neutral interfaces.
- [x] Existing tests pass under `Debug`.

Progress notes:

- Completed 2026-06-12.
- Added `GridTopologyKind`, `HexOrientation`, `GridTopologyMetrics`, `IGridTopology`, and `RectangularPrismTopology`.
- Moved rectangular cell geometry to `GridConfiguration.TopologyMetrics`.
- Removed world-level `VoxelSize`, `DefaultVoxelSize`, `VoxelResolution`, and public `*VoxelSize` snapping helpers.
- Preserved rectangular tracing, scan-cell coverage, blocker, scan, lookup, and neighbor behavior under `Debug` tests.

Validation:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

## Phase 2: Add Hex-Prism Topology Construction And Lookup

Intent: create hex-prism grids that resolve world positions and indices without changing user-facing query workflows.

Likely files:

- Existing: `src/GridForge/Grids/Topology/HexOrientation.cs`
- Create: `src/GridForge/Grids/Topology/HexPrismTopology.cs`
- Create: `src/GridForge/Spatial/HexCoordinateUtility.cs`
- Modify: `src/GridForge/Configuration/GridConfiguration.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Test: `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Spatial/HexCoordinateUtility.Tests.cs`

Checklist:

- [x] Add `const long Sqrt3Raw = 7439101574L` in the GridForge topology or hex coordinate layer.
- [x] Add `internal static readonly Fixed64 Sqrt3 = Fixed64.FromRaw(Sqrt3Raw)` next to the first actual hex topology consumer.
- [x] Do not expose `Sqrt3` as a FixedMathSharp public constant in this roadmap slice.
- [x] Add tests that assert the exact raw payload.
- [x] Add tests that assert the value is within a fixed tolerance of `FixedMath.Sqrt(new Fixed64(3))`.
- [x] Add `HexOrientation.FlatTop` and `HexOrientation.PointyTop`.
- [x] Add hex metrics validation for positive radius and positive layer height.
- [x] Implement axial-to-world projection for flat-top and pointy-top using the GridForge-local `Sqrt3`.
- [x] Implement world-to-axial inverse projection for flat-top and pointy-top using fixed-point math.
- [x] Implement deterministic cube-coordinate rounding for projected axial coordinates.
- [x] Generate hex-prism voxel centers from `VoxelIndex(q, y, r)`.
- [x] Resolve `TryGetVoxel(position)` through hex projection and layer selection.
- [x] Resolve `TryGetVoxel(VoxelIndex)` as axial `q`, layer `y`, axial `r`.
- [x] Define hex grid address ranges from normalized bounds without relying on rectangular `Width * Height * Length` assumptions.
- [x] Ensure `TryGetGrid(position)` rejects positions inside the coarse AABB but outside the actual hex topology coverage.
- [x] Add tests for flat-top projection, pointy-top projection, inverse projection, center lookup, edge lookup, outside lookup, exact layer lookup, and invalid metrics.

Exit criteria:

- [x] GridForge can consume `Sqrt3` without local approximation or floating-point conversion.
- [x] No FixedMathSharp source, package, or release workflow change is required for this hex topology slice.
- [x] A `GridWorld` can own rectangular and hex-prism grids at the same time.
- [x] `TryGetGridAndVoxel(...)` works for both topologies without caller branching.
- [x] Hex orientation differences are covered by exact deterministic tests.

Progress notes:

- Completed 2026-06-12.
- Added GridForge-local `HexCoordinateUtility.Sqrt3Raw` and `Sqrt3` beside the first hex topology consumer instead of changing FixedMathSharp.
- Added `GridTopologyMetrics.Hex(...)`, `HexCoordinateUtility`, `HexPrismTopology`, and factory creation for `GridTopologyKind.HexPrism`.
- Implemented fixed-point flat-top and pointy-top axial projection, inverse projection, cube rounding, layer selection, center generation, and point lookup.
- Hex grids use `VoxelIndex.x = q`, `VoxelIndex.y = layer`, and `VoxelIndex.z = r`; `BoundsMin` anchors local axial `(0, 0)`.
- Bounds normalization expands the max corner onto the hex lattice for the configured orientation. Topology-aware neighbors, tracing, scan-cell coverage, blockers, and scans were completed in later phases.
- Added construction and lookup tests for invalid metrics, exact `Sqrt3` payload, projection/inverse projection, mixed rectangular-plus-hex worlds, center lookup, exact layer lookup, and coarse-AABB rejection.

Validation:

```bash
dotnet test GridForge.slnx --configuration Debug --filter "HexPrismGrid|HexCoordinateUtility"
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
git diff --check
```

Results: focused Phase 2 tests passed 11/11, full `Debug` tests passed
264/264, full `ReleaseLean` tests passed 266/266, and `git diff --check`
reported no whitespace errors.

## Phase 3: Topology-Aware Neighbors And Conjoined Grids

Intent: make adjacency correct when grids of the same or different topology coexist.

Likely files:

- Create: `src/GridForge/Spatial/HexDirection.cs`
- Create: `src/GridForge/Spatial/HexDirectionUtility.cs`
- Rename: `src/GridForge/Spatial/SpatialDirection.cs` to `src/GridForge/Spatial/RectangularDirection.cs`
- Rename: `src/GridForge/Spatial/SpatialAwareness.cs` to `src/GridForge/Spatial/RectangularDirectionUtility.cs`
- Delete: `src/GridForge/Grids/Managers/GridDirectionUtility.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Modify: `src/GridForge/Grids/Nodes/Voxel.cs`
- Modify: `tests/GridForge.Benchmarks/Memory/NeighborCacheBenchmarks.cs`
- Modify: `tests/GridForge.Benchmarks/Memory/SparseVoxelGridBenchmarks.cs`
- Test: `tests/GridForge.Tests/Grids/Voxel.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`

Checklist:

- [x] Add internal topology-provided neighbor slot enumeration.
- [x] Preserve existing rectangular direction ordering for rectangular grids through `RectangularDirection`.
- [x] Add deterministic hex neighbor ordering for the full 20-cell hex-prism neighborhood.
- [x] Preserve the 8 face-adjacent hex directions as `HexDirectionUtility.Primary`.
- [x] Split public neighbor APIs into `Voxel.GetRectangularNeighbors(...)` / `TryGetRectangularNeighbor(...)` and `Voxel.GetHexNeighbors(...)` / `TryGetHexNeighbor(...)`.
- [x] Invalidate boundary neighbor caches through topology-aware boundary ranges.
- [x] Validate same-topology conjoined neighbors for rectangular-to-rectangular and hex-to-hex grids.
- [x] Validate mixed-topology overlap behavior. Recommended first release: allow world-level coexistence, but only same-topology conjoined voxel-neighbor bridging unless a mixed-topology mapping is explicitly designed.
- [x] Add tests for missing neighbors, same-grid hex planar neighbors, vertical hex neighbors, hex grid boundary neighbors, and mixed rectangular/hex coexistence.

Exit criteria:

- [x] Rectangular neighbor behavior remains unchanged.
- [x] Hex neighbor behavior is deterministic and documented.
- [x] Mixed topology grids can coexist in one world without accidental neighbor corruption.

Progress notes:

- Completed 2026-06-12.
- Added `RectangularDirection` and `HexDirection` as separate public direction enums so callers do not need topology slot indices or cross-topology descriptors.
- `RectangularPrismTopology` now exposes the existing 26 rectangular offsets internally in their original order.
- `HexPrismTopology` exposes 20 deterministic offsets: 6 same-layer planar neighbors, 7 below-layer neighbors, and 7 above-layer neighbors.
- `RectangularDirectionUtility` and `HexDirectionUtility` expose deterministic subsets for `Primary`, `Planar`, `Vertical`, layer groups, and vertical diagonals.
- `Voxel.GetRectangularNeighbors(...)`, `Voxel.TryGetRectangularNeighbor(...)`, `Voxel.GetHexNeighbors(...)`, and `Voxel.TryGetHexNeighbor(...)` expose topology-specific direction identity without asking callers to know neighbor slot indices.
- Grid neighbor sets are keyed internally by topology-local neighbor slot. Mixed rectangular/hex grids can coexist in one world but do not form voxel-neighbor bridges until an explicit mixed-topology mapping is designed.
- Boundary cache invalidation now flows through topology-provided boundary ranges.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --filter "GetHexNeighbors|TryGetHexNeighbor|HexDirectionUtility|RectangularDirectionUtility"
dotnet test GridForge.slnx --configuration Debug --filter "HexPrismGrid|Voxel|VoxelGrid"
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
git diff --check
```

Results: focused direction subset and hex-neighbor tests passed 6/6, and the broader `HexPrismGrid|Voxel|VoxelGrid` slice passed 150/150.
Full `Debug` tests passed 269/269, full `ReleaseLean` tests passed 271/271, and `git diff --check` reported no whitespace errors.

## Phase 4: Topology-Aware Coverage, Tracing, Blockers, And Scans

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

- [x] Move covered-voxel enumeration behind topology-aware methods.
- [x] Move covered-scan-cell enumeration behind topology-aware methods.
- [x] Implement hex line tracing through axial/cube interpolation and deterministic rounding.
- [x] Implement conservative hex bounds coverage for world-space bounds.
- [x] Ensure blockers apply to all covered hex-prism voxels and remove by token correctly.
- [x] Ensure cached blocker removal via `WorldVoxelIndex` works for hex grids.
- [x] Ensure occupant registration, deregistration, ticket lookup, and radius scan work on hex grids.
- [x] Ensure radius scans still apply exact fixed-point distance checks after scan-cell candidate enumeration.
- [x] Add tests for line traces across flat-top and pointy-top hex grids.
- [x] Add tests for blockers spanning rectangular and hex grids in the same world.

Implementation notes:

- `GridTracer` keeps rectangular coverage on the existing index-range path and routes hex-prism coverage through axial candidate ranges.
- Hex bounds coverage is conservative: the broad phase expands X/Z by the hex cell radius, projects the candidate AABB corners into axial space, and filters covered voxels by horizontal cell-radius reach. Scan-cell coverage uses the same candidate range and leaves occupant radius checks to the existing exact fixed-point narrow phase.
- Hex line tracing now samples deterministic axial/cube interpolation between snapped grid endpoints and preserves the existing `includeEnd` behavior by leaving terminal voxel insertion to the shared endpoint path.
- Blockers and scans continue to call tracer APIs without caller-side topology branching. Cached blocker removal still relies on `WorldVoxelIndex`.

Exit criteria:

- [x] `GridTracer`, blockers, occupants, and scans do not require caller-side topology branching.
- [x] Hex coverage is deterministic, documented, and benchmark-ready.
- [x] Rectangular coverage remains unchanged.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
```

Results: focused Phase 4 coverage passed 7/7, including flat-top and pointy-top
hex line traces, conservative hex voxel/scan-cell bounds coverage, cached hex
blocker removal, mixed rectangular/hex blocker coverage, and hex occupant
ticket/radius scan behavior. Broader `GridTracer|Blocker|ManagerCoverage|ScanCell|HexPrismGrid`
coverage passed 123/123. Full `Debug` build passed with 0 warnings and 0
errors, full `Debug` tests passed 276/276, `ReleaseLean` build passed with 0
warnings and 0 errors, `ReleaseLean` tests passed 278/278, and
`git diff --check` reported no whitespace errors.

## Phase 5: Performance Hardening And Benchmarks

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

- [x] Add BenchmarkDotNet benchmarks for topology conversion hot paths.
- [x] Compare rectangular baseline before and after topology extraction.
- [x] Compare flat-top and pointy-top projection cost.
- [x] Validate no per-query allocations in hot topology conversion paths.
- [x] Optimize hex rounding and coverage only after benchmark evidence.
- [x] Decide whether lookup tables or cached per-grid constants are justified.

Implementation notes:

- Added `HexPrismTopologyBenchmarks` with the `hex-prism-topology` benchmark alias covering rectangular baseline lookup, pointy/flat hex lookup, projection, construction, line tracing, bounds coverage, blockers, occupant registration, caller-owned radius scans, and mixed rectangular/hex world lookup.
- The first benchmark pass showed hex world-to-index lookup around 31 ms per 65,536 lookup batch versus roughly 4.9 ms for the rectangular baseline. The hot cost was repeated axial max-index derivation inside topology lookup.
- `VoxelGrid` now passes its cached dimensions through the internal topology contract for bounds checks, world-to-index lookup, and floor/ceil snapping. Hex topology uses those dimensions directly instead of recomputing max axial bounds in per-query lookup and trace endpoint snapping.
- `GridTracer` hoists repeated hex coverage constants out of inner loops, passes cached topology metrics into axial corner projection, and marks small hex trace helpers for aggressive inlining.
- `HexCoordinateUtility.RoundCube` is marked for aggressive inlining because it is in the lookup and trace rounding path.
- No lookup tables or extra per-grid caches were added. The dimension pass-through removed the measured repeated work without introducing cache invalidation, sparse mutation, or pooling lifetime risks.
- The full short-run benchmark now shows pointy/flat hex world-to-index lookup at roughly 16.9 ms and 17.8 ms per 65,536 lookup batch, with the rectangular baseline at roughly 5.0 ms. Lookup/projection benchmarks report only fixed benchmark overhead allocations shared with the rectangular baseline.
- Hex bounds coverage remains conservative and scales with candidate region size: small/medium/large coverage measured roughly 0.78 ms, 5.6 ms, and 20.1 ms in the short-run suite.
- BenchmarkDotNet emitted `MinIterationTime` warnings because the repository's in-process short-run config intentionally favors quick smoke feedback. Treat these numbers as relative phase evidence, not a release-grade performance report.

Exit criteria:

- [x] Rectangular hot paths are not materially regressed.
- [x] Hex lookup and coverage performance is documented with realistic tradeoffs.
- [x] Any added caches are deterministic, reset-safe, and pooling-safe.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- hex-prism-topology --list flat
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*Hex*'
git diff --check
```

Results: `Debug` build passed with 0 warnings and 0 errors, focused
topology/tracing coverage passed 135/135 after internal dimension pass-through
hardening, and full `Debug` tests passed 276/276. `ReleaseLean` build passed
with 0 warnings and 0 errors, and `ReleaseLean` tests passed 278/278. Benchmark
catalog validation exposed the `hex-prism-topology` alias and all 16 benchmark
methods. Full benchmark smoke coverage executed all 16 hex-prism benchmark
methods and refreshed
`BenchmarkDotNet.Artifacts/results/GridForge.Benchmarks.HexPrismTopologyBenchmarks-report-github.md`
with the Phase 5 numbers above.

## Phase 6: Documentation And Release Alignment

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

- [x] Explain topology as rectangular-prism or hex-prism cells owned by the same `VoxelGrid` public model.
- [x] Explain that hex grids use axial `q/r` coordinates through `VoxelIndex.x/z` and vertical layer through `VoxelIndex.y`.
- [x] Explain flat-top versus pointy-top orientation without implying engine-specific rendering behavior.
- [x] Document that `GridWorld` can own mixed topology grids.
- [x] Document mixed-topology neighbor limitations for the first release.
- [x] Document blocker and trace behavior over hex grids.
- [x] Document deterministic fixed-point constraints and the GridForge-local `Sqrt3` constant.
- [x] Update XML docs for topology configuration, orientation, metrics, and topology-local `VoxelIndex` meaning.
- [x] Run wiki link rewrite tests if docs links change.

Implementation notes:

- Updated README and wiki pages to explain per-grid rectangular-prism and hex-prism topology, axial `(q, layer, r)` index semantics, pointy-top versus flat-top projection, mixed rectangular/hex worlds, and same-topology neighbor bridging limits.
- Added hex examples to Getting Started and Common Workflows without introducing an engine-specific rendering assumption.
- Updated tracing, blocker, scan, occupant, sparse, determinism, and benchmark docs so coverage, blocker, and scan behavior tell the same topology story.
- Updated XML docs for `GridConfiguration`, `GridTopologyMetrics`, `VoxelIndex`, `HexDirection`, and `VoxelGrid` topology-local lookup APIs.
- Updated package metadata to mention rectangular and hex-prism support and added hex package tags.
- Extracted intentionally deferred work into `2026-06-13-hex-prism-follow-up-plan.md`: mixed-topology voxel-neighbor bridging, sparse hex-prism validation, and the later FixedMathSharp `Sqrt3` ownership revisit.

Exit criteria:

- [x] README, wiki, XML docs, tests, benchmarks, and package metadata tell the same topology story.
- [x] Users can choose rectangular or hex-prism grids without needing engine-specific assumptions.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py
git diff --check
```

Results: `Debug` build passed with 0 warnings and 0 errors, full `Debug`
tests passed 276/276, `ReleaseLean` build passed with 0 warnings and 0 errors,
and full `ReleaseLean` tests passed 278/278. Benchmark catalog validation
showed the `hex-prism-topology` alias. Wiki link rewrite tests passed 4/4.

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
| Floating-point constants sneak into hex math | Add a GridForge-local raw-backed `Sqrt3` and reject runtime `double` conversions in topology code. |
| Rectangular directions cannot represent hex neighbors cleanly | Use separate `RectangularDirection` and `HexDirection` public APIs, with compact topology-local slots kept internal. |
| Bounds coverage over hex cells is too slow | Start conservative and correct, then benchmark coverage algorithms before optimizing. |
| Mixed topology neighbor bridging becomes ambiguous | Permit mixed topology coexistence first; defer cross-topology voxel-neighbor bridging until a concrete mapping is designed. |
| World-level cell-size APIs leak rectangular topology | Move cell geometry to per-grid topology metrics and replace `VoxelSize`/`*VoxelSize` public API names during the breaking topology cleanup. |
| `VoxelIndex` meaning becomes unclear | Document topology-local coordinate meaning in XML docs and wiki, and keep exact tests for both topologies. |
| ReleaseLean breaks through new dependency usage | Keep MemoryPack and package-variant guards consistent with existing project patterns. |

## Recommended Implementation Order

1. Finish Phase 0 decisions in this document.
2. Extract rectangular topology and prove behavior did not move.
3. Add hex-prism construction and lookup for both orientations, including the GridForge-local raw-backed `Sqrt3`.
4. Add topology-aware neighbor behavior.
5. Add topology-aware tracing, coverage, blockers, occupants, and scans.
6. Run benchmarks and optimize only where evidence points.
7. Complete docs and release alignment.
