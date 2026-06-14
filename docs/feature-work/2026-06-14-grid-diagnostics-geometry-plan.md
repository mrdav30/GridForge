# Grid Diagnostics Geometry Battle Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` to implement this plan task-by-task. Use
> `superpowers:test-driven-development` for runtime behavior changes and
> `superpowers:verification-before-completion` before claiming a phase is
> complete. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Active

**Goal:** Add an engine-agnostic diagnostic middle layer that lets tools inspect
and render dense or sparse rectangular-prism and hex-prism grids without putting
rendering, Unity, colors, cameras, materials, or mesh ownership in GridForge.

**Architecture:** Keep diagnostics as a read-only projection over the existing
`GridWorld`, `VoxelGrid`, `Voxel`, storage, topology, and event model. The core
API should emit compact cell descriptors, topology-aware geometry, optional
missing sparse address-space cells, and dirty change data that adapters can turn
into Gizmos, meshes, logs, overlays, or tests. Physical voxels are the default;
missing sparse address cells are first-class but opt-in, bounded, and never
materialized as `Voxel` instances.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`,
`SwiftCollections`, xUnit v3, BenchmarkDotNet, standard and `ReleaseLean`
package variants.

---

## Status

- Started: 2026-06-14.
- Release posture: additive public API under a new diagnostics namespace.
- Backwards compatibility: no rendering API, no Unity dependency, no behavior
  changes to existing grid queries.
- Current state: planning complete; implementation not started.
- Related completed work: sparse storage, hex-prism topology, mixed-topology
  contact helpers, `TopologyVoxelAabb`, and `TopologyVoxelRangeUtility`.

## Problem

The current Unity-side debugger is a useful prototype, but it is not the shape
GridForge should promote into core:

- it resolves one grid and loops `Width * Height * Length`
- it asks `TryGetVoxel(x, y, z)` inside that dense-style loop
- it draws cubes only
- it owns renderer choices that belong in `GridForge-Unity`

That model breaks down for sparse address spaces and for hex-prism grids. The
core library needs to answer a different question:

```text
Given a world, query, and diagnostic filter, what cells and geometry facts can
an adapter consume without knowing GridForge's storage or topology internals?
```

## Locked Decisions

- Diagnostics are a middle layer, not a renderer.
- Public diagnostic APIs live in GridForge core, but rendering adapters live
  outside the core package.
- Physical cells are the default diagnostic output.
- Missing sparse address-space cells are supported through explicit modes:
  `PhysicalAndMissing` and `MissingOnly`.
- Missing sparse address cells are descriptors only. They do not allocate,
  configure, return, or pretend to be real `Voxel` instances.
- Sparse-hole enumeration must be bounded by a query range, layer slice,
  explicit maximum cell budget, or an explicit full-address-space opt-in.
- Diagnostics should iterate storage-neutral physical voxels through
  `VoxelGrid.EnumerateVoxels()`, not by assuming dense arrays.
- Geometry is topology-aware. Rectangular-prism and hex-prism cells share the
  same public diagnostic flow but emit different prism vertices and edge sets.
- Diagnostic output uses `Fixed64`, `Vector3d`, `VoxelIndex`,
  `WorldVoxelIndex`, `GridTopologyKind`, `GridTopologyMetrics`, and
  `GridStorageKind`; no `float`, `double`, `System.Numerics`, Unity, or engine
  types enter the core API.
- Incremental renderer support should observe existing grid, obstacle, and
  occupant events. Do not rely only on `VoxelGrid.Version` for occupancy
  changes because occupant add/remove does not currently raise
  `GridWorld.OnActiveGridChange`.
- Query and geometry APIs must be allocation-conscious, caller-owned-buffer
  friendly, and deterministic in output order.

## Non-Goals

- Do not add Gizmo, mesh, line renderer, material, camera, editor, or color APIs
  to GridForge.
- Do not migrate the Unity `GridDebugger` into this repository.
- Do not add hierarchy, streaming, sectors, save-game state, pathfinding, or
  authoring serialization.
- Do not expose internal dense arrays as the diagnostic surface.
- Do not materialize missing sparse voxels during diagnostic reads.
- Do not add compatibility wrappers that preserve dense-only debugger
  assumptions.
- Do not make diagnostic sessions mutate grid state.

## Target Mental Model

```text
GridWorld
  -> GridDiagnosticQuery
      -> storage-neutral physical cell descriptors
      -> optional sparse address-space hole descriptors
      -> topology-aware geometry descriptors
      -> optional dirty-cell/session notifications
  -> adapter-owned rendering, logging, overlay, or test output
