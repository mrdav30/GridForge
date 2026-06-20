using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Tests;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using Xunit;

namespace GridForge.Utility.Tests;

public sealed class GridTraversalTests
{
    [Fact]
    public void TryVisitUnique_UsesSelectedPaddingModeAndSuppressesDuplicates()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(
            GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)9, (Fixed64)4));
        Voxel voxel = GetVoxel(world, Vector3d.Zero);
        SwiftHashSet<int> visited = new SwiftHashSet<int>();

        GridTraversalState maxTraversal = new GridTraversalState(
            world,
            GridTraversalPaddingMode.MaxCellEdge);

        Assert.True(maxTraversal.TryVisitUnique(voxel, visited, out Fixed64 maxPadding));
        Assert.Equal((Fixed64)9, maxPadding);
        Assert.False(maxTraversal.TryVisitUnique(voxel, visited, out _));

        visited.Clear();
        GridTraversalState planarTraversal = new GridTraversalState(
            world,
            GridTraversalPaddingMode.PlanarMaxCellEdge);

        Assert.True(planarTraversal.TryVisitUnique(voxel, visited, out Fixed64 planarPadding));
        Assert.Equal((Fixed64)4, planarPadding);
    }

    [Fact]
    public void TryGetUniquePartition_ReturnsAttachedPartitionOnce()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        Voxel voxel = GetVoxel(world, Vector3d.Zero);
        TestPartition partition = new TestPartition();
        Assert.True(voxel.TryAddPartition(partition));
        SwiftHashSet<int> visited = new SwiftHashSet<int>();

        Assert.True(GridTraversal.TryGetUniquePartition(voxel, visited, out TestPartition resolved));

        Assert.Same(partition, resolved);
        Assert.False(GridTraversal.TryGetUniquePartition(voxel, visited, out resolved));
    }

    [Fact]
    public void PaddedBounds_IncludeNegativeEdgeCoordinatesAndRejectOutsidePositions()
    {
        Vector3d min = new Vector3d(-3, -2, -4);
        Vector3d max = new Vector3d(-1, 0, -2);
        Fixed64 cellEdge = (Fixed64)2;
        Fixed64 outside = Fixed64.One / (Fixed64)16;

        Assert.True(GridTraversal.IsWorldPositionInPaddedBounds(
            min,
            max,
            cellEdge,
            new Vector3d(-4, -3, -5)));

        Assert.False(GridTraversal.IsWorldPositionInPaddedBounds(
            min,
            max,
            cellEdge,
            new Vector3d((Fixed64)(-4) - outside, (Fixed64)(-3), (Fixed64)(-5))));

        Assert.True(GridTraversal.IsPlanarPositionInPaddedBounds(
            new Vector2d(-3, -4),
            new Vector2d(-1, -2),
            cellEdge,
            new Vector3d((Fixed64)(-4), Fixed64.Zero, (Fixed64)(-5))));

        Assert.False(GridTraversal.IsPlanarPositionInPaddedBounds(
            new Vector2d(-3, -4),
            new Vector2d(-1, -2),
            cellEdge,
            new Vector3d((Fixed64)(-4), Fixed64.Zero, (Fixed64)(-5) - outside)));
    }

    [Fact]
    public void GridTopologyMetricUtility_PreservesThreeDimensionalAndPlanarCellEdgeSemantics()
    {
        using GridWorld rectangularWorld = CreateWorldWithRectangularGrid(
            GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)9, (Fixed64)4));
        VoxelGrid rectangularGrid = rectangularWorld.ActiveGrids[0];

        Assert.Equal((Fixed64)9, GridTopologyMetricUtility.GetMaxCellEdge(rectangularGrid));
        Assert.Equal((Fixed64)4, GridTopologyMetricUtility.GetPlanarMaxCellEdge(rectangularGrid));
        Assert.Equal((Fixed64)9, GridTopologyMetricUtility.GetRepresentativeCellEdge(rectangularWorld));

        using GridWorld hexWorld = CreateWorldWithHexGrid(GridTopologyMetrics.Hex((Fixed64)3, (Fixed64)10));
        VoxelGrid hexGrid = hexWorld.ActiveGrids[0];

        Assert.Equal((Fixed64)10, GridTopologyMetricUtility.GetMaxCellEdge(hexGrid));
        Assert.Equal((Fixed64)6, GridTopologyMetricUtility.GetPlanarMaxCellEdge(hexGrid));

        using GridWorld emptyWorld = new GridWorld();
        Assert.Equal(GridWorld.DefaultRectangularCellSize, GridTopologyMetricUtility.GetRepresentativeCellEdge(emptyWorld));
    }

    private static GridWorld CreateWorldWithRectangularGrid(GridTopologyMetrics metrics)
    {
        GridWorld world = new GridWorld();
        GridConfiguration configuration = new GridConfiguration(
            new Vector3d(-8, -8, -8),
            new Vector3d(8, 8, 8),
            topologyMetrics: metrics);

        Assert.True(world.TryAddGrid(configuration, out _));
        return world;
    }

    private static GridWorld CreateWorldWithHexGrid(GridTopologyMetrics metrics)
    {
        GridWorld world = new GridWorld();
        GridConfiguration configuration = new GridConfiguration(
            new Vector3d(-8, -8, -8),
            new Vector3d(8, 8, 8),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);

        Assert.True(world.TryAddGrid(configuration, out _));
        return world;
    }

    private static Voxel GetVoxel(GridWorld world, Vector3d position)
    {
        Assert.True(world.TryGetVoxel(position, out Voxel voxel));
        return voxel;
    }
}
