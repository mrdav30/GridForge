using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;

namespace GridForge.Grids
{
    /// <summary>
    /// Represents a node within a 3D grid, handling spatial positioning, obstacles, occupants, and neighbor relationships.
    /// </summary>
    public class Node : IEquatable<Node>
    {
        #region Properties & Fields

        /// <summary>
        /// Unique token identifying this node instance.
        /// </summary>
        public int SpawnToken { get; private set; }

        /// <summary>
        /// The global and local coordinates of this node within the grid system.
        /// </summary>
        public CoordinatesGlobal GlobalCoordinates { get; set; }

        /// <summary>
        /// The global index of the grid this node belongs to.
        /// </summary>
        public int GridIndex => GlobalCoordinates.GridIndex;

        /// <summary>
        /// The local coordinates of this node within its grid.
        /// </summary>
        public CoordinatesLocal LocalCoordinates => GlobalCoordinates.NodeCoordinates;

        /// <summary>
        /// The spatial hash key of the scan cell that this node belongs to.
        /// </summary>
        public int ScanCellKey { get; private set; }

        /// <summary>
        /// The world-space position of this node.
        /// </summary>
        public Vector3d WorldPosition { get; private set; }

        /// <summary>
        /// Indicates whether the neighbor cache is valid.
        /// </summary>
        private bool _isNeighborCacheValid;

        /// <summary>
        /// Cached array of neighboring nodes for fast lookup representing a 3x3x3 linear direction grid
        /// </summary>
        /// <remarks>
        /// Unlike Grid adjacency (which is 1:many), nodes can only have 1 neighbor in any one direction (1:1).
        /// </remarks>
        private Node[] _cachedNeighbors;

        /// <summary>
        /// Stores a unique hash value for each obstacle added to this node to prevent adding duplicates
        /// </summary>
        public SwiftHashSet<int> ObstacleTracker { get; internal set; }

        /// <summary>
        /// The current number of obstacles on this node.
        /// </summary>
        public byte ObstacleCount { get; internal set; }

        /// <summary>
        /// The current number of occupants on this node.
        /// </summary>
        public byte OccupantCount { get; internal set; }

        /// <summary>
        /// Dictionary mapping partition names to their respective partitions.
        /// </summary>
        private SwiftDictionary<int, INodePartition> _partitions;

        /// <summary>
        /// Indicates whether this node has any active partitions.
        /// </summary>
        public bool IsPartioned { get; private set; }

        /// <summary>
        /// Determines if this node is a boundary node.
        /// </summary>
        public bool IsBoundaryNode { get; private set; }

        /// <summary>
        /// The current version of the grid at the time this node was created.
        /// </summary>
        public uint CachedGridVersion { get; internal set; }

        /// <summary>
        /// Indicates whether this node is allocated within a grid.
        /// </summary>
        public bool IsAllocated { get; private set; }

        /// <summary>
        /// Determines whether this node is blocked due to obstacles.
        /// </summary>
        public bool IsBlocked => IsAllocated && ObstacleCount > 0;

        /// <summary>
        /// Determines if this node can accept additional obstacles.
        /// </summary>
        public bool IsBlockable => IsAllocated
            && ObstacleCount < GridObstacleManager.MaxObstacleCount
            && !IsOccupied;

        /// <summary>
        /// Determines whether this node is occupied by entities.
        /// </summary>
        public bool IsOccupied => IsAllocated && OccupantCount > 0;

        /// <summary>
        /// Checks if this node has open slots for new occupants.
        /// </summary>
        public bool HasVacancy => !IsBlocked && OccupantCount < GridOccupantManager.MaxOccupantCount;

        #endregion

        #region Events

        /// <summary>
        /// Event triggered when an obstacle is added or removed.
        /// </summary>
        public Action<GridChange, Node> OnObstacleChange;

        /// <summary>
        /// Event triggered when an occupant is added or removed.
        /// </summary>
        public Action<GridChange, Node> OnOccupantChange;

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Configures the node with its position, grid version, and boundary status.
        /// </summary>
        internal void Initialize(
            CoordinatesGlobal coordinates,
            Vector3d position,
            int scanCellKey,
            bool isBoundaryNode,
            uint gridVersion)
        {
            ScanCellKey = scanCellKey;
            IsBoundaryNode = isBoundaryNode;

            GlobalCoordinates = coordinates;
            WorldPosition = position;

            SpawnToken = GetHashCode();
            CachedGridVersion = gridVersion;
            IsAllocated = true;
        }

        /// <summary>
        /// Resets the node, clearing all allocated data and returning it to pools.
        /// </summary>
        internal void Reset()
        {
            if (!IsAllocated)
                return;

            if (_partitions != null)
            {
                foreach (INodePartition partition in _partitions.Values)
                {
                    try
                    {
                        partition.RemoveFromNode(this);
                    }
                    catch (Exception ex)
                    {
                        GridForgeLogger.Error(
                            $"Attempting to call {nameof(partition.OnRemoveFromNode)} on {partition.GetType().Name}: {ex.Message}");
                    }
                }
                _partitions = null;
            }

            IsPartioned = false;

            if (_cachedNeighbors != null)
            {
                Pools.NodeNeighborPool.Release(_cachedNeighbors);
                _cachedNeighbors = null;
            }

            _isNeighborCacheValid = false;
            IsBoundaryNode = false;

            SpawnToken = 0;
            ScanCellKey = 0;

            ObstacleCount = 0;
            OccupantCount = 0;

            IsAllocated = false;
        }

