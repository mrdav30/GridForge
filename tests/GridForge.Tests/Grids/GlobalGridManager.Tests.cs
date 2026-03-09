using FixedMathSharp;
using GridForge.Configuration;
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
}
