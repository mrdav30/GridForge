using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridPlanarTraceIntervalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultPrism_ShouldRetainFootprintValidationInsteadOfBecomingAGeometricMiss(bool stationary)
    {
        Vector2d start = new Vector2d(2, 3);
        Vector2d end = stationary ? start : new Vector2d(4, 5);

        Assert.Throws<ArgumentException>(() => GridCellGeometry.TryGetPlanarSegmentInterval(
            default, start, end, out _, out _));
    }

    [Theory]
    [InlineData(-2, 0, 2, 4, false, 0, 0)]
    [InlineData(-2, -4, 2, 0, false, 0, 0)]
    // The supporting line crosses the footprint, but the finite segment stops outside it.
    [InlineData(2, 0, 3, 0, false, 0, 0)]
    [InlineData(-2, -1, 2, 3, true, 3, 3)]
    [InlineData(-2, 1, 2, -3, true, 3, 3)]
    [InlineData(-1, -1, 1, 1, true, 2, 6)]
    public void RectangularInterval_ShouldPreserveStrictMissesCornerTouchesAndCrossingsInBothDirections(
        int startX, int startZ, int endX, int endZ, bool intersects, int enterEighths, int exitEighths)
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Vector2d start = new Vector2d(startX, startZ);
        Vector2d end = new Vector2d(endX, endZ);
        Fixed64 expectedEnter = Fixed64.FromFraction(enterEighths, 8);
        Fixed64 expectedExit = Fixed64.FromFraction(exitEighths, 8);

        AssertInterval(prism, start, end, intersects, expectedEnter, expectedExit);
        AssertInterval(prism, end, start, intersects,
            intersects ? Fixed64.One - expectedExit : Fixed64.Zero,
            intersects ? Fixed64.One - expectedEnter : Fixed64.Zero);
    }

    [Fact]
    public void RectangularInterval_ShouldDistinguishOneRawUnitBeyondACornerFromExactContact()
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Fixed64 epsilon = Fixed64.MinIncrement;
        Vector2d start = new Vector2d(new Fixed64(-2), -Fixed64.One + epsilon);
        Vector2d end = new Vector2d(new Fixed64(2), new Fixed64(3) + epsilon);

        AssertInterval(prism, start, end, false, Fixed64.Zero, Fixed64.Zero);
        AssertInterval(prism, end, start, false, Fixed64.Zero, Fixed64.Zero);
        AssertInterval(prism, new Vector2d(-2, -1), new Vector2d(2, 3), true,
            Fixed64.FromFraction(3, 8), Fixed64.FromFraction(3, 8));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void StationaryPlanarInterval_ShouldRetainClosedPointContainment(int twiceX, bool contained)
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Vector2d point = new Vector2d(Fixed64.FromFraction(twiceX, 2), Fixed64.Zero);

        // Point segments retain the complete parameter domain, including on the false return path.
        AssertInterval(prism, point, point, contained, Fixed64.Zero, Fixed64.One);
    }

    [Fact]
    public void RectangularInterval_ShouldRetainCollinearEdgeOverlapAndInteriorEndpoints()
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Vector2d start = new Vector2d(-Fixed64.One, Fixed64.Half);
        Vector2d end = new Vector2d(Fixed64.One, Fixed64.Half);

        AssertInterval(prism, start, end, true, Fixed64.Quarter, Fixed64.One - Fixed64.Quarter);
        AssertInterval(prism, end, start, true, Fixed64.Quarter, Fixed64.One - Fixed64.Quarter);
        AssertInterval(prism, Vector2d.Zero, new Vector2d(1, 0), true, Fixed64.Zero, Fixed64.Half);
        AssertInterval(prism, new Vector2d(1, 0), Vector2d.Zero, true, Fixed64.Half, Fixed64.One);
    }

    [Fact]
    public void FullDomainSegment_ShouldPreserveExactMissAndCrossingWithoutNarrowingDifferences()
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Vector2d start = new Vector2d(Fixed64.MinValue, Fixed64.MinValue + new Fixed64(2));
        Vector2d end = new Vector2d(Fixed64.MaxValue - new Fixed64(2), Fixed64.MaxValue);
        AssertInterval(prism, start, end, false, Fixed64.Zero, Fixed64.Zero);
        AssertInterval(prism, end, start, false, Fixed64.Zero, Fixed64.Zero);

        start = new Vector2d(Fixed64.MinValue, Fixed64.MinValue);
        end = new Vector2d(Fixed64.MaxValue, Fixed64.MaxValue);
        // The asymmetric full-domain endpoints place the exact boundaries just above
        // the half-raw rounding ties; reversal preserves the complementary interval.
        AssertInterval(prism, start, end, true, Fixed64.Half, Fixed64.Half + Fixed64.MinIncrement);
        AssertInterval(prism, end, start, true, Fixed64.Half - Fixed64.MinIncrement, Fixed64.Half);
    }

    [Theory]
    [InlineData(-16, -2, 16, 2, 1610612736L, 2684354560L)]
    [InlineData(16, 2, -16, -2, 1610612736L, 2684354560L)]
    [InlineData(-8, 1, 16, 1, 715827883L, 2147483648L)]
    [InlineData(16, 1, -8, 1, 2147483648L, 3579139413L)]
    [InlineData(-4, -4, 12, 4, 0L, 2147483648L)]
    [InlineData(12, 4, -4, -4, 2147483648L, 4294967296L)]
    [InlineData(-4, 4, 12, -4, 0L, 2147483648L)]
    [InlineData(12, -4, -4, 4, 2147483648L, 4294967296L)]
    public void RectangularInterval_ShouldRetainCrossingsWhenOtherEdgesAreStrictlySeparated(
        int startEighthsX, int startEighthsZ, int endEighthsX, int endEighthsZ,
        long enterRaw, long exitRaw)
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Vector2d start = new Vector2d(
            Fixed64.FromFraction(startEighthsX, 8), Fixed64.FromFraction(startEighthsZ, 8));
        Vector2d end = new Vector2d(
            Fixed64.FromFraction(endEighthsX, 8), Fixed64.FromFraction(endEighthsZ, 8));

        // Literal slab crossings include 1/6 and 5/6 rounding, and first/last-vertex
        // contact. Rejecting a whole prism from one separated edge loses these hits.
        AssertInterval(prism, start, end, true, Fixed64.FromRaw(enterRaw), Fixed64.FromRaw(exitRaw));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LongDiagonal_ShouldDistinguishExactCornerContactFromASubParameterRoundingMiss(
        bool miss, bool reverse)
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Fixed64 offset = Fixed64.One + (miss ? Fixed64.MinIncrement : Fixed64.Zero);
        Vector2d start = new(new Fixed64(-1000000000), new Fixed64(-1000000000) + offset);
        Vector2d end = new(new Fixed64(1000000000), new Fixed64(1000000000) + offset);
        if (reverse)
            (start, end) = (end, start);

        // The corner is at X=-1/2: its exact parameter rounds to 1/2 minus
        // one raw unit (plus one on reversal). Moving the supporting line
        // one raw coordinate unit above it is a miss even if both slab limits
        // would round to the same parameter.
        Fixed64 parameter = miss ? Fixed64.Zero
            : Fixed64.FromRaw(reverse ? 2147483649L : 2147483647L);
        AssertInterval(prism, start, end, !miss, parameter, parameter);
    }

    [Theory]
    [InlineData(16, 0, 24, 8, false, 0, 0)]
    [InlineData(-24, -8, -16, 0, false, 0, 0)]
    [InlineData(0, 16, 8, 24, false, 0, 0)]
    [InlineData(-8, -24, 0, -16, false, 0, 0)]
    [InlineData(-2, -16, 2, 16, true, 3, 5)]
    [InlineData(-16, 2, 16, -2, true, 3, 5)]
    [InlineData(0, 0, 8, 8, true, 0, 4)]
    [InlineData(-8, -8, 0, 0, true, 4, 8)]
    public void DiagonalInterval_ShouldClipBothFiniteAxesBeforeRounding(
        int startX, int startZ, int endX, int endZ, bool intersects, int enter, int exit)
    {
        GridCellPrism prism = CreateRectangle(Vector3d.Zero);
        Vector2d start = new Vector2d(Fixed64.FromFraction(startX, 8), Fixed64.FromFraction(startZ, 8));
        Vector2d end = new Vector2d(Fixed64.FromFraction(endX, 8), Fixed64.FromFraction(endZ, 8));
        AssertInterval(prism, start, end, intersects, Fixed64.FromFraction(enter, 8), Fixed64.FromFraction(exit, 8));
        AssertInterval(prism, end, start, intersects,
            intersects ? Fixed64.FromFraction(8 - exit, 8) : Fixed64.Zero,
            intersects ? Fixed64.FromFraction(8 - enter, 8) : Fixed64.Zero);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DiagonalInterval_ShouldRetainMissesWhenAPlaneOffsetCannotBeRepresented(bool lowerOffsetOverflows, bool reverse)
    {
        // In the first layout both offsets exceed the scalar range. In the
        // second only the far plane does. The generic wide predicate rejects
        // both; an overflowing slab subtraction must not become a contact.
        Vector3d center = lowerOffsetOverflows
            ? new Vector3d(Fixed64.MinValue + Fixed64.One, Fixed64.Zero, Fixed64.Zero)
            : Vector3d.Zero;
        GridCellPrism prism = CreateRectangle(center);
        Vector2d start = lowerOffsetOverflows
            ? new Vector2d(Fixed64.MaxValue - Fixed64.One, -Fixed64.One)
            : new Vector2d(reverse ? Fixed64.MaxValue : Fixed64.MinValue + Fixed64.Half, -Fixed64.One);
        Vector2d end = lowerOffsetOverflows
            ? new Vector2d(Fixed64.MaxValue, Fixed64.One)
            : new Vector2d(reverse ? Fixed64.MaxValue - Fixed64.One : start.X + Fixed64.One, Fixed64.One);
        if (lowerOffsetOverflows && reverse)
            (start, end) = (end, start);

        AssertInterval(prism, start, end, false, Fixed64.Zero, Fixed64.Zero);
    }

    [Fact]
    public void TranslatedExtremePrism_ShouldPreserveTheSameRepresentableInterval()
    {
        Vector3d center = new Vector3d(
            Fixed64.MaxValue - Fixed64.One, Fixed64.Zero, Fixed64.MinValue + Fixed64.One);
        GridCellPrism prism = CreateRectangle(center);
        Vector2d start = new Vector2d(center.X - Fixed64.One, center.Z - Fixed64.One);
        Vector2d end = new Vector2d(center.X + Fixed64.One, center.Z + Fixed64.One);

        AssertInterval(prism, start, end, true, Fixed64.Quarter, Fixed64.One - Fixed64.Quarter);
        AssertInterval(prism, end, start, true, Fixed64.Quarter, Fixed64.One - Fixed64.Quarter);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexInterval_ShouldPreserveAxisCrossingsVertexTouchesEdgesAndAabbOnlyMisses(HexOrientation orientation)
    {
        Assert.True(GridCellGeometry.TryCreatePrism(GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(new Fixed64(2), new Fixed64(2), orientation),
            Vector3d.Zero, default, out GridCellPrism prism));
        bool pointy = orientation == HexOrientation.PointyTop;
        Vector2d start = pointy ? new Vector2d(0, -4) : new Vector2d(-4, 0);
        Vector2d end = pointy ? new Vector2d(0, 4) : new Vector2d(4, 0);
        AssertInterval(prism, start, end, true, Fixed64.Quarter, Fixed64.One - Fixed64.Quarter);
        AssertInterval(prism, end, start, true, Fixed64.Quarter, Fixed64.One - Fixed64.Quarter);

        start = pointy ? new Vector2d(-4, 2) : new Vector2d(2, -4);
        end = pointy ? new Vector2d(4, 2) : new Vector2d(2, 4);
        AssertInterval(prism, start, end, true, Fixed64.Half, Fixed64.Half);
        AssertInterval(prism, end, start, true, Fixed64.Half, Fixed64.Half);

        start = new Vector2d(-4, 0);
        end = new Vector2d(0, 4);
        AssertInterval(prism, start, end, false, Fixed64.Zero, Fixed64.Zero);
        AssertInterval(prism, end, start, false, Fixed64.Zero, Fixed64.Zero);

        start = prism.GetFootprintVertex(1);
        end = prism.GetFootprintVertex(2);
        AssertInterval(prism, start, end, true, Fixed64.Zero, Fixed64.One);
        AssertInterval(prism, end, start, true, Fixed64.Zero, Fixed64.One);
    }

    [Fact]
    public void DiagonalTrace_ShouldRetainAllCandidateWorkAndCanonicalPointPeers()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, new Vector3d(4, 0, 4)), out _));
        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(32);
        GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(2, 32);
        Vector3d start = new Vector3d(-Fixed64.Half, Fixed64.Zero, -Fixed64.Half);
        Vector3d end = new Vector3d(new Fixed64(4) + Fixed64.Half, Fixed64.Zero, new Fixed64(4) + Fixed64.Half);

        GridTraceIntervalReport report = Trace(25, 13, 26);
        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(25, report.AddressCandidateCount);
        Assert.Equal(13, report.IntervalCount);
        Assert.True(report.HasContinuousAddressCoverage);
        Assert.True(report.HasContinuousPhysicalCoverage);
        Assert.Equal(new VoxelIndex(0, 0, 0), results[0].Cell.VoxelIndex);
        Assert.Equal(Fixed64.Zero, results[0].TEnter);
        Assert.Equal(Fixed64.FromFraction(1, 5), results[0].TExit);
        for (int boundary = 1; boundary <= 4; boundary++)
        {
            int offset = 1 + (boundary - 1) * 3;
            Fixed64 enter = Fixed64.FromFraction(boundary, 5);
            Assert.Equal(new VoxelIndex(boundary, 0, boundary), results[offset].Cell.VoxelIndex);
            Assert.Equal(enter, results[offset].TEnter);
            Assert.Equal(Fixed64.FromFraction(boundary + 1, 5), results[offset].TExit);
            Assert.Equal(new VoxelIndex(boundary - 1, 0, boundary), results[offset + 1].Cell.VoxelIndex);
            Assert.Equal(new VoxelIndex(boundary, 0, boundary - 1), results[offset + 2].Cell.VoxelIndex);
            Assert.Equal(enter, results[offset + 1].TEnter);
            Assert.Equal(enter, results[offset + 1].TExit);
            Assert.Equal(enter, results[offset + 2].TEnter);
            Assert.Equal(enter, results[offset + 2].TExit);
            Assert.Equal(results[offset + 1].TieGroupId, results[offset + 2].TieGroupId);
            Assert.Equal(0, results[offset + 1].TieOrder);
            Assert.Equal(1, results[offset + 2].TieOrder);
        }

        report = Trace(24, 13, 26);
        Assert.Equal(GridTraceIntervalStatus.AddressCandidateLimitExceeded, report.Status);
        Assert.Equal(24, report.AddressCandidateCount);
        Assert.Empty(results);
        report = Trace(25, 13, 25);
        Assert.Equal(GridTraceIntervalStatus.CandidateWorkLimitExceeded, report.Status);
        Assert.Equal(24, report.AddressCandidateCount);
        Assert.Empty(results);
        report = Trace(25, 12, 26);
        Assert.Equal(GridTraceIntervalStatus.OutputLimitExceeded, report.Status);
        Assert.Equal(25, report.AddressCandidateCount);
        Assert.Empty(results);

        GridTraceIntervalReport Trace(int addresses, int output, long work) => GridTracer.TraceIntervalsInto(
            world, start, end, results, scratch, gridCandidateLimit: 1,
            addressCandidateLimit: addresses, outputLimit: output, candidateWorkLimit: work);
    }

    [Fact]
    public void DisjointUnrepresentableCandidate_ShouldStillFailAfterAnEarlierIntersection()
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Vector3d invalidCenter = new Vector3d(2, 0, 1);
        Assert.True(world.TryAddGrid(new GridConfiguration(invalidCenter, invalidCenter,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.MinIncrement, Fixed64.One, Fixed64.One)), out _));
        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(4);
        GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(2, 4);

        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(world, Vector3d.Zero,
            new Vector3d(4, 0, 4), results, scratch, gridCandidateLimit: 2,
            addressCandidateLimit: 4, outputLimit: 4, candidateWorkLimit: 6);

        Assert.Equal(GridTraceIntervalStatus.UnrepresentableGeometry, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(2, report.AddressCandidateCount);
        Assert.Empty(results);

        report = GridTracer.TraceIntervalsInto(world, Vector3d.Zero,
            new Vector3d(4, 0, 4), results, scratch, gridCandidateLimit: 2,
            addressCandidateLimit: 1, outputLimit: 4, candidateWorkLimit: 6);
        Assert.Equal(GridTraceIntervalStatus.AddressCandidateLimitExceeded, report.Status);
        Assert.Equal(1, report.AddressCandidateCount);
        Assert.Empty(results);

        report = GridTracer.TraceIntervalsInto(world, Vector3d.Zero,
            new Vector3d(4, 0, 4), results, scratch, gridCandidateLimit: 2,
            addressCandidateLimit: 4, outputLimit: 0, candidateWorkLimit: 6);
        Assert.Equal(GridTraceIntervalStatus.OutputLimitExceeded, report.Status);
        Assert.Equal(2, report.AddressCandidateCount);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SpatialDiagonal_ShouldRetainClosedCornerPeersAndFullRangeBudgets(bool sparse, bool reverse)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        GridConfiguration configuration = new GridConfiguration(Vector3d.Zero, new Vector3d(4, 4, 4),
            storageKind: sparse ? Storage.GridStorageKind.Sparse : Storage.GridStorageKind.Dense);
        if (sparse)
            Assert.True(world.TryAddGrid(configuration, new[] { new VoxelIndex(0, 0, 0) }, out _));
        else
            Assert.True(world.TryAddGrid(configuration, out _));

        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(128);
        GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(1, 125);
        Vector3d start = reverse ? new Vector3d(4, 4, 4) : Vector3d.Zero;
        Vector3d end = reverse ? Vector3d.Zero : new Vector3d(4, 4, 4);
        GridTraceIntervalReport report = Trace(125, 29, 126);
        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(125, report.AddressCandidateCount);
        Assert.Equal(29, results.Count);
        Assert.True(report.HasContinuousAddressCoverage);
        Assert.Equal(!sparse, report.HasContinuousPhysicalCoverage);

        // Five positive-length cube intervals and six point-only peers at each
        // of four corners: 5 + 4 * 6 = 29 closed-set contacts.
        for (int index = 0; index <= 4; index++)
            Check(new VoxelIndex(index, index, index),
                index == 0 ? Fixed64.Zero : Fixed64.FromFraction(2 * index - 1, 8),
                index == 4 ? Fixed64.One : Fixed64.FromFraction(2 * index + 1, 8));
        for (int corner = 0; corner < 4; corner++)
        {
            Fixed64 parameter = Fixed64.FromFraction(2 * corner + 1, 8);
            for (int peer = 1; peer < 7; peer++)
                Check(new VoxelIndex(corner + (peer & 1), corner + ((peer >> 1) & 1),
                    corner + ((peer >> 2) & 1)), parameter, parameter);
        }

        report = Trace(124, 29, 126);
        Assert.Equal(GridTraceIntervalStatus.AddressCandidateLimitExceeded, report.Status);
        Assert.Equal(124, report.AddressCandidateCount);
        Assert.Empty(results);
        report = Trace(125, 29, 125);
        Assert.Equal(GridTraceIntervalStatus.CandidateWorkLimitExceeded, report.Status);
        Assert.Equal(124, report.AddressCandidateCount);
        Assert.Empty(results);
        report = Trace(125, 28, 126);
        Assert.Equal(GridTraceIntervalStatus.OutputLimitExceeded, report.Status);
        Assert.Equal(125, report.AddressCandidateCount);
        Assert.Empty(results);

        GridTraceIntervalReport Trace(int addresses, int output, long work) =>
            GridTracer.TraceIntervalsInto(world, start, end, results, scratch, 1, addresses, output, work);

        void Check(VoxelIndex index, Fixed64 enter, Fixed64 exit)
        {
            int matches = 0;
            foreach (GridTraceInterval interval in results)
            {
                if (interval.Cell.VoxelIndex != index)
                    continue;
                matches++;
                Assert.Equal(reverse ? Fixed64.One - exit : enter, interval.TEnter);
                Assert.Equal(reverse ? Fixed64.One - enter : exit, interval.TExit);
                Assert.Equal(!sparse || index == default, interval.IsPhysicallyPresent);
            }
            Assert.Equal(1, matches);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HorizontalTrace_ShouldRetainRoundedCollinearEndpointIntervals(bool reverse)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, new Vector3d(1, 0, 0)), out _));
        Vector3d start = new Vector3d(new Fixed64(-1073741824), Fixed64.Zero, Fixed64.Half);
        Vector3d end = new Vector3d(Fixed64.FromFraction(3, 8), Fixed64.Zero, Fixed64.Half);
        if (reverse)
            (start, end) = (end, start);
        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(2);

        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
            world, start, end, results, new GridTraceIntervalScratch(1, 2), 1, 2, 2, 3);

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(2, report.AddressCandidateCount);
        Assert.Equal(2, results.Count);
        Assert.Equal(new VoxelIndex(0, 0, 0), results[0].Cell.VoxelIndex);
        Assert.Equal(reverse ? Fixed64.Zero : Fixed64.FromRaw(4294967293L), results[0].TEnter);
        Assert.Equal(reverse ? Fixed64.FromRaw(3L) : Fixed64.One, results[0].TExit);
        // The existing collinear projection rounds a parameter just outside
        // [0, 1] onto its endpoint. Candidate narrowing must not redefine it.
        Assert.Equal(new VoxelIndex(1, 0, 0), results[1].Cell.VoxelIndex);
        Assert.Equal(reverse ? Fixed64.Zero : Fixed64.One, results[1].TEnter);
        Assert.Equal(results[1].TEnter, results[1].TExit);
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(2, 3, false)]
    [InlineData(2, 3, true)]
    public void ShortXTrace_ShouldOmitDisjointColumnsWithoutChangingLogicalAdmission(
        int width, int length, bool reverse)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, new Vector3d(width, 0, 4 * length),
            topologyMetrics: GridTopologyMetrics.Rectangular(new Fixed64(width), Fixed64.Two, new Fixed64(length))), out _));
        Vector3d start = Vector3d.Zero;
        Vector3d end = new Vector3d(Fixed64.FromFraction(3 * width, 8), Fixed64.Zero, new Fixed64(4 * length));
        if (reverse)
            (start, end) = (end, start);
        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(5);
        GridTraceIntervalScratch scratch = new GridTraceIntervalScratch(1, 10);

        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(world, start, end, results, scratch, 1, 10, 5, 11);

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(10, report.AddressCandidateCount);
        Assert.Equal(5, results.Count);
        Assert.True(report.HasContinuousPhysicalCoverage);
        for (int z = 0; z < 5; z++)
        {
            int expectedZ = reverse ? 4 - z : z;
            Assert.Equal(new VoxelIndex(0, 0, expectedZ), results[z].Cell.VoxelIndex);
            Assert.Equal(z == 0 ? Fixed64.Zero : Fixed64.FromFraction(2 * z - 1, 8), results[z].TEnter);
            Assert.Equal(z == 4 ? Fixed64.One : Fixed64.FromFraction(2 * z + 1, 8), results[z].TExit);
        }

        report = GridTracer.TraceIntervalsInto(world, start, end, results, scratch, 1, 9, 5, 11);
        Assert.Equal(GridTraceIntervalStatus.AddressCandidateLimitExceeded, report.Status);
        Assert.Equal(9, report.AddressCandidateCount);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FullDomainTrace_ShouldPreserveWideIntervalsWithOrWithoutRepresentableXDelta(bool fullX, bool reverse)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(Vector3d.Zero, Vector3d.Zero), out _));
        Vector3d start = new Vector3d(fullX ? Fixed64.MinValue : -Fixed64.One, Fixed64.Zero, Fixed64.MinValue);
        Vector3d end = new Vector3d(fullX ? Fixed64.MaxValue : Fixed64.One, Fixed64.Zero, Fixed64.MaxValue);
        if (reverse)
            (start, end) = (end, start);
        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(1);

        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
            world, start, end, results, new GridTraceIntervalScratch(1, 1), 1, 1, 1, 2);

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(1, report.AddressCandidateCount);
        GridTraceInterval interval = Assert.Single(results);
        Assert.Equal(default, interval.Cell.VoxelIndex);
        Assert.Equal(reverse ? Fixed64.FromRaw(2147483647L) : Fixed64.Half, interval.TEnter);
        Assert.Equal(reverse ? Fixed64.Half : Fixed64.FromRaw(2147483649L), interval.TExit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialDiagonal_ShouldRetainBoundaryPeersAfterEmptyColumnRanges(bool reverse)
    {
        using GridWorld world = GridWorldTestFactory.CreateWorld();
        Assert.True(world.TryAddGrid(new GridConfiguration(new Vector3d(0, 0, 2), new Vector3d(4, 0, 4)), out _));
        Vector3d start = new Vector3d(-4, 0, -4);
        Vector3d end = new Vector3d(4, 0, 4);
        if (reverse)
            (start, end) = (end, start);
        SwiftList<GridTraceInterval> results = new SwiftList<GridTraceInterval>(8);

        GridTraceIntervalReport report = GridTracer.TraceIntervalsInto(
            world, start, end, results, new GridTraceIntervalScratch(1, 15), 1, 15, 8, 16);

        Assert.Equal(GridTraceIntervalStatus.Complete, report.Status);
        Assert.Equal(15, report.AddressCandidateCount);
        Assert.Equal(8, results.Count);
        Assert.False(report.HasContinuousAddressCoverage);
        // Z addresses are relative to the grid's world-space origin at Z=2.
        (int x, int z, int enter, int exit)[] expected =
        {
            (1, 0, 11, 11), (2, 0, 11, 13),
            (2, 1, 13, 13), (3, 0, 13, 13), (3, 1, 13, 15),
            (3, 2, 15, 15), (4, 1, 15, 15), (4, 2, 15, 16)
        };
        foreach ((int x, int z, int enter, int exit) in expected)
        {
            Fixed64 expectedEnter = Fixed64.FromFraction(reverse ? 16 - exit : enter, 16);
            Fixed64 expectedExit = Fixed64.FromFraction(reverse ? 16 - enter : exit, 16);
            Assert.Contains(results, value => value.Cell.VoxelIndex == new VoxelIndex(x, 0, z)
                && value.TEnter == expectedEnter && value.TExit == expectedExit);
        }
    }

    private static GridCellPrism CreateRectangle(Vector3d center)
    {
        Assert.True(GridCellGeometry.TryCreatePrism(GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(Fixed64.One), center, default, out GridCellPrism prism));
        return prism;
    }

    private static void AssertInterval(GridCellPrism prism, Vector2d start, Vector2d end,
        bool expected, Fixed64 expectedEnter, Fixed64 expectedExit)
    {
        Assert.Equal(expected, GridCellGeometry.TryGetPlanarSegmentInterval(
            prism, start, end, out Fixed64 enter, out Fixed64 exit));
        Assert.Equal(expectedEnter, enter);
        Assert.Equal(expectedExit, exit);
    }
}
