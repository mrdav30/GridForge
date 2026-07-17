using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Tests;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;
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
        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();

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
    public void TryVisitUnique_ReusedGridSlotUsesReplacementGenerationMetrics()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(
            GridTopologyMetrics.Rectangular((Fixed64)2, (Fixed64)9, (Fixed64)4));
        VoxelGrid originalGrid = world.ActiveGrids[0];
        Voxel originalVoxel = GetVoxel(world, Vector3d.Zero);
        WorldVoxelIndex originalIndex = originalVoxel.WorldIndex;
        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();
        GridTraversalState traversal = new GridTraversalState(world, GridTraversalPaddingMode.MaxCellEdge);

        Assert.True(traversal.TryVisitUnique(originalVoxel, visited, out Fixed64 originalEdge));
        Assert.Equal((Fixed64)9, originalEdge);
        Assert.True(world.TryRemoveGrid(originalGrid.GridIndex));

        GridConfiguration replacementConfiguration = new GridConfiguration(
            new Vector3d(-8, -8, -8),
            new Vector3d(8, 8, 8),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)3, (Fixed64)11, (Fixed64)5));

        Assert.True(world.TryAddGrid(replacementConfiguration, out ushort replacementGridIndex));
        VoxelGrid replacementGrid = world.ActiveGrids[replacementGridIndex];
        Assert.Equal(originalIndex.GridIndex, replacementGrid.GridIndex);
        Assert.NotEqual(originalIndex.GridSpawnToken, replacementGrid.SpawnToken);

        Voxel replacementVoxel = GetVoxel(world, Vector3d.Zero);
        Assert.True(traversal.TryVisitUnique(replacementVoxel, visited, out Fixed64 replacementEdge));
        Assert.Equal((Fixed64)11, replacementEdge);
    }

    [Fact]
    public void GridTraversalState_RejectsRemovedGridGenerationVoxels()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        VoxelGrid grid = world.ActiveGrids[0];
        HashCollidingVoxel staleVoxel = CreateHashCollidingVoxel(grid, new VoxelIndex(0, 0, 0));
        GridTraversalState traversal = new GridTraversalState(world, GridTraversalPaddingMode.MaxCellEdge);

        Assert.Equal(Fixed64.One, traversal.GetCellEdge(staleVoxel));
        Assert.True(world.TryRemoveGrid(grid.GridIndex));

        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();
        Assert.False(traversal.TryVisitUnique(staleVoxel, visited, out _));
        Assert.Empty(visited);
        Assert.Throws<InvalidOperationException>(() => traversal.GetCellEdge(staleVoxel));
    }

    [Fact]
    public void TryVisitUnique_VisitsDistinctWorldIndicesWhenVoxelHashesCollide()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        VoxelGrid grid = world.ActiveGrids[0];
        HashCollidingVoxel first = CreateHashCollidingVoxel(grid, new VoxelIndex(0, 0, 0));
        HashCollidingVoxel second = CreateHashCollidingVoxel(grid, new VoxelIndex(1, 0, 0));
        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();
        GridTraversalState traversal = new GridTraversalState(world, GridTraversalPaddingMode.MaxCellEdge);

        Assert.True(traversal.TryVisitUnique(first, visited, out _));
        Assert.True(traversal.TryVisitUnique(second, visited, out _));
        Assert.False(traversal.TryVisitUnique(first, visited, out _));
    }

    [Fact]
    public void TryVisitUnique_WithReusableExactIdentitySet_ShouldNotAllocateAfterWarmup()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        Voxel voxel = GetVoxel(world, Vector3d.Zero);
        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();

        for (int i = 0; i < 16; i++)
        {
            visited.Clear();
            GridTraversalState warmup = new GridTraversalState(world, GridTraversalPaddingMode.MaxCellEdge);
            Assert.True(warmup.TryVisitUnique(voxel, visited, out _));
            Assert.False(warmup.TryVisitUnique(voxel, visited, out _));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            visited.Clear();
            GridTraversalState traversal = new GridTraversalState(world, GridTraversalPaddingMode.MaxCellEdge);
            traversal.TryVisitUnique(voxel, visited, out _);
            traversal.TryVisitUnique(voxel, visited, out _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TryGetUniquePartition_ReturnsAttachedPartitionOnce()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        Voxel voxel = GetVoxel(world, Vector3d.Zero);
        TestPartition partition = new TestPartition();
        Assert.True(voxel.TryAddPartition(partition));
        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();

        Assert.True(GridTraversal.TryGetUniquePartition(voxel, visited, out TestPartition resolved));

        Assert.Same(partition, resolved);
        Assert.False(GridTraversal.TryGetUniquePartition(voxel, visited, out resolved));
    }

    [Fact]
    public void TryGetUniquePartition_WithReusableExactIdentitySet_ShouldNotAllocateAfterWarmup()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        Voxel voxel = GetVoxel(world, Vector3d.Zero);
        Assert.True(voxel.TryAddPartition(new TestPartition()));
        SwiftHashSet<WorldVoxelIndex> visited = new SwiftHashSet<WorldVoxelIndex>();

        for (int i = 0; i < 16; i++)
        {
            visited.Clear();
            Assert.True(GridTraversal.TryGetUniquePartition(voxel, visited, out TestPartition _));
            Assert.False(GridTraversal.TryGetUniquePartition(voxel, visited, out TestPartition _));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1_024; i++)
        {
            visited.Clear();
            GridTraversal.TryGetUniquePartition(voxel, visited, out TestPartition _);
            GridTraversal.TryGetUniquePartition(voxel, visited, out TestPartition _);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ExactIdentitySet_WithManyUniqueAndDuplicateVoxels_ShouldNotAllocateAfterWarmup()
    {
        using GridWorld world = CreateWorldWithRectangularGrid(GridTopologyMetrics.Rectangular(Fixed64.One));
        var voxels = new SwiftList<Voxel>(256);
        foreach (Voxel voxel in world.ActiveGrids[0].EnumerateVoxels())
        {
            voxels.Add(voxel);
            if (voxels.Count == 256)
                break;
        }

        var visited = new SwiftHashSet<WorldVoxelIndex>(512);
        for (int pass = 0; pass < 16; pass++)
        {
            visited.Clear();
            VisitAllTwice(voxels, visited);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int pass = 0; pass < 64; pass++)
        {
            visited.Clear();
            VisitAllTwice(voxels, visited);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
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

    private static void VisitAllTwice(
        SwiftList<Voxel> voxels,
        SwiftHashSet<WorldVoxelIndex> visited)
    {
        for (int repeat = 0; repeat < 2; repeat++)
        {
            for (int i = 0; i < voxels.Count; i++)
                visited.Add(voxels[i].WorldIndex);
        }
    }

    private static HashCollidingVoxel CreateHashCollidingVoxel(VoxelGrid grid, VoxelIndex index)
    {
        HashCollidingVoxel voxel = new HashCollidingVoxel();
        voxel.Initialize(
            new WorldVoxelIndex(grid.World!.SpawnToken, grid.GridIndex, grid.SpawnToken, index),
            grid.GetWorldPosition(index),
            scanCellKey: 0,
            isBoundaryVoxel: false,
            gridVersion: grid.Version);
        return voxel;
    }

    private sealed class HashCollidingVoxel : Voxel
    {
        public override int GetHashCode() => 1;
    }
}
