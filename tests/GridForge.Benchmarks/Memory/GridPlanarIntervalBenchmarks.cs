using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids.Topology;

namespace GridForge.Benchmarks;

/// <summary>Exact planar intervals, including crossing, tangent and strict-miss controls.</summary>
[MemoryDiagnoser]
public class GridPlanarIntervalBenchmarks
{
    private GridCellPrism _prism;
    private Vector2d _crossStart;
    private Vector2d _crossEnd;
    private Vector2d _touchStart;
    private Vector2d _touchEnd;
    private Vector2d _missStart;
    private Vector2d _missEnd;

    [Params("Rectangle", "PointyHex", "FlatHex")]
    public string Topology { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        bool rectangle = Topology == "Rectangle";
        bool pointy = Topology == "PointyHex";
        GridTopologyMetrics metrics = rectangle
            ? GridTopologyMetrics.Rectangular(Fixed64.Two)
            : GridTopologyMetrics.Hex(Fixed64.Two, Fixed64.Two,
                pointy ? HexOrientation.PointyTop : HexOrientation.FlatTop);
        if (!GridCellGeometry.TryCreatePrism(
                rectangle ? GridTopologyKind.RectangularPrism : GridTopologyKind.HexPrism,
                metrics, Vector3d.Zero, default, out _prism))
        {
            throw new InvalidOperationException("Planar interval benchmark prism must be representable.");
        }

        _crossStart = pointy ? new Vector2d(0, -4) : new Vector2d(-4, 0);
        _crossEnd = pointy ? new Vector2d(0, 4) : new Vector2d(4, 0);
        _touchStart = rectangle ? new Vector2d(-2, 0)
            : pointy ? new Vector2d(-4, 2) : new Vector2d(2, -4);
        _touchEnd = rectangle ? new Vector2d(0, 2)
            : pointy ? new Vector2d(4, 2) : new Vector2d(2, 4);
        Vector2d missOffset = rectangle || pointy
            ? new Vector2d(Fixed64.Zero, Fixed64.MinIncrement)
            : new Vector2d(Fixed64.MinIncrement, Fixed64.Zero);
        _missStart = _touchStart + missOffset;
        _missEnd = _touchEnd + missOffset;

        Fixed64 enter = rectangle ? Fixed64.FromFraction(3, 8) : Fixed64.Quarter;
        Validate(_crossStart, _crossEnd, true, enter, Fixed64.One - enter);
        Validate(_touchStart, _touchEnd, true, Fixed64.Half, Fixed64.Half);
        Validate(_missStart, _missEnd, false, Fixed64.Zero, Fixed64.Zero);
    }

    [Benchmark]
    public bool Crossing() => GridCellGeometry.TryGetPlanarSegmentInterval(
        _prism, _crossStart, _crossEnd, out _, out _);

    [Benchmark]
    public bool VertexTouch() => GridCellGeometry.TryGetPlanarSegmentInterval(
        _prism, _touchStart, _touchEnd, out _, out _);

    [Benchmark]
    public bool StrictMiss() => GridCellGeometry.TryGetPlanarSegmentInterval(
        _prism, _missStart, _missEnd, out _, out _);

    private void Validate(Vector2d start, Vector2d end, bool expected,
        Fixed64 expectedEnter, Fixed64 expectedExit)
    {
        bool actual = GridCellGeometry.TryGetPlanarSegmentInterval(
            _prism, start, end, out Fixed64 enter, out Fixed64 exit);
        if (actual != expected || enter != expectedEnter || exit != expectedExit)
            throw new InvalidOperationException("Planar interval benchmark changed its exact geometry.");
    }
}
