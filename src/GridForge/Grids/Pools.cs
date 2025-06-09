using SwiftCollections.Pool;
using System;

namespace GridForge.Grids
{
    internal static class Pools
    {
        #region Grid Pooling

        /// <summary>
        /// Object pool for reusing <see cref="VoxelGrid"/> instances.
        /// </summary>
        public static readonly SwiftObjectPool<VoxelGrid> GridPool = new SwiftObjectPool<VoxelGrid>(
                    createFunc: () => new VoxelGrid(),
                    actionOnRelease: grid => grid.Reset()
                );

        /// <summary>
        /// Object pool for reusing <see cref="Voxel"/> instances.
        /// </summary>
        public static readonly SwiftObjectPool<Voxel> VoxelPool = new SwiftObjectPool<Voxel>(
                createFunc: () => new Voxel(),
                actionOnRelease: voxel => voxel.Reset()
            );

        /// <summary>
        /// Object pool for reusing <see cref="ScanCell"/> instances.
        /// </summary>
        public static readonly SwiftObjectPool<ScanCell> ScanCellPool = new SwiftObjectPool<ScanCell>(
                createFunc: () => new ScanCell(),
                actionOnRelease: cell => cell.Reset()
            );

        #endregion

        #region Voxel Pooling

        /// <summary>
        /// Object pool for caching neighbor voxel arrays.
        /// </summary>
        private static readonly Lazy<SwiftArrayPool<Voxel>> _voxelNeighborPool =
            new Lazy<SwiftArrayPool<Voxel>>(() => new SwiftArrayPool<Voxel>());

        /// <inheritdoc cref="_voxelNeighborPool"/>
        public static SwiftArrayPool<Voxel> VoxelNeighborPool => _voxelNeighborPool.Value;

        #endregion

        public static void ClearPools()
        {
            GridPool.Clear();
            VoxelPool.Clear();
            ScanCellPool.Clear();
            VoxelNeighborPool.Clear();
        }
    }
}
