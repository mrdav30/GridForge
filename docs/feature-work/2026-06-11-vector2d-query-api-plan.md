# Vector2d Query API Battle Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add first-class 2D-friendly query and mutation overloads while preserving GridForge's current 3D runtime model, deterministic math, and explicit `GridWorld` ownership.

**Architecture:** Treat 2D APIs as an ergonomic projection over the existing voxel model. `Vector2d` inputs represent XZ-plane coordinates, and a `Fixed64 layerY` value selects the vertical world layer. Internally, APIs lift 2D inputs to `Vector3d` or use layer-locked XZ comparisons without changing voxel identity, storage, blockers, occupants, or partitions.

**Tech Stack:** C# 11, `netstandard2.1`, `net8.0`, `FixedMathSharp`, `SwiftCollections`, xUnit v3, BenchmarkDotNet where scan hot paths change, optional MemoryPack package variant guards.

---

## Status

- Started: 2026-06-11
- Release posture: Mostly additive. The only likely correction is documentation or signature cleanup around the existing `GridTracer.TraceLine(Vector2d, Vector2d, ...)` overload.
- Backwards compatibility: Existing `Vector3d` workflows must remain equivalent. Existing `GridTracer.TraceLine(Vector2d, Vector2d, padding, includeEnd)` call sites should not silently change the meaning of positional arguments.
- Current state: Phase 0-3 complete; Phase 4 is next.

## Locked Decisions

- GridForge remains a 3D voxel runtime internally.
- 2D APIs are convenience overloads, not a separate 2D grid engine.
- `Vector2d.X` maps to world `Vector3d.X`.
- `Vector2d.Y` maps to world `Vector3d.Z`.
- `layerY` maps to world `Vector3d.Y` and defaults to `default(Fixed64)`, which is zero.
- 2D radius scans are layer-locked XZ scans. They must not behave like 3D sphere scans centered at the lifted position.
- 2D scan filtering must require the occupant to be on the same resolved Y layer before applying XZ squared-distance checks.
- `IVoxelOccupant.Position` remains `Vector3d` in the first release.
- Query APIs should feel seamless: a caller should not care whether they supplied `Vector2d` or `Vector3d` except for explicit 2D layer semantics.
- Keep the core library engine-agnostic. Unity integration belongs outside this repository.

## Non-Goals

- Do not add a parallel `VoxelGrid2D`, `GridWorld2D`, `Voxel2D`, or `IVoxelOccupant2D` model in the first release.
- Do not introduce arbitrary plane support such as XY, XZ, and YZ projections in the first release.
- Do not change `WorldVoxelIndex`, `VoxelIndex`, `Voxel.WorldPosition`, scan-cell identity, blocker tokens, or occupant tickets.
- Do not make `ScanRadius(Vector2d, ...)` include occupants from other Y layers that merely share a coarse scan cell.
- Do not add floating-point conversions in core runtime paths.
- Do not add compatibility wrappers that hide ambiguous axis semantics.

## Target Mental Model

Existing 3D API:

```text
Vector3d(x, y, z) -> GridWorld -> VoxelGrid -> Voxel
```

2D convenience API:

```text
Vector2d(x, zLikeY) + layerY
  -> Vector3d(x, layerY, zLikeY)
  -> GridWorld -> VoxelGrid -> Voxel
```

Layer-locked radius scan:

```text
Vector2d center + radius + layerY
  -> scan candidate cells intersecting the XZ radius bounds on that Y layer
  -> reject occupants outside the resolved Y layer
  -> apply XZ squared-distance check
  -> return matching IVoxelOccupant instances
```

## Architecture Direction

### Projection Boundary

Create one small projection helper so every 2D overload uses the same axis mapping.

Likely file:

- Create: `src/GridForge/Spatial/GridPlane2d.cs`

Responsibilities:

- lift `Vector2d` to `Vector3d` by mapping `(X, Y)` to `(X, Z)` and applying `layerY` to world `Y`
- project `Vector3d` to `Vector2d` by dropping world `Y`
- build XZ-aligned `Vector3d` min/max bounds for 2D coverage calls
- compute XZ squared distance without allocating or using floating-point math
- provide XML docs that explicitly call the second `Vector2d` component "world Z"

Candidate internal/public shape:

