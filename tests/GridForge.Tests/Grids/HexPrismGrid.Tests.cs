using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public class HexPrismGridTests
{
    [Fact]
    public void TryAddGrid_ShouldRejectInvalidHexMetrics()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration zeroRadius = CreateHexConfiguration(
            Fixed64.Zero,
            Fixed64.One,
            HexOrientation.PointyTop,
            new VoxelIndex(1, 0, 1));
        GridConfiguration zeroLayerHeight = CreateHexConfiguration(
            Fixed64.One,
            Fixed64.Zero,
            HexOrientation.PointyTop,
            new VoxelIndex(1, 0, 1));

        Assert.False(world.TryAddGrid(zeroRadius, out _));
        Assert.False(world.TryAddGrid(zeroLayerHeight, out _));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void TryAddGrid_ShouldConstructHexGridAndResolveProjectedCenters(HexOrientation orientation)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(3),
            orientation);
        VoxelIndex maxIndex = new(2, 1, 2);
        GridConfiguration configuration = CreateHexConfiguration(metrics, maxIndex);

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.Equal(GridTopologyKind.HexPrism, grid.Configuration.TopologyKind);
        Assert.Equal(metrics, grid.Configuration.TopologyMetrics);
        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(3, grid.Length);
        Assert.Equal(18, grid.Size);

        Vector3d expectedWorldPosition = grid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        Assert.True(grid.TryGetVoxel(maxIndex, out Voxel maxVoxel));
        Assert.Equal(expectedWorldPosition, maxVoxel.WorldPosition);
        Assert.True(grid.TryGetVoxelIndex(expectedWorldPosition, out VoxelIndex resolvedIndex));
        Assert.Equal(maxIndex, resolvedIndex);
        Assert.True(world.TryGetGridAndVoxel(expectedWorldPosition, out VoxelGrid resolvedGrid, out Voxel resolvedVoxel));
        Assert.Same(grid, resolvedGrid);
        Assert.Same(maxVoxel, resolvedVoxel);
    }

    [Fact]
    public void TryGetGrid_ShouldAllowMixedRectangularAndHexTopologies()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridConfiguration rectangularConfiguration = new(
            new Vector3d(-4, 0, -4),
            new Vector3d(-2, 0, -2));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration hexConfiguration = CreateHexConfiguration(
            new Vector3d(10, 0, 10),
            hexMetrics,
            new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(rectangularConfiguration, out ushort rectangularIndex));
        Assert.True(world.TryAddGrid(hexConfiguration, out ushort hexIndex));

        VoxelGrid rectangularGrid = world.ActiveGrids[rectangularIndex];
        VoxelGrid hexGrid = world.ActiveGrids[hexIndex];
        Vector3d rectangularPosition = new Vector3d(-3, 0, -3);
        Vector3d hexPosition = hexGrid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(1, 0, 1), hexMetrics);

        Assert.True(world.TryGetGridAndVoxel(rectangularPosition, out VoxelGrid resolvedRectangular, out Voxel rectangularVoxel));
        Assert.Same(rectangularGrid, resolvedRectangular);
        Assert.Equal(new VoxelIndex(1, 0, 1), rectangularVoxel.Index);

        Assert.True(world.TryGetGridAndVoxel(hexPosition, out VoxelGrid resolvedHex, out Voxel hexVoxel));
        Assert.Same(hexGrid, resolvedHex);
        Assert.Equal(new VoxelIndex(1, 0, 1), hexVoxel.Index);
    }

    [Fact]
    public void TryGetGrid_ShouldRejectCoarseAabbPositionOutsideHexCoverage()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.PointyTop);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Vector3d outsideHexCoverage = new Vector3d(grid.BoundsMax.X, grid.BoundsMin.Y, grid.BoundsMin.Z);

        Assert.True(outsideHexCoverage.X >= grid.BoundsMin.X && outsideHexCoverage.X <= grid.BoundsMax.X);
        Assert.True(outsideHexCoverage.Z >= grid.BoundsMin.Z && outsideHexCoverage.Z <= grid.BoundsMax.Z);
        Assert.False(world.TryGetGrid(outsideHexCoverage, out _));
        Assert.False(world.TryGetGridAndVoxel(outsideHexCoverage, out _, out _));
    }

    private static GridConfiguration CreateHexConfiguration(
        Fixed64 radius,
        Fixed64 layerHeight,
        HexOrientation orientation,
        VoxelIndex maxIndex) =>
        CreateHexConfiguration(
            GridTopologyMetrics.Hex(radius, layerHeight, orientation),
            maxIndex);

    private static GridConfiguration CreateHexConfiguration(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex) =>
        CreateHexConfiguration(Vector3d.Zero, metrics, maxIndex);

    private static GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics);
        return new GridConfiguration(
            boundsMin,
            boundsMax,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
    }
}
