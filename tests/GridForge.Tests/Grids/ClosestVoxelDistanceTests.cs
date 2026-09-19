using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class ClosestVoxelDistanceTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 8);

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void ClosestVoxel_DirectHit_ShouldPreserveIdentityAndExactDistanceAtTiesBoundsAndExtremes(
        GridStorageKind storageKind,
        GridTopologyKind topologyKind,
        HexOrientation orientation)
    {
        GridConfiguration configuration = CreateConfiguration(storageKind, topologyKind, orientation, 2);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[]
            {
                new VoxelIndex(0, 0, 0),
                new VoxelIndex(0, 1, 0),
                new VoxelIndex(0, 2, 0),
                new VoxelIndex(1, 0, 1)
            },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        // Both storage kinds hit a configured center directly. Half-layer ties round to even.
        AssertClosest(grid, Vector3d.Zero, new VoxelIndex(0, 0, 0), Fixed64.Zero);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, Fixed64.Half, Fixed64.Zero), new VoxelIndex(0, 0, 0), new Fixed64(1) / 4);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, new Fixed64(3) / 4, Fixed64.Zero), new VoxelIndex(0, 1, 0), new Fixed64(1) / 16);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, new Fixed64(3) / 2, Fixed64.Zero), new VoxelIndex(0, 2, 0), new Fixed64(1) / 4);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, new Fixed64(7) / 4, Fixed64.Zero), new VoxelIndex(0, 2, 0), new Fixed64(1) / 16);
        AssertClosest(grid, new Vector3d(0, 2, 0), new VoxelIndex(0, 2, 0), Fixed64.Zero);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, new Fixed64(-3) / 4, Fixed64.Zero), new VoxelIndex(0, 0, 0), new Fixed64(9) / 16);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, new Fixed64(11) / 4, Fixed64.Zero), new VoxelIndex(0, 2, 0), new Fixed64(9) / 16);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, Fixed64.MinValue, Fixed64.Zero), new VoxelIndex(0, 0, 0), Fixed64.MaxValue);
        AssertClosest(grid, new Vector3d(Fixed64.Zero, Fixed64.MaxValue, Fixed64.Zero), new VoxelIndex(0, 2, 0), Fixed64.MaxValue);

        VoxelIndex horizontalIndex = new(1, 0, 1);
        Assert.True(grid.TryGetVoxel(horizontalIndex, out Voxel horizontalVoxel));
        AssertClosest(grid, horizontalVoxel.WorldPosition, horizontalIndex, Fixed64.Zero);
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void ClosestVoxel_NonCubicDirectHit_ShouldKeepAllThreeDistanceComponents(GridStorageKind storageKind)
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(4, 6, 8),
            topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(2), new Fixed64(3), new Fixed64(4)),
            storageKind: storageKind);
        VoxelIndex expectedIndex = new(1, 1, 1);
        Assert.True(_world.TryAddGrid(configuration, new[] { expectedIndex }, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];

        // Center (2, 3, 4), displacement (-1/4, -1/2, 1/4): squared distance 3/8.
        Vector3d position = new(new Fixed64(7) / 4, new Fixed64(5) / 2, new Fixed64(17) / 4);
        AssertClosest(grid, position, expectedIndex, new Fixed64(3) / 8);
    }

    [Theory]
    [InlineData(GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void ClosestVoxel_SparseFallback_ShouldKeepDistanceTieAndMutationOrdering(
        GridTopologyKind topologyKind,
        HexOrientation orientation)
    {
        GridConfiguration configuration = CreateConfiguration(GridStorageKind.Sparse, topologyKind, orientation, 8);
        Assert.True(_world.TryAddGrid(
            configuration,
            new[] { new VoxelIndex(0, 8, 0), new VoxelIndex(0, 0, 0) },
            out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        VoxelIndex missingIndex = new(0, 4, 0);
        Vector3d midpoint = new(0, 4, 0);
        Assert.False(grid.TryGetVoxel(missingIndex, out _));

        AssertClosest(grid, new Vector3d(0, 5, 0), new VoxelIndex(0, 8, 0), new Fixed64(9));
        AssertClosest(grid, midpoint, new VoxelIndex(0, 0, 0), new Fixed64(16));

        Assert.True(grid.TryAddVoxel(missingIndex, out Voxel addedVoxel));
        AssertClosest(grid, midpoint, missingIndex, Fixed64.Zero);
        Assert.True(grid.TryGetClosestVoxel(midpoint, out Voxel closestAfterAdd));
        Assert.Same(addedVoxel, closestAfterAdd);

        Assert.True(grid.TryRemoveVoxel(missingIndex));
        Assert.False(grid.TryGetVoxel(missingIndex, out _));
        AssertClosest(grid, midpoint, new VoxelIndex(0, 0, 0), new Fixed64(16));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosestVoxel_EmptyOrInactiveGrid_ShouldPreserveFailureOutputs(bool removeGrid)
    {
        GridConfiguration configuration = new(Vector3d.Zero, new Vector3d(2, 0, 0), storageKind: GridStorageKind.Sparse);
        VoxelIndex[] configured = removeGrid ? new[] { new VoxelIndex(0, 0, 0) } : Array.Empty<VoxelIndex>();
        Assert.True(_world.TryAddGrid(configuration, configured, out ushort gridIndex));
        VoxelGrid grid = _world.ActiveGrids[gridIndex];
        if (removeGrid)
            Assert.True(_world.TryRemoveGrid(gridIndex));

        Assert.False(grid.TryGetClosestVoxel(Vector3d.Zero, out Voxel voxelOnly));
        Assert.Null(voxelOnly);
        Assert.False(grid.TryGetClosestVoxel(Vector3d.Zero, out Voxel withDistance, out Fixed64 distanceSquared));
        Assert.Null(withDistance);
        Assert.Equal(Fixed64.MaxValue, distanceSquared);
    }

    [Theory]
    [InlineData(GridStorageKind.Dense)]
    [InlineData(GridStorageKind.Sparse)]
    public void ClosestGridAndVoxel_ShouldUseDirectHitDistanceInsteadOfClosestBounds(GridStorageKind storageKind)
    {
        // Both bounds contain the query, so the lower grid index wins the bounds tie.
        // Its nearest center is farther away: 1/4 squared versus 1/8 squared.
        GridConfiguration fartherConfiguration = new(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            storageKind: storageKind);
        Assert.True(_world.TryAddGrid(
            fartherConfiguration,
            new[] { new VoxelIndex(1, 0, 0) },
            out ushort fartherGridIndex));
        GridConfiguration nearerConfiguration = new(
            Vector3d.Zero,
            new Vector3d(new Fixed64(9) / 4, Fixed64.Zero, Fixed64.Zero),
            topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(9) / 8, Fixed64.One, Fixed64.One),
            storageKind: storageKind);
        Assert.True(_world.TryAddGrid(
            nearerConfiguration,
            new[] { new VoxelIndex(1, 0, 0) },
            out ushort nearerGridIndex));
        VoxelGrid fartherGrid = _world.ActiveGrids[fartherGridIndex];
        VoxelGrid nearerGrid = _world.ActiveGrids[nearerGridIndex];
        Vector3d position = new(new Fixed64(5) / 4, Fixed64.Zero, Fixed64.Zero);
        VoxelIndex expectedIndex = new(1, 0, 0);

        AssertClosest(fartherGrid, position, expectedIndex, new Fixed64(1) / 16);
        AssertClosest(nearerGrid, position, expectedIndex, new Fixed64(1) / 64);
        Assert.True(_world.TryGetClosestGrid(position, out VoxelGrid closestBoundsGrid));
        Assert.Same(fartherGrid, closestBoundsGrid);
        Assert.True(nearerGrid.TryGetVoxel(expectedIndex, out Voxel expectedVoxel));
        Assert.True(_world.TryGetClosestGridAndVoxel(position, out VoxelGrid closestGrid, out Voxel closestVoxel));
        Assert.Same(nearerGrid, closestGrid);
        Assert.Same(expectedVoxel, closestVoxel);
    }

    private static GridConfiguration CreateConfiguration(
        GridStorageKind storageKind,
        GridTopologyKind topologyKind,
        HexOrientation orientation,
        int highestLayer)
    {
        GridTopologyMetrics metrics = topologyKind == GridTopologyKind.HexPrism
            ? GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation)
            : GridTopologyMetrics.Rectangular(Fixed64.One);
        Vector3d boundsMax = topologyKind == GridTopologyKind.HexPrism
            ? HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(1, highestLayer, 1), metrics)
            : new Vector3d(1, highestLayer, 1);
        return new GridConfiguration(Vector3d.Zero, boundsMax,
            topologyKind: topologyKind, topologyMetrics: metrics, storageKind: storageKind);
    }

    private static void AssertClosest(
        VoxelGrid grid,
        Vector3d position,
        VoxelIndex expectedIndex,
        Fixed64 expectedDistanceSquared)
    {
        Assert.True(grid.TryGetVoxel(expectedIndex, out Voxel expectedVoxel));
        Assert.True(grid.TryGetClosestVoxel(position, out Voxel voxelOnly));
        Assert.Same(expectedVoxel, voxelOnly);
        Assert.True(grid.TryGetClosestVoxel(position, out Voxel withDistance, out Fixed64 distanceSquared));
        Assert.Same(expectedVoxel, withDistance);
        Assert.Equal(expectedDistanceSquared, distanceSquared);
    }
}
