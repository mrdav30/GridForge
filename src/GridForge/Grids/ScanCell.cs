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
        /// A unique identifier for this scan cell in the grid, derived from spatial hashing.
        /// </summary>
        public int CellKey { get; private set; }


        /// <summary>
        /// Unique token identifying this scan cell instance.
        /// </summary>
        public int SpawnToken { get; private set; }

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
        /// Determines whether this scan cell is occupied by any occupants.
        /// A scan cell is only considered occupied if it is allocated and contains at least one occupant.
        /// </summary>
        public bool IsOccupied => IsAllocated && CellOccupantCount > 0;

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Initializes the scan cell with the specified grid index and unique cell key.
        /// </summary>
        internal void Initialize(ushort gridIndex, int cellKey)
        {
            GridIndex = gridIndex;
            CellKey = cellKey;
            SpawnToken = GetHashCode();
            IsAllocated = true;
        }

        /// <summary>
        /// Resets the scan cell, clearing all occupants and returning memory to object pools.
        /// This effectively marks the scan cell as deallocated and removes all references.
        /// </summary>
        internal void Reset()
        {
            if (!IsAllocated)
                return;

            if (_nodeOccupants != null)
            {
                foreach (SwiftBucket<INodeOccupant> nodeOccupants in _nodeOccupants.Values)
                {
                    SwiftCollectionPool<SwiftBucket<INodeOccupant>, INodeOccupant>.Release(nodeOccupants);
                    _nodeOccupants = null;
                }
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
        /// <param name="occupantTicket"></param>
        /// <returns>An integer ticket representing the occupant's position in the data structure.</returns>
        internal void AddOccupant(int nodeSpawnToken, INodeOccupant occupant, out int occupantTicket)
        {
            _nodeOccupants ??= new SwiftDictionary<int, SwiftBucket<INodeOccupant>>();
            if (!_nodeOccupants.TryGetValue(nodeSpawnToken, out SwiftBucket<INodeOccupant> bucket))
            {
                bucket = SwiftCollectionPool<SwiftBucket<INodeOccupant>, INodeOccupant>.Rent();
                _nodeOccupants[nodeSpawnToken] = bucket;
            }

            occupantTicket = bucket.Add(occupant);
            CellOccupantCount++;
        }

        /// <summary>
        /// Removes an occupant from this scan cell.
        /// </summary>
        /// <param name="nodeSpawnToken">The spawn token of the node the occupant was assigned to.</param>
        /// <param name="occupant"></param>
        /// <returns>True if the occupant was successfully removed; otherwise, false.</returns>
        internal bool TryRemoveOccupant(int nodeSpawnToken, INodeOccupant occupant)
        {
            if (!IsOccupied || !_nodeOccupants.TryGetValue(nodeSpawnToken, out var bucket))
                return false;

            if (!bucket.TryRemoveAt(occupant.OccupantTicket))
                return false;

            // If the occupant was the last in its bucket, remove the entire bucket
            if (bucket.Count == 0)
                _nodeOccupants.Remove(nodeSpawnToken);

            CellOccupantCount--;

            return true;
        }

        #endregion

        #region Occupant Retrieval

        /// <summary>
        /// Retrieves all occupants associated with this ScanCell.
        /// </summary>
        /// <returns>An enumerable of occupants within this scan cell.</returns>
        internal IEnumerable<INodeOccupant> GetOccupants()
        {
            foreach (SwiftBucket<INodeOccupant> bucket in _nodeOccupants.Values)
            {
                foreach (INodeOccupant nodeOccupant in bucket)
                    yield return nodeOccupant;
            }
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        internal IEnumerable<INodeOccupant> GetConditionalOccupants(Func<byte, bool> groupConditional)
        {
            // Loop through each node's bucket and filter by the cluster condition
            foreach (var bucket in _nodeOccupants.Values)
            {
                foreach (var occupant in bucket)
                {
                    if (groupConditional(occupant.OccupantGroupId))
                        yield return occupant;
                }
            }
        }

        /// <summary>
        /// Retrieves all occupants associated with a specific node spawn token within this scan cell.
        /// </summary>
        /// <param name="nodeSpawnKey">The unique spawn key of the node.</param>
        /// <returns>An enumerable collection of occupants assigned to the node.</returns>
        internal IEnumerable<INodeOccupant> GetOccupantsFor(int nodeSpawnKey)
        {
            if (!_nodeOccupants.TryGetValue(nodeSpawnKey, out SwiftBucket<INodeOccupant> nodeOccupants))
                yield break;

            foreach (INodeOccupant nodeOccupant in nodeOccupants)
                yield return nodeOccupant;
        }

        /// <summary>
        /// Attempts to retrieve a specific occupant in this scan cell using a node's spawn key and occupant ticket.
        /// </summary>
        /// <param name="nodeSpawnKey">The spawn key of the node the occupant belongs to.</param>
        /// <param name="occupantTicket">The unique ticket identifying the occupant.</param>
        /// <param name="nodeOccupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        internal bool TryGetOccupantAt(int nodeSpawnKey, int occupantTicket, out INodeOccupant nodeOccupant)
        {
            nodeOccupant = null;
            if (!_nodeOccupants.TryGetValue(nodeSpawnKey, out SwiftBucket<INodeOccupant> nodeOccupants)
                || !nodeOccupants.IsAllocated(occupantTicket))
            {
                return false;
            }

            nodeOccupant = nodeOccupants[occupantTicket];
            return true;
        }

        #endregion

        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(
                GridIndex,
                CellKey,
                31
            );
    }
}
