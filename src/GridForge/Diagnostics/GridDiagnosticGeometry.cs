//=======================================================================
// GridDiagnosticGeometry.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;

namespace GridForge.Diagnostics;

/// <summary>
/// Topology-aware diagnostic geometry helpers for cell descriptors.
/// </summary>
public static class GridDiagnosticGeometry
{
    /// <summary>
    /// Number of vertices in a rectangular-prism diagnostic cell.
    /// </summary>
    public const int RectangularPrismVertexCount = 8;

    /// <summary>
    /// Number of vertices in a hex-prism diagnostic cell.
    /// </summary>
    public const int HexPrismVertexCount = 12;

    /// <summary>
    /// Number of wireframe edges in a rectangular-prism diagnostic cell.
    /// </summary>
    public const int RectangularPrismEdgeCount = 12;

    /// <summary>
    /// Number of wireframe edges in a hex-prism diagnostic cell.
    /// </summary>
    public const int HexPrismEdgeCount = 18;

    private static readonly GridDiagnosticEdge[] RectangularPrismEdges =
    {
        new GridDiagnosticEdge(0, 1),
        new GridDiagnosticEdge(1, 2),
        new GridDiagnosticEdge(2, 3),
        new GridDiagnosticEdge(3, 0),
        new GridDiagnosticEdge(4, 5),
        new GridDiagnosticEdge(5, 6),
        new GridDiagnosticEdge(6, 7),
        new GridDiagnosticEdge(7, 4),
        new GridDiagnosticEdge(0, 4),
        new GridDiagnosticEdge(1, 5),
        new GridDiagnosticEdge(2, 6),
        new GridDiagnosticEdge(3, 7)
    };

    private static readonly GridDiagnosticEdge[] HexPrismEdges =
    {
        new GridDiagnosticEdge(0, 1),
        new GridDiagnosticEdge(1, 2),
        new GridDiagnosticEdge(2, 3),
        new GridDiagnosticEdge(3, 4),
        new GridDiagnosticEdge(4, 5),
        new GridDiagnosticEdge(5, 0),
        new GridDiagnosticEdge(6, 7),
        new GridDiagnosticEdge(7, 8),
        new GridDiagnosticEdge(8, 9),
        new GridDiagnosticEdge(9, 10),
        new GridDiagnosticEdge(10, 11),
        new GridDiagnosticEdge(11, 6),
        new GridDiagnosticEdge(0, 6),
        new GridDiagnosticEdge(1, 7),
        new GridDiagnosticEdge(2, 8),
        new GridDiagnosticEdge(3, 9),
        new GridDiagnosticEdge(4, 10),
        new GridDiagnosticEdge(5, 11)
    };

    /// <summary>
    /// Gets the number of vertices written for the supplied topology kind.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetVertexCount(GridTopologyKind topologyKind) => topologyKind switch
    {
        GridTopologyKind.RectangularPrism => RectangularPrismVertexCount,
        GridTopologyKind.HexPrism => HexPrismVertexCount,
        _ => 0
    };

    /// <summary>
    /// Gets the number of edges exposed for the supplied topology kind.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetEdgeCount(GridTopologyKind topologyKind) => topologyKind switch
    {
        GridTopologyKind.RectangularPrism => RectangularPrismEdgeCount,
        GridTopologyKind.HexPrism => HexPrismEdgeCount,
        _ => 0
    };

    /// <summary>
    /// Writes topology-aware world-space cell vertices into caller-owned
    /// storage.
    /// </summary>
    /// <returns>The number of vertices written, or <c>0</c> when the span is too small.</returns>
    public static int WriteVertices(
        in GridDiagnosticCell cell,
        Span<Vector3d> vertices) => cell.TopologyKind switch
        {
            GridTopologyKind.RectangularPrism => WriteRectangularVertices(cell.WorldPosition, cell.TopologyMetrics, vertices),
            GridTopologyKind.HexPrism => WriteHexVertices(cell.WorldPosition, cell.TopologyMetrics, vertices),
            _ => 0
        };

    /// <summary>
    /// Gets immutable edge topology for the supplied cell topology kind.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<GridDiagnosticEdge> GetEdges(GridTopologyKind topologyKind) => topologyKind switch
    {
        GridTopologyKind.RectangularPrism => RectangularPrismEdges,
        GridTopologyKind.HexPrism => HexPrismEdges,
        _ => ReadOnlySpan<GridDiagnosticEdge>.Empty
    };

    private static int WriteRectangularVertices(
        Vector3d center,
        GridTopologyMetrics metrics,
        Span<Vector3d> vertices)
    {
        if (vertices.Length < RectangularPrismVertexCount)
            return 0;

        Fixed64 halfX = metrics.CellWidth * Fixed64.Half;
        Fixed64 halfY = metrics.LayerHeight * Fixed64.Half;
        Fixed64 halfZ = metrics.CellLength * Fixed64.Half;
        Fixed64 minX = center.X - halfX;
        Fixed64 maxX = center.X + halfX;
        Fixed64 minY = center.Y - halfY;
        Fixed64 maxY = center.Y + halfY;
        Fixed64 minZ = center.Z - halfZ;
        Fixed64 maxZ = center.Z + halfZ;

        vertices[0] = new Vector3d(minX, minY, minZ);
        vertices[1] = new Vector3d(maxX, minY, minZ);
        vertices[2] = new Vector3d(maxX, minY, maxZ);
        vertices[3] = new Vector3d(minX, minY, maxZ);
        vertices[4] = new Vector3d(minX, maxY, minZ);
        vertices[5] = new Vector3d(maxX, maxY, minZ);
        vertices[6] = new Vector3d(maxX, maxY, maxZ);
        vertices[7] = new Vector3d(minX, maxY, maxZ);

        return RectangularPrismVertexCount;
    }

    private static int WriteHexVertices(
        Vector3d center,
        GridTopologyMetrics metrics,
        Span<Vector3d> vertices)
    {
        if (vertices.Length < HexPrismVertexCount)
            return 0;

        Fixed64 halfY = metrics.LayerHeight * Fixed64.Half;
        Fixed64 bottomY = center.Y - halfY;
        Fixed64 topY = center.Y + halfY;

        if (metrics.HexOrientation == HexOrientation.FlatTop)
        {
            WriteFlatTopHexRing(center.X, bottomY, center.Z, metrics.CellRadius, vertices);
            WriteFlatTopHexRing(center.X, topY, center.Z, metrics.CellRadius, vertices.Slice(6));
            return HexPrismVertexCount;
        }

        WritePointyTopHexRing(center.X, bottomY, center.Z, metrics.CellRadius, vertices);
        WritePointyTopHexRing(center.X, topY, center.Z, metrics.CellRadius, vertices.Slice(6));
        return HexPrismVertexCount;
    }

    private static void WritePointyTopHexRing(
        Fixed64 centerX,
        Fixed64 y,
        Fixed64 centerZ,
        Fixed64 radius,
        Span<Vector3d> vertices)
    {
        Fixed64 halfRadius = radius * Fixed64.Half;
        Fixed64 halfWidth = HexCoordinateUtility.Sqrt3 * radius * Fixed64.Half;

        vertices[0] = new Vector3d(centerX + halfWidth, y, centerZ + halfRadius);
        vertices[1] = new Vector3d(centerX, y, centerZ + radius);
        vertices[2] = new Vector3d(centerX - halfWidth, y, centerZ + halfRadius);
        vertices[3] = new Vector3d(centerX - halfWidth, y, centerZ - halfRadius);
        vertices[4] = new Vector3d(centerX, y, centerZ - radius);
        vertices[5] = new Vector3d(centerX + halfWidth, y, centerZ - halfRadius);
    }

    private static void WriteFlatTopHexRing(
        Fixed64 centerX,
        Fixed64 y,
        Fixed64 centerZ,
        Fixed64 radius,
        Span<Vector3d> vertices)
    {
        Fixed64 halfRadius = radius * Fixed64.Half;
        Fixed64 halfWidth = HexCoordinateUtility.Sqrt3 * radius * Fixed64.Half;

        vertices[0] = new Vector3d(centerX + radius, y, centerZ);
        vertices[1] = new Vector3d(centerX + halfRadius, y, centerZ + halfWidth);
        vertices[2] = new Vector3d(centerX - halfRadius, y, centerZ + halfWidth);
        vertices[3] = new Vector3d(centerX - radius, y, centerZ);
        vertices[4] = new Vector3d(centerX - halfRadius, y, centerZ - halfWidth);
        vertices[5] = new Vector3d(centerX + halfRadius, y, centerZ - halfWidth);
    }
}
