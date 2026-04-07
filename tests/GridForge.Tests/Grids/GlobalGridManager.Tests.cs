using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class GlobalGridManagerTests : IDisposable
{
    public GlobalGridManagerTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        GlobalGridManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Setup_ShouldInitializeCollections()
    {
        GlobalGridManager.Setup();

        Assert.NotNull(GlobalGridManager.ActiveGrids);
        Assert.NotNull(GlobalGridManager.SpatialGridHash);
    }

    [Fact]
    public void Setup_ShouldIgnoreDuplicateSetupCallsAndFallbackInvalidVoxelSize()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup((Fixed64)(-2), spatialGridCellSize: 25);

        try
        {
            SwiftBucket<VoxelGrid> activeGrids = GlobalGridManager.ActiveGrids;

            Assert.Equal(GlobalGridManager.DefaultVoxelSize, GlobalGridManager.VoxelSize);
            Assert.Equal(25, GlobalGridManager.SpatialGridCellSize);

            GlobalGridManager.Setup((Fixed64)3, spatialGridCellSize: 99);

            Assert.Same(activeGrids, GlobalGridManager.ActiveGrids);
            Assert.Equal(GlobalGridManager.DefaultVoxelSize, GlobalGridManager.VoxelSize);
            Assert.Equal(25, GlobalGridManager.SpatialGridCellSize);
            Assert.True(GlobalGridManager.IsActive);
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void Reset_ShouldClearAllGrids()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out _);

        GlobalGridManager.Reset();

        Assert.Empty(GlobalGridManager.ActiveGrids);
        Assert.Empty(GlobalGridManager.SpatialGridHash);
    }

    [Fact]
    public void Reset_ShouldReturnEarlyWhenInactive()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Reset();

        Assert.False(GlobalGridManager.IsActive);
        Assert.False(GlobalGridManager.TryGetGrid(0, out _));

        GlobalGridManager.Setup();
    }

    [Fact]
    public void AddGrid_ShouldSucceedWithValidConfiguration()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));

        var result = GlobalGridManager.TryAddGrid(config, out ushort index);
        Assert.True(result == true || index != ushort.MaxValue);
        Assert.True(GlobalGridManager.ActiveGrids.Count > 0);
    }

    [Fact]
    public void RemoveGrid_ShouldRemoveGridSuccessfully()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out ushort index);

        bool removed = GlobalGridManager.TryRemoveGrid(index);

        Assert.True(removed);
    }

    [Fact]
    public void GridConfiguration_ShouldCorrectInvalidBounds()
    {
        var invalidConfig = new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(-10, 0, -10));

        bool result = GlobalGridManager.TryAddGrid(invalidConfig, out ushort index);
        Assert.True(result == true || index != ushort.MaxValue);
    }

    [Fact]
    public void GetGrid_ShouldReturnCorrectGrid()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out _);

        Assert.True(GlobalGridManager.TryGetGrid(new Vector3d(0, 0, 0), out VoxelGrid grid));
        Assert.NotNull(grid);
    }

    [Fact]
    public void GetGridAndVoxel_ShouldReturnCorrectVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out _);

        Assert.True(GlobalGridManager.TryGetGridAndVoxel(new Vector3d(0, 0, 0), out VoxelGrid grid, out Voxel voxel));
        Assert.NotNull(grid);
        Assert.NotNull(voxel);
    }

    [Fact]
    public void GetGrid_ShouldReturnFalseForOutOfBounds()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out _);

        bool found = GlobalGridManager.TryGetGrid(new Vector3d(100, 0, 100), out VoxelGrid grid);

        Assert.False(found);
        Assert.Null(grid);
    }

    [Fact]
    public void GetGridAndVoxel_ShouldReturnFalseForInvalidVoxel()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out _);

        bool found = GlobalGridManager.TryGetGridAndVoxel(new Vector3d(100, 0, 100), out VoxelGrid grid, out Voxel voxel);

        Assert.False(found);
        Assert.Null(grid);
        Assert.Null(voxel);
    }

    [Fact]
    public void FindOverlappingGrids_ShouldReturnCorrectResults()
    {
        var config1 = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        var config2 = new GridConfiguration(new Vector3d(5, 0, 5), new Vector3d(15, 0, 15));

        GlobalGridManager.TryAddGrid(config1, out ushort index1);
        GlobalGridManager.TryAddGrid(config2, out _);

        VoxelGrid targetGrid = GlobalGridManager.ActiveGrids[index1];
        IEnumerable<VoxelGrid> overlaps = GlobalGridManager.FindOverlappingGrids(targetGrid);

        Assert.Single(overlaps);
    }

    [Fact]
    public void FindOverlappingGrids_ShouldReturnEmptyWhenNoOverlap()
    {
        var config1 = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));
        var config2 = new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(20, 0, 20));

        GlobalGridManager.TryAddGrid(config1, out ushort index1);
        GlobalGridManager.TryAddGrid(config2, out _);

        VoxelGrid targetGrid = GlobalGridManager.ActiveGrids[index1];
        IEnumerable<VoxelGrid> overlaps = GlobalGridManager.FindOverlappingGrids(targetGrid);

        Assert.Empty(overlaps);
    }

    [Fact]
    public void AddGrid_ShouldRejectDuplicateBounds()
    {
        var config = new GridConfiguration(new Vector3d(-1, -1, -1), new Vector3d(2, 2, 2));

        Assert.True(GlobalGridManager.TryAddGrid(config, out ushort firstIndex));
        Assert.NotEqual(ushort.MaxValue, firstIndex);

        bool duplicateAdded = GlobalGridManager.TryAddGrid(config, out ushort duplicateIndex);

        Assert.False(duplicateAdded);
        Assert.Equal(firstIndex, duplicateIndex);
        Assert.Single(GlobalGridManager.ActiveGrids);
    }

    [Fact]
    public void AddGrid_ShouldTreatSignedBoundsAsDistinct()
    {
        var negativeMinConfig = new GridConfiguration(new Vector3d(-1, -1, -1), new Vector3d(2, 2, 2));
        var positiveMinConfig = new GridConfiguration(new Vector3d(1, 1, 1), new Vector3d(2, 2, 2));

        bool firstAdded = GlobalGridManager.TryAddGrid(negativeMinConfig, out ushort firstIndex);
        bool secondAdded = GlobalGridManager.TryAddGrid(positiveMinConfig, out ushort secondIndex);

        Assert.True(firstAdded);
        Assert.True(secondAdded);
        Assert.NotEqual(firstIndex, secondIndex);
        Assert.Equal(2, GlobalGridManager.ActiveGrids.Count);
    }

    [Fact]
    public void AddGrid_ShouldMapAllocatedIndexAcrossActiveGridsAndSpatialHash()
    {
        var config = new GridConfiguration(new Vector3d(-75, 0, -75), new Vector3d(75, 0, 75));

        Assert.True(GlobalGridManager.TryAddGrid(config, out ushort index));

        VoxelGrid grid = GlobalGridManager.ActiveGrids[index];

        Assert.Equal(index, grid.GlobalIndex);

        foreach (int cellIndex in GlobalGridManager.GetSpatialGridCells(grid.BoundsMin, grid.BoundsMax))
        {
            Assert.True(GlobalGridManager.SpatialGridHash.TryGetValue(cellIndex, out var gridIndices));
            Assert.Contains(index, gridIndices);
        }
    }

    [Fact]
    public void GetNeighborDirectionFromOffset_ShouldMatchSpatialAwarenessOffsets()
    {
        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            SpatialDirection expectedDirection = (SpatialDirection)i;
            SpatialDirection actualDirection =
                GlobalGridManager.GetNeighborDirectionFromOffset(SpatialAwareness.DirectionOffsets[i]);

            Assert.Equal(expectedDirection, actualDirection);
        }
    }

    [Fact]
    public void TryGetGrid_ShouldReflectDynamicLoadAndUnload()
    {
        var staticConfig = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        var dynamicConfig = new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(30, 0, 30));
        Vector3d dynamicPosition = new Vector3d(25, 0, 25);

        Assert.True(GlobalGridManager.TryAddGrid(staticConfig, out _));
        Assert.False(GlobalGridManager.TryGetGrid(dynamicPosition, out _));

        Assert.True(GlobalGridManager.TryAddGrid(dynamicConfig, out ushort dynamicIndex));
        Assert.True(GlobalGridManager.TryGetGrid(dynamicPosition, out VoxelGrid loadedGrid));
        Assert.Equal(dynamicIndex, loadedGrid.GlobalIndex);

        Assert.True(GlobalGridManager.TryRemoveGrid(dynamicIndex));
        Assert.False(GlobalGridManager.TryGetGrid(dynamicPosition, out _));
    }

    [Fact]
    public void TryAddGrid_ShouldRejectWhenGridCountExceedsMaximum()
    {
        try
        {
            for (int i = 0; i < checked((int)GlobalGridManager.MaxGrids) + 1; i++)
                GlobalGridManager.ActiveGrids.Add(new VoxelGrid());

            bool added = GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1)),
                out ushort allocatedIndex);

            Assert.False(added);
            Assert.Equal(ushort.MaxValue, allocatedIndex);
        }
        finally
        {
            GlobalGridManager.ActiveGrids.Clear();
        }
    }

    [Fact]
    public void Reset_ShouldDeactivateWhenRequested()
    {
        GlobalGridManager.Reset(deactivate: true);

        Assert.False(GlobalGridManager.IsActive);
        Assert.False(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out _));

        GlobalGridManager.Setup();
        Assert.True(GlobalGridManager.IsActive);
    }

    [Fact]
    public void Setup_ShouldPreserveCustomVoxelSizeAboveOneAndUpdateResolution()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup((Fixed64)4);

        try
        {
            Assert.Equal((Fixed64)4, GlobalGridManager.VoxelSize);
            Assert.Equal((Fixed64)2, GlobalGridManager.VoxelResolution);
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Setup_ShouldFallbackToDefaultSpatialGridCellSizeWhenConfiguredValueIsNotPositive(int invalidSpatialGridCellSize)
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: invalidSpatialGridCellSize);

        try
        {
            Assert.Equal(GlobalGridManager.DefaultSpatialGridCellSize, GlobalGridManager.SpatialGridCellSize);
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(-2, 0, -2), new Vector3d(2, 0, 2)),
                out _));
            Assert.True(GlobalGridManager.TryGetGrid(new Vector3d(0, 0, 0), out _));
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void IncrementGridVersion_ShouldUpdateGridAndGlobalVersion()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-2, 0, -2), new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
        uint initialGridVersion = grid.Version;
        uint initialManagerVersion = GlobalGridManager.Version;

        GlobalGridManager.IncrementGridVersion(gridIndex);
        Assert.Equal(initialGridVersion + 1, grid.Version);
        Assert.Equal(initialManagerVersion, GlobalGridManager.Version);

        GlobalGridManager.IncrementGridVersion(gridIndex, significant: true);
        Assert.Equal(initialGridVersion + 2, grid.Version);
        Assert.Equal(initialManagerVersion + 1, GlobalGridManager.Version);
    }

    [Fact]
    public void TryGetGridOverloads_ShouldHandleOutOfRangeUnallocatedAndSpawnMismatchCases()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
            out ushort firstIndex));
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 0, 11)),
            out ushort secondIndex));

        Assert.True(GlobalGridManager.TryRemoveGrid(firstIndex));
        Assert.False(GlobalGridManager.TryRemoveGrid(firstIndex));
        Assert.False(GlobalGridManager.TryGetGrid(firstIndex, out VoxelGrid unallocatedGrid));
        Assert.Null(unallocatedGrid);
        Assert.False(GlobalGridManager.TryGetGrid(GlobalGridManager.ActiveGrids.Count + 10, out VoxelGrid outOfRangeGrid));
        Assert.Null(outOfRangeGrid);

        VoxelGrid secondGrid = GlobalGridManager.ActiveGrids[secondIndex];
        Assert.True(secondGrid.TryGetVoxel(new Vector3d(10, 0, 10), out Voxel voxel));

        GlobalVoxelIndex wrongSpawnIndex = new GlobalVoxelIndex(
            secondIndex,
            voxel.Index,
            voxel.GlobalIndex.GridSpawnToken + 1);

        Assert.False(GlobalGridManager.TryGetGrid(wrongSpawnIndex, out VoxelGrid mismatchedGrid));
        Assert.Same(secondGrid, mismatchedGrid);
        Assert.False(GlobalGridManager.TryGetGridAndVoxel(wrongSpawnIndex, out VoxelGrid mismatchedResolvedGrid, out Voxel mismatchedResolvedVoxel));
        Assert.Same(secondGrid, mismatchedResolvedGrid);
        Assert.Null(mismatchedResolvedVoxel);
        Assert.False(GlobalGridManager.TryGetVoxel(wrongSpawnIndex, out Voxel mismatchedDirectVoxel));
        Assert.Null(mismatchedDirectVoxel);
    }

    [Fact]
    public void TryGetGrid_ShouldIgnoreStaleSpatialHashEntriesAndReportMissesForOutOfBoundsCandidates()
    {
        GridConfiguration config = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(GlobalGridManager.TryAddGrid(config, out ushort gridIndex));
        Assert.False(GlobalGridManager.TryGetGrid(new Vector3d(40, 0, 40), out VoxelGrid outsideBoundsGrid));
        Assert.Null(outsideBoundsGrid);

        int cellKey = GlobalGridManager.GetSpatialGridKey(new Vector3d(0, 0, 0));

        Assert.True(GlobalGridManager.TryRemoveGrid(gridIndex));

        if (!GlobalGridManager.SpatialGridHash.ContainsKey(cellKey))
            GlobalGridManager.SpatialGridHash.Add(cellKey, new SwiftHashSet<ushort>());

        GlobalGridManager.SpatialGridHash[cellKey].Add(gridIndex);

        Assert.False(GlobalGridManager.TryGetGrid(new Vector3d(0, 0, 0), out VoxelGrid staleGrid));
        Assert.Null(staleGrid);
    }

    [Fact]
    public void TryAddGrid_ShouldIgnoreStaleSpatialHashEntriesWhenLinkingNeighbors()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 100);

        try
        {
            GridConfiguration staleConfig = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));
            GridConfiguration anchorConfig = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10));
            GridConfiguration newConfig = new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(20, 0, 10));
            int cellKey = GlobalGridManager.GetSpatialGridKey(new Vector3d(0, 0, 0));

            Assert.True(GlobalGridManager.TryAddGrid(staleConfig, out ushort staleIndex));
            Assert.True(GlobalGridManager.TryRemoveGrid(staleIndex));
            if (!GlobalGridManager.SpatialGridHash.ContainsKey(cellKey))
                GlobalGridManager.SpatialGridHash.Add(cellKey, new SwiftHashSet<ushort>());
            GlobalGridManager.SpatialGridHash[cellKey].Add(staleIndex);

            Assert.True(GlobalGridManager.TryAddGrid(anchorConfig, out ushort anchorIndex));
            Assert.True(GlobalGridManager.TryAddGrid(newConfig, out ushort newIndex));

            VoxelGrid anchorGrid = GlobalGridManager.ActiveGrids[anchorIndex];
            VoxelGrid newGrid = GlobalGridManager.ActiveGrids[newIndex];

            Assert.True(anchorGrid.IsConjoined);
            Assert.True(newGrid.IsConjoined);
            Assert.Contains(newIndex, anchorGrid.GetAllGridNeighbors().Select(grid => grid.GlobalIndex));
            Assert.Contains(anchorIndex, newGrid.GetAllGridNeighbors().Select(grid => grid.GlobalIndex));
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void TryRemoveGrid_ShouldHandleMissingHashCellsAndSkipStaleOrNonOverlappingNeighbors()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 100);

        try
        {
            GridConfiguration removableConfig = new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10));
            GridConfiguration adjacentConfig = new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(20, 0, 10));
            GridConfiguration farConfig = new GridConfiguration(new Vector3d(40, 0, 40), new Vector3d(50, 0, 50));
            GridConfiguration staleConfig = new GridConfiguration(new Vector3d(-30, 0, -30), new Vector3d(-20, 0, -20));

            Assert.True(GlobalGridManager.TryAddGrid(removableConfig, out ushort removableIndex));
            Assert.True(GlobalGridManager.TryAddGrid(adjacentConfig, out ushort adjacentIndex));
            Assert.True(GlobalGridManager.TryAddGrid(farConfig, out _));
            Assert.True(GlobalGridManager.TryAddGrid(staleConfig, out ushort staleIndex));

            VoxelGrid removableGrid = GlobalGridManager.ActiveGrids[removableIndex];
            int removableCellKey = GlobalGridManager.GetSpatialGridKey(removableGrid.BoundsMin);

            Assert.True(GlobalGridManager.TryRemoveGrid(staleIndex));
            GlobalGridManager.SpatialGridHash[removableCellKey].Add(staleIndex);
            GlobalGridManager.SpatialGridHash.Remove(GlobalGridManager.GetSpatialGridKey(new Vector3d(90, 0, 90)));
            GlobalGridManager.SpatialGridHash.Remove(removableCellKey);
            GlobalGridManager.SpatialGridHash.Add(removableCellKey, new SwiftHashSet<ushort> { adjacentIndex, staleIndex });

            Assert.True(GlobalGridManager.TryRemoveGrid(removableIndex));
            Assert.True(GlobalGridManager.TryGetGrid(adjacentIndex, out VoxelGrid adjacentGrid));
            Assert.DoesNotContain(removableIndex, adjacentGrid.GetAllGridNeighbors().Select(grid => grid.GlobalIndex));
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void TryGetGridAndVoxel_ShouldSupportGlobalVoxelIndexOverloads()
    {
        Assert.True(GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2)),
            out ushort gridIndex));
        VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];

        Assert.True(grid.TryGetVoxel(new Vector3d(1, 0, 1), out Voxel voxel));
        Assert.True(GlobalGridManager.TryGetGridAndVoxel(voxel.GlobalIndex, out VoxelGrid resolvedGrid, out Voxel resolvedVoxel));
        Assert.True(GlobalGridManager.TryGetVoxel(voxel.GlobalIndex, out Voxel directVoxel));
        Assert.Same(grid, resolvedGrid);
        Assert.Same(voxel, resolvedVoxel);
        Assert.Same(voxel, directVoxel);
    }

    [Fact]
    public void FindOverlappingGrids_ShouldIgnoreNonOverlappingCandidatesWithinSameSpatialCellAndWhenInactive()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup(spatialGridCellSize: 100);

        try
        {
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(10, 0, 10)),
                out ushort firstIndex));
            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(40, 0, 40), new Vector3d(50, 0, 50)),
                out _));

            VoxelGrid firstGrid = GlobalGridManager.ActiveGrids[firstIndex];

            Assert.Empty(GlobalGridManager.FindOverlappingGrids(firstGrid));

            GlobalGridManager.Reset(deactivate: true);

            Assert.Empty(GlobalGridManager.FindOverlappingGrids(firstGrid));
        }
        finally
        {
            if (GlobalGridManager.IsActive)
                GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void UtilityHelpers_ShouldHandleNegativeAndInvertedBounds()
    {
        GlobalGridManager.Reset(deactivate: true);
        GlobalGridManager.Setup((Fixed64)2, spatialGridCellSize: 10);

        try
        {
            int negativeKey = GlobalGridManager.GetSpatialGridKey(new Vector3d(-12, 0, -12));
            int sameNegativeCellKey = GlobalGridManager.GetSpatialGridKey(new Vector3d(-19, 0, -19));
            int positiveKey = GlobalGridManager.GetSpatialGridKey(new Vector3d(12, 0, 12));
            int[] invertedBoundsCells = GlobalGridManager.GetSpatialGridCells(
                new Vector3d(12, 0, 12),
                new Vector3d(-12, 0, -12))
                .Distinct()
                .ToArray();
            (Vector3d snappedMin, Vector3d snappedMax) = GlobalGridManager.SnapBoundsToVoxelSize(
                new Vector3d(3, 5, 7),
                new Vector3d(-3, -5, -7),
                padding: (Fixed64)1);

            Assert.Equal(SpatialDirection.None, GlobalGridManager.GetNeighborDirectionFromOffset((0, 0, 0)));
            Assert.Equal(negativeKey, sameNegativeCellKey);
            Assert.NotEqual(negativeKey, positiveKey);
            Assert.Equal(9, invertedBoundsCells.Length);
            Assert.Equal(new Vector3d(-4, 6, -8), GlobalGridManager.CeilToVoxelSize(new Vector3d(-3, 5, -7)));
            Assert.Equal(new Vector3d(-2, 4, -6), GlobalGridManager.FloorToVoxelSize(new Vector3d(-3, 5, -7)));
            Assert.Equal(new Vector3d(-2, -4, -6), snappedMin);
            Assert.Equal(new Vector3d(2, 4, 6), snappedMax);
        }
        finally
        {
            GlobalGridManager.Reset(deactivate: true);
            GlobalGridManager.Setup();
        }
    }

    [Fact]
    public void ChangeNotificationsAndInactiveGuards_ShouldHandleInternalEdgeCases()
    {
        Action<GridEventInfo> throwingGridChangeHandler = (_) => throw new InvalidOperationException("grid change");

        try
        {
            GlobalGridManager.OnActiveGridChange += throwingGridChangeHandler;

            InvokeNotifyActiveGridChange(null);

            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
                out ushort gridIndex));
            VoxelGrid activeGrid = GlobalGridManager.ActiveGrids[gridIndex];

            InvokeNotifyActiveGridChange(activeGrid);

            Assert.True(GlobalGridManager.TryRemoveGrid(gridIndex));
            InvokeNotifyActiveGridChange(activeGrid);

            GlobalGridManager.Reset(deactivate: true);

            GlobalGridManager.IncrementGridVersion(0);
            Assert.False(GlobalGridManager.TryGetGrid(new Vector3d(0, 0, 0), out _));

            GlobalGridManager.Setup();
        }
        finally
        {
            GlobalGridManager.OnActiveGridChange -= throwingGridChangeHandler;
        }
    }

    [Fact]
    public void GridNotifications_ShouldSwallowSubscriberExceptions()
    {
        Action<GridEventInfo> gridAddedHandler = (_) => throw new InvalidOperationException("grid added");
        Action<GridEventInfo> gridRemovedHandler = (_) => throw new InvalidOperationException("grid removed");
        Action<GridEventInfo> gridChangeHandler = (_) => throw new InvalidOperationException("grid change");
        static void resetHandler() => throw new InvalidOperationException("reset");

        try
        {
            GlobalGridManager.OnActiveGridAdded += gridAddedHandler;
            GlobalGridManager.OnActiveGridRemoved += gridRemovedHandler;
            GlobalGridManager.OnActiveGridChange += gridChangeHandler;
            GlobalGridManager.OnReset += resetHandler;

            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
                out ushort gridIndex));
            VoxelGrid grid = GlobalGridManager.ActiveGrids[gridIndex];
            Assert.True(grid.TryGetVoxel(new Vector3d(0, 0, 0), out Voxel voxel));
            Assert.True(grid.TryAddObstacle(voxel, new BoundsKey(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0))));
            Assert.True(GlobalGridManager.TryRemoveGrid(gridIndex));

            GlobalGridManager.Setup();
            GlobalGridManager.Reset();
            Assert.True(GlobalGridManager.IsActive);
        }
        finally
        {
            GlobalGridManager.OnActiveGridAdded -= gridAddedHandler;
            GlobalGridManager.OnActiveGridRemoved -= gridRemovedHandler;
            GlobalGridManager.OnActiveGridChange -= gridChangeHandler;
            GlobalGridManager.OnReset -= resetHandler;
        }
    }

    [Fact]
    public void RemoveGrid_ShouldPublishRemovedGridSnapshotAfterRelease()
    {
        GridConfiguration config = new GridConfiguration(new Vector3d(-2, 0, -2), new Vector3d(2, 0, 2));
        Assert.True(GlobalGridManager.TryAddGrid(config, out ushort gridIndex));

        GridEventInfo removedEvent = default;
        bool notified = false;

        void RemovedHandler(GridEventInfo eventInfo)
        {
            notified = true;
            removedEvent = eventInfo;

            Assert.False(GlobalGridManager.TryGetGrid(eventInfo.GridIndex, out VoxelGrid removedGrid));
            Assert.Null(removedGrid);
        }

        GlobalGridManager.OnActiveGridRemoved += RemovedHandler;

        try
        {
            Assert.True(GlobalGridManager.TryRemoveGrid(gridIndex));
        }
        finally
        {
            GlobalGridManager.OnActiveGridRemoved -= RemovedHandler;
        }

        Assert.True(notified);
        Assert.Equal(gridIndex, removedEvent.GridIndex);
        Assert.Equal(config.BoundsMin, removedEvent.BoundsMin);
        Assert.Equal(config.BoundsMax, removedEvent.BoundsMax);
        Assert.Equal(config.ScanCellSize, removedEvent.Configuration.ScanCellSize);
    }

    private static void InvokeNotifyActiveGridChange(VoxelGrid grid)
    {
        var notifyMethod = typeof(GlobalGridManager).GetMethod(
            "NotifyActiveGridChange",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null,
            new[] { typeof(VoxelGrid) },
            null);

        Assert.NotNull(notifyMethod);
        notifyMethod.Invoke(null, new object[] { grid });
    }
}
