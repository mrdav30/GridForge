using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using System;
using System.Linq;

namespace GridForge.Blockers;

/// <summary>
/// Base class for all grid blockers that handles applying and removing obstacles.
/// </summary>
public abstract class Blocker : IBlocker
{
    private static readonly object _gridWatcherLock = new object();
    private static readonly SwiftHashSet<Blocker> _registeredBlockers = new SwiftHashSet<Blocker>();

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
    protected SwiftList<GlobalVoxelIndex> _cachedCoveredVoxels;

    /// <summary>
    /// Grid indices currently covered by this blocker.
    /// </summary>
    private readonly SwiftHashSet<ushort> _watchedGridIndices = new SwiftHashSet<ushort>();

    /// <summary>
    /// Event triggered when a blocker is applied.
    /// </summary>
    private static Action<BlockageEventInfo> _onBlockageApplied;

    /// <inheritdoc cref="_onBlockageApplied"/>
    public static event Action<BlockageEventInfo> OnBlockageApplied
    {
        add => _onBlockageApplied += value;
        remove => _onBlockageApplied -= value;
    }

    /// <summary>
    /// Event triggered when a blocker is removed.
    /// </summary>
    private static Action<BlockageEventInfo> _onBlockageRemoved;

    /// <inheritdoc cref="_onBlockageRemoved"/>
    public static event Action<BlockageEventInfo> OnBlockageRemoved
    {
        add => _onBlockageRemoved += value;
        remove => _onBlockageRemoved -= value;
    }

    static Blocker()
    {
        GlobalGridManager.OnActiveGridAdded += HandleActiveGridAdded;
        GlobalGridManager.OnActiveGridRemoved += HandleActiveGridRemoved;
        GlobalGridManager.OnReset += HandleGlobalReset;
    }

    /// <summary>
    /// Initializes a new blocker instance.
    /// </summary>
    /// <param name="active">Flag whether or not this blocker will block on update.</param>
    /// <param name="cacheCoveredVoxels">Flag whether or not to cache covered voxels that are blocked.</param>
    public Blocker(bool active = true, bool cacheCoveredVoxels = false)
    {
        IsActive = active;
        CacheCoveredVoxels = cacheCoveredVoxels;
    }

    /// <summary>
    /// Toggles the blocker from inactive to active or active to inactive state
    /// If object is currently blocking, the blocker will be removed.
    /// If object is not active and not blocking, the blocker will be applied.
    /// </summary>
    /// <param name="status"></param>
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
        if (!IsActive || IsBlocking)
            return;

        CacheMin = GetBoundsMin();
        CacheMax = GetBoundsMax();
        // Generate a unique blockage token based on the min/max bounds
        BlockageToken = new(CacheMin, CacheMax);

        if (CacheCoveredVoxels)
            _cachedCoveredVoxels ??= new SwiftList<GlobalVoxelIndex>();

        RegisterGridWatcher();
        _cachedCoveredVoxels?.Clear();
        _watchedGridIndices.Clear();

        bool hasCoverage = true;
        bool foundCoverage = false;
        // Iterate over all affected voxels and apply obstacles
        foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(CacheMin, CacheMax))
        {
            if (covered.Voxels.Count <= 0)
                continue;

            foundCoverage = true;
            _watchedGridIndices.Add(covered.Grid.GlobalIndex);

            foreach (Voxel voxel in covered.Voxels)
            {
                if (!covered.Grid.TryAddObstacle(voxel, BlockageToken))
                {
                    hasCoverage = false;
                    continue;
                }

                if (CacheCoveredVoxels)
                    _cachedCoveredVoxels.Add(voxel.GlobalIndex);
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

        if (CacheCoveredVoxels && _cachedCoveredVoxels?.Count > 0)
        {
            foreach (GlobalVoxelIndex voxelIndex in _cachedCoveredVoxels)
                GridObstacleManager.TryRemoveObstacle(voxelIndex, BlockageToken);
        }
        else
        {
            foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(CacheMin, CacheMax))
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

        NotifyBlockageRemoved();
    }

    /// <summary>
    /// Creates a snapshot describing the current blocker coverage.
    /// </summary>
    protected BlockageEventInfo CreateBlockageEventInfo()
    {
        return new BlockageEventInfo(BlockageToken, CacheMin, CacheMax);
    }

    /// <summary>
    /// Notifies subscribers that blockage has been applied.
    /// </summary>
    protected virtual void NotifyBlockageApplied()
    {
        Action<BlockageEventInfo> handlers = _onBlockageApplied;
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
    protected virtual void NotifyBlockageRemoved()
    {
        Action<BlockageEventInfo> handlers = _onBlockageRemoved;
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
    /// <param name="cache"></param>
    public virtual void SetCacheCoveredVoxels(bool cache)
    {
        if (CacheCoveredVoxels == cache)
            return;

        CacheCoveredVoxels = cache;
        if (cache && _cachedCoveredVoxels == null)
            _cachedCoveredVoxels = new SwiftList<GlobalVoxelIndex>();
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
            _registeredBlockers.Add(this);
    }

    private void UnregisterGridWatcher()
    {
        lock (_gridWatcherLock)
            _registeredBlockers.Remove(this);
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

    private static void HandleActiveGridAdded(GridEventInfo eventInfo)
    {
        Blocker[] blockers;
        lock (_gridWatcherLock)
            blockers = _registeredBlockers.ToArray();

        foreach (Blocker blocker in blockers)
        {
            if (!blocker.ShouldReactToGridAdded(eventInfo))
                continue;

            blocker.ReapplyBlockage();
        }
    }

    private static void HandleActiveGridRemoved(GridEventInfo eventInfo)
    {
        Blocker[] blockers;
        lock (_gridWatcherLock)
            blockers = _registeredBlockers.ToArray();

        foreach (Blocker blocker in blockers)
        {
            if (!blocker.ShouldReactToGridRemoved(eventInfo))
                continue;

            blocker.ReapplyBlockage();
        }
    }

    private static void HandleGlobalReset()
    {
        Blocker[] blockers;
        lock (_gridWatcherLock)
            blockers = _registeredBlockers.ToArray();

        foreach (Blocker blocker in blockers)
        {
            if (!blocker.IsActive)
            {
                blocker.UnregisterGridWatcher();
                continue;
            }

            blocker.IsBlocking = false;
            blocker.BlockageToken = default;
            blocker._cachedCoveredVoxels?.Clear();
            blocker._watchedGridIndices.Clear();
        }

        lock (_gridWatcherLock)
            _registeredBlockers.Clear();
    }
}
