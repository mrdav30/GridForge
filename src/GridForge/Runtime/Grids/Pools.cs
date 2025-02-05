using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;

namespace GridForge.Grids
{
    internal static class Pools
    {
        /// <summary>
        /// Object pool for reusing <see cref="Grid"/> instances.
        /// </summary>
        public static readonly SwiftObjectPool<Grid> GridPool = new SwiftObjectPool<Grid>(
                    createFunc: () => new Grid(),
                    actionOnRelease: grid => grid.Reset()
                );

        /// <summary>
        /// Object pool for reusing <see cref="Node"/> instances.
        /// </summary>
        public static readonly SwiftObjectPool<Node> NodePool = new SwiftObjectPool<Node>(
                createFunc: () => new Node(),
                actionOnRelease: node => node.Reset()
            );

        /// <summary>
        /// Object pool for reusing <see cref="ScanCell"/> instances.
        /// </summary>
        public static readonly SwiftObjectPool<ScanCell> ScanCellPool = new SwiftObjectPool<ScanCell>(
                createFunc: () => new ScanCell(),
                actionOnRelease: cell => cell.Reset()
            );

        /// <summary>
        /// Object pool for reusing active scan cell collections.
        /// </summary>
        private static Lazy<SwiftHashSetPool<int>> _activeScanCellPool =
            new Lazy<SwiftHashSetPool<int>>(() => new SwiftHashSetPool<int>());

        /// <inheritdoc cref="_activeScanCellPool"/>
        public static SwiftHashSetPool<int> ActiveScanCellPool => _activeScanCellPool.Value;

        /// <summary>
        /// Object pool for reusing neighbor byte arrays.
        /// </summary>
        public static Lazy<SwiftArrayPool<byte>> _gridNeighborPool =
            new Lazy<SwiftArrayPool<byte>>(() => new SwiftArrayPool<byte>(
                    createFunc: (size) => new byte[size].Populate(() => byte.MaxValue),
                    actionOnRelease: (array) => array.Populate(() => byte.MaxValue)
                ));

        /// <inheritdoc cref="_gridNeighborPool"/>
        public static SwiftArrayPool<byte> GridNeighborPool => _gridNeighborPool.Value;

        /// <summary>
        /// Object pool for managing partition allocations.
        /// </summary>
        public static readonly Lazy<SwiftDictionaryPool<int, INodePartition>> _partitionPool =
            new(() => new SwiftDictionaryPool<int, INodePartition>());

        /// <inheritdoc cref="_partitionPool"/>
        public static SwiftDictionaryPool<int, INodePartition> PartitionPool => _partitionPool.Value;

        /// <summary>
        /// Object pool for caching neighbor node arrays.
        /// </summary>
        public static readonly Lazy<SwiftArrayPool<Node>> _nodeNeighborPool =
            new Lazy<SwiftArrayPool<Node>>(() => new SwiftArrayPool<Node>());

        /// <inheritdoc cref="_nodeNeighborPool"/>
        public static SwiftArrayPool<Node> NodeNeighborPool => _nodeNeighborPool.Value;

        /// <summary>
        /// Object pool for managing clustered occupant data efficiently.
        /// </summary>
        public static readonly Lazy<SwiftDictionaryPool<byte, SwiftHashSet<int>>> _clusterToBucketPool =
            new(() => new SwiftDictionaryPool<byte, SwiftHashSet<int>>());

        /// <inheritdoc cref="_clusterToBucketPool"/>
        public static SwiftDictionaryPool<byte, SwiftHashSet<int>> ClusterToBucketPool => _clusterToBucketPool.Value;

        /// <summary>
        /// Object pool for managing node occupant tracking efficiently.
        /// </summary>
        public static readonly Lazy<SwiftDictionaryPool<int, SwiftBucket<INodeOccupant>>> _nodeOccupantPool =
            new(() => new SwiftDictionaryPool<int, SwiftBucket<INodeOccupant>>());

        /// <inheritdoc cref="_nodeOccupantPool"/>
        public static SwiftDictionaryPool<int, SwiftBucket<INodeOccupant>> NodeOccupantPool => _nodeOccupantPool.Value;

        public static void ClearPools()
        {
            GridPool.Clear();
            NodePool.Clear();
            ScanCellPool.Clear();
            ActiveScanCellPool.Clear();
            GridNeighborPool.Clear();
            PartitionPool.Clear();
            NodeNeighborPool.Clear();
            ClusterToBucketPool.Clear();
            NodeOccupantPool.Clear();
        }
    }
}
