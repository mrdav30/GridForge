# GridForge Coverage Restoration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore authoritative Release coverage to 100% line, branch, and method coverage with focused behavioral tests and deletion of proven redundant branches.

**Architecture:** Keep the existing runtime architecture and coverage configuration. Add characterization tests at public/internal behavioral seams, replace duplicate diagnostic-key equality with the already-tested `GridDiagnosticChange` value contract, and remove guards made impossible by active-grid, candidate-discovery, dense-storage, and scan-cell lifecycle invariants.

**Tech Stack:** C# 11, .NET 8 test host, xUnit v3, Coverlet collector, OpenCover/Cobertura, ReportGenerator, FixedMathSharp, SwiftCollections.

## Global Constraints

- Release line coverage must be 100% under `tests/GridForge.Tests/coverlet.runsettings`.
- Release branch coverage must be 100% under `tests/GridForge.Tests/coverlet.runsettings`.
- Release method coverage must be 100% in the paired OpenCover report.
- ReleaseLean has a different compilation surface; build and test it, but do not mix it into the Release coverage denominator.
- Preserve deterministic fixed-point behavior, stable ordering, explicit `GridWorld` ownership, and engine independence.
- Do not add coverage exclusions, reflection-only coverage tests, new dependencies, or speculative abstractions.
- Delete production code only where every caller and producer invariant proves the branch unreachable.
- Preserve all pre-existing dirty worktree changes; implementation commits may touch only files named by their task.

---

### Task 1: Close diagnostics coverage and delete duplicate diagnostic-key logic

**Files:**
- Modify: `src/GridForge/Diagnostics/GridDiagnosticSession.cs`
- Modify: `src/GridForge/Diagnostics/GridDiagnostics.cs`
- Modify: `tests/GridForge.Tests/Diagnostics/GridDiagnosticSessionTests.cs`
- Modify: `tests/GridForge.Tests/Diagnostics/GridDiagnosticGeometryTests.cs`
- Modify: `tests/GridForge.Tests/Diagnostics/GridDiagnosticsContractTests.cs`
- Modify: `tests/GridForge.Tests/Diagnostics/GridDiagnosticsPhysicalQueryTests.cs`
- Modify: `tests/GridForge.Tests/Diagnostics/GridDiagnosticsSparseAddressTests.cs`

**Interfaces:**
- Consumes: existing `GridDiagnosticChange`, `GridDiagnosticSession`, `GridDiagnostics`, `GridWorldTestFactory`, and diagnostic test helpers.
- Produces: complete public comparison/equality coverage, complete diagnostic query/session/geometry behavior coverage, and a session key path that reuses `GridDiagnosticChange` equality and deterministic hashing.

- [ ] **Step 1: Add characterization tests for the diagnostic value contract**

Add `GridDiagnosticChange_ShouldImplementComparisonEqualityAndHashContracts` to `GridDiagnosticSessionTests.cs`. Build a baseline change and variants that differ at the first unequal field. Exercise both comparison directions and all scope orders.

