//=======================================================================
// Blocker.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;

namespace GridForge.Blockers;

/// <summary>
/// Base class for all grid blockers that handles applying and removing obstacles.
/// </summary>
public abstract class Blocker : IBlocker
{
    private readonly object _gridWatcherLock = new();
    private bool _isWatchingWorldEvents;

    /// <summary>
    /// The world this blocker is bound to.
    /// </summary>
    /// <value>The world this blocker is bound to.</value>
    public GridWorld World { get; }

    /// <summary>
    /// Unique token representing this blockage instance.
    /// </summary>
    /// <value>Unique token representing this blockage instance.</value>
    public ObstacleToken BlockageToken { get; private set; }

    /// <summary>
    /// Indicates whether the blocker is currently active.
    /// </summary>
    /// <value>Indicates whether the blocker is currently active.</value>
    public bool IsActive { get; protected set; }

    /// <summary>
    /// The cached minimum bounds of the blockage area.
    /// </summary>
    /// <value>The cached minimum bounds of the blockage area.</value>
    public Vector3d CacheMin { get; protected set; }

    /// <summary>
    /// The cached maximum bounds of the blockage area.
    /// </summary>
    /// <value>The cached maximum bounds of the blockage area.</value>
    public Vector3d CacheMax { get; private set; }

    /// <summary>
    /// Tracks whether the blocker is currently blocking voxels.
    /// </summary>
    /// <value>Tracks whether the blocker is currently blocking voxels.</value>
    public bool IsBlocking { get; protected set; }

    /// <summary>
    /// Whether to cache world-scoped voxel identities for covered voxels.
    /// </summary>
    /// <value>Whether to cache world-scoped voxel identities for covered voxels.</value>
    public bool CacheCoveredVoxels { get; protected set; }

    /// <summary>
    /// Stable voxel identifiers cached for safe blocker removal when <see cref="CacheCoveredVoxels"/> is true.
    /// </summary>
    protected SwiftList<WorldVoxelIndex>? _cachedCoveredVoxels;

    /// <summary>
    /// Grid indices currently covered by this blocker.
    /// </summary>
    private readonly SwiftHashSet<ushort> _watchedGridIndices = new();

    /// <summary>
    /// Event triggered when a blocker is applied.
    /// </summary>
    private static Action<BlockageEventInfo>? _onBlockageApplied;

    /// <summary>Raised when a blocker has successfully applied its coverage.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
    public static event Action<BlockageEventInfo> OnBlockageApplied
    {
        add => _onBlockageApplied += value;
        remove => _onBlockageApplied -= value;
    }

    /// <summary>
    /// Event triggered when a blocker is removed.
    /// </summary>
    private static Action<BlockageEventInfo>? _onBlockageRemoved;

    /// <summary>Raised when a blocker has removed its coverage.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
    public static event Action<BlockageEventInfo> OnBlockageRemoved
    {
        add => _onBlockageRemoved += value;
        remove => _onBlockageRemoved -= value;
    }

    /// <summary>
    /// Initializes a new blocker instance bound to the supplied world.
    /// </summary>
    /// <param name="world">The world whose grids this blocker should affect.</param>
    /// <param name="active">Flag whether or not this blocker will block on update.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels that are blocked.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="world"/> is null.</exception>
    protected Blocker(GridWorld world, bool active = true, bool cacheCoveredVoxels = false)
    {
        ChronicleGuard.ThrowIfNull(world, nameof(world));
        World = world;
        IsActive = active;
        CacheCoveredVoxels = cacheCoveredVoxels;
    }

    /// <summary>
    /// Sets the requested blocker state. False removes the blockage and deactivates
    /// the blocker; true applies the blockage if it is not already blocking.
    /// </summary>
    /// <param name="status">True requests application; false removes the blockage and deactivates the blocker.</param>
    public virtual void ToggleStatus(bool status)
    {
        if (!status)
        {
            RemoveBlockageCore(keepWatching: false, preserveToken: false);
            IsActive = false;
            return;
        }

        if (!IsBlocking)
        {
            IsActive = true;
            ApplyBlockage();
        }
    }

    /// <summary>
    /// Applies the blockage by marking voxels as obstacles.
    /// </summary>
    /// <remarks>Operations use this blocker's retained world. Application skips inactive/already-blocking states and rolls back newly added tokens if any covered voxel rejects the attachment. Removal affects only this blocker's token. Subscriber exceptions are logged after the state change.</remarks>
    public virtual void ApplyBlockage()
    {
        ApplyBlockageCore(preserveTokenWhenEmpty: false);
    }

