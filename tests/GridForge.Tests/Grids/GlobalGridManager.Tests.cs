using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using System;
using System.Collections.Generic;
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
    public void Reset_ShouldClearAllGrids()
    {
        var config = new GridConfiguration(new Vector3d(-10, 0, -10), new Vector3d(10, 0, 10));
        GlobalGridManager.TryAddGrid(config, out _);

        GlobalGridManager.Reset();

        Assert.Empty(GlobalGridManager.ActiveGrids);
        Assert.Empty(GlobalGridManager.SpatialGridHash);
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
    public void GridNotifications_ShouldSwallowSubscriberExceptions()
    {
        Action<GridChange, uint> originalGridChange = GlobalGridManager.OnActiveGridChange;
        Action originalReset = GlobalGridManager.OnReset;

        try
        {
            GlobalGridManager.OnActiveGridChange = (_, _) => throw new InvalidOperationException("grid change");
            GlobalGridManager.OnReset = () => throw new InvalidOperationException("reset");

            Assert.True(GlobalGridManager.TryAddGrid(
                new GridConfiguration(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1)),
                out ushort gridIndex));
            Assert.True(GlobalGridManager.TryRemoveGrid(gridIndex));

            GlobalGridManager.Setup();
            GlobalGridManager.Reset();
            Assert.True(GlobalGridManager.IsActive);
        }
        finally
        {
            GlobalGridManager.OnActiveGridChange = originalGridChange;
            GlobalGridManager.OnReset = originalReset;
        }
    }
}