```csharp
[Fact]
public void GridDiagnosticChange_ShouldImplementComparisonEqualityAndHashContracts()
{
    WorldVoxelIndex cellIndex = new(1, 2, 3, new VoxelIndex(4, 5, 6));
    GridDiagnosticChange baseline = new(
        GridDiagnosticChangeKind.GridChanged,
        1,
        2,
        3,
        default,
        new VoxelIndex(4, 5, 6),
        new Vector3d(1, 2, 3),
        new Vector3d(4, 5, 6));
    GridDiagnosticChange equal = baseline;

    Assert.Equal(0, baseline.CompareTo(equal));
    Assert.True(baseline.Equals(equal));
    Assert.True(baseline.Equals((object)equal));
    Assert.False(baseline.Equals(null));
    Assert.False(baseline.Equals("not a change"));
    Assert.Equal(baseline.GetHashCode(), equal.GetHashCode());

    GridDiagnosticChange Variant(
        GridDiagnosticChangeKind? kind = null,
        long? worldSpawnToken = null,
        ushort? gridIndex = null,
        long? gridSpawnToken = null,
        WorldVoxelIndex? worldIndex = null,
        VoxelIndex? voxelIndex = null,
        Vector3d? boundsMin = null,
        Vector3d? boundsMax = null) => new(
            kind ?? baseline.Kind,
            worldSpawnToken ?? baseline.WorldSpawnToken,
            gridIndex ?? baseline.GridIndex,
            gridSpawnToken ?? baseline.GridSpawnToken,
            worldIndex ?? baseline.WorldIndex,
            voxelIndex ?? baseline.VoxelIndex,
            boundsMin ?? baseline.BoundsMin,
            boundsMax ?? baseline.BoundsMax);

    static void AssertComparisonDiffers(GridDiagnosticChange left, GridDiagnosticChange right)
    {
        int forward = left.CompareTo(right);
        int reverse = right.CompareTo(left);
        Assert.NotEqual(0, forward);
        Assert.Equal(-Math.Sign(forward), Math.Sign(reverse));
        Assert.False(left.Equals(right));
    }

    GridDiagnosticChange world = new(GridDiagnosticChangeKind.WorldReset, 1, ushort.MaxValue, 0, default, default, default, default);
    GridDiagnosticChange cell = new(GridDiagnosticChangeKind.OccupantChanged, 1, 2, 3, cellIndex, cellIndex.VoxelIndex, default, default);
    GridDiagnosticChange range = new(GridDiagnosticChangeKind.SparseAddressChanged, 1, 2, 3, default, new VoxelIndex(1, 0, 1), default, default);
    Assert.True(world.CompareTo(baseline) < 0);
    Assert.True(baseline.CompareTo(cell) < 0);
    Assert.True(cell.CompareTo(range) < 0);

    AssertComparisonDiffers(baseline, Variant(worldSpawnToken: 2));
    AssertComparisonDiffers(baseline, Variant(gridIndex: 3));
    AssertComparisonDiffers(baseline, Variant(gridSpawnToken: 4));
    AssertComparisonDiffers(baseline, Variant(voxelIndex: new VoxelIndex(5, 5, 6)));
    AssertComparisonDiffers(baseline, Variant(boundsMin: new Vector3d(2, 2, 3)));
    AssertComparisonDiffers(baseline, Variant(boundsMin: new Vector3d(1, 3, 3)));
    AssertComparisonDiffers(baseline, Variant(boundsMin: new Vector3d(1, 2, 4)));
    AssertComparisonDiffers(baseline, Variant(boundsMax: new Vector3d(5, 5, 6)));
    AssertComparisonDiffers(baseline, Variant(boundsMax: new Vector3d(4, 6, 6)));
    AssertComparisonDiffers(baseline, Variant(boundsMax: new Vector3d(4, 5, 7)));
    AssertComparisonDiffers(baseline, Variant(kind: GridDiagnosticChangeKind.GridAdded));
    Assert.False(baseline.Equals(Variant(worldIndex: cellIndex)));
}
```

Do not call production private helpers or derive expected field ordering with the code under test.

- [ ] **Step 2: Run the focused diagnostic value test and mutation-check it**

Run:

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Debug --filter "FullyQualifiedName~GridDiagnosticChange_ShouldImplementComparisonEqualityAndHashContracts" --property:UseLocalLsfStack=false
```

Expected: PASS because this is a characterization test for existing live behavior. Temporarily change one `CompareTo` field comparison or one `Equals` conjunct, rerun and confirm FAIL, restore the production line, then rerun and confirm PASS.

- [ ] **Step 3: Add the remaining public diagnostic behavior cases**

Add these exact cases in the named existing test classes:

```csharp
// GridDiagnosticGeometryTests
GridTopologyKind unsupported = (GridTopologyKind)999;
Assert.Equal(0, GridDiagnosticGeometry.GetVertexCount(unsupported));
Assert.Equal(0, GridDiagnosticGeometry.GetEdgeCount(unsupported));
Assert.Equal(0, GridDiagnosticGeometry.WriteVertices(in unsupportedCell, vertices));
Assert.True(GridDiagnosticGeometry.GetEdges(unsupported).IsEmpty);

// GridDiagnosticsContractTests
Assert.Equal(GridDiagnosticQuery.DefaultMaxCells, new GridDiagnosticQuery(maxCells: 0).MaxCells);
Assert.Equal(GridDiagnosticQuery.DefaultMaxCells, new GridDiagnosticQuery(maxCells: -1).MaxCells);
Assert.Equal(GridDiagnosticQueryStatus.InactiveWorld,
    GridDiagnostics.VisitCells(null!, GridDiagnosticQuery.AllPhysical(), ref visitor).Status);
```

Extend the existing physical-cell resolution fixture with four malformed descriptors: mismatched world token, grid index, grid spawn token, and voxel index. Each must return `false` with null grid/voxel outputs.

Add the following behavioral scenarios using existing grid helpers:

```csharp
// selected active grid plus conflicting topology/storage filter -> Completed, zero cells
// selected sparse MissingOnly query without bounds -> MissingAddressSpaceRequiresBounds
// dense selected grid with bounds wholly outside -> Completed, zero cells
// 3x1x3 sparse MissingOnly query -> interior missing cell lacks Boundary, edge missing cell has Boundary
// 3x3x3 dense exact-centre bounds -> only (1,1,1), rejecting lower and upper Y layers
```

- [ ] **Step 4: Add session construction, clearing, lifecycle, and foreign-event behavior cases**

Add one fact that asserts the two constructor guards, a bulk obstacle clear, and idempotent disposal:

```csharp
Assert.Throws<ArgumentNullException>(() => new GridDiagnosticSession(null!));
using GridWorld inactiveWorld = new();
inactiveWorld.Dispose();
Assert.Throws<InvalidOperationException>(() => new GridDiagnosticSession(inactiveWorld));

GridObstacleManager.TryAddObstacle(world, voxel.WorldIndex, world.AllocateObstacleToken());
grid.ClearObstacles(voxel);
Assert.Contains(changes, change =>
    change.Kind == GridDiagnosticChangeKind.ObstacleChanged
    && change.WorldIndex.Equals(voxel.WorldIndex));

session.Dispose();
session.Dispose();
```

Keep the existing foreign-world obstacle/occupant assertions; they cover the world-token rejection path used by static manager events.

- [ ] **Step 5: Delete diagnostic branches made impossible by public invariants**

In `GridDiagnostics.cs`:

```csharp
if (!world.TryGetGrid(query.GridIndex.Value, out VoxelGrid? requestedGrid))
    return new GridDiagnosticQueryResult(GridDiagnosticQueryStatus.InvalidGrid, 0, 0);

VoxelGrid grid = requestedGrid!;
// Use grid for RequiresMissingAddressBounds and VisitGridCells.
```

Remove the `grid.IsActive` branch from `ShouldVisitGrid`; all callers enumerate `ActiveGrids` or use `TryGetGrid`. For an unbounded active sparse grid, assign the inclusive range and `return true` without rechecking positive dimensions; active initialization guarantees positive dimensions and reset clears activity first.

In `GridDiagnosticSession.cs`, instance world events already come only from `_world`. Remove the impossible token/disposed checks from `HandleActiveGridAdded`, `HandleActiveGridRemoved`, and `HandleWorldReset`; reduce `HandleActiveGridChanged` to the pending-reset guard. Keep token checks for static obstacle/occupant events.

Delete `CanRecord` and make the static-event guard explicit:

```csharp
private bool CanRecordCell(WorldVoxelIndex worldIndex) =>
    worldIndex.WorldSpawnToken == _worldSpawnToken
    && !HasPendingWorldReset()
    && _world.TryGetGrid(worldIndex, out _);
```

Replace the duplicate key implementation with the existing deterministic value contract:

```csharp
private readonly SwiftDictionary<GridDiagnosticChange, int> _changeIndexes = new();

private static GridDiagnosticChange CreateChangeKey(GridDiagnosticChange change) =>
    change.WithKind((GridDiagnosticChangeKind)GetScope(change));
```

Use `CreateChangeKey` in `HandleWorldReset` and `RecordChange`, delete `GridDiagnosticChangeKey`, and retain `GridDiagnosticChangeScope` plus its existing `GetScope` logic. In `RecordSparseAddressRangeChange`, index the active grid directly from `_world.ActiveGrids[eventInfo.GridIndex]` and remove the impossible missing-grid fallback.

- [ ] **Step 6: Run focused diagnostics and commit**

Run:

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Debug --filter "FullyQualifiedName~GridForge.Tests.Diagnostics" --property:UseLocalLsfStack=false
```

Expected: all diagnostic tests pass.

Commit only the files in this task:

```powershell
git add src/GridForge/Diagnostics/GridDiagnosticSession.cs src/GridForge/Diagnostics/GridDiagnostics.cs tests/GridForge.Tests/Diagnostics
git commit -m "test: close diagnostic coverage gaps"
```

---

### Task 2: Close storage, scan-cell, topology, world, and manager branches

**Files:**
- Modify: `src/GridForge/Grids/Nodes/ScanCell.cs`
- Modify: `src/GridForge/Grids/Storage/DenseVoxelGridStorage.cs`
- Modify: `src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs`
- Modify: `src/GridForge/Grids/Topology/TopologyVoxelRangeUtility.cs`
- Modify: `src/GridForge/Grids/Topology/VoxelNeighborResolver.cs`
- Modify: `tests/GridForge.Tests/Grids/StorageGuardTests.cs`
- Modify: `tests/GridForge.Tests/Grids/ScanCell.Tests.cs`
- Modify: `tests/GridForge.Tests/Grids/SparseVoxelGrid.Tests.cs`
- Modify: `tests/GridForge.Tests/Grids/VoxelGrid.Tests.cs`
- Modify: `tests/GridForge.Tests/Grids/ManagerCoverage.Tests.cs`
- Modify: `tests/GridForge.Tests/Grids/ClosestQueryTests.cs`
- Modify: `tests/GridForge.Tests/Grids/HexPrismGrid.Tests.cs`

**Interfaces:**
- Consumes: existing `IVoxelStorageVisitor`, storage guard fixtures, grid factories, `TestOccupant`, and closest-query fixtures.
- Produces: deterministic visitor/range/guard coverage and simpler producer-guaranteed lookup paths.

- [ ] **Step 1: Add deterministic public/internal guard tests**

Use existing fixtures to add these exact assertions:

```csharp
Assert.False(GridOccupantManager.TryGetOccupancyTicket(null!, occupant, default, out OccupantTicket ticket));
Assert.Equal(default, ticket);
Assert.Empty(GridOccupantManager.GetOccupiedIndices(null!, occupant));

Assert.False(emptySparseGrid.TryRemoveVoxel(new VoxelIndex(0, 0, 0)));
Assert.Equal(originalVersion, emptySparseGrid.Version);

inactiveScanCell.Reset();
Assert.False(inactiveScanCell.TryGetOccupantAt(default, default, out IVoxelOccupant? found));
Assert.Null(found);

Assert.Equal(-1, grid.GetScanCellKey(0, -1, 0));
Assert.Equal(-1, grid.GetScanCellKey(0, 0, -1));
```

Extend the existing closest-grid test with a topology filter that has no candidate while an unfiltered configured grid exists, and assert `TryGetClosestGridAndVoxel` returns false with null outputs.

- [ ] **Step 2: Cover visitor early-stop, uninitialized, and sparse range behavior**

In `StorageGuardTests.cs`, reuse or add a tiny counting visitor:

```csharp
private struct CountingVoxelVisitor : IVoxelStorageVisitor
{
    private readonly bool _continue;
    public int Count;

    public CountingVoxelVisitor(bool @continue)
    {
        _continue = @continue;
        Count = 0;
    }

    public bool Visit(Voxel voxel)
    {
        Count++;
        return _continue;
    }
}
```

Assert dense and sparse uninitialized storage visit zero voxels; initialized sparse storage with two voxels and `continue: false` visits exactly one. Query a populated sparse block with a range containing one allocated voxel and one allocated out-of-range voxel; assert only the in-range voxel is added. Query an initialized sparse storage across one populated and one empty valid scan cell; assert one result, then repeat with the same redundancy set and assert no duplicate. Include invalid negative Y and Z scan coordinates and assert no result.

Add `VoxelGrid_VisitVoxels_ShouldNoOpWhenStorageIsMissing` using a new inactive `VoxelGrid` and the same counting visitor.

- [ ] **Step 3: Cover post-projection and candidate guard branches**

In `HexPrismGrid.Tests.cs`, add a theory that calls the topology-level `TryGetVoxelIndex` with coarse bounds containing the point but dimensions one smaller than the rounded X, Y, or Z coordinate. Each row must assert false and a default index.

Extend `SparseVoxelBlock` range coverage with two voxels in one block and exact bounds around only one. Extend neighbor/world manager tests with a registered-world/different-occupant record miss and a fresh-world/no-registry snapshot.

- [ ] **Step 4: Remove redundant producer-validated branches and atomically claim scan-cell reset**

Replace the stale reset guard with a single atomic cleanup claim:

```csharp
object? syncRoot = Interlocked.Exchange(ref _occupantSyncRoot, null);
if (syncRoot == null)
    return;

lock (syncRoot)
{
    // Existing occupant release and field reset body.
    IsAllocated = false;
}
```

Do not write `_occupantSyncRoot` again at the end; the successful exchange already owns and clears it. Existing synchronized lookup tests must continue to prove stale readers return safely.

In `VoxelNeighborResolver.TryGetCandidateGrid`, delete the allocation check because `GridCandidateDiscovery.CollectInStableSlotOrder` adds only allocated IDs. In dense `AddScanCellsInRange`, index `ScanCells[scanCellKey]` after a nonnegative grid-generated key instead of repeating `TryGetValue`. In sparse `AddScanCellsInRange`, remove the `block.ScanCell != null` branch because `SparseVoxelBlock.Initialize` always assigns it before block insertion.

After `TryClipBoundsToGrid` succeeds in rectangular candidate-range calculation, resolve both endpoints, assert the producer invariant in Debug, and return true; clipped active rectangular endpoints are guaranteed resolvable:

```csharp
if (!TryClipBoundsToGrid(grid, snappedMin, snappedMax, out Vector3d clippedMin, out Vector3d clippedMax))
    return false;

bool minResolved = grid.TryGetVoxelIndex(clippedMin, out minIndex);
bool maxResolved = grid.TryGetVoxelIndex(clippedMax, out maxIndex);
Debug.Assert(minResolved && maxResolved);
return true;
```

Add the explicit `System.Diagnostics` using required by `Debug.Assert`.

- [ ] **Step 5: Run focused runtime tests and commit**

Run:

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Debug --filter "FullyQualifiedName~StorageGuardTests|FullyQualifiedName~ScanCellTests|FullyQualifiedName~SparseVoxelGridTests|FullyQualifiedName~VoxelGridTests|FullyQualifiedName~ManagerCoverageTests|FullyQualifiedName~ClosestQueryTests|FullyQualifiedName~HexPrismGridTests" --property:UseLocalLsfStack=false
```

Expected: all selected tests pass without hangs.

Commit only Task 2 files:

```powershell
git add src/GridForge/Grids/Nodes/ScanCell.cs src/GridForge/Grids/Storage/DenseVoxelGridStorage.cs src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs src/GridForge/Grids/Topology/TopologyVoxelRangeUtility.cs src/GridForge/Grids/Topology/VoxelNeighborResolver.cs tests/GridForge.Tests/Grids
git commit -m "test: close runtime branch coverage gaps"
```

---

### Task 3: Reuse blocker tests, complete value semantics, and remove no-op fixture cleanup

**Files:**
- Modify: `tests/GridForge.Tests/Blockers/BlockerTests.cs`
- Modify: `tests/GridForge.Tests/Spatial/SpatialTypes.Tests.cs`
- Modify: `tests/GridForge.Tests/Grids/GridWorld.Tests.cs`
- Modify: `tests/GridForge.Tests/GridForgeFixture.cs`

**Interfaces:**
- Consumes: existing blocker behavior facts, token/ticket value facts, allocator wraparound fact, and direction utility fact.
- Produces: coverage for both forwarding constructors, `ObstacleToken` value members, remaining short-circuit guards, and a smaller fixture.

- [ ] **Step 1: Rewire existing blocker behavior facts instead of adding constructor-only tests**

Use the forwarding constructors in the existing apply/remove tests:

```csharp
BoundsBlocker blocker = new(
    _world,
    new Vector3d(32, 0, 32),
    new Vector3d(34, 0, 34));

