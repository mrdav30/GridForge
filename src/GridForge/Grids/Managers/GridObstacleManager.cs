using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections.Pool;
using System;
using System.Collections.Concurrent;

namespace GridForge.Grids;

/// <summary>
/// Handles the addition, removal, and tracking of obstacles within a grid.
/// Ensures thread safety and proper event notifications when obstacles change.
/// </summary>
public static class GridObstacleManager
{
    #region Constants & Events

    /// <summary>
    /// Maximum number of obstacles that can exist on a single voxel.
    /// </summary>
    public const byte MaxObstacleCount = byte.MaxValue;

    /// <summary>
    /// Event triggered when an obstacle is added or removed.
    /// </summary>
    public static event Action<GridChange, GlobalVoxelIndex> OnObstacleChange;

    #endregion

    #region Private Fields

    /// <summary>
    /// Per-grid locks for ensuring thread-safe obstacle operations.
    /// </summary>
    private static readonly ConcurrentDictionary<ushort, object> _gridLocks = new ConcurrentDictionary<ushort, object>();

    #endregion

    #region Public Methods

    /// <summary>
    /// Attempts to add an obstacle at the given global voxel index.
    /// </summary>
    public static bool TryAddObstacle(GlobalVoxelIndex index, BoundsKey obstacleSpawnToken)
    {
        return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid grid, out Voxel voxel)
            && TryAddObstacle(grid, voxel, obstacleSpawnToken);
    }

    /// <summary>
    /// Attempts to add an obstacle at the given world position.
    /// </summary>
    public static bool TryAddObstacle(this VoxelGrid grid, Vector3d position, BoundsKey obstacleSpawnToken)
    {
        return grid.TryGetVoxel(position, out Voxel voxel)
            && TryAddObstacle(grid, voxel, obstacleSpawnToken);
    }

    /// <summary>
    /// Adds an obstacle to this voxel.
    /// </summary>
    /// <param name="grid"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="obstacleSpawnToken"></param>
    /// <exception cref="Exception"></exception>
    public static bool TryAddObstacle(this VoxelGrid grid, Voxel targetVoxel, BoundsKey obstacleSpawnToken)
    {
        if (!targetVoxel.IsBlockable)
            return false;

        object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

        lock (gridLock)
        {
            targetVoxel.ObstacleTracker ??= SwiftHashSetPool<BoundsKey>.Shared.Rent();
            if (!targetVoxel.ObstacleTracker.Add(obstacleSpawnToken))
                return false;
            targetVoxel.ObstacleCount++;

            grid.ObstacleCount++;
            grid.IncrementVersion();
        }

        NotifyObstacleChange(GridChange.Add, targetVoxel, grid.Version);

        return true;
    }

    /// <summary>
    /// Attempts to remove an obstacle at the given global voxel index.
    /// </summary>
    public static bool TryRemoveObstacle(GlobalVoxelIndex index, BoundsKey obstacleSpawnToken)
    {
        return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid grid, out Voxel voxel)
            && TryRemoveObstacle(grid, voxel, obstacleSpawnToken);
    }

    /// <summary>
    /// Attempts to remove an obstacle from the specified world position.
    /// </summary>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Vector3d position, BoundsKey obstacleSpawnToken)
    {
        return grid.TryGetVoxel(position, out Voxel voxel)
            && TryRemoveObstacle(grid, voxel, obstacleSpawnToken);
    }

    /// <summary>
    /// Removes an obstacle from a given voxel.
    /// </summary>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Voxel targetVoxel, BoundsKey obstacleSpawnToken)
    {
        if (targetVoxel.ObstacleCount == 0)
        {
            GridForgeLogger.Warn($"No obstacle to remove on voxel ({targetVoxel.GlobalIndex})!");
            return false;
        }

        object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

        lock (gridLock)
        {
            if (!targetVoxel.ObstacleTracker.Remove(obstacleSpawnToken))
                return false;

            if (--targetVoxel.ObstacleCount <= 0)
            {
                SwiftHashSetPool<BoundsKey>.Shared.Release(targetVoxel.ObstacleTracker);
                targetVoxel.ObstacleTracker = null;
                targetVoxel.ObstacleCount = 0;
            }

            grid.ObstacleCount--;
            grid.IncrementVersion();
        }

        NotifyObstacleChange(GridChange.Remove, targetVoxel, grid.Version);

        return true;
    }

    /// <summary>
    /// Clears all obstacles from the specified voxel.
    /// </summary>
    /// <param name="grid"></param>
    /// <param name="targetVoxel"></param>
    public static void ClearObstacles(this VoxelGrid grid, Voxel targetVoxel)
    {
        if (targetVoxel.ObstacleCount == 0)
            return;

        object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

        lock (gridLock)
        {
            if (targetVoxel.ObstacleTracker != null)
            {
                SwiftHashSetPool<BoundsKey>.Shared.Release(targetVoxel.ObstacleTracker);
                targetVoxel.ObstacleTracker = null;
            }

            grid.ObstacleCount -= targetVoxel.ObstacleCount;
            targetVoxel.ObstacleCount = 0;
            grid.IncrementVersion();
        }

        NotifyObstacleChange(GridChange.Remove, targetVoxel, grid.Version);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Notifies listeners of an obstacle state change.
    /// </summary>
    private static void NotifyObstacleChange(GridChange change, Voxel targetVoxel, uint gridVersion)
    {
        Action<GridChange, GlobalVoxelIndex> handlers = OnObstacleChange;
        if (handlers != null)
        {
            foreach (Action<GridChange, GlobalVoxelIndex> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(change, targetVoxel.GlobalIndex);
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Error(
                        $"[Voxel {targetVoxel.GlobalIndex}] Obstacle change error: {ex.Message} | Change: {change}");
                }
            }
        }

        targetVoxel.NotifyObstacleChange(change);

        targetVoxel.CachedGridVersion = gridVersion;
    }

    #endregion
}
