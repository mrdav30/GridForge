//=======================================================================
// ObstacleEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing a single obstacle mutation on a voxel.
/// </summary>
public readonly struct ObstacleEventInfo
{
    /// <summary>
    /// The world-owned ordering and cause identity for this committed mutation.
    /// </summary>
    public readonly GridChangeStamp ChangeStamp;

    /// <summary>
    /// The voxel affected by the obstacle mutation.
    /// </summary>
    public readonly WorldVoxelIndex VoxelIndex;

    /// <summary>
    /// The token identifying the obstacle that was added or removed.
    /// </summary>
    public readonly ObstacleToken ObstacleToken;

    /// <summary>
    /// The number of active obstacles on the voxel after the mutation completes.
    /// </summary>
    public readonly byte ObstacleCount;

    /// <summary>
    /// The grid version recorded after the mutation completes.
    /// </summary>
    public readonly uint GridVersion;

    /// <summary>
    /// The world-local commit order of this event.
    /// </summary>
    /// <value>The world-local commit order of this event.</value>
    public ulong ChangeSequence => ChangeStamp.Sequence;

    /// <summary>
    /// The logical cause shared with the corresponding grid notification.
    /// </summary>
    /// <value>The logical cause shared with the corresponding grid notification.</value>
    public ulong CauseId => ChangeStamp.CauseId;

    /// <summary>
    /// The grid index containing <see cref="VoxelIndex"/>.
    /// </summary>
    /// <value>The grid index containing <see cref="F:GridForge.Grids.ObstacleEventInfo.VoxelIndex" />.</value>
    public readonly ushort GridIndex => VoxelIndex.GridIndex;

    /// <summary>
    /// Initializes a new immutable obstacle mutation snapshot.
    /// </summary>
    /// <param name="voxelIndex">The voxel affected by the obstacle mutation.</param>
    /// <param name="obstacleToken">The token identifying the obstacle that was added or removed.</param>
    /// <param name="obstacleCount">The number of active obstacles on the voxel after the mutation completes.</param>
    /// <param name="gridVersion">The grid version recorded after the mutation completes.</param>
    /// <param name="changeStamp">The world-owned ordering and cause identity for this committed mutation.</param>
    public ObstacleEventInfo(
        WorldVoxelIndex voxelIndex,
        ObstacleToken obstacleToken,
        byte obstacleCount,
        uint gridVersion,
        GridChangeStamp changeStamp = default)
    {
        ChangeStamp = changeStamp;
        VoxelIndex = voxelIndex;
        ObstacleToken = obstacleToken;
        ObstacleCount = obstacleCount;
        GridVersion = gridVersion;
    }
}
