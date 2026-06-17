using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
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

    [Fact]
    public void HexPrismTopology_ShouldNormalizePaddingAndClampNegativeDimensions()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.PointyTop);
        HexPrismTopology topology = new(metrics);

        (Vector3d unpaddedMin, Vector3d unpaddedMax) =
            topology.NormalizeBounds(Vector3d.Zero, Vector3d.Zero, Fixed64.Zero);
        (Vector3d paddedMin, Vector3d paddedMax) =
            topology.NormalizeBounds(Vector3d.Zero, Vector3d.Zero, Fixed64.One);
        GridDimensions dimensions = topology.CalculateDimensions(new Vector3d(2, 0, 2), Vector3d.Zero);

        Assert.Equal(Vector3d.Zero, unpaddedMin);
        Assert.Equal(Vector3d.Zero, unpaddedMax);
        Assert.True(paddedMin.X < unpaddedMin.X);
        Assert.True(paddedMin.Z < unpaddedMin.Z);
        Assert.True(paddedMax.X > unpaddedMax.X);
        Assert.True(paddedMax.Z > unpaddedMax.Z);
        Assert.Equal(1, dimensions.Width);
        Assert.Equal(1, dimensions.Height);
        Assert.Equal(1, dimensions.Length);
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
    public void GetHexNeighborsInto_ShouldReturnDeterministicExpandedNeighborOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(2, 2, 2));

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        VoxelIndex centerIndex = new(1, 1, 1);
        HexDirection[] expectedDirections =
        {
            HexDirection.QPositive,
            HexDirection.QPositiveRNegative,
            HexDirection.RNegative,
            HexDirection.QNegative,
            HexDirection.QNegativeRPositive,
            HexDirection.RPositive,
            HexDirection.Below,
            HexDirection.BelowQPositive,
            HexDirection.BelowQPositiveRNegative,
            HexDirection.BelowRNegative,
            HexDirection.BelowQNegative,
            HexDirection.BelowQNegativeRPositive,
            HexDirection.BelowRPositive,
            HexDirection.Above,
            HexDirection.AboveQPositive,
            HexDirection.AboveQPositiveRNegative,
            HexDirection.AboveRNegative,
            HexDirection.AboveQNegative,
            HexDirection.AboveQNegativeRPositive,
            HexDirection.AboveRPositive
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

        SwiftList<(HexDirection Direction, Voxel Voxel)> neighbors = new();
        voxel.GetHexNeighborsInto(grid, neighbors);

        Assert.Equal(expectedOffsets.Length, neighbors.Count);

        for (int i = 0; i < expectedOffsets.Length; i++)
        {
            HexDirection direction = neighbors[i].Direction;
            Voxel neighbor = neighbors[i].Voxel;
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
    public void GetHexNeighborsInto_ShouldSkipMissingCornerNeighbors()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration configuration = CreateHexConfiguration(metrics, new VoxelIndex(1, 1, 1));

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        Assert.True(grid.TryGetVoxel(new VoxelIndex(0, 0, 0), out Voxel voxel));

        SwiftList<(HexDirection Direction, Voxel Voxel)> neighbors = new();
        voxel.GetHexNeighborsInto(grid, neighbors);

        Assert.Equal(5, neighbors.Count);
        Assert.Equal(HexDirection.QPositive, neighbors[0].Direction);
        Assert.Equal(HexDirection.RPositive, neighbors[1].Direction);
        Assert.Equal(HexDirection.Above, neighbors[2].Direction);
        Assert.Equal(HexDirection.AboveQPositive, neighbors[3].Direction);
        Assert.Equal(HexDirection.AboveRPositive, neighbors[4].Direction);
    }

    [Fact]
    public void TryGetNeighbor_ShouldReflectSameTopologyHexGridLoadAndUnload()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration firstConfiguration = CreateHexConfiguration(metrics, new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(firstConfiguration, out ushort firstGridIndex));

        VoxelGrid firstGrid = world.ActiveGrids[firstGridIndex];
        Assert.True(firstGrid.TryGetVoxel(new VoxelIndex(1, 0, 0), out Voxel boundaryVoxel));
        Assert.False(boundaryVoxel.TryGetNeighbor(firstGrid, HexDirection.QPositive, out _));

        Vector3d secondMin = boundaryVoxel.WorldPosition + HexCoordinateUtility.AxialToWorldOffset(
            HexDirectionUtility.GetOffset(HexDirection.QPositive),
            metrics);
        GridConfiguration secondConfiguration = CreateHexConfiguration(secondMin, metrics, new VoxelIndex(1, 0, 1));

        Assert.True(world.TryAddGrid(secondConfiguration, out ushort secondGridIndex));

        Assert.Equal(1, firstGrid.NeighborCount);
        Assert.Equal(HexDirection.QPositive, VoxelGrid.GetHexNeighborDirection(firstGrid, world.ActiveGrids[secondGridIndex]));
        Assert.True(firstGrid.Neighbors!.ContainsKey((int)HexDirection.QPositive));
        Assert.True(boundaryVoxel.TryGetNeighbor(firstGrid, HexDirection.QPositive, out Voxel resolvedNeighbor));
        Assert.Equal(new VoxelIndex(0, 0, 0), resolvedNeighbor.Index);

        Assert.True(world.TryRemoveGrid(secondGridIndex));

        Assert.False(boundaryVoxel.TryGetNeighbor(firstGrid, HexDirection.QPositive, out _));
    }

    [Fact]
    public void TryGetNeighbor_ShouldResolveExpandedHexNeighborAcrossConjoinedLayerBoundary()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        GridConfiguration firstConfiguration = CreateHexConfiguration(metrics, new VoxelIndex(1, 1, 1));

        Assert.True(world.TryAddGrid(firstConfiguration, out ushort firstGridIndex));

        VoxelGrid firstGrid = world.ActiveGrids[firstGridIndex];
        Assert.True(firstGrid.TryGetVoxel(new VoxelIndex(1, 1, 0), out Voxel boundaryVoxel));

        Vector3d secondMin = boundaryVoxel.WorldPosition + HexCoordinateUtility.AxialToWorldOffset(
            HexDirectionUtility.GetOffset(HexDirection.AboveQPositive),
            metrics);
        GridConfiguration secondConfiguration = CreateHexConfiguration(secondMin, metrics, new VoxelIndex(1, 1, 1));

        Assert.True(world.TryAddGrid(secondConfiguration, out ushort secondGridIndex));

        VoxelGrid secondGrid = world.ActiveGrids[secondGridIndex];
        Assert.Equal(HexDirection.AboveQPositive, VoxelGrid.GetHexNeighborDirection(firstGrid, secondGrid));
        Assert.True(firstGrid.Neighbors!.ContainsKey((int)HexDirection.AboveQPositive));
        Assert.True(boundaryVoxel.TryGetNeighbor(firstGrid, HexDirection.AboveQPositive, out Voxel neighbor));
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

        HexDirection[] planar = CopyToArray(HexDirectionUtility.Planar);
        HexDirection[] vertical = CopyToArray(HexDirectionUtility.Vertical);
        HexDirection[] belowLayer = CopyToArray(HexDirectionUtility.BelowLayer);
        HexDirection[] aboveLayer = CopyToArray(HexDirectionUtility.AboveLayer);
        HexDirection[] verticalDiagonal = CopyToArray(HexDirectionUtility.VerticalDiagonal);

        Assert.Equal(
            planar.Concat(vertical).ToArray(),
            CopyToArray(HexDirectionUtility.Primary));
        Assert.Equal(
            planar.Concat(belowLayer).Concat(aboveLayer).ToArray(),
            CopyToArray(HexDirectionUtility.All));
        Assert.DoesNotContain(HexDirection.Below, verticalDiagonal);
        Assert.DoesNotContain(HexDirection.Above, verticalDiagonal);
        Assert.Contains(HexDirection.AboveQPositive, aboveLayer);
        Assert.Contains(HexDirection.BelowQNegativeRPositive, belowLayer);
    }

    [Fact]
    public void HexDirectionUtility_ShouldUseOrientationNeutralAxialNames()
    {
        Assert.Equal(new VoxelIndex(1, 0, 0), HexDirectionUtility.GetOffset(HexDirection.QPositive));
        Assert.Equal(new VoxelIndex(1, 0, -1), HexDirectionUtility.GetOffset(HexDirection.QPositiveRNegative));
        Assert.Equal(new VoxelIndex(0, 0, -1), HexDirectionUtility.GetOffset(HexDirection.RNegative));
        Assert.Equal(new VoxelIndex(-1, 0, 0), HexDirectionUtility.GetOffset(HexDirection.QNegative));
        Assert.Equal(new VoxelIndex(-1, 0, 1), HexDirectionUtility.GetOffset(HexDirection.QNegativeRPositive));
        Assert.Equal(new VoxelIndex(0, 0, 1), HexDirectionUtility.GetOffset(HexDirection.RPositive));
        Assert.Equal(new VoxelIndex(1, -1, 0), HexDirectionUtility.GetOffset(HexDirection.BelowQPositive));
        Assert.Equal(new VoxelIndex(1, 1, -1), HexDirectionUtility.GetOffset(HexDirection.AboveQPositiveRNegative));
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

    [Theory]
    [InlineData(2, 0, 0, HexDirection.QPositive)]
    [InlineData(2, -1, 0, HexDirection.QPositiveRNegative)]
    [InlineData(0, -2, 0, HexDirection.RNegative)]
    [InlineData(-2, 0, 0, HexDirection.QNegative)]
    [InlineData(-2, 1, 0, HexDirection.QNegativeRPositive)]
    [InlineData(0, 2, 0, HexDirection.RPositive)]
    [InlineData(0, 0, 1, HexDirection.Above)]
    [InlineData(2, 0, 1, HexDirection.AboveQPositive)]
    [InlineData(-2, 1, -1, HexDirection.BelowQNegativeRPositive)]
    public void HexTopology_ShouldNormalizeWorldDeltasToNeighborSlots(int q, int r, int y, HexDirection expected)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        HexPrismTopology topology = new(metrics);
        Vector3d worldDelta = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(q, 0, r), metrics)
            + new Vector3d(0, y, 0);

        Assert.True(topology.TryGetNeighborSlotFromWorldDelta(worldDelta, out int slot));
        Assert.Equal((int)expected, slot);
    }

    [Fact]
    public void HexTopology_ShouldRejectNonAdjacentVerticalPlanarDelta()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        HexPrismTopology topology = new(metrics);
        Vector3d worldDelta = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(1, 0, 1), metrics)
            + new Vector3d(0, 1, 0);

        Assert.False(topology.TryGetNeighborSlotFromWorldDelta(worldDelta, out int slot));
        Assert.Equal(-1, slot);
    }

    [Fact]
    public void HexTopology_ShouldRejectZeroPlanarNeighborDelta()
    {
        HexPrismTopology topology = new HexPrismTopology(
            GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One));

        Assert.False(topology.TryGetNeighborSlotFromWorldDelta(Vector3d.Zero, out int slot));
        Assert.Equal(-1, slot);
    }

    [Fact]
    public void HexGrid_ShouldExposeBoundaryRangesSnappingAndCeilingThroughTopology()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 64);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One);
        VoxelIndex maxIndex = new(3, 3, 3);
        GridConfiguration configuration = new(
            Vector3d.Zero,
            HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics),
            scanCellSize: 2,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);

        Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));

        VoxelGrid grid = world.ActiveGrids[gridIndex];
        HexPrismTopology topology = new(metrics);
        Vector3d center = grid.GetWorldPosition(new VoxelIndex(3, 3, 3));
        Vector3d oneOneOffset = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(1, 0, 1), metrics);
        (Vector3d paddedMin, Vector3d paddedMax) = topology.NormalizeBounds(
            new Vector3d(0, 0, 0),
            oneOneOffset,
            Fixed64.Half);
        GridDimensions clampedDimensions = topology.CalculateDimensions(
            new Vector3d(0, 0, 0),
            new Vector3d(-1, 0, -1));

        Assert.Equal(Vector3d.FromDouble(-0.5, -1, -0.5), paddedMin);
        Assert.True(paddedMax.X > oneOneOffset.X);
        Assert.Equal(1, clampedDimensions.Width);
        Assert.Equal(1, clampedDimensions.Height);
        Assert.Equal(1, clampedDimensions.Length);
        Assert.Equal((1, 1, 1), grid.SnapToScanCell(center));
        Assert.Equal(center, grid.CeilToGrid(center));

        Assert.True(grid.IsFacingBoundary(new VoxelIndex(3, 2, 0), HexDirection.QPositiveRNegative));
        Assert.False(grid.IsFacingBoundary(new VoxelIndex(2, 2, 0), HexDirection.QPositiveRNegative));
        Assert.True(grid.IsFacingBoundary(new VoxelIndex(0, 0, 3), HexDirection.BelowQNegativeRPositive));
        Assert.True(grid.IsFacingBoundary(new VoxelIndex(1, 3, 1), HexDirection.Above));

        topology.GetBoundaryRange(
            (int)HexDirection.QPositiveRNegative,
            grid.Width,
            grid.Height,
            grid.Length,
            out int xStart,
            out int xEnd,
            out int yStart,
            out int yEnd,
            out int zStart,
            out int zEnd);

        Assert.Equal((3, 3), (xStart, xEnd));
        Assert.Equal((0, 3), (yStart, yEnd));
        Assert.Equal((0, 0), (zStart, zEnd));
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

    private static T[] CopyToArray<T>(ReadOnlySpan<T> values)
    {
        T[] copy = new T[values.Length];
        values.CopyTo(copy);
        return copy;
    }

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
