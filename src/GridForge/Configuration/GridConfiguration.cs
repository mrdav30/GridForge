//=======================================================================
// GridConfiguration.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using MemoryPack;
using SwiftCollections.Utility;
using System;
using System.Text.Json.Serialization;

namespace GridForge.Configuration;

/// <summary>
/// Defines the configuration parameters for a grid, including boundaries, topology, storage, and scan cell size.
/// Used to describe grid properties before a world normalizes and registers the grid.
/// </summary>
[Serializable]
[MemoryPackable]
public readonly partial struct GridConfiguration
{
    /// <summary>
    /// The default size of each scan cell.
    /// </summary>
    public const int DefaultScanCellSize = 8;

    #region Properties

    /// <summary>
    /// The minimum boundary of the grid in world coordinates.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly Vector3d BoundsMin;

    /// <summary>
    /// The maximum boundary of the grid in world coordinates.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly Vector3d BoundsMax;

    /// <summary>
    /// The size of each scan cell, determining the granularity of spatial partitioning.
    /// Customizable based on grid density and expected entity distribution.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly int ScanCellSize;

    /// <summary>
    /// The cell topology used by this grid. Rectangular-prism grids interpret
    /// <see cref="VoxelIndex"/> as local X/Y/Z coordinates; hex-prism grids
    /// interpret it as axial Q, vertical layer, and axial R.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly GridTopologyKind TopologyKind;

    /// <summary>
    /// The deterministic cell geometry used by this grid's topology.
    /// Rectangular-prism metrics define cell width, layer height, and length.
    /// Hex-prism metrics define horizontal radius, layer height, and orientation.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly GridTopologyMetrics TopologyMetrics;

    /// <summary>
    /// The physical voxel storage used by this grid. Dense storage materializes every in-bounds voxel;
    /// sparse storage materializes only explicitly configured voxels.
    /// </summary>
    [JsonInclude]
    [MemoryPackInclude]
    public readonly GridStorageKind StorageKind;

    /// <summary>
    /// The center point of the grid's bounding volume.
    /// </summary>
    [JsonIgnore]
    [MemoryPackIgnore]
    public readonly Vector3d GridCenter => (BoundsMin + BoundsMax) * Fixed64.Half;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of <see cref="GridConfiguration"/> with specified bounds and scan cell size.
    /// Ensures that <see cref="BoundsMin"/> is always less than or equal to <see cref="BoundsMax"/>.
    /// </summary>
    /// <param name="boundsMin">The minimum boundary of the grid.</param>
    /// <param name="boundsMax">The maximum boundary of the grid.</param>
    /// <param name="scanCellSize">The size of scan cells within the grid. Default is 8.</param>
    /// <param name="topologyKind">The topology kind used by the grid. Defaults to rectangular-prism.</param>
    /// <param name="topologyMetrics">Deterministic topology metrics. Defaults to 1x1x1 rectangular-prism cells.</param>
    /// <param name="storageKind">The physical voxel storage kind. Defaults to dense storage.</param>
    [JsonConstructor]
    public GridConfiguration(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int scanCellSize = DefaultScanCellSize,
        GridTopologyKind topologyKind = GridTopologyKind.RectangularPrism,
        GridTopologyMetrics topologyMetrics = default,
        GridStorageKind storageKind = GridStorageKind.Dense)
    {
        if (boundsMin > boundsMax)
            GridForgeLogger.Channel.Warn($"GridMin was greater than GridMax, auto-correcting values.");

        BoundsMin = new Vector3d(
            FixedMath.Min(boundsMin.X, boundsMax.X),
            FixedMath.Min(boundsMin.Y, boundsMax.Y),
            FixedMath.Min(boundsMin.Z, boundsMax.Z));
        BoundsMax = new Vector3d(
            FixedMath.Max(boundsMin.X, boundsMax.X),
            FixedMath.Max(boundsMin.Y, boundsMax.Y),
            FixedMath.Max(boundsMin.Z, boundsMax.Z));

        ScanCellSize = scanCellSize > 0 ? scanCellSize : DefaultScanCellSize;
        TopologyKind = topologyKind;
        TopologyMetrics = topologyMetrics == default
            ? GridTopologyMetrics.Normalize(topologyKind, topologyMetrics)
            : topologyMetrics;
        StorageKind = storageKind;
    }

    #endregion

    /// <summary>
    /// Creates an exact identity key for this configuration's snapped bounds.
    /// </summary>
    public readonly BoundsKey ToBoundsKey() => new(BoundsMin, BoundsMax);

    /// <summary>
    /// Creates an exact identity key for this configuration's snapped bounds and topology.
    /// </summary>
    public readonly GridConfigurationKey ToGridKey() =>
        new(BoundsMin, BoundsMax, TopologyKind, TopologyMetrics);

    /// <inheritdoc/>
    public override readonly int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(BoundsMin.GetHashCode(), BoundsMax.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, TopologyKind.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, TopologyMetrics.GetHashCode());
    }
}
