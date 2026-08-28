//=======================================================================
// GridCoveredAddressGeneration.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Configuration;

namespace GridForge.Grids.Topology;

/// <summary>
/// Identifies one exact active grid generation eligible for a covered-address query.
/// </summary>
public readonly struct GridCoveredAddressGeneration : IComparable<GridCoveredAddressGeneration>
{
    /// <summary>The durable normalized grid identity.</summary>
    public GridConfigurationKey ConfigurationKey { get; }

    /// <summary>The recyclable world-local grid slot.</summary>
    public ushort GridIndex { get; }

    /// <summary>The exact process-unique grid allocation identity.</summary>
    public long GridSpawnToken { get; }

    /// <summary>The last committed sequence applied to this grid generation.</summary>
    public ulong GridLastChangeSequence { get; }

    /// <summary>Initializes an exact eligible grid-generation identity.</summary>
    public GridCoveredAddressGeneration(
        GridConfigurationKey configurationKey,
        ushort gridIndex,
        long gridSpawnToken,
        ulong gridLastChangeSequence)
    {
        ConfigurationKey = configurationKey;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        GridLastChangeSequence = gridLastChangeSequence;
    }

    /// <summary>
    /// Compares durable configuration identity only. Eligible inputs must be strictly ascending by this order.
    /// </summary>
    public int CompareTo(GridCoveredAddressGeneration other) =>
        CompareConfigurationKeys(ConfigurationKey, other.ConfigurationKey);

    internal static int CompareConfigurationKeys(
        GridConfigurationKey left,
        GridConfigurationKey right)
    {
        int value = left.BoundsMin.X.CompareTo(right.BoundsMin.X);
        if (value != 0) return value;
        value = left.BoundsMin.Y.CompareTo(right.BoundsMin.Y);
        if (value != 0) return value;
        value = left.BoundsMin.Z.CompareTo(right.BoundsMin.Z);
        if (value != 0) return value;
        value = left.BoundsMax.X.CompareTo(right.BoundsMax.X);
        if (value != 0) return value;
        value = left.BoundsMax.Y.CompareTo(right.BoundsMax.Y);
        if (value != 0) return value;
        value = left.BoundsMax.Z.CompareTo(right.BoundsMax.Z);
        if (value != 0) return value;
        value = left.TopologyKind.CompareTo(right.TopologyKind);
        if (value != 0) return value;
        value = left.TopologyMetrics.CellRadius.CompareTo(right.TopologyMetrics.CellRadius);
        if (value != 0) return value;
        value = left.TopologyMetrics.CellWidth.CompareTo(right.TopologyMetrics.CellWidth);
        if (value != 0) return value;
        value = left.TopologyMetrics.LayerHeight.CompareTo(right.TopologyMetrics.LayerHeight);
        if (value != 0) return value;
        value = left.TopologyMetrics.CellLength.CompareTo(right.TopologyMetrics.CellLength);
        return value != 0
            ? value
            : left.TopologyMetrics.HexOrientation.CompareTo(right.TopologyMetrics.HexOrientation);
    }
}
