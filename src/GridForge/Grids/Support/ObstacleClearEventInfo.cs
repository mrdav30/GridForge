using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing a bulk obstacle clear operation on a voxel.
/// </summary>
public readonly struct ObstacleClearEventInfo
{
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
    /// The grid index containing <see cref="VoxelIndex"/>.
    /// </summary>
    public readonly ushort GridIndex => VoxelIndex.GridIndex;

    /// <summary>
    /// Initializes a new immutable obstacle clear snapshot.
    /// </summary>
    public ObstacleClearEventInfo(
        WorldVoxelIndex voxelIndex,
        byte clearedObstacleCount,
        uint gridVersion)
    {
        VoxelIndex = voxelIndex;
        ClearedObstacleCount = clearedObstacleCount;
        GridVersion = gridVersion;
    }
}
