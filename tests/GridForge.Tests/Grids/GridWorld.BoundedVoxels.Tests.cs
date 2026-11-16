using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
    public void CollectionChecksPublicationWithoutWaiting()
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

        Assert.Equal(GridBoundedVoxelStatus.CapacityExceeded, world.CollectCoveredVoxelsBounded(
            Vector3d.One, Vector3d.One, false, 2, Span<Voxel>.Empty, out ulong work, out int count, out _));
        Assert.Equal(2UL, work);
        Assert.Equal(0, count);

        world.Reset(deactivate: true);
        Assert.Equal(GridBoundedVoxelStatus.WorldNotReady, world.CheckBoundedVoxelSnapshot(oldSequence));
        Assert.Equal(GridBoundedVoxelStatus.WorldNotReady, world.CollectCoveredVoxelsBounded(
            Vector3d.One, Vector3d.One, false, 1, Span<Voxel>.Empty, out _, out _, out _));
    }

    [Fact]
    public void OverlappingGridsReplayAcrossReorderedSlotsAndRemovedGrids()
    {
        using var live = GridWorldTestFactory.CreateWorld();
        using var rebuilt = GridWorldTestFactory.CreateWorld();
        var lower = new GridConfiguration(-Vector3d.One, Vector3d.Zero);
        var upper = new GridConfiguration(Vector3d.Zero, Vector3d.One);
        Assert.True(live.TryAddGrid(new GridConfiguration(Vector3d.One, Vector3d.One), out ushort removed));
        Assert.True(live.TryAddGrid(upper, out ushort liveUpper));
        Assert.True(live.TryAddGrid(lower, out ushort liveLower));
        Assert.True(live.TryRemoveGrid(removed));
        Assert.True(rebuilt.TryAddGrid(lower, out ushort rebuiltLower));
        Assert.True(rebuilt.TryAddGrid(upper, out ushort rebuiltUpper));

        foreach (ulong limit in new[] { 3UL, 5UL, 6UL })
        {
            Voxel[] liveResults = new Voxel[2];
            Voxel[] rebuiltResults = new Voxel[2];
            var expected = limit == 6 ? GridBoundedVoxelStatus.Completed : GridBoundedVoxelStatus.WorkBudgetExceeded;
            Assert.Equal(expected, live.CollectCoveredVoxelsBounded(
                Vector3d.Zero, Vector3d.Zero, false, limit, liveResults, out ulong liveWork, out int liveCount, out _));
            Assert.Equal(expected, rebuilt.CollectCoveredVoxelsBounded(
                Vector3d.Zero, Vector3d.Zero, false, limit, rebuiltResults, out ulong rebuiltWork, out int rebuiltCount, out _));
            Assert.Equal(limit, liveWork);
            Assert.Equal(liveWork, rebuiltWork);
            Assert.Equal(limit == 6 ? 2 : 1, liveCount);
            Assert.Equal(liveCount, rebuiltCount);
            Assert.Equal(liveLower, liveResults[0].GridIndex);
            Assert.Equal(rebuiltLower, rebuiltResults[0].GridIndex);
            if (liveCount == 2)
            {
                Assert.Equal(liveUpper, liveResults[1].GridIndex);
                Assert.Equal(rebuiltUpper, rebuiltResults[1].GridIndex);
                Assert.NotSame(liveResults[0], liveResults[1]);
            }
        }
    }

    [Fact]
    public void CollectionReleasesItsGridOrderOnResetBeforeSlotReuse()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(-Vector3d.One, Vector3d.Zero), out ushort removed));
        world.Reset();
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 0, Span<Voxel>.Empty, out ulong work, out int count, out _));
        Assert.Equal(0UL, work);
        Assert.Equal(0, count);

        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.One), out ushort replacement));
        Assert.Equal(removed, replacement);
        Voxel[] results = new Voxel[1];
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 3, results, out work, out count, out _));
        Assert.Equal(3UL, work);
        Assert.Equal(1, count);
        Assert.Equal(replacement, results[0].GridIndex);
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
    public async Task PartitionLookupUnderContentionReturnsWithoutSpendingAllowance()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel voxel));
        object partitionLock = typeof(Voxel)
            .GetField("_partitionLock", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(voxel);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        Task owner = Task.Run(() =>
        {
            lock (partitionLock)
            {
                entered.Set();
                release.Wait(timeout, cancellation);
            }
        }, cancellation);

        try
        {
            Assert.True(entered.Wait(timeout, cancellation));
            Assert.Equal(GridBoundedVoxelStatus.WorldNotReady,
                voxel.TryGetPartitionBounded<TestPartition>(4, out ulong work, out var hit));
            Assert.Equal(0UL, work);
            Assert.Null(hit);
        }
        finally
        {
            release.Set();
            await owner;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartitionLookupReplaysRequestedTypeWorkAcrossAbsentAndAttachedPayloads(bool overflow)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Right), out _));
        Assert.True(world.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel absent));
        Assert.True(world.TryGetGridAndVoxel(Vector3d.Right, out _, out Voxel attached));
        if (overflow)
        {
            foreach (Voxel voxel in new[] { absent, attached })
            {
                Assert.True(voxel.TryAddPartition(new PartitionFamilyA.SharedPartition()));
                Assert.True(voxel.TryAddPartition(new PartitionFamilyB.SharedPartition()));
            }
        }

        var expectedPartition = new TestPartition();
        Assert.True(attached.TryAddPartition(expectedPartition));
        ulong exact = overflow ? 3UL : 1UL;
        for (ulong limit = 0; limit <= exact; limit++)
        {
            var expectedStatus = limit == exact
                ? GridBoundedVoxelStatus.Completed : GridBoundedVoxelStatus.WorkBudgetExceeded;
            Assert.Equal(expectedStatus,
                absent.TryGetPartitionBounded<TestPartition>(limit, out ulong absentWork, out var missing));
            Assert.Equal(expectedStatus,
                attached.TryGetPartitionBounded<TestPartition>(limit, out ulong attachedWork, out var found));
            Assert.Equal(limit, absentWork);
            Assert.Equal(absentWork, attachedWork);
            Assert.Null(missing);
            Assert.Same(limit == exact ? expectedPartition : null, found);
        }
    }
}
