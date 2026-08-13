//=======================================================================
// ObstacleClearEventInfo.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing a bulk obstacle clear operation on a voxel.
/// </summary>
public readonly struct ObstacleClearEventInfo
{
    /// <summary>
    /// The world-owned ordering and cause identity for this committed mutation.
    /// </summary>
    public readonly GridChangeStamp ChangeStamp;

    /// <summary>
    /// The voxel that had its obstacles cleared.
    /// </summary>
    public readonly WorldVoxelIndex VoxelIndex;

    /// <summary>
    /// The number of obstacles removed by the clear operation.
    /// </summary>
    public readonly byte ClearedObstacleCount;

    /// <summary>
    /// The grid version recorded after the clear operation completes.
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
    /// Initializes a new immutable obstacle clear snapshot.
    /// </summary>
    public ObstacleClearEventInfo(
        WorldVoxelIndex voxelIndex,
        byte clearedObstacleCount,
        uint gridVersion,
        GridChangeStamp changeStamp = default)
    {
        ChangeStamp = changeStamp;
        VoxelIndex = voxelIndex;
        ClearedObstacleCount = clearedObstacleCount;
        GridVersion = gridVersion;
    }
}
