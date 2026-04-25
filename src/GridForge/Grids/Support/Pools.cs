using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;

namespace GridForge.Grids;

internal static class Pools
{
    #region Grid Pooling

    /// <summary>
    /// Object pool for reusing <see cref="VoxelGrid"/> instances.
    /// </summary>
    public static readonly SwiftObjectPool<VoxelGrid> GridPool = new(
        createFunc: () => new VoxelGrid(),
        actionOnRelease: grid => grid.Reset()
    );

    /// <summary>
    /// Object pool for reusing <see cref="Voxel"/> instances.
    /// </summary>
    public static readonly SwiftObjectPool<Voxel> VoxelPool = new(
        createFunc: () => new Voxel(),
        actionOnRelease: voxel => voxel.Reset()
    );

    public static readonly SwiftObjectPool<SwiftSparseMap<ScanCell>> ScanCellMapPool = new(
        createFunc: () => new SwiftSparseMap<ScanCell>(),
        actionOnRelease: map => map.Clear()
    );

    /// <summary>
    /// Object pool for reusing <see cref="ScanCell"/> instances.
    /// </summary>
    public static readonly SwiftObjectPool<ScanCell> ScanCellPool = new(
        createFunc: () => new ScanCell(),
        actionOnRelease: cell => cell.Reset()
    );

    #endregion

    #region Node Pooling

    /// <summary>
    /// Object pool for caching neighbor voxel arrays.
    /// </summary>
    public static readonly SwiftArrayPool<Voxel> VoxelNeighborPool = new();

    public static readonly SwiftDictionaryPool<WorldVoxelIndex, SwiftBucket<IVoxelOccupant>> VoxelOccupantDictionaryPool = new();

    public static readonly SwiftObjectPool<SwiftBucket<IVoxelOccupant>> VoxelOccupantBucketPool = new(
        createFunc: () => new SwiftBucket<IVoxelOccupant>(),
        actionOnRelease: bucket => bucket.Clear()
    );

    #endregion

    public static void ClearPools()
    {
        GridPool.Clear();
        VoxelPool.Clear();
        ScanCellMapPool.Clear();
        ScanCellPool.Clear();
        VoxelNeighborPool.Clear();
        VoxelOccupantDictionaryPool.Clear();
        VoxelOccupantBucketPool.Clear();
    }
}
