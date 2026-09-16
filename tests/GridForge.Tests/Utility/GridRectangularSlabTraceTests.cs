using System;
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
public sealed class GridRectangularSlabTraceTests : IDisposable
{
    private readonly GridWorld _world = GridWorldTestFactory.CreateWorld(spatialGridCellSize: 16);
    private readonly SwiftList<GridTraceInterval> _results = new SwiftList<GridTraceInterval>(128);
    private readonly GridTraceIntervalScratch _scratch = new GridTraceIntervalScratch(2, 128);
    private readonly int[] _tags = new int[128];
    private static readonly Func<GridTraceSlab, bool> Continue = static _ => true;

    public void Dispose() => _world.Dispose();

    [Fact]
    public void Stop_ShouldRetainOnlyCompletedSlabsWithOriginalParametersAndFullAdmission()
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        int callbacks = 0;
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, slab =>
        {
            Assert.Equal(callbacks++, slab.XIndex);
            Assert.Equal(_world.SpawnToken, slab.WorldSpawnToken);
            Assert.Equal(_world.ChangeSequence, slab.WorldChangeSequence);
            Assert.Equal(generation, slab.Generation);
            Assert.Equal(_results.Count, slab.IntervalStart + slab.IntervalCount);
            Assert.Equal(slab.XIndex == 1, slab.SeparatesEndpoints);
            return slab.XIndex != 1;
        }, out GridTraceIntervalReport report));

        Assert.Equal(2, callbacks);
        Assert.Equal(GridTraceIntervalStatus.StoppedAfterCompleteSlab, report.Status);
        Assert.False(report.IsComplete);
        Assert.False(report.HasContinuousAddressCoverage);
        Assert.False(report.HasContinuousPhysicalCoverage);
        Assert.Equal(0, report.TieGroupCount);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(25, report.AddressCandidateCount);
        Assert.Equal(5, report.IntervalCount);
        Assert.All(_results, interval =>
        {
            Assert.InRange(interval.Cell.VoxelIndex.x, 0, 1);
            Assert.Equal(-1, interval.TieGroupId);
            Assert.Equal(-1, interval.TieOrder);
        });
        Assert.Equal(Fixed64.FromFraction(3, 8), _results.Max(x => x.TExit));
        Assert.Equal(Enumerable.Range(0, _results.Count), _tags.Take(_results.Count));
        AssertScratchReleased();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Continue_ShouldMatchEagerIncludingCanonicalTiesAndSourceAssociations(bool sparse, bool reverse)
    {
        VoxelGrid grid = AddGrid(sparse);
        Vector3d start = new Vector3d(-Fixed64.Half, Fixed64.Zero, -Fixed64.Half);
        Vector3d end = new Vector3d(Fixed64.FromFraction(9, 2), Fixed64.Zero, Fixed64.FromFraction(9, 2));
        if (reverse) (start, end) = (end, start);
        SwiftList<GridTraceInterval> emitted = new SwiftList<GridTraceInterval>();
        Assert.True(Trace(start, end, Generation(grid), slab =>
        {
            for (int i = slab.IntervalStart; i < slab.IntervalStart + slab.IntervalCount; i++)
                emitted.Add(_results[i]);
            return true;
        }, out GridTraceIntervalReport report));
        SwiftList<GridTraceInterval> eager = new SwiftList<GridTraceInterval>();
        GridTraceIntervalReport expected = GridTracer.TraceIntervalsInto(
            _world, start, end, eager, _scratch, 2, 128, 128, 130);
        Assert.Equal(expected, report);
        Assert.Equal(eager.ToArray(), _results.ToArray());
        Assert.NotEmpty(_results);
        for (int i = 0; i < _results.Count; i++)
        {
            GridTraceInterval source = emitted[_tags[i]];
            Assert.Equal(_results[i], source.WithTie(_results[i].TieGroupId, _results[i].TieOrder));
        }
        AssertScratchReleased();
    }

    [Theory]
    [InlineData(0, 128, 128, 130)]
    [InlineData(1, 128, 128, 0)]
    [InlineData(0, 128, 128, 0)]
    [InlineData(1, 0, 128, 130)]
    [InlineData(1, 24, 128, 130)]
    [InlineData(1, 25, 128, 25)]
    [InlineData(1, 24, 128, 25)]
    [InlineData(1, 25, 0, 26)]
    [InlineData(1, 25, 12, 26)]
    [InlineData(1, 25, 13, 26)]
    [InlineData(2, 26, 14, 27)]
    public void QualifiedCeilings_ShouldBeTerminalAndMatchEagerAccounting(int grids, int addresses, int output, long work)
    {
        VoxelGrid grid = AddGrid();
        Array.Fill(_tags, -7);
        Assert.True(GridTracer.TryTraceRectangularSlabsInto(_world, Vector3d.Zero, new Vector3d(4, 0, 4),
            Generation(grid), _results, _scratch, _tags, Continue, grids, addresses, output, work, out var report));
        SwiftList<GridTraceInterval> eager = new SwiftList<GridTraceInterval>();
        GridTraceIntervalReport expected = GridTracer.TraceIntervalsInto(_world, Vector3d.Zero, new Vector3d(4, 0, 4),
            eager, _scratch, grids, addresses, output, work);
        Assert.Equal(expected, report);
        Assert.Equal(eager.ToArray(), _results.ToArray());
        if (!report.IsComplete)
            Assert.DoesNotContain(_tags, tag => tag > 0);
        AssertScratchReleased();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CallbackMisuse_ShouldRejectBeforeClearingLiveOutput(int misuse)
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, slab =>
        {
            GridTraceInterval[] before = _results.ToArray();
            Assert.NotEmpty(before);
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (misuse == 0) _scratch.Clear();
                else if (misuse == 1) Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, Continue, out _);
                else GridTracer.TraceIntervalsInto(_world, Vector3d.Zero, new Vector3d(4, 0, 4),
                    _results, _scratch, 2, 128, 128, 130);
            });
            Assert.Equal(before, _results.ToArray());
            return false;
        }, out var report));
        Assert.Equal(GridTraceIntervalStatus.StoppedAfterCompleteSlab, report.Status);
        AssertScratchReleased();
    }

    [Fact]
    public void CallbackException_ShouldClearOutputTagsAndReleaseScratchForReuse()
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        var exception = new InvalidOperationException("observer failed");
        Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
            Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, _ => throw exception, out _)));
        Assert.Empty(_results);
        Assert.All(_tags, tag => Assert.Equal(0, tag));
        AssertScratchReleased();
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, Continue, out var report));
        Assert.True(report.IsComplete);
    }

    [Theory]
    [InlineData(536870912L, false)]
    [InlineData(1073741823L, false)]
    [InlineData(1073741824L, false)]
    [InlineData(1073741825L, true)]
    public void Separator_ShouldRequireStrictlyMoreThanOneRawParameter(long gapRaw, bool separates)
    {
        VoxelGrid grid = AddGrid();
        Fixed64 distance = Fixed64.FromRaw(1L << 62);
        Fixed64 gap = Fixed64.FromRaw(gapRaw);
        foreach (bool endSide in new[] { false, true })
        foreach (bool reverse in new[] { false, true })
        {
            // Literal unit cell X=1 has faces .5 and 1.5; the raw parameter gap is gapRaw / 2^30.
            Fixed64 startX = endSide ? Fixed64.FromFraction(3, 2) + gap - distance : Fixed64.Half - gap;
            Fixed64 endX = endSide ? Fixed64.FromFraction(3, 2) + gap : startX + distance;
            Vector3d start = new Vector3d(startX, -Fixed64.Half, Fixed64.Zero);
            Vector3d end = new Vector3d(endX, -Fixed64.Half, Fixed64.Quarter);
            if (reverse) (start, end) = (end, start);
            bool observed = false;
            Assert.True(Trace(start, end, Generation(grid), slab =>
            {
                if (slab.XIndex != 1) return true;
                observed = true;
                Assert.Equal(separates, slab.SeparatesEndpoints);
                GridTraceInterval cut = _results.Single(value => value.Cell.VoxelIndex == new VoxelIndex(1, 0, 0));
                if (gapRaw == 536870912L)
                    Assert.Equal(!endSide ^ reverse ? 0 : Fixed64.One.m_rawValue,
                        !endSide ^ reverse ? cut.TEnter.m_rawValue : cut.TExit.m_rawValue);
                if (separates)
                {
                    Assert.True(cut.TEnter > Fixed64.Zero);
                    Assert.True(cut.TExit < Fixed64.One);
                }
                return false;
            }, out var report));
            Assert.True(observed);
            Assert.Equal(GridTraceIntervalStatus.StoppedAfterCompleteSlab, report.Status);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneRawOblique_ShouldObserveEverySlabAndKeepLiteralBoundaries(bool reverse)
    {
        VoxelGrid grid = AddGrid();
        Vector3d start = new Vector3d(Fixed64.Quarter, -Fixed64.Half, Fixed64.Zero);
        Vector3d end = new Vector3d(Fixed64.FromFraction(15, 4), -Fixed64.Half, Fixed64.MinIncrement);
        if (reverse) (start, end) = (end, start);
        int callbacks = 0;
        Assert.True(Trace(start, end, Generation(grid), slab => { callbacks++; return true; }, out var report));
        Assert.Equal(5, callbacks);
        Assert.Equal(5, report.IntervalCount);
        Assert.Equal(new long[] { 0, 306783378, 1533916891, 2761050405, 3988183918 },
            _results.Select(value => value.TEnter.m_rawValue));
        Assert.Equal(new long[] { 306783378, 1533916891, 2761050405, 3988183918, 4294967296 },
            _results.Select(value => value.TExit.m_rawValue));
        Assert.Equal(reverse ? new[] { 4, 3, 2, 1, 0 } : new[] { 0, 1, 2, 3, 4 },
            _results.Select(value => value.Cell.VoxelIndex.x));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void IneligibleSegmentOrGeneration_ShouldBypassWithoutObservationAndPreserveFallback(int scenario)
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        Vector3d start = Vector3d.Zero;
        Vector3d end = new Vector3d(4, 0, 4);
        switch (scenario)
        {
            case 0: end = start; break;
            case 1: end.Z = start.Z; break;
            case 2: end.X = start.X; break;
            case 3: start.Y = end.Y = new Fixed64(5); break;
            case 4: start.X = Fixed64.FromRaw(-(1L << 62)); end.X = Fixed64.FromRaw(1L << 62); break;
            case 5: start.X = Fixed64.FromRaw(1L << 62); end.X = Fixed64.FromRaw(-(1L << 62)); break;
            case 6: start.Z = Fixed64.MinValue; end.Z = Fixed64.MaxValue; break;
            case 7: generation = new GridCoveredAddressGeneration(generation.ConfigurationKey, 99, generation.GridSpawnToken, generation.GridLastChangeSequence); break;
            case 8: generation = new GridCoveredAddressGeneration(generation.ConfigurationKey, generation.GridIndex, generation.GridSpawnToken + 1, generation.GridLastChangeSequence); break;
            case 9: generation = new GridCoveredAddressGeneration(generation.ConfigurationKey, generation.GridIndex, generation.GridSpawnToken, generation.GridLastChangeSequence + 1); break;
            case 10: generation = new GridCoveredAddressGeneration(default, generation.GridIndex, generation.GridSpawnToken, generation.GridLastChangeSequence); break;
            case 11: Assert.True(_world.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20), new Vector3d(21, 0, 21)), out _)); break;
        }
        GridTraceIntervalReport expected = GridTracer.TraceIntervalsInto(_world, start, end, _results, _scratch, 2, 128, 128, 130);
        GridTraceInterval[] expectedIntervals = _results.ToArray();
        Assert.False(Trace(start, end, generation, _ => throw new Exception("Ineligible callback"), out _));
        Assert.Empty(_results);
        AssertScratchReleased();
        GridTraceIntervalReport actual = GridTracer.TraceIntervalsInto(_world, start, end, _results, _scratch, 2, 128, 128, 130);
        Assert.Equal(expected, actual);
        Assert.Equal(expectedIntervals, _results.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void UnsupportedTopologyOrPrism_ShouldBypass(int scenario)
    {
        GridConfiguration config = scenario switch
        {
            0 => new GridConfiguration(Vector3d.Zero, Vector3d.Zero, topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop)),
            1 => new GridConfiguration(Vector3d.Zero, Vector3d.Zero, topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.FlatTop)),
            2 => new GridConfiguration(Vector3d.Zero, new Vector3d(4, 1, 4)),
            _ => new GridConfiguration(Vector3d.Zero, Vector3d.Zero,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.MinIncrement, Fixed64.One, Fixed64.One))
        };
        Assert.True(_world.TryAddGrid(config, out ushort index));
        Assert.False(Trace(-Vector3d.One, Vector3d.One, Generation(_world.ActiveGrids[index]),
            _ => throw new Exception("Unsupported callback"), out _));
        Assert.Empty(_results);
        AssertScratchReleased();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptySlab_ShouldBeObservedIncludingOriginalStampAfterConcurrentObstacleMutation(bool stop)
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        ulong originalSequence = _world.ChangeSequence;
        long originalSpawn = _world.SpawnToken;
        ObstacleToken token = _world.AllocateObstacleToken();
        Exception workerFailure = null;
        int callbacks = 0;
        Assert.True(Trace(new Vector3d(0, 10, 0), new Vector3d(4, -10, 4), generation, slab =>
        {
            callbacks++;
            Assert.Equal(originalSequence, slab.WorldChangeSequence);
            Assert.Equal(originalSpawn, slab.WorldSpawnToken);
            Assert.Equal(generation, slab.Generation);
            if (slab.XIndex == 0)
            {
                Assert.Equal(0, slab.IntervalCount);
                // Independent mutation is permitted by the world read lock; the observer itself does not reenter.
                Thread worker = new Thread(() =>
                {
                    try { Assert.True(grid.TryAddObstacle(Vector3d.Zero, token)); }
                    catch (Exception exception) { workerFailure = exception; }
                });
                worker.Start();
                Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
                Assert.Null(workerFailure);
                Assert.True(_world.ChangeSequence > originalSequence);
            }
            if (slab.XIndex == 1)
            {
                Assert.Equal(0, slab.IntervalCount);
                Assert.True(slab.SeparatesEndpoints);
                return !stop;
            }
            return true;
        }, out var report));
        Assert.Equal(stop ? 2 : 5, callbacks);
        Assert.Equal(stop ? GridTraceIntervalStatus.StoppedAfterCompleteSlab : GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(stop ? 0 : 1, report.IntervalCount);
        AssertScratchReleased();
        Assert.False(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, Continue, out _));
    }

    [Fact]
    public void SparseMissingSlab_ShouldRetainMissingIntervalsNotSkipTheirObservation()
    {
        VoxelGrid grid = AddGrid(sparse: true);
        bool sawMissingSlab = false;
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), Generation(grid), slab =>
        {
            if (slab.XIndex != 1) return true;
            sawMissingSlab = true;
            Assert.Equal(3, slab.IntervalCount);
            Assert.All(_results.Skip(slab.IntervalStart), value => Assert.False(value.IsPhysicallyPresent));
            return false;
        }, out _));
        Assert.True(sawMissingSlab);
    }

    [Fact]
    public void InvalidArgumentsAndInactiveWorld_ShouldNotLeakScratchOwnership()
    {
        Assert.False(GridTracer.TryTraceRectangularSlabsInto(null, default, default, default,
            _results, _scratch, _tags, Continue, 2, 128, 128, 130, out _));
        Assert.False(Trace(Vector3d.Zero, Vector3d.One, default, Continue, out _));
        Assert.Throws<ArgumentNullException>(() => GridTracer.TryTraceRectangularSlabsInto(_world, default, default, default,
            null, _scratch, _tags, Continue, 2, 128, 128, 130, out _));
        Assert.Throws<ArgumentNullException>(() => GridTracer.TryTraceRectangularSlabsInto(_world, default, default, default,
            _results, null, _tags, Continue, 2, 128, 128, 130, out _));
        Assert.Throws<ArgumentNullException>(() => Trace(default, default, default, null, out _));
        Assert.Throws<ArgumentException>(() => GridTracer.TryTraceRectangularSlabsInto(_world, default, default, default,
            _results, _scratch, new int[127], Continue, 2, 128, 128, 130, out _));
        foreach (int argument in new[] { 0, 1, 2, 3 })
            Assert.Throws<ArgumentOutOfRangeException>(() => GridTracer.TryTraceRectangularSlabsInto(_world, default, default, default,
                _results, _scratch, _tags, Continue, argument == 0 ? -1 : 2, argument == 1 ? -1 : 128,
                argument == 2 ? -1 : 128, argument == 3 ? -1 : 130, out _));
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        _world.Dispose();
        _results.Add(default);
        Assert.False(Trace(Vector3d.Zero, Vector3d.One, generation, Continue, out _));
        Assert.Empty(_results);
        AssertScratchReleased();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmTrace_ShouldAllocateNothingAndReuseRetainedCapacity(bool sparse)
    {
        VoxelGrid grid = AddGrid(sparse);
        GridCoveredAddressGeneration generation = Generation(grid);
        int gridCapacity = _scratch.CandidateGrids.Capacity;
        int addressCapacity = _scratch.AddressCandidates.Capacity;
        int resultCapacity = _results.Capacity;
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, Continue, out _));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool qualified = Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation, Continue, out var report);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(qualified);
        Assert.True(report.IsComplete);
        Assert.Equal(0, allocated);
        Assert.Equal(gridCapacity, _scratch.CandidateGrids.Capacity);
        Assert.Equal(addressCapacity, _scratch.AddressCandidates.Capacity);
        Assert.Equal(resultCapacity, _results.Capacity);
        AssertScratchReleased();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void CornerTangencyAndOneRawPerturbation_ShouldMatchExactClosedPrism(int rawOffset)
    {
        Assert.True(_world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out ushort index));
        GridCoveredAddressGeneration generation = Generation(_world.ActiveGrids[index]);
        Fixed64 offset = Fixed64.FromRaw(rawOffset);
        Vector3d start = new Vector3d(new Fixed64(-2), Fixed64.Zero, -Fixed64.One + offset);
        Vector3d end = new Vector3d(new Fixed64(2), Fixed64.Zero, new Fixed64(3) + offset);
        foreach (bool reverse in new[] { false, true })
        {
            int observed = 0;
            Assert.True(Trace(reverse ? end : start, reverse ? start : end, generation,
                slab => { observed++; return true; }, out var report));
            Assert.Equal(1, observed);
            Assert.Equal(rawOffset > 0 ? 0 : 1, report.IntervalCount);
            if (rawOffset <= 0)
            {
                GridTraceInterval contact = Assert.Single(_results);
                // The one-raw interior crossing still rounds to the exact corner parameter.
                Fixed64 expected = Fixed64.FromFraction(reverse ? 5 : 3, 8);
                Assert.Equal(expected, contact.TEnter);
                Assert.Equal(expected, contact.TExit);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffCenterAnisotropicGrid_ShouldUseActualCentersAndObserveOmittedColumn(bool reverse)
    {
        GridConfiguration config = new GridConfiguration(new Vector3d(-6, -5, -9), new Vector3d(-4, -5, 3),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.Two, Fixed64.One, new Fixed64(3)));
        Assert.True(_world.TryAddGrid(config, out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];
        Vector3d start = new Vector3d(new Fixed64(-6), new Fixed64(-5), new Fixed64(-9));
        Vector3d end = new Vector3d(Fixed64.FromFraction(-21, 4), new Fixed64(-5), new Fixed64(3));
        if (reverse) (start, end) = (end, start);
        int calls = 0;
        Assert.True(Trace(start, end, Generation(grid), slab =>
        {
            Assert.Equal(calls++, slab.XIndex);
            Assert.False(slab.SeparatesEndpoints);
            Assert.Equal(slab.XIndex == 0 ? 5 : 0, slab.IntervalCount);
            return true;
        }, out var report));
        Assert.Equal(2, calls);
        Assert.Equal(10, report.AddressCandidateCount);
        Assert.Equal(5, report.IntervalCount);
        Assert.True(report.HasContinuousPhysicalCoverage);
        SwiftList<GridTraceInterval> eager = new SwiftList<GridTraceInterval>();
        Assert.Equal(GridTracer.TraceIntervalsInto(_world, start, end, eager, _scratch, 2, 128, 128, 130), report);
        Assert.Equal(eager.ToArray(), _results.ToArray());
    }

    [Fact]
    public void UnsupportedWorldReentry_ShouldPropagateAndReleaseBothScratchInstances()
    {
        VoxelGrid grid = AddGrid();
        GridTraceIntervalScratch nested = new GridTraceIntervalScratch();
        Assert.Throws<LockRecursionException>(() => Trace(Vector3d.Zero, new Vector3d(4, 0, 4), Generation(grid), _ =>
        {
            GridTracer.TraceIntervalsInto(_world, Vector3d.Zero, Vector3d.One,
                new SwiftList<GridTraceInterval>(), nested, 2, 128, 128, 130);
            return true;
        }, out _));
        Assert.Empty(_results);
        Assert.All(_tags, tag => Assert.Equal(0, tag));
        nested.Clear();
        AssertScratchReleased();
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), Generation(grid), Continue, out var report));
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void RecycledGridSlot_ShouldRejectPreviousGenerationWithoutRetainingGridReferences()
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration original = Generation(grid);
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), original, Continue, out _));
        AssertScratchReleased();
        Assert.True(_world.TryRemoveGrid(grid.GridIndex));
        VoxelGrid replacement = AddGrid();
        Assert.Equal(original.GridIndex, replacement.GridIndex);
        Assert.False(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), original, Continue, out _));
        Assert.Empty(_results);
        Assert.True(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), Generation(replacement), Continue, out var report));
        Assert.All(_results, value => Assert.Equal(replacement.SpawnToken, value.Cell.GridSpawnToken));
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void InactiveCandidateBucket_ShouldBypassBeforeReadingReleasedTopology()
    {
        VoxelGrid grid = AddGrid();
        GridCoveredAddressGeneration generation = Generation(grid);
        Assert.True(_world.TryRemoveGrid(grid.GridIndex));
        // Match GridWorld's existing stale-bucket boundary test: ActiveGrids is publicly mutable.
        int slot = _world.ActiveGrids.Add(grid);
        try
        {
            Assert.False(Trace(Vector3d.Zero, new Vector3d(4, 0, 4), generation,
                _ => throw new Exception("Inactive grid callback"), out _));
            Assert.Empty(_results);
            AssertScratchReleased();
        }
        finally
        {
            _world.ActiveGrids.RemoveAt(slot);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrepresentableExtremePrism_ShouldBypassAndPreserveEagerGeometryFailure(bool reverse)
    {
        // A normalized cell at MinValue has an unrepresentable negative X face.
        Vector3d origin = new Vector3d(Fixed64.MinValue, Fixed64.Zero, Fixed64.Zero);
        Vector3d far = new Vector3d(Fixed64.MinValue + Fixed64.Two, Fixed64.Zero, Fixed64.One);
        Assert.True(_world.TryAddGrid(new GridConfiguration(origin, far), out ushort index));
        VoxelGrid grid = _world.ActiveGrids[index];
        Vector3d start = reverse ? far : origin;
        Vector3d end = reverse ? origin : far;
        Assert.False(Trace(start, end, Generation(grid), _ => throw new Exception("Unrepresentable callback"), out _));
        Assert.Empty(_results);
        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(_world, start, end, _results, _scratch, 2, 128, 128, 130);
        Assert.Equal(GridTraceIntervalStatus.UnrepresentableGeometry, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(6, report.AddressCandidateCount);
        Assert.Empty(_results);
    }

    [Fact]
    public void SnappedRangeOutsideDiscoveryBounds_ShouldCompleteWithoutAdmissionOrObservation()
    {
        Assert.True(_world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out ushort index));
        Vector3d start = new Vector3d(Fixed64.FromFraction(5, 4), Fixed64.Zero, Fixed64.Zero);
        Vector3d end = new Vector3d(Fixed64.FromFraction(3, 2), Fixed64.Zero, Fixed64.Quarter);
        GridTraceIntervalReport expected = GridTracer.TraceIntervalsInto(_world, start, end, _results, _scratch, 0, 0, 0, 0);
        Assert.True(expected.IsComplete);
        Assert.Equal(0, expected.GridCandidateCount);
        Assert.Equal(0, expected.AddressCandidateCount);
        Assert.True(GridTracer.TryTraceRectangularSlabsInto(_world, start, end, Generation(_world.ActiveGrids[index]),
            _results, _scratch, _tags, _ => throw new Exception("Undiscovered slab"), 0, 0, 0, 0, out var actual));
        Assert.Equal(expected, actual);
        Assert.Empty(_results);
        AssertScratchReleased();
    }

    private VoxelGrid AddGrid(bool sparse = false)
    {
        GridConfiguration config = new GridConfiguration(Vector3d.Zero, new Vector3d(4, 0, 4),
            storageKind: sparse ? GridStorageKind.Sparse : GridStorageKind.Dense);
        Assert.True(_world.TryAddGrid(config,
            sparse ? new[] { new VoxelIndex(0, 0, 0), new VoxelIndex(4, 0, 4) } : null, out ushort index));
        return _world.ActiveGrids[index];
    }

    private static GridCoveredAddressGeneration Generation(VoxelGrid grid) =>
        new GridCoveredAddressGeneration(grid.Configuration.ToGridKey(), grid.GridIndex, grid.SpawnToken, grid.LastChangeSequence);

    private bool Trace(Vector3d start, Vector3d end, GridCoveredAddressGeneration generation,
        Func<GridTraceSlab, bool> observer, out GridTraceIntervalReport report) =>
        GridTracer.TryTraceRectangularSlabsInto(_world, start, end, generation, _results, _scratch, _tags,
            observer, 2, 128, 128, 130, out report);

    private void AssertScratchReleased()
    {
        Assert.Empty(_scratch.CandidateGrids);
        Assert.Empty(_scratch.AddressCandidates);
        Assert.All(_scratch.AddressCandidates.InnerArray, candidate => Assert.Null(candidate.Grid));
        _scratch.Clear();
    }
}