    private void ApplyBlockageCore(bool preserveTokenWhenEmpty)
    {
        if (!IsActive || IsBlocking || !World.IsActive)
            return;

        PrepareBlockageApplication();
        ObstacleToken blockageToken = BlockageToken;
        bool foundCoverage = ApplyBlockageToCoveredVoxels(blockageToken, out bool hasCoverage);

        IsBlocking = foundCoverage && hasCoverage;

        if (IsBlocking)
        {
            NotifyBlockageApplied();
            return;
        }

        if (!preserveTokenWhenEmpty || !hasCoverage)
            BlockageToken = default;
    }

    /// <summary>
    /// Removes the blockage by clearing obstacle markers from voxels.
    /// </summary>
    /// <remarks>Operations use this blocker's retained world. Application skips inactive/already-blocking states and rolls back newly added tokens if any covered voxel rejects the attachment. Removal affects only this blocker's token. Subscriber exceptions are logged after the state change.</remarks>
    public virtual void RemoveBlockage()
    {
        RemoveBlockageCore(keepWatching: false, preserveToken: false);
    }

    private void RemoveBlockageCore(bool keepWatching, bool preserveToken)
    {
        if (!IsBlocking)
        {
            if (!preserveToken)
                BlockageToken = default;

            ClearCoverageTracking();
            UnregisterGridWatcherIfNeeded(keepWatching);
            return;
        }

        ObstacleToken blockageToken = BlockageToken;
        BlockageEventInfo removalEventInfo = CreateBlockageEventInfo(blockageToken);

        RemoveAppliedBlockage(blockageToken);

        if (!preserveToken)
            BlockageToken = default;
        IsBlocking = false;
        ClearCoverageTracking();
        UnregisterGridWatcherIfNeeded(keepWatching);

        NotifyBlockageRemoved(removalEventInfo);
    }

    private void PrepareBlockageApplication()
    {
        CacheMin = GetBoundsMin();
        CacheMax = GetBoundsMax();
        if (!BlockageToken.IsValid)
            BlockageToken = World.AllocateObstacleToken();

        if (CacheCoveredVoxels)
            _cachedCoveredVoxels ??= new SwiftList<WorldVoxelIndex>();

        RegisterGridWatcher();
        ClearCoverageTracking();
    }

