using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;

namespace GridForge.Grids
{
    internal static class Pools
    {
        #region Grid Pooling

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

        #endregion

        #region Node Pooling

        /// <summary>
        /// Object pool for caching neighbor node arrays.
        /// </summary>
        private static readonly Lazy<SwiftArrayPool<Node>> _nodeNeighborPool =
            new Lazy<SwiftArrayPool<Node>>(() => new SwiftArrayPool<Node>());

        /// <inheritdoc cref="_nodeNeighborPool"/>
        public static SwiftArrayPool<Node> NodeNeighborPool => _nodeNeighborPool.Value;

        #endregion

        public static void ClearPools()
        {
            GridPool.Clear();
            NodePool.Clear();
            ScanCellPool.Clear();
            NodeNeighborPool.Clear();
        }
    }
}
