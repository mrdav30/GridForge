//=======================================================================
// GridTopologyMetrics.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using MemoryPack;
using SwiftCollections.Utility;
using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GridForge.Grids.Topology;

/// <summary>
/// Stores deterministic cell geometry for a grid topology.
/// </summary>
[Serializable]
[MemoryPackable]
public readonly partial struct GridTopologyMetrics : IEquatable<GridTopologyMetrics>
{
    /// <summary>
    /// Horizontal radius for hex-prism cells. Rectangular-prism grids leave this as zero.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly Fixed64 CellRadius;

    /// <summary>
    /// Rectangular-prism cell width along world X.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly Fixed64 CellWidth;

    /// <summary>
    /// Rectangular-prism cell height along world Y.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly Fixed64 LayerHeight;

    /// <summary>
    /// Rectangular-prism cell length along world Z.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly Fixed64 CellLength;

    /// <summary>
    /// Hex-prism horizontal projection orientation. Rectangular-prism grids ignore this value.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly HexOrientation HexOrientation;

    /// <summary>
    /// Initializes deterministic topology metrics.
    /// </summary>
    [JsonConstructor]
    public GridTopologyMetrics(
        Fixed64 cellRadius,
        Fixed64 cellWidth,
        Fixed64 layerHeight,
        Fixed64 cellLength,
        HexOrientation hexOrientation = HexOrientation.PointyTop)
    {
        CellRadius = cellRadius;
        CellWidth = cellWidth;
        LayerHeight = layerHeight;
        CellLength = cellLength;
        HexOrientation = hexOrientation;
    }

    internal Fixed64 SmallestRectangularEdge
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Fixed64 smallest = FixedMath.Min(CellWidth, LayerHeight);
            return FixedMath.Min(smallest, CellLength);
        }
    }

    internal Fixed64 LargestRectangularEdge
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Fixed64 largest = FixedMath.Max(CellWidth, LayerHeight);
            return FixedMath.Max(largest, CellLength);
        }
    }

    /// <summary>
    /// Creates cubic rectangular-prism metrics.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GridTopologyMetrics Rectangular(Fixed64 cellSize) =>
         Rectangular(cellSize, cellSize, cellSize);

    /// <summary>
    /// Creates rectangular-prism metrics with independent X, Y, and Z cell edges.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GridTopologyMetrics Rectangular(
        Fixed64 cellWidth,
        Fixed64 layerHeight,
        Fixed64 cellLength) => new(Fixed64.Zero,
            ResolvePositive(cellWidth),
            ResolvePositive(layerHeight),
            ResolvePositive(cellLength),
            HexOrientation.PointyTop);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static GridTopologyMetrics Normalize(
        GridTopologyKind topologyKind,
        GridTopologyMetrics metrics) => topologyKind == GridTopologyKind.RectangularPrism
            ? Rectangular(metrics.CellWidth, metrics.LayerHeight, metrics.CellLength)
            : metrics;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 ResolvePositive(Fixed64 value) =>
        value > Fixed64.Zero ? value : Fixed64.One;

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(CellRadius.GetHashCode(), CellWidth.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, LayerHeight.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, CellLength.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, HexOrientation.GetHashCode());
    }

    /// <inheritdoc/>
    public readonly bool Equals(GridTopologyMetrics other)
    {
        return CellRadius == other.CellRadius
            && CellWidth == other.CellWidth
            && LayerHeight == other.LayerHeight
            && CellLength == other.CellLength
            && HexOrientation == other.HexOrientation;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is GridTopologyMetrics other && Equals(other);

    /// <inheritdoc/>
    public static bool operator ==(GridTopologyMetrics left, GridTopologyMetrics right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(GridTopologyMetrics left, GridTopologyMetrics right) => !left.Equals(right);
}