```

Adapters should be able to render:

- dense rectangular voxels
- sparse rectangular configured voxels
- sparse rectangular missing address cells
- dense hex-prism cells
- sparse hex-prism configured cells
- sparse hex-prism missing address cells
- occupied, blocked, empty, boundary, and selected-state overlays

## Proposed Public Surface

Create a new namespace and folder:

- Create: `src/GridForge/Diagnostics`
- Test: `tests/GridForge.Tests/Diagnostics`
- Benchmark: `tests/GridForge.Benchmarks/Memory/GridDiagnosticsBenchmarks.cs`

### Public Enums

```csharp
namespace GridForge.Diagnostics;

public enum GridDiagnosticAddressMode
{
    PhysicalOnly = 0,
    PhysicalAndMissing = 1,
    MissingOnly = 2
}

public enum GridDiagnosticCellKind
{
    Physical = 0,
    MissingSparseAddress = 1
}

[Flags]
public enum GridDiagnosticCellState : byte
{
    None = 0,
    Empty = 1,
    Occupied = 2,
    Blocked = 4,
    Boundary = 8,
    Partitioned = 16,
    MissingSparseAddress = 32
}

public enum GridDiagnosticQueryStatus
{
    Completed = 0,
    InactiveWorld = 1,
    InvalidGrid = 2,
    MissingAddressSpaceRequiresBounds = 3,
    MaxCellsExceeded = 4,
    Truncated = 5
}
```

### Public Data Contracts

```csharp
namespace GridForge.Diagnostics;

public readonly struct GridDiagnosticQuery
{
    public const int DefaultMaxCells = 65536;

    public readonly ushort? GridIndex;
    public readonly GridTopologyKind? TopologyKind;
    public readonly GridStorageKind? StorageKind;
    public readonly GridDiagnosticAddressMode AddressMode;
    public readonly GridDiagnosticCellState RequiredStates;
    public readonly GridDiagnosticCellState ExcludedStates;
    public readonly Vector3d? BoundsMin;
    public readonly Vector3d? BoundsMax;
    public readonly int MaxCells;
    public readonly bool AllowFullAddressSpaceScan;
}

public readonly struct GridDiagnosticCell
{
    public readonly GridDiagnosticCellKind Kind;
    public readonly int WorldSpawnToken;
    public readonly ushort GridIndex;
    public readonly int GridSpawnToken;
    public readonly VoxelIndex Index;
    public readonly Vector3d WorldPosition;
    public readonly GridTopologyKind TopologyKind;
    public readonly GridStorageKind StorageKind;
    public readonly GridTopologyMetrics TopologyMetrics;
    public readonly GridDiagnosticCellState State;
    public readonly WorldVoxelIndex WorldIndex;
}

public readonly struct GridDiagnosticQueryResult
{
    public readonly GridDiagnosticQueryStatus Status;
    public readonly int CellCount;
    public readonly int SkippedCellCount;
}

public readonly struct GridDiagnosticEdge
{
    public readonly byte Start;
    public readonly byte End;
}
```

Rules:

- `GridDiagnosticCell.WorldIndex` is always composed from the active world,
  grid slot, grid spawn token, and local index. For `Physical` cells it resolves
  to a real voxel while the grid remains active. For `MissingSparseAddress`
  cells it is a potential identity only and `GridWorld.TryGetVoxel(...)` should
  return false.
- `GridDiagnosticCell.State` for missing address cells includes
  `MissingSparseAddress` and no occupied, blocked, or partitioned flags.
- `GridDiagnosticQuery.MaxCells` defaults to
  `GridDiagnosticQuery.DefaultMaxCells`, currently `65536`, so tooling can opt
  into a higher budget deliberately.
- Bounds filters are world-space and clip to grid bounds through existing
  topology utilities.

### Public Query APIs

```csharp
namespace GridForge.Diagnostics;

public static class GridDiagnostics
{
    public static GridDiagnosticQueryResult GetCellsInto(
        GridWorld world,
        in GridDiagnosticQuery query,
        SwiftList<GridDiagnosticCell> results,
        GridDiagnosticScratch? scratch = null);