```csharp
public static class GridPlane2d
{
    public static Vector3d ToWorld(Vector2d position, Fixed64 layerY = default);

    public static Vector2d FromWorld(Vector3d position);

    public static (Vector3d min, Vector3d max) ToWorldBounds(
        Vector2d min,
        Vector2d max,
        Fixed64 layerY = default);

    internal static Fixed64 DistanceSquaredXZ(Vector3d a, Vector3d b);
}
```

The exact type name can change during Phase 0, but the project should not duplicate `new Vector3d(position.X, layerY, position.Y)` in many call sites.

### Overload Shape

Use `layerY = default` where it does not create overload ambiguity. Where an existing `Vector2d` overload already has optional parameters, preserve existing call-site meaning and add layer selection carefully.

Important existing case:

```csharp
GridTracer.TraceLine(
    GridWorld world,
    Vector2d start,
    Vector2d end,
    Fixed64? padding = null,
    bool includeEnd = true)
```

Do not add a second overload with the same first three arguments and a defaulted `Fixed64 layerY` before `padding`; `TraceLine(world, start, end)` and named `padding:` calls can become ambiguous.

Preferred correction:

```csharp
public static IEnumerable<GridVoxelSet> TraceLine(
    GridWorld world,
    Vector2d start,
    Vector2d end,
    Fixed64? padding = null,
    bool includeEnd = true,
    Fixed64 layerY = default)
```

This keeps existing positional `padding` and `includeEnd` calls intact while enabling named `layerY:` usage. XML docs must state that the default layer is world `Y = 0`.

For `out`-heavy methods, prefer paired overloads when that makes call sites cleaner:

```csharp
public bool TryGetVoxel(Vector2d position, out Voxel? result);

public bool TryGetVoxel(Vector2d position, Fixed64 layerY, out Voxel? result);
```

The default overload delegates to the explicit-layer overload.

### Layer-Locked Scan Semantics

The existing 3D scan path expands bounds on all axes and filters with full `Vector3d.MagnitudeSquared`.

The 2D path needs a separate internal append routine:

- candidate bounds expand X and Z by radius
- candidate bounds stay on the selected Y layer
- scan-cell candidate enumeration may still return coarse scan cells that span multiple Y voxel layers
- exact filtering must compare resolved voxel layer before applying XZ distance
- typed and untyped scan paths must share the same layer filter
- caller-owned result and scratch overloads must remain allocation-conscious

Do not implement the 2D scan by simply lifting the center to `Vector3d` and calling the existing 3D scan.

## Public API Direction

Candidate additions:

- `GridPlane2d.ToWorld(Vector2d position, Fixed64 layerY = default)`
- `GridPlane2d.FromWorld(Vector3d position)`
- `GridWorld.TryGetGrid(Vector2d position, out VoxelGrid? outGrid)`
- `GridWorld.TryGetGrid(Vector2d position, Fixed64 layerY, out VoxelGrid? outGrid)`
- `GridWorld.TryGetGridAndVoxel(Vector2d position, out VoxelGrid? outGrid, out Voxel? outVoxel)`
- `GridWorld.TryGetGridAndVoxel(Vector2d position, Fixed64 layerY, out VoxelGrid? outGrid, out Voxel? outVoxel)`
- `GridWorld.TryGetVoxel(Vector2d position, out Voxel? result)`
- `GridWorld.TryGetVoxel(Vector2d position, Fixed64 layerY, out Voxel? result)`
- `VoxelGrid.TryGetVoxelIndex(Vector2d position, out VoxelIndex result)`
- `VoxelGrid.TryGetVoxelIndex(Vector2d position, Fixed64 layerY, out VoxelIndex result)`
- `VoxelGrid.TryGetVoxel(Vector2d position, out Voxel? result)`
- `VoxelGrid.TryGetVoxel(Vector2d position, Fixed64 layerY, out Voxel? result)`
- `GridTracer.TraceLine(GridWorld world, Vector2d start, Vector2d end, Fixed64? padding = null, bool includeEnd = true, Fixed64 layerY = default)`
- `GridTracer.GetCoveredVoxels(GridWorld world, Vector2d boundsMin, Vector2d boundsMax, Fixed64 layerY = default, Fixed64? padding = null)`
- `GridTracer.GetCoveredScanCells(GridWorld world, Vector2d boundsMin, Vector2d boundsMax, Fixed64 layerY = default, Fixed64? padding = null)`
- `GridTracer.GetCoveredScanCellsInto(...)` overloads for caller-owned `SwiftList<ScanCell>` and scratch paths
- `GridScanManager.ScanRadius(GridWorld world, Vector2d position, Fixed64 radius, Fixed64 layerY = default, ...)`
- `GridScanManager.ScanRadius<T>(GridWorld world, Vector2d position, Fixed64 radius, Fixed64 layerY = default, ...)`
- `GridScanManager.ScanRadiusInto(...)` overloads for untyped, typed, scratch, and non-scratch paths
- `GridObstacleManager.TryAddObstacle(this VoxelGrid grid, Vector2d position, BoundsKey token)`
- `GridObstacleManager.TryAddObstacle(this VoxelGrid grid, Vector2d position, Fixed64 layerY, BoundsKey token)`
- `GridObstacleManager.TryRemoveObstacle(this VoxelGrid grid, Vector2d position, BoundsKey token)`
- `GridObstacleManager.TryRemoveObstacle(this VoxelGrid grid, Vector2d position, Fixed64 layerY, BoundsKey token)`
- `BoundsBlocker` constructor or static factory accepting 2D bounds plus `layerY`

