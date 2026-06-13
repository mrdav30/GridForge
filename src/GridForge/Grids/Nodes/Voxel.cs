//=======================================================================
// Voxel.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Utility;
using System;
using System.Collections.Generic;
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
    /// The world-scoped runtime identity of this voxel within the grid system.
    /// </summary>
    public WorldVoxelIndex WorldIndex { get; set; }

    /// <summary>
    /// The world-local index of the grid this voxel belongs to.
    /// </summary>
    public ushort GridIndex => WorldIndex.GridIndex;

    /// <summary>
    /// The local coordinates of this voxel within its grid.
    /// </summary>
    public VoxelIndex Index => WorldIndex.VoxelIndex;

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
    /// Cached array of neighboring voxels for fast lookup by topology-local slot.
    /// </summary>
    /// <remarks>
    /// Unlike Grid adjacency (which is 1:many), voxels can only have 1 neighbor in any one direction (1:1).
    /// </remarks>
    private Voxel[]? _cachedNeighbors;

    /// <summary>
    /// Stores a unique <see cref="BoundsKey" /> for each obstacle added to this voxel to prevent adding duplicates
    /// </summary>
    public SwiftHashSet<BoundsKey>? ObstacleTracker { get; internal set; }

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

    internal bool HasEventSubscribers =>
        _onObstacleAdded != null
        || _onObstacleRemoved != null
        || _onObstaclesCleared != null
        || _onOccupantAdded != null
        || _onOccupantRemoved != null;

    #endregion

    #region Events

    /// <summary>
    /// Event triggered when an obstacle is added.
    /// </summary>
    private Action<ObstacleEventInfo>? _onObstacleAdded;

    /// <inheritdoc cref="_onObstacleAdded"/>
    public event Action<ObstacleEventInfo> OnObstacleAdded
    {
        add => _onObstacleAdded += value;
        remove => _onObstacleAdded -= value;
    }

    /// <summary>
    /// Event triggered when an obstacle is removed.
    /// </summary>
    private Action<ObstacleEventInfo>? _onObstacleRemoved;

    /// <inheritdoc cref="_onObstacleRemoved"/>
    public event Action<ObstacleEventInfo> OnObstacleRemoved
    {
        add => _onObstacleRemoved += value;
        remove => _onObstacleRemoved -= value;
    }

    /// <summary>
    /// Event triggered when all obstacles on the voxel are cleared at once.
    /// </summary>
    private Action<ObstacleClearEventInfo>? _onObstaclesCleared;

    /// <inheritdoc cref="_onObstaclesCleared"/>
    public event Action<ObstacleClearEventInfo> OnObstaclesCleared
    {
        add => _onObstaclesCleared += value;
        remove => _onObstaclesCleared -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is added.
    /// </summary>
    private Action<OccupantEventInfo>? _onOccupantAdded;

    /// <inheritdoc cref="_onOccupantAdded"/>
    public event Action<OccupantEventInfo> OnOccupantAdded
    {
        add => _onOccupantAdded += value;
        remove => _onOccupantAdded -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is removed.
    /// </summary>
    private Action<OccupantEventInfo>? _onOccupantRemoved;

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
        WorldVoxelIndex worldVoxelIndex,
        Vector3d worldPosition,
        int scanCellKey,
        bool isBoundaryVoxel,
        uint gridVersion)
    {
        ScanCellKey = scanCellKey;
        IsBoundaryVoxel = isBoundaryVoxel;

        WorldIndex = worldVoxelIndex;
        WorldPosition = worldPosition;

        SpawnToken = GetHashCode();
        CachedGridVersion = gridVersion;
        IsAllocated = true;
    }

    /// <summary>
    /// Resets the voxel, clearing all allocated data and returning it to pools.
    /// </summary>
    internal void Reset(VoxelGrid? ownerGrid = null)
    {
        if (!IsAllocated)
            return;

        RemovePartitions();
        ReleaseNeighborCache();
        ReleaseObstacleState(ownerGrid);
        ClearRuntimeState();
    }

    private void RemovePartitions()
    {
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
                        GridForgeLogger.Channel.Error(
                            $"Attempting to call {nameof(partition.OnRemoveFromVoxel)} on {partition.GetType().Name}: {ex.Message}");
                    }
                }

                _partitionProvider.Clear();
            }
        }
    }

    private void ReleaseNeighborCache()
    {
        if (_cachedNeighbors != null)
        {
            Pools.VoxelNeighborPool.Release(_cachedNeighbors);
            _cachedNeighbors = null;
        }
    }

    private void ReleaseObstacleState(VoxelGrid? ownerGrid)
    {
        if (ownerGrid != null && ObstacleCount > 0)
            ownerGrid.ClearObstacles(this);
        else if (ObstacleTracker != null)
            SwiftHashSetPool<BoundsKey>.Shared.Release(ObstacleTracker);

        ObstacleTracker = null;
        ObstacleCount = 0;
    }

    private void ClearRuntimeState()
    {
        _isNeighborCacheValid = false;
        IsBoundaryVoxel = false;

        SpawnToken = 0;
        ScanCellKey = 0;
        WorldIndex = default;

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
        Action<ObstacleEventInfo>? handlers = _onObstacleAdded;
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
                GridForgeLogger.Channel.Error($"[Voxel {WorldIndex}] Obstacle add error: {ex.Message}");
            }
        }
    }

    internal void NotifyObstacleRemoved(ObstacleEventInfo eventInfo)
    {
        Action<ObstacleEventInfo>? handlers = _onObstacleRemoved;
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
                GridForgeLogger.Channel.Error($"[Voxel {WorldIndex}] Obstacle remove error: {ex.Message}");
            }
        }
    }

    internal void NotifyObstaclesCleared(ObstacleClearEventInfo eventInfo)
    {
        Action<ObstacleClearEventInfo>? handlers = _onObstaclesCleared;
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
                GridForgeLogger.Channel.Error($"[Voxel {WorldIndex}] Obstacle clear error: {ex.Message}");
            }
        }
    }

    internal void NotifyOccupantAdded(OccupantEventInfo eventInfo)
    {
        Action<OccupantEventInfo>? handlers = _onOccupantAdded;
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
                GridForgeLogger.Channel.Error($"[Voxel {WorldIndex}] Occupant add error: {ex.Message}");
            }
        }
    }

    internal void NotifyOccupantRemoved(OccupantEventInfo eventInfo)
    {
        Action<OccupantEventInfo>? handlers = _onOccupantRemoved;
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
                GridForgeLogger.Channel.Error($"[Voxel {WorldIndex}] Occupant remove error: {ex.Message}");
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
            partition.SetParentIndex(WorldIndex);
            partition.OnAddToVoxel(this);
            return true;
        }
        catch (Exception ex)
        {
            lock (_partitionLock)
                _partitionProvider.TryRemove(partitionType, out _);

            GridForgeLogger.Channel.Error($"Error attempting to attach partition {partitionName}: {ex.Message}");
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

        IVoxelPartition? partition = null;
        lock (_partitionLock)
            _partitionProvider.TryRemove(partitionType, out partition);

        if (partition == null)
        {
            GridForgeLogger.Channel.Warn($"Partition {partitionName} not found on this voxel.");
            return false;
        }

        try
        {
            partition.OnRemoveFromVoxel(this);
        }
        catch (Exception ex)
        {
            GridForgeLogger.Channel.Error($"Attempting to call {nameof(partition.OnRemoveFromVoxel)} on {partitionName}: {ex.Message}");
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
    public bool TryGetPartition<T>(out T? partition) where T : IVoxelPartition
    {
        lock (_partitionLock)
            return _partitionProvider.TryGet(out partition);
    }

    /// <summary>
    /// Retrieves a partition from the voxel by type and returns null if it doesn't exist.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetPartitionOrDefault<T>() where T : class, IVoxelPartition
    {
        lock (_partitionLock)
            return _partitionProvider?.TryGet(out T? partition) ?? false
                ? partition
                : null;
    }

    #endregion

    #region Neighbor Handling

    /// <summary>
    /// Invalidates the neighbor cache when a boundary relationship changes.
    /// </summary>
    internal void InvalidateNeighborCache() => _isNeighborCacheValid = false;

    /// <summary>
    /// Retrieves rectangular-prism neighbors of this voxel in deterministic <see cref="RectangularDirection"/> order.
    /// </summary>
    public IEnumerable<(RectangularDirection Direction, Voxel Voxel)> GetRectangularNeighbors(
        VoxelGrid ownerGrid,
        bool useCache = true)
    {
        if (!IsValidOwnerGrid(ownerGrid) || ownerGrid.TopologyKind != GridTopologyKind.RectangularPrism)
            yield break;

        if (useCache)
            EnsureNeighborCache(ownerGrid);

        for (int slot = 0; slot < ownerGrid.NeighborSlotCount; slot++)
        {
            Voxel? neighbor;
            if (useCache)
            {
                neighbor = _cachedNeighbors![slot];
                if (neighbor == null)
                    continue;
            }
            else if (!TryGetNeighborFromSlot(ownerGrid, slot, out neighbor, useCache: false))
            {
                continue;
            }

            yield return ((RectangularDirection)slot, neighbor!);
        }
    }

    /// <summary>
    /// Retrieves hex-prism neighbors of this voxel in deterministic <see cref="HexDirection"/> order.
    /// </summary>
    public IEnumerable<(HexDirection Direction, Voxel Voxel)> GetHexNeighbors(
        VoxelGrid ownerGrid,
        bool useCache = true)
    {
        if (!IsValidOwnerGrid(ownerGrid) || ownerGrid.TopologyKind != GridTopologyKind.HexPrism)
            yield break;

        if (useCache)
            EnsureNeighborCache(ownerGrid);

        for (int slot = 0; slot < ownerGrid.NeighborSlotCount; slot++)
        {
            Voxel? neighbor;
            if (useCache)
            {
                neighbor = _cachedNeighbors![slot];
                if (neighbor == null)
                    continue;
            }
            else if (!TryGetNeighborFromSlot(ownerGrid, slot, out neighbor, useCache: false))
            {
                continue;
            }

            yield return ((HexDirection)slot, neighbor!);
        }
    }

    /// <summary>
    /// Retrieves the rectangular-prism neighbor voxel in the supplied direction.
    /// </summary>
    public bool TryGetRectangularNeighbor(
        VoxelGrid ownerGrid,
        RectangularDirection direction,
        out Voxel? neighbor,
        bool useCache = true)
    {
        neighbor = null;
        return IsValidOwnerGrid(ownerGrid)
            && ownerGrid.TryGetNeighborSlot(direction, out int slot)
            && TryGetNeighborFromSlot(ownerGrid, slot, out neighbor, useCache);
    }

    /// <summary>
    /// Retrieves the hex-prism neighbor voxel in the supplied direction.
    /// </summary>
    public bool TryGetHexNeighbor(
        VoxelGrid ownerGrid,
        HexDirection direction,
        out Voxel? neighbor,
        bool useCache = true)
    {
        neighbor = null;
        return IsValidOwnerGrid(ownerGrid)
            && ownerGrid.TryGetNeighborSlot(direction, out int slot)
            && TryGetNeighborFromSlot(ownerGrid, slot, out neighbor, useCache);
    }

    /// <summary>
    /// Fills caller-owned storage with mixed-topology neighboring voxels whose world-space
    /// voxel footprints touch this voxel's footprint.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="results">Caller-owned storage cleared and filled with mixed-topology contacts.</param>
    /// <param name="tolerance">Optional fixed-point tolerance applied to footprint contact checks.</param>
    public void GetMixedTopologyNeighborsInto(
        VoxelGrid ownerGrid,
        SwiftList<Voxel> results,
        Fixed64? tolerance = null)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (!IsValidOwnerGrid(ownerGrid))
            return;

        MixedTopologyNeighborResolver.AddNeighbors(this, ownerGrid, results, tolerance);
    }

    /// <summary>
    /// Determines whether this voxel has at least one mixed-topology neighboring voxel.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="tolerance">Optional fixed-point tolerance applied to footprint contact checks.</param>
    /// <returns>True when a mixed-topology contact exists; otherwise false.</returns>
    public bool HasMixedTopologyNeighbor(
        VoxelGrid ownerGrid,
        Fixed64? tolerance = null) =>
        IsValidOwnerGrid(ownerGrid)
        && MixedTopologyNeighborResolver.HasNeighbor(this, ownerGrid, tolerance);

    private bool TryGetNeighborFromSlot(
        VoxelGrid ownerGrid,
        int slot,
        out Voxel? neighbor,
        bool useCache)
    {
        neighbor = null;
        if ((uint)slot >= (uint)ownerGrid.NeighborSlotCount)
            return false;

        if (useCache)
        {
            EnsureNeighborCache(ownerGrid);
            neighbor = _cachedNeighbors![slot];
            return neighbor != null;
        }

        return TryResolveNeighborAtOffset(ownerGrid, ToTuple(ownerGrid.GetNeighborOffset(slot)), out neighbor);
    }

    private bool TryResolveNeighborAtOffset(
        VoxelGrid ownerGrid,
        (int x, int y, int z) offset,
        out Voxel? neighbor)
    {
        neighbor = null;
        if (!IsValidOwnerGrid(ownerGrid))
            return false;

        VoxelIndex neighborCoords = new(
            Index.x + offset.x,
            Index.y + offset.y,
            Index.z + offset.z
        );

        if (ownerGrid.TryGetVoxel(neighborCoords, out neighbor))
            return true;

        return ownerGrid.World!.TryGetVoxel(GetNeighborWorldPosition(ownerGrid, offset), out neighbor);
    }

    private bool IsValidOwnerGrid(VoxelGrid? ownerGrid)
    {
        return ownerGrid != null
            && ownerGrid.IsActive
            && ownerGrid.GridIndex == WorldIndex.GridIndex
            && ownerGrid.SpawnToken == WorldIndex.GridSpawnToken
            && ownerGrid.World != null
            && ownerGrid.World.SpawnToken == WorldIndex.WorldSpawnToken;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3d GetNeighborWorldPosition(VoxelGrid ownerGrid, (int x, int y, int z) offset) =>
         WorldPosition + ownerGrid.GetWorldOffset(offset);

    private void EnsureNeighborCache(VoxelGrid ownerGrid)
    {
        if (!_isNeighborCacheValid || _cachedNeighbors == null || _cachedNeighbors.Length != ownerGrid.NeighborSlotCount)
            RefreshNeighborCache(ownerGrid);
    }

    /// <summary>
    /// Updates and caches the neighboring voxels of this voxel.
    /// </summary>
    private void RefreshNeighborCache(VoxelGrid ownerGrid)
    {
        int slotCount = ownerGrid.NeighborSlotCount;
        if (_cachedNeighbors == null || _cachedNeighbors.Length != slotCount)
        {
            ReleaseNeighborCache();
            _cachedNeighbors = Pools.VoxelNeighborPool.Rent(slotCount);
        }

        Array.Clear(_cachedNeighbors, 0, _cachedNeighbors.Length); // Ensure clean state

        for (int slot = 0; slot < slotCount; slot++)
        {
            if (TryResolveNeighborAtOffset(ownerGrid, ToTuple(ownerGrid.GetNeighborOffset(slot)), out Voxel? neighbor))
            {
                _cachedNeighbors[slot] = neighbor!;
            }
        }

        _isNeighborCacheValid = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int x, int y, int z) ToTuple(VoxelIndex offset) => (offset.x, offset.y, offset.z);

    #endregion

    #region Utility

    /// <inheritdoc/>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    /// <inheritdoc/>
    public override string ToString() => WorldIndex.ToString();

    /// <inheritdoc/>
    public bool Equals(Voxel? other) => ReferenceEquals(this, other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    #endregion
}
