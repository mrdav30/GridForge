//=======================================================================
// GridConfiguration.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Text.Json.Serialization;
using FixedMathSharp;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using MemoryPack;
using SwiftCollections.Utility;

namespace GridForge.Configuration;

/// <summary>
/// Defines the configuration parameters for a grid, including boundaries, topology, storage, and scan cell size.
/// Used to describe grid properties before a world normalizes and registers the grid.
/// </summary>
/// <remarks>An explicitly constructed configuration orders bounds per component, replaces nonpositive scan-cell sizes with 8, and resolves default metrics to unit edges for rectangular topology. Hex topology requires explicit positive radius and layer height. <c>default(GridConfiguration)</c> and parameterless struct construction bypass that initialization: scan-cell size and metrics remain zero, and normalization fails. System.Text.Json restoration uses this type's normalizing constructor. Standard MemoryPack uses generated unmanaged field restoration without that constructor. In particular, a serialized default value restores with scan-cell size 8 and unit rectangular metrics through JSON, but retains zero fields through MemoryPack. Neither restore path registers a live grid; validate and normalize restored input explicitly.</remarks>
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
    /// <value>The center point of the grid's bounding volume.</value>
    [JsonIgnore]
    [MemoryPackIgnore]
    public readonly Vector3d GridCenter => Vector3d.Midpoint(BoundsMin, BoundsMax);

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of <see cref="GridConfiguration"/> with specified bounds and scan cell size.
    /// Ensures that <see cref="BoundsMin"/> is always less than or equal to <see cref="BoundsMax"/>.
    /// </summary>
    /// <param name="boundsMin">The first world-space bounds corner. Each component is ordered with boundsMax; supplied values are not mutated.</param>
    /// <param name="boundsMax">The opposite world-space bounds corner, ordered per component with boundsMin.</param>
    /// <param name="scanCellSize">Scan-cell edge length in topology-address units. Nonpositive values become 8.</param>
    /// <param name="topologyKind">The topology kind used by the grid. Defaults to rectangular-prism.</param>
    /// <param name="topologyMetrics">Cell geometry in world units. Default resolves unit rectangular metrics. Hex configurations require explicit positive radius and layer height; nondefault values are stored until validation.</param>
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
    /// Creates an exact identity key for this configuration's current bounds.
    /// </summary>
    /// <remarks>Use <see cref="TryNormalize"/> before deriving a normalized bounds key.</remarks>
    /// <returns>An exact copy of the current bounds as a key, without additional snapping.</returns>
    public readonly BoundsKey ToBoundsKey() => new(BoundsMin, BoundsMax);

    /// <summary>
    /// Creates an exact identity key for this configuration's current bounds and topology.
    /// </summary>
    /// <remarks>Use the descriptor from <see cref="TryNormalize"/> for normalized registration identity.</remarks>
    /// <returns>An exact bounds/topology key. Storage kind and scan-cell size are excluded.</returns>
    public readonly GridConfigurationKey ToGridKey() =>
        new(BoundsMin, BoundsMax, TopologyKind, TopologyMetrics);

    /// <summary>
    /// Validates and normalizes this configuration without registering a live grid.
    /// </summary>
    /// <param name="descriptor">
    /// The normalized configuration, exact binding key, dimensions, and local-index validator.
    /// </param>
    /// <returns>
    /// True when the topology and dimensions are supported; otherwise false and
    /// <paramref name="descriptor"/> is default.
    /// </returns>
    /// <remarks>Validates supported topology, metric positivity, positive dimensions, and an address count no greater than Int32.MaxValue. Storage kind and hex orientation enum values are not exhaustively validated here. No grid is registered and no physical voxels are materialized. Arithmetic follows underlying fixed-point operations; false is not a general exception-catching boundary.</remarks>
    public readonly bool TryNormalize(out NormalizedGridConfiguration descriptor)
    {
        descriptor = default;
        if (!GridTopologyFactory.TryCreate(this, out IGridTopology? topology))
            return false;

        (Vector3d boundsMin, Vector3d boundsMax) =
            topology!.NormalizeBounds(BoundsMin, BoundsMax);
        GridConfiguration normalizedConfiguration = new(
            boundsMin,
            boundsMax,
            ScanCellSize,
            TopologyKind,
            TopologyMetrics,
            StorageKind);
        GridDimensions dimensions = topology.CalculateDimensions(boundsMin, boundsMax);

        if (dimensions.Width <= 0 || dimensions.Height <= 0 || dimensions.Length <= 0)
        {
            GridForgeLogger.Channel.Warn($"Grid dimensions must define a positive voxel address space.");
            return false;
        }

        long layerSize = (long)dimensions.Width * dimensions.Height;
        if (layerSize > int.MaxValue || layerSize * dimensions.Length > int.MaxValue)
        {
            GridForgeLogger.Channel.Warn($"Grid dimensions exceed the supported int voxel address space.");
            return false;
        }

        descriptor = new NormalizedGridConfiguration(normalizedConfiguration, topology, dimensions);
        return true;
    }

    /// <summary>Computes a hash of the equality components.</summary>
    /// <returns>A hash for lookup; collisions are possible and equality remains authoritative.</returns>
    public override readonly int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes(BoundsMin.GetHashCode(), BoundsMax.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, TopologyKind.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, TopologyMetrics.GetHashCode());
    }
}
