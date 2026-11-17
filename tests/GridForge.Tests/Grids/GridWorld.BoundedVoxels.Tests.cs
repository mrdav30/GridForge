using System;
using System.Linq;
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
    public void CollectionOrdersAndRemovesIdenticalBoundsByExactTopology()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        GridConfiguration flat = new(Vector3d.Zero, Vector3d.Zero, topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.FlatTop));
        GridConfiguration pointy = new(Vector3d.Zero, Vector3d.Zero, topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop));
        Assert.True(world.TryAddGrid(pointy, out ushort pointyIndex));
        Assert.True(world.TryAddGrid(flat, out ushort flatIndex));
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out ushort rectangularIndex));
        Assert.False(world.TryAddGrid(flat, out _));

        var results = new Voxel[3];
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 9, results, out ulong work, out int count, out _));
        Assert.Equal(9UL, work);
        Assert.Equal(3, count);
        Assert.Equal(new[] { rectangularIndex, flatIndex, pointyIndex }, results.Select(voxel => voxel.GridIndex));

        Array.Clear(results);
        Assert.True(world.TryRemoveGrid(flatIndex));
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 6, results, out work, out count, out _));
        Assert.Equal(6UL, work);
        Assert.Equal(2, count);
        Assert.Equal(rectangularIndex, results[0].GridIndex);
        Assert.Equal(pointyIndex, results[1].GridIndex);
        Assert.Null(results[2]);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionRejectsUnrepresentableBoundsBeforeInspectingStorage(bool positive)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(
            Vector3d.Zero, Vector3d.One, storageKind: GridStorageKind.Sparse), out _));

        Vector3d extreme = Vector3d.One * (positive ? Fixed64.MaxValue : Fixed64.MinValue);
        Assert.Equal(GridBoundedVoxelStatus.Unrepresentable, world.CollectCoveredVoxelsBounded(
            extreme, extreme, false, 1, Span<Voxel>.Empty, out ulong work, out int count, out _));
        Assert.Equal(0UL, work);
        Assert.Equal(0, count);

        Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 1, Span<Voxel>.Empty, out work, out count, out _));
        Assert.Equal(1UL, work);
        Assert.Equal(0, count);
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 2, Span<Voxel>.Empty, out work, out count, out _));
        Assert.Equal(2UL, work);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.RectangularPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.PointyTop)]
    [InlineData(GridStorageKind.Dense, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    [InlineData(GridStorageKind.Sparse, GridTopologyKind.HexPrism, HexOrientation.FlatTop)]
    public void NativeCoverageMatchesConventionalResultsAndReplaysAtExactLimits(
        GridStorageKind storage, GridTopologyKind topology, HexOrientation orientation)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = topology == GridTopologyKind.HexPrism
            ? GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation)
            : GridTopologyMetrics.Rectangular(Fixed64.One);
        Vector3d max = topology == GridTopologyKind.HexPrism
            ? HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(3, 1, 3), metrics)
            : new Vector3d(3, 1, 3);
        GridConfiguration configuration = new(Vector3d.Zero, max,
            topologyKind: topology, topologyMetrics: metrics, storageKind: storage);
        VoxelIndex[] authored = [new(0, 0, 0), new(1, 0, 1), new(2, 0, 1), new(1, 1, 1), new(2, 1, 2)];
        Assert.True(storage == GridStorageKind.Sparse
            ? world.TryAddGrid(configuration, authored, out ushort gridIndex)
            : world.TryAddGrid(configuration, out gridIndex));
        Assert.True(world.TryGetGrid(gridIndex, out VoxelGrid grid));

        foreach (bool planar in new[] { false, true })
        {
            Vector3d minimum = Vector3d.Zero;
            Vector3d maximum = grid.GetWorldPosition(new VoxelIndex(2, planar ? 0 : 1, 2));
            SwiftList<Voxel> conventional = new();
            GridTracer.GetCoveredVoxelsInto(world, minimum, maximum, conventional);
            Voxel[] expected = conventional.Where(voxel => !planar || GridTraversal.IsPlanarPositionInPaddedBounds(
                    GridPlane2d.FromWorld(minimum), GridPlane2d.FromWorld(maximum),
                    GridTopologyMetricUtility.GetPlanarMaxCellEdge(grid), voxel.WorldPosition))
                .OrderBy(voxel => voxel.Index).ToArray();
            Assert.True(expected.Length > 1);

            var results = new Voxel[expected.Length];
            Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
                minimum, maximum, planar, ulong.MaxValue, results, out ulong fullWork, out int fullCount, out _));
            Assert.Equal(expected.Length, fullCount);
            Assert.Equal(expected, results);

            if (storage == GridStorageKind.Sparse)
            {
                VoxelIndex removed = new(3, 1, 3);
                Assert.True(grid.TryAddVoxel(removed, out _));
                Assert.True(grid.TryRemoveVoxel(removed));
            }

            Array.Clear(results);
            Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
                minimum, maximum, planar, fullWork, results, out ulong exactWork, out int exactCount, out _));
            Assert.Equal(fullWork, exactWork);
            Assert.Equal(fullCount, exactCount);
            Assert.Equal(expected, results);

            Array.Clear(results);
            Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded, world.CollectCoveredVoxelsBounded(
                minimum, maximum, planar, fullWork - 1, results, out ulong shortWork, out int written, out _));
            Assert.InRange(shortWork, 1UL, fullWork - 1);
            Assert.Equal(expected.AsSpan(0, written).ToArray(), results.AsSpan(0, written).ToArray());
            Assert.All(results.AsSpan(written).ToArray(), Assert.Null);

            Array.Clear(results);
            Assert.Equal(GridBoundedVoxelStatus.CapacityExceeded, world.CollectCoveredVoxelsBounded(
                minimum, maximum, planar, fullWork, results.AsSpan(0, fullCount - 1),
                out _, out int capacityCount, out _));
            Assert.Equal(fullCount - 1, capacityCount);
            Assert.Null(results[capacityCount]);
        }
    }

    [Theory]
    [InlineData(0, 5UL, 4UL, true)]
    [InlineData(1, 4UL, 2UL, false)]
    [InlineData(8, 2UL, 1UL, false)]
    public void SparseAddressChargesSourceComparisonsOnlyForItsLiveBlock(
        int x, ulong expectedWork, ulong expectedShortWork, bool containsVoxel)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Vector3d maximum = new(8, 0, 0);
        GridConfiguration config = new(Vector3d.Zero, maximum, storageKind: GridStorageKind.Sparse);
        VoxelIndex[] authored = [new(0, 0, 0), new(2, 0, 0), new(4, 0, 0)];
        Assert.True(world.TryAddGrid(config, authored, out _));

        Vector3d point = new(x, 0, 0);
        Voxel[] results = new Voxel[1];
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            point, point, false, expectedWork, results, out ulong work, out int count, out _));
        Assert.Equal(expectedWork, work);
        Assert.Equal(containsVoxel ? 1 : 0, count);

        Array.Clear(results);
        Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded, world.CollectCoveredVoxelsBounded(
            point, point, false, expectedWork - 1, results, out work, out count, out _));
        Assert.Equal(expectedShortWork, work);
        Assert.Equal(0, count);
        Assert.Null(results[0]);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexCoverageRejectsAnEmptyAxialRangeInsideCoarseBounds(HexOrientation orientation)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        bool pointy = orientation == HexOrientation.PointyTop;
        Vector3d maximum = HexCoordinateUtility.AxialToWorldOffset(
            new VoxelIndex(pointy ? 3 : 10, 0, pointy ? 10 : 3), metrics);
        GridConfiguration config = new(Vector3d.Zero, maximum,
            topologyKind: GridTopologyKind.HexPrism, topologyMetrics: metrics);
        Assert.True(world.TryAddGrid(config, out _));

        Vector3d point = pointy
            ? new Vector3d(maximum.X, Fixed64.Zero, Fixed64.Zero)
            : new Vector3d(Fixed64.Zero, Fixed64.Zero, maximum.Z);
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            point, point, false, 1, Span<Voxel>.Empty, out ulong work, out int count, out _));
        Assert.Equal(1UL, work);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, -1000)]
    [InlineData(HexOrientation.FlatTop, 1000)]
    public void TinyHexCoverageIncludesTheActualForwardLatticeCenter(HexOrientation orientation, int translation)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(6), Fixed64.One, orientation);
        Vector3d min = new(translation, 0, translation);
        Vector3d max = min + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(120, 0, 120), metrics);
        GridConfiguration config = new(min, max, topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics, storageKind: GridStorageKind.Sparse);
        VoxelIndex target = new(100, 0, 100);
        Assert.True(world.TryAddGrid(config, new[] { target }, out ushort gridIndex));
        Assert.True(world.TryGetGrid(gridIndex, out VoxelGrid grid));
        Assert.True(grid.TryGetVoxel(target, out Voxel expected));

        Vector3d center = expected.WorldPosition;
        SwiftList<Voxel> conventional = new();
        GridTracer.GetCoveredVoxelsInto(world, center, center, conventional);
        Assert.Same(expected, Assert.Single(conventional));
        Voxel[] results = new Voxel[1];
        Assert.Equal(GridBoundedVoxelStatus.Completed, world.CollectCoveredVoxelsBounded(
            center, center, false, 150, results, out _, out int count, out _));
        Assert.Equal(1, count);
        Assert.Same(expected, results[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectionRejectsNestedGridAccess(bool write)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        var gridLock = Assert.IsType<ReaderWriterLockSlim>(typeof(GridWorld)
            .GetField("_gridLock", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(world));
        if (write)
            gridLock.EnterWriteLock();
        else
            gridLock.EnterReadLock();

        try
        {
            AssertNoWorldSnapshot(world);
        }
        finally
        {
            if (write)
                gridLock.ExitWriteLock();
            else
                gridLock.ExitReadLock();
        }
    }

    [Fact]
    public async Task CollectionRejectsContendedPublication()
    {
        using var world = GridWorldTestFactory.CreateWorld();
        await CheckUnderContention(world.ChangeSyncRoot, () => AssertNoWorldSnapshot(world));
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

        await CheckUnderContention(partitionLock, () =>
        {
            Assert.Equal(GridBoundedVoxelStatus.WorldNotReady,
                voxel.TryGetPartitionBounded<TestPartition>(4, out ulong work, out var hit));
            Assert.Equal(0UL, work);
            Assert.Null(hit);
        });
    }

    private static void AssertNoWorldSnapshot(GridWorld world)
    {
        Assert.Equal(GridBoundedVoxelStatus.WorldNotReady, world.CollectCoveredVoxelsBounded(
            Vector3d.Zero, Vector3d.Zero, false, 1, Span<Voxel>.Empty, out ulong work, out int count, out ulong sequence));
        Assert.Equal(0UL, work);
        Assert.Equal(0, count);
        Assert.Equal(0UL, sequence);
    }

    private static async Task CheckUnderContention(object syncRoot, Action assertLookup)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        TimeSpan timeout = TimeSpan.FromSeconds(5);
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        Task owner = Task.Run(() =>
        {
            lock (syncRoot)
            {
                entered.Set();
                release.Wait(timeout, cancellation);
            }
        }, cancellation);

        try
        {
            Assert.True(entered.Wait(timeout, cancellation));
            assertLookup();
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

        Assert.Equal(GridBoundedVoxelStatus.Completed,
            absent.TryGetPartitionBounded<TestPartition, PartitionFamilyA.SharedPartition, PartitionFamilyB.SharedPartition>(
                1, out ulong missingWork, out var missingWithExclusions));
        Assert.Equal(1UL, missingWork);
        Assert.Null(missingWithExclusions);
        Assert.Equal(GridBoundedVoxelStatus.Completed,
            attached.TryGetPartitionBounded<TestPartition, PartitionFamilyA.SharedPartition, PartitionFamilyB.SharedPartition>(
                1, out ulong foundWork, out var foundWithExclusions));
        Assert.Equal(1UL, foundWork);
        Assert.Same(expectedPartition, foundWithExclusions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PartitionLookupExcludesExactCacheTypesWhileChargingOtherPayloads(bool requestedAttached, bool hostInOverflow)
    {
        using var world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Right), out _));
        Assert.True(world.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel fresh));
        Assert.True(world.TryGetGridAndVoxel(Vector3d.Right, out _, out Voxel retained));

        Assert.True(fresh.TryAddPartition(new HostPartition()));
        Assert.True(retained.TryAddPartition(new PartitionFamilyB.SharedPartition()));
        if (!hostInOverflow)
            Assert.True(retained.TryAddPartition(new HostPartition()));
        Assert.True(retained.TryAddPartition(new TestPartition()));
        if (hostInOverflow)
            Assert.True(retained.TryAddPartition(new HostPartition()));
        foreach (Voxel voxel in new[] { fresh, retained })
        {
            var requested = requestedAttached ? new PartitionFamilyA.SharedPartition() : null;
            if (requested != null)
                Assert.True(voxel.TryAddPartition(requested));

            Assert.Equal(GridBoundedVoxelStatus.WorkBudgetExceeded,
                voxel.TryGetPartitionBounded<PartitionFamilyA.SharedPartition, PartitionFamilyB.SharedPartition, TestPartition>(
                    1, out ulong work, out var found));
            Assert.Equal(1UL, work);
            Assert.Null(found);

            Assert.Equal(GridBoundedVoxelStatus.Completed,
                voxel.TryGetPartitionBounded<PartitionFamilyA.SharedPartition, PartitionFamilyB.SharedPartition, TestPartition>(
                    2, out work, out found));
            Assert.Equal(2UL, work);
            Assert.Same(requested, found);
        }
    }

    private sealed class HostPartition : TestPartition { }
}
