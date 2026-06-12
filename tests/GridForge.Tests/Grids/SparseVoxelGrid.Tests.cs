//=======================================================================
// SparseVoxelGrid.Tests.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using System;
using System.Linq;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class SparseVoxelGridTests : IDisposable
{
    private readonly GridWorld _world;

    public SparseVoxelGridTests()
    {
        _world = GridWorldTestFactory.CreateWorld();
    }

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SparseGrid_ShouldCreateConfiguredVoxelsOnly()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), scanCellSize: 2);
        VoxelIndex[] configured =
        {
            new(0, 0, 0),
            new(1, 0, 1),
            new(2, 0, 2)
        };

        Assert.True(_world.TryAddGrid(config, configured, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.Equal(GridStorageKind.Sparse, grid.StorageKind);
        Assert.Equal(9, grid.Size);
        Assert.Equal(3, grid.ConfiguredVoxelCount);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(2, 0, 2), out Voxel maxVoxel));
        Assert.Equal(new Vector3d(2, 0, 2), maxVoxel.WorldPosition);
        Assert.True(grid.TryGetScanCell(new VoxelIndex(2, 0, 2), out ScanCell maxScanCell));
        Assert.Equal(grid.GetScanCellKey(new VoxelIndex(2, 0, 2)), maxScanCell.CellKey);

        Assert.False(grid.TryGetVoxel(new VoxelIndex(0, 0, 2), out _));
        Assert.False(grid.IsVoxelAllocated(0, 0, 2));
        Assert.False(grid.TryGetScanCell(new Vector3d(0, 0, 2), out _));

        Assert.True(_world.TryGetGrid(new Vector3d(0, 0, 2), out VoxelGrid resolvedGrid));
        Assert.Same(grid, resolvedGrid);
        Assert.False(_world.TryGetGridAndVoxel(new Vector3d(0, 0, 2), out VoxelGrid missingGrid, out Voxel missingVoxel));
        Assert.Same(grid, missingGrid);
        Assert.Null(missingVoxel);

        Assert.True(_world.TryGetGridAndVoxel(new Vector3d(1, 0, 1), out VoxelGrid configuredGrid, out Voxel configuredVoxel));
        Assert.Same(grid, configuredGrid);
        Assert.Equal(new VoxelIndex(1, 0, 1), configuredVoxel.Index);
    }

    [Fact]
    public void SparseGrid_ShouldDeduplicateAndEnumerateConfiguredVoxelsDeterministically()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2));
        VoxelIndex[] configured =
        {
            new(2, 0, 2),
            new(0, 0, 1),
            new(2, 0, 2),
            new(1, 0, 0)
        };

        Assert.True(_world.TryAddGrid(config, configured, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        VoxelIndex[] actual = grid
            .EnumerateVoxels()
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Equal(3, grid.ConfiguredVoxelCount);
        Assert.Equal(
            new[]
            {
                new VoxelIndex(0, 0, 1),
                new VoxelIndex(1, 0, 0),
                new VoxelIndex(2, 0, 2)
            },
            actual);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(2, 0, 0)]
    public void SparseGrid_ShouldRejectInvalidConfiguredIndices(int x, int y, int z)
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.False(_world.TryAddGrid(config, new[] { new VoxelIndex(x, y, z) }, out ushort gridIndex));
        Assert.Equal(ushort.MaxValue, gridIndex);
        Assert.Empty(_world.ActiveGrids);
    }

    [Fact]
    public void SparseGrid_ShouldAllowEmptySparseGridBoundsWithoutMaterializingVoxels()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.Equal(GridStorageKind.Sparse, grid.StorageKind);
        Assert.Equal(0, grid.ConfiguredVoxelCount);
        Assert.Empty(grid.EnumerateVoxels());
        Assert.True(_world.TryGetGrid(new Vector3d(1, 0, 1), out VoxelGrid resolvedGrid));
        Assert.Same(grid, resolvedGrid);
        Assert.False(grid.TryGetVoxel(new VoxelIndex(1, 0, 1), out _));
        Assert.False(_world.TryGetGridAndVoxel(new Vector3d(1, 0, 1), out _, out _));
        Assert.False(grid.TryGetScanCell(new Vector3d(1, 0, 1), out _));
    }

    [Fact]
    public void SparseGrid_ShouldCreateConfiguredVoxelsFromBooleanMask()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(10, 0, 10), new Vector3d(11, 0, 11));
        bool[,,] configured =
        {
            {
                { false, true }
            },
            {
                { true, false }
            }
        };

        Assert.True(_world.TryAddGrid(config, configured, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.Equal(2, grid.ConfiguredVoxelCount);
        Assert.True(grid.TryGetVoxel(new Vector3d(10, 0, 11), out Voxel firstVoxel));
        Assert.Equal(new VoxelIndex(0, 0, 1), firstVoxel.Index);
        Assert.True(grid.TryGetVoxel(new Vector3d(11, 0, 10), out Voxel secondVoxel));
        Assert.Equal(new VoxelIndex(1, 0, 0), secondVoxel.Index);
        Assert.False(grid.TryGetVoxel(new Vector3d(10, 0, 10), out _));
    }

    [Fact]
    public void SparseGrid_ShouldRejectBooleanMaskWithMismatchedDimensions()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        bool[,,] configured = new bool[1, 1, 1];

        Assert.False(_world.TryAddGrid(config, configured, out ushort gridIndex));
        Assert.Equal(ushort.MaxValue, gridIndex);
        Assert.Empty(_world.ActiveGrids);
    }

    [Fact]
    public void SparseGrid_ShouldRejectAddressSpacesThatOverflowCurrentSizeEnvelope()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(50_000, 0, 50_000));

        Assert.False(_world.TryAddGrid(config, out ushort gridIndex));
        Assert.Equal(ushort.MaxValue, gridIndex);
        Assert.Empty(_world.ActiveGrids);
    }

    [Fact]
    public void SparseGrid_ShouldReleaseConfiguredVoxelsAndReuseGridStorageCleanly()
    {
        GridConfiguration sparseConfig = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(sparseConfig, new[] { new VoxelIndex(0, 0, 0) }, out ushort sparseIndex));
        VoxelGrid sparseGrid = _world.ActiveGrids[sparseIndex];
        Assert.True(sparseGrid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel sparseVoxel));

        Assert.True(_world.TryRemoveGrid(sparseIndex));
        Assert.False(sparseVoxel.IsAllocated);

        GridConfiguration denseConfig = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        Assert.True(_world.TryAddGrid(denseConfig, out ushort denseIndex));
        VoxelGrid denseGrid = _world.ActiveGrids[denseIndex];

        Assert.Equal(GridStorageKind.Dense, denseGrid.StorageKind);
        Assert.Equal(denseGrid.Size, denseGrid.ConfiguredVoxelCount);
        Assert.True(denseGrid.TryGetVoxel(new VoxelIndex(1, 0, 1), out Voxel denseVoxel));
        Assert.True(denseVoxel.IsAllocated);
    }

    private static GridConfiguration CreateSparseConfig(Vector3d min, Vector3d max, int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        return new GridConfiguration(
            min,
            max,
            scanCellSize,
            storageKind: GridStorageKind.Sparse);
    }
}
