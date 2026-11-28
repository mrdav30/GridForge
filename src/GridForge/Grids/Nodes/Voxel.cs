//=======================================================================
// Voxel.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;

namespace GridForge.Grids;

/// <summary>
/// Represents a voxel within a 3D grid, handling spatial positioning, obstacles, occupants, and neighbor relationships.
/// </summary>
public class Voxel : IEquatable<Voxel>
{
    #region Properties & Fields

    /// <summary>
    /// The world-scoped runtime identity of this voxel within the grid system.
    /// </summary>
    /// <value>The world-scoped runtime identity of this voxel within the grid system.</value>
    /// <remarks>Assigned by grid initialization. Changing this public value manually does not move the voxel or update grid, scan-cell, or registration bookkeeping.</remarks>
    public WorldVoxelIndex WorldIndex { get; set; }

    /// <summary>
    /// The world-local index of the grid this voxel belongs to.
    /// </summary>
    /// <value>The world-local index of the grid this voxel belongs to.</value>
    public ushort GridIndex => WorldIndex.GridIndex;

    /// <summary>
    /// The local coordinates of this voxel within its grid.
    /// </summary>
    /// <value>The local coordinates of this voxel within its grid.</value>
    public VoxelIndex Index => WorldIndex.VoxelIndex;

    /// <summary>
    /// The grid-local key of the scan cell that this voxel belongs to.
    /// </summary>
    /// <value>The grid-local key of the scan cell that this voxel belongs to.</value>
    public int ScanCellKey { get; private set; }

    /// <summary>
    /// The world-space position of this voxel.
    /// </summary>
    /// <value>The world-space position of this voxel.</value>
    public Vector3d WorldPosition { get; private set; }

    /// <summary>
    /// Stores the process-unique identity of each obstacle registration added to this voxel.
    /// </summary>
    /// <value>A borrowed pooled token set, or null when no obstacles remain. Inspect only; the last removal, obstacle clear, or voxel reset returns this set to its pool. Do not retain or mutate it.</value>
    public SwiftHashSet<ObstacleToken>? ObstacleTracker { get; internal set; }

    /// <summary>
    /// The current number of obstacles on this voxel.
    /// </summary>
    /// <value>The number of distinct obstacle-token attachments on this voxel, from zero through 255; stacked blockers count separately.</value>
    public byte ObstacleCount { get; internal set; }

    /// <summary>
    /// The current number of occupants on this voxel.
    /// </summary>
    /// <value>The current registration count on this voxel, from zero through 255. Registrations in different voxels are counted separately even for the same occupant reference.</value>
    public byte OccupantCount { get; internal set; }

    /// <summary>
    /// Holds partitions and serves as their private per-voxel monitor.
    /// </summary>
    private readonly PartitionProvider<IVoxelPartition> _partitionProvider = new();

    /// <summary>
    /// Indicates whether this voxel has any active partitions.
    /// </summary>
    /// <value>Indicates whether this voxel has any active partitions.</value>
    public bool IsPartioned => !_partitionProvider.IsEmpty;

    // Zero for initial grid storage. A later sparse insertion uses its world-local committed stamp.
    internal ulong MaterializationSequence { get; private set; }

    /// <summary>
    /// Determines if this voxel is a boundary voxel.
    /// </summary>
    /// <value>Determines if this voxel is a boundary voxel.</value>
    public bool IsBoundaryVoxel { get; private set; }

    /// <summary>
    /// The current version of the grid at the time this voxel was created.
    /// </summary>
    /// <value>The current version of the grid at the time this voxel was created.</value>
    public uint CachedGridVersion { get; internal set; }

    /// <summary>
    /// Indicates whether this voxel is allocated within a grid.
    /// </summary>
    /// <value>Indicates whether this voxel is allocated within a grid.</value>
    public bool IsAllocated { get; private set; }

    /// <summary>
    /// Determines whether this voxel is blocked due to obstacles.
    /// </summary>
    /// <value>Determines whether this voxel is blocked due to obstacles.</value>
    public bool IsBlocked => IsAllocated && ObstacleCount > 0;

