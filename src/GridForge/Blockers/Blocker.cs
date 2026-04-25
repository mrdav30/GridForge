using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using System;

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
    public GridWorld World { get; }

    /// <summary>
    /// Unique token representing this blockage instance.
    /// </summary>
    public BoundsKey BlockageToken { get; protected set; } = default;

    /// <summary>
    /// Indicates whether the blocker is currently active.
    /// </summary>
    public bool IsActive { get; protected set; }

    /// <summary>
    /// The cached minimum bounds of the blockage area.
    /// </summary>
    public Vector3d CacheMin { get; protected set; }

    /// <summary>
    /// The cached maximum bounds of the blockage area.
    /// </summary>
    public Vector3d CacheMax { get; private set; }

    /// <summary>
    /// Tracks whether the blocker is currently blocking voxels.
    /// </summary>
    public bool IsBlocking { get; protected set; }

    /// <summary>
    /// Flags whether or not to hold onto a reference of the voxels this blocker covers.
    /// </summary>
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

    /// <inheritdoc cref="_onBlockageApplied"/>
    public static event Action<BlockageEventInfo> OnBlockageApplied
    {
        add => _onBlockageApplied += value;
        remove => _onBlockageApplied -= value;
    }

    /// <summary>
    /// Event triggered when a blocker is removed.
    /// </summary>
    private static Action<BlockageEventInfo>? _onBlockageRemoved;

    /// <inheritdoc cref="_onBlockageRemoved"/>
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
    protected Blocker(GridWorld world, bool active = true, bool cacheCoveredVoxels = false)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        IsActive = active;
        CacheCoveredVoxels = cacheCoveredVoxels;
    }

    /// <summary>
    /// Toggles the blocker from inactive to active or active to inactive state
    /// If object is currently blocking, the blocker will be removed.
    /// If object is not active and not blocking, the blocker will be applied.
    /// </summary>
    public virtual void ToggleStatus(bool status)
    {
        if (!status)
        {
            RemoveBlockageCore(keepWatching: false);
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
    public virtual void ApplyBlockage()
    {
        if (!IsActive || IsBlocking || !World.IsActive)
            return;

        CacheMin = GetBoundsMin();
        CacheMax = GetBoundsMax();
        BlockageToken = new BoundsKey(CacheMin, CacheMax);

        if (CacheCoveredVoxels)
            _cachedCoveredVoxels ??= new SwiftList<WorldVoxelIndex>();

        RegisterGridWatcher();
        _cachedCoveredVoxels?.Clear();
        _watchedGridIndices.Clear();

        bool hasCoverage = true;
        bool foundCoverage = false;
        foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(World, CacheMin, CacheMax))
        {
            if (covered.Voxels.Count <= 0)
                continue;

            foundCoverage = true;
            _watchedGridIndices.Add(covered.Grid.GridIndex);

            foreach (Voxel voxel in covered.Voxels)
            {
                if (!covered.Grid.TryAddObstacle(voxel, BlockageToken))
                {
                    hasCoverage = false;
                    continue;
                }

                if (CacheCoveredVoxels)
                    _cachedCoveredVoxels!.Add(voxel.WorldIndex);
            }
        }

        IsBlocking = foundCoverage && hasCoverage;

        if (IsBlocking)
        {
            NotifyBlockageApplied();
            return;
        }

        if (!foundCoverage)
            BlockageToken = default;
    }

    /// <summary>
    /// Removes the blockage by clearing obstacle markers from voxels.
    /// </summary>
    public virtual void RemoveBlockage()
    {
        RemoveBlockageCore(keepWatching: false);
    }

    private void RemoveBlockageCore(bool keepWatching)
    {
        if (!IsBlocking)
        {
            _cachedCoveredVoxels?.Clear();
            _watchedGridIndices.Clear();
            if (!keepWatching || !IsActive)
                UnregisterGridWatcher();
            return;
        }

        BlockageEventInfo removalEventInfo = CreateBlockageEventInfo();

        if (CacheCoveredVoxels && _cachedCoveredVoxels?.Count > 0)
        {
            foreach (WorldVoxelIndex voxelIndex in _cachedCoveredVoxels)
                GridObstacleManager.TryRemoveObstacle(World, voxelIndex, BlockageToken);
        }
        else
        {
            foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(World, CacheMin, CacheMax))
            {
                foreach (Voxel voxel in covered.Voxels)
                    covered.Grid.TryRemoveObstacle(voxel, BlockageToken);
            }
        }

        BlockageToken = default;
        IsBlocking = false;
        _cachedCoveredVoxels?.Clear();
        _watchedGridIndices.Clear();

        if (!keepWatching || !IsActive)
            UnregisterGridWatcher();

        NotifyBlockageRemoved(removalEventInfo);
    }

    /// <summary>
    /// Creates a snapshot describing the current blocker coverage.
    /// </summary>
    protected BlockageEventInfo CreateBlockageEventInfo()
    {
        return new BlockageEventInfo(World.SpawnToken, BlockageToken, CacheMin, CacheMax);
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
                GridForgeLogger.Error(
                    $"Blockage apply notification: {ex.Message} | Bounds: {eventInfo.BoundsMin} -> {eventInfo.BoundsMax}");
            }
        }
    }

    /// <summary>
    /// Notifies subscribers that blockage has been removed.
    /// </summary>
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
                GridForgeLogger.Error(
                    $"Blockage remove notification: {ex.Message} | Bounds: {eventInfo.BoundsMin} -> {eventInfo.BoundsMax}");
            }
        }
    }

    /// <summary>
    /// Gets the min bounds of the area to block. Must be implemented by subclasses.
    /// </summary>
    protected abstract Vector3d GetBoundsMin();

    /// <summary>
    /// Gets the max bounds of the area to block. Must be implemented by subclasses.
    /// </summary>
    protected abstract Vector3d GetBoundsMax();

    /// <summary>
    /// Sets whether or not to cache covered voxels for this blocker. 
    /// If enabled, the blocker will store references to the voxels it covers when applying blockage, 
    /// which can improve performance when removing blockage at the cost of increased memory usage.
    /// </summary>
    public virtual void SetCacheCoveredVoxels(bool cache)
    {
        if (CacheCoveredVoxels == cache)
            return;

        CacheCoveredVoxels = cache;
        if (cache && _cachedCoveredVoxels == null)
            _cachedCoveredVoxels = new SwiftList<WorldVoxelIndex>();
        else if (!cache)
            _cachedCoveredVoxels = null;
    }

    /// <summary>
    /// Resets the blocker to its default state, removing any active blockage and clearing cached data.
    /// </summary>
    public virtual void Reset()
    {
        RemoveBlockageCore(keepWatching: false);

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

        RemoveBlockageCore(keepWatching: true);
        ApplyBlockage();
    }

    private void RegisterGridWatcher()
    {
        lock (_gridWatcherLock)
        {
            if (_isWatchingWorldEvents)
                return;

            World.OnActiveGridAdded += HandleActiveGridAdded;
            World.OnActiveGridRemoved += HandleActiveGridRemoved;
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
            World.OnReset -= HandleWorldReset;
            _isWatchingWorldEvents = false;
        }
    }

    private bool ShouldReactToGridAdded(GridEventInfo eventInfo)
    {
        if (!IsActive)
            return false;

        return CacheMax.x >= eventInfo.BoundsMin.x
            && CacheMin.x <= eventInfo.BoundsMax.x
            && CacheMax.y >= eventInfo.BoundsMin.y
            && CacheMin.y <= eventInfo.BoundsMax.y
            && CacheMax.z >= eventInfo.BoundsMin.z
            && CacheMin.z <= eventInfo.BoundsMax.z;
    }

    private bool ShouldReactToGridRemoved(GridEventInfo eventInfo)
    {
        return IsActive && _watchedGridIndices.Contains(eventInfo.GridIndex);
    }

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

    private void HandleWorldReset()
    {
        IsBlocking = false;
        BlockageToken = default;
        _cachedCoveredVoxels?.Clear();
        _watchedGridIndices.Clear();
        UnregisterGridWatcher();
    }
}
