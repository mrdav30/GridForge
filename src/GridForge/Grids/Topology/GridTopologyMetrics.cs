//=======================================================================
// GridTopologyMetrics.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using FixedMathSharp;
using MemoryPack;
using SwiftCollections.Utility;

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
    /// <param name="cellRadius">Horizontal center-to-corner radius for hex-prism cells. Rectangular-prism grids leave this as zero.</param>
    /// <param name="cellWidth">Rectangular-prism cell width along world X.</param>
    /// <param name="layerHeight">Vertical layer height along world Y for rectangular-prism and hex-prism grids.</param>
    /// <param name="cellLength">Rectangular-prism cell length along world Z.</param>
    /// <param name="hexOrientation">Hex-prism horizontal projection orientation. Rectangular-prism grids ignore this value. The orientation affects fixed-point world XZ projection only; it is not an engine rendering setting.</param>
    /// <remarks>This constructor/factory stores values without validation. Configuration normalization validates topology-specific dimensions; zero/default inputs are not proof of usable exact geometry.</remarks>
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
    /// <param name="cellSize">The edge length in world units. Nonpositive values become one.</param>
    /// <returns>Rectangular metrics with positive edges; nonpositive supplied edges become one world unit.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static GridTopologyMetrics Rectangular(Fixed64 cellSize) =>
         Rectangular(cellSize, cellSize, cellSize);

    /// <summary>
    /// Creates rectangular-prism metrics with independent X, Y, and Z cell edges.
    /// </summary>
    /// <param name="cellWidth">The rectangular X edge in world units. The rectangular factory replaces nonpositive values with one.</param>
    /// <param name="layerHeight">The vertical Y edge in world units.</param>
    /// <param name="cellLength">The rectangular Z edge in world units. The rectangular factory replaces nonpositive values with one.</param>
    /// <returns>Rectangular metrics with positive edges; nonpositive supplied edges become one world unit.</returns>
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
    /// <param name="cellRadius">The hex center-to-vertex radius in world units.</param>
    /// <param name="layerHeight">The vertical Y edge in world units.</param>
    /// <param name="hexOrientation">The hex orientation; defaults to pointy-top.</param>
    /// <returns>Unnormalized hex metrics storing the supplied radius, layer height, and orientation. Invalid values are rejected when the configuration is normalized.</returns>
    /// <remarks>This constructor/factory stores values without validation. Configuration normalization validates topology-specific dimensions; zero/default inputs are not proof of usable exact geometry.</remarks>
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

    /// <summary>Computes a hash of the equality components.</summary>
    /// <returns>A hash for lookup; collisions are possible and equality remains authoritative.</returns>
    public override readonly int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(CellRadius.GetHashCode(), CellWidth.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, LayerHeight.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, CellLength.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, HexOrientation.GetHashCode());
    }

    /// <summary>Tests whether the supplied value has the same equality components.</summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public readonly bool Equals(GridTopologyMetrics other)
    {
        return CellRadius == other.CellRadius
            && CellWidth == other.CellWidth
            && LayerHeight == other.LayerHeight
            && CellLength == other.CellLength
            && HexOrientation == other.HexOrientation;
    }

    /// <summary>Tests whether the supplied value has the same equality components.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public override readonly bool Equals(object? obj) => obj is GridTopologyMetrics other && Equals(other);

    /// <summary>Tests whether two values are equal.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>True when all equality components match; otherwise false.</returns>
    public static bool operator ==(GridTopologyMetrics left, GridTopologyMetrics right) => left.Equals(right);

    /// <summary>Tests whether two values differ.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns>True when any equality component differs; otherwise false.</returns>
    public static bool operator !=(GridTopologyMetrics left, GridTopologyMetrics right) => !left.Equals(right);
}
