using FixedMathSharp;
using GridForge.Grids;
using MemoryPack;
using SwiftCollections;
using System;
using System.Text.Json.Serialization;

namespace GridForge.Configuration;

/// <summary>
/// Defines the configuration parameters for a grid, including boundaries and scan cell size.
/// Used to initialize and validate grid properties before creation.
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
    [JsonConstructor]
    public GridConfiguration(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int scanCellSize = DefaultScanCellSize)
    {
        if (boundsMin > boundsMax)
            GridForgeLogger.Warn("GridMin was greater than GridMax, auto-correcting values.");

        // Ensures GridMin <= GridMax for each coordinate axis
        (BoundsMin, BoundsMax) = GlobalGridManager.SnapBoundsToVoxelSize(boundsMin, boundsMax);

        ScanCellSize = scanCellSize > 0 ? scanCellSize : DefaultScanCellSize;
    }

    #endregion

    /// <summary>
    /// Creates an exact identity key for this configuration's snapped bounds.
    /// </summary>
    public readonly BoundsKey ToBoundsKey() => new(BoundsMin, BoundsMax);

    /// <inheritdoc/>
    public override readonly int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(BoundsMin.GetHashCode(), BoundsMax.GetHashCode());
}
