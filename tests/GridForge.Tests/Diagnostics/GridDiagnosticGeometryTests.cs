using FixedMathSharp;
using GridForge.Diagnostics;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using System;
using Xunit;

namespace GridForge.Diagnostics.Tests;

[Collection("GridForgeCollection")]
public class GridDiagnosticGeometryTests
{
    [Fact]
    public void Counts_ShouldMatchTopologyPrisms()
    {
        Assert.Equal(8, GridDiagnosticGeometry.RectangularPrismVertexCount);
        Assert.Equal(12, GridDiagnosticGeometry.HexPrismVertexCount);
        Assert.Equal(12, GridDiagnosticGeometry.RectangularPrismEdgeCount);
        Assert.Equal(18, GridDiagnosticGeometry.HexPrismEdgeCount);

        Assert.Equal(8, GridDiagnosticGeometry.GetVertexCount(GridTopologyKind.RectangularPrism));
        Assert.Equal(12, GridDiagnosticGeometry.GetVertexCount(GridTopologyKind.HexPrism));
        Assert.Equal(12, GridDiagnosticGeometry.GetEdgeCount(GridTopologyKind.RectangularPrism));
        Assert.Equal(18, GridDiagnosticGeometry.GetEdgeCount(GridTopologyKind.HexPrism));
    }

    [Fact]
    public void WriteVertices_ShouldUseRectangularMetrics()
    {
        GridDiagnosticCell cell = CreateCell(
            GridDiagnosticCellKind.Physical,
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(new Fixed64(2), new Fixed64(4), new Fixed64(6)),
            new Vector3d(10, 20, 30));
        Span<Vector3d> vertices = stackalloc Vector3d[GridDiagnosticGeometry.RectangularPrismVertexCount];

        int written = GridDiagnosticGeometry.WriteVertices(cell, vertices);

        Assert.Equal(8, written);
        Assert.Equal(new Vector3d(9, 18, 27), vertices[0]);
        Assert.Equal(new Vector3d(11, 18, 27), vertices[1]);
        Assert.Equal(new Vector3d(11, 18, 33), vertices[2]);
        Assert.Equal(new Vector3d(9, 18, 33), vertices[3]);
        Assert.Equal(new Vector3d(9, 22, 27), vertices[4]);
        Assert.Equal(new Vector3d(11, 22, 27), vertices[5]);
        Assert.Equal(new Vector3d(11, 22, 33), vertices[6]);
        Assert.Equal(new Vector3d(9, 22, 33), vertices[7]);
    }

    [Fact]
    public void WriteVertices_ShouldUsePointyTopHexMetrics()
    {
        Fixed64 radius = new Fixed64(2);
        GridDiagnosticCell cell = CreateCell(
            GridDiagnosticCellKind.Physical,
            GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(radius, new Fixed64(6), HexOrientation.PointyTop),
            new Vector3d(5, 10, -3));
        Fixed64 halfHexWidth = HexCoordinateUtility.Sqrt3 * radius * Fixed64.Half;
        Fixed64 halfRadius = radius * Fixed64.Half;
        Span<Vector3d> vertices = stackalloc Vector3d[GridDiagnosticGeometry.HexPrismVertexCount];

        int written = GridDiagnosticGeometry.WriteVertices(cell, vertices);

        Assert.Equal(12, written);
        Assert.Equal(V(5 + halfHexWidth, 7, -3 + halfRadius), vertices[0]);
        Assert.Equal(new Vector3d(5, 7, -1), vertices[1]);
        Assert.Equal(V(5 - halfHexWidth, 7, -3 + halfRadius), vertices[2]);
        Assert.Equal(V(5 - halfHexWidth, 7, -3 - halfRadius), vertices[3]);
        Assert.Equal(new Vector3d(5, 7, -5), vertices[4]);
        Assert.Equal(V(5 + halfHexWidth, 7, -3 - halfRadius), vertices[5]);
        Assert.Equal(V(5 + halfHexWidth, 13, -3 + halfRadius), vertices[6]);
        Assert.Equal(new Vector3d(5, 13, -1), vertices[7]);
        Assert.Equal(new Vector3d(5, 13, -5), vertices[10]);
    }

    [Fact]
    public void WriteVertices_ShouldUseFlatTopHexMetrics()
    {
        Fixed64 radius = new Fixed64(2);
        GridDiagnosticCell cell = CreateCell(
            GridDiagnosticCellKind.Physical,
            GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(radius, new Fixed64(6), HexOrientation.FlatTop),
            new Vector3d(-4, 3, 8));
        Fixed64 halfHexWidth = HexCoordinateUtility.Sqrt3 * radius * Fixed64.Half;
        Fixed64 halfRadius = radius * Fixed64.Half;
        Span<Vector3d> vertices = stackalloc Vector3d[GridDiagnosticGeometry.HexPrismVertexCount];

        int written = GridDiagnosticGeometry.WriteVertices(cell, vertices);

        Assert.Equal(12, written);
        Assert.Equal(new Vector3d(-2, 0, 8), vertices[0]);
        Assert.Equal(V(-4 + halfRadius, 0, 8 + halfHexWidth), vertices[1]);
        Assert.Equal(V(-4 - halfRadius, 0, 8 + halfHexWidth), vertices[2]);
        Assert.Equal(new Vector3d(-6, 0, 8), vertices[3]);
        Assert.Equal(V(-4 - halfRadius, 0, 8 - halfHexWidth), vertices[4]);
        Assert.Equal(V(-4 + halfRadius, 0, 8 - halfHexWidth), vertices[5]);
        Assert.Equal(new Vector3d(-2, 6, 8), vertices[6]);
        Assert.Equal(V(-4 + halfRadius, 6, 8 + halfHexWidth), vertices[7]);
        Assert.Equal(V(-4 + halfRadius, 6, 8 - halfHexWidth), vertices[11]);
    }

