//=======================================================================
// Pools.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Storage;
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

    public static readonly SwiftObjectPool<SwiftSparseMap<SparseVoxelBlock>> SparseVoxelBlockMapPool = new(
        createFunc: () => new SwiftSparseMap<SparseVoxelBlock>(),
        actionOnRelease: map => map.Clear()
    );

    public static readonly SwiftObjectPool<SparseVoxelBlock> SparseVoxelBlockPool = new(
        createFunc: () => new SparseVoxelBlock(),
        actionOnRelease: block => block.Clear()
    );

    public static readonly SwiftDictionaryPool<int, int> SparseVoxelBlockCapacityPool = new();

    /// <summary>
    /// Object pool for reusing <see cref="ScanCell"/> instances.
    /// </summary>
    public static readonly SwiftObjectPool<ScanCell> ScanCellPool = new(
        createFunc: () => new ScanCell(),
        actionOnRelease: cell => cell.Reset()
    );

    #endregion

    #region Node Pooling

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
        SparseVoxelBlockMapPool.Clear();
        SparseVoxelBlockPool.Clear();
        SparseVoxelBlockCapacityPool.Clear();
        ScanCellPool.Clear();
        VoxelOccupantDictionaryPool.Clear();
        VoxelOccupantBucketPool.Clear();
    }
}