Do not add a world-level occupant registration overload that accepts `Vector2d` in the first release because `IVoxelOccupant.Position` is currently the source of truth and remains `Vector3d`.

## Behavior Matrix

| Operation | Existing 3D API | New 2D API |
| --- | --- | --- |
| position lookup | uses supplied `Vector3d(x, y, z)` | lifts `Vector2d(x, z)` with `layerY` |
| grid lookup | resolves containing grid by 3D world position | resolves containing grid by projected XZ position and layer |
| voxel lookup | returns voxel containing 3D world position | returns voxel containing projected XZ position on selected layer |
| trace line | traces 3D line | traces XZ line on selected layer |
| covered voxels | covers 3D bounds | covers XZ bounds on selected layer |
| covered scan cells | covers 3D bounds | covers scan cells intersecting XZ bounds on selected layer |
| radius scan | sphere-like fixed-point distance over X/Y/Z | layer-locked circle distance over X/Z |
| obstacle by position | mutates voxel at 3D position | mutates voxel at projected XZ position and layer |
| blocker bounds | uses `FixedBoundArea` with `Vector3d` corners | builds a layer-locked `FixedBoundArea` from 2D corners |
| occupants | `IVoxelOccupant.Position` is `Vector3d` | returned occupants must be on selected layer |

## Phase 0: Lock API Names And Ambiguity Rules

Intent: make the 2D surface easy to use without creating ambiguous overloads or changing existing call-site meaning.

Likely files:

- `docs/feature-work/2026-06-11-vector2d-query-api-plan.md`
- `src/GridForge/Utility/GridTracer.cs`
- `src/GridForge/Grids/Managers/GridScanManager.cs`
- `src/GridForge/Grids/Managers/GridWorld.cs`
- `src/GridForge/Grids/VoxelGrid.cs`

Checklist:

- [x] Decide final projection helper name: `GridPlane2d`.
- [x] Decide final layer parameter name: `layerY`.
- [x] Confirm `GridTracer.TraceLine(Vector2d, Vector2d, ...)` will append `layerY` at the end in Phase 2, preserving existing positional `padding` and `includeEnd` call-site behavior.
- [x] Confirm paired overload style for `out`-heavy APIs: default-layer overloads delegate to explicit `layerY` overloads.
- [x] Confirm `Vector2d` always means XZ projection in GridForge docs and XML comments: `Vector2d.X` maps to world X and `Vector2d.Y` maps to world Z.
- [x] Record these decisions in this plan before implementation starts.

Exit criteria:

- [x] There is one agreed meaning for `Vector2d` in GridForge.
- [x] Existing `Vector2d` tracer call sites keep their padding/include-end behavior.
- [x] There is no unresolved overload ambiguity.

## Phase 1: Add Projection Helper And Lookup Overloads

Intent: add the low-risk `Vector2d` lookup surface first.

Likely files:

- Create: `src/GridForge/Spatial/GridPlane2d.cs`
- Modify: `src/GridForge/Grids/Managers/GridWorld.cs`
- Modify: `src/GridForge/Grids/VoxelGrid.cs`
- Test: `tests/GridForge.Tests/Spatial/GridPlane2d.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/GridWorld.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs`

Checklist:

- [x] Add `GridPlane2d.ToWorld(...)`, `FromWorld(...)`, `ToWorldBounds(...)`, and XZ squared-distance helpers.
- [x] Add exact tests proving `Vector2d(3, 5)` maps to `Vector3d(3, layerY, 5)`.
- [x] Add default-layer tests proving omitted `layerY` maps to zero.
- [x] Add `GridWorld.TryGetGrid(...)`, `TryGetGridAndVoxel(...)`, and `TryGetVoxel(...)` overloads for `Vector2d`.
- [x] Add `VoxelGrid.TryGetVoxelIndex(...)` and `TryGetVoxel(...)` overloads for `Vector2d`.
- [x] Ensure all overloads delegate through the projection helper and existing `Vector3d` lookup paths.
- [x] Add tests for default layer, explicit nonzero layer, outside bounds, and exact boundary positions.

Exit criteria:

- [x] 2D lookup APIs work for flat XZ grids and vertical Y layers.
- [x] Existing 3D lookup behavior is unchanged.
- [x] Projection helper has exact deterministic tests.

Validation:

```bash
dotnet restore GridForge.slnx
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "GridPlane2d|GridWorld|VoxelGrid"
```

Phase 1 validation on 2026-06-11:

- `dotnet restore GridForge.slnx` passed.
- `dotnet build GridForge.slnx --configuration Debug` passed with 0 warnings.
- `dotnet test GridForge.slnx --configuration Debug --no-build --filter "GridPlane2d|GridWorld|VoxelGrid"` passed: 77 tests.
- `dotnet test GridForge.slnx --configuration Debug --no-build` passed: 211 tests.

## Phase 2: Extend Tracing And Coverage APIs

Intent: make line tracing, covered voxels, and covered scan cells easy to call with 2D bounds.

Likely files:

- Modify: `src/GridForge/Utility/GridTracer.cs`
- Test: `tests/GridForge.Tests/Utility/GridTracer.Tests.cs`

Checklist:

- [x] Correct the existing `GridTracer.TraceLine(Vector2d, Vector2d, ...)` XML docs so they say XZ plane with `layerY`, not X-Y plane.
- [x] Preserve existing `padding` and `includeEnd` call-site behavior.
- [x] Add explicit-layer support for `TraceLine(Vector2d, Vector2d, ...)` without ambiguous overloads.
- [x] Add `GetCoveredVoxels(...)` overloads accepting `Vector2d` bounds and `layerY`.
- [x] Add `GetCoveredScanCells(...)` overloads accepting `Vector2d` bounds and `layerY`.
- [x] Add caller-owned `GetCoveredScanCellsInto(...)` overloads for 2D bounds.
- [x] Add tests for default layer, explicit layer, descending bounds, padding, include-end behavior, and multi-grid coverage.

Exit criteria:

- [x] 2D tracing and coverage produce the same voxels as equivalent explicit `Vector3d` calls on the selected layer.
- [x] Existing 2D trace tests remain meaningful and documentation matches behavior.
- [x] Caller-owned scan-cell paths support 2D inputs without extra steady-state allocations.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "GridTracer"
```

Phase 2 validation on 2026-06-11:

- `dotnet restore GridForge.slnx` passed.
- `dotnet build GridForge.slnx --configuration Debug` passed with 0 warnings.
- `dotnet test GridForge.slnx --configuration Debug --no-build --filter "GridTracer"` passed: 20 tests.
- `dotnet test GridForge.slnx --configuration Debug --no-build` passed: 216 tests.
- `PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py` passed: 4 tests.

## Phase 3: Add Layer-Locked 2D Radius Scans

Intent: add true 2D scan semantics instead of reusing the 3D spherical scan path.

Likely files:

- Modify: `src/GridForge/Grids/Managers/GridScanManager.cs`
- Modify: `src/GridForge/Grids/Nodes/ScanCell.cs`
- Test: `tests/GridForge.Tests/Grids/ScanCell.Tests.cs`
- Test: `tests/GridForge.Tests/Grids/ManagerCoverage.Tests.cs`

Checklist:

- [x] Add untyped `ScanRadius(...)` and `ScanRadiusInto(...)` overloads for `Vector2d`.
- [x] Add typed `ScanRadius<T>(...)` and `ScanRadiusInto<T>(...)` overloads for `Vector2d`.
- [x] Add scratch overloads for caller-owned scan-cell storage.
- [x] Implement internal 2D scan append methods that enumerate candidate scan cells on the selected layer.
- [x] Add exact filtering that rejects occupants from different resolved Y layers.
- [x] Add XZ squared-distance filtering that ignores vertical offset after the layer check.
- [x] Add tests with occupants inside radius on the same layer, outside radius on the same layer, and inside XZ radius on a different Y layer.
- [x] Add tests proving 2D scans and 3D scans intentionally differ when vertical offset is present.
- [x] Add allocation-focused tests for caller-owned result paths if the existing scan allocation tests can be extended cleanly.

Exit criteria:

- [x] `ScanRadius(Vector2d, ...)` behaves like a layer-locked 2D circle query.
- [x] It does not return occupants from other Y layers, even when scan cells span multiple vertical voxels.
- [x] Typed, untyped, scratch, and non-scratch scan paths share the same semantics.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "ScanRadius|ScanCell|ManagerCoverage"
```