    [Fact]
    public void WriteVertices_ShouldReturnZeroWhenSpanIsTooSmall()
    {
        GridDiagnosticCell rectangularCell = CreateCell(
            GridDiagnosticCellKind.Physical,
            GridTopologyKind.RectangularPrism,
            GridTopologyMetrics.Rectangular(Fixed64.One),
            Vector3d.Zero);
        GridDiagnosticCell hexCell = CreateCell(
            GridDiagnosticCellKind.Physical,
            GridTopologyKind.HexPrism,
            GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One),
            Vector3d.Zero);
        Span<Vector3d> rectangularVertices = stackalloc Vector3d[GridDiagnosticGeometry.RectangularPrismVertexCount - 1];
        Span<Vector3d> hexVertices = stackalloc Vector3d[GridDiagnosticGeometry.HexPrismVertexCount - 1];

        Assert.Equal(0, GridDiagnosticGeometry.WriteVertices(rectangularCell, rectangularVertices));
        Assert.Equal(0, GridDiagnosticGeometry.WriteVertices(hexCell, hexVertices));
    }

    [Fact]
    public void GetEdges_ShouldReturnStableReadOnlyEdgeTopology()
    {
        ReadOnlySpan<GridDiagnosticEdge> rectangular = GridDiagnosticGeometry.GetEdges(GridTopologyKind.RectangularPrism);
        ReadOnlySpan<GridDiagnosticEdge> rectangularAgain = GridDiagnosticGeometry.GetEdges(GridTopologyKind.RectangularPrism);
        ReadOnlySpan<GridDiagnosticEdge> hex = GridDiagnosticGeometry.GetEdges(GridTopologyKind.HexPrism);

        Assert.Equal(12, rectangular.Length);
        Assert.Equal(18, hex.Length);
        AssertEdge(new GridDiagnosticEdge(0, 1), rectangular[0]);
        AssertEdge(new GridDiagnosticEdge(7, 4), rectangular[7]);
        AssertEdge(new GridDiagnosticEdge(3, 7), rectangular[11]);
        AssertEdge(new GridDiagnosticEdge(0, 1), hex[0]);
        AssertEdge(new GridDiagnosticEdge(11, 6), hex[11]);
        AssertEdge(new GridDiagnosticEdge(5, 11), hex[17]);
        AssertSameEdges(rectangular, rectangularAgain);
    }

    [Fact]
    public void WriteVertices_ShouldUseSameGeometryForMissingSparseAddressCells()
    {
        GridTopologyMetrics metrics = GridTopologyMetrics.Hex(Fixed64.One, new Fixed64(2), HexOrientation.FlatTop);
        Vector3d center = new Vector3d(6, 4, 2);
        GridDiagnosticCell physicalCell = CreateCell(
            GridDiagnosticCellKind.Physical,
            GridTopologyKind.HexPrism,
            metrics,
            center);
        GridDiagnosticCell missingCell = CreateCell(
            GridDiagnosticCellKind.MissingSparseAddress,
            GridTopologyKind.HexPrism,
            metrics,
            center);
        Span<Vector3d> physicalVertices = stackalloc Vector3d[GridDiagnosticGeometry.HexPrismVertexCount];
        Span<Vector3d> missingVertices = stackalloc Vector3d[GridDiagnosticGeometry.HexPrismVertexCount];

        int physicalWritten = GridDiagnosticGeometry.WriteVertices(physicalCell, physicalVertices);
        int missingWritten = GridDiagnosticGeometry.WriteVertices(missingCell, missingVertices);

        Assert.Equal(physicalWritten, missingWritten);
        for (int i = 0; i < physicalWritten; i++)
            Assert.Equal(physicalVertices[i], missingVertices[i]);
    }

    private static GridDiagnosticCell CreateCell(
        GridDiagnosticCellKind kind,
        GridTopologyKind topologyKind,
        GridTopologyMetrics metrics,
        Vector3d worldPosition)
    {
        VoxelIndex index = new VoxelIndex(1, 2, 3);
        WorldVoxelIndex worldIndex = new WorldVoxelIndex(11, 2, 17, index);
        GridDiagnosticCellState state = kind == GridDiagnosticCellKind.MissingSparseAddress
            ? GridDiagnosticCellState.MissingSparseAddress
            : GridDiagnosticCellState.Empty;

        return new GridDiagnosticCell(
            kind,
            worldIndex.WorldSpawnToken,
            worldIndex.GridIndex,
            worldIndex.GridSpawnToken,
            index,
            worldPosition,
            topologyKind,
            kind == GridDiagnosticCellKind.MissingSparseAddress ? GridStorageKind.Sparse : GridStorageKind.Dense,
            metrics,
            state,
            worldIndex);
    }

    private static Vector3d V(Fixed64 x, Fixed64 y, Fixed64 z) => new Vector3d(x, y, z);

    private static Vector3d V(Fixed64 x, int y, Fixed64 z) => new Vector3d(x, new Fixed64(y), z);

    private static void AssertEdge(GridDiagnosticEdge expected, GridDiagnosticEdge actual)
    {
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.End, actual.End);
    }

    private static void AssertSameEdges(
        ReadOnlySpan<GridDiagnosticEdge> expected,
        ReadOnlySpan<GridDiagnosticEdge> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
            AssertEdge(expected[i], actual[i]);
    }
}
