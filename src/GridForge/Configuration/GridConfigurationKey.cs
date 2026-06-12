//=======================================================================
// GridConfigurationKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using SwiftCollections.Utility;
using System;

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

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(BoundsMin.GetHashCode(), BoundsMax.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, TopologyKind.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, TopologyMetrics.GetHashCode());
    }

    /// <inheritdoc/>
    public readonly bool Equals(GridConfigurationKey other)
    {
        return BoundsMin == other.BoundsMin
            && BoundsMax == other.BoundsMax
            && TopologyKind == other.TopologyKind
            && TopologyMetrics == other.TopologyMetrics;
    }

    /// <inheritdoc/>
    public override readonly bool Equals(object? obj) => obj is GridConfigurationKey other && Equals(other);

    /// <inheritdoc/>
    public static bool operator ==(GridConfigurationKey left, GridConfigurationKey right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(GridConfigurationKey left, GridConfigurationKey right) => !left.Equals(right);
}