    public static GridDiagnosticQueryResult VisitCells<TVisitor>(
        GridWorld world,
        in GridDiagnosticQuery query,
        ref TVisitor visitor,
        GridDiagnosticScratch? scratch = null)
        where TVisitor : struct, IGridDiagnosticCellVisitor;

    public static bool TryResolvePhysicalCell(
        GridWorld world,
        in GridDiagnosticCell cell,
        out VoxelGrid? grid,
        out Voxel? voxel);
}

public interface IGridDiagnosticCellVisitor
{
    bool Visit(in GridDiagnosticCell cell);
}

public sealed class GridDiagnosticScratch
{
    public void Clear();
}
```

Rules:

- `GetCellsInto` clears and fills caller-owned storage.
- `VisitCells` is the preferred hot path for render adapters that write
  directly into their own buffers.
- Returning `false` from `Visit(...)` stops traversal and returns
  `GridDiagnosticQueryStatus.Truncated`.
- `GridDiagnosticScratch` owns reusable temporary collections needed for sorted
  candidate grids, range traversal, and sparse-hole de-duplication.
- The implementation must not allocate per cell.

### Public Geometry APIs

```csharp
namespace GridForge.Diagnostics;

public static class GridDiagnosticGeometry
{
    public const int RectangularPrismVertexCount = 8;
    public const int HexPrismVertexCount = 12;
    public const int RectangularPrismEdgeCount = 12;
    public const int HexPrismEdgeCount = 18;

    public static int GetVertexCount(GridTopologyKind topologyKind);

    public static int GetEdgeCount(GridTopologyKind topologyKind);

    public static int WriteVertices(
        in GridDiagnosticCell cell,
        Span<Vector3d> vertices);

    public static ReadOnlySpan<GridDiagnosticEdge> GetEdges(
        GridTopologyKind topologyKind);
}
```

Rules:

- Rectangular vertices describe the cell prism corners using rectangular
  metrics.
- Hex vertices describe bottom and top hex-ring corners using hex radius,
  layer height, and orientation.
- Edge spans are static, immutable, and allocation-free.
- Geometry is expressed in world coordinates and fixed-point `Vector3d`.
- Rendering adapters own conversion to engine coordinates, floats, colors,
  vertex buffers, line batches, or meshes.

## Phase 0: Lock Semantics And File Boundaries

Intent: make the diagnostic contract unambiguous before adding APIs.

Files:

- Modify: `docs/feature-work/2026-06-14-grid-diagnostics-geometry-plan.md`
- Read: `docs/wiki/Diagnostics-and-Logging.md`
- Read: `docs/wiki/VoxelGrid-and-Voxel-Model.md`
- Read: `docs/wiki/Sparse-Grid-Storage.md`
- Read: `docs/wiki/GridTracer-and-Coverage.md`
- Read: `src/GridForge/Grids/VoxelGrid.cs`
- Read: `src/GridForge/Grids/Topology/TopologyVoxelAabb.cs`
- Read: `src/GridForge/Grids/Topology/TopologyVoxelRangeUtility.cs`

Checklist:

- [ ] Confirm public diagnostics live under `GridForge.Diagnostics`.
- [ ] Confirm diagnostics emit descriptors and topology geometry, never draw
  commands.
- [ ] Confirm missing sparse address cells are opt-in and bounded.
- [ ] Confirm missing sparse address cells are not `Voxel` instances.
- [ ] Confirm physical traversal uses `VoxelGrid.EnumerateVoxels()`.
- [ ] Confirm bounded address traversal uses `TopologyVoxelRangeUtility`.
- [ ] Confirm dirty tracking listens to occupant events directly rather than
  depending only on grid version changes.

Exit criteria:

- [ ] This plan contains the public API names, non-goals, and sparse-hole
  semantics needed for implementation.

## Phase 1: Public Contracts And Query Skeleton

Intent: add the diagnostics namespace, contracts, scratch type, and no-op query
entry points behind tests before implementing traversal.

Files:

- Create: `src/GridForge/Diagnostics/GridDiagnosticAddressMode.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticCellKind.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticCellState.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticQueryStatus.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticQuery.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticCell.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticQueryResult.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticScratch.cs`
- Create: `src/GridForge/Diagnostics/IGridDiagnosticCellVisitor.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnostics.cs`
- Create: `tests/GridForge.Tests/Diagnostics/GridDiagnosticsContractTests.cs`

Checklist:

- [ ] Add public enums with XML documentation.
- [ ] Add readonly public data contracts with XML documentation.
- [ ] Add `GridDiagnosticQuery.DefaultMaxCells` with value `65536`.
- [ ] Add factory helpers for common queries:
  `GridDiagnosticQuery.AllPhysical()`,
  `GridDiagnosticQuery.ForGrid(ushort gridIndex)`, and
  `GridDiagnosticQuery.ForBounds(Vector3d min, Vector3d max)`.
- [ ] Add `GridDiagnostics.GetCellsInto(...)` with null checks, result clearing,
  inactive-world handling, and a no-cell completed result.
- [ ] Add `GridDiagnostics.VisitCells(...)` with inactive-world handling and
  visitor early-exit behavior.
- [ ] Add `GridDiagnostics.TryResolvePhysicalCell(...)` and ensure it returns
  false for missing address cells and stale world/grid tokens.
- [ ] Test null result storage throws the same style of argument exception used
  by existing caller-owned result APIs.
- [ ] Test inactive worlds return `InactiveWorld` without writing cells.
- [ ] Test `TryResolvePhysicalCell(...)` resolves physical descriptors and
  rejects missing descriptors.

Exit criteria:

- [ ] Contracts compile on `netstandard2.1` and `net8.0`.
- [ ] No traversal or geometry behavior is implemented beyond safe no-op
  skeletons.

Validation:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --filter "FullyQualifiedName~GridDiagnosticsContractTests"
```

