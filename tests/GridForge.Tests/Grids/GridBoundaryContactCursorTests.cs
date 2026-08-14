using System;
using System.Reflection;
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

    [Fact]
    public void Advance_ShouldStaleOnBoundPairHighWaterMismatch()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
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

        world.ActiveGrids[targetIndex].ChangeHighWaterSequence++;

        Assert.Equal(
            GridBoundaryContactCursorStatus.Stale,
            world.AdvanceBoundaryContacts(cursor, output, 1, 1, out int staleProbes, out count));
        Assert.Equal(0, staleProbes);
        Assert.Equal(0, count);
        Assert.Equal(0UL, cursor.CandidateOrdinal);
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
