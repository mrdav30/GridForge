using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace GridForge.Grids
{
    /// <summary>
    /// Represents a voxel within a 3D grid, handling spatial positioning, obstacles, occupants, and neighbor relationships.
    /// </summary>
    public class Voxel : IEquatable<Voxel>
    {
        #region Properties & Fields

        /// <summary>
        /// Unique token identifying this voxel instance.
        /// </summary>
        public int SpawnToken { get; private set; }

        /// <summary>
        /// The global and local coordinates of this voxel within the grid system.
        /// </summary>
        public GlobalVoxelIndex GlobalIndex { get; set; }

        /// <summary>
        /// The global index of the grid this voxel belongs to.
        /// </summary>
        public int GridIndex => GlobalIndex.GridIndex;

        /// <summary>
        /// The local coordinates of this voxel within its grid.
        /// </summary>
        public VoxelIndex Index => GlobalIndex.VoxelIndex;

        /// <summary>
        /// The spatial hash key of the scan cell that this voxel belongs to.
        /// </summary>
        public int ScanCellKey { get; private set; }

        /// <summary>
        /// The world-space position of this voxel.
        /// </summary>
        public Vector3d WorldPosition { get; private set; }

        /// <summary>
        /// Indicates whether the neighbor cache is valid.
        /// </summary>
        private bool _isNeighborCacheValid;

        /// <summary>
        /// Cached array of neighboring voxels for fast lookup representing a 3x3x3 linear direction grid
        /// </summary>
        /// <remarks>
        /// Unlike Grid adjacency (which is 1:many), voxels can only have 1 neighbor in any one direction (1:1).
        /// </remarks>
        private Voxel[] _cachedNeighbors;

        /// <summary>
        /// Stores a unique hash value for each obstacle added to this voxel to prevent adding duplicates
        /// </summary>
        public SwiftHashSet<int> ObstacleTracker { get; internal set; }

        /// <summary>
        /// The current number of obstacles on this voxel.
        /// </summary>
        public byte ObstacleCount { get; internal set; }

        /// <summary>
        /// The current number of occupants on this voxel.
        /// </summary>
        public byte OccupantCount { get; internal set; }

        /// <summary>
        /// Handles management of partitioned data.
        /// </summary>
        private readonly PartitionProvider<IVoxelPartition> _partitionProvider = new();

        /// <summary>
        /// Indicates whether this voxel has any active partitions.
        /// </summary>
        public bool IsPartioned => !_partitionProvider.IsEmpty;

        private readonly object _partitionLock = new();

        /// <summary>
        /// Determines if this voxel is a boundary voxel.
        /// </summary>
        public bool IsBoundaryVoxel { get; private set; }

        /// <summary>
        /// The current version of the grid at the time this voxel was created.
        /// </summary>
        public uint CachedGridVersion { get; internal set; }

        /// <summary>
        /// Indicates whether this voxel is allocated within a grid.
        /// </summary>
        public bool IsAllocated { get; private set; }

        /// <summary>
        /// Determines whether this voxel is blocked due to obstacles.
        /// </summary>
        public bool IsBlocked => IsAllocated && ObstacleCount > 0;

        /// <summary>
        /// Determines if this voxel can accept additional obstacles.
        /// </summary>
        public bool IsBlockable => IsAllocated
            && ObstacleCount < GridObstacleManager.MaxObstacleCount
            && !IsOccupied;

        /// <summary>
        /// Determines whether this voxel is occupied by entities.
        /// </summary>
        public bool IsOccupied => IsAllocated && OccupantCount > 0;

        /// <summary>
        /// Checks if this voxel has open slots for new occupants.
        /// </summary>
        public bool HasVacancy => !IsBlocked && OccupantCount < GridOccupantManager.MaxOccupantCount;

        #endregion

        #region Events

        /// <summary>
        /// Event triggered when an obstacle is added or removed.
        /// </summary>
        public Action<GridChange, Voxel> OnObstacleChange;

        /// <summary>
        /// Event triggered when an occupant is added or removed.
        /// </summary>
        public Action<GridChange, Voxel> OnOccupantChange;

        #endregion

        #region Initialization & Reset

        /// <summary>
        /// Configures the voxel with its position, grid version, and boundary status.
        /// </summary>
        internal void Initialize(
            GlobalVoxelIndex globalVoxelIndex,
            Vector3d worldPosition,
            int scanCellKey,
            bool isBoundaryVoxel,
            uint gridVersion)
        {
            ScanCellKey = scanCellKey;
            IsBoundaryVoxel = isBoundaryVoxel;

            GlobalIndex = globalVoxelIndex;
            WorldPosition = worldPosition;

            SpawnToken = GetHashCode();
            CachedGridVersion = gridVersion;
            IsAllocated = true;
        }

        /// <summary>
        /// Resets the voxel, clearing all allocated data and returning it to pools.
        /// </summary>
        internal void Reset()
        {
            if (!IsAllocated)
                return;

            if (!_partitionProvider.IsEmpty)
            {
                lock (_partitionLock)
                {
                    foreach (IVoxelPartition partition in _partitionProvider.Partitions)
                    {
                        try
                        {
                            partition.OnRemoveFromVoxel(this);
                        }
                        catch (Exception ex)
                        {
                            GridForgeLogger.Error(
                                $"Attempting to call {nameof(partition.OnRemoveFromVoxel)} on {partition.GetType().Name}: {ex.Message}");
                        }
                    }

                    _partitionProvider.Clear();
                }
            }

            if (_cachedNeighbors != null)
            {
                Pools.VoxelNeighborPool.Release(_cachedNeighbors);
                _cachedNeighbors = null;
            }

            _isNeighborCacheValid = false;
            IsBoundaryVoxel = false;

            SpawnToken = 0;
            ScanCellKey = 0;

            ObstacleCount = 0;
            OccupantCount = 0;

            IsAllocated = false;
        }

        #endregion

        #region Partition Management

        /// <summary>
        /// Generates a unique key for a partition based on the voxel's spawn token and partition name.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GeneratePartitionKey(int voxelToken, string partitionName) =>
            voxelToken ^ partitionName.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPartitionKey<T>(int voxelToken) where T : IVoxelPartition =>
            GeneratePartitionKey(voxelToken, typeof(T).Name);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPartitionKey<T>(int voxelToken, T partition) where T : IVoxelPartition =>
            GeneratePartitionKey(voxelToken, partition.GetType().Name);

        /// <summary>
        /// Adds a partition to this voxel, allowing specialized behaviors.
        /// </summary>
        public bool TryAddPartition(IVoxelPartition partition)
        {
            if (partition == null)
                return false;

            string partitionName = partition.GetType().Name;
            int key = GetPartitionKey(SpawnToken, partition);

            lock (_partitionLock)
            {
                if (!_partitionProvider.TryAdd(key, partition))
                    return false;
            }

            try
            {
                partition.OnAddToVoxel(this);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"Error attempting to call {nameof(partition.OnAddToVoxel)} on {partitionName}: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Removes a partition from this voxel.
        /// </summary>
        public bool TryRemovePartition<T>() where T : IVoxelPartition
        {
            string partitionName = typeof(T).Name;
            int key = GeneratePartitionKey(SpawnToken, partitionName);

            IVoxelPartition partition = null;
            lock (_partitionLock)
                _partitionProvider.TryRemove(key, out partition);

            if (partition == null)
            {
                GridForgeLogger.Warn($"Partition {partitionName} not found on this voxel.");
                return false;
            }

            try
            {
                partition.OnRemoveFromVoxel(this);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"Attempting to call {nameof(partition.OnRemoveFromVoxel)} on {partitionName}: {ex.Message}");
            }

            return true;
        }

        /// <summary>
        /// Checks whether or not this voxel contains a specific partition.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasPartition<T>() where T : IVoxelPartition
        {
            lock (_partitionLock)
                return _partitionProvider.Has<T>(GeneratePartitionKey(SpawnToken, typeof(T).Name));
        }

        /// <summary>
        /// Retrieves a partition from the voxel by type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetPartition<T>(out T partition) where T : IVoxelPartition
        {
            lock (_partitionLock)
                return _partitionProvider.TryGet(GeneratePartitionKey(SpawnToken, typeof(T).Name), out partition);
        }

        /// <summary>
        /// Retrieves a partition from the voxel by type and returns null if it doesn't exist.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetPartitionOrDefault<T>() where T : class, IVoxelPartition
        {
            lock (_partitionLock)
                return TryGetPartition(out T partition) ? partition : null;
        }

        #endregion

        #region Neighbor Handling

        /// <summary>
        /// Invalidates the neighbor cache when a boundary relationship changes.
        /// </summary>
        internal void InvalidateNeighborCache() => _isNeighborCacheValid = false;

        /// <summary>
        /// Retrieves the neighbors of this voxel, caching results if specified.
        /// </summary>
        public IEnumerable<(SpatialDirection, Voxel)> GetNeighbors(bool useCache = true)
        {
            if (useCache && _isNeighborCacheValid)
            {
                for (int i = 0; i < _cachedNeighbors.Length; i++)
                {
                    if (_cachedNeighbors[i] == null)
                        continue;
                    yield return ((SpatialDirection)i, _cachedNeighbors[i]);
                }
            }

            RefreshNeighborCache();

            for (int i = 0; i < _cachedNeighbors.Length; i++)
            {
                if (_cachedNeighbors[i] == null)
                    continue;
                yield return ((SpatialDirection)i, _cachedNeighbors[i]);
            }
        }

        /// <summary>
        /// Retrieves a neighbor voxel in a specific direction.
        /// </summary>
        public bool TryGetNeighborFromDirection(SpatialDirection direction, out Voxel neighbor, bool useCache = true)
        {
            neighbor = default;

            // Validate the index
            if (direction == SpatialDirection.None)
                return false;

            // Check cached neighbors if caching is enabled
            if (useCache)
            {
                if (!_isNeighborCacheValid)
                    RefreshNeighborCache();

                neighbor = _cachedNeighbors[(int)direction];
                return neighbor != null;
            }

            (int x, int y, int z) offset = GlobalGridManager.DirectionOffsets[(int)direction];
            return TryGetNeighborFromOffset(offset, out neighbor);
        }

        /// <summary>
        /// Retrieves a neighbor voxel based on a coordinate offset.
        /// </summary>
        public bool TryGetNeighborFromOffset((int x, int y, int z) offset, out Voxel neighbor)
        {
            neighbor = default;
            if (!GlobalGridManager.TryGetGrid(GlobalIndex, out VoxelGrid grid))
                return false;

            VoxelIndex neighborCoords = new VoxelIndex(
                Index.x + offset.x,
                Index.y + offset.y,
                Index.z + offset.z
            );

            return grid.TryGetVoxel(neighborCoords, out neighbor);
        }

        /// <summary>
        /// Updates and caches the neighboring voxels of this voxel.
        /// </summary>
        private void RefreshNeighborCache()
        {
            _cachedNeighbors ??= Pools.VoxelNeighborPool.Rent(GlobalGridManager.DirectionOffsets.Length);
            Array.Clear(_cachedNeighbors, 0, _cachedNeighbors.Length); // Ensure clean state

            for (int i = 0; i < GlobalGridManager.DirectionOffsets.Length; i++)
            {
                (int x, int y, int z) offset = GlobalGridManager.DirectionOffsets[i];
                if (TryGetNeighborFromOffset(offset, out Voxel neighbor))
                    _cachedNeighbors[i] = neighbor;
            }

            _isNeighborCacheValid = true;
        }

        #endregion

        #region Utility

        /// <inheritdoc/>
        public override int GetHashCode() => GlobalGridManager.GetSpawnHash(
                GlobalIndex.GetHashCode(),
                WorldPosition.GetHashCode(),
                IsBoundaryVoxel.GetHashCode()
            );

        /// <inheritdoc/>
        public override string ToString() => GlobalIndex.ToString();

        /// <inheritdoc/>
        public bool Equals(Voxel other) => SpawnToken == other.SpawnToken;

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is Voxel other && Equals(other);
        }

        #endregion
    }
}