AreaBlocker areaBlocker = new(
    _world,
    new Vector2d(1, 1),
    new Vector2d(3, 3),
    layerY: Fixed64.One);
```

Delete only the now-unused local `FixedBoundBox`/`FixedBoundArea` variables. Keep separate cached/noncached and active/inactive blocker tests.

In the bounds blocker fact, apply twice and assert the second application leaves the obstacle count and `BlockageToken` unchanged and does not publish a second `OnBlockageApplied` event.

- [ ] **Step 2: Complete token, ticket, allocator, and direction value cases**

Add beside the existing ticket value fact:

```csharp
[Fact]
public void ObstacleToken_ShouldUseExactValueSemantics()
{
    ObstacleToken token = new(42);
    ObstacleToken same = new(42);
    ObstacleToken different = new(43);

    Assert.True(token == same);
    Assert.False(token != same);
    Assert.False(token == different);
    Assert.True(token != different);
    Assert.True(token.Equals((object)same));
    Assert.False(token.Equals("not a token"));
    Assert.Equal("42", token.ToString());
}
```

Append:

```csharp
Assert.False(new OccupantTicket(-1, 1).IsValid);
Assert.Equal(RectangularDirection.None,
    RectangularDirectionUtility.GetDirectionFromOffset((0, 2, 0)));

long negativeCounter = -1;
Assert.Throws<InvalidOperationException>(
    () => RuntimeIdentityAllocator.Allocate(ref negativeCounter));
Assert.Equal(-1, negativeCounter);
```

Place the ticket/direction assertions in their existing `SpatialTypesTests` facts and the counter assertion in `RuntimeIdentityAllocator_ShouldThrowBeforeWraparound`.

- [ ] **Step 3: Delete the fixture's no-op disposal hook**

Change:

```csharp
public sealed class GridForgeFixture : IDisposable
```

to:

```csharp
public sealed class GridForgeFixture
```

Delete `Dispose()` and the now-unused `using System;`. The fixture owns no disposable resource or finalizer.

- [ ] **Step 4: Run focused blocker/value tests and commit**

Run:

```powershell
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj -c Debug --filter "FullyQualifiedName~BlockerTests|FullyQualifiedName~SpatialTypesTests|FullyQualifiedName~RuntimeIdentityAllocator_ShouldThrowBeforeWraparound" --property:UseLocalLsfStack=false
```

Expected: all selected tests pass.

Commit only Task 3 files:

```powershell
git add tests/GridForge.Tests/Blockers/BlockerTests.cs tests/GridForge.Tests/Spatial/SpatialTypes.Tests.cs tests/GridForge.Tests/Grids/GridWorld.Tests.cs tests/GridForge.Tests/GridForgeFixture.cs
git commit -m "test: close value and blocker coverage gaps"
```

---

### Task 4: Prove 100% Release coverage and validate every supported build configuration

**Files:**
- Generate only: `tests/GridForge.Tests/TestResults/coverage-final/`
- Modify only if a residual is reported: the nearest file already named in Tasks 1-3.

**Interfaces:**
- Consumes: completed Tasks 1-3 and the repository Coverlet configuration.
- Produces: fresh Cobertura, OpenCover, HTML/summary reports, exact 100% threshold evidence, and Debug/ReleaseLean compatibility evidence.

- [ ] **Step 1: Restore and run fresh authoritative Release coverage**

Validate the exact target is within the repository, remove only that generated target if it exists, then run:

```powershell
dotnet restore GridForge.slnx --property:Configuration=Release --property:UseLocalLsfStack=false
dotnet test tests/GridForge.Tests/GridForge.Tests.csproj `
    --configuration Release `
    --no-restore `
    --collect:"XPlat Code Coverage" `
    --settings tests/GridForge.Tests/coverlet.runsettings `
    --results-directory tests/GridForge.Tests/TestResults/coverage-final `
    --property:UseLocalLsfStack=false
