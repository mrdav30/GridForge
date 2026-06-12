//=======================================================================
// GridEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing a grid at the time a world grid notification is raised.
/// </summary>
public readonly struct GridEventInfo
{
    /// <summary>
    /// The runtime token of the owning <see cref="GridWorld"/> instance.
    /// </summary>
    public readonly int WorldSpawnToken;

    /// <summary>
    /// The stable slot index assigned to the grid within <see cref="GridWorld.ActiveGrids"/>.
    /// </summary>
    public readonly ushort GridIndex;

    /// <summary>
    /// The unique spawn token for the specific grid instance occupying <see cref="GridIndex"/>.
    /// </summary>
    public readonly int GridSpawnToken;

    /// <summary>
    /// The snapped configuration for the grid when the notification was raised.
    /// </summary>
    public readonly GridConfiguration Configuration;

    /// <summary>
    /// The per-grid version recorded when the notification was raised.
    /// </summary>
    public readonly uint GridVersion;

    /// <summary>
    /// The reason this grid event was raised.
    /// </summary>
    public readonly GridEventKind ChangeKind;

    /// <summary>
    /// The changed voxel index for voxel-scoped grid events.
    /// </summary>
    public readonly VoxelIndex VoxelIndex;

    /// <summary>
    /// The minimum world-space bounds affected by this event.
    /// </summary>
    public readonly Vector3d AffectedBoundsMin;

    /// <summary>
    /// The maximum world-space bounds affected by this event.
    /// </summary>
    public readonly Vector3d AffectedBoundsMax;

    /// <summary>
    /// The minimum snapped bounds of the grid.
    /// </summary>
    public readonly Vector3d BoundsMin => Configuration.BoundsMin;

    /// <summary>
    /// The maximum snapped bounds of the grid.
    /// </summary>
    public readonly Vector3d BoundsMax => Configuration.BoundsMax;

    /// <summary>
    /// Initializes a new immutable grid event snapshot.
    /// </summary>
    public GridEventInfo(
        int worldSpawnToken,
        ushort gridIndex,
        int gridSpawnToken,
        GridConfiguration configuration,
        uint gridVersion,
        GridEventKind changeKind = GridEventKind.Unspecified,
        VoxelIndex voxelIndex = default,
        Vector3d affectedBoundsMin = default,
        Vector3d affectedBoundsMax = default)
    {
        WorldSpawnToken = worldSpawnToken;
        GridIndex = gridIndex;
        GridSpawnToken = gridSpawnToken;
        Configuration = configuration;
        GridVersion = gridVersion;
        ChangeKind = changeKind;
        VoxelIndex = voxelIndex;
        AffectedBoundsMin = !voxelIndex.IsAllocated && affectedBoundsMin == default && affectedBoundsMax == default
            ? configuration.BoundsMin
            : affectedBoundsMin;
        AffectedBoundsMax = !voxelIndex.IsAllocated && affectedBoundsMin == default && affectedBoundsMax == default
            ? configuration.BoundsMax
            : affectedBoundsMax;
    }

    /// <summary>
    /// Creates an exact bounds key from the stored grid configuration.
    /// </summary>
    public readonly BoundsKey ToBoundsKey() => Configuration.ToBoundsKey();
}
