using GridForge.Configuration;
using GridForge.Spatial;

namespace GridForge.Grids;

/// <summary>
/// Immutable snapshot describing a single obstacle mutation on a voxel.
/// </summary>
public readonly struct ObstacleEventInfo
{
    /// <summary>
    /// The voxel affected by the obstacle mutation.
    /// </summary>
    public readonly GlobalVoxelIndex VoxelIndex;

    /// <summary>
    /// The token identifying the obstacle that was added or removed.
    /// </summary>
    public readonly BoundsKey ObstacleToken;

    /// <summary>
    /// The number of active obstacles on the voxel after the mutation completes.
    /// </summary>
    public readonly byte ObstacleCount;

    /// <summary>
    /// The grid version recorded after the mutation completes.
    /// </summary>
    public readonly uint GridVersion;

    /// <summary>
    /// The grid index containing <see cref="VoxelIndex"/>.
    /// </summary>
    public readonly ushort GridIndex => VoxelIndex.GridIndex;

    /// <summary>
    /// Initializes a new immutable obstacle mutation snapshot.
    /// </summary>
    public ObstacleEventInfo(
        GlobalVoxelIndex voxelIndex,
        BoundsKey obstacleToken,
        byte obstacleCount,
        uint gridVersion)
    {
        VoxelIndex = voxelIndex;
        ObstacleToken = obstacleToken;
        ObstacleCount = obstacleCount;
        GridVersion = gridVersion;
    }
}