    /// <summary>
    /// Determines if this voxel can accept additional obstacles.
    /// </summary>
    /// <value>Determines if this voxel can accept additional obstacles.</value>
    public bool IsBlockable => IsAllocated
        && ObstacleCount < GridObstacleManager.MaxObstacleCount
        && !IsOccupied;

    /// <summary>
    /// Determines whether this voxel is occupied by entities.
    /// </summary>
    /// <value>Determines whether this voxel is occupied by entities.</value>
    public bool IsOccupied => IsAllocated && OccupantCount > 0;

    /// <summary>
    /// Checks if this voxel has open slots for new occupants.
    /// </summary>
    /// <value>Checks if this voxel has open slots for new occupants.</value>
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

    /// <summary>Raised when an obstacle registration is added.</summary>
    /// <remarks>Handlers run synchronously after the voxel state change. Handler failures are logged and remaining subscribers are still invoked. Subscriptions belong to this pooled voxel lifetime and are cleared on reset.</remarks>
    public event Action<ObstacleEventInfo> OnObstacleAdded
    {
        add => _onObstacleAdded += value;
        remove => _onObstacleAdded -= value;
    }

    /// <summary>
    /// Event triggered when an obstacle is removed.
    /// </summary>
    private Action<ObstacleEventInfo>? _onObstacleRemoved;

    /// <summary>Raised when an obstacle registration is removed.</summary>
    /// <remarks>Handlers run synchronously after the voxel state change. Handler failures are logged and remaining subscribers are still invoked. Subscriptions belong to this pooled voxel lifetime and are cleared on reset.</remarks>
    public event Action<ObstacleEventInfo> OnObstacleRemoved
    {
        add => _onObstacleRemoved += value;
        remove => _onObstacleRemoved -= value;
    }

    /// <summary>
    /// Event triggered when all obstacles on the voxel are cleared at once.
    /// </summary>
    private Action<ObstacleClearEventInfo>? _onObstaclesCleared;

    /// <summary>Raised when all obstacle registrations on a voxel are cleared.</summary>
    /// <remarks>Handlers run synchronously after the voxel state change. Handler failures are logged and remaining subscribers are still invoked. Subscriptions belong to this pooled voxel lifetime and are cleared on reset.</remarks>
    public event Action<ObstacleClearEventInfo> OnObstaclesCleared
    {
        add => _onObstaclesCleared += value;
        remove => _onObstaclesCleared -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is added.
    /// </summary>
    private Action<OccupantEventInfo>? _onOccupantAdded;

    /// <summary>Raised when an occupant registration is added.</summary>
    /// <remarks>Handlers run synchronously after the voxel state change. Handler failures are logged and remaining subscribers are still invoked. Subscriptions belong to this pooled voxel lifetime and are cleared on reset.</remarks>
    public event Action<OccupantEventInfo> OnOccupantAdded
    {
        add => _onOccupantAdded += value;
        remove => _onOccupantAdded -= value;
    }

    /// <summary>
    /// Event triggered when an occupant is removed.
    /// </summary>
    private Action<OccupantEventInfo>? _onOccupantRemoved;

    /// <summary>Raised when an occupant registration is removed.</summary>
    /// <remarks>Handlers run synchronously after the voxel state change. Handler failures are logged and remaining subscribers are still invoked. Subscriptions belong to this pooled voxel lifetime and are cleared on reset.</remarks>
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
        uint gridVersion,
        ulong materializationSequence = 0)
    {
        ScanCellKey = scanCellKey;
        IsBoundaryVoxel = isBoundaryVoxel;

        MaterializationSequence = materializationSequence;
        WorldIndex = worldVoxelIndex;
        WorldPosition = worldPosition;

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
        ReleaseObstacleState(ownerGrid);
        ClearRuntimeState();
    }

