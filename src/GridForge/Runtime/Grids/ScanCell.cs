using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;

namespace GridForge.Grids
{
    /// <summary>
    /// Represents a spatial partition within a grid, managing occupants at a finer granularity than grid nodes.
    /// Handles efficient tracking, retrieval, and removal of occupants within a designated scan cell area.
    /// </summary>
    public class ScanCell
    {
        #region Properties

        /// <summary>
        /// The global index of the grid this scan cell belongs to.
        /// </summary>
        public ushort GridIndex { get; private set; }

        /// <summary>
        /// A unique identifier for this scan cell, derived from spatial hashing.
        /// </summary>
        public int CellKey { get; private set; }

        /// <summary>
        /// Maps a <see cref="INodeOccupant.ClusterKey"/> to a set of GridNodes containing matching occupants.
        /// </summary>
        private SwiftDictionary<byte, SwiftHashSet<int>> _clusterToBucket;

        /// <summary>
        /// Maps a <see cref="Node.SpawnToken"/> to a bucket of associated <see cref="INodeOccupant"/> instances.
        /// </summary>
        private SwiftDictionary<int, SwiftBucket<INodeOccupant>> _nodeOccupants;

        /// <summary>
        /// The total number of occupants in this scan cell.
        /// </summary>
        public int CellOccupantCount { get; private set; }

        /// <summary>
        /// Indicates whether this scan cell is currently allocated in the grid.
        /// </summary>
        public bool IsAllocated { get; private set; }

        /// <summary>
        /// Determines whether this scan cell is occupied by any entities.
        /// </summary>
        public bool IsOccupied => IsAllocated && CellOccupantCount > 0;

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Initializes the scan cell with the specified grid index and unique cell key.
        /// </summary>
        public void Initialize(ushort gridIndex, int cellKey)
        {
            GridIndex = gridIndex;
            CellKey = cellKey;
            IsAllocated = true;
        }

        /// <summary>
        /// Resets the scan cell, clearing all occupants and returning memory to pools.
        /// </summary>
        public void Reset()
        {
            if (!IsAllocated)
                return;

            if (_clusterToBucket != null)
            {
                Pools.ClusterToBucketPool.Release(_clusterToBucket);
                _clusterToBucket = null;
            }

            if (_nodeOccupants != null)
            {
                Pools.NodeOccupantPool.Release(_nodeOccupants);
                _nodeOccupants = null;
            }

            CellOccupantCount = 0;

            GridIndex = ushort.MaxValue;
            CellKey = byte.MaxValue;

            IsAllocated = false;
        }

        #endregion

        #region Occupant Management

        /// <summary>
        /// Adds an occupant to this scan cell and tracks its presence.
        /// </summary>
        /// <param name="nodeSpawnToken">The unique spawn token of the node where the occupant resides.</param>
        /// <param name="occupant">The occupant instance to add.</param>
        /// <returns>An integer ticket representing the occupant's position in the data structure.</returns>
        internal int AddOccupant(int nodeSpawnToken, INodeOccupant occupant)
        {
            _nodeOccupants ??= Pools.NodeOccupantPool.Rent();
            if (!_nodeOccupants.TryGetValue(nodeSpawnToken, out SwiftBucket<INodeOccupant> bucket))
            {
                bucket = new SwiftBucket<INodeOccupant>();
                _nodeOccupants[nodeSpawnToken] = bucket;
            }

            int ticket = bucket.Add(occupant);

            if (occupant.ClusterKey >= 0)
            {
                _clusterToBucket ??= Pools.ClusterToBucketPool.Rent();
                if (!_clusterToBucket.TryGetValue(occupant.ClusterKey, out SwiftHashSet<int> occupiedNodes))
                {
                    occupiedNodes = new SwiftHashSet<int>();
                    _clusterToBucket[occupant.ClusterKey] = occupiedNodes;
                }

                occupiedNodes.Add(nodeSpawnToken);
            }

            CellOccupantCount++;

            return ticket;
        }

        /// <summary>
        /// Removes an occupant from this scan cell.
        /// </summary>
        /// <param name="nodeSpawnToken">The spawn token of the node the occupant was assigned to.</param>
        /// <param name="occupantTicket">The ticket associated with this occupant.</param>
        /// <param name="occupantClusterKey">The cluster key associated with this occupant, if any.</param>
        /// <returns>True if the occupant was successfully removed; otherwise, false.</returns>
        internal bool RemoveOccupant(int nodeSpawnToken, int occupantTicket, byte occupantClusterKey = byte.MaxValue)
        {
            if (!IsOccupied || !_nodeOccupants.TryGetValue(nodeSpawnToken, out var bucket))
                return false;

            if (!bucket.TryRemoveAt(occupantTicket))
                return false;

            // If the occupant was the last in its bucket, remove the entire bucket
            if (bucket.Count == 0)
                _nodeOccupants.Remove(nodeSpawnToken);

            if (occupantClusterKey != byte.MaxValue && _clusterToBucket != null)
            {
                if (_clusterToBucket.TryGetValue(occupantClusterKey, out SwiftHashSet<int> occupiedNodes))
                {
                    occupiedNodes.Remove(nodeSpawnToken);

                    // Release empty cluster sets
                    if (occupiedNodes.Count == 0)
                    {
                        _clusterToBucket.Remove(occupantClusterKey);

                        if (_clusterToBucket.Count == 0)
                        {
                            Pools.ClusterToBucketPool.Release(_clusterToBucket);
                            _clusterToBucket = null;
                        }
                    }   
                }
            }

            CellOccupantCount--;

            return true;
        }

        #endregion

        #region Occupant Retrieval

        /// <summary>
        /// Retrieves all occupants associated with a given node spawn token.
        /// </summary>
        /// <param name="nodeSpawnKey">The unique spawn token of the node.</param>
        /// <returns>An enumerable of occupants within this scan cell at the given node.</returns>
        public IEnumerable<INodeOccupant> GetOccupantsFor(int nodeSpawnKey)
        {
            if (!IsOccupied)
            {
                Console.WriteLine($"Scan cell is inactive.");
                yield break;
            }

            if (!_nodeOccupants.TryGetValue(nodeSpawnKey, out SwiftBucket<INodeOccupant> nodeOccupants))
                yield break;

            foreach (INodeOccupant nodeOccupant in nodeOccupants)
                yield return nodeOccupant;
        }


        /// <summary>
        /// Retrieves occupants whose cluster keys match a given condition.
        /// </summary>
        /// <param name="clusterConditional">A function used to filter cluster keys.</param>
        /// <returns>An enumerable of occupants that match the specified condition.</returns>
        public IEnumerable<INodeOccupant> GetConditionalOccupants(Func<byte, bool> clusterConditional)
        {
            if (clusterConditional == null)
            {
                Console.WriteLine($"Must supply a {nameof(clusterConditional)} function parameter.");
                yield break;
            }

            if (!IsOccupied)
            {
                Console.WriteLine($"Scan Cell is not currently occupied.");
                yield break;
            }

            // Loop through cluster-to-bucket dictionary and apply the conditional function
            foreach (KeyValuePair<byte, SwiftHashSet<int>> kvp in _clusterToBucket)
            {
                try
                {
                    if (!clusterConditional(kvp.Key))
                        continue;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error running {nameof(clusterConditional)}:\n{e.Message}\n{e.StackTrace}");
                    yield break;
                }

                foreach (int key in kvp.Value)
                {
                    foreach (INodeOccupant nodeOccupant in GetOccupantsFor(key))
                        yield return nodeOccupant;
                }
            }
        }

        #endregion
    }
}
