using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridBoundaryContactCursorTests
{
    [Fact]
    public void Advance_ShouldBoundFirstChunkAndDiscoverCanonicalOneToManyContacts()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(4, 4, 4)),
            out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(2, 2, 2), new Vector3d(2, 2, 2)),
            out _));

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] chunk = new VoxelContactManifold[1];
        world.BeginBoundaryContacts(cursor);

        GridBoundaryContactCursorStatus status = world.AdvanceBoundaryContacts(
            cursor,
            chunk,
            candidateProbeLimit: 1,
            outputLimit: 1,
            out int firstProbes,
            out int firstCount);

        Assert.Equal(GridBoundaryContactCursorStatus.More, status);
        Assert.Equal(0, firstCount);
        Assert.Equal(1, firstProbes);
        Assert.Equal(1UL, cursor.CandidateOrdinal);

        VoxelContactManifold[] contacts = new VoxelContactManifold[27];
        int contactCount = 0;
        while (status == GridBoundaryContactCursorStatus.More)
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                chunk,
                candidateProbeLimit: 1,
                outputLimit: 1,
                out int probes,
                out int count);
            Assert.InRange(probes, 0, 1);
            Assert.InRange(count, 0, 1);
            if (count != 0)
                contacts[contactCount++] = chunk[0];
        }

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(27, contactCount);
        AssertCanonicalOrder(contacts);
    }

    [Fact]
    public void Advance_ShouldUseMaintainedSpatialPairsInsteadOfScanningGridSlots()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        for (int i = 0; i < 32; i++)
        {
            Vector3d center = new Vector3d(100 + i * 10, 0, 0);
            Assert.True(world.TryAddGrid(new GridConfiguration(center, center), out _));
        }

        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        world.BeginBoundaryContacts(cursor);

        GridBoundaryContactCursorStatus status = world.AdvanceBoundaryContacts(
            cursor,
            output,
            candidateProbeLimit: 5,
            outputLimit: 1,
            out int candidateProbes,
            out int outputCount);

        Assert.Equal(GridBoundaryContactCursorStatus.More, status);
        Assert.Equal(1, outputCount);
        Assert.Equal(5, candidateProbes);
        Assert.Equal(5UL, cursor.CandidateOrdinal);

        status = world.AdvanceBoundaryContacts(
            cursor,
            output,
            candidateProbeLimit: 1,
            outputLimit: 1,
            out candidateProbes,
            out outputCount);

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(0, outputCount);
        Assert.Equal(0, candidateProbes);
        Assert.Equal(5UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void Advance_ShouldEnumerateTopologyAddressesWithoutRequiringSparsePhysicalVoxels()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero, storageKind: GridStorageKind.Sparse),
            out ushort sparseIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out ushort denseIndex));
        Assert.Equal(0, world.ActiveGrids[sparseIndex].ConfiguredVoxelCount);

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        world.BeginBoundaryContacts(cursor);

        GridBoundaryContactCursorStatus status = world.AdvanceBoundaryContacts(
            cursor,
            output,
            candidateProbeLimit: 5,
            outputLimit: 1,
            out int candidateProbes,
            out int outputCount);

        Assert.Equal(GridBoundaryContactCursorStatus.More, status);
        Assert.Equal(1, outputCount);
        Assert.Equal(5, candidateProbes);
        Assert.Equal(VoxelContactKind.Face, output[0].Kind);
        Assert.Equal(sparseIndex, output[0].Source.GridIndex);
        Assert.Equal(denseIndex, output[0].Target.GridIndex);
    }

    [Fact]
    public void Advance_ShouldStaleAndResetAfterCommittedDirectoryOrGridMutation()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        world.BeginBoundaryContacts(cursor);
        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 5,
                outputLimit: 1,
                out _,
                out int firstCount));
        Assert.Equal(1, firstCount);

        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(100, 0, 0), new Vector3d(100, 0, 0)),
            out _));

        GridBoundaryContactCursorStatus stale = world.AdvanceBoundaryContacts(
            cursor,
            output,
            candidateProbeLimit: 1,
            outputLimit: 1,
            out int staleProbes,
            out int staleCount);

        Assert.Equal(GridBoundaryContactCursorStatus.Stale, stale);
        Assert.Equal(0, staleCount);
        Assert.Equal(0, staleProbes);
        Assert.Equal(0UL, cursor.CandidateOrdinal);

        world.BeginBoundaryContacts(cursor);
        Assert.Equal(GridBoundaryContactCursorStatus.More, cursor.Status);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void Advance_ShouldAllocateNothingAndRetainNoVoxelReferencesAfterWarmup()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        Assert.Equal(GridBoundaryContactCursorStatus.Complete, Drain(world, cursor, output));

        long before = GC.GetAllocatedBytesForCurrentThread();
        GridBoundaryContactCursorStatus status = Drain(world, cursor, output);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(0, allocated);
        Assert.DoesNotContain(
            typeof(GridBoundaryContactCursor).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic),
            field => typeof(Voxel).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void Advance_ShouldDiscoverTouchingCellsWithDifferentMetricEnvelopes()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        GridTopologyMetrics largeMetrics = GridTopologyMetrics.Rectangular(
            new Fixed64(6),
            new Fixed64(2),
            new Fixed64(2));
        GridTopologyMetrics smallMetrics = GridTopologyMetrics.Rectangular(new Fixed64(2));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero, topologyMetrics: largeMetrics),
            out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(4, 0, 0),
                new Vector3d(4, 0, 0),
                topologyMetrics: smallMetrics),
            out _));

        VoxelContactManifold contact = GetSingleContact(world);

        Assert.Equal(VoxelContactKind.Face, contact.Kind);
        Assert.True(contact.IsPositiveAreaFace);
    }

    [Fact]
    public void Advance_ShouldScanEveryTargetCellInsideALargeSourceEnvelope()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        GridTopologyMetrics largeMetrics = GridTopologyMetrics.Rectangular(
            new Fixed64(6),
            Fixed64.One,
            Fixed64.One);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero, topologyMetrics: largeMetrics),
            out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(2, 0, 0),
                new Vector3d(4, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        var output = new VoxelContactManifold[1];
        int contactCount = 0;
        world.BeginBoundaryContacts(cursor);

        GridBoundaryContactCursorStatus status;
        do
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 16,
                outputLimit: 1,
                out _,
                out int outputCount);
            contactCount += outputCount;
        }
        while (status == GridBoundaryContactCursorStatus.More);

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(2, contactCount);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void Advance_ShouldDiscoverMixedTopologyContactsAtEnvelopeLimit(HexOrientation orientation)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        GridTopologyMetrics rectangularMetrics = GridTopologyMetrics.Rectangular(new Fixed64(2));
        GridTopologyMetrics hexMetrics = GridTopologyMetrics.Hex(Fixed64.One, new Fixed64(2), orientation);
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            hexMetrics,
            Vector3d.Zero,
            default,
            out GridCellPrism centeredHex));
        Fixed64 centerDistance = Fixed64.One + centeredHex.PlanarInradius;
        Vector3d targetCenter = orientation == HexOrientation.PointyTop
            ? new Vector3d(centerDistance, Fixed64.Zero, Fixed64.Zero)
            : new Vector3d(Fixed64.Zero, Fixed64.Zero, centerDistance);

        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero, topologyMetrics: rectangularMetrics),
            out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                targetCenter,
                targetCenter,
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: hexMetrics),
            out _));

        VoxelContactManifold contact = GetSingleContact(world);

        Assert.Equal(VoxelContactKind.Face, contact.Kind);
        Assert.True(contact.IsPositiveAreaFace);
    }

    [Fact]
    public void PairIndex_ShouldRemoveIncidentKeysAndUseCurrentGenerationAfterSlotReuse()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out ushort removedIndex));
        long removedSpawnToken = world.ActiveGrids[removedIndex].SpawnToken;
        Assert.True(world.TryRemoveGrid(removedIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(100, 0, 0), new Vector3d(100, 0, 0)),
            out ushort reusedIndex));
        Assert.Equal(removedIndex, reusedIndex);

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        Assert.Equal(GridBoundaryContactCursorStatus.Complete, Drain(world, cursor, output));

        Assert.True(world.TryRemoveGrid(reusedIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out reusedIndex));
        VoxelContactManifold contact = GetSingleContact(world);

        Assert.NotEqual(removedSpawnToken, contact.Target.GridSpawnToken);
        Assert.Equal(world.ActiveGrids[reusedIndex].SpawnToken, contact.Target.GridSpawnToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_ShouldStaleOnEitherBoundGridLastChangeSequenceMismatch(bool sourceMismatch)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out ushort sourceIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out ushort targetIndex));
        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        world.BeginBoundaryContacts(cursor);
        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(cursor, output, 3, 1, out int pairProbes, out int count));
        Assert.Equal(3, pairProbes);
        Assert.Equal(0, count);

        world.ActiveGrids[sourceMismatch ? sourceIndex : targetIndex].LastChangeSequence++;

        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(cursor, output, 1, 1, out int staleProbes, out count));
        Assert.Equal(0, staleProbes);
        Assert.Equal(0, count);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void Advance_ShouldRejectOutputLimitsLargerThanEitherResultSpan()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        var cursor = new GridBoundaryContactCursor();

        Assert.Equal("outputLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 1,
                out _,
                out _)).ParamName);
        Assert.Equal("outputLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.AdvanceBoundaryContacts(
                cursor,
                Span<GridBoundaryContact>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 1,
                out _,
                out _)).ParamName);
    }

    [Fact]
    public void Advance_ShouldPreserveAPendingContactAcrossAZeroOutputBudget()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        world.BeginBoundaryContacts(cursor);

        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 16,
                outputLimit: 0,
                out int discoveryProbes,
                out int zeroCount));
        Assert.True(discoveryProbes > 0);
        Assert.Equal(0, zeroCount);

        var output = new VoxelContactManifold[1];
        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 0,
                outputLimit: 1,
                out int resumeProbes,
                out int outputCount));
        Assert.Equal(0, resumeProbes);
        Assert.Equal(1, outputCount);
        Assert.Equal(VoxelContactKind.Face, output[0].Kind);
    }

    [Fact]
    public void Advance_ShouldHonorZeroCandidateBudgetBeforeReadingThePairDirectory()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        world.BeginBoundaryContacts(cursor);

        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out int candidateProbes,
                out int outputCount));
        Assert.Equal(0, candidateProbes);
        Assert.Equal(0, outputCount);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void Advance_ShouldSaturateCandidateOrdinalWhileContinuingBoundedWork()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        world.BeginBoundaryContacts(cursor);
        cursor.CandidateOrdinal = ulong.MaxValue;

        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 1,
                outputLimit: 0,
                out int candidateProbes,
                out int outputCount));
        Assert.Equal(1, candidateProbes);
        Assert.Equal(0, outputCount);
        Assert.Equal(ulong.MaxValue, cursor.CandidateOrdinal);
    }

    [Fact]
    public void Advance_ShouldRejectACursorBoundToAnotherWorld()
    {
        using GridWorld first = GridWorldTestFactory.CreateWorld();
        using GridWorld second = GridWorldTestFactory.CreateWorld();
        var cursor = new GridBoundaryContactCursor();
        first.BeginBoundaryContacts(cursor);

        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            second.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out int candidateProbes,
                out int outputCount));
        Assert.Equal(0, candidateProbes);
        Assert.Equal(0, outputCount);
        Assert.Equal(default, cursor.RunStamp);
    }

    [Fact]
    public void BoundaryContacts_ShouldRejectHexEnvelopeCornerFalsePositives()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            Fixed64.One,
            HexOrientation.PointyTop);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: metrics),
            out _));
        Vector3d diagonal = new(
            HexCoordinateUtility.Sqrt3 * new Fixed64(2) - Fixed64.FromFraction(1, 100),
            Fixed64.Zero,
            new Fixed64(4) - Fixed64.FromFraction(1, 100));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                diagonal,
                diagonal,
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: metrics),
            out _));
        var cursor = new GridBoundaryContactCursor();
        var output = new VoxelContactManifold[1];
        int totalContacts = 0;
        world.BeginBoundaryContacts(cursor);

        GridBoundaryContactCursorStatus status;
        do
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 16,
                outputLimit: 1,
                out _,
                out int outputCount);
            totalContacts += outputCount;
        }
        while (status == GridBoundaryContactCursorStatus.More);

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.True(cursor.CandidateOrdinal > 0);
        Assert.Equal(0, totalContacts);
    }

    [Fact]
    public async Task BeginAndAdvance_ShouldWaitForTheCommittedPrefix()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        GridConfiguration selected = new(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out ushort selectedIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        VoxelGrid grid = world.ActiveGrids[selectedIndex];
        Assert.True(grid.TryGetVoxel(default(VoxelIndex), out Voxel voxel));
        var advanceCursor = new GridBoundaryContactCursor();
        world.BeginBoundaryContacts(advanceCursor);
        var beginCursor = new GridBoundaryContactCursor();
        var filteredCursor = new GridBoundaryContactCursor();
        using ManualResetEventSlim handlerEntered = new();
        using ManualResetEventSlim releaseHandler = new();
        void BlockCommittedHandler(GridEventInfo eventInfo)
        {
            if (eventInfo.ChangeKind != GridEventKind.ObstacleAdded)
                return;

            handlerEntered.Set();
            releaseHandler.Wait(TestContext.Current.CancellationToken);
        }

        world.OnChangeCommitted += BlockCommittedHandler;
        Task mutation = Task.Run(
            () => Assert.True(grid.TryAddObstacle(voxel, world.AllocateObstacleToken())),
            TestContext.Current.CancellationToken);
        Assert.True(handlerEntered.Wait(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken));
        Exception beginError = null;
        Exception filteredError = null;
        Exception advanceError = null;
        bool filteredResult = false;
        GridBoundaryContactCursorStatus advanceStatus = default;
        Thread beginThread = new(() =>
        {
            try
            {
                world.BeginBoundaryContacts(beginCursor);
            }
            catch (Exception exception)
            {
                beginError = exception;
            }
        }) { IsBackground = true };
        Thread filteredThread = new(() =>
        {
            try
            {
                filteredResult = world.TryBeginBoundaryContacts(
                    selected.ToGridKey(),
                    filteredCursor);
            }
            catch (Exception exception)
            {
                filteredError = exception;
            }
        }) { IsBackground = true };
        Thread advanceThread = new(() =>
        {
            try
            {
                advanceStatus = world.AdvanceBoundaryContacts(
                    advanceCursor,
                    Span<VoxelContactManifold>.Empty,
                    candidateProbeLimit: 0,
                    outputLimit: 0,
                    out _,
                    out _);
            }
            catch (Exception exception)
            {
                advanceError = exception;
            }
        }) { IsBackground = true };

        try
        {
            beginThread.Start();
            AssertThreadIsWaiting(beginThread);
            filteredThread.Start();
            AssertThreadIsWaiting(filteredThread);
            advanceThread.Start();
            AssertThreadIsWaiting(advanceThread);

            releaseHandler.Set();
            Assert.True(beginThread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(filteredThread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(advanceThread.Join(TimeSpan.FromSeconds(5)));
            await mutation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);

            Assert.Null(beginError);
            Assert.Null(filteredError);
            Assert.Null(advanceError);
            Assert.True(filteredResult);
            Assert.Equal(GridBoundaryContactCursorStatus.Stale, advanceStatus);
            Assert.NotEqual(default, beginCursor.RunStamp);
            Assert.NotEqual(default, filteredCursor.RunStamp);
        }
        finally
        {
            releaseHandler.Set();
            world.OnChangeCommitted -= BlockCommittedHandler;
            if (beginThread.IsAlive)
                beginThread.Join(TimeSpan.FromSeconds(5));
            if (filteredThread.IsAlive)
                filteredThread.Join(TimeSpan.FromSeconds(5));
            if (advanceThread.IsAlive)
                advanceThread.Join(TimeSpan.FromSeconds(5));
            await mutation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void Complete_ShouldRevalidateWithZeroBudgets()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        Assert.Equal(GridBoundaryContactCursorStatus.Complete, Drain(world, cursor, output));

        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            world.AdvanceBoundaryContacts(cursor, Span<VoxelContactManifold>.Empty, 0, 0, out int probes, out int count));
        Assert.Equal(0, probes);
        Assert.Equal(0, count);

        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(100, 0, 0), new Vector3d(100, 0, 0)),
            out _));

        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(cursor, Span<VoxelContactManifold>.Empty, 0, 0, out probes, out count));
        Assert.Equal(0, probes);
        Assert.Equal(0, count);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void PairIndex_ShouldExcludeManyAnisotropicGridsWithDisjointExactEnvelopes()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(100),
            Fixed64.One,
            Fixed64.One);
        for (int i = 0; i < 16; i++)
        {
            Vector3d center = new Vector3d(0, 0, i * 3);
            Assert.True(world.TryAddGrid(
                new GridConfiguration(center, center, topologyMetrics: metrics),
                out _));
        }

        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, Drain(world, cursor, output));
        Assert.Equal(0UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void Advance_ShouldRangeSourceAddressesFromExactTargetEnvelope()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                new Vector3d(9_999, 0, 0),
                storageKind: GridStorageKind.Sparse),
            out ushort sourceIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(10_000, 0, 0),
                new Vector3d(10_000, 0, 0)),
            out ushort targetIndex));
        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[2];
        world.BeginBoundaryContacts(cursor);

        GridBoundaryContactCursorStatus status = world.AdvanceBoundaryContacts(
            cursor,
            output,
            candidateProbeLimit: 5,
            outputLimit: output.Length,
            out int probes,
            out int count);

        Assert.Equal(5, probes);
        Assert.Equal(1, count);
        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(sourceIndex, output[0].Source.GridIndex);
        Assert.Equal(new VoxelIndex(9_999, 0, 0), output[0].Source.VoxelIndex);
        Assert.Equal(targetIndex, output[0].Target.GridIndex);
    }

    [Fact]
    public void FilteredAdvance_ShouldIgnoreUnrelatedPairsAfterTwoIncidentRowProbes()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var selected = new GridConfiguration(
            new Vector3d(1_000, 0, 0),
            new Vector3d(1_000, 0, 0));
        Assert.True(world.TryAddGrid(selected, out _));
        for (int i = 0; i < 16; i++)
        {
            Vector3d center = new Vector3d(i, 0, 0);
            Assert.True(world.TryAddGrid(new GridConfiguration(center, center), out _));
        }

        var cursor = new GridBoundaryContactCursor();

        Assert.True(world.TryBeginBoundaryContacts(selected.ToGridKey(), cursor));
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 2,
                outputLimit: 0,
                out int probes,
                out int outputCount));
        Assert.Equal(2, probes);
        Assert.Equal(0, outputCount);
        Assert.Equal(2UL, cursor.CandidateOrdinal);
    }

    [Fact]
    public void FilteredAdvance_ShouldMergeIncomingThenOutgoingPairsInCanonicalOrder()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var firstLowerConfiguration = new GridConfiguration(
            new Vector3d(-1, 0, 0),
            new Vector3d(-1, 0, 0));
        var secondLowerConfiguration = new GridConfiguration(
            new Vector3d(0, -1, 0),
            new Vector3d(0, -1, 0));
        var selected = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        var firstHigherConfiguration = new GridConfiguration(
            new Vector3d(0, 0, -1),
            new Vector3d(0, 0, -1));
        var secondHigherConfiguration = new GridConfiguration(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0));
        Assert.True(world.TryAddGrid(firstLowerConfiguration, out ushort firstLower));
        Assert.True(world.TryAddGrid(secondLowerConfiguration, out ushort secondLower));
        Assert.True(world.TryAddGrid(selected, out ushort selectedIndex));
        Assert.True(world.TryAddGrid(firstHigherConfiguration, out ushort firstHigher));
        Assert.True(world.TryAddGrid(secondHigherConfiguration, out ushort secondHigher));
        var cursor = new GridBoundaryContactCursor();
        GridBoundaryContact[] output = new GridBoundaryContact[5];

        Assert.True(world.TryBeginBoundaryContacts(selected.ToGridKey(), cursor));
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 32,
                outputLimit: output.Length,
                out int probes,
                out int outputCount));

        Assert.Equal(4, outputCount);
        Assert.Equal(16, probes);
        Assert.Equal(firstLowerConfiguration.ToGridKey(), output[0].SourceConfigurationKey);
        Assert.Equal(selected.ToGridKey(), output[0].TargetConfigurationKey);
        Assert.Equal(firstLower, output[0].Contact.Source.GridIndex);
        Assert.Equal(selectedIndex, output[0].Contact.Target.GridIndex);
        Assert.Equal(secondLowerConfiguration.ToGridKey(), output[1].SourceConfigurationKey);
        Assert.Equal(selected.ToGridKey(), output[1].TargetConfigurationKey);
        Assert.Equal(secondLower, output[1].Contact.Source.GridIndex);
        Assert.Equal(selectedIndex, output[1].Contact.Target.GridIndex);
        Assert.Equal(selected.ToGridKey(), output[2].SourceConfigurationKey);
        Assert.Equal(firstHigherConfiguration.ToGridKey(), output[2].TargetConfigurationKey);
        Assert.Equal(selectedIndex, output[2].Contact.Source.GridIndex);
        Assert.Equal(firstHigher, output[2].Contact.Target.GridIndex);
        Assert.Equal(selected.ToGridKey(), output[3].SourceConfigurationKey);
        Assert.Equal(secondHigherConfiguration.ToGridKey(), output[3].TargetConfigurationKey);
        Assert.Equal(selectedIndex, output[3].Contact.Source.GridIndex);
        Assert.Equal(secondHigher, output[3].Contact.Target.GridIndex);
    }

    [Fact]
    public void FilteredBegins_ShouldShareRunStampAndCanonicalIdentityForBothParticipants()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var first = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        var second = new GridConfiguration(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0));
        Assert.True(world.TryAddGrid(first, out ushort firstIndex));
        Assert.True(world.TryAddGrid(second, out ushort secondIndex));
        var firstCursor = new GridBoundaryContactCursor();
        var secondCursor = new GridBoundaryContactCursor();
        var firstOutput = new GridBoundaryContact[2];
        var secondOutput = new GridBoundaryContact[2];

        Assert.True(world.TryBeginBoundaryContacts(first.ToGridKey(), firstCursor));
        Assert.True(world.TryBeginBoundaryContacts(second.ToGridKey(), secondCursor));
        Assert.NotEqual(default, firstCursor.RunStamp);
        Assert.Equal(firstCursor.RunStamp, secondCursor.RunStamp);
        var exactRevisionCache = new HashSet<GridBoundaryContactRunStamp>
        {
            firstCursor.RunStamp
        };
        Assert.Contains(secondCursor.RunStamp, exactRevisionCache);
        Assert.True(firstCursor.RunStamp.Equals((object)secondCursor.RunStamp));
        Assert.False(firstCursor.RunStamp.Equals(null));
        Assert.False(firstCursor.RunStamp.Equals(world));
        Assert.True(firstCursor.RunStamp == secondCursor.RunStamp);
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            world.AdvanceBoundaryContacts(firstCursor, firstOutput, 16, 2, out _, out int firstCount));
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            world.AdvanceBoundaryContacts(secondCursor, secondOutput, 16, 2, out _, out int secondCount));

        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
        Assert.Equal(first.ToGridKey(), firstOutput[0].SourceConfigurationKey);
        Assert.Equal(second.ToGridKey(), firstOutput[0].TargetConfigurationKey);
        Assert.Equal(firstIndex, firstOutput[0].Contact.Source.GridIndex);
        Assert.Equal(secondIndex, firstOutput[0].Contact.Target.GridIndex);
        Assert.Equal(firstOutput[0].SourceConfigurationKey, secondOutput[0].SourceConfigurationKey);
        Assert.Equal(firstOutput[0].TargetConfigurationKey, secondOutput[0].TargetConfigurationKey);
        Assert.Equal(firstOutput[0].Contact.Source, secondOutput[0].Contact.Source);
        Assert.Equal(firstOutput[0].Contact.Target, secondOutput[0].Contact.Target);
    }

    [Fact]
    public void FilteredBegins_ShouldExposeDifferentRunStampsAcrossInterveningChange()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var selected = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out _));
        var firstCursor = new GridBoundaryContactCursor();
        var secondCursor = new GridBoundaryContactCursor();
        Assert.True(world.TryBeginBoundaryContacts(selected.ToGridKey(), firstCursor));
        GridBoundaryContactRunStamp firstStamp = firstCursor.RunStamp;

        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(100, 0, 0),
                new Vector3d(100, 0, 0)),
            out _));
        Assert.True(world.TryBeginBoundaryContacts(selected.ToGridKey(), secondCursor));

        Assert.NotEqual(firstStamp, secondCursor.RunStamp);
        Assert.True(firstStamp != secondCursor.RunStamp);
        Assert.False(firstStamp == secondCursor.RunStamp);
        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(
                firstCursor,
                Span<GridBoundaryContact>.Empty,
                0,
                0,
                out _,
                out _));
        Assert.Equal(default, firstCursor.RunStamp);
    }

    [Fact]
    public void FilteredAdvance_ShouldDebitIncomingRowOutgoingRowAndPairSeparately()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var selected = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        Assert.True(world.TryBeginBoundaryContacts(selected.ToGridKey(), cursor));

        for (ulong expectedOrdinal = 1; expectedOrdinal <= 3; expectedOrdinal++)
        {
            Assert.Equal(
                GridBoundaryContactCursorStatus.More,
                world.AdvanceBoundaryContacts(
                    cursor,
                    Span<VoxelContactManifold>.Empty,
                    candidateProbeLimit: 1,
                    outputLimit: 0,
                    out int probes,
                    out int outputCount));
            Assert.Equal(1, probes);
            Assert.Equal(0, outputCount);
            Assert.Equal(expectedOrdinal, cursor.CandidateOrdinal);
        }
    }

    [Fact]
    public void FilteredAdvance_ShouldPauseBetweenIncidentPairsAtZeroCandidateBudget()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        GridConfiguration selected = new(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(0, 0, 1), new Vector3d(0, 0, 1)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        var output = new VoxelContactManifold[1];
        Assert.True(world.TryBeginBoundaryContacts(selected.ToGridKey(), cursor));

        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 16,
                outputLimit: 1,
                out _,
                out int firstCount));
        Assert.Equal(1, firstCount);
        ulong ordinalAfterFirstContact = cursor.CandidateOrdinal;

        Assert.Equal(
            GridBoundaryContactCursorStatus.More,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out int pausedProbes,
                out int pausedCount));
        Assert.Equal(0, pausedProbes);
        Assert.Equal(0, pausedCount);
        Assert.Equal(ordinalAfterFirstContact, cursor.CandidateOrdinal);

        int remainingContacts = 0;
        GridBoundaryContactCursorStatus status;
        do
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 16,
                outputLimit: 1,
                out _,
                out int outputCount);
            remainingContacts += outputCount;
        }
        while (status == GridBoundaryContactCursorStatus.More);

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(1, remainingContacts);
    }

    [Fact]
    public void TryBeginFiltered_ShouldFailStaleWithoutPartialStateForMissingKey()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        var cursor = new GridBoundaryContactCursor();
        world.BeginBoundaryContacts(cursor);

        Assert.False(world.TryBeginBoundaryContacts(
            new GridConfiguration(
                new Vector3d(100, 0, 0),
                new Vector3d(100, 0, 0)).ToGridKey(),
            cursor));
        Assert.Equal(GridBoundaryContactCursorStatus.Stale, cursor.Status);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
        Assert.Equal(default, cursor.RunStamp);
        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out int probes,
                out int outputCount));
        Assert.Equal(0, probes);
        Assert.Equal(0, outputCount);
    }

    [Fact]
    public void FilteredComplete_ShouldStaleAfterSelectedGridSlotReuse()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var selected = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out ushort selectedIndex));
        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            DrainFiltered(world, selected.ToGridKey(), cursor, output));
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out _,
                out _));

        Assert.True(world.TryRemoveGrid(selectedIndex));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(
                new Vector3d(100, 0, 0),
                new Vector3d(100, 0, 0)),
            out ushort reusedIndex));
        Assert.Equal(selectedIndex, reusedIndex);

        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out int probes,
                out int outputCount));
        Assert.Equal(0, probes);
        Assert.Equal(0, outputCount);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
        Assert.Equal(default, cursor.RunStamp);
    }

    [Fact]
    public void FilteredComplete_ShouldStaleOnSelectedGridLastChangeSequenceMismatch()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var selected = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out ushort selectedIndex));
        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[1];
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            DrainFiltered(world, selected.ToGridKey(), cursor, output));

        world.ActiveGrids[selectedIndex].LastChangeSequence++;

        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(
                cursor,
                Span<VoxelContactManifold>.Empty,
                candidateProbeLimit: 0,
                outputLimit: 0,
                out int probes,
                out int outputCount));
        Assert.Equal(0, probes);
        Assert.Equal(0, outputCount);
        Assert.Equal(default, cursor.RunStamp);
    }

    [Fact]
    public void FilteredAdvance_ShouldAllocateNothingAfterWarmup()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        var selected = new GridConfiguration(Vector3d.Zero, Vector3d.Zero);
        Assert.True(world.TryAddGrid(selected, out _));
        Assert.True(world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));
        var cursor = new GridBoundaryContactCursor();
        GridBoundaryContact[] output = new GridBoundaryContact[1];
        GridConfigurationKey selectedKey = selected.ToGridKey();
        Assert.Equal(
            GridBoundaryContactCursorStatus.Complete,
            DrainFilteredBindings(world, selectedKey, cursor, output));

        long before = GC.GetAllocatedBytesForCurrentThread();
        GridBoundaryContactCursorStatus status = DrainFilteredBindings(
            world,
            selectedKey,
            cursor,
            output);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(0, allocated);
        Assert.All(
            typeof(GridBoundaryContact).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => Assert.True(field.FieldType.IsValueType));
    }

    [Fact]
    public void PairDirectory_ShouldUseIncidentAdjacencyInsteadOfGlobalPairArray()
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        Assert.Null(typeof(GridWorld).GetField("_boundaryContactPairs", Flags));
        Assert.NotNull(typeof(GridWorld).GetField("_boundaryContactTargetsBySource", Flags));
        Assert.NotNull(typeof(GridWorld).GetField("_boundaryContactSourcesByTarget", Flags));
    }

    [Fact]
    public void PairDirectory_ShouldNotAllocateWhenLastPairIsRemovedAndReaddedAfterWarmup()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        GridConfiguration neighbor = new GridConfiguration(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0));
        GridConfiguration unrelated = new GridConfiguration(
            new Vector3d(100, 0, 0),
            new Vector3d(100, 0, 0));

        for (int i = 0; i < 2; i++)
        {
            Assert.True(world.TryAddGrid(neighbor, out ushort warmIndex));
            Assert.True(world.TryRemoveGrid(warmIndex));
            Assert.True(world.TryAddGrid(unrelated, out warmIndex));
            Assert.True(world.TryRemoveGrid(warmIndex));
        }

        long baselineAllocated = MeasureGridCycle(world, unrelated);
        long contactAllocated = MeasureGridCycle(world, neighbor);

        Assert.True(baselineAllocated >= 0);
        Assert.True(
            contactAllocated <= baselineAllocated,
            $"Contact churn allocated {contactAllocated} bytes versus {baselineAllocated} baseline bytes.");
    }

    private static long MeasureGridCycle(GridWorld world, GridConfiguration configuration)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        if (!world.TryAddGrid(configuration, out ushort index) || !world.TryRemoveGrid(index))
            return -1;

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static VoxelContactManifold GetSingleContact(GridWorld world)
    {
        var cursor = new GridBoundaryContactCursor();
        VoxelContactManifold[] output = new VoxelContactManifold[2];
        world.BeginBoundaryContacts(cursor);
        GridBoundaryContactCursorStatus status = world.AdvanceBoundaryContacts(
            cursor,
            output,
            candidateProbeLimit: 16,
            outputLimit: output.Length,
            out _,
            out int outputCount);

        Assert.Equal(GridBoundaryContactCursorStatus.Complete, status);
        Assert.Equal(1, outputCount);
        return output[0];
    }

    private static GridBoundaryContactCursorStatus Drain(
        GridWorld world,
        GridBoundaryContactCursor cursor,
        VoxelContactManifold[] output)
    {
        world.BeginBoundaryContacts(cursor);
        GridBoundaryContactCursorStatus status;
        do
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 8,
                outputLimit: 1,
                out _,
                out _);
        }
        while (status == GridBoundaryContactCursorStatus.More);

        return status;
    }

    private static GridBoundaryContactCursorStatus DrainFiltered(
        GridWorld world,
        GridConfigurationKey configurationKey,
        GridBoundaryContactCursor cursor,
        VoxelContactManifold[] output)
    {
        if (!world.TryBeginBoundaryContacts(configurationKey, cursor))
            return cursor.Status;

        GridBoundaryContactCursorStatus status;
        do
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 8,
                outputLimit: 1,
                out _,
                out _);
        }
        while (status == GridBoundaryContactCursorStatus.More);

        return status;
    }

    private static GridBoundaryContactCursorStatus DrainFilteredBindings(
        GridWorld world,
        GridConfigurationKey configurationKey,
        GridBoundaryContactCursor cursor,
        GridBoundaryContact[] output)
    {
        if (!world.TryBeginBoundaryContacts(configurationKey, cursor))
            return cursor.Status;

        GridBoundaryContactCursorStatus status;
        do
        {
            status = world.AdvanceBoundaryContacts(
                cursor,
                output,
                candidateProbeLimit: 8,
                outputLimit: 1,
                out _,
                out _);
        }
        while (status == GridBoundaryContactCursorStatus.More);

        return status;
    }

    private static void AssertThreadIsWaiting(Thread thread)
    {
        Assert.True(SpinWait.SpinUntil(
            () => (thread.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0,
            TimeSpan.FromSeconds(5)));
        Assert.Equal(ThreadState.WaitSleepJoin, thread.ThreadState & ThreadState.WaitSleepJoin);
    }

    private static void AssertCanonicalOrder(ReadOnlySpan<VoxelContactManifold> contacts)
    {
        for (int i = 1; i < contacts.Length; i++)
        {
            VoxelContactManifold previous = contacts[i - 1];
            VoxelContactManifold current = contacts[i];
            int sourceComparison = previous.Source.VoxelIndex.CompareTo(current.Source.VoxelIndex);
            Assert.True(
                sourceComparison < 0
                || sourceComparison == 0
                && previous.Target.VoxelIndex.CompareTo(current.Target.VoxelIndex) < 0);
        }
    }
}
