//=======================================================================
// GridCellPrism.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Spatial;

namespace GridForge.Grids.Topology;

/// <summary>
/// Describes one exact topology cell as an ordered convex XZ footprint and a closed vertical interval.
/// </summary>
/// <remarks>
/// Construction fails when a metric cannot be bisected exactly in the fixed-point scalar domain.
/// </remarks>
public readonly struct GridCellPrism
{
    private readonly Vector2d _vertex0;
    private readonly Vector2d _vertex1;
    private readonly Vector2d _vertex2;
    private readonly Vector2d _vertex3;
    private readonly Vector2d _vertex4;
    private readonly Vector2d _vertex5;

    /// <summary>
    /// The exact runtime cell identity represented by this prism.
    /// </summary>
    public WorldVoxelIndex Cell { get; }

    /// <summary>
    /// The topology that produced the footprint.
    /// </summary>
    public GridTopologyKind TopologyKind { get; }

    /// <summary>
    /// The world-space cell center.
    /// </summary>
    public Vector3d Center { get; }

    /// <summary>
    /// The inclusive lower Y bound.
    /// </summary>
    public Fixed64 VerticalMin { get; }

    /// <summary>
    /// The inclusive upper Y bound.
    /// </summary>
    public Fixed64 VerticalMax { get; }

    /// <summary>
    /// The largest radius that can be inset from every horizontal footprint edge.
    /// </summary>
    public Fixed64 PlanarInradius { get; }

    /// <summary>
    /// The number of boundary-ordered footprint vertices.
    /// </summary>
    public int FootprintVertexCount { get; }

    internal GridCellPrism(
        WorldVoxelIndex cell,
        GridTopologyKind topologyKind,
        Vector3d center,
        Fixed64 verticalMin,
        Fixed64 verticalMax,
        Fixed64 planarInradius,
        ReadOnlySpan<Vector2d> footprint)
    {
        if (footprint.Length != 4 && footprint.Length != 6)
            throw new ArgumentOutOfRangeException(nameof(footprint));

        Cell = cell;
        TopologyKind = topologyKind;
        Center = center;
        VerticalMin = verticalMin;
        VerticalMax = verticalMax;
        PlanarInradius = planarInradius;
        FootprintVertexCount = footprint.Length;
        _vertex0 = footprint[0];
        _vertex1 = footprint[1];
        _vertex2 = footprint[2];
        _vertex3 = footprint[3];
        _vertex4 = footprint.Length > 4 ? footprint[4] : default;
        _vertex5 = footprint.Length > 5 ? footprint[5] : default;
    }

    /// <summary>
    /// Gets one boundary-ordered XZ footprint vertex.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2d GetFootprintVertex(int index) => index switch
    {
        0 when FootprintVertexCount > 0 => _vertex0,
        1 when FootprintVertexCount > 1 => _vertex1,
        2 when FootprintVertexCount > 2 => _vertex2,
        3 when FootprintVertexCount > 3 => _vertex3,
        4 when FootprintVertexCount > 4 => _vertex4,
        5 when FootprintVertexCount > 5 => _vertex5,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>
    /// Copies the boundary-ordered XZ footprint into caller-owned storage.
    /// </summary>
    public void CopyFootprintTo(Span<Vector2d> destination)
    {
        if (destination.Length < FootprintVertexCount)
            throw new ArgumentException("The destination is smaller than the footprint.", nameof(destination));

        for (int i = 0; i < FootprintVertexCount; i++)
            destination[i] = GetFootprintVertex(i);
    }

    /// <summary>
    /// Determines whether a world-space point lies inside or on this closed prism.
    /// </summary>
    public bool Contains(Vector3d point)
    {
        if (FootprintVertexCount is not 4 and not 6
            || point.Y < VerticalMin
            || point.Y > VerticalMax)
            return false;

        Span<Vector2d> offsets = stackalloc Vector2d[6];
        Vector2d origin = new(Center.X, Center.Z);
        for (int i = 0; i < FootprintVertexCount; i++)
            offsets[i] = GetFootprintVertex(i) - origin;

        return FixedConvex2dRelations.ContainsPoint(
            new Vector2d(point.X, point.Z),
            origin,
            offsets[..FootprintVertexCount]);
    }

    internal TopologyVoxelAabb GetAabb()
    {
        Vector2d first = _vertex0;
        Fixed64 minX = first.X;
        Fixed64 maxX = first.X;
        Fixed64 minZ = first.Y;
        Fixed64 maxZ = first.Y;

        for (int i = 1; i < FootprintVertexCount; i++)
        {
            Vector2d vertex = GetFootprintVertex(i);
            minX = FixedMath.Min(minX, vertex.X);
            maxX = FixedMath.Max(maxX, vertex.X);
            minZ = FixedMath.Min(minZ, vertex.Y);
            maxZ = FixedMath.Max(maxZ, vertex.Y);
        }

        return new TopologyVoxelAabb(
            new Vector3d(minX, VerticalMin, minZ),
            new Vector3d(maxX, VerticalMax, maxZ));
    }
}
