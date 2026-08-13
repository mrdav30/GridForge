//=======================================================================
// GridConvexPolygon2d.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

/// <summary>
/// Stores a boundary-ordered convex polygon produced by exact grid-cell contact clipping.
/// </summary>
public readonly struct GridConvexPolygon2d
{
    /// <summary>
    /// Maximum vertices in the intersection of two rectangular or hexagonal cell footprints.
    /// </summary>
    public const int MaxVertexCount = 12;

    private readonly Vector2d _vertex0;
    private readonly Vector2d _vertex1;
    private readonly Vector2d _vertex2;
    private readonly Vector2d _vertex3;
    private readonly Vector2d _vertex4;
    private readonly Vector2d _vertex5;
    private readonly Vector2d _vertex6;
    private readonly Vector2d _vertex7;
    private readonly Vector2d _vertex8;
    private readonly Vector2d _vertex9;
    private readonly Vector2d _vertex10;
    private readonly Vector2d _vertex11;

    /// <summary>
    /// Number of boundary-ordered vertices in this polygon.
    /// </summary>
    public int VertexCount { get; }

    internal GridConvexPolygon2d(ReadOnlySpan<Vector2d> vertices)
    {
        if (vertices.Length > MaxVertexCount)
            throw new ArgumentOutOfRangeException(nameof(vertices));

        VertexCount = vertices.Length;
        _vertex0 = GetOrDefault(vertices, 0);
        _vertex1 = GetOrDefault(vertices, 1);
        _vertex2 = GetOrDefault(vertices, 2);
        _vertex3 = GetOrDefault(vertices, 3);
        _vertex4 = GetOrDefault(vertices, 4);
        _vertex5 = GetOrDefault(vertices, 5);
        _vertex6 = GetOrDefault(vertices, 6);
        _vertex7 = GetOrDefault(vertices, 7);
        _vertex8 = GetOrDefault(vertices, 8);
        _vertex9 = GetOrDefault(vertices, 9);
        _vertex10 = GetOrDefault(vertices, 10);
        _vertex11 = GetOrDefault(vertices, 11);
    }

    /// <summary>
    /// Gets one boundary-ordered polygon vertex.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2d GetVertex(int index) => index switch
    {
        0 when VertexCount > 0 => _vertex0,
        1 when VertexCount > 1 => _vertex1,
        2 when VertexCount > 2 => _vertex2,
        3 when VertexCount > 3 => _vertex3,
        4 when VertexCount > 4 => _vertex4,
        5 when VertexCount > 5 => _vertex5,
        6 when VertexCount > 6 => _vertex6,
        7 when VertexCount > 7 => _vertex7,
        8 when VertexCount > 8 => _vertex8,
        9 when VertexCount > 9 => _vertex9,
        10 when VertexCount > 10 => _vertex10,
        11 when VertexCount > 11 => _vertex11,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>
    /// Copies the boundary-ordered vertices into caller-owned storage.
    /// </summary>
    public void CopyTo(Span<Vector2d> destination)
    {
        if (destination.Length < VertexCount)
            throw new ArgumentException("The destination is smaller than the polygon.", nameof(destination));

        for (int i = 0; i < VertexCount; i++)
            destination[i] = GetVertex(i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2d GetOrDefault(ReadOnlySpan<Vector2d> vertices, int index) =>
        index < vertices.Length ? vertices[index] : default;
}
