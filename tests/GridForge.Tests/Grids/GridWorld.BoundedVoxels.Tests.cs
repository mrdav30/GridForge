using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridWorldBoundedVoxelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionMatchesConventionalCoverageAndStopsBeforeEveryLimit(bool planar)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(new Vector3d(-2, 0, -2), new Vector3d(2, 0, 2)), out ushort gridIndex));
        Assert.True(world.TryGetGrid(gridIndex, out VoxelGrid grid));
        Fixed64 wide = Fixed64.FromFraction(3, 4);
        Fixed64 narrow = Fixed64.FromFraction(1, 4);
        Vector3d minimum = new(-wide, Fixed64.Zero, -narrow);
        Vector3d maximum = new(wide, Fixed64.Zero, narrow);
        SwiftList<Voxel> conventional = new();
        GridTracer.GetCoveredVoxelsInto(world, minimum, maximum, conventional);
        Voxel[] results = new Voxel[25];

        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            minimum, maximum, planar, 100, results, out ulong fullWork, out int fullCount, out ulong sequence));
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CheckBoundedVoxelSnapshot(sequence));
        Assert.True(fullCount > 1);
        Assert.Equal(fullWork, 1UL + (ulong)conventional.Count + (ulong)fullCount);
        if (planar)
            Assert.True(fullCount < conventional.Count);
        int index = 0;
        foreach (Voxel voxel in conventional)
        {
            if (!planar || GridTraversal.IsPlanarPositionInPaddedBounds(
                    GridPlane2d.FromWorld(minimum), GridPlane2d.FromWorld(maximum),
                    GridTopologyMetricUtility.GetPlanarMaxCellEdge(grid), voxel.WorldPosition))
            {
                Assert.Same(voxel, results[index++]);
            }
        }
        Assert.Equal(fullCount, index);

        Array.Clear(results, 0, fullCount);
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            minimum, maximum, planar, fullWork, results.AsSpan(0, fullCount),
            out ulong exactWork, out int exactCount, out _));
        Assert.Equal(fullWork, exactWork);
        Assert.Equal(fullCount, exactCount);
        Array.Clear(results, 0, exactCount);

        Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded, world.CollectCoveredVoxelsBounded(
            minimum, maximum, planar, fullWork - 1, results, out ulong shortWork, out int written, out _));
        Assert.Equal(fullWork - 1, shortWork);
        Assert.InRange(written, 1, fullCount);
        Assert.All(results.AsSpan(written).ToArray(), Assert.Null);
        Array.Clear(results, 0, written);

        Assert.Equal(GridBoundedVoxelStatus.CapacityExceeded, world.CollectCoveredVoxelsBounded(
            minimum, maximum, planar, fullWork, results.AsSpan(0, fullCount - 1),
            out ulong capacityWork, out int capacityCount, out _));
        Assert.InRange(capacityWork, 2UL, fullWork - 1);
        Assert.Equal(fullCount - 1, capacityCount);
        Assert.Null(results[capacityCount]);
    }

    [Fact]
    public void CollectionChargesVacantGridSlotsAndChecksPublicationWithoutWaiting()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 0, Span<Voxel>.Empty, out ulong emptyWork, out int emptyCount, out ulong oldSequence));
        Assert.Equal(0UL, emptyWork);
        Assert.Equal(0, emptyCount);

        GridBoundedVoxelStatus duringPublish = GridBoundedVoxelStatus.Completed;
        world.OnChangeCommitted += _ => duringPublish = world.CheckBoundedVoxelSnapshot(oldSequence);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out ushort first));
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.One, Vector3d.One), out _));
        Assert.True(world.TryRemoveGrid(first));
        Assert.Equal(GridBoundedVoxelStatus.WorldNotReady, duringPublish);
        Assert.Equal(GridBoundedVoxelStatus.Stale, world.CheckBoundedVoxelSnapshot(oldSequence));

        Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded, world.CollectCoveredVoxelsBounded(
            Vector3d.One, Vector3d.One, false, 1, Span<Voxel>.Empty, out ulong work, out int count, out _));
        Assert.Equal(1UL, work);
        Assert.Equal(0, count);

        world.Reset(deactivate: true);
        Assert.Equal(GridBoundedVoxelStatus.WorldNotReady, world.CheckBoundedVoxelSnapshot(oldSequence));
        Assert.Equal(GridBoundedVoxelStatus.WorldNotReady, world.CollectCoveredVoxelsBounded(
            Vector3d.One, Vector3d.One, false, 1, Span<Voxel>.Empty, out _, out _, out _));
    }

    [Fact]
    public void OverlappingGridsKeepDistinctVoxelsInGridSlotOrder()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.One), out ushort first));
        Assert.True(world.TryAddGrid(new GridConfiguration(-Vector3d.One, Vector3d.Zero), out ushort second));
        Voxel[] results = new Voxel[2];

        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 6, results, out ulong work, out int count, out _));
        Assert.Equal(6UL, work);
        Assert.Equal(2, count);
        Assert.Equal(first, results[0].GridIndex);
        Assert.Equal(second, results[1].GridIndex);
        Assert.NotSame(results[0], results[1]);
    }

    [Fact]
    public void CollectionRejectsUnrepresentableBoundsAndUnsupportedIntersectingStorage()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(
            Vector3d.Zero, Vector3d.One, storageKind: GridStorageKind.Sparse), out _));

        Vector3d maximum = Vector3d.One * Fixed64.MaxValue;
        Assert.Equal(GridBoundedVoxelStatus.Unrepresentable, world.CollectCoveredVoxelsBounded(
            maximum, maximum, false, 1, Span<Voxel>.Empty, out ulong work, out int count, out _));
        Assert.Equal(0UL, work);
        Assert.Equal(0, count);

        Assert.Equal(GridBoundedVoxelStatus.UnsupportedGrid, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 1, Span<Voxel>.Empty, out work, out count, out _));
        Assert.Equal(1UL, work);
        Assert.Equal(0, count);
    }

    [Fact]
    public void PartitionLookupReservesOverflowInspectionsAndReturnsAnExactMatch()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel voxel));
        Assert.Equal(GridBoundedVoxelStatus.Completed, voxel.TryGetPartitionBounded<TestPartition>(
            0, out ulong work, out TestPartition absent));
        Assert.Equal(0UL, work);
        Assert.Null(absent);

        Assert.True(voxel.TryAddPartition(new PartitionFamilyA.SharedPartition()));
        Assert.True(voxel.TryAddPartition(new PartitionFamilyB.SharedPartition()));
        TestPartition expected = new();
        Assert.True(voxel.TryAddPartition(expected));

        Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded, voxel.TryGetPartitionBounded<TestPartition>(
            2, out work, out absent));
        Assert.Equal(0UL, work);
        Assert.Null(absent);
        Assert.Equal(GridBoundedVoxelStatus.Completed, voxel.TryGetPartitionBounded<TestPartition>(
            3, out work, out TestPartition present));
        Assert.Equal(3UL, work);
        Assert.Same(expected, present);
    }
}
