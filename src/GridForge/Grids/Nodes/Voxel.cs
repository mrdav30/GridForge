using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GridForge.Grids;

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
    public ushort GridIndex => GlobalIndex.GridIndex;

    /// <summary>
    /// The local coordinates of this voxel within its grid.
    /// </summary>
    public VoxelIndex Index => GlobalIndex.VoxelIndex;

    /// <summary>
    /// The grid-local key of the scan cell that this voxel belongs to.
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
    /// Stores a unique <see cref="BoundsKey" /> for each obstacle added to this voxel to prevent adding duplicates
    /// </summary>
    public SwiftHashSet<BoundsKey> ObstacleTracker { get; internal set; }

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
    /// Event triggered when an obstacle is added.
    /// </summary>
    private Action<ObstacleEventInfo> _onObstacleAdded;

    /// <inheritdoc cref="_onObstacleAdded"/>
    public event Action<ObstacleEventInfo> OnObstacleAdded
    {
        add => _onObstacleAdded += value;
        remove => _onObstacleAdded -= value;
    }

    /// <summary>
    /// Event triggered when an obstacle is removed.
    /// </summary>
    private Action<ObstacleEventInfo> _onObstacleRemoved;

    /// <inheritdoc cref="_onObstacleRemoved"/>
    public event Action<ObstacleEventInfo> OnObstacleRemoved
    {
        add => _onObstacleRemoved += value;
        remove => _onObstacleRemoved -= value;
    }

    /// <summary>
    /// Event triggered when all obstacles on the voxel are cleared at once.
    /// </summary>
    private Action<ObstacleClearEventInfo> _onObstaclesCleared;

    /// <inheritdoc cref="_onObstaclesCleared"/>
    public event Action<ObstacleClearEventInfo> OnObstaclesCleared
    {
        add => _onObstaclesCleared += value;
        remove => _onObstaclesCleared -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is added.
    /// </summary>
    private Action<OccupantEventInfo> _onOccupantAdded;

    /// <inheritdoc cref="_onOccupantAdded"/>
    public event Action<OccupantEventInfo> OnOccupantAdded
    {
        add => _onOccupantAdded += value;
        remove => _onOccupantAdded -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is removed.
    /// </summary>
    private Action<OccupantEventInfo> _onOccupantRemoved;

    /// <inheritdoc cref="_onOccupantRemoved"/>
    public event Action<OccupantEventInfo> OnOccupantRemoved
    {
        add => _onOccupantRemoved += value;
        remove => _onOccupantRemoved -= value;
    }

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
    internal void Reset(VoxelGrid ownerGrid = null)
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

        if (ObstacleTracker != null && ObstacleTracker.Count > 0)
        {
            ownerGrid ??= GlobalGridManager.TryGetGrid(GlobalIndex.GridIndex, out VoxelGrid grid)
                ? grid
                : null;

            if (ownerGrid == null)
            {
                if (ObstacleTracker != null)
                {
                    SwiftHashSetPool<BoundsKey>.Shared.Release(ObstacleTracker);
                    ObstacleTracker = null;
                }

                ObstacleCount = 0;
            }
            else
                ownerGrid?.ClearObstacles(this);
        }

        ObstacleTracker = null;
        ObstacleCount = 0;

        _isNeighborCacheValid = false;
        IsBoundaryVoxel = false;

        SpawnToken = 0;
        ScanCellKey = 0;

        OccupantCount = 0;
        _onObstacleAdded = null;
        _onObstacleRemoved = null;
        _onObstaclesCleared = null;
        _onOccupantAdded = null;
        _onOccupantRemoved = null;

        IsAllocated = false;
    }

    #endregion

    #region Notifications

    internal void NotifyObstacleAdded(ObstacleEventInfo eventInfo)
    {
        Action<ObstacleEventInfo> handlers = _onObstacleAdded;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<ObstacleEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Voxel {GlobalIndex}] Obstacle add error: {ex.Message}");
            }
        }
    }

    internal void NotifyObstacleRemoved(ObstacleEventInfo eventInfo)
    {
        Action<ObstacleEventInfo> handlers = _onObstacleRemoved;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<ObstacleEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Voxel {GlobalIndex}] Obstacle remove error: {ex.Message}");
            }
        }
    }

    internal void NotifyObstaclesCleared(ObstacleClearEventInfo eventInfo)
    {
        Action<ObstacleClearEventInfo> handlers = _onObstaclesCleared;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<ObstacleClearEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Voxel {GlobalIndex}] Obstacle clear error: {ex.Message}");
            }
        }
    }

    internal void NotifyOccupantAdded(OccupantEventInfo eventInfo)
    {
        Action<OccupantEventInfo> handlers = _onOccupantAdded;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<OccupantEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Voxel {GlobalIndex}] Occupant add error: {ex.Message}");
            }
        }
    }

    internal void NotifyOccupantRemoved(OccupantEventInfo eventInfo)
    {
        Action<OccupantEventInfo> handlers = _onOccupantRemoved;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<OccupantEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error($"[Voxel {GlobalIndex}] Occupant remove error: {ex.Message}");
            }
        }
    }

    #endregion

    #region Partition Management

    /// <summary>
    /// Adds a partition to this voxel, allowing specialized behaviors.
    /// </summary>
    public bool TryAddPartition(IVoxelPartition partition)
    {
        if (partition == null)
            return false;

        Type partitionType = partition.GetType();
        string partitionName = partitionType.Name;

        lock (_partitionLock)
        {
            if (!_partitionProvider.TryAdd(partitionType, partition))
                return false;
        }

        try
        {
            partition.SetParentIndex(GlobalIndex);
            partition.OnAddToVoxel(this);
            return true;
        }
        catch (Exception ex)
        {
            lock (_partitionLock)
                _partitionProvider.TryRemove(partitionType, out _);

            GridForgeLogger.Error($"Error attempting to attach partition {partitionName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a partition from this voxel.
    /// </summary>
    public bool TryRemovePartition<T>() where T : IVoxelPartition
    {
        Type partitionType = typeof(T);
        string partitionName = partitionType.Name;

        IVoxelPartition partition = null;
        lock (_partitionLock)
            _partitionProvider.TryRemove(partitionType, out partition);

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
            return _partitionProvider.Has<T>();
    }

    /// <summary>
    /// Retrieves a partition from the voxel by type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPartition<T>(out T partition) where T : IVoxelPartition
    {
        lock (_partitionLock)
            return _partitionProvider.TryGet(out partition);
    }

    /// <summary>
    /// Retrieves a partition from the voxel by type and returns null if it doesn't exist.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetPartitionOrDefault<T>() where T : class, IVoxelPartition
    {
        lock (_partitionLock)
            return _partitionProvider.TryGet(out T partition) ? partition : null;
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

            yield break;
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
        int directionIndex = (int)direction;
        if (direction == SpatialDirection.None
            || directionIndex < 0
            || directionIndex >= SpatialAwareness.DirectionOffsets.Length)
            return false;

        // Check cached neighbors if caching is enabled
        if (useCache)
        {
            if (!_isNeighborCacheValid)
                RefreshNeighborCache();

            neighbor = _cachedNeighbors[directionIndex];
            return neighbor != null;
        }

        (int x, int y, int z) offset = SpatialAwareness.DirectionOffsets[directionIndex];
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

        if (grid.TryGetVoxel(neighborCoords, out neighbor))
            return true;

        Vector3d neighborPosition = new Vector3d(
            WorldPosition.x + offset.x * GlobalGridManager.VoxelSize,
            WorldPosition.y + offset.y * GlobalGridManager.VoxelSize,
            WorldPosition.z + offset.z * GlobalGridManager.VoxelSize);

        return GlobalGridManager.TryGetVoxel(neighborPosition, out neighbor);
    }

    /// <summary>
    /// Updates and caches the neighboring voxels of this voxel.
    /// </summary>
    private void RefreshNeighborCache()
    {
        _cachedNeighbors ??= Pools.VoxelNeighborPool.Rent(SpatialAwareness.DirectionOffsets.Length);
        Array.Clear(_cachedNeighbors, 0, _cachedNeighbors.Length); // Ensure clean state

        for (int i = 0; i < SpatialAwareness.DirectionOffsets.Length; i++)
        {
            (int x, int y, int z) offset = SpatialAwareness.DirectionOffsets[i];
            if (TryGetNeighborFromOffset(offset, out Voxel neighbor))
                _cachedNeighbors[i] = neighbor;
        }

        _isNeighborCacheValid = true;
    }

    #endregion

    #region Utility

    /// <inheritdoc/>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    /// <inheritdoc/>
    public override string ToString() => GlobalIndex.ToString();

    /// <inheritdoc/>
    public bool Equals(Voxel other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override bool Equals(object obj)
    {
        return ReferenceEquals(this, obj);
    }

    #endregion
}
