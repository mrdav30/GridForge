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
    /// Event triggered when an obstacle is added.
    /// </summary>
    private static Action<ObstacleEventInfo>? _onObstacleAdded;

    /// <inheritdoc cref="_onObstacleAdded"/>
    public static event Action<ObstacleEventInfo> OnObstacleAdded
    {
        add => _onObstacleAdded += value;
        remove => _onObstacleAdded -= value;
    }

    /// <summary>
    /// Event triggered when an obstacle is removed.
    /// </summary>
    private static Action<ObstacleEventInfo>? _onObstacleRemoved;

    /// <inheritdoc cref="_onObstacleRemoved"/>
    public static event Action<ObstacleEventInfo> OnObstacleRemoved
    {
        add => _onObstacleRemoved += value;
        remove => _onObstacleRemoved -= value;
    }

    /// <summary>
    /// Event triggered when all obstacles on a voxel are cleared at once.
    /// </summary>
    private static Action<ObstacleClearEventInfo>? _onObstaclesCleared;

    /// <inheritdoc cref="_onObstaclesCleared"/>
    public static event Action<ObstacleClearEventInfo> OnObstaclesCleared
    {
        add => _onObstaclesCleared += value;
        remove => _onObstaclesCleared -= value;
    }

    #endregion

    #region Private Fields

    /// <summary>
    /// Per-grid locks for ensuring thread-safe obstacle operations.
    /// </summary>
    private static readonly ConcurrentDictionary<ushort, object> _gridLocks = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// Attempts to add an obstacle at the given global voxel index.
    /// </summary>
    public static bool TryAddObstacle(GlobalVoxelIndex index, BoundsKey obstacleSpawnToken)
    {
        return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryAddObstacle(voxel!, obstacleSpawnToken) == true;
    }

    /// <summary>
    /// Attempts to add an obstacle at the given world position.
    /// </summary>
    public static bool TryAddObstacle(this VoxelGrid grid, Vector3d position, BoundsKey obstacleSpawnToken)
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            && grid.TryAddObstacle(voxel!, obstacleSpawnToken);
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
        byte obstacleCount;
        uint gridVersion;

        lock (gridLock)
        {
            targetVoxel.ObstacleTracker ??= SwiftHashSetPool<BoundsKey>.Shared.Rent();
            if (!targetVoxel.ObstacleTracker.Add(obstacleSpawnToken))
                return false;
            targetVoxel.ObstacleCount++;

            grid.ObstacleCount++;
            gridVersion = grid.IncrementVersion();
            obstacleCount = targetVoxel.ObstacleCount;
        }

        NotifyObstacleAdded(grid, targetVoxel, obstacleSpawnToken, obstacleCount, gridVersion);

        return true;
    }

    /// <summary>
    /// Attempts to remove an obstacle at the given global voxel index.
    /// </summary>
    public static bool TryRemoveObstacle(GlobalVoxelIndex index, BoundsKey obstacleSpawnToken)
    {
        return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryRemoveObstacle(voxel!, obstacleSpawnToken);
    }

    /// <summary>
    /// Attempts to remove an obstacle from the specified world position.
    /// </summary>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Vector3d position, BoundsKey obstacleSpawnToken)
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            && grid.TryRemoveObstacle(voxel!, obstacleSpawnToken);
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
        byte obstacleCount;
        uint gridVersion;

        lock (gridLock)
        {
            if (targetVoxel.ObstacleTracker?.Remove(obstacleSpawnToken) != true)
                return false;

            if (--targetVoxel.ObstacleCount <= 0)
            {
                SwiftHashSetPool<BoundsKey>.Shared.Release(targetVoxel.ObstacleTracker);
                targetVoxel.ObstacleTracker = null;
                targetVoxel.ObstacleCount = 0;
            }

            grid.ObstacleCount--;
            gridVersion = grid.IncrementVersion();
            obstacleCount = targetVoxel.ObstacleCount;
        }

        NotifyObstacleRemoved(grid, targetVoxel, obstacleSpawnToken, obstacleCount, gridVersion);

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
        byte clearedObstacleCount;
        uint gridVersion;

        lock (gridLock)
        {
            clearedObstacleCount = targetVoxel.ObstacleCount;
            if (targetVoxel.ObstacleTracker != null)
            {
                SwiftHashSetPool<BoundsKey>.Shared.Release(targetVoxel.ObstacleTracker);
                targetVoxel.ObstacleTracker = null;
            }

            grid.ObstacleCount -= targetVoxel.ObstacleCount;
            targetVoxel.ObstacleCount = 0;
            gridVersion = grid.IncrementVersion();
        }

        NotifyObstaclesCleared(grid, targetVoxel, clearedObstacleCount, gridVersion);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Notifies listeners that an obstacle was added.
    /// </summary>
    private static void NotifyObstacleAdded(
        VoxelGrid grid,
        Voxel targetVoxel,
        BoundsKey obstacleSpawnToken,
        byte obstacleCount,
        uint gridVersion)
    {
        ObstacleEventInfo eventInfo = new(targetVoxel.GlobalIndex, obstacleSpawnToken, obstacleCount, gridVersion);
        Action<ObstacleEventInfo>? handlers = _onObstacleAdded;
        if (handlers != null)
        {
            var handlerDelegates = handlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action<ObstacleEventInfo>)handlerDelegates[i])(eventInfo);
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Error($"[Voxel {targetVoxel.GlobalIndex}] Obstacle add error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyObstacleAdded(eventInfo);

        targetVoxel.CachedGridVersion = gridVersion;
        GlobalGridManager.NotifyActiveGridChange(grid);
    }

    /// <summary>
    /// Notifies listeners that an obstacle was removed.
    /// </summary>
    private static void NotifyObstacleRemoved(
        VoxelGrid grid,
        Voxel targetVoxel,
        BoundsKey obstacleSpawnToken,
        byte obstacleCount,
        uint gridVersion)
    {
        ObstacleEventInfo eventInfo = new(targetVoxel.GlobalIndex, obstacleSpawnToken, obstacleCount, gridVersion);
        Action<ObstacleEventInfo>? handlers = _onObstacleRemoved;
        if (handlers != null)
        {
            var handlerDelegates = handlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action<ObstacleEventInfo>)handlerDelegates[i])(eventInfo);
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Error($"[Voxel {targetVoxel.GlobalIndex}] Obstacle remove error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyObstacleRemoved(eventInfo);

        targetVoxel.CachedGridVersion = gridVersion;
        GlobalGridManager.NotifyActiveGridChange(grid);
    }

    /// <summary>
    /// Notifies listeners that all obstacles on a voxel were cleared.
    /// </summary>
    private static void NotifyObstaclesCleared(
        VoxelGrid grid,
        Voxel targetVoxel,
        byte clearedObstacleCount,
        uint gridVersion)
    {
        ObstacleClearEventInfo eventInfo = new(targetVoxel.GlobalIndex, clearedObstacleCount, gridVersion);
        Action<ObstacleClearEventInfo>? handlers = _onObstaclesCleared;
        if (handlers != null)
        {
            var handlerDelegates = handlers.GetInvocationList();
            for (int i = 0; i < handlerDelegates.Length; i++)
            {
                try
                {
                    ((Action<ObstacleClearEventInfo>)handlerDelegates[i])(eventInfo);
                }
                catch (Exception ex)
                {
                    GridForgeLogger.Error($"[Voxel {targetVoxel.GlobalIndex}] Obstacle clear error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyObstaclesCleared(eventInfo);

        targetVoxel.CachedGridVersion = gridVersion;
        GlobalGridManager.NotifyActiveGridChange(grid);
    }

    #endregion
}