        #endregion

        #region Partition Management

        /// <summary>
        /// Generates a unique key for a partition based on the node's spawn token and partition name.
        /// </summary>
        public int GetPartitionKey(string partitionName) => SpawnToken ^ partitionName.GetHashCode();

        /// <summary>
        /// Adds a partition to this node, allowing specialized behaviors.
        /// </summary>
        public bool TryAddPartition(INodePartition partition)
        {
            if (partition == null)
                return false;

            string partitionName = partition.GetType().Name;
            int key = GetPartitionKey(partitionName);
            _partitions ??= new SwiftDictionary<int, INodePartition>();
            if (!_partitions.Add(key, partition))
                return false;
            IsPartioned = true;

            try
            {
                partition.AddToNode(this);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"Error attempting to call {nameof(partition.OnAddToNode)} on {partitionName}: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Removes a partition from this node.
        /// </summary>
        public bool TryRemovePartition<T>()
        {
            if (!IsPartioned)
                return false;

            string partitionName = typeof(T).Name;
            int key = GetPartitionKey(partitionName);
            if (!_partitions.TryGetValue(key, out INodePartition partition))
            {
                GridForgeLogger.Warn($"Partition {partitionName} not found on this node.");
                return false;
            }

            _partitions.Remove(key);
            if (_partitions.Count == 0)
            {
                GridForgeLogger.Info($"Releasing Node's unused Partitions collection.");
                _partitions = null;
                IsPartioned = false;
            }

            try
            {
                partition.RemoveFromNode(this);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error(
                    $"Attempting to call {nameof(partition.OnRemoveFromNode)} on {partitionName}: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Retrieves a partition from the node by name.
        /// </summary>
        public bool TryGetPartition<T>(out T partition) where T : INodePartition
        {
            partition = default;

            if (!IsPartioned)
                return false;

            int key = GetPartitionKey(typeof(T).Name);
            if (!_partitions.TryGetValue(key, out INodePartition tempPartition))
                return false;

            if (tempPartition is T typedPartition)
            {
                partition = typedPartition;
                return true;
            }

            return false;
        }

        #endregion

        #region Neighbor Handling

        /// <summary>
        /// Invalidates the neighbor cache when a boundary relationship changes.
        /// </summary>
        internal void InvalidateNeighborCache() => _isNeighborCacheValid = false;

        /// <summary>
        /// Retrieves the neighbors of this node, caching results if specified.
        /// </summary>
        public IEnumerable<(LinearDirection, Node)> GetNeighbors(bool useCache = true)
        {
            if (useCache && _isNeighborCacheValid)
            {
                for (int i = 0; i < _cachedNeighbors.Length; i++)
                    yield return ((LinearDirection)i, _cachedNeighbors[i]);
            }

            SetNeighborCache();

            for (int i = 0; i < _cachedNeighbors.Length; i++)
                yield return ((LinearDirection)i, _cachedNeighbors[i]);
        }

        /// <summary>
        /// Retrieves a neighbor node in a specific direction.
        /// </summary>
        public bool TryGetNeighborFromDirection(LinearDirection direction, out Node neighbor, bool useCache = true)
        {
            neighbor = default;

            // Validate the index
            if (direction == LinearDirection.None)
                return false;

            // Check cached neighbors if caching is enabled
            if (useCache)
            {
                if (!_isNeighborCacheValid)
                    SetNeighborCache();

                neighbor = _cachedNeighbors[(int)direction];
                return neighbor != null;
            }

            (int x, int y, int z) offset = GlobalGridManager.DirectionOffsets[(int)direction];
            return TryGetNeighborFromOffset(offset, out neighbor);
        }

        /// <summary>
        /// Retrieves a neighbor node based on a coordinate offset.
        /// </summary>
        public bool TryGetNeighborFromOffset((int x, int y, int z) offset, out Node neighbor)
        {
            neighbor = default;
            if (!GlobalGridManager.TryGetGrid(GlobalCoordinates, out Grid grid))
                return false;

            CoordinatesLocal neighborCoords = new CoordinatesLocal(
                LocalCoordinates.x + offset.x,
                LocalCoordinates.y + offset.y,
                LocalCoordinates.z + offset.z
            );

            return grid.TryGetNode(neighborCoords, out neighbor);
        }

        /// <summary>
        /// Updates and caches the neighboring nodes of this node.
        /// </summary>
        private void SetNeighborCache()
        {
            _cachedNeighbors ??= Pools.NodeNeighborPool.Rent(GlobalGridManager.DirectionOffsets.Length);
            Array.Clear(_cachedNeighbors, 0, _cachedNeighbors.Length); // Ensure clean state

            for (int i = 0; i < GlobalGridManager.DirectionOffsets.Length; i++)
            {
                (int x, int y, int z) offset = GlobalGridManager.DirectionOffsets[i];
                if (TryGetNeighborFromOffset(offset, out Node neighbor))
                    _cachedNeighbors[i] = neighbor;
            }

            _isNeighborCacheValid = true;
        }

        #endregion

        #region Utility

        /// <inheritdoc/>
        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(
                GlobalCoordinates.GetHashCode(),
                WorldPosition.GetHashCode(),
                IsBoundaryNode.GetHashCode()
            );

        /// <inheritdoc/>
        public override string ToString() => GlobalCoordinates.ToString();

        /// <inheritdoc/>
        public bool Equals(Node other) => SpawnToken == other.SpawnToken;

        #endregion
    }
}