```

Expected: all tests pass and exactly one fresh Cobertura/OpenCover pair is generated.

- [ ] **Step 2: Enforce exact line, branch, and method totals**

```powershell
$cobertura = Get-ChildItem tests/GridForge.Tests/TestResults/coverage-final -Filter coverage.cobertura.xml -Recurse -ErrorAction Stop
$openCover = Get-ChildItem tests/GridForge.Tests/TestResults/coverage-final -Filter coverage.opencover.xml -Recurse -ErrorAction Stop
if ($cobertura.Count -ne 1 -or $openCover.Count -ne 1) { throw "Expected one coverage report pair." }

[xml]$coberturaXml = Get-Content -Raw $cobertura.FullName
if ([int]$coberturaXml.coverage.'lines-covered' -ne [int]$coberturaXml.coverage.'lines-valid') { throw "Line coverage is below 100%." }
if ([int]$coberturaXml.coverage.'branches-covered' -ne [int]$coberturaXml.coverage.'branches-valid') { throw "Branch coverage is below 100%." }

[xml]$openCoverXml = Get-Content -Raw $openCover.FullName
$summary = $openCoverXml.CoverageSession.Summary
if ([int]$summary.visitedMethods -ne [int]$summary.numMethods) { throw "Method coverage is below 100%." }
```

Expected: no exception. If any assertion fails, return `BLOCKED` with the exact uncovered sequence/branch/method list from OpenCover; do not weaken filters or thresholds.

- [ ] **Step 3: Generate the final report**

```powershell
reportgenerator `
    "-reports:tests/GridForge.Tests/TestResults/coverage-final/**/coverage.cobertura.xml" `
    "-targetdir:tests/GridForge.Tests/TestResults/coverage-final/report" `
    "-reporttypes:Html;TextSummary;MarkdownSummaryGithub;CsvSummary"
```

Expected: `report/index.html` and summary files exist and report 100% line/branch coverage. Record OpenCover method totals beside them in the task report.

- [ ] **Step 4: Run full Debug and ReleaseLean verification**

```powershell
dotnet build GridForge.slnx --configuration Debug --property:UseLocalLsfStack=false
dotnet test GridForge.slnx --configuration Debug --no-build --property:UseLocalLsfStack=false
dotnet build GridForge.slnx --configuration ReleaseLean --property:UseLocalLsfStack=false
dotnet test GridForge.slnx --configuration ReleaseLean --no-build --property:UseLocalLsfStack=false
```

Expected: all builds/tests pass with zero failures.

- [ ] **Step 5: Commit only residual fixes, if the exact threshold gate found any**

If Step 2 passed immediately, make no Task 4 commit. If a residual was fixed in a file already listed by Tasks 1-3, mutation-check the covering test, rerun Steps 1-4, and stage the allowed source/test set; unchanged files are ignored by Git:

```powershell
git add src/GridForge/Diagnostics/GridDiagnosticSession.cs src/GridForge/Diagnostics/GridDiagnostics.cs src/GridForge/Grids/Nodes/ScanCell.cs src/GridForge/Grids/Storage/DenseVoxelGridStorage.cs src/GridForge/Grids/Storage/SparseVoxelGridStorage.cs src/GridForge/Grids/Topology/TopologyVoxelRangeUtility.cs src/GridForge/Grids/Topology/VoxelNeighborResolver.cs tests/GridForge.Tests/Diagnostics tests/GridForge.Tests/Grids tests/GridForge.Tests/Blockers/BlockerTests.cs tests/GridForge.Tests/Spatial/SpatialTypes.Tests.cs tests/GridForge.Tests/GridForgeFixture.cs
git commit -m "test: close final coverage residuals"
```

Expected: the task report names the residual, the test that catches its mutation, and final 100% evidence.
