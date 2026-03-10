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
    /// Event triggered when a blockage is added or removed.
    /// </summary>
    public static event Action<GridChange, Vector3d, Vector3d> OnBlockageChanged;

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
        if (!status && IsBlocking)
        {
            RemoveBlockage();
            IsActive = false;
            return;
        }

        if (status && !IsBlocking)
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

        _cachedCoveredVoxels?.Clear();

        bool hasCoverage = true;
        // Iterate over all affected voxels and apply obstacles
        foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(CacheMin, CacheMax))
        {
            if (covered.Voxels.Count <= 0)
                continue;

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

        if (hasCoverage)
        {
            IsBlocking = true;
            NotifyBlockageChanged(GridChange.Add);
        }
    }

    /// <summary>
    /// Removes the blockage by clearing obstacle markers from voxels.
    /// </summary>
    public virtual void RemoveBlockage()
    {
        if (!IsBlocking)
            return;

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

        NotifyBlockageChanged(GridChange.Remove);
    }

    /// <summary>
    /// Notifies subscribers of blockage changes with error handling to prevent exceptions from disrupting the system.
    /// </summary>
    protected virtual void NotifyBlockageChanged(GridChange change)
    {
        try
        {
            OnBlockageChanged?.Invoke(change, CacheMin, CacheMax);
        }
        catch (Exception ex)
        {
            GridForgeLogger.Error($"Blockage notification: {ex.Message} | Change: {change} | Bounds: {CacheMin} -> {CacheMax}");
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
        if (IsBlocking)
            RemoveBlockage();

        CacheCoveredVoxels = false;
        _cachedCoveredVoxels = null;

        IsActive = false;
        CacheMin = Vector3d.Zero;
        CacheMax = Vector3d.Zero;
        BlockageToken = default;
    }
}
