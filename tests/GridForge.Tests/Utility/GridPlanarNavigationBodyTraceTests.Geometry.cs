//=======================================================================
// GridPlanarNavigationBodyTraceTests.Geometry.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Linq;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed partial class GridPlanarNavigationBodyTraceTests
{
    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void SmallRepresentableHexMetrics_DoNotLoseEndpointInInverseProjection(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(6), Fixed64.One, orientation);
        VoxelGrid grid = Add(Vector3d.Zero, HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(120, 0, 120), metrics),
            metrics: metrics, kind: GridTopologyKind.HexPrism);
        VoxelIndex source = new VoxelIndex(100, 0, 100);
        Vector2d center = grid.GetWorldPosition(source).ToVector2d();
        GridNavigationBodyTraceReport report = Trace(grid, source, source, center, center, Fixed64.Zero, Fixed64.FromRaw(1));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(Address(grid, source), Assert.Single(_results).Cell);
    }

    [Theory]
    [InlineData(1, 0)] [InlineData(-1, 0)] [InlineData(0, 1)] [InlineData(0, -1)]
    [InlineData(1, 1)] [InlineData(1, -1)] [InlineData(-1, 1)] [InlineData(-1, -1)]
    public void EveryRectangularDirection_PreservesFaceOrDiagonalClosure(int dx, int dz)
    {
        VoxelGrid grid = Add(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        VoxelIndex source = new VoxelIndex(1, 0, 1);
        VoxelIndex target = new VoxelIndex(1 + dx, 0, 1 + dz);
        GridNavigationBodyTraceReport report = Trace(grid, source, target, Vector2d.Zero, new Vector2d(dx, dz), Fixed64.Zero, Fixed64.FromFraction(1, 8));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(dx != 0 && dz != 0 ? 4 : 2, _results.Count);
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == source);
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == target);
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void WideHexCircle_ClosesAllSixPlanarNeighbors(HexOrientation orientation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        VoxelGrid grid = Add(Vector3d.Zero, HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(4, 0, 4), metrics),
            metrics: metrics, kind: GridTopologyKind.HexPrism);
        VoxelIndex source = new VoxelIndex(2, 0, 2);
        Vector2d center = grid.GetWorldPosition(source).ToVector2d();
        GridNavigationBodyTraceReport report = Trace(grid, source, source, center, center, Fixed64.Zero, Fixed64.FromFraction(5, 4));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(7, _results.Count);
        Assert.All(_results, cell => Assert.True(cell.IsPhysicallyPresent));
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop, 1, 0)]
    [InlineData(HexOrientation.PointyTop, -1, 0)]
    [InlineData(HexOrientation.PointyTop, 0, 1)]
    [InlineData(HexOrientation.PointyTop, 0, -1)]
    [InlineData(HexOrientation.PointyTop, 1, -1)]
    [InlineData(HexOrientation.PointyTop, -1, 1)]
    [InlineData(HexOrientation.FlatTop, 1, 0)]
    [InlineData(HexOrientation.FlatTop, -1, 0)]
    [InlineData(HexOrientation.FlatTop, 0, 1)]
    [InlineData(HexOrientation.FlatTop, 0, -1)]
    [InlineData(HexOrientation.FlatTop, 1, -1)]
    [InlineData(HexOrientation.FlatTop, -1, 1)]
    public void EveryHexDirection_ProducesCompleteEndpointCoverage(HexOrientation orientation, int dx, int dz)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation);
        VoxelGrid grid = Add(Vector3d.Zero, HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(4, 0, 4), metrics),
            metrics: metrics, kind: GridTopologyKind.HexPrism);
        VoxelIndex source = new VoxelIndex(2, 0, 2);
        VoxelIndex target = new VoxelIndex(2 + dx, 0, 2 + dz);
        GridNavigationBodyTraceReport report = Trace(grid, source, target,
            grid.GetWorldPosition(source).ToVector2d(), grid.GetWorldPosition(target).ToVector2d(), Fixed64.Zero, Fixed64.FromFraction(1, 8));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, _results.Count);
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == source);
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == target);
    }

    [Fact]
    public void RoundedCorner_DoesNotClaimBoundingBoxOnlyCells()
    {
        VoxelGrid grid = Add(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        GridNavigationBodyTraceReport report = Trace(grid, new VoxelIndex(1, 0, 1), new VoxelIndex(1, 0, 1),
            Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.FromFraction(3, 5));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(new[] { new VoxelIndex(0, 0, 1), new VoxelIndex(1, 0, 0), new VoxelIndex(1, 0, 1), new VoxelIndex(1, 0, 2), new VoxelIndex(2, 0, 1) },
            _results.Select(cell => cell.Cell.VoxelIndex));
    }

    [Fact]
    public void CapsuleMiddleMissingCell_IsNotHiddenByEndpointDiscs()
    {
        VoxelGrid grid = Add(new Vector3d(0, 0, -2), new Vector3d(0, 0, 2), new[]
        { new VoxelIndex(0, 0, 0), new VoxelIndex(0, 0, 1), new VoxelIndex(0, 0, 3), new VoxelIndex(0, 0, 4) });
        GridNavigationBodyTraceReport report = Trace(grid, default, default, Vector2d.Zero, Vector2d.Zero, new Fixed64(4), Fixed64.FromFraction(1, 4));
        Assert.Equal(GridNavigationBodyTraceStatus.IncompletePhysicalCoverage, report.Status);
        Assert.Equal(5, _results.Count);
        Assert.Equal(new VoxelIndex(0, 0, 2), Assert.Single(_results, cell => !cell.IsPhysicallyPresent).Cell.VoxelIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ClosedEndpointCorner_UsesExactTangencyNotRoundedNormal(int outwardRaw)
    {
        VoxelGrid grid = Add(new Vector3d(-1, 0, -1), new Vector3d(3, 0, 3));
        Vector2d center = new Vector2d(Fixed64.FromFraction(7, 8), Fixed64.One + Fixed64.FromRaw(outwardRaw));
        GridNavigationBodyTraceReport report = Trace(grid, new VoxelIndex(1, 0, 1), new VoxelIndex(1, 0, 1), center, center,
            Fixed64.Zero, Fixed64.FromFraction(5, 8));
        Assert.Equal(outwardRaw == 0 ? GridNavigationBodyTraceStatus.Complete : GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        if (outwardRaw == 0) Assert.Contains(_results, cell => cell.Cell.VoxelIndex == new VoxelIndex(1, 0, 1));
        else Assert.Empty(_results);
    }

    [Fact]
    public void OddRawCapsuleAxis_PreservesItsHalfRawExtension()
    {
        VoxelGrid grid = Add(new Vector3d(0, 0, -1), new Vector3d(0, 0, 1));
        GridNavigationBodyTraceReport report = Trace(grid, new VoxelIndex(0, 0, 1), new VoxelIndex(0, 0, 1),
            Vector2d.Zero, Vector2d.Zero, Fixed64.FromRaw(1), Fixed64.Half);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(3, _results.Count);
        Assert.Equal(new VoxelIndex(0, 0, 0), _results[0].Cell.VoxelIndex);
        Assert.Equal(new VoxelIndex(0, 0, 2), _results[2].Cell.VoxelIndex);
    }

    [Fact]
    public void IncompatibleFootprintSeam_CannotBecomeSuccessfulUnion()
    {
        VoxelGrid left = Add(Vector3d.Zero, Vector3d.Zero);
        VoxelGrid right = Add(new Vector3d(1, 0, 0), new Vector3d(1, 0, 0),
            metrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, new Fixed64(2)));
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(left), Layer(right) }, Fixed64.Zero,
            Address(left, default), Address(right, default), Vector2d.Zero, Vector2d.Right, Fixed64.Zero, Fixed64.FromFraction(1, 8),
            _results, _scratch, 2, 8, 8, 10);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Empty(_results);
    }

    [Fact]
    public void WarmedTrace_DoesNotAllocateAndStillReturnsWholeEvidence()
    {
        VoxelGrid grid = Add(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1));
        GridPlanarLayer[] layers = { Layer(grid) };
        WorldVoxelIndex address = Address(grid, new VoxelIndex(1, 0, 1));
        for (int i = 0; i < 64; i++)
            GridTracer.TracePlanarNavigationBodyInto(_world, layers, Fixed64.Zero, address, address, Vector2d.Zero, Vector2d.Zero,
                Fixed64.Zero, Fixed64.One, _results, _scratch, 8, 128, 128, 136);
        long before = GC.GetAllocatedBytesForCurrentThread();
        GridNavigationBodyTraceReport report = default;
        for (int i = 0; i < 64; i++)
            report = GridTracer.TracePlanarNavigationBodyInto(_world, layers, Fixed64.Zero, address, address, Vector2d.Zero, Vector2d.Zero,
                Fixed64.Zero, Fixed64.One, _results, _scratch, 8, 128, 128, 136);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(9, report.CellCount);
        Assert.Equal(9, _results.Count);
        Assert.All(_results, cell => Assert.True(cell.IsPhysicallyPresent));
    }
}
