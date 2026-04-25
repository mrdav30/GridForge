using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class VoxelGridTests : IDisposable
{
    private GridWorld _world;

    public static TheoryData<SpatialDirection, VoxelIndex> BoundaryDirectionCases => CreateBoundaryDirectionCases();

    public VoxelGridTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Initialize_ShouldSetCorrectDimensions()
    {
        var start = new Vector3d(-10, 0, -10);
        var end = new Vector3d(10, 0, 10);
        var config = new GridConfiguration(start, end);
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        int width = ((end.x - start.x) / _world.VoxelSize).FloorToInt() + 1;
        int height = ((end.y - start.y) / _world.VoxelSize).FloorToInt() + 1;
        int length = ((end.z - start.z) / _world.VoxelSize).FloorToInt() + 1;

        Assert.Equal(width, grid.Width);
        Assert.Equal(height, grid.Height);
        Assert.Equal(length, grid.Length);
        Assert.True(grid.IsActive);
    }

    [Fact]
    public void Initialize_ShouldReturnEarlyWhenGridIsAlreadyActive()
    {
        GridConfiguration initialConfig = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        GridConfiguration secondConfig = new(new Vector3d(50, 0, 50), new Vector3d(51, 0, 51));

        Assert.True(_world.TryAddGrid(initialConfig, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        int originalSpawnToken = grid.SpawnToken;
        ushort originalIndex = grid.GridIndex;

        InvokeGridInitialize(grid, grid.World!, 999, secondConfig);

        Assert.True(grid.IsActive);
        Assert.Equal(originalIndex, grid.GridIndex);
        Assert.Equal(originalSpawnToken, grid.SpawnToken);
        Assert.Equal(initialConfig.BoundsMin, grid.BoundsMin);
        Assert.Equal(initialConfig.BoundsMax, grid.BoundsMax);
    }

    [Fact]
    public void GetVoxel_ShouldReturnCorrectVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        bool found = grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel);

        Assert.True(found);
        Assert.NotNull(voxel);
    }

    [Fact]
    public void IsVoxelAllocated_ShouldReturnCorrectState()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.True(grid.IsVoxelAllocated(10, 0, 10));
        Assert.True(grid.IsVoxelAllocated(20, 0, 20));
    }

    [Fact]
    public void GetScanCell_ShouldReturnValidCell()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        bool found = grid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell scanCell);

        Assert.True(found);
        Assert.NotNull(scanCell);
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnExpectedCount()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        _world.TryAddGrid(config, out ushort index);
        VoxelGrid grid = _world.ActiveGrids[index];

        int count = 0;
        foreach (var cell in grid.GetActiveScanCells())
            count++;

        Assert.Equal(grid.ActiveScanCells?.Count ?? 0, count);
    }

    [Fact]
    public void Grid_ShouldCorrectlyManageNeighbors()
    {
        var config1 = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        var config2 = new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(30, 0, 30));

        _world.TryAddGrid(config1, out ushort index1);
        _world.TryAddGrid(config2, out ushort index2);

        VoxelGrid grid1 = _world.ActiveGrids[index1];
        VoxelGrid grid2 = _world.ActiveGrids[index2];

        Assert.True(grid1.IsConjoined);
        Assert.True(grid2.IsConjoined);

        // since tests run in parallel...there maybe other grids in play
        Assert.True(grid1.NeighborCount >= 1);
        Assert.True(grid1.NeighborCount >= 1);

        // get the direction before removal
        int neighborIndex = (int)VoxelGrid.GetNeighborDirection(grid1, grid2);

        _world.TryRemoveGrid(grid2.GridIndex);

        if (grid1.Neighbors != null)
        {
            if (grid1.Neighbors.ContainsKey(neighborIndex))
                Assert.DoesNotContain(grid2.GridIndex, grid1.Neighbors[neighborIndex]);
        }
        else
            Assert.False(grid1.IsConjoined);

        Assert.False(grid2.IsActive);
    }

    [Fact]
    public void Reset_ShouldReturnEarlyWhenGridIsInactive()
    {
        VoxelGrid detachedGrid = new();

        InvokeGridReset(detachedGrid);

        Assert.False(detachedGrid.IsActive);
        Assert.Equal(0, detachedGrid.NeighborCount);
        Assert.False(detachedGrid.IsConjoined);
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldHandleNegativePositionsAndFractionalVoxelSize()
    {
        ResetWorld((Fixed64)0.5);

        try
        {
            var config = new GridConfiguration(new Vector3d(-1.5, 0, -1.5), new Vector3d(1.5, 0, 1.5));

            Assert.True(_world.TryAddGrid(config, out ushort index));

            VoxelGrid grid = _world.ActiveGrids[index];

            Assert.True(grid.TryGetVoxelIndex(new Vector3d(-1.25, 0, -0.75), out VoxelIndex negativeIndex));
            Assert.Equal(new VoxelIndex(0, 0, 1), negativeIndex);

            Assert.Equal(
                new Vector3d(-1.5, 0, -1.0),
                grid.FloorToGrid(new Vector3d(-1.26, 0, -0.74)));
            Assert.Equal(
                new Vector3d(-1.0, 0, -0.5),
                grid.CeilToGrid(new Vector3d(-1.26, 0, -0.74)));
        }
        finally
        {
            ResetWorld();
        }
    }

    [Fact]
    public void GetScanCellKey_ShouldTransitionAcrossScanCellBoundaries()
    {
        var config = new GridConfiguration(
            new Vector3d(0, 0, 0),
            new Vector3d(15, 0, 15),
            scanCellSize: 4);

        Assert.True(_world.TryAddGrid(config, out ushort index));

        VoxelGrid grid = _world.ActiveGrids[index];
        Vector3d beforeBoundary = new(3.9, 0, 3.9);
        Vector3d atBoundary = new(4, 0, 4);

        int firstCellKey = grid.GetScanCellKey(beforeBoundary);
        int secondCellKey = grid.GetScanCellKey(atBoundary);

        Assert.NotEqual(firstCellKey, secondCellKey);
        Assert.Equal((0, 0, 0), grid.SnapToScanCell(beforeBoundary));
        Assert.Equal((1, 0, 1), grid.SnapToScanCell(atBoundary));
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldIncludeExactBoundsMaxAndRejectOutsidePositions()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.True(grid.TryGetVoxelIndex(new Vector3d(2, 0, 2), out VoxelIndex maxIndex));
        Assert.Equal(new VoxelIndex(2, 0, 2), maxIndex);
        Assert.True(grid.IsVoxelAllocated(maxIndex.x, maxIndex.y, maxIndex.z));

        Assert.False(grid.TryGetVoxelIndex(new Vector3d(2.01, 0, 2), out _));
        Assert.False(grid.TryGetVoxelIndex(new Vector3d(2, 0, 2.01), out _));
        Assert.False(grid.IsVoxelAllocated(3, 0, 2));
        Assert.False(grid.TryGetVoxel(new VoxelIndex(3, 0, 2), out _));
    }

    [Fact]
    public void ScanCellQueries_ShouldReturnGracefulDefaultsForInvalidKeysAndIndices()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 2),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];

        Assert.Equal(-1, grid.GetScanCellKey(new Vector3d(8, 0, 8)));
        Assert.Equal(-1, grid.GetScanCellKey(new VoxelIndex(99, 0, 99)));
        Assert.False(grid.TryGetScanCell(-1, out _));
        Assert.False(grid.TryGetScanCell(999, out _));
        Assert.False(grid.TryGetScanCell(new VoxelIndex(99, 0, 99), out _));
    }

    [Fact]
    public void GridNeighborManagement_ShouldRejectInvalidAndDuplicateRelationshipsAndReleaseLastNeighborSet()
    {
        VoxelGrid centerGrid = CreateStandaloneGrid(
            10,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));
        VoxelGrid eastGrid = CreateStandaloneGrid(
            11,
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0));
        VoxelGrid secondEastGrid = CreateStandaloneGrid(
            13,
            new Vector3d(2, 0, 0),
            new Vector3d(2, 0, 0));
        VoxelGrid sameCenterGrid = CreateStandaloneGrid(
            12,
            new Vector3d(0, 0, 0),
            new Vector3d(0, 0, 0));

        try
        {
            Assert.False(InvokeTryAddGridNeighbor(centerGrid, sameCenterGrid));
            Assert.True(InvokeTryAddGridNeighbor(centerGrid, eastGrid));
            Assert.False(InvokeTryAddGridNeighbor(centerGrid, eastGrid));
            Assert.True(centerGrid.IsConjoined);
            Assert.Equal(1, centerGrid.NeighborCount);
            Assert.False(InvokeTryRemoveGridNeighbor(centerGrid, sameCenterGrid));
            Assert.False(InvokeTryRemoveGridNeighbor(centerGrid, secondEastGrid));
            Assert.True(InvokeTryRemoveGridNeighbor(centerGrid, eastGrid));
            Assert.False(centerGrid.IsConjoined);
            Assert.Null(centerGrid.Neighbors);
            Assert.Equal(0, centerGrid.NeighborCount);
            Assert.False(InvokeTryRemoveGridNeighbor(centerGrid, eastGrid));
        }
        finally
        {
            InvokeGridReset(centerGrid);
            InvokeGridReset(eastGrid);
            InvokeGridReset(secondEastGrid);
            InvokeGridReset(sameCenterGrid);
            centerGrid.World?.Dispose();
            eastGrid.World?.Dispose();
            secondEastGrid.World?.Dispose();
            sameCenterGrid.World?.Dispose();
        }
    }

    [Fact]
    public void IncrementVersion_ShouldWrapFromUIntMaxValue()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        SetGridVersion(grid, uint.MaxValue);

        Assert.Equal(1u, InvokeIncrementVersion(grid));
        Assert.Equal(1u, grid.Version);
    }

    [Fact]
    public void IsGridOverlapValid_ShouldRespectExplicitTolerance()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0)),
            out ushort firstIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(2, 0, 0), new Vector3d(2, 0, 0)),
            out ushort secondIndex));

        VoxelGrid firstGrid = _world.ActiveGrids[firstIndex];
        VoxelGrid secondGrid = _world.ActiveGrids[secondIndex];

        Assert.False(VoxelGrid.IsGridOverlapValid(firstGrid, secondGrid, tolerance: Fixed64.Zero));
        Assert.True(VoxelGrid.IsGridOverlapValid(firstGrid, secondGrid, tolerance: (Fixed64)2));
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnEmptyWhenGridHasNoOccupants()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(5, 0, 5)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.Empty(grid.GetActiveScanCells());
    }

    [Fact]
    public void GetActiveScanCells_ShouldReturnOnlyOccupiedCells()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(7, 0, 7), scanCellSize: 2),
            out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];
        TestOccupant firstOccupant = new(new Vector3d(0, 0, 0));
        TestOccupant secondOccupant = new(new Vector3d(4, 0, 4));

        Assert.True(grid.TryAddVoxelOccupant(firstOccupant));
        Assert.True(grid.TryAddVoxelOccupant(secondOccupant));
        Assert.True(grid.TryGetScanCell(firstOccupant.Position, out ScanCell firstCell));
        Assert.True(grid.TryGetScanCell(secondOccupant.Position, out ScanCell secondCell));

        ScanCell[] activeCells = grid.GetActiveScanCells().ToArray();

        Assert.Equal(2, activeCells.Length);
        Assert.All(activeCells, scanCell => Assert.True(scanCell.IsOccupied));
        Assert.Contains(firstCell, activeCells);
        Assert.Contains(secondCell, activeCells);
        Assert.DoesNotContain(
            activeCells,
            scanCell => scanCell.CellKey == grid.GetScanCellKey(new Vector3d(2, 0, 2)));
    }

    [Theory]
    [MemberData(nameof(BoundaryDirectionCases))]
    public void IsFacingBoundaryDirection_ShouldMatchSpatialDirectionOffsets(
        SpatialDirection direction,
        VoxelIndex boundaryIndex)
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.IsFacingBoundaryDirection(boundaryIndex, direction));
    }

    [Fact]
    public void IsFacingBoundaryDirection_ShouldReturnFalseForCenterVoxel()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex centerIndex = new(1, 1, 1);

        foreach (SpatialDirection direction in SpatialAwareness.AllDirections)
            Assert.False(grid.IsFacingBoundaryDirection(centerIndex, direction));
    }

    [Fact]
    public void BoundaryQueries_ShouldRejectInvalidDirections()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 2, 2)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.False(grid.IsFacingBoundaryDirection(new VoxelIndex(0, 0, 0), (SpatialDirection)(-2)));

        grid.NotifyBoundaryChange((SpatialDirection)(-2));
        grid.NotifyBoundaryChange((SpatialDirection)999);
    }

    [Fact]
    public void NotifyBoundaryChange_ShouldSkipNullBoundarySlots()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        grid.Voxels[0, 0, 0] = null;

        grid.NotifyBoundaryChange(SpatialDirection.West);

        Assert.Null(grid.Voxels[0, 0, 0]);
    }

    [Fact]
    public void Grid_ShouldHandleComplexConnectionsDuringDynamicLoadAndUnload()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10)),
            out ushort centerIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(20, 0, 10)),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 10), new Vector3d(10, 0, 20)),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(20, 0, 20)),
            out ushort northEastIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];

        Assert.Equal(3, centerGrid.NeighborCount);
        Assert.Equal(3, centerGrid.GetAllGridNeighbors().Select(grid => grid.GridIndex).Distinct().Count());

        Assert.True(_world.TryRemoveGrid(northEastIndex));

        Assert.Equal(2, centerGrid.NeighborCount);
        Assert.Equal(2, centerGrid.GetAllGridNeighbors().Select(grid => grid.GridIndex).Distinct().Count());
    }

    [Fact]
    public void ReleasedGridAndScanCell_ShouldNotLeakStateWhenReused()
    {
        GridConfiguration centerConfig = new(
            new Vector3d(0, 0, 0),
            new Vector3d(1, 0, 1),
            scanCellSize: 8);
        GridConfiguration eastConfig = new(
            new Vector3d(1, 0, 0),
            new Vector3d(2, 0, 1),
            scanCellSize: 8);
        TestOccupant occupant = new(new Vector3d(0, 0, 0), 4);
        BoundsKey obstacleToken = new(new Vector3d(1, 0, 1), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(centerConfig, out ushort centerIndex));
        Assert.True(_world.TryAddGrid(eastConfig, out ushort eastIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];

        Assert.True(centerGrid.TryAddVoxelOccupant(occupant));
        Assert.True(centerGrid.TryAddObstacle(new Vector3d(1, 0, 1), obstacleToken));
        Assert.True(centerGrid.IsConjoined);
        Assert.True(centerGrid.IsOccupied);
        Assert.Equal(1, centerGrid.ObstacleCount);

        Assert.True(_world.TryRemoveGrid(eastIndex));
        Assert.True(_world.TryRemoveGrid(centerIndex));
        Assert.Empty(GridOccupantManager.GetOccupiedIndices(_world, occupant));

        Assert.True(_world.TryAddGrid(centerConfig, out ushort reusedIndex));

        VoxelGrid reusedGrid = _world.ActiveGrids[reusedIndex];

        Assert.False(reusedGrid.IsConjoined);
        Assert.Equal(0, reusedGrid.NeighborCount);
        Assert.False(reusedGrid.IsOccupied);
        Assert.Equal(0, reusedGrid.ObstacleCount);
        Assert.Empty(reusedGrid.GetAllGridNeighbors());
        Assert.Empty(reusedGrid.GetActiveScanCells());
        Assert.True(reusedGrid.TryGetScanCell(new Vector3d(0, 0, 0), out ScanCell reusedScanCell));
        Assert.False(reusedScanCell.IsOccupied);
        Assert.Equal(0, reusedScanCell.CellOccupantCount);
        Assert.Empty(reusedGrid.GetOccupants(new Vector3d(0, 0, 0)));
    }

    [Fact]
    public void ReleasedGridQueries_ShouldFailGracefullyWhenGridIsInactive()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(_world.TryRemoveGrid(gridIndex));
        Assert.False(grid.TryGetVoxelIndex(new Vector3d(1, 0, 1), out _));
        Assert.False(grid.TryGetVoxel(0, 0, 0, out _));
        Assert.False(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out _));
    }

    [Fact]
    public void TryGetVoxelIndex_ShouldUseOwningWorldVoxelSizeEvenWhenOtherWorldsDiffer()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        using GridWorld fractionalWorld = GridWorldTestFactory.CreateWorld((Fixed64)0.5);

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelGrid fractionalGrid = GridWorldTestFactory.AddGrid(fractionalWorld, config);

        Assert.True(grid.TryGetVoxelIndex(new Vector3d(1, 0, 1), out VoxelIndex resolvedIndex));
        Assert.Equal(new VoxelIndex(1, 0, 1), resolvedIndex);
        Assert.True(fractionalGrid.TryGetVoxelIndex(new Vector3d(1, 0, 1), out VoxelIndex fractionalIndex));
        Assert.Equal(new VoxelIndex(2, 0, 2), fractionalIndex);
    }

    [Fact]
    public void ClearPools_ShouldDropReleasedScanCellMaps()
    {
        GridConfiguration config = new(
            new Vector3d(0, 0, 0),
            new Vector3d(3, 0, 3),
            scanCellSize: 2);

        Assert.True(_world.TryAddGrid(config, out ushort firstIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstIndex];
        object firstScanCellMap = firstGrid.ScanCells;

        Assert.True(_world.TryRemoveGrid(firstIndex));

        Type poolsType = typeof(GridWorld).Assembly.GetType("GridForge.Grids.Pools");
        Assert.NotNull(poolsType);

        MethodInfo clearPools = poolsType.GetMethod("ClearPools", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(clearPools);
        clearPools.Invoke(null, null);

        Assert.True(_world.TryAddGrid(config, out ushort secondIndex));
        VoxelGrid secondGrid = _world.ActiveGrids[secondIndex];

        Assert.NotSame(firstScanCellMap, secondGrid.ScanCells);
    }

    [Fact]
    public void GetNeighborDirection_ShouldFollowCardinalAxisOffsets()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort centerIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(2, 0, 1)),
            out ushort eastIndex));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 1), new Vector3d(1, 0, 2)),
            out ushort northIndex));

        VoxelGrid centerGrid = _world.ActiveGrids[centerIndex];
        VoxelGrid eastGrid = _world.ActiveGrids[eastIndex];
        VoxelGrid northGrid = _world.ActiveGrids[northIndex];

        Assert.Equal(SpatialDirection.East, VoxelGrid.GetNeighborDirection(centerGrid, eastGrid));
        Assert.Equal(SpatialDirection.North, VoxelGrid.GetNeighborDirection(centerGrid, northGrid));
    }

    [Fact]
    public void ClearPools_ShouldAllowFreshGridAllocationAfterPoolReset()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        Assert.True(_world.TryRemoveGrid(gridIndex));

        Type poolsType = typeof(VoxelGrid).Assembly.GetType("GridForge.Grids.Pools");
        MethodInfo clearPools = poolsType?.GetMethod(
            "ClearPools",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(clearPools);
        clearPools.Invoke(null, null);

        Assert.True(_world.TryAddGrid(config, out ushort reusedIndex));
        VoxelGrid reusedGrid = _world.ActiveGrids[reusedIndex];

        Assert.True(reusedGrid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
        Assert.NotNull(voxel);
    }

    [Fact]
    public void GridConfigurationAndBoundsKey_ShouldProvideStableIdentityForMatchingBounds()
    {
        GridConfiguration first = new(
            new Vector3d(0, 0, 0),
            new Vector3d(5, 0, 5),
            scanCellSize: 2);
        GridConfiguration second = new(
            new Vector3d(0, 0, 0),
            new Vector3d(5, 0, 5),
            scanCellSize: 16);
        BoundsKey firstKey = first.ToBoundsKey();
        BoundsKey secondKey = second.ToBoundsKey();
        BoundsKey differentKey = new(new Vector3d(1, 0, 1), new Vector3d(5, 0, 5));

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(first.BoundsMin, firstKey.BoundsMin);
        Assert.Equal(first.BoundsMax, firstKey.BoundsMax);
        Assert.Equal(firstKey, secondKey);
        Assert.Equal(firstKey.GetHashCode(), secondKey.GetHashCode());
        Assert.NotEqual(firstKey, differentKey);
    }

    private static TheoryData<SpatialDirection, VoxelIndex> CreateBoundaryDirectionCases()
    {
        TheoryData<SpatialDirection, VoxelIndex> cases = new();

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            (int x, int y, int z) offset = SpatialAwareness.DirectionOffsets[i];
            VoxelIndex boundaryIndex = new(
                offset.x < 0 ? 0 : offset.x > 0 ? 2 : 1,
                offset.y < 0 ? 0 : offset.y > 0 ? 2 : 1,
                offset.z < 0 ? 0 : offset.z > 0 ? 2 : 1);

            cases.Add((SpatialDirection)i, boundaryIndex);
        }

        return cases;
    }

    private static VoxelGrid CreateStandaloneGrid(ushort globalIndex, Vector3d min, Vector3d max)
    {
        GridWorld world = GridWorldTestFactory.CreateWorld();
        VoxelGrid grid = new();
        InvokeGridInitialize(grid, world, globalIndex, new GridConfiguration(min, max));
        return grid;
    }

    private static void InvokeGridInitialize(VoxelGrid grid, GridWorld world, ushort globalIndex, GridConfiguration configuration)
    {
        MethodInfo initializeMethod = typeof(VoxelGrid).GetMethod(
            "Initialize",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(GridWorld), typeof(ushort), typeof(GridConfiguration) },
            null);

        Assert.NotNull(initializeMethod);
        initializeMethod.Invoke(grid, new object[] { world, globalIndex, configuration });
    }

    private static void InvokeGridReset(VoxelGrid grid)
    {
        MethodInfo resetMethod = typeof(VoxelGrid).GetMethod(
            "Reset",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resetMethod);
        resetMethod.Invoke(grid, null);
    }

    private static bool InvokeTryAddGridNeighbor(VoxelGrid grid, VoxelGrid neighborGrid)
    {
        MethodInfo addNeighborMethod = typeof(VoxelGrid).GetMethod(
            "TryAddGridNeighbor",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(addNeighborMethod);
        return (bool)addNeighborMethod.Invoke(grid, new object[] { neighborGrid });
    }

    private static bool InvokeTryRemoveGridNeighbor(VoxelGrid grid, VoxelGrid neighborGrid)
    {
        MethodInfo removeNeighborMethod = typeof(VoxelGrid).GetMethod(
            "TryRemoveGridNeighbor",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(removeNeighborMethod);
        return (bool)removeNeighborMethod.Invoke(grid, new object[] { neighborGrid });
    }

    private static uint InvokeIncrementVersion(VoxelGrid grid)
    {
        MethodInfo incrementMethod = typeof(VoxelGrid).GetMethod(
            "IncrementVersion",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(incrementMethod);
        return (uint)incrementMethod.Invoke(grid, null);
    }

    private static void SetGridVersion(VoxelGrid grid, uint version)
    {
        FieldInfo versionField = typeof(VoxelGrid).GetField(
            "<Version>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(versionField);
        versionField.SetValue(grid, version);
    }

    private void ResetWorld(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        _world.Dispose();
        _world = GridWorldTestFactory.CreateWorld(voxelSize, spatialGridCellSize);
    }
}
