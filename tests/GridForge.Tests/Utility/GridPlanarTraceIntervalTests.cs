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