2026-06-11 validation:

- `dotnet restore GridForge.slnx` - pass; all projects up-to-date.
- `dotnet build GridForge.slnx --configuration Debug` - pass; 0 warnings, 0 errors.
- `dotnet test GridForge.slnx --configuration Debug --no-build --filter "ScanRadius|ScanCell|ManagerCoverage"` - pass; 53 passed, 0 failed.
- `dotnet test GridForge.slnx --configuration Debug --no-build` - pass; 218 passed, 0 failed.
- `dotnet test GridForge.slnx --configuration Release` - pass; 220 passed, 0 failed.
- `dotnet test GridForge.slnx --configuration ReleaseLean` - pass; 220 passed, 0 failed.
- `PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py` - pass; 4 tests.
- `dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list` - pass; confirmed `scan-radius-memory` selection.
- `dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -f net8.0 -- scan-radius-memory --filter '*' --job short` - pass; warm pool allocation stayed low (`320 B` default toolchain, `1616 B` in-process). BenchmarkDotNet reported the expected short-run minimum-iteration-time warning.

## Phase 4: Add 2D Obstacle And Blocker Conveniences

Intent: let 2D callers place obstacles and bounds blockers without hand-writing `Vector3d` corners.

Likely files:

- Modify: `src/GridForge/Grids/Managers/GridObstacleManager.cs`
- Modify: `src/GridForge/Blockers/BoundsBlocker.cs`
- Test: `tests/GridForge.Tests/Blockers/BlockerTests.cs`
- Test: `tests/GridForge.Tests/Grids/ManagerCoverage.Tests.cs`

Checklist:

- [ ] Add `GridObstacleManager.TryAddObstacle(...)` overloads for `Vector2d` positions.
- [ ] Add `GridObstacleManager.TryRemoveObstacle(...)` overloads for `Vector2d` positions.
- [ ] Add `BoundsBlocker` 2D constructor or static factory that creates a layer-locked `FixedBoundArea`.
- [ ] Ensure blocker apply/remove behavior matches equivalent explicit `Vector3d` bounds.
- [ ] Add tests for default layer, explicit layer, stacked blockers, cached blocker removal, and multi-grid coverage.
- [ ] Confirm blocker events still report `Vector3d` bounds because runtime event payloads remain 3D.

Exit criteria:

