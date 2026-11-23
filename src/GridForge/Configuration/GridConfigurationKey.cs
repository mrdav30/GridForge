//=======================================================================
// GridConfigurationKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids.Topology;
using SwiftCollections.Utility;

namespace GridForge.Configuration;

/// <summary>
/// Exact identity key for a grid's snapped bounds and topology.
/// </summary>
public readonly struct GridConfigurationKey : IEquatable<GridConfigurationKey>
{
    /// <summary>
    /// The minimum snapped bounds of the grid.
    /// </summary>
    public readonly Vector3d BoundsMin;

    /// <summary>
    /// The maximum snapped bounds of the grid.
    /// </summary>
    public readonly Vector3d BoundsMax;

    /// <summary>
    /// The grid topology used by these bounds.
    /// </summary>
    public readonly GridTopologyKind TopologyKind;

    /// <summary>
    /// The topology metrics used by these bounds.
    /// </summary>
    public readonly GridTopologyMetrics TopologyMetrics;

    /// <summary>
    /// Initializes a new grid configuration key.
    /// </summary>
    /// <param name="boundsMin">The minimum world-space corner, copied without sorting or snapping.</param>
    /// <param name="boundsMax">The maximum world-space corner, copied without sorting or snapping.</param>
    /// <param name="topologyKind">The topology discriminator stored in the key.</param>
    /// <param name="topologyMetrics">The exact fixed-point geometry stored in the key; no normalization is performed.</param>
    public GridConfigurationKey(
        Vector3d boundsMin,
        Vector3d boundsMax,
        GridTopologyKind topologyKind,
        GridTopologyMetrics topologyMetrics)
    {
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        TopologyKind = topologyKind;
        TopologyMetrics = topologyMetrics;
    }

    /// <summary>Computes a hash of the value components used by equality.</summary>
    /// <returns>A hash for lookup; distinct values can share a hash.</returns>
    public override readonly int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(BoundsMin.GetHashCode(), BoundsMax.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, TopologyKind.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, TopologyMetrics.GetHashCode());
    }

    /// <summary>Tests whether another value has the same identity components.</summary>
    /// <param name="other">The value to compare; default values are compared by their stored components.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public readonly bool Equals(GridConfigurationKey other)
    {
        return BoundsMin == other.BoundsMin
            && BoundsMax == other.BoundsMax
            && TopologyKind == other.TopologyKind
            && TopologyMetrics == other.TopologyMetrics;
    }

    /// <summary>Tests whether an object has the same type and value.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns><see langword="true"/> when all identity components match; otherwise <see langword="false"/>.</returns>
    public override readonly bool Equals(object? obj) => obj is GridConfigurationKey other && Equals(other);

    /// <summary>Compares two values for equality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are equal; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(GridConfigurationKey left, GridConfigurationKey right) => left.Equals(right);

    /// <summary>Compares two values for inequality.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> when the values are different; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(GridConfigurationKey left, GridConfigurationKey right) => !left.Equals(right);
}
