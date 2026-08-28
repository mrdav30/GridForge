using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
public sealed class GridTraceIntervalTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
    private readonly SwiftList<GridTraceInterval> _results = new SwiftList<GridTraceInterval>(128);
    private readonly GridTraceIntervalScratch _scratch = new GridTraceIntervalScratch(8, 128);

    public void Dispose()
    {
        _world.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DenseRectangularTrace_ShouldReturnExactOrderedContinuousIntervals()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 0)),
            out ushort gridIndex));

        GridTraceIntervalReport report = Trace(
            new Vector3d(Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.FromFraction(5, 2), Fixed64.Zero, Fixed64.Zero));

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.True(report.HasContinuousAddressCoverage);
        Assert.True(report.HasContinuousPhysicalCoverage);
        Assert.Equal(3, _results.Count);
        Assert.Equal(new[] { 0, 1, 2 }, _results.Select(value => value.Cell.VoxelIndex.x));
        Assert.Equal(Fixed64.Zero, _results[0].TEnter);
        Assert.Equal(Fixed64.One, _results[2].TExit);
        Assert.All(_results, value =>
        {
            Assert.Equal(_world.SpawnToken, value.Cell.WorldSpawnToken);
            Assert.Equal(gridIndex, value.Cell.GridIndex);
            Assert.Equal(_world.ActiveGrids[gridIndex].SpawnToken, value.Cell.GridSpawnToken);
            Assert.Equal(
                _world.ActiveGrids[gridIndex].LastChangeSequence,
                value.GridLastChangeSequence);
            Assert.True(value.IsPhysicallyPresent);
        });
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexTrace_ShouldUseTruePrismsInBothOrientations(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(2), orientation);
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(metrics, new VoxelIndex(2, 0, 0)),
            out _));
        Vector3d end = HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(2, 0, 0), metrics);

        GridTraceIntervalReport report = Trace(Vector3d.Zero, end);

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.True(report.HasContinuousPhysicalCoverage);
        Assert.Equal(
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(1, 0, 0), new VoxelIndex(2, 0, 0) },
            _results.Select(value => value.Cell.VoxelIndex));

        GridWorld outsideWorld = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
        try
        {
            Assert.True(outsideWorld.TryAddGrid(
                CreateHexConfiguration(metrics, default),
                out _));
            Vector3d aabbOnly = orientation == HexOrientation.PointyTop
                ? new Vector3d(HexCoordinateUtility.Sqrt3 - Fixed64.FromFraction(1, 100), Fixed64.Zero,
                    new Fixed64(2) - Fixed64.FromFraction(1, 100))
                : new Vector3d(new Fixed64(2) - Fixed64.FromFraction(1, 100), Fixed64.Zero,
                    HexCoordinateUtility.Sqrt3 - Fixed64.FromFraction(1, 100));
            _results.Clear();
            GridTraceIntervalReport miss = GridTracer.TraceIntervalsInto(
                outsideWorld,
                aabbOnly,
                aabbOnly,
                _results,
                _scratch,
                gridCandidateLimit: 4,
                addressCandidateLimit: 64,
                outputLimit: 64,
                candidateWorkLimit: 68L);

            Assert.Equal(GridTraceIntervalStatus.Complete, miss.Status);
            Assert.Empty(_results);
        }
        finally
        {
            outsideWorld.Dispose();
        }
    }

    [Fact]
    public void SparseTrace_ShouldEmitMissingAddressAndDistinguishPhysicalCoverage()
    {
        GridConfiguration sparse = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            storageKind: GridStorageKind.Sparse);
        Assert.True(_world.TryAddGrid(
            sparse,
            new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(2, 0, 0) },
            out _));

        GridTraceIntervalReport report = Trace(
            new Vector3d(Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.FromFraction(5, 2), Fixed64.Zero, Fixed64.Zero));

        Assert.Equal(3, _results.Count);
        Assert.True(report.HasContinuousAddressCoverage);
        Assert.False(report.HasContinuousPhysicalCoverage);
        Assert.Equal(new[] { true, false, true }, _results.Select(value => value.IsPhysicallyPresent));
        Assert.Equal(new VoxelIndex(1, 0, 0), _results[1].Cell.VoxelIndex);
    }

    [Fact]
    public void OverlappingGridsAndBoundaryPeers_ShouldReceiveStableTieGroupsWithoutAdjacencyClaims()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 1)),
            out ushort firstGrid));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 1)),
            out ushort secondGrid));
        Vector3d start = new Vector3d(
            Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.FromFraction(1, 2));
        Vector3d end = new Vector3d(
            Fixed64.FromFraction(3, 2), Fixed64.Zero, Fixed64.FromFraction(1, 2));

        GridTraceIntervalReport report = Trace(start, end);
        (ushort grid, VoxelIndex index, Fixed64 enter, Fixed64 exit, int group, int order)[] first =
            _results.Select(value => (
                value.Cell.GridIndex,
                value.Cell.VoxelIndex,
                value.TEnter,
                value.TExit,
                value.TieGroupId,
                value.TieOrder)).ToArray();

        Assert.True(report.HasContinuousPhysicalCoverage);
        Assert.Contains(_results, value => value.Cell.GridIndex == firstGrid);
        Assert.Contains(_results, value => value.Cell.GridIndex == secondGrid);
        Assert.Contains(_results.GroupBy(value => value.TieGroupId), group => group.Count() >= 4);

        Trace(start, end);
        Assert.Equal(first, _results.Select(value => (
            value.Cell.GridIndex,
            value.Cell.VoxelIndex,
            value.TEnter,
            value.TExit,
            value.TieGroupId,
            value.TieOrder)));
    }

    [Fact]
    public void CornerBoundaryTrace_ShouldGroupPointPeersSeparatelyFromContinuousCells()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 1)),
            out _));

        GridTraceIntervalReport report = Trace(
            new Vector3d(Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.FromFraction(-1, 2)),
            new Vector3d(Fixed64.FromFraction(3, 2), Fixed64.Zero, Fixed64.FromFraction(3, 2)));

        GridTraceInterval[] pointPeers = _results
            .Where(value => value.TEnter == value.TExit)
            .ToArray();
        Assert.Equal(2, pointPeers.Length);
        Assert.Equal(pointPeers[0].TieGroupId, pointPeers[1].TieGroupId);
        Assert.Equal(new[] { 0, 1 }, pointPeers.Select(value => value.TieOrder));
        Assert.True(report.HasContinuousPhysicalCoverage);
    }

    [Fact]
    public void FootprintEdgeTrace_ShouldRetainTheExactVerticalEdgeInterval()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out _));
        Fixed64 half = Fixed64.Half;
        Fixed64 quarter = Fixed64.One / new Fixed64(4);

        GridTraceIntervalReport report = Trace(
            new Vector3d(half, Fixed64.Zero, -quarter),
            new Vector3d(half, Fixed64.Zero, quarter));

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.True(report.HasContinuousAddressCoverage);
        Assert.True(report.HasContinuousPhysicalCoverage);
        GridTraceInterval interval = Assert.Single(_results);
        Assert.Equal(default, interval.Cell.VoxelIndex);
        Assert.Equal(Fixed64.Zero, interval.TEnter);
        Assert.Equal(Fixed64.One, interval.TExit);
    }

    [Fact]
    public void PartialInteriorOverlap_ShouldExtendOneStableTieGroup()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(2))),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0)),
            out _));

        GridTraceIntervalReport report = Trace(
            new Vector3d(-1, 0, 0),
            new Vector3d(2, 0, 0));

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(2, _results.Count);
        Assert.Equal(_results[0].TieGroupId, _results[1].TieGroupId);
        Assert.Equal(new[] { 0, 1 }, _results.Select(value => value.TieOrder));
        Assert.True(_results[1].TExit > _results[0].TExit);
    }

    [Fact]
    public void CanonicalOrder_ShouldIgnoreRegistrationOrderAndGroupPartialOverlapPeers()
    {
        GridConfiguration narrow = new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0));
        GridConfiguration wide = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(2)));

        (GridConfigurationKey key, VoxelIndex index, Fixed64 enter, Fixed64 exit, int group, int order)[] forward =
            TraceOrder(narrow, wide);
        (GridConfigurationKey key, VoxelIndex index, Fixed64 enter, Fixed64 exit, int group, int order)[] reverse =
            TraceOrder(wide, narrow);

        Assert.Equal(forward, reverse);
        Assert.Contains(
            forward.GroupBy(value => value.group),
            group => group.Select(value => value.key).Distinct().Count() == 2
                && group.Select(value => (value.enter, value.exit)).Distinct().Count() > 1);

        (GridConfigurationKey key, VoxelIndex index, Fixed64 enter, Fixed64 exit, int group, int order)[] TraceOrder(
            GridConfiguration first,
            GridConfiguration second)
        {
            using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
            Assert.True(world.TryAddGrid(first, out _));
            Assert.True(world.TryAddGrid(second, out _));
            SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(16);
            GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(4, 32);
            GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
                world,
                new Vector3d(-1, 0, 0),
                new Vector3d(1, 0, 0),
                results,
                scratch,
                gridCandidateLimit: 4,
                addressCandidateLimit: 64,
                outputLimit: 32,
                candidateWorkLimit: 68L);
            Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
            return results.Select(value => (
                value.ConfigurationKey,
                value.Cell.VoxelIndex,
                value.TEnter,
                value.TExit,
                value.TieGroupId,
                value.TieOrder)).ToArray();
        }
    }

    [Fact]
    public void CanonicalOrder_ShouldUseEveryConfigurationComponentBeforeRegistrationOrder()
    {
        var orderedPairs = new (GridConfiguration Earlier, GridConfiguration Later)[]
        {
            (
                new GridConfiguration(new Vector3d(-1, 0, 0), Vector3d.Zero),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero)),
            (
                new GridConfiguration(new Vector3d(0, -1, 0), Vector3d.Zero),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero)),
            (
                new GridConfiguration(new Vector3d(0, 0, -1), Vector3d.Zero),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero)),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
                new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0))),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
                new GridConfiguration(Vector3d.Zero, new Vector3d(0, 1, 0))),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
                new GridConfiguration(Vector3d.Zero, new Vector3d(0, 0, 1))),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyKind: GridTopologyKind.HexPrism,
                    topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One)),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyKind: GridTopologyKind.HexPrism,
                    topologyMetrics: GridTopologyMetrics.Hex(new Fixed64(2), Fixed64.One))),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.One)),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(2), Fixed64.One, Fixed64.One))),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.One)),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, new Fixed64(2), Fixed64.One))),
            (
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.One)),
                new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                    topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, new Fixed64(2))))
        };

        foreach ((GridConfiguration earlier, GridConfiguration later) in orderedPairs)
        {
            GridConfigurationKey[] forward = TraceOrder(earlier, later);
            GridConfigurationKey[] reverse = TraceOrder(later, earlier);

            Assert.Equal(forward, reverse);
            Assert.Equal(2, forward.Length);
            Assert.Equal(earlier.ToGridKey(), forward[0]);
            Assert.Equal(later.ToGridKey(), forward[1]);
        }

        static GridConfigurationKey[] TraceOrder(
            GridConfiguration first,
            GridConfiguration second)
        {
            using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
            Assert.True(world.TryAddGrid(first, out _));
            Assert.True(world.TryAddGrid(second, out _));
            var results = new SwiftList<GridTraceInterval>(4);
            var scratch = new GridTraceIntervalScratch(2, 8);
            GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
                world,
                Vector3d.Zero,
                Vector3d.Zero,
                results,
                scratch,
                gridCandidateLimit: 2,
                addressCandidateLimit: 8,
                outputLimit: 4,
                candidateWorkLimit: 10L);
            Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
            return results.Select(value => value.ConfigurationKey).ToArray();
        }
    }

    [Fact]
    public void VerticalTrace_ShouldReturnExactClosedIntervals()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(0, 2, 0)),
            out _));

        GridTraceIntervalReport report = Trace(
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero),
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(5, 2), Fixed64.Zero));

        Assert.Equal(3, _results.Count);
        Assert.Equal(new[] { 0, 1, 2 }, _results.Select(value => value.Cell.VoxelIndex.y));
        Assert.True(report.HasContinuousPhysicalCoverage);
        Assert.Equal(Fixed64.Zero, _results[0].TEnter);
        Assert.Equal(Fixed64.One, _results[2].TExit);
    }

    [Fact]
    public void IncompleteAndVerticallyDisjointSegments_ShouldReportNoContinuousCoverage()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out _));

        GridTraceIntervalReport incomplete = Trace(Vector3d.Zero, new Vector3d(3, 0, 0));
        Assert.Single(_results);
        Assert.False(incomplete.HasContinuousAddressCoverage);
        Assert.False(incomplete.HasContinuousPhysicalCoverage);

        GridTraceIntervalReport above = Trace(new Vector3d(0, 2, 0), new Vector3d(0, 3, 0));
        Assert.Empty(_results);
        Assert.False(above.HasContinuousAddressCoverage);

        GridTraceIntervalReport below = Trace(new Vector3d(0, -2, 0), new Vector3d(0, -3, 0));
        Assert.Empty(_results);
        Assert.False(below.HasContinuousAddressCoverage);

        GridTraceIntervalReport stationaryBelow = Trace(
            new Vector3d(0, -2, 0),
            new Vector3d(0, -2, 0));
        Assert.Empty(_results);
        Assert.False(stationaryBelow.HasContinuousAddressCoverage);
    }

    [Fact]
    public void ExactPrismInterval_ShouldRejectAPlanarCrossingAboveTheVerticalSpan()
    {
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(Fixed64.One),
            Vector3d.Zero,
            default,
            out GridCellPrism prism));

        Assert.False(GridTracer.TryGetPrismInterval(
            new Vector3d(-Fixed64.One, new Fixed64(2), Fixed64.Zero),
            new Vector3d(Fixed64.One, new Fixed64(3), Fixed64.Zero),
            prism,
            out _,
            out _));
    }

    [Fact]
    public void BroadPhaseOnlyCandidate_ShouldCompleteWithoutConsumingAnAddressBudget()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                Vector3d.Zero,
                Vector3d.Zero,
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One)),
            out _));
        Fixed64 outsideCellButInsideBroadPhase = Fixed64.FromFraction(3, 2);

        GridTraceIntervalReport report = Trace(
            new Vector3d(outsideCellButInsideBroadPhase, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(outsideCellButInsideBroadPhase, Fixed64.Zero, Fixed64.Zero));

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
        Assert.Empty(_results);
    }

    [Fact]
    public void UnrepresentableCandidatePrism_ShouldFailClosedWithoutPartialResults()
    {
        Vector3d position = Vector3d.Zero;
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(
                position,
                position,
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    Fixed64.MinIncrement,
                    Fixed64.One,
                    Fixed64.One)),
            out _));

        GridTraceIntervalReport report = Trace(position, position);

        Assert.Equal(GridTraceIntervalStatus.UnrepresentableGeometry, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(1, report.AddressCandidateCount);
        Assert.Empty(_results);
    }

    [Fact]
    public void TraceCeilings_ShouldFailClosedAndWarmedTraceShouldAllocateZero()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(4, 0, 0)),
            out _));
        Vector3d start = new Vector3d(Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.Zero);
        Vector3d end = new Vector3d(Fixed64.FromFraction(9, 2), Fixed64.Zero, Fixed64.Zero);

        GridTraceIntervalReport candidateLimited = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 1,
            outputLimit: 32,
            candidateWorkLimit: 9L);
        Assert.Equal(GridTraceIntervalStatus.AddressCandidateLimitExceeded, candidateLimited.Status);
        Assert.Equal(1, candidateLimited.GridCandidateCount);
        Assert.Equal(1, candidateLimited.AddressCandidateCount);
        Assert.Empty(_results);

        GridTraceIntervalReport outputLimited = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 64,
            outputLimit: 1,
            candidateWorkLimit: 72L);
        Assert.Equal(GridTraceIntervalStatus.OutputLimitExceeded, outputLimited.Status);
        Assert.Equal(1, outputLimited.GridCandidateCount);
        Assert.Equal(5, outputLimited.AddressCandidateCount);
        Assert.Empty(_results);

        Trace(start, end);
        long before = GC.GetAllocatedBytesForCurrentThread();
        GridTraceIntervalReport warmed = Trace(start, end);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(GridTraceIntervalStatus.Complete, warmed.Status);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TraceIntervals_ShouldHandleEmptyWorldsInvalidLimitsAndVerticalDirectionExactly()
    {
        GridTraceIntervalReport noWorld = GridTracer.TraceIntervalsInto(
            null,
            Vector3d.Zero,
            Vector3d.One,
            _results,
            _scratch,
            0,
            0,
            0,
            0L);
        Assert.Equal(GridTraceIntervalStatus.Complete, noWorld.Status);
        Assert.Empty(_results);

        Assert.Equal("gridCandidateLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceIntervalsInto(
                _world, Vector3d.Zero, Vector3d.Zero, _results, _scratch,
                -1, 0, 0, 0L)).ParamName);
        Assert.Equal("addressCandidateLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceIntervalsInto(
                _world, Vector3d.Zero, Vector3d.Zero, _results, _scratch,
                0, -1, 0, 0L)).ParamName);
        Assert.Equal("outputLimit", Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridTracer.TraceIntervalsInto(
                _world, Vector3d.Zero, Vector3d.Zero, _results, _scratch,
                0, 0, -1, 0L)).ParamName);

        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(0, 2, 0)),
            out _));
        GridTraceIntervalReport descending = Trace(
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(5, 2), Fixed64.Zero),
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero));
        Assert.Equal(GridTraceIntervalStatus.Complete, descending.Status);
        Assert.Equal(new[] { 2, 1, 0 }, _results.Select(value => value.Cell.VoxelIndex.y));
        Assert.True(descending.HasContinuousPhysicalCoverage);

        GridTraceIntervalReport stationaryOutside = Trace(
            new Vector3d(0, 10, 0),
            new Vector3d(0, 10, 0));
        Assert.Equal(GridTraceIntervalStatus.Complete, stationaryOutside.Status);
        Assert.Empty(_results);

        GridWorld disposedWorld = GridWorldTestFactory.CreateWorld();
        disposedWorld.Dispose();
        GridTraceIntervalReport disposed = GridTracer.TraceIntervalsInto(
            disposedWorld,
            Vector3d.Zero,
            Vector3d.Zero,
            _results,
            _scratch,
            0,
            0,
            0,
            0L);
        Assert.Equal(GridTraceIntervalStatus.Complete, disposed.Status);
        Assert.Empty(_results);
    }

    [Fact]
    public void CombinedCandidateWorkCeiling_ShouldStopBeforeTheNextGridOrAddress()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(4, 0, 0)),
            out _));
        Vector3d start = new(Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.Zero);
        Vector3d end = new(Fixed64.FromFraction(9, 2), Fixed64.Zero, Fixed64.Zero);

        GridTraceIntervalReport zero = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 64,
            outputLimit: 32,
            candidateWorkLimit: 0L);
        Assert.Equal(GridTraceIntervalStatus.CandidateWorkLimitExceeded, zero.Status);
        Assert.Equal(0, zero.GridCandidateCount);
        Assert.Equal(0, zero.AddressCandidateCount);
        Assert.Empty(_results);

        GridTraceIntervalReport coincidentGridLimit = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 0,
            addressCandidateLimit: 64,
            outputLimit: 32,
            candidateWorkLimit: 0L);
        Assert.Equal(
            GridTraceIntervalStatus.GridCandidateLimitExceeded,
            coincidentGridLimit.Status);

        GridTraceIntervalReport oneBelow = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 64,
            outputLimit: 32,
            candidateWorkLimit: 5L);
        Assert.Equal(GridTraceIntervalStatus.CandidateWorkLimitExceeded, oneBelow.Status);
        Assert.Equal(1, oneBelow.GridCandidateCount);
        Assert.Equal(4, oneBelow.AddressCandidateCount);
        Assert.Empty(_results);

        GridTraceIntervalReport coincidentAddressLimit = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 4,
            outputLimit: 32,
            candidateWorkLimit: 5L);
        Assert.Equal(
            GridTraceIntervalStatus.AddressCandidateLimitExceeded,
            coincidentAddressLimit.Status);
        Assert.Equal(1, coincidentAddressLimit.GridCandidateCount);
        Assert.Equal(4, coincidentAddressLimit.AddressCandidateCount);

        GridTraceIntervalReport exact = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 64,
            outputLimit: 32,
            candidateWorkLimit: 6L);
        Assert.Equal(GridTraceIntervalStatus.Complete, exact.Status);
        Assert.Equal(1, exact.GridCandidateCount);
        Assert.Equal(5, exact.AddressCandidateCount);
        Assert.Equal(5, exact.IntervalCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 64,
            outputLimit: 32,
            candidateWorkLimit: -1L));
    }

    [Fact]
    public void GridCandidateCeiling_ShouldFailBeforeAddressWorkAndAcceptExactTopologyCount()
    {
        GridTopologyMetrics pointyMetrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(2),
            HexOrientation.PointyTop);
        GridTopologyMetrics flatMetrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(2),
            HexOrientation.FlatTop);
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, Vector3d.Zero),
            out _));
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(pointyMetrics, default),
            out _));
        Assert.True(_world.TryAddGrid(
            CreateHexConfiguration(flatMetrics, default),
            out _));
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(new Vector3d(100, 0, 0), new Vector3d(100, 0, 0)),
            out _));

        Vector3d start = new Vector3d(Fixed64.FromFraction(-1, 4), Fixed64.Zero, Fixed64.Zero);
        Vector3d end = new Vector3d(Fixed64.FromFraction(1, 4), Fixed64.Zero, Fixed64.Zero);
        GridTraceIntervalReport oneBelow = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 2,
            addressCandidateLimit: 0,
            outputLimit: 0,
            candidateWorkLimit: 2L);

        Assert.Equal(GridTraceIntervalStatus.GridCandidateLimitExceeded, oneBelow.Status);
        Assert.Equal(2, oneBelow.GridCandidateCount);
        Assert.Equal(0, oneBelow.AddressCandidateCount);
        Assert.Equal(0, oneBelow.IntervalCount);
        Assert.Empty(_results);

        long failureBefore = GC.GetAllocatedBytesForCurrentThread();
        oneBelow = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 2,
            addressCandidateLimit: 0,
            outputLimit: 0,
            candidateWorkLimit: 2L);
        long failureAllocated = GC.GetAllocatedBytesForCurrentThread() - failureBefore;

        Assert.Equal(GridTraceIntervalStatus.GridCandidateLimitExceeded, oneBelow.Status);
        Assert.Equal(2, oneBelow.GridCandidateCount);
        Assert.Equal(0, oneBelow.AddressCandidateCount);
        Assert.Empty(_results);
        Assert.Equal(0, failureAllocated);

        GridTraceIntervalReport exact = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 3,
            addressCandidateLimit: 16,
            outputLimit: 8,
            candidateWorkLimit: 19L);
        GridConfigurationKey[] firstOrder = _results
            .Select(value => value.ConfigurationKey)
            .ToArray();

        Assert.Equal(GridTraceIntervalStatus.Complete, exact.Status);
        Assert.Equal(3, exact.GridCandidateCount);
        Assert.Equal(3, exact.AddressCandidateCount);
        Assert.Equal(3, exact.IntervalCount);
        Assert.Equal(3, firstOrder.Distinct().Count());
        Assert.Equal(
            new[]
            {
                GridTopologyKind.RectangularPrism,
                GridTopologyKind.HexPrism,
                GridTopologyKind.HexPrism
            },
            firstOrder.Select(value => value.TopologyKind));
        Assert.Equal(HexOrientation.FlatTop, firstOrder[1].TopologyMetrics.HexOrientation);
        Assert.Equal(HexOrientation.PointyTop, firstOrder[2].TopologyMetrics.HexOrientation);

        GridTraceIntervalReport warmed = default;
        for (int i = 0; i < 16; i++)
        {
            warmed = GridTracer.TraceIntervalsInto(
                _world,
                start,
                end,
                _results,
                _scratch,
                gridCandidateLimit: 3,
                addressCandidateLimit: 16,
                outputLimit: 8,
                candidateWorkLimit: 19L);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        warmed = GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 3,
            addressCandidateLimit: 16,
            outputLimit: 8,
            candidateWorkLimit: 19L);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(GridTraceIntervalStatus.Complete, warmed.Status);
        Assert.Equal(firstOrder, _results.Select(value => value.ConfigurationKey));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void CandidateWalk_ShouldMatchExhaustiveExactNarrowPhaseForSmallTopologyMatrix()
    {
        GridTopologyMetrics pointyMetrics = GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(2));
        GridTopologyMetrics flatMetrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(2),
            HexOrientation.FlatTop);
        GridTopologyMetrics anisotropicRectangular = GridTopologyMetrics.Rectangular(
            new Fixed64(8),
            new Fixed64(2),
            new Fixed64(2));
        (GridConfiguration configuration, Vector3d start, Vector3d end)[] cases =
        {
            (
                new GridConfiguration(Vector3d.Zero, new Vector3d(3, 2, 3)),
                new Vector3d(new Fixed64(-1), Fixed64.FromFraction(1, 2), Fixed64.FromFraction(1, 2)),
                new Vector3d(new Fixed64(4), Fixed64.FromFraction(3, 2), Fixed64.FromFraction(5, 2))),
            (
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d(24, 2, 6),
                    topologyMetrics: anisotropicRectangular),
                new Vector3d(-4, 0, -1),
                new Vector3d(28, 2, 7)),
            (
                CreateHexConfiguration(pointyMetrics, new VoxelIndex(3, 1, 3)),
                Vector3d.Zero,
                HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(3, 1, 3), pointyMetrics)),
            (
                CreateHexConfiguration(flatMetrics, new VoxelIndex(3, 1, 3)),
                HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(0, 0, 3), flatMetrics),
                HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(3, 1, 0), flatMetrics))
        };

        foreach ((GridConfiguration configuration, Vector3d start, Vector3d end) in cases)
        {
            using GridWorld world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
            Assert.True(world.TryAddGrid(configuration, out ushort gridIndex));
            VoxelGrid grid = world.ActiveGrids[gridIndex];
            SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(128);
            GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(2, 128);

            GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
                world,
                start,
                end,
                results,
                scratch,
                gridCandidateLimit: 2,
                addressCandidateLimit: 4096,
                outputLimit: 1024,
                candidateWorkLimit: 4098L);
            Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);

            HashSet<VoxelIndex> expected = new HashSet<VoxelIndex>();
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    for (int z = 0; z < grid.Length; z++)
                    {
                        VoxelIndex index = new VoxelIndex(x, y, z);
                        Assert.True(GridCellGeometry.TryGetPrism(grid, index, out GridCellPrism prism));
                        if (GridTracer.TryGetPrismInterval(start, end, prism, out _, out _))
                            expected.Add(index);
                    }
                }
            }

            Assert.Equal(
                expected.OrderBy(index => index),
                results.Select(value => value.Cell.VoxelIndex).OrderBy(index => index));
        }
    }

    [Fact]
    public void DenseTrace_ShouldNotWaitForTheWorldChangeGate()
    {
        Assert.True(_world.TryAddGrid(
            new GridConfiguration(Vector3d.Zero, new Vector3d(4, 0, 0)),
            out _));
        using ManualResetEventSlim traceFinished = new ManualResetEventSlim();
        Exception traceError = null;
        GridTraceIntervalReport report = default;
        Thread traceThread = new Thread(() =>
        {
            try
            {
                SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(16);
                GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(2, 32);
                report = GridTracer.TraceIntervalsInto(
                    _world,
                    new Vector3d(Fixed64.FromFraction(-1, 2), Fixed64.Zero, Fixed64.Zero),
                    new Vector3d(Fixed64.FromFraction(9, 2), Fixed64.Zero, Fixed64.Zero),
                    results,
                    scratch,
                    gridCandidateLimit: 2,
                    addressCandidateLimit: 64,
                    outputLimit: 16,
                    candidateWorkLimit: 66L);
            }
            catch (Exception exception)
            {
                traceError = exception;
            }
            finally
            {
                traceFinished.Set();
            }
        });

        lock (_world.ChangeSyncRoot)
        {
            traceThread.Start();
            Assert.True(
                traceFinished.Wait(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken),
                "A dense trace serialized behind the world change gate.");
        }

        Assert.Null(traceError);
        Assert.True(report.IsComplete);
        Assert.Equal(5, report.IntervalCount);
    }

    private GridTraceIntervalReport Trace(Vector3d start, Vector3d end) =>
        GridTracer.TraceIntervalsInto(
            _world,
            start,
            end,
            _results,
            _scratch,
            gridCandidateLimit: 8,
            addressCandidateLimit: 4096,
            outputLimit: 1024,
            candidateWorkLimit: 4104L);

    private static GridConfiguration CreateHexConfiguration(
        GridTopologyMetrics metrics,
        VoxelIndex maxIndex) =>
        new GridConfiguration(
            Vector3d.Zero,
            HexCoordinateUtility.AxialToWorldOffset(maxIndex, metrics),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: metrics);
}