## Phase 2: Topology-Aware Geometry

Intent: expose deterministic prism vertices and edge topology that render
adapters can consume without knowing GridForge topology internals.

Files:

- Create: `src/GridForge/Diagnostics/GridDiagnosticEdge.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticGeometry.cs`
- Create: `tests/GridForge.Tests/Diagnostics/GridDiagnosticGeometryTests.cs`
- Read: `src/GridForge/Spatial/HexCoordinateUtility.cs`
- Read: `src/GridForge/Grids/Topology/TopologyVoxelAabb.cs`
- Read: `tests/GridForge.Tests/Grids/MixedTopologyNeighborTests.cs`
- Read: `tests/GridForge.Tests/Spatial/HexCoordinateUtility.Tests.cs`

Checklist:

- [ ] Add immutable edge spans for rectangular prisms and hex prisms.
- [ ] Implement rectangular vertex writing from cell center and rectangular
  metrics.
- [ ] Implement pointy-top hex vertex writing from cell center, hex radius,
  layer height, and the existing deterministic `Sqrt3` constant.
- [ ] Implement flat-top hex vertex writing from cell center, hex radius,
  layer height, and the existing deterministic `Sqrt3` constant.
- [ ] Return `0` when the provided vertex span is too small.
- [ ] Return expected vertex and edge counts for each topology.
- [ ] Test rectangular vertices for non-cubic metrics.
- [ ] Test pointy-top and flat-top hex vertices for deterministic orientation.
- [ ] Test edge spans are read-only and stable across calls.
- [ ] Test missing sparse address cells use the same geometry path as physical
  cells with the same topology metrics and index.

Exit criteria:

- [ ] Adapters can draw wireframes or meshes for rectangular and hex-prism cells
  using only public diagnostic descriptors.

