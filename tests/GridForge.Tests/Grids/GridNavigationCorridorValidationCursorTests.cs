using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

[Collection("GridForgeCollection")]
public sealed class GridNavigationCorridorValidationCursorTests
{
    [Fact]
    public void Advance_NegativeWorkBudget_ShouldRejectInsteadOfRemainingInProgress()
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            cursor.Advance(cells, new Vector3d[2], maxWork: -1));

        Assert.Equal("maxWork", exception.ParamName);
        Assert.Equal(GridNavigationCorridorValidationStatus.InProgress, cursor.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_CurrentPortalEmitsEachRectangularCertificateInOrder(bool reverse)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(2));
        var cells = new GridCellPrism[3];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(2, 0, 0),
            default,
            out cells[1]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(2, 2, 0),
            default,
            out cells[2]));
        if (reverse)
            Array.Reverse(cells);

        var expected = new GridNavigationPortal[cells.Length - 1];
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(GridCellGeometry.TryCreateNavigationPortal(
                cells[i],
                cells[i + 1],
                out expected[i]));
        }

        var actual = new GridNavigationPortal[expected.Length];
        int actualCount = 0;
        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[cells.Length - 1].Center,
            Fixed64.Zero,
            Fixed64.One);
        while (cursor.Status == GridNavigationCorridorValidationStatus.InProgress)
        {
            cursor.Advance(cells, waypoints, maxWork: 1);
            if (cursor.TryGetCurrentPortal(out GridNavigationPortal portal))
                actual[actualCount++] = portal;
        }

        Assert.Equal(GridNavigationCorridorValidationStatus.Complete, cursor.Status);
        Assert.False(cursor.TryGetCurrentPortal(out GridNavigationPortal none));
        Assert.Equal(default, none);
        Assert.Equal(expected.Length, actualCount);
        Assert.Equal(expected, actual);
        Assert.Equal(VoxelContactFaceKind.Vertical, actual[reverse ? 1 : 0].FaceKind);
        Assert.Equal(VoxelContactFaceKind.Horizontal, actual[reverse ? 0 : 1].FaceKind);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, false)]
    [InlineData(HexOrientation.PointyTop, true)]
    [InlineData(HexOrientation.FlatTop, false)]
    [InlineData(HexOrientation.FlatTop, true)]
    public void Advance_CurrentPortalPreservesHexCertificateAndDirection(
        HexOrientation orientation,
        bool reverse)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(2),
            orientation);
        Vector3d targetCenter = HexCoordinateUtility.AxialToWorldOffset(
            new VoxelIndex(1, 0, 0),
            metrics);
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            metrics,
            targetCenter,
            default,
            out cells[1]));
        if (reverse)
            Array.Reverse(cells);
        Assert.True(GridCellGeometry.TryCreateNavigationPortal(
            cells[0],
            cells[1],
            out GridNavigationPortal expected));

        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);
        GridNavigationPortal actual = default;
        int producedCount = 0;
        while (cursor.Status == GridNavigationCorridorValidationStatus.InProgress)
        {
            cursor.Advance(cells, waypoints, maxWork: 1);
            if (cursor.TryGetCurrentPortal(out GridNavigationPortal produced))
            {
                actual = produced;
                producedCount++;
            }
        }

        Assert.Equal(1, producedCount);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Advance_CurrentPortalIsSynchronizedWithItsPortalWorkUnit()
    {
        Assert.Equal(224, Unsafe.SizeOf<GridNavigationCorridorValidationCursor>());
        GridCellPrism[] cells = CreateRectangularChain(2);
        Assert.True(GridCellGeometry.TryCreateNavigationPortal(
            cells[0],
            cells[1],
            out GridNavigationPortal expected));
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(
                GridNavigationCorridorValidationStatus.InProgress,
                cursor.Advance(cells, waypoints, maxWork: 1));
            Assert.False(cursor.TryGetCurrentPortal(out GridNavigationPortal none));
            Assert.Equal(default, none);
        }

        Assert.Equal(
            GridNavigationCorridorValidationStatus.InProgress,
            cursor.Advance(cells, waypoints, maxWork: 1));
        Assert.True(cursor.TryGetCurrentPortal(out GridNavigationPortal current));
        Assert.Equal(expected, current);
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, maxWork: 1));
        Assert.False(cursor.TryGetCurrentPortal(out GridNavigationPortal completed));
        Assert.Equal(default, completed);
    }

    [Fact]
    public void Advance_BatchedExitWorkDoesNotExposeEarlierPortalUnit()
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, maxWork: 5));
        Assert.False(cursor.TryGetCurrentPortal(out GridNavigationPortal portal));
        Assert.Equal(default, portal);
    }

    [Fact]
    public void Advance_InvalidPortalClearsPreviouslyProducedCertificate()
    {
        GridCellPrism[] chain = CreateRectangularChain(4);
        GridCellPrism[] cells = { chain[0], chain[1], chain[3] };
        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[2].Center,
            Fixed64.Zero,
            Fixed64.One);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(
                GridNavigationCorridorValidationStatus.InProgress,
                cursor.Advance(cells, waypoints, maxWork: 1));
            Assert.False(cursor.TryGetCurrentPortal(out _));
        }

        Assert.Equal(
            GridNavigationCorridorValidationStatus.InProgress,
            cursor.Advance(cells, waypoints, maxWork: 1));
        Assert.True(cursor.TryGetCurrentPortal(out GridNavigationPortal first));
        Assert.True(first.IsValid);
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, maxWork: 1));
        Assert.False(cursor.TryGetCurrentPortal(out GridNavigationPortal stale));
        Assert.Equal(default, stale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_PartialVerticalPortalRejectsEndpointBesideHorizontalOpening(
        bool exitIsInvalid)
    {
        GridCellPrism[] cells = CreateOffsetPartialFaceCells(out GridNavigationPortal portal);
        Vector3d invalidAnchor = exitIsInvalid
            ? new Vector3d(2, 0, 3)
            : new Vector3d(2, 0, -1);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            exitIsInvalid ? portal.CanonicalFacePoint : invalidAnchor,
            exitIsInvalid ? invalidAnchor : portal.CanonicalFacePoint,
            Fixed64.One / new Fixed64(2),
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_PartialVerticalPortalRejectsEndpointOutsideVerticalOpening(
        bool exitIsInvalid)
    {
        GridCellPrism[] cells = CreateOffsetPartialFaceCells(out GridNavigationPortal portal);
        Vector3d invalidAnchor = exitIsInvalid
            ? new Vector3d(2, 2, 1)
            : new Vector3d(2, -1, 1);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            exitIsInvalid ? portal.CanonicalFacePoint : invalidAnchor,
            exitIsInvalid ? invalidAnchor : portal.CanonicalFacePoint,
            Fixed64.One / new Fixed64(2),
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Fact]
    public void Advance_PartialVerticalPortalRejectsBodyClippingOpeningEnd()
    {
        GridCellPrism[] cells = CreateOffsetPartialFaceCells(out GridNavigationPortal portal);
        var entry = new Vector3d(
            new Fixed64(7) / new Fixed64(4),
            Fixed64.Zero,
            Fixed64.One / new Fixed64(4));
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            entry,
            portal.CanonicalFacePoint,
            Fixed64.One / new Fixed64(2),
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_PositiveRadiusEntryApproachingFirstVerticalPortal_Completes(bool reverse)
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        if (reverse)
            Array.Reverse(cells);

        Fixed64 direction = reverse ? -Fixed64.One : Fixed64.One;
        var entry = new Vector3d(
            cells[0].Center.X + (direction / new Fixed64(2)),
            cells[0].VerticalMin,
            cells[0].Center.Z);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            entry,
            cells[1].Center,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_PositiveRadiusExitApproachingLastVerticalPortal_Completes(bool reverse)
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        if (reverse)
            Array.Reverse(cells);

        Fixed64 direction = reverse ? -Fixed64.One : Fixed64.One;
        var exit = new Vector3d(
            cells[1].Center.X - (direction / new Fixed64(2)),
            cells[1].VerticalMin,
            cells[1].Center.Z);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            exit,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Fact]
    public void Advance_PositiveRadiusAnchorsOnFirstAndLastVerticalPortals_Complete()
    {
        GridCellPrism[] cells = CreateRectangularChain(3);
        var entry = new Vector3d(1, -1, 0);
        var exit = new Vector3d(3, -1, 0);
        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            entry,
            exit,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, false)]
    [InlineData(HexOrientation.PointyTop, true)]
    [InlineData(HexOrientation.FlatTop, false)]
    [InlineData(HexOrientation.FlatTop, true)]
    public void Advance_PositiveRadiusHexAnchorsOnSelectedVerticalPortal_Complete(
        HexOrientation orientation,
        bool reverse)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(2),
            orientation);
        Vector3d targetCenter = HexCoordinateUtility.AxialToWorldOffset(
            new VoxelIndex(1, 0, 0),
            metrics);
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            metrics,
            targetCenter,
            default,
            out cells[1]));
        if (reverse)
            Array.Reverse(cells);

        Assert.True(GridCellGeometry.TryCreateNavigationPortal(
            cells[0],
            cells[1],
            out GridNavigationPortal portal));
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            portal.CanonicalFacePoint,
            portal.CanonicalFacePoint,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Advance_PositiveRadiusEndpointNearOtherWall_RemainsInvalid(bool exitIsInvalid)
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        Vector3d invalidAnchor = exitIsInvalid
            ? new Vector3d(2, -1, 1)
            : new Vector3d(0, -1, 1);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            exitIsInvalid ? cells[0].Center : invalidAnchor,
            exitIsInvalid ? invalidAnchor : cells[1].Center,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Fact]
    public void Advance_WithOneWorkUnit_CompletesOnlyAfterEveryCellAndPortalIsValidated()
    {
        GridCellPrism[] cells = CreateRectangularChain(3);
        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[2].Center,
            Fixed64.One,
            Fixed64.One);

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(
                GridNavigationCorridorValidationStatus.InProgress,
                cursor.Advance(cells, waypoints, 1));
        }

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, 1));
        Assert.Equal(2, cursor.PortalWaypointCount);
        Assert.Equal(new Vector3d(1, -1, 0), waypoints[0]);
        Assert.Equal(new Vector3d(3, -1, 0), waypoints[1]);
        Assert.True(Vector3d.TryGetDistance(cells[0].Center, waypoints[0], out Fixed64 first));
        Assert.True(Vector3d.TryGetDistance(waypoints[0], waypoints[1], out Fixed64 second));
        Assert.True(Vector3d.TryGetDistance(waypoints[1], cells[2].Center, out Fixed64 third));
        Assert.True(Fixed64.TryAdd(first, second, out Fixed64 prefix));
        Assert.True(Fixed64.TryAdd(prefix, third, out Fixed64 expectedCost));
        Assert.Equal(expectedCost, cursor.GeometricCost);
    }

    [Fact]
    public void Advance_HorizontalPortalRetainsBothCanonicalWaypoints()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(2),
            new Fixed64(2),
            new Fixed64(2));
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(0, 2, 0),
            default,
            out cells[1]));
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, 5));
        Assert.Equal(2, cursor.PortalWaypointCount);
        Assert.Equal(Vector3d.Zero, waypoints[0]);
        Assert.Equal(new Vector3d(0, 1, 0), waypoints[1]);
        Assert.Equal(new Fixed64(2), cursor.GeometricCost);
    }

    [Fact]
    public void Advance_PointyHexPortalRetainsExactRawCertificate()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(
            new Fixed64(2),
            new Fixed64(2),
            HexOrientation.PointyTop);
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[0]));
        var targetCenter = new Vector3d(Fixed64.FromRaw(14_878_203_148L), Fixed64.Zero, Fixed64.Zero);
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.HexPrism,
            metrics,
            targetCenter,
            default,
            out cells[1]));
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.One,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            cursor.Advance(cells, waypoints, 5));
        var expectedWaypoint = new Vector3d(
            Fixed64.FromRaw(7_439_101_574L),
            -Fixed64.One,
            Fixed64.Zero);
        Assert.Equal(1, cursor.PortalWaypointCount);
        Assert.Equal(expectedWaypoint, waypoints[0]);
        Assert.True(Vector3d.TryGetDistance(cells[0].Center, expectedWaypoint, out Fixed64 first));
        Assert.True(Vector3d.TryGetDistance(expectedWaypoint, cells[1].Center, out Fixed64 second));
        Assert.True(Fixed64.TryAdd(first, second, out Fixed64 expectedCost));
        Assert.Equal(expectedCost, cursor.GeometricCost);
    }

    [Fact]
    public void Advance_CarryoverAndOneShotProduceTheSameCertificate()
    {
        GridCellPrism[] cells = CreateRectangularChain(3);
        Vector3d[] expectedWaypoints = new Vector3d[4];
        Assert.True(GridCellGeometry.TryValidateNavigationCorridor(
            cells,
            cells[0].Center,
            cells[2].Center,
            Fixed64.Zero,
            Fixed64.One,
            expectedWaypoints,
            out int expectedCount,
            out Fixed64 expectedCost));

        Vector3d[] actualWaypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[2].Center,
            Fixed64.Zero,
            Fixed64.One);
        while (cursor.Status == GridNavigationCorridorValidationStatus.InProgress)
            cursor.Advance(cells, actualWaypoints, 1);

        Assert.Equal(GridNavigationCorridorValidationStatus.Complete, cursor.Status);
        Assert.Equal(expectedCount, cursor.PortalWaypointCount);
        Assert.Equal(expectedCost, cursor.GeometricCost);
        Assert.Equal(expectedWaypoints.AsSpan(0, expectedCount).ToArray(), actualWaypoints.AsSpan(0, expectedCount).ToArray());
    }

    [Fact]
    public void Advance_MalformedCarryoverFailsClosedAndStaysTerminal()
    {
        GridCellPrism[] cells = CreateRectangularChain(3);
        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[2].Center,
            Fixed64.Zero,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.InProgress,
            cursor.Advance(cells, waypoints, 1));
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells.AsSpan(0, 2), waypoints, 1));
        Assert.Equal(0, cursor.PortalWaypointCount);
        Assert.Equal(Fixed64.Zero, cursor.GeometricCost);
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, int.MaxValue));
    }

    [Fact]
    public void Advance_DisconnectedPortalFailsOnItsOwnWorkUnit()
    {
        GridCellPrism[] chain = CreateRectangularChain(4);
        GridCellPrism[] cells = { chain[0], chain[1], chain[3] };
        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[2].Center,
            Fixed64.Zero,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.InProgress,
            cursor.Advance(cells, waypoints, 5));
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, 1));
    }

    [Fact]
    public void Advance_CostOverflowIsDistinctAndResetsTheCertificate()
    {
        Fixed64 width = new Fixed64(1_500_000_000);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            width,
            new Fixed64(2),
            new Fixed64(2));
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(-750_000_000, 0, 0),
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(750_000_000, 0, 0),
            default,
            out cells[1]));
        var entry = new Vector3d(-1_500_000_000, -1, 0);
        var exit = new Vector3d(1_500_000_000, -1, 0);
        Vector3d[] waypoints = new Vector3d[2];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            entry,
            exit,
            Fixed64.Zero,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.CostOverflow,
            cursor.Advance(cells, waypoints, int.MaxValue));
        Assert.Equal(0, cursor.PortalWaypointCount);
        Assert.Equal(Fixed64.Zero, cursor.GeometricCost);
    }

    [Fact]
    public void Advance_WarmedCallerOwnedValidationAllocatesZero()
    {
        GridCellPrism[] cells = CreateRectangularChain(3);
        Vector3d[] waypoints = new Vector3d[4];
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Complete,
            Validate(cells, waypoints));

        long before = GC.GetAllocatedBytesForCurrentThread();
        GridNavigationCorridorValidationStatus status = default;
        for (int i = 0; i < 1_000; i++)
            status = Validate(cells, waypoints);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(GridNavigationCorridorValidationStatus.Complete, status);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Advance_RejectsBodySegmentThatClipsCornerBetweenSelectedPortals()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(4));
        var cells = new GridCellPrism[3];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(-4, 0, 0),
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[1]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(0, 0, 4),
            default,
            out cells[2]));

        Vector3d[] waypoints = new Vector3d[4];
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            new Vector3d(-4, -2, 0),
            new Vector3d(0, -2, 4),
            new Fixed64(3) / new Fixed64(2),
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, waypoints, int.MaxValue));
        Assert.Equal(0, cursor.PortalWaypointCount);
        Assert.Equal(Fixed64.Zero, cursor.GeometricCost);
    }

    [Fact]
    public void Advance_InvalidConstructionInputsAreImmediatelyTerminal()
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        var tooShort = new GridNavigationCorridorValidationCursor(
            1,
            cells[0].Center,
            cells[0].Center,
            Fixed64.Zero,
            Fixed64.One);
        var negativeRadius = new GridNavigationCorridorValidationCursor(
            2,
            cells[0].Center,
            cells[1].Center,
            -Fixed64.MinIncrement,
            Fixed64.One);
        var zeroHeight = new GridNavigationCorridorValidationCursor(
            2,
            cells[0].Center,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.Zero);

        Assert.Equal(GridNavigationCorridorValidationStatus.Invalid, tooShort.Status);
        Assert.Equal(GridNavigationCorridorValidationStatus.Invalid, negativeRadius.Status);
        Assert.Equal(GridNavigationCorridorValidationStatus.Invalid, zeroHeight.Status);
        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            tooShort.Advance(cells.AsSpan(0, 1), Span<Vector3d>.Empty, int.MaxValue));
    }

    [Fact]
    public void Advance_MalformedCellGeometryFailsBeforePortalValidation()
    {
        GridCellPrism[] cells = CreateRectangularChain(2);
        var missingFootprint = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);
        cells[0] = default;

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            missingFootprint.Advance(cells, new Vector3d[2], int.MaxValue));

        Vector2d[] footprint =
        {
            new(-1, -1),
            new(1, -1),
            new(1, 1),
            new(-1, 1)
        };
        cells = CreateRectangularChain(2);
        cells[0] = new GridCellPrism(
            default,
            GridTopologyKind.RectangularPrism,
            Vector3d.Zero,
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.One,
            footprint);
        var invertedVerticalSpan = new GridNavigationCorridorValidationCursor(
            cells.Length,
            Vector3d.Zero,
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            invertedVerticalSpan.Advance(cells, new Vector3d[2], int.MaxValue));
    }

    [Fact]
    public void Advance_SecondPortalRejectsAProfileThatFitTheFirstPortal()
    {
        var cells = new GridCellPrism[3];
        GridTopologyMetrics wide = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            new Fixed64(4),
            new Fixed64(8));
        GridTopologyMetrics narrow = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            new Fixed64(4),
            new Fixed64(2));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            wide,
            Vector3d.Zero,
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            wide,
            new Vector3d(4, 0, 0),
            default,
            out cells[1]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            narrow,
            new Vector3d(8, 0, 0),
            default,
            out cells[2]));
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            cells[0].Center,
            cells[2].Center,
            new Fixed64(2),
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.Invalid,
            cursor.Advance(cells, new Vector3d[4], int.MaxValue));
        Assert.Equal(0, cursor.PortalWaypointCount);
        Assert.Equal(Fixed64.Zero, cursor.GeometricCost);
    }

    [Fact]
    public void Advance_FirstPortalDistanceOverflowFailsBeforePublishingACertificate()
    {
        Fixed64 width = new Fixed64(1_600_000_000);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            width,
            new Fixed64(2),
            width);
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(-800_000_000, 0, -800_000_000),
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(800_000_000, 0, 790_000_000),
            default,
            out cells[1]));
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            new Vector3d(-1_600_000_000, -1, -1_600_000_000),
            cells[1].Center,
            Fixed64.Zero,
            Fixed64.One);

        Assert.Equal(
            GridNavigationCorridorValidationStatus.CostOverflow,
            cursor.Advance(cells, new Vector3d[2], int.MaxValue));
        Assert.False(cursor.TryGetCurrentPortal(out _));
        Assert.Equal(0, cursor.PortalWaypointCount);
        Assert.Equal(Fixed64.Zero, cursor.GeometricCost);
    }

    [Fact]
    public void Advance_HorizontalPortalCostOverflowResetsEarlierWaypoints()
    {
        Fixed64 width = new Fixed64(1_200_000_000);
        Fixed64 height = new Fixed64(400_000_000);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            width,
            height,
            Fixed64.One);
        var cells = new GridCellPrism[3];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(-600_000_000, 0, 0),
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(600_000_000, 0, 0),
            default,
            out cells[1]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(600_000_000, 400_000_000, 0),
            default,
            out cells[2]));
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            new Vector3d(-1_200_000_000, -200_000_000, 0),
            new Vector3d(600_000_000, 200_000_000, 0),
            Fixed64.Zero,
            height);

        Vector3d[] waypoints = new Vector3d[4];
        Assert.Equal(
            GridNavigationCorridorValidationStatus.InProgress,
            cursor.Advance(cells, waypoints, maxWork: 5));
        Assert.True(cursor.TryGetCurrentPortal(out GridNavigationPortal firstPortal));
        Assert.Equal(VoxelContactFaceKind.Vertical, firstPortal.FaceKind);
        Assert.Equal(
            GridNavigationCorridorValidationStatus.CostOverflow,
            cursor.Advance(cells, waypoints, maxWork: 1));
        Assert.False(cursor.TryGetCurrentPortal(out _));
        Assert.Equal(0, cursor.PortalWaypointCount);
        Assert.Equal(Fixed64.Zero, cursor.GeometricCost);
    }

    private static GridNavigationCorridorValidationStatus Validate(
        GridCellPrism[] cells,
        Vector3d[] waypoints)
    {
        var cursor = new GridNavigationCorridorValidationCursor(
            cells.Length,
            new Vector3d(1, -1, 0),
            new Vector3d(3, -1, 0),
            Fixed64.One,
            Fixed64.One);
        while (cursor.Status == GridNavigationCorridorValidationStatus.InProgress)
        {
            cursor.Advance(cells, waypoints, maxWork: 1);
            cursor.TryGetCurrentPortal(out _);
        }

        return cursor.Status;
    }

    private static GridCellPrism[] CreateRectangularChain(int cellCount)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(2),
            new Fixed64(2),
            new Fixed64(2));
        var cells = new GridCellPrism[cellCount];
        for (int i = 0; i < cells.Length; i++)
        {
            Assert.True(GridCellGeometry.TryCreatePrism(
                GridTopologyKind.RectangularPrism,
                metrics,
                new Vector3d(i * 2, 0, 0),
                default,
                out cells[i]));
        }

        return cells;
    }

    private static GridCellPrism[] CreateOffsetPartialFaceCells(
        out GridNavigationPortal portal)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            new Fixed64(4),
            new Fixed64(4),
            new Fixed64(4));
        var cells = new GridCellPrism[2];
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            Vector3d.Zero,
            default,
            out cells[0]));
        Assert.True(GridCellGeometry.TryCreatePrism(
            GridTopologyKind.RectangularPrism,
            metrics,
            new Vector3d(4, 2, 2),
            default,
            out cells[1]));
        Assert.True(GridCellGeometry.TryCreateNavigationPortal(
            cells[0],
            cells[1],
            out portal));
        Assert.Equal(new Vector3d(2, 0, 1), portal.CanonicalFacePoint);
        Assert.Equal(Fixed64.One, portal.MaximumHorizontalRadius);
        Assert.Equal(new Fixed64(2), portal.MaximumBodyHeight);
        return cells;
    }
}
