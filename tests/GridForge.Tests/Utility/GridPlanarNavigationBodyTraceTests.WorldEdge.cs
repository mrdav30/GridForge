//=======================================================================
// GridPlanarNavigationBodyTraceTests.WorldEdge.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Xunit;

namespace GridForge.Grids.Tests;

public sealed partial class GridPlanarNavigationBodyTraceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtremeVirtualNeighbor_IsRejectedWithoutWrapping(bool distinctTarget)
    {
        Fixed64 x = Fixed64.MaxValue - Fixed64.Half;
        VoxelGrid grid = Add(new Vector3d(x, Fixed64.Zero, Fixed64.Zero), new Vector3d(x, Fixed64.Zero, Fixed64.One));
        VoxelIndex target = new VoxelIndex(0, 0, distinctTarget ? 1 : 0);
        Vector2d start = grid.GetWorldPosition(default).ToVector2d();
        Vector2d end = grid.GetWorldPosition(target).ToVector2d();
        GridNavigationBodyTraceReport report = Trace(grid, default, target, start, end, Fixed64.Zero, Fixed64.FromFraction(1, 8));
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }

    [Fact]
    public void UnbisectableFootprint_IsRejectedDuringChargedLayerValidation()
    {
        VoxelGrid grid = Add(Vector3d.Zero, Vector3d.Zero, metrics: GridTopologyMetrics.Rectangular(
            Fixed64.One + Fixed64.FromRaw(1), Fixed64.One, Fixed64.One));
        GridNavigationBodyTraceReport report = Trace(grid, default, default, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero, Fixed64.Half);
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
        Assert.Empty(_results);
    }

    [Fact]
    public void AddressAtUnrepresentableFootprintEdge_FailsWithoutPartialEvidence()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, HexOrientation.PointyTop);
        Vector3d min = new Vector3d(Fixed64.MaxValue - new Fixed64(2), Fixed64.Zero, Fixed64.Zero);
        VoxelGrid grid = Add(min, min + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(1, 0, 0), metrics),
            metrics: metrics, kind: GridTopologyKind.HexPrism);
        Vector2d center = grid.GetWorldPosition(default).ToVector2d();
        GridNavigationBodyTraceReport report = Trace(grid, default, default, center, center, Fixed64.Zero, Fixed64.FromFraction(3, 4));
        Assert.Equal(GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry, report.Status);
        Assert.Equal(2, report.AddressCandidateCount);
        Assert.Empty(_results);
        AssertScratchEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanarDepthExpansionOverflow_IsCheckedAfterEndpointContact(bool upper)
    {
        Fixed64 cellZ = new Fixed64(upper ? int.MaxValue - 1 : int.MinValue + 2);
        Vector3d cellCenter = new Vector3d(Fixed64.Zero, Fixed64.Zero, cellZ);
        VoxelGrid grid = Add(cellCenter, cellCenter,
            metrics: GridTopologyMetrics.Rectangular(Fixed64.One, Fixed64.One, new Fixed64(2)));
        Fixed64 bodyZ = cellZ + (upper ? Fixed64.FromFraction(5, 4) : Fixed64.FromFraction(-5, 4));
        Vector2d center = new Vector2d(Fixed64.Zero, bodyZ);
        GridNavigationBodyTraceReport report = Trace(grid, default, default, center, center, Fixed64.Zero, Fixed64.FromFraction(1, 4));
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
        Assert.Empty(_results);
    }

    [Fact]
    public void PlanarWidthExpansionOverflow_IsCheckedAfterEndpointContact()
    {
        Fixed64 cellX = new Fixed64(int.MaxValue - 1);
        Vector3d cellCenter = new Vector3d(cellX, Fixed64.Zero, Fixed64.Zero);
        VoxelGrid grid = Add(cellCenter, cellCenter,
            metrics: GridTopologyMetrics.Rectangular(new Fixed64(2), Fixed64.One, Fixed64.One));
        Vector2d center = new Vector2d(cellX + Fixed64.FromFraction(5, 4), Fixed64.Zero);
        GridNavigationBodyTraceReport report = Trace(grid, default, default, center, center, Fixed64.Zero, Fixed64.FromFraction(1, 4));
        Assert.Equal(GridNavigationBodyTraceStatus.ArithmeticOverflow, report.Status);
        Assert.Equal(1, report.GridCandidateCount);
        Assert.Equal(0, report.AddressCandidateCount);
        Assert.Empty(_results);
    }
}