Validation:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --filter "FullyQualifiedName~GridDiagnosticGeometryTests"
```

## Phase 3: Physical Cell Query Traversal

Intent: fill diagnostic queries with real physical cells for dense and sparse
grids without depending on storage layout.

Files:

- Modify: `src/GridForge/Diagnostics/GridDiagnostics.cs`
- Modify: `src/GridForge/Diagnostics/GridDiagnosticScratch.cs`
- Create: `tests/GridForge.Tests/Diagnostics/GridDiagnosticsPhysicalQueryTests.cs`
- Read: `src/GridForge/Grids/VoxelGrid.cs`
- Read: `src/GridForge/Grids/Managers/GridWorld.cs`
- Read: `src/GridForge/Grids/Storage/DenseVoxelGridStorage.cs`
- Read: `src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs`

Checklist:

- [ ] Traverse active grids in deterministic `GridWorld.ActiveGrids` order.
- [ ] Apply optional grid index, topology, storage, and bounds filters.
- [ ] Use `VoxelGrid.EnumerateVoxels()` for physical cell traversal.
- [ ] Build `GridDiagnosticCell` descriptors from each physical voxel without
  allocating.
- [ ] Derive cell state from `Voxel.IsOccupied`, `Voxel.IsBlocked`,
  `Voxel.IsBoundaryVoxel`, and `Voxel.IsPartioned`.
- [ ] Treat unoccupied and unblocked physical cells as `Empty`.
- [ ] Apply required-state and excluded-state filters after state derivation.
- [ ] Stop at `MaxCells` and report `MaxCellsExceeded` when the query budget is
  reached before traversal completes.
- [ ] Preserve deterministic output order for dense rectangular, sparse
  rectangular, dense hex, and sparse hex grids.
- [ ] Test physical-only dense rectangular output count equals `Size`.
- [ ] Test physical-only sparse rectangular output count equals
  `ConfiguredVoxelCount`.
- [ ] Test physical-only dense and sparse hex output includes axial indices and
  hex topology metadata.
- [ ] Test occupied, blocked, empty, boundary, topology, storage, and grid-index
  filters.
- [ ] Test bounds filtering clips through topology-aware candidate ranges and
  returns only cells whose diagnostic AABB overlaps the query bounds.

Exit criteria:

- [ ] Diagnostic queries replace the Unity prototype's dense nested loop for
  physical cells.
- [ ] Sparse physical traversal scales with configured voxels, not address-space
  size.

Validation:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --filter "FullyQualifiedName~GridDiagnosticsPhysicalQueryTests"
```

## Phase 4: Missing Sparse Address-Cell Query Modes

Intent: make sparse holes visible for tools while preserving sparse performance
semantics by default.

Files:

- Modify: `src/GridForge/Diagnostics/GridDiagnostics.cs`
- Modify: `src/GridForge/Diagnostics/GridDiagnosticQuery.cs`
- Modify: `src/GridForge/Diagnostics/GridDiagnosticQueryResult.cs`
- Create: `tests/GridForge.Tests/Diagnostics/GridDiagnosticsSparseAddressTests.cs`
- Read: `src/GridForge/Grids/Topology/TopologyVoxelRangeUtility.cs`
- Read: `src/GridForge/Grids/VoxelGrid.cs`
- Read: `docs/wiki/Sparse-Grid-Storage.md`

Checklist:

- [ ] Keep `PhysicalOnly` as the default address mode.
- [ ] Implement `PhysicalAndMissing` for sparse grids by emitting configured
  physical cells and missing address cells inside the bounded query range.
- [ ] Implement `MissingOnly` for sparse grids by emitting only unconfigured
  address cells inside the bounded query range.
- [ ] Emit no missing cells for dense grids.
- [ ] Require bounds or `AllowFullAddressSpaceScan` before scanning a sparse
  grid's full address space for holes.
- [ ] Return `MissingAddressSpaceRequiresBounds` when a missing-cell query would
  otherwise scan a full sparse address space without opt-in.
- [ ] Use `TopologyVoxelRangeUtility.TryGetCandidateRange(...)` to get
  rectangular and hex candidate ranges.
- [ ] Use `VoxelGrid.ContainsVoxel(...)` to distinguish configured physical
  cells from missing sparse address cells.
- [ ] Apply `MaxCells` to the combined physical plus missing output.
- [ ] Report skipped cells when traversal stops at the max-cell budget.
- [ ] Test `PhysicalAndMissing` includes configured and missing rectangular
  cells within bounds.
- [ ] Test `MissingOnly` excludes configured rectangular cells.
- [ ] Test sparse hex missing address cells use axial `VoxelIndex(q, y, r)`
  coordinates.
- [ ] Test unbounded missing-cell queries require explicit opt-in.
- [ ] Test full address-space opt-in respects `MaxCells`.
- [ ] Test missing address descriptors do not resolve to physical voxels.

Exit criteria:

- [ ] Tools can render "where voxels could be" for sparse rectangular and hex
  grids without changing runtime sparse semantics.

