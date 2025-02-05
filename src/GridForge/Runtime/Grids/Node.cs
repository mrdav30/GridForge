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
    public class Node
    {
        #region Constants

        /// <summary>
        /// Maximum number of obstacles that can exist on a single node.
        /// </summary>
        public const byte MaxObstacleCount = byte.MaxValue - 1;

        /// <summary>
        /// Maximum number of occupants that can exist on a single node.
        /// </summary>
        public const byte MaxOccupantCount = byte.MaxValue - 1;

        #endregion

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
        public CoordinatesLocal Coordinates => GlobalCoordinates.NodeCoordinates;

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
        /// Cached array of neighboring nodes for fast lookup.
        /// </summary>
        private Node[] _cachedNeighbors;

        /// <summary>
        /// The current number of obstacles on this node.
        /// </summary>
        public byte ObstacleCount { get; private set; }

        /// <summary>
        /// The current number of occupants on this node.
        /// </summary>
        public byte OccupantCount { get; private set; }

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
        /// Event triggered when an obstacle is added or removed from this node.
        /// </summary>
        public Action<GridChange> OnObstacleChange;

        /// <summary>
        /// Event triggered when an occupant is added or removed from this node.
        /// </summary>
        public Action<GridChange> OnOccupantChange;

        /// <summary>
        /// The current version of the grid at the time this node was created.
        /// </summary>
        public uint GridVersion { get; private set; }

        /// <summary>
        /// Indicates whether this node is allocated within a grid.
        /// </summary>
        public bool IsAllocated { get; private set; }

        /// <summary>
        /// Determines whether this node is blocked due to obstacles.
        /// </summary>
        public bool IsBlocked => !IsAllocated || ObstacleCount > 0;

        /// <summary>
        /// Determines whether this node is occupied by entities.
        /// </summary>
        public bool IsOccupied => IsAllocated && OccupantCount > 0;

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Configures the node with its position, grid version, and boundary status.
        /// </summary>
        public void Initialize(CoordinatesGlobal coordinates, Vector3d position, uint gridVersion, bool isBoundaryNode)
        {
            if (!GlobalGridManager.GetGrid(coordinates, out Grid grid))
                return;

            ScanCellKey = grid.GetScanCellKey(coordinates.NodeCoordinates);
            GlobalCoordinates = coordinates;
            WorldPosition = position;

            if (isBoundaryNode)
            {
                IsBoundaryNode = isBoundaryNode;
                grid.OnBoundaryChange += InvalidateNeighborCache;
            }

            SpawnToken = GetHashCode();

            GridVersion = gridVersion;

            IsAllocated = true;
        }

        /// <summary>
        /// Resets the node, clearing all allocated data and returning it to pools.
        /// </summary>
        public void Reset()
        {
            if (!IsAllocated)
                return;

            if (_partitions != null)
            {
                foreach (INodePartition partition in _partitions.Values)
                    partition.Reset();
                Pools.PartitionPool.Release(_partitions);
                _partitions = null;
            }

            IsPartioned = false;

            if (_cachedNeighbors != null)
            {
                Pools.NodeNeighborPool.Release(_cachedNeighbors);
                _cachedNeighbors = null;
            }

            _isNeighborCacheValid = false;

            if (IsBoundaryNode)
            {
                if (GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid))
                    grid.OnBoundaryChange -= InvalidateNeighborCache;
                else
                    Console.WriteLine($"Unable to find corresponding grid at GlobalGridIndex {GlobalCoordinates.GridIndex}");
            }

            SpawnToken = 0;
            ScanCellKey = 0;

            ObstacleCount = 0;
            OccupantCount = 0;

            IsAllocated = false;
        }

        #endregion

        #region Partition Management

        /// <summary>
        /// Adds a partition to this node, allowing specialized behaviors.
        /// </summary>
        public bool AddPartition(string name, INodePartition partition)
        {
            if (string.IsNullOrEmpty(name) || partition == null)
                return false;

            _partitions ??= Pools.PartitionPool.Rent();

            int key = GetPartitionKey(name);
            if (_partitions.ContainsKey(key))
            {
                Console.WriteLine($"Partition ({name}) has already been added to Grid Node at ({Coordinates}).");
                return false;
            }

            partition.Setup(GlobalCoordinates);

            IsPartioned = true;

            return _partitions.Add(key, partition);
        }

        /// <summary>
        /// Removes a partition from this node.
        /// </summary>
        public bool RemovePartition(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsPartioned)
                return false;

            int key = GetPartitionKey(name);
            if (!_partitions.TryGetValue(key, out INodePartition partition))
            {
                Console.WriteLine($"Partition {name} not found on this node.");
                return false;
            }

            partition.Reset();
            _partitions.Remove(key);

            if (_partitions.Count == 0)
            {
                Console.WriteLine($"Releasing GridNode's unused Partitions collection.");
                Pools.PartitionPool.Release(_partitions);
                _partitions = null;
                IsPartioned = false;
            }

            return true;
        }

        /// <summary>
        /// Checks if this node has a specific type of partition.
        /// </summary>
        public bool IsPartitioned<T>(string name) where T : INodePartition
        {
            if (!IsPartioned)
                return false;
            int key = GetPartitionKey(name);
            return _partitions.TryGetValue(key, out INodePartition part) && part is T;
        }

        /// <summary>
        /// Retrieves a partition from the node by name.
        /// </summary>
        public bool GetPartition<T>(string name, out T partition) where T : INodePartition
        {
            partition = default;

            if (string.IsNullOrEmpty(name) || !IsPartioned)
                return false;

            int key = GetPartitionKey(name);
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

        #region Obstacle & Occupant Management

        /// <summary>
        /// Adds an obstacle to this node.
        /// </summary>
        public bool AddObstacle()
        {
            if (ObstacleCount == MaxObstacleCount)
            {
                Console.WriteLine($"Too many obstacles on node ({Coordinates}).");
                return false;
            }

            ObstacleCount++;

            NotifyObstacleChange(GridChange.AddNodeObstacle);

            return true;
        }

        /// <summary>
        /// Removes an obstacle from this node.
        /// </summary>
        public bool RemoveObstacle()
        {
            if (ObstacleCount == 0)
            {
                Console.WriteLine($"No obstacle to remove on this node ({Coordinates})!");
                return false;
            }

            ObstacleCount--;

            NotifyObstacleChange(GridChange.RemoveNodeObstacle);

            return true;
        }

        /// <summary>
        /// Notifies the system when an obstacle state changes.
        /// </summary>
        private void NotifyObstacleChange(GridChange changeType)
        {
            if (!GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid))
                return;

            grid.NotifyGridVersionChange();

            try
            {
                OnObstacleChange?.Invoke(changeType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during obstacle change notification: {ex.Message}");
            }

            GridVersion = grid.Version;
        }

        /// <summary>
        /// Adds an occupant to this node.
        /// </summary>
        public bool AddOccupant(INodeOccupant occupant)
        {
            if (occupant == null || IsBlocked)
                return false;

            if (OccupantCount == MaxOccupantCount)
            {
                Console.WriteLine($"Too many occupants on node ({Coordinates}.)");
                return false;
            }

            if (!GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid) || !grid.RegisterActiveScanCell(ScanCellKey))
                return false;

            if (!grid.GetScanCell(ScanCellKey, out ScanCell scanCell))
                return false;

            occupant.OccupantTicket = scanCell.AddOccupant(SpawnToken, occupant);
            occupant.GridCoordinates = GlobalCoordinates;

            OccupantCount++;
            NotifyOccupantChange(GridChange.AddNodeOccupant);

            return true;
        }

        /// <summary>
        /// Removes an occupant from this node.
        /// </summary>
        public bool RemoveOccupant(INodeOccupant occupant)
        {
            if (occupant == null || !IsOccupied)
            {
                Console.WriteLine($"No occupants to remove on this node ({Coordinates})!");
                return false;
            }

            if (!GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid) || !grid.GetScanCell(ScanCellKey, out ScanCell scanCell))
                return false;

            if (!scanCell.RemoveOccupant(SpawnToken, occupant.OccupantTicket, occupant.ClusterKey))
                return false;

            if (!scanCell.IsOccupied)
                grid.UnregisterActiveScanCell(ScanCellKey);

            occupant.OccupantTicket = -1;
            occupant.GridCoordinates = default;

            OccupantCount--;
            NotifyOccupantChange(GridChange.RemoveNodeOccupant);

            return true;
        }

        /// <summary>
        /// Notifies the system when an occupant state changes.
        /// </summary>
        private void NotifyOccupantChange(GridChange changeType)
        {
            try
            {
                // Notify all registered event listeners of the occupant change
                OnOccupantChange?.Invoke(changeType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during occupant change notification: {ex.Message}");
            }
        }

        #endregion

        #region Neighbor Handling

        /// <summary>
        /// Invalidates the neighbor cache when a boundary relationship changes.
        /// </summary>
        public void InvalidateNeighborCache(GridChange change, LinearDirection direction)
        {
            if (change != GridChange.RemoveNodeObstacle || change != GridChange.AddNodeObstacle)
                return;

            if (!IsFacingDirection(direction))
                return;

            _isNeighborCacheValid = false;
        }

        /// <summary>
        /// Determines if this node is facing a specific boundary direction.
        /// A node is considered to be "facing" a boundary if it is at the grid edge
        /// in the given direction, including diagonal boundaries.
        /// </summary>
        /// <param name="direction">The direction to check.</param>
        /// <returns>True if the node is at the edge in the given direction, otherwise false.</returns>
        public bool IsFacingDirection(LinearDirection direction)
        {
            if (!GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid))
                return false;

            return direction switch
            {
                // Principal directions (cardinal)
                LinearDirection.West => Coordinates.x == 0,
                LinearDirection.East => Coordinates.x == grid.Width - 1,
                LinearDirection.North => Coordinates.z == grid.Length - 1,
                LinearDirection.South => Coordinates.z == 0,
                LinearDirection.Above => Coordinates.y == grid.Height - 1,
                LinearDirection.Below => Coordinates.y == 0,

                // Diagonal XY-plane
                LinearDirection.NorthWest => Coordinates.x == 0 && Coordinates.z == grid.Length - 1,
                LinearDirection.NorthEast => Coordinates.x == grid.Width - 1 && Coordinates.z == grid.Length - 1,
                LinearDirection.SouthWest => Coordinates.x == 0 && Coordinates.z == 0,
                LinearDirection.SouthEast => Coordinates.x == grid.Width - 1 && Coordinates.z == 0,

                // Diagonal XZ-plane (Above & Below variants)
                LinearDirection.AboveNorth => Coordinates.y == grid.Height - 1 && Coordinates.z == grid.Length - 1,
                LinearDirection.AboveSouth => Coordinates.y == grid.Height - 1 && Coordinates.z == 0,
                LinearDirection.AboveWest => Coordinates.y == grid.Height - 1 && Coordinates.x == 0,
                LinearDirection.AboveEast => Coordinates.y == grid.Height - 1 && Coordinates.x == grid.Width - 1,

                LinearDirection.BelowNorth => Coordinates.y == 0 && Coordinates.z == grid.Length - 1,
                LinearDirection.BelowSouth => Coordinates.y == 0 && Coordinates.z == 0,
                LinearDirection.BelowWest => Coordinates.y == 0 && Coordinates.x == 0,
                LinearDirection.BelowEast => Coordinates.y == 0 && Coordinates.x == grid.Width - 1,

                // Diagonal 3D Corners
                LinearDirection.AboveNorthWest => Coordinates.x == 0 
                    && Coordinates.z == grid.Length - 1 
                    && Coordinates.y == grid.Height - 1,
                LinearDirection.AboveNorthEast => Coordinates.x == grid.Width - 1
                    && Coordinates.z == grid.Length - 1 
                    && Coordinates.y == grid.Height - 1,
                LinearDirection.AboveSouthWest => Coordinates.x == 0 
                    && Coordinates.z == 0 
                    && Coordinates.y == grid.Height - 1,
                LinearDirection.AboveSouthEast => Coordinates.x == grid.Width - 1 
                    && Coordinates.z == 0 
                    && Coordinates.y == grid.Height - 1,

                LinearDirection.BelowNorthWest => Coordinates.x == 0 
                    && Coordinates.z == grid.Length - 1 
                    && Coordinates.y == 0,
                LinearDirection.BelowNorthEast => Coordinates.x == grid.Width - 1 
                    && Coordinates.z == grid.Length - 1 
                    && Coordinates.y == 0,
                LinearDirection.BelowSouthWest => Coordinates.x == 0 
                    && Coordinates.z == 0 
                    && Coordinates.y == 0,
                LinearDirection.BelowSouthEast => Coordinates.x == grid.Width - 1 
                    && Coordinates.z == 0 
                    && Coordinates.y == 0,

                // No direction (should never be true)
                LinearDirection.None => false,

                // Catch-all (should never reach this)
                _ => false
            };
        }

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
        public bool GetNeighborFromDirection(LinearDirection direction, out Node neighbor, bool useCache = true)
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
            return GetNeighborFromOffset(offset, out neighbor);
        }

        public bool GetNeighborFromOffset((int x, int y, int z) offset, out Node neighbor)
        {
            neighbor = default;
            if (!GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid))
                return false;

            CoordinatesLocal neighborCoords = new CoordinatesLocal(
                Coordinates.x + offset.x,
                Coordinates.y + offset.y,
                Coordinates.z + offset.z
            );

            return grid.GetNode(neighborCoords, out neighbor);
        }

        private void SetNeighborCache()
        {
            _cachedNeighbors ??= Pools.NodeNeighborPool.Rent(GlobalGridManager.DirectionOffsets.Length);
            Array.Clear(_cachedNeighbors, 0, _cachedNeighbors.Length); // Ensure clean state

            for (int i = 0; i < GlobalGridManager.DirectionOffsets.Length; i++)
            {
                (int x, int y, int z) offset = GlobalGridManager.DirectionOffsets[i];
                if (GetNeighborFromOffset(offset, out Node neighbor))
                    _cachedNeighbors[i] = neighbor;
            }

            _isNeighborCacheValid = true;
        }

        #endregion

        #region ScanCell Interaction

        /// <summary>
        /// Retrieves the scan cell associated with this node.
        /// </summary>
        public bool GetScanCell(out ScanCell scanCell)
        {
            scanCell = null;
            if (!GlobalGridManager.GetGrid(GlobalCoordinates, out Grid grid))
                return false;

            if (!grid.GetScanCell(ScanCellKey, out scanCell))
                return false;

            return true;
        }

        /// <summary>
        /// Retrieves all occupants of this node.
        /// </summary>
        public IEnumerable<INodeOccupant> GetNodeOccupants()
        {
            if (!IsOccupied || !GetScanCell(out ScanCell scanCell) || !scanCell.IsOccupied)
                yield break;

            foreach (INodeOccupant occupant in scanCell.GetOccupantsFor(SpawnToken))
                yield return occupant;
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at this node.
        /// </summary>
        public IEnumerable<T> GetNodeOccupantsByType<T>() where T : INodeOccupant
        {
            foreach (INodeOccupant occupant in GetNodeOccupants())
            {
                if (occupant is T typedOccupant)
                    yield return typedOccupant;
            }
        }

        public int GetPartitionKey(string partitionName) => SpawnToken ^ partitionName.GetHashCode();

        #endregion

        #region Utility

        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(
                GlobalCoordinates.GetHashCode(),
                WorldPosition.GetHashCode(),
                IsBoundaryNode.GetHashCode()
            );

        public override string ToString() => Coordinates.ToString();

        #endregion
    }
}