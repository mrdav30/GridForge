using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace GridForge.Benchmarks;

/// <summary>Interior sweeps and the portal, clipping and hex fallback controls.</summary>
[MemoryDiagnoser]
public class GridNavigationBodySegmentBenchmarks
{
    private GridCellPrism _rectangle;
    private GridCellPrism _hex;
    private GridNavigationPortal _east;
    private GridNavigationPortal _north;
    private readonly Vector3d _interior = new(0, -2, 0);
    private readonly Vector3d _interiorEnd = new(1, -1, 1);
    private readonly Vector3d _outside = new(-3, -2, 0);

    [GlobalSetup]
    public void Setup()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(new Fixed64(4));
        if (!GridCellGeometry.TryCreatePrism(GridTopologyKind.RectangularPrism,
                metrics, Vector3d.Zero, default, out _rectangle)
            || !GridCellGeometry.TryCreatePrism(GridTopologyKind.RectangularPrism,
                metrics, new Vector3d(4, 0, 0), default, out GridCellPrism east)
            || !GridCellGeometry.TryCreatePrism(GridTopologyKind.RectangularPrism,
                metrics, new Vector3d(0, 0, 4), default, out GridCellPrism north)
            || !GridCellGeometry.TryCreateNavigationPortal(_rectangle, east, out _east)
            || !GridCellGeometry.TryCreateNavigationPortal(_rectangle, north, out _north)
            || !GridCellGeometry.TryCreatePrism(GridTopologyKind.HexPrism,
                GridTopologyMetrics.Hex(Fixed64.Two, new Fixed64(4), HexOrientation.PointyTop),
                Vector3d.Zero, default, out _hex))
            throw new InvalidOperationException("Body segment benchmark geometry must be representable.");

        if (!InteriorAnchor() || !InteriorSweep() || !SelectedPortal()
            || BlockedCorner() || !ClippedEntry() || !HexAnchor())
            throw new InvalidOperationException("Body segment benchmark changed its clearance behavior.");
    }

    [Benchmark]
    public bool InteriorAnchor() => GridCellGeometry.IsNavigationBodyAnchorValid(
        _rectangle, _interior, Fixed64.Half, Fixed64.One, default);

    [Benchmark]
    public bool InteriorSweep() => GridCellGeometry.IsNavigationBodySegmentValid(
        _rectangle, _interior, _interiorEnd, Fixed64.Half, Fixed64.One, default, default,
        GridNavigationBodySegmentEndpointAllowance.None);

    [Benchmark]
    public bool SelectedPortal() => GridCellGeometry.IsNavigationBodyAnchorValid(
        _rectangle, _east.CanonicalFacePoint, Fixed64.Half, Fixed64.One, _east);

    [Benchmark]
    public bool BlockedCorner() => GridCellGeometry.IsNavigationBodySegmentValid(
        _rectangle, _east.CanonicalFacePoint, _north.CanonicalFacePoint,
        Fixed64.One + Fixed64.Half, Fixed64.One, _east, _north,
        GridNavigationBodySegmentEndpointAllowance.None);

    [Benchmark]
    public bool ClippedEntry() => GridCellGeometry.IsNavigationBodySegmentValid(
        _rectangle, _outside, _interior, Fixed64.Half, Fixed64.One, default, default,
        GridNavigationBodySegmentEndpointAllowance.StartFootprintEdge);

    [Benchmark]
    public bool HexAnchor() => GridCellGeometry.IsNavigationBodyAnchorValid(
        _hex, _interior, Fixed64.Half, Fixed64.One, default);
}