Validation:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --filter "FullyQualifiedName~GridDiagnosticsSparseAddressTests"
```

## Phase 5: Diagnostic Session And Dirty Tracking

Intent: support incremental adapters that rebuild only changed diagnostic cells
or grids.

Files:

- Create: `src/GridForge/Diagnostics/GridDiagnosticChangeKind.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticChange.cs`
- Create: `src/GridForge/Diagnostics/GridDiagnosticSession.cs`
- Create: `tests/GridForge.Tests/Diagnostics/GridDiagnosticSessionTests.cs`
- Read: `src/GridForge/Grids/Managers/GridWorld.cs`
- Read: `src/GridForge/Grids/Managers/GridObstacleManager.cs`
- Read: `src/GridForge/Grids/Managers/GridOccupantManager.cs`
- Read: `src/GridForge/Grids/Support/GridEventInfo.cs`
- Read: `src/GridForge/Grids/Support/ObstacleEventInfo.cs`
- Read: `src/GridForge/Grids/Support/OccupantEventInfo.cs`

Checklist:

- [ ] Add a disposable session bound to one active `GridWorld`.
- [ ] Subscribe to `GridWorld.OnActiveGridAdded`,
  `GridWorld.OnActiveGridRemoved`, `GridWorld.OnActiveGridChange`, and
  `GridWorld.OnReset`.
- [ ] Subscribe to `GridObstacleManager` static obstacle events and filter by
  the session world's spawn token.
- [ ] Subscribe to `GridOccupantManager` static occupant events and filter by
  the session world's spawn token.
- [ ] Record dirty grid ids for grid add/remove/reset and broad grid-change
  events.
- [ ] Record dirty physical cell identities for obstacle and occupant events.
- [ ] Record sparse voxel add/remove changes from `GridEventKind.SparseVoxelAdded`
  and `GridEventKind.SparseVoxelRemoved`.
- [ ] Expose caller-owned `GetDirtyChangesInto(...)` and `ClearDirtyChanges()`
  APIs.
- [ ] Coalesce duplicate dirty cells deterministically.
- [ ] Release event subscriptions on dispose.
- [ ] Test obstacle changes are reported through the session.
- [ ] Test occupant changes are reported even though grid version does not
  currently change for occupancy.
- [ ] Test sparse add/remove reports both the physical cell and surrounding
  address-space range as dirty for hole-rendering adapters.
- [ ] Test events from other worlds are ignored.
- [ ] Test disposing the session prevents further dirty captures.

Exit criteria:

- [ ] Unity and other adapters can avoid whole-grid rebuilds when only occupied,
  blocked, or sparse mutation state changes.

Validation:

```bash
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj --configuration Debug --filter "FullyQualifiedName~GridDiagnosticSessionTests"
```

## Phase 6: Performance Hardening And Benchmarks

Intent: prove diagnostic queries are low allocation and scale by the selected
mode rather than accidentally becoming dense scans.

Files:

- Create: `tests/GridForge.Benchmarks/Memory/GridDiagnosticsBenchmarks.cs`
- Modify: `tests/GridForge.Benchmarks/Program.cs`
- Modify: `docs/wiki/Testing-and-Benchmarking.md`
- Read: `tests/GridForge.Benchmarks/Memory/SparseVoxelGridBenchmarks.cs`
- Read: `tests/GridForge.Benchmarks/Memory/HexPrismTopologyBenchmarks.cs`

Checklist:

- [ ] Add benchmark scenarios for dense rectangular physical query.
- [ ] Add benchmark scenarios for sparse rectangular physical query.
- [ ] Add benchmark scenarios for dense hex physical query.
- [ ] Add benchmark scenarios for sparse hex physical query.
- [ ] Add benchmark scenarios for bounded sparse missing-cell query.
- [ ] Add benchmark scenarios comparing `GetCellsInto` and `VisitCells`.
- [ ] Add a benchmark alias named `grid-diagnostics`.
- [ ] Confirm physical sparse query cost follows configured voxel count.
- [ ] Confirm missing-cell query cost follows bounded candidate range and
  `MaxCells`.
- [ ] Confirm warm-path query allocation is zero or limited to caller-owned
  buffer growth outside the measured core operation.
- [ ] If a benchmark exposes a real hotspot, fix the query structure before
  adding cache complexity.

Exit criteria:

- [ ] Benchmark evidence supports the public performance guidance.

Validation:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- grid-diagnostics --filter '*GridDiagnosticsBenchmarks*'
```