    private void RemovePartitions()
    {
        if (!_partitionProvider.IsEmpty)
        {
            lock (_partitionProvider)
            {
                PartitionProvider<IVoxelPartition>.Enumerator partitions = _partitionProvider.GetEnumerator();
                while (partitions.MoveNext())
                {
                    IVoxelPartition partition = partitions.Current;
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

    private void ReleaseObstacleState(VoxelGrid? ownerGrid)
    {
        if (ownerGrid != null && ObstacleCount > 0 && !ownerGrid.SuppressTeardownObstacleClears)
            GridObstacleManager.ClearObstacles(ownerGrid, this, detached: ownerGrid.RemovalWorldToken != 0);
        else if (ObstacleTracker != null)
            SwiftHashSetPool<ObstacleToken>.Shared.Release(ObstacleTracker);

        ObstacleTracker = null;
        ObstacleCount = 0;
    }

    private void ClearRuntimeState()
    {
        IsBoundaryVoxel = false;

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

    // Read the delegate before checking the address and birth stamp so reuse cannot substitute new handlers.
    private bool CanNotifyLocal([NotNullWhen(true)] Delegate? handlers, WorldVoxelIndex index, ulong observedSequence) =>
        IsAllocated && handlers != null && WorldIndex == index && MaterializationSequence <= observedSequence;

    internal void NotifyObstacleAdded(ObstacleEventInfo eventInfo)
    {
        Action<ObstacleEventInfo>? handlers = _onObstacleAdded;
        if (!CanNotifyLocal(handlers, eventInfo.VoxelIndex, eventInfo.ChangeSequence))
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
                GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Obstacle add error: {ex.Message}");
            }
        }
    }

    internal void NotifyObstacleRemoved(ObstacleEventInfo eventInfo)
    {
        Action<ObstacleEventInfo>? handlers = _onObstacleRemoved;
        if (!CanNotifyLocal(handlers, eventInfo.VoxelIndex, eventInfo.ChangeSequence))
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
                GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Obstacle remove error: {ex.Message}");
            }
        }
    }

    internal void NotifyObstaclesCleared(ObstacleClearEventInfo eventInfo)
    {
        Action<ObstacleClearEventInfo>? handlers = _onObstaclesCleared;
        if (!CanNotifyLocal(handlers, eventInfo.VoxelIndex, eventInfo.ChangeSequence))
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
                GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Obstacle clear error: {ex.Message}");
            }
        }
    }

    internal void NotifyOccupantAdded(OccupantEventInfo eventInfo, ulong materializationSequence)
    {
        Action<OccupantEventInfo>? handlers = _onOccupantAdded;
        if (!CanNotifyLocal(handlers, eventInfo.VoxelIndex, materializationSequence))
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
                GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Occupant add error: {ex.Message}");
            }
        }
    }

    internal void NotifyOccupantRemoved(OccupantEventInfo eventInfo, ulong materializationSequence)
    {
        Action<OccupantEventInfo>? handlers = _onOccupantRemoved;
        if (!CanNotifyLocal(handlers, eventInfo.VoxelIndex, materializationSequence))
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
                GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Occupant remove error: {ex.Message}");
            }
        }
    }

    #endregion

    #region Partition Management

    /// <summary>
    /// Adds a partition to this voxel, allowing specialized behaviors.
    /// </summary>
    /// <param name="partition">The partition instance to retain under its exact type key.</param>
    /// <returns>True after storage, parent assignment, and the add callback succeed; false for null, a duplicate exact type, or a callback failure. Failed callbacks remove the stored entry but cannot undo caller object side effects.</returns>
    /// <remarks>The partition reference is retained on success. A throwing parent/add callback is logged and the stored entry is removed, but any changes the callback already made to the supplied object are not rolled back.</remarks>
    public bool TryAddPartition(IVoxelPartition partition)
    {
        if (partition == null)
            return false;

        Type partitionType = partition.GetType();

        lock (_partitionProvider)
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
            lock (_partitionProvider)
                _partitionProvider.TryRemove(partitionType, out _);

            string partitionName = partitionType.Name;
            GridForgeLogger.Channel.Error($"Error attempting to attach partition {partitionName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes a partition from this voxel.
    /// </summary>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>True once the exact type was detached, even if its removal callback throws; false when absent.</returns>
    /// <remarks>The entry is removed before its callback. Callback exceptions are logged; the removal remains successful and parent identity is not automatically cleared.</remarks>
    public bool TryRemovePartition<T>() where T : IVoxelPartition
    {
        Type partitionType = typeof(T);

        IVoxelPartition? partition = null;
        lock (_partitionProvider)
            _partitionProvider.TryRemove(partitionType, out partition);

        if (partition == null)
        {
            string partitionName = partitionType.Name;
            GridForgeLogger.Channel.Warn($"Partition {partitionName} not found on this voxel.");
            return false;
        }

        try
        {
            partition.OnRemoveFromVoxel(this);
        }
        catch (Exception ex)
        {
            string partitionName = partitionType.Name;
            GridForgeLogger.Channel.Error($"Attempting to call {nameof(partition.OnRemoveFromVoxel)} on {partitionName}: {ex.Message}");
        }

        return true;
    }

    /// <summary>
    /// Checks whether or not this voxel contains a specific partition.
    /// </summary>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>True when a partition stored under the exact requested type can be materialized as that type.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasPartition<T>() where T : IVoxelPartition
    {
        return TryGetPartition<T>(out _);
    }

    /// <summary>
    /// Retrieves a partition from the voxel by type.
    /// </summary>
    /// <param name="partition">The resolved partition on success; default on failure. Use it only when the operation succeeds.</param>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>True when the exact type is attached and the output is valid; otherwise false and the output is default.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPartition<T>(out T? partition) where T : IVoxelPartition
    {
        lock (_partitionProvider)
        {
            // Resolve the exact key through the existing provider without a
            // nested generic lookup. Keep typed materialization under the lock:
            // reset callbacks can mutate or pool payloads, including boxed structs.
            if (_partitionProvider.TryGet(typeof(T), out IVoxelPartition? candidate)
                && candidate is T typedPartition)
            {
                partition = typedPartition;
                return true;
            }

            partition = default;
            return false;
        }
    }

    /// <summary>Resolves a partition type without waiting, within the supplied inspection allowance.</summary>
    /// <remarks>
    /// Reserves one unit for the requested type and one for each other attached partition.
    /// An exhausted lookup charges the supplied allowance. A completed lookup can return
    /// a null partition. Use the borrowed partition within
    /// the caller's serialized world phase.
    /// </remarks>
    /// <param name="discoveryLimit">Maximum charged discovery work. Zero cannot fund an inspection.</param>
    /// <param name="discoveryWork">The work charged by the operation, including work on incomplete results; exhaustion charges the supplied allowance.</param>
    /// <param name="partition">The resolved partition on success; default on failure. Use it only when the operation succeeds.</param>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>Completed when discovery finished, even if the partition is null; WorkBudgetExceeded when the allowance is insufficient; WorldNotReady when the partition lock is busy.</returns>
    public GridBoundedVoxelStatus TryGetPartitionBounded<T>(
        ulong discoveryLimit, out ulong discoveryWork, out T? partition)
        where T : class, IVoxelPartition =>
        TryGetPartitionBounded(discoveryLimit, null, null, out discoveryWork, out partition);

    /// <summary>Resolves a partition type with up to two exact types excluded from the work allowance.</summary>
    /// <remarks>
    /// Use the excluded types for caller-owned partition caches that have no bearing on this lookup.
    /// Inspecting these types adds at most two inspections to the supplied allowance. The requested
    /// type always reserves one unit. Each other attached, nonexcluded partition reserves one unit.
    /// Exhaustion charges the supplied allowance. Use the borrowed partition within the caller's
    /// serialized world phase.
    /// </remarks>
    /// <param name="discoveryLimit">Maximum charged discovery work. Zero cannot fund an inspection.</param>
    /// <param name="discoveryWork">The work charged by the operation, including work on incomplete results; exhaustion charges the supplied allowance.</param>
    /// <param name="partition">The resolved partition on success; default on failure. Use it only when the operation succeeds.</param>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <typeparam name="TFirstExcluded">The first exact cache partition type excluded from discovery charging.</typeparam>
    /// <typeparam name="TSecondExcluded">The second exact cache partition type excluded from discovery charging.</typeparam>
    /// <returns>Completed when discovery finished, even if the partition is null; WorkBudgetExceeded when the allowance is insufficient; WorldNotReady when the partition lock is busy.</returns>
    public GridBoundedVoxelStatus TryGetPartitionBounded<T, TFirstExcluded, TSecondExcluded>(
        ulong discoveryLimit, out ulong discoveryWork, out T? partition)
        where T : class, IVoxelPartition
        where TFirstExcluded : class, IVoxelPartition
        where TSecondExcluded : class, IVoxelPartition =>
        TryGetPartitionBounded(discoveryLimit, typeof(TFirstExcluded), typeof(TSecondExcluded), out discoveryWork, out partition);

    private GridBoundedVoxelStatus TryGetPartitionBounded<T>(
        ulong discoveryLimit, Type? firstExcludedType, Type? secondExcludedType,
        out ulong discoveryWork, out T? partition)
        where T : class, IVoxelPartition
    {
        partition = null;
        discoveryWork = 0;
        if (!Monitor.TryEnter(_partitionProvider))
            return GridBoundedVoxelStatus.WorldNotReady;

        try
        {
            if (!_partitionProvider.TryGetWithinInspectionLimit(typeof(T), firstExcludedType, secondExcludedType,
                    discoveryLimit, out discoveryWork, out IVoxelPartition? candidate))
                return GridBoundedVoxelStatus.WorkBudgetExceeded;

            partition = candidate as T;
            return GridBoundedVoxelStatus.Completed;
        }
        finally
        {
            Monitor.Exit(_partitionProvider);
        }
    }

    /// <summary>
    /// Retrieves a partition from the voxel by type and returns null if it doesn't exist.
    /// </summary>
    /// <typeparam name="T">The exact partition type to query or remove.</typeparam>
    /// <returns>The attached reference for the exact requested type, or null. The reference is borrowed until detach or voxel reset.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetPartitionOrDefault<T>() where T : class, IVoxelPartition
    {
        return TryGetPartition(out T? partition) ? partition : null;
    }

    #endregion

    #region Neighbor Handling

    /// <summary>
    /// Clears and fills caller-owned storage with neighboring voxels whose
    /// world-space voxel footprints touch this voxel's footprint.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="results">Caller-owned storage cleared and filled with contact neighbors.</param>
    /// <param name="scope">The grid groups included by the contact query.</param>
    /// <param name="tolerance">Optional fixed-point tolerance applied to footprint contact checks.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    public void GetNeighborsInto(
        VoxelGrid ownerGrid,
        SwiftList<Voxel> results,
        VoxelNeighborScope scope = VoxelNeighborScope.All,
        Fixed64? tolerance = null)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (!IsValidOwnerGrid(ownerGrid))
            return;

        VoxelNeighborResolver.AddContactNeighbors(this, ownerGrid, results, scope, tolerance);
    }

    /// <summary>
    /// Determines whether this voxel has at least one footprint-contact neighbor in the requested scope.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="scope">The grid groups included by the contact query.</param>
    /// <param name="tolerance">Optional fixed-point tolerance applied to footprint contact checks.</param>
    /// <returns>True when at least one contact exists; otherwise false.</returns>
    public bool HasNeighbor(
        VoxelGrid ownerGrid,
        VoxelNeighborScope scope = VoxelNeighborScope.All,
        Fixed64? tolerance = null)
    {
        if (!IsValidOwnerGrid(ownerGrid))
            return false;

        return VoxelNeighborResolver.HasContactNeighbor(this, ownerGrid, scope, tolerance);
    }

    /// <summary>
    /// Retrieves the rectangular-prism neighbor voxel in the supplied topology-local direction.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="direction">The rectangular-prism direction to resolve.</param>
    /// <param name="neighbor">The resolved same-topology neighbor when found.</param>
    /// <returns>True when a same-topology neighbor exists in the supplied direction; otherwise false.</returns>
    public bool TryGetNeighbor(
        VoxelGrid ownerGrid,
        RectangularDirection direction,
        out Voxel? neighbor)
    {
        neighbor = null;
        if (!IsValidOwnerGrid(ownerGrid))
            return false;

        return VoxelNeighborResolver.TryGetNeighbor(this, ownerGrid, direction, out neighbor);
    }

    /// <summary>
    /// Retrieves the hex-prism neighbor voxel in the supplied topology-local direction.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="direction">The hex-prism direction to resolve.</param>
    /// <param name="neighbor">The resolved same-topology neighbor when found.</param>
    /// <returns>True when a same-topology neighbor exists in the supplied direction; otherwise false.</returns>
    public bool TryGetNeighbor(
        VoxelGrid ownerGrid,
        HexDirection direction,
        out Voxel? neighbor)
    {
        neighbor = null;
        if (!IsValidOwnerGrid(ownerGrid))
            return false;

        return VoxelNeighborResolver.TryGetNeighbor(this, ownerGrid, direction, out neighbor);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with rectangular-prism neighbors in deterministic direction order.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="results">Caller-owned storage cleared and filled with direction-labeled neighbors.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    public void GetRectangularNeighborsInto(
        VoxelGrid ownerGrid,
        SwiftList<(RectangularDirection Direction, Voxel Voxel)> results)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (!IsValidOwnerGrid(ownerGrid))
            return;

        VoxelNeighborResolver.AddRectangularNeighbors(this, ownerGrid, results);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with hex-prism neighbors in deterministic direction order.
    /// </summary>
    /// <param name="ownerGrid">The active grid that owns this voxel.</param>
    /// <param name="results">Caller-owned storage cleared and filled with direction-labeled neighbors.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="results"/> is null.</exception>
    public void GetHexNeighborsInto(
        VoxelGrid ownerGrid,
        SwiftList<(HexDirection Direction, Voxel Voxel)> results)
    {
        ChronicleGuard.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (!IsValidOwnerGrid(ownerGrid))
            return;

        VoxelNeighborResolver.AddHexNeighbors(this, ownerGrid, results);
    }

    internal bool IsValidOwnerGrid(VoxelGrid? ownerGrid)
    {
        return ownerGrid != null
            && ownerGrid.IsActive
            && ownerGrid.GridIndex == WorldIndex.GridIndex
            && ownerGrid.SpawnToken == WorldIndex.GridSpawnToken
            && ownerGrid.World != null
            && ownerGrid.OwnerWorldToken == WorldIndex.WorldSpawnToken;
    }

    #endregion

    #region Utility

    /// <summary>Gets the stable runtime hash of this voxel object.</summary>
    /// <returns>A hash for lookup; collisions are possible and equality remains authoritative.</returns>
    /// <remarks>The object hash is independent of WorldIndex, but a pooled object can represent a different cell after reset/reuse. Use WorldVoxelIndex for generation-scoped keys.</remarks>
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    /// <summary>Formats this value for diagnostics.</summary>
    /// <returns>Diagnostic text; do not use it as a persistence format.</returns>
    public override string ToString() => WorldIndex.ToString();

    /// <summary>Tests object reference identity.</summary>
    /// <param name="other">The value to compare.</param>
    /// <returns>True only when the supplied reference is this same voxel object; matching WorldIndex values alone do not establish equality.</returns>
    public bool Equals(Voxel? other) => ReferenceEquals(this, other);

    /// <summary>Tests object reference identity.</summary>
    /// <param name="obj">The object to compare; null and other types compare unequal.</param>
    /// <returns>True only when the supplied reference is this same voxel object; matching WorldIndex values alone do not establish equality.</returns>
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    #endregion
}
