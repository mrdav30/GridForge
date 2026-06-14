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
    /// Horizontal center-to-corner radius for hex-prism cells. Rectangular-prism grids leave this as zero.
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
    /// Vertical layer height along world Y for rectangular-prism and hex-prism grids.
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
    /// The orientation affects fixed-point world XZ projection only; it is not an engine rendering setting.
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

    internal Fixed64 LargestHexEdge
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            Fixed64 horizontalDiameter = CellRadius * new Fixed64(2);
            return FixedMath.Max(horizontalDiameter, LayerHeight);
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

    /// <summary>
    /// Creates hex-prism metrics with the supplied horizontal radius, vertical layer height, and orientation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GridTopologyMetrics Hex(
        Fixed64 cellRadius,
        Fixed64 layerHeight,
        HexOrientation hexOrientation = HexOrientation.PointyTop) => new(
            cellRadius,
            Fixed64.Zero,
            layerHeight,
            Fixed64.Zero,
            hexOrientation);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static GridTopologyMetrics Normalize(
        GridTopologyKind topologyKind,
        GridTopologyMetrics metrics) => topologyKind == GridTopologyKind.RectangularPrism
            ? Rectangular(metrics.CellWidth, metrics.LayerHeight, metrics.CellLength)
            : Hex(metrics.CellRadius, metrics.LayerHeight, metrics.HexOrientation);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsValid(
        GridTopologyKind topologyKind,
        GridTopologyMetrics metrics) => topologyKind switch
        {
            GridTopologyKind.RectangularPrism => metrics.CellWidth > Fixed64.Zero
                && metrics.LayerHeight > Fixed64.Zero
                && metrics.CellLength > Fixed64.Zero,
            GridTopologyKind.HexPrism => metrics.CellRadius > Fixed64.Zero
                && metrics.LayerHeight > Fixed64.Zero,
            _ => false
        };

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
