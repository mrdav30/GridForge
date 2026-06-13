using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using System.Linq;
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

    [Fact]
    public void GetHexNeighbors_ShouldReturnDeterministicExpandedNeighborOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(2, 2, 2));

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        VoxelIndex centerIndex = new(1, 1, 1);
        HexDirection[] expectedDirections =
        {
            HexDirection.East,
            HexDirection.NorthEast,
            HexDirection.NorthWest,
            HexDirection.West,
            HexDirection.SouthWest,
            HexDirection.SouthEast,
            HexDirection.Below,
            HexDirection.BelowEast,
            HexDirection.BelowNorthEast,
            HexDirection.BelowNorthWest,
            HexDirection.BelowWest,
            HexDirection.BelowSouthWest,
            HexDirection.BelowSouthEast,
            HexDirection.Above,
            HexDirection.AboveEast,
            HexDirection.AboveNorthEast,
            HexDirection.AboveNorthWest,
            HexDirection.AboveWest,
            HexDirection.AboveSouthWest,
            HexDirection.AboveSouthEast
        };
        VoxelIndex[] expectedOffsets =
        {
            new(1, 0, 0),
            new(1, 0, -1),
            new(0, 0, -1),
            new(-1, 0, 0),
            new(-1, 0, 1),
            new(0, 0, 1),
            new(0, -1, 0),
            new(1, -1, 0),
            new(1, -1, -1),
            new(0, -1, -1),
            new(-1, -1, 0),
            new(-1, -1, 1),
            new(0, -1, 1),
            new(0, 1, 0),
            new(1, 1, 0),
            new(1, 1, -1),
            new(0, 1, -1),
            new(-1, 1, 0),
            new(-1, 1, 1),
            new(0, 1, 1)
        };

        Assert.True(grid.TryGetVoxel(centerIndex, out Voxel voxel));

        var neighbors = voxel.GetHexNeighbors(grid, useCache: false).ToArray();

        Assert.Equal(expectedOffsets.Length, neighbors.Length);

        for (int i = 0; i < expectedOffsets.Length; i++)
        {
            HexDirection direction = neighbors[i].Item1;
            Voxel neighbor = neighbors[i].Item2;
            VoxelIndex expectedIndex = new(
                centerIndex.x + expectedOffsets[i].x,
                centerIndex.y + expectedOffsets[i].y,
                centerIndex.z + expectedOffsets[i].z);

            Assert.Equal(expectedDirections[i], direction);
            Assert.Equal(expectedOffsets[i], HexDirectionUtility.GetOffset(direction));
            Assert.Equal(expectedIndex, neighbor.Index);
        }
    }

    [Fact]
    public void GetHexNeighbors_ShouldSkipMissingCornerNeighbors()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 1, 1));

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));

        var neighbors = voxel.GetHexNeighbors(grid, useCache: false).ToArray();

        Assert.Equal(5, neighbors.Length);
        Assert.Equal(HexDirection.East, neighbors[0].Item1);
        Assert.Equal(HexDirection.SouthEast, neighbors[1].Item1);
        Assert.Equal(HexDirection.Above, neighbors[2].Item1);
        Assert.Equal(HexDirection.AboveEast, neighbors[3].Item1);
        Assert.Equal(HexDirection.AboveSouthEast, neighbors[4].Item1);
    }

    [Fact]
    public void TryGetHexNeighbor_ShouldRefreshBoundaryCacheWhenSameTopologyGridLoadsAndUnloads()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration firstConfiguration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(firstConfiguration, out ushort firstGridIndex));

        VoxelGrid firstGrid = world.ActiveGrids[firstGridIndex];
        Assert.True(firstGrid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel boundaryVoxel));
        Assert.False(boundaryVoxel.TryGetHexNeighbor(firstGrid, HexDirection.East, out _, useCache: true));

        Vector3d secondMin = boundaryVoxel.WorldPosition + HexCoordinateUtility.AxialToWorldOffset(
            HexDirectionUtility.GetOffset(HexDirection.East),
            metrics);
        GridConfiguration secondConfiguration = CreateHexConfiguration(secondMin, metrics, new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(secondConfiguration, out ushort secondGridIndex));

        Assert.Equal(1, firstGrid.NeighborCount);
        Assert.Equal(HexDirection.East, VoxelGrid.GetHexNeighborDirection(firstGrid, world.ActiveGrids[secondGridIndex]));
        Assert.True(firstGrid.Neighbors!.ContainsKey((int)HexDirection.East));
        Assert.True(boundaryVoxel.TryGetHexNeighbor(firstGrid, HexDirection.East, out Voxel cachedNeighbor, useCache: true));
        Assert.Equal(new VoxelIndex(0, 0, 0), cachedNeighbor.Index);

        Assert.True(world.TryRemoveGrid(secondGridIndex));

        Assert.False(boundaryVoxel.TryGetHexNeighbor(firstGrid, HexDirection.East, out _, useCache: true));
    }

    [Fact]
    public void TryGetHexNeighbor_ShouldResolveExpandedNeighborAcrossConjoinedLayerBoundary()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration firstConfiguration = CreateHexConfiguration(metrics, new VoxelIndex(1, 1, 1));

        Assert.True(world.TryAddGrid(firstConfiguration, out ushort firstGridIndex));

        VoxelGrid firstGrid = world.ActiveGrids[firstGridIndex];
        Assert.True(firstGrid.TryGetVoxel(new VoxelIndex(1, 1, 0), out Voxel boundaryVoxel));

        Vector3d secondMin = boundaryVoxel.WorldPosition + HexCoordinateUtility.AxialToWorldOffset(
            HexDirectionUtility.GetOffset(HexDirection.AboveEast),
            metrics);
        GridConfiguration secondConfiguration = CreateHexConfiguration(secondMin, metrics, new VoxelIndex(1, 1, 1));

        Assert.True(world.TryAddGrid(secondConfiguration, out ushort secondGridIndex));

        VoxelGrid secondGrid = world.ActiveGrids[secondGridIndex];
        Assert.Equal(HexDirection.AboveEast, VoxelGrid.GetHexNeighborDirection(firstGrid, secondGrid));
        Assert.True(firstGrid.Neighbors!.ContainsKey((int)HexDirection.AboveEast));
        Assert.True(boundaryVoxel.TryGetHexNeighbor(firstGrid, HexDirection.AboveEast, out Voxel neighbor, useCache: true));
        Assert.Equal(new VoxelIndex(0, 0, 0), neighbor.Index);
    }

    [Fact]
    public void HexDirectionUtility_ShouldExposeDeterministicSubsets()
    {
        Assert.Equal(20, HexDirectionUtility.All.Length);
        Assert.Equal(8, HexDirectionUtility.Primary.Length);
        Assert.Equal(6, HexDirectionUtility.Planar.Length);
        Assert.Equal(2, HexDirectionUtility.Vertical.Length);
        Assert.Equal(7, HexDirectionUtility.BelowLayer.Length);
        Assert.Equal(7, HexDirectionUtility.AboveLayer.Length);
        Assert.Equal(12, HexDirectionUtility.VerticalDiagonal.Length);

        Assert.Equal(
            HexDirectionUtility.Planar.Concat(HexDirectionUtility.Vertical).ToArray(),
            HexDirectionUtility.Primary);
        Assert.Equal(
            HexDirectionUtility.Planar.Concat(HexDirectionUtility.BelowLayer).Concat(HexDirectionUtility.AboveLayer).ToArray(),
            HexDirectionUtility.All);
        Assert.DoesNotContain(HexDirection.Below, HexDirectionUtility.VerticalDiagonal);
        Assert.DoesNotContain(HexDirection.Above, HexDirectionUtility.VerticalDiagonal);
        Assert.Contains(HexDirection.AboveEast, HexDirectionUtility.AboveLayer);
        Assert.Contains(HexDirection.BelowSouthWest, HexDirectionUtility.BelowLayer);
    }

    [Fact]
    public void TryAddGrid_ShouldSkipMixedTopologyNeighborBridge()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration hexConfiguration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(hexConfiguration, out ushort hexGridIndex));

        VoxelGrid hexGrid = world.ActiveGrids[hexGridIndex];
        Vector3d rectangularMin = hexGrid.BoundsMin + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(2, 0, 0), metrics);
        GridConfiguration rectangularConfiguration = new(rectangularMin, rectangularMin + new Vector3d(1, 0, 1));

        Assert.True(world.TryAddGrid(rectangularConfiguration, out ushort rectangularGridIndex));

        VoxelGrid rectangularGrid = world.ActiveGrids[rectangularGridIndex];
        Assert.Equal(0, hexGrid.NeighborCount);
        Assert.Equal(0, rectangularGrid.NeighborCount);
        Assert.True(world.TryGetGridAndVoxel(hexGrid.BoundsMin, out VoxelGrid resolvedHex, out _));
        Assert.Same(hexGrid, resolvedHex);
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