    private bool ApplyBlockageToCoveredVoxels(ObstacleToken blockageToken, out bool hasCoverage)
    {
        hasCoverage = true;
        bool foundCoverage = false;
        SwiftList<WorldVoxelIndex>? appliedVoxels = CacheCoveredVoxels
            ? _cachedCoveredVoxels
            : SwiftListPool<WorldVoxelIndex>.Shared.Rent();

        try
        {
            foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(World, CacheMin, CacheMax))
            {
                foundCoverage = true;
                _watchedGridIndices.Add(covered.Grid.GridIndex);
                ApplyBlockageToVoxels(covered, appliedVoxels!, blockageToken, ref hasCoverage);
            }

            if (!hasCoverage)
                RollbackAppliedBlockage(appliedVoxels!, blockageToken);

            return foundCoverage;
        }
        finally
        {
            if (!CacheCoveredVoxels)
                SwiftListPool<WorldVoxelIndex>.Shared.Release(appliedVoxels!);
        }
    }

    private void ApplyBlockageToVoxels(
        GridVoxelSet covered,
        SwiftList<WorldVoxelIndex> appliedVoxels,
        ObstacleToken blockageToken,
        ref bool hasCoverage)
    {
        foreach (Voxel voxel in covered.Voxels)
        {
            if (!covered.Grid.TryAddObstacle(voxel, blockageToken))
            {
                hasCoverage = false;
                continue;
            }

            appliedVoxels.Add(voxel.WorldIndex);
        }
    }

    private void RollbackAppliedBlockage(
        SwiftList<WorldVoxelIndex> appliedVoxels,
        ObstacleToken blockageToken)
    {
        foreach (WorldVoxelIndex voxelIndex in appliedVoxels)
            GridObstacleManager.TryRemoveObstacle(World, voxelIndex, blockageToken);

        appliedVoxels.Clear();
    }

    private void RemoveAppliedBlockage(ObstacleToken blockageToken)
    {
        if (CacheCoveredVoxels && _cachedCoveredVoxels?.Count > 0)
        {
            RemoveCachedBlockage(blockageToken);
            return;
        }

        RemoveTracedBlockage(blockageToken);
    }

    private void RemoveCachedBlockage(ObstacleToken blockageToken)
    {
        foreach (WorldVoxelIndex voxelIndex in _cachedCoveredVoxels!)
            GridObstacleManager.TryRemoveObstacle(World, voxelIndex, blockageToken);
    }

    private void RemoveTracedBlockage(ObstacleToken blockageToken)
    {
        foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(World, CacheMin, CacheMax))
        {
            foreach (Voxel voxel in covered.Voxels)
                covered.Grid.TryRemoveObstacle(voxel, blockageToken);
        }
    }

    private void ClearCoverageTracking()
    {
        _cachedCoveredVoxels?.Clear();
        _watchedGridIndices.Clear();
    }

    private void UnregisterGridWatcherIfNeeded(bool keepWatching)
    {
        if (!keepWatching || !IsActive)
            UnregisterGridWatcher();
    }

    /// <summary>
    /// Creates a snapshot describing the current blocker coverage.
    /// </summary>
    /// <returns>A value snapshot of the current world token, blockage token, and cached bounds.</returns>
    protected BlockageEventInfo CreateBlockageEventInfo()
    {
        return CreateBlockageEventInfo(BlockageToken);
    }

    private BlockageEventInfo CreateBlockageEventInfo(ObstacleToken blockageToken)
    {
        return new BlockageEventInfo(World.SpawnToken, blockageToken, CacheMin, CacheMax);
    }

    /// <summary>
    /// Notifies subscribers that blockage has been applied.
    /// </summary>
    protected virtual void NotifyBlockageApplied()
    {
        Action<BlockageEventInfo>? handlers = _onBlockageApplied;
        if (handlers == null)
            return;

        BlockageEventInfo eventInfo = CreateBlockageEventInfo();

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<BlockageEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error(
                    $"Blockage apply notification: {ex.Message} | Bounds: {eventInfo.BoundsMin} -> {eventInfo.BoundsMax}");
            }
        }
    }

    /// <summary>
    /// Notifies subscribers that blockage has been removed.
    /// </summary>
    /// <param name="eventInfo">The value snapshot supplied to removal subscribers.</param>
    protected virtual void NotifyBlockageRemoved(BlockageEventInfo eventInfo)
    {
        Action<BlockageEventInfo>? handlers = _onBlockageRemoved;
        if (handlers == null)
            return;

        var handlerDelegates = handlers.GetInvocationList();
        for (int i = 0; i < handlerDelegates.Length; i++)
        {
            try
            {
                ((Action<BlockageEventInfo>)handlerDelegates[i])(eventInfo);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Channel.Error(
                    $"Blockage remove notification: {ex.Message} | Bounds: {eventInfo.BoundsMin} -> {eventInfo.BoundsMax}");
            }
        }
    }

    /// <summary>
    /// Gets the min bounds of the area to block. Must be implemented by subclasses.
    /// </summary>
    /// <returns>The minimum blockage corner in fixed-point world coordinates.</returns>
    protected abstract Vector3d GetBoundsMin();

    /// <summary>
    /// Gets the max bounds of the area to block. Must be implemented by subclasses.
    /// </summary>
    /// <returns>The maximum blockage corner in fixed-point world coordinates.</returns>
    protected abstract Vector3d GetBoundsMax();

    /// <summary>Selects whether future applications cache world-scoped voxel identities for removal.</summary>
    /// <param name="cache">Whether future applications cache exact voxel identities for removal. Changing the setting does not immediately reapply the blockage.</param>
    /// <remarks>The cache stores value identities, not voxel references. Changing this flag does not itself apply or remove blockage; disabling it makes removal retrace the cached bounds.</remarks>
    public virtual void SetCacheCoveredVoxels(bool cache)
    {
        if (CacheCoveredVoxels == cache)
            return;

        CacheCoveredVoxels = cache;
        if (cache)
        {
            _cachedCoveredVoxels = new SwiftList<WorldVoxelIndex>();
            return;
        }

        _cachedCoveredVoxels = null;
    }

    /// <summary>
    /// Resets the blocker to its default state, removing any active blockage and clearing cached data.
    /// </summary>
    /// <remarks>Operations use this blocker's retained world. Application skips inactive/already-blocking states and rolls back newly added tokens if any covered voxel rejects the attachment. Removal affects only this blocker's token. Subscriber exceptions are logged after the state change.</remarks>
    public virtual void Reset()
    {
        RemoveBlockageCore(keepWatching: false, preserveToken: false);

        CacheCoveredVoxels = false;
        _cachedCoveredVoxels = null;
        _watchedGridIndices.Clear();

        IsActive = false;
        CacheMin = Vector3d.Zero;
        CacheMax = Vector3d.Zero;
        BlockageToken = default;
    }

    private void ReapplyBlockage()
    {
        if (!IsActive)
            return;

        RemoveBlockageCore(keepWatching: true, preserveToken: true);
        ApplyBlockageCore(preserveTokenWhenEmpty: true);
    }

    private void RegisterGridWatcher()
    {
        lock (_gridWatcherLock)
        {
            if (_isWatchingWorldEvents)
                return;

            World.OnActiveGridAdded += HandleActiveGridAdded;
            World.OnActiveGridRemoved += HandleActiveGridRemoved;
            World.OnActiveGridChange += HandleActiveGridChanged;
            World.OnReset += HandleWorldReset;
            _isWatchingWorldEvents = true;
        }
    }

    private void UnregisterGridWatcher()
    {
        lock (_gridWatcherLock)
        {
            if (!_isWatchingWorldEvents)
                return;

            World.OnActiveGridAdded -= HandleActiveGridAdded;
            World.OnActiveGridRemoved -= HandleActiveGridRemoved;
            World.OnActiveGridChange -= HandleActiveGridChanged;
            World.OnReset -= HandleWorldReset;
            _isWatchingWorldEvents = false;
        }
    }

    private bool ShouldReactToGridAdded(GridEventInfo eventInfo)
    {
        return IsActive && BoundsOverlap(CacheMin, CacheMax, eventInfo.BoundsMin, eventInfo.BoundsMax);
    }

    private static bool BoundsOverlap(
        Vector3d firstMin,
        Vector3d firstMax,
        Vector3d secondMin,
        Vector3d secondMax)
    {
        return AxisOverlaps(firstMin.X, firstMax.X, secondMin.X, secondMax.X)
            && AxisOverlaps(firstMin.Y, firstMax.Y, secondMin.Y, secondMax.Y)
            && AxisOverlaps(firstMin.Z, firstMax.Z, secondMin.Z, secondMax.Z);
    }

    private static bool AxisOverlaps(Fixed64 firstMin, Fixed64 firstMax, Fixed64 secondMin, Fixed64 secondMax)
    {
        return firstMax >= secondMin && firstMin <= secondMax;
    }

    private bool ShouldReactToGridRemoved(GridEventInfo eventInfo)
    {
        return IsActive && _watchedGridIndices.Contains(eventInfo.GridIndex);
    }

    private bool ShouldReactToGridChanged(GridEventInfo eventInfo)
    {
        return IsActive
            && IsSparseVoxelMutation(eventInfo.ChangeKind)
            && BoundsOverlap(CacheMin, CacheMax, eventInfo.AffectedBoundsMin, eventInfo.AffectedBoundsMax);
    }

    private static bool IsSparseVoxelMutation(GridEventKind changeKind) =>
        changeKind == GridEventKind.SparseVoxelAdded
        || changeKind == GridEventKind.SparseVoxelRemoved;

    private void HandleActiveGridAdded(GridEventInfo eventInfo)
    {
        if (eventInfo.WorldSpawnToken != World.SpawnToken || !ShouldReactToGridAdded(eventInfo))
            return;

        ReapplyBlockage();
    }

    private void HandleActiveGridRemoved(GridEventInfo eventInfo)
    {
        if (eventInfo.WorldSpawnToken != World.SpawnToken || !ShouldReactToGridRemoved(eventInfo))
            return;

        ReapplyBlockage();
    }

    private void HandleActiveGridChanged(GridEventInfo eventInfo)
    {
        if (eventInfo.WorldSpawnToken != World.SpawnToken || !ShouldReactToGridChanged(eventInfo))
            return;

        ReapplyBlockage();
    }

    private void HandleWorldReset()
    {
        IsBlocking = false;
        BlockageToken = default;
        _cachedCoveredVoxels?.Clear();
        _watchedGridIndices.Clear();
        UnregisterGridWatcher();
    }
}