## Phase 7: Documentation And Adapter Handoff

Intent: make the feature understandable for core users and straightforward for
the later `GridForge-Unity` migration.

Files:

- Modify: `README.md`
- Modify: `docs/wiki/Home.md`
- Modify: `docs/wiki/Diagnostics-and-Logging.md`
- Create: `docs/wiki/Grid-Diagnostics-and-Geometry.md`
- Modify: `docs/wiki/VoxelGrid-and-Voxel-Model.md`
- Modify: `docs/wiki/Sparse-Grid-Storage.md`
- Modify: `docs/wiki/Testing-and-Benchmarking.md`
- Modify: `src/GridForge/GridForge.csproj` to add the `diagnostics` package tag.
- Reference for Unity migration:
  `/mnt/f/gamedevrepos/GridForge-Unity/Assets/Packages/Build/Base/Runtime/Utility/Debugging/GridDebugger.cs`

Checklist:

- [ ] Document the difference between logging diagnostics and geometry
  diagnostics.
- [ ] Document `PhysicalOnly`, `PhysicalAndMissing`, and `MissingOnly`.
- [ ] Document that missing sparse address cells are not runtime voxels.
- [ ] Document bounds and max-cell budget requirements for sparse-hole views.
- [ ] Document rectangular and hex geometry output shape.
- [ ] Document how adapters should convert `Vector3d`/`Fixed64` to engine
  floats at the adapter boundary.
- [ ] Add a small example that fills a caller-owned `SwiftList<GridDiagnosticCell>`.
- [ ] Add a small example that writes diagnostic vertices for one cell.
- [ ] Add a Unity migration note that the old dense `Width * Height * Length`
  loop should move to `GridDiagnostics.VisitCells(...)`.
- [ ] Link the new wiki page from `Home`, `Diagnostics-and-Logging`, and
  `Testing-and-Benchmarking`.
- [ ] Keep README concise and point deep details to the wiki.

Exit criteria:

- [ ] Core users can discover the API and understand sparse-hole costs.
- [ ] Unity migration has a clear adapter-oriented path without pulling
  rendering into GridForge.

## Validation Baseline

Run focused tests after each phase. Before considering the whole plan complete,
run:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
dotnet build GridForge.slnx --configuration ReleaseLean
dotnet test GridForge.slnx --configuration ReleaseLean --no-build
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- grid-diagnostics --filter '*GridDiagnosticsBenchmarks*'
git diff --check
```

## Risk Register

| Risk | Why It Matters | Mitigation |
| --- | --- | --- |
| Sparse-hole mode accidentally scans huge address spaces | Sparse grids exist to avoid dense costs | Require bounds, max-cell budgets, or explicit full-scan opt-in. |
| Diagnostics become renderer-shaped | Core must stay engine-agnostic | Emit descriptors, vertices, and edge topology only. |
| Missing cells look like real voxels | Sparse absence is intentional runtime behavior | Use `GridDiagnosticCellKind.MissingSparseAddress` and make physical resolution fail. |
| Occupant state does not dirty renderer caches | Occupant changes do not currently raise world grid-change events | Diagnostic sessions subscribe to occupant events directly. |
| Hex geometry drifts from topology projection | Incorrect adapter rendering undermines debugging | Reuse topology metrics and existing deterministic hex math; test both orientations. |
| Query APIs allocate per cell | Diagnostics can run in editor or runtime loops | Provide visitor and caller-owned list APIs plus reusable scratch. |
| Public API grows too broad | Debug tooling can become a second query framework | Keep the surface to cell descriptors, geometry, query filters, and dirty changes. |

## Completion Criteria

- [ ] Public diagnostics are documented, tested, and benchmarked.
- [ ] Dense and sparse rectangular grids are supported.
- [ ] Dense and sparse hex-prism grids are supported.
- [ ] Sparse missing address cells are available through explicit bounded modes.
- [ ] No renderer or engine-specific types are introduced into GridForge.
- [ ] Debug and ReleaseLean validation pass.
- [ ] The plan is moved to `docs/feature-work/done` with `Status: Done` after
  implementation, docs, benchmarks, and validation complete.
