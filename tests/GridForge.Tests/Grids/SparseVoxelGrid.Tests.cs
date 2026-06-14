//=======================================================================
// SparseVoxelGrid.Tests.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
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
    public void SparseHexGrid_ShouldCreateConfiguredAxialVoxelsOnly()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.PointyTop);
        VoxelIndex maxIndex = new(2, 0, 2);
        GridConfiguration config = CreateSparseHexConfig(metrics, maxIndex, scanCellSize: 2);
        VoxelIndex[] configured =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 2)
        };

        Assert.True(_world.TryAddGrid(config, configured, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex missingIndex = new(0, 0, 2);
        Vector3d missingPosition = grid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(missingIndex, metrics);
        Vector3d maxPosition = grid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);

        Assert.Equal(GridTopologyKind.HexPrism, grid.Configuration.TopologyKind);
        Assert.Equal(GridStorageKind.Sparse, grid.StorageKind);
        Assert.Equal(3, grid.Width);
        Assert.Equal(1, grid.Height);
        Assert.Equal(3, grid.Length);
        Assert.Equal(9, grid.Size);
        Assert.Equal(3, grid.ConfiguredVoxelCount);
        Assert.True(grid.TryGetVoxel(maxIndex, out Voxel maxVoxel));
        Assert.Equal(maxPosition, maxVoxel.WorldPosition);
        Assert.True(grid.TryGetScanCell(maxIndex, out ScanCell maxScanCell));
        Assert.Equal(grid.GetScanCellKey(maxIndex), maxScanCell.CellKey);

        Assert.False(grid.TryGetVoxel(missingIndex, out _));
        Assert.False(grid.ContainsVoxel(missingIndex));
        Assert.False(grid.TryGetScanCell(missingPosition, out _));
        Assert.True(_world.TryGetGrid(missingPosition, out VoxelGrid resolvedGrid));
        Assert.Same(grid, resolvedGrid);
        Assert.False(_world.TryGetGridAndVoxel(missingPosition, out VoxelGrid missingGrid, out Voxel missingVoxel));
        Assert.Same(grid, missingGrid);
        Assert.Null(missingVoxel);

        VoxelIndex[] actual = grid
            .EnumerateVoxels()
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Equal(configured, actual);
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
    public void SparseGrid_ShouldAddAndRemoveConfiguredVoxelAtRuntime()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(2, 0, 2), scanCellSize: 2);
        VoxelIndex index = new(1, 0, 1);
        GridEventInfo lastEvent = default;
        int changedCount = 0;

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        _world.OnActiveGridChange += eventInfo =>
        {
            lastEvent = eventInfo;
            changedCount++;
        };

        uint initialVersion = grid.Version;

        Assert.True(grid.TryAddVoxel(index, out Voxel addedVoxel));
        Assert.NotNull(addedVoxel);
        Assert.Equal(1, grid.ConfiguredVoxelCount);
        Assert.True(grid.ContainsVoxel(index));
        Assert.True(grid.TryGetVoxel(index, out Voxel resolvedVoxel));
        Assert.Same(addedVoxel, resolvedVoxel);
        Assert.True(grid.TryGetScanCell(index, out ScanCell scanCell));
        Assert.Equal(grid.GetScanCellKey(index), scanCell.CellKey);
        Assert.Equal(initialVersion + 1, grid.Version);
        Assert.Equal(GridEventKind.SparseVoxelAdded, lastEvent.ChangeKind);
        Assert.Equal(index, lastEvent.VoxelIndex);
        Assert.Equal(addedVoxel.WorldPosition, lastEvent.AffectedBoundsMin);
        Assert.Equal(addedVoxel.WorldPosition, lastEvent.AffectedBoundsMax);

        Assert.False(grid.TryAddVoxel(index, out _));
        Assert.Equal(1, grid.ConfiguredVoxelCount);

        Assert.True(grid.TryRemoveVoxel(index));

        Assert.False(addedVoxel.IsAllocated);
        Assert.Equal(0, grid.ConfiguredVoxelCount);
        Assert.False(grid.ContainsVoxel(index));
        Assert.False(grid.TryGetVoxel(index, out _));
        Assert.False(grid.TryGetScanCell(index, out _));
        Assert.Empty(grid.EnumerateVoxels());
        Assert.Equal(GridEventKind.SparseVoxelRemoved, lastEvent.ChangeKind);
        Assert.Equal(index, lastEvent.VoxelIndex);
        Assert.Equal(2, changedCount);
    }

    [Fact]
    public void SparseGrid_RuntimeAdds_ShouldGrowSingleBlockAndKeepVoxelCacheSorted()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(31, 0, 0), scanCellSize: 64);

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryAddVoxel(new VoxelIndex(10, 0, 0), out _));
        Assert.True(grid.TryAddVoxel(new VoxelIndex(0, 0, 0), out _));

        for (int x = 1; x <= 20; x++)
        {
            if (x == 10)
                continue;

            Assert.True(grid.TryAddVoxel(new VoxelIndex(x, 0, 0), out _));
        }

        VoxelIndex[] actual = grid
            .EnumerateVoxels()
            .Select(voxel => voxel.Index)
            .ToArray();

        Assert.Equal(21, grid.ConfiguredVoxelCount);
        Assert.Equal(
            Enumerable.Range(0, 21).Select(x => new VoxelIndex(x, 0, 0)).ToArray(),
            actual);
        Assert.True(grid.TryGetVoxel(new VoxelIndex(20, 0, 0), out Voxel lastVoxel));
        Assert.Equal(new Vector3d(20, 0, 0), lastVoxel.WorldPosition);

        Assert.True(grid.TryRemoveVoxel(new VoxelIndex(0, 0, 0)));
        Assert.Equal(
            Enumerable.Range(1, 20).Select(x => new VoxelIndex(x, 0, 0)).ToArray(),
            grid.EnumerateVoxels().Select(voxel => voxel.Index).ToArray());
    }

    [Fact]
    public void SparseHexGrid_ShouldAddAndRemoveAxialVoxelAtRuntime()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.FlatTop);
        GridConfiguration config = CreateSparseHexConfig(metrics, new VoxelIndex(2, 0, 2), scanCellSize: 2);
        VoxelIndex index = new(1, 0, 1);
        GridEventInfo lastEvent = default;
        int changedCount = 0;

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Vector3d position = grid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(index, metrics);
        _world.OnActiveGridChange += eventInfo =>
        {
            lastEvent = eventInfo;
            changedCount++;
        };

        uint initialVersion = grid.Version;

        Assert.True(_world.TryGetGrid(position, out VoxelGrid resolvedGrid));
        Assert.Same(grid, resolvedGrid);
        Assert.False(_world.TryGetGridAndVoxel(position, out _, out _));
        Assert.True(grid.TryAddVoxel(index, out Voxel addedVoxel));
        Assert.NotNull(addedVoxel);
        Assert.Equal(position, addedVoxel.WorldPosition);
        Assert.Equal(1, grid.ConfiguredVoxelCount);
        Assert.True(grid.ContainsVoxel(index));
        Assert.True(_world.TryGetGridAndVoxel(position, out VoxelGrid configuredGrid, out Voxel configuredVoxel));
        Assert.Same(grid, configuredGrid);
        Assert.Same(addedVoxel, configuredVoxel);
        Assert.True(grid.TryGetScanCell(index, out ScanCell scanCell));
        Assert.Equal(grid.GetScanCellKey(index), scanCell.CellKey);
        Assert.Equal(initialVersion + 1, grid.Version);
        Assert.Equal(GridEventKind.SparseVoxelAdded, lastEvent.ChangeKind);
        Assert.Equal(index, lastEvent.VoxelIndex);
        Assert.Equal(position, lastEvent.AffectedBoundsMin);
        Assert.Equal(position, lastEvent.AffectedBoundsMax);

        Assert.True(grid.TryRemoveVoxel(index));

        Assert.False(addedVoxel.IsAllocated);
        Assert.Equal(0, grid.ConfiguredVoxelCount);
        Assert.False(grid.ContainsVoxel(index));
        Assert.False(_world.TryGetGridAndVoxel(position, out _, out _));
        Assert.Empty(grid.EnumerateVoxels());
        Assert.Equal(GridEventKind.SparseVoxelRemoved, lastEvent.ChangeKind);
        Assert.Equal(index, lastEvent.VoxelIndex);
        Assert.Equal(2, changedCount);
    }

    [Fact]
    public void DenseGrid_ShouldRejectRuntimeSparseVoxelMutation()
    {
        GridConfiguration config = new(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));

        Assert.True(_world.TryAddGrid(config, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        Assert.True(grid.ContainsVoxel(new VoxelIndex(1, 0, 1)));
        Assert.False(grid.TryAddVoxel(new VoxelIndex(1, 0, 1), out _));
        Assert.False(grid.TryRemoveVoxel(new VoxelIndex(1, 0, 1)));
        Assert.Equal(grid.Size, grid.ConfiguredVoxelCount);
    }

    [Fact]
    public void SparseGrid_ShouldRejectRuntimeRemoveWhenVoxelHasUnsafeState()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(0, 0, 0));
        VoxelIndex index = new(0, 0, 0);

        Assert.True(_world.TryAddGrid(config, new[] { index }, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(index, out Voxel voxel));

        TestOccupant occupant = new(voxel.WorldPosition);
        Assert.True(grid.TryAddVoxelOccupant(voxel, occupant));
        Assert.False(grid.TryRemoveVoxel(index));
        Assert.True(grid.TryRemoveVoxelOccupant(voxel, occupant));

        BoundsKey obstacleToken = new(voxel.WorldPosition, voxel.WorldPosition);
        Assert.True(grid.TryAddObstacle(voxel, obstacleToken));
        Assert.False(grid.TryRemoveVoxel(index));
        Assert.True(grid.TryRemoveObstacle(voxel, obstacleToken));

        TestPartition partition = new();
        Assert.True(voxel.TryAddPartition(partition));
        Assert.False(grid.TryRemoveVoxel(index));
        Assert.True(voxel.TryRemovePartition<TestPartition>());

        void HandleObstacleAdded(ObstacleEventInfo _) { }

        voxel.OnObstacleAdded += HandleObstacleAdded;
        Assert.False(grid.TryRemoveVoxel(index));
        voxel.OnObstacleAdded -= HandleObstacleAdded;

        Assert.True(grid.TryRemoveVoxel(index));
    }

    [Fact]
    public void SparseGrid_ShouldReflectLocalNeighborLookupWhenRuntimeVoxelsChange()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
        VoxelIndex originIndex = new(0, 0, 0);
        VoxelIndex eastIndex = new(1, 0, 0);

        Assert.True(_world.TryAddGrid(config, new[] { originIndex }, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(originIndex, out Voxel originVoxel));
        Assert.False(originVoxel.TryGetNeighbor(grid, RectangularDirection.East, out _));

        Assert.True(grid.TryAddVoxel(eastIndex, out Voxel eastVoxel));

        Assert.True(originVoxel.TryGetNeighbor(grid, RectangularDirection.East, out Voxel resolvedNeighbor));
        Assert.Same(eastVoxel, resolvedNeighbor);

        Assert.True(grid.TryRemoveVoxel(eastIndex));

        Assert.False(originVoxel.TryGetNeighbor(grid, RectangularDirection.East, out _));
    }

    [Fact]
    public void SparseGrid_ShouldReflectNeighborGridLookupWhenRuntimeVoxelsChange()
    {
        GridConfiguration firstConfig = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 0));
        GridConfiguration secondConfig = CreateSparseConfig(new Vector3d(1, 0, 0), new Vector3d(2, 0, 0));
        VoxelIndex firstBoundaryIndex = new(1, 0, 0);
        VoxelIndex secondBoundaryIndex = new(1, 0, 0);

        Assert.True(_world.TryAddGrid(firstConfig, new[] { firstBoundaryIndex }, out ushort firstGridIndex));
        Assert.True(_world.TryAddGrid(secondConfig, out ushort secondGridIndex));
        VoxelGrid firstGrid = _world.ActiveGrids[firstGridIndex];
        VoxelGrid secondGrid = _world.ActiveGrids[secondGridIndex];
        Assert.True(firstGrid.TryGetVoxel(firstBoundaryIndex, out Voxel firstBoundaryVoxel));
        Assert.False(firstBoundaryVoxel.TryGetNeighbor(firstGrid, RectangularDirection.East, out _));

        Assert.True(secondGrid.TryAddVoxel(secondBoundaryIndex, out Voxel secondBoundaryVoxel));

        Assert.True(firstBoundaryVoxel.TryGetNeighbor(firstGrid, RectangularDirection.East, out Voxel resolvedNeighbor));
        Assert.Same(secondBoundaryVoxel, resolvedNeighbor);

        Assert.True(secondGrid.TryRemoveVoxel(secondBoundaryIndex));

        Assert.False(firstBoundaryVoxel.TryGetNeighbor(firstGrid, RectangularDirection.East, out _));
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
    public void SparseGrid_ShouldAllowEmptyBooleanMask()
    {
        GridConfiguration config = CreateSparseConfig(new Vector3d(0, 0, 0), new Vector3d(1, 0, 1));
        bool[,,] configured = new bool[2, 1, 2];

        Assert.True(_world.TryAddGrid(config, configured, out ushort gridIndex));

        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        Assert.Equal(0, grid.ConfiguredVoxelCount);
        Assert.Empty(grid.EnumerateVoxels());
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

    private static GridConfiguration CreateSparseHexConfig(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        Vector3d boundsMax = HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            Vector3d.Zero,
            boundsMax,
            scanCellSize,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
    }
}