- [ ] 2D obstacle/blocker helpers are convenience-only and reuse existing blocker token and event behavior.
- [ ] Existing blocker behavior remains unchanged.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build --filter "Blocker|Obstacle|ManagerCoverage"
```

## Phase 5: Documentation And Examples

Intent: teach users the 2D model without implying GridForge has a separate 2D runtime.

Likely files:

- Modify: `README.md`
- Modify: `docs/wiki/Home.md`
- Modify: `docs/wiki/Getting-Started.md`
- Modify: `docs/wiki/Core-Concepts.md`
- Modify: `docs/wiki/Common-Workflows.md`
- Modify: `docs/wiki/GridTracer-and-Coverage.md`
- Modify: `docs/wiki/Scan-Cells-and-Query-Flow.md`
- Modify: `docs/wiki/Blockers-and-Obstacles.md`
- Modify: `docs/wiki/Occupants-and-Partitions.md`
- Modify: `docs/wiki/Determinism-Snapping-and-Pooling.md`
- Modify: `src/GridForge/GridForge.csproj` if package metadata or XML docs need new wording

Checklist:

- [ ] Add a concise README note that 2D simulations use XZ coordinates with `layerY` defaulting to zero.
- [ ] Add wiki examples for `TryGetVoxel(Vector2d)`, `TraceLine(Vector2d)`, `ScanRadius(Vector2d)`, and a 2D `BoundsBlocker`.
- [ ] Document that `IVoxelOccupant.Position` remains `Vector3d`.
- [ ] Document that 2D scans are layer-locked and use XZ distance.
- [ ] Document the difference between 2D convenience APIs and future topology support.
- [ ] Update XML docs for every new public API.
- [ ] Run wiki link rewrite tests if links change.

Exit criteria:

- [ ] README, wiki, XML docs, and tests tell the same 2D projection story.
- [ ] Users can write flat 2D simulation code without repeated `new Vector3d(x, 0, z)` conversions.

Validation:

```bash
dotnet build GridForge.slnx --configuration Debug
dotnet test GridForge.slnx --configuration Debug --no-build
PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/rewrite_wiki_links_for_github_wiki_tests.py
git diff --check
```

## Phase 6: Performance Check

Intent: prove the 2D convenience layer does not regress hot scan or trace paths.

Likely files:

- Modify: `tests/GridForge.Benchmarks/**/*` if scan or trace hot paths change enough to warrant benchmark coverage
- Modify: `tests/GridForge.Tests/**/*`

Benchmark scenarios:

- `Vector2d` lookup versus equivalent `Vector3d` lookup
- `Vector2d` trace versus equivalent explicit-layer `Vector3d` trace
- 2D radius scan on a single layer with sparse occupant distribution
- 2D radius scan where coarse scan cells contain occupants on neighboring Y layers
- caller-owned 2D scan result paths

Checklist:

- [ ] Run existing tests and allocation-focused scan tests first.
- [ ] Add benchmarks only if the implementation adds new hot-path loops or measurable filtering overhead.
- [ ] Confirm 2D scan layer filtering avoids avoidable allocations.
- [ ] Confirm helper methods are inline-friendly and do not use LINQ in hot paths.

Exit criteria:

- [ ] 2D helpers are allocation-conscious.
- [ ] Any added scan overhead is documented and justified by correct layer-locked semantics.

Validation:

```bash
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- list
dotnet run --project tests/GridForge.Benchmarks/GridForge.Benchmarks.csproj -c Release -- all --filter '*Scan*'
```

## Test Matrix

Minimum 2D API coverage before release:

- `GridPlane2d.ToWorld` exact mapping
- `GridPlane2d.FromWorld` exact projection
- default `layerY` equals zero
- explicit nonzero `layerY`
- `GridWorld.TryGetGrid(Vector2d)`
- `GridWorld.TryGetGridAndVoxel(Vector2d)`
- `GridWorld.TryGetVoxel(Vector2d)`
- `VoxelGrid.TryGetVoxelIndex(Vector2d)`
- `VoxelGrid.TryGetVoxel(Vector2d)`
- 2D trace default layer
- 2D trace explicit layer
- 2D trace padding and include-end behavior
- 2D covered voxels over descending bounds
- 2D covered scan cells into caller-owned result list
- 2D untyped radius scan
- 2D typed radius scan
- 2D scan scratch overload
- 2D scan rejects occupants from other Y layers
- 2D scan differs from 3D scan when vertical offset matters
- 2D obstacle add/remove
- 2D bounds blocker apply/remove/reapply
- cached 2D blocker removal
- multi-grid 2D coverage
- Release and ReleaseLean build and test coverage

## Risk Register

| Risk | Mitigation |
| --- | --- |
| `Vector2d` axis semantics surprise users | Document GridForge's XZ projection everywhere and use `layerY` naming consistently. |
| Existing `TraceLine(Vector2d, ...)` calls become ambiguous | Append `layerY` after existing optional parameters or use a named explicit-layer API. |
| 2D scans accidentally include other vertical layers | Add explicit resolved-layer filtering before XZ distance checks. |
| Convenience overloads duplicate projection code | Route all conversions through one projection helper. |
| The API grows too wide | Add overloads only for high-value lookup, trace, scan, obstacle, and blocker workflows. |
| Future topology work invalidates the helper | Keep `GridPlane2d` focused on projection; topology-specific world/index conversion remains inside `VoxelGrid` later. |
| Docs imply a separate 2D runtime | State that 2D support is a convenience layer over the 3D voxel runtime. |
| ReleaseLean breaks through new attributes | Keep any serialization/docs additions aligned with existing MemoryPack guard patterns. |

## Recommended Implementation Order

1. Finish Phase 0 naming and overload-shape decisions.
2. Implement Phase 1 lookup and projection helper.
3. Implement Phase 2 tracing and coverage.
4. Implement Phase 3 layer-locked scans.
5. Implement Phase 4 obstacle and blocker conveniences.
6. Complete Phase 5 docs.
7. Run Phase 6 performance checks if scan or trace internals changed materially.
