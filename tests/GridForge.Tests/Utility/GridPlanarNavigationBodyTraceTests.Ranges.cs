//=======================================================================
// GridPlanarNavigationBodyTraceTests.Ranges.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed partial class GridPlanarNavigationBodyTraceTests
{
    [Theory]
    [InlineData(HexOrientation.PointyTop, 6, -1000)]
    [InlineData(HexOrientation.PointyTop, 8, 1000)]
    [InlineData(HexOrientation.PointyTop, 14, -1000)]
    [InlineData(HexOrientation.FlatTop, 6, 1000)]
    [InlineData(HexOrientation.FlatTop, 8, -1000)]
    [InlineData(HexOrientation.FlatTop, 14, 1000)]
    public void TranslatedSmallHexNeighbor_OddHalfAxisUsesActualCoupledLattice(HexOrientation orientation, int radiusRaw, int translation)
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.FromRaw(radiusRaw), Fixed64.One, orientation);
        Vector3d min = new Vector3d(new Fixed64(translation) + Fixed64.FromRaw(7), Fixed64.Zero, new Fixed64(translation) + Fixed64.FromRaw(11));
        VoxelGrid grid = Add(min, min + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(120, 0, 120), metrics),
            metrics: metrics, kind: GridTopologyKind.HexPrism);
        VoxelIndex source = new VoxelIndex(100, 0, 100);
        VoxelIndex target = new VoxelIndex(101, 0, 99);
        GridNavigationBodyTraceReport report = Trace(grid, source, target,
            grid.GetWorldPosition(source).ToVector2d(), grid.GetWorldPosition(target).ToVector2d(), Fixed64.FromRaw(3), Fixed64.FromRaw(1));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, _results.Count);
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == source);
        Assert.Contains(_results, cell => cell.Cell.VoxelIndex == target);
    }

    [Fact]
    public void TwoRawRectangularCells_PreserveOddHalfAxisStrictCoverage()
    {
        Fixed64 edge = Fixed64.FromRaw(2);
        VoxelGrid grid = Add(Vector3d.Zero, new Vector3d(Fixed64.FromRaw(240), Fixed64.Zero, Fixed64.FromRaw(240)),
            metrics: GridTopologyMetrics.Rectangular(edge, Fixed64.One, edge));
        VoxelIndex source = new VoxelIndex(100, 0, 100);
        VoxelIndex target = new VoxelIndex(101, 0, 100);
        GridNavigationBodyTraceReport report = Trace(grid, source, target,
            grid.GetWorldPosition(source).ToVector2d(), grid.GetWorldPosition(target).ToVector2d(), Fixed64.FromRaw(1), Fixed64.FromRaw(1));
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(6, _results.Count);
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(1, 0, 100)]
    [InlineData(1, 100, 0)]
    [InlineData(2, 100, 0)]
    [InlineData(2, 0, 100)]
    public void DistantEligibleLattice_HasNoAddressCandidates(int topology, int x, int z)
    {
        VoxelGrid source = Add(Vector3d.Zero, Vector3d.Zero);
        GridTopologyMetrics metrics = topology == 0 ? GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, Fixed64.One)
            : GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, topology == 1 ? HexOrientation.PointyTop : HexOrientation.FlatTop);
        Vector3d min = new Vector3d(x, 0, z);
        VoxelGrid distant = Add(min, min, metrics: metrics, kind: topology == 0 ? GridTopologyKind.RectangularPrism : GridTopologyKind.HexPrism);
        GridNavigationBodyTraceReport report = GridTracer.TracePlanarNavigationBodyInto(_world, new[] { Layer(source), Layer(distant) }, Fixed64.Zero,
            Address(source, default), Address(source, default), Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.FromFraction(1, 4),
            _results, _scratch, 2, 1, 1, 3);
        Assert.Equal(GridNavigationBodyTraceStatus.Complete, report.Status);
        Assert.Equal(2, report.GridCandidateCount);
        Assert.Equal(1, report.AddressCandidateCount);
        Assert.Equal(Address(source, default), Assert.Single(_results).Cell);
    }
}
