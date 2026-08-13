//=======================================================================
// ObstacleEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
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
    public ulong ChangeSequence => ChangeStamp.Sequence;

    /// <summary>
    /// The logical cause shared with the corresponding grid notification.
    /// </summary>
    public ulong CauseId => ChangeStamp.CauseId;

    /// <summary>
    /// The grid index containing <see cref="VoxelIndex"/>.
    /// </summary>
    public readonly ushort GridIndex => VoxelIndex.GridIndex;

    /// <summary>
    /// Initializes a new immutable obstacle mutation snapshot.
    /// </summary>
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
