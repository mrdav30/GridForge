//=======================================================================
// GridEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
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
    /// The world-owned ordering and cause identity for this committed mutation.
    /// </summary>
    public readonly GridChangeStamp ChangeStamp;

    /// <summary>
    /// The process-unique 64-bit runtime allocation token of the owning <see cref="GridWorld"/> instance.
    /// </summary>
    public readonly long WorldSpawnToken;

    /// <summary>
    /// The stable slot index assigned to the grid within <see cref="GridWorld.ActiveGrids"/>.
    /// </summary>
    public readonly ushort GridIndex;

    /// <summary>
    /// The 64-bit world-local allocation generation for the grid occupying <see cref="GridIndex"/>.
    /// </summary>
    public readonly long GridSpawnToken;

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
    /// Whether this event contains an exact post-mutation voxel state.
    /// </summary>
    public readonly bool HasVoxelState;

    /// <summary>
    /// Whether the addressed physical voxel exists after the mutation.
    /// </summary>
    public readonly bool IsVoxelPresent;

    /// <summary>
    /// The addressed voxel's obstacle count after the mutation.
    /// </summary>
    public readonly byte ObstacleCount;

    /// <summary>
    /// The world-local commit order of this event.
    /// </summary>
    /// <value>The world-local commit order of this event.</value>
    public ulong ChangeSequence => ChangeStamp.Sequence;

    /// <summary>
    /// The logical cause shared with exact notifications for the same mutation.
    /// </summary>
    /// <value>The logical cause shared with exact notifications for the same mutation.</value>
    public ulong CauseId => ChangeStamp.CauseId;

    /// <summary>
    /// The minimum snapped bounds of the grid.
    /// </summary>
    /// <value>The minimum snapped bounds of the grid.</value>
    public readonly Vector3d BoundsMin => Configuration.BoundsMin;

    /// <summary>
    /// The maximum snapped bounds of the grid.
    /// </summary>
    /// <value>The maximum snapped bounds of the grid.</value>
    public readonly Vector3d BoundsMax => Configuration.BoundsMax;

    /// <summary>
    /// Initializes a new immutable grid event snapshot.
    /// </summary>
    /// <param name="worldSpawnToken">The process-unique 64-bit runtime allocation token of the owning <see cref="T:GridForge.Grids.GridWorld" /> instance.</param>
    /// <param name="gridIndex">The stable slot index assigned to the grid within <see cref="P:GridForge.Grids.GridWorld.ActiveGrids" />.</param>
    /// <param name="gridSpawnToken">The 64-bit world-local allocation generation for the grid occupying <see cref="F:GridForge.Grids.GridEventInfo.GridIndex" />.</param>
    /// <param name="configuration">The snapped configuration for the grid when the notification was raised.</param>
    /// <param name="gridVersion">The per-grid version recorded when the notification was raised.</param>
    /// <param name="changeKind">The reason this grid event was raised.</param>
    /// <param name="voxelIndex">The changed voxel index for voxel-scoped grid events.</param>
    /// <param name="affectedBoundsMin">The minimum world-space bounds affected by this event.</param>
    /// <param name="affectedBoundsMax">The maximum world-space bounds affected by this event.</param>
    /// <param name="changeStamp">The world-owned ordering and cause identity for this committed mutation.</param>
    /// <param name="hasVoxelState">Whether this event contains an exact post-mutation voxel state.</param>
    /// <param name="isVoxelPresent">Whether the addressed physical voxel exists after the mutation.</param>
    /// <param name="obstacleCount">The addressed voxel's obstacle count after the mutation.</param>
    public GridEventInfo(
        long worldSpawnToken,
        ushort gridIndex,
        long gridSpawnToken,
        GridConfiguration configuration,
        uint gridVersion,
        GridEventKind changeKind = GridEventKind.Unspecified,
        VoxelIndex voxelIndex = default,
        Vector3d affectedBoundsMin = default,
        Vector3d affectedBoundsMax = default,
        GridChangeStamp changeStamp = default,
        bool hasVoxelState = false,
        bool isVoxelPresent = false,
        byte obstacleCount = 0)
    {
        ChangeStamp = changeStamp;
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
        HasVoxelState = hasVoxelState;
        IsVoxelPresent = isVoxelPresent;
        ObstacleCount = obstacleCount;
    }

    /// <summary>
    /// Creates an exact bounds key from the stored grid configuration.
    /// </summary>
    /// <returns>An exact copy of the current bounds as a key, without additional snapping.</returns>
    public readonly BoundsKey ToBoundsKey() => Configuration.ToBoundsKey();
}
