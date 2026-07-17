//=======================================================================
// GridObstacleManager.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections.Pool;
using System;

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

    #region Public Methods

    /// <summary>
    /// Attempts to add an obstacle at the given world-scoped voxel identity in the supplied world.
    /// </summary>
    public static bool TryAddObstacle(
        GridWorld world,
        WorldVoxelIndex index,
        ObstacleToken obstacleToken)
    {
        return world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryAddObstacle(voxel!, obstacleToken) == true;
    }

    /// <summary>
    /// Attempts to add an obstacle at the given world position.
    /// </summary>
    public static bool TryAddObstacle(this VoxelGrid grid, Vector3d position, ObstacleToken obstacleToken)
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            && grid.TryAddObstacle(voxel!, obstacleToken);
    }

    /// <summary>
    /// Attempts to add an obstacle at the given XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="grid">The grid to mutate.</param>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="obstacleToken">The obstacle token to attach to the resolved voxel.</param>
    /// <returns>True if an obstacle was added to the resolved voxel; otherwise false.</returns>
    public static bool TryAddObstacle(this VoxelGrid grid, Vector2d position, ObstacleToken obstacleToken)
    {
        return grid.TryAddObstacle(position, default, obstacleToken);
    }

    /// <summary>
    /// Attempts to add an obstacle at the given XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="grid">The grid to mutate.</param>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve.</param>
    /// <param name="obstacleToken">The obstacle token to attach to the resolved voxel.</param>
    /// <returns>True if an obstacle was added to the resolved voxel; otherwise false.</returns>
    public static bool TryAddObstacle(this VoxelGrid grid, Vector2d position, Fixed64 layerY, ObstacleToken obstacleToken)
    {
        return grid.TryAddObstacle(GridPlane2d.ToWorld(position, layerY), obstacleToken);
    }

    /// <summary>
    /// Adds an obstacle to this voxel.
    /// </summary>
    /// <param name="grid"></param>
    /// <param name="targetVoxel"></param>
    /// <param name="obstacleToken">The process-unique obstacle registration token.</param>
    /// <exception cref="Exception"></exception>
    public static bool TryAddObstacle(this VoxelGrid grid, Voxel targetVoxel, ObstacleToken obstacleToken)
    {
        if (!obstacleToken.IsValid || !targetVoxel.IsBlockable)
            return false;

        byte obstacleCount;
        uint gridVersion;

        lock (grid.ObstacleSyncRoot)
        {
            if (targetVoxel.ObstacleCount >= MaxObstacleCount)
                return false;

            targetVoxel.ObstacleTracker ??= SwiftHashSetPool<ObstacleToken>.Shared.Rent();
            if (!targetVoxel.ObstacleTracker.Add(obstacleToken))
                return false;
            targetVoxel.ObstacleCount++;

            grid.ObstacleCount++;
            gridVersion = grid.IncrementVersion();
            obstacleCount = targetVoxel.ObstacleCount;
        }

        NotifyObstacleAdded(grid, targetVoxel, obstacleToken, obstacleCount, gridVersion);

        return true;
    }

    /// <summary>
    /// Attempts to remove an obstacle at the given world-scoped voxel identity in the supplied world.
    /// </summary>
    public static bool TryRemoveObstacle(
        GridWorld world,
        WorldVoxelIndex index,
        ObstacleToken obstacleToken)
    {
        return world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryRemoveObstacle(voxel!, obstacleToken);
    }

    /// <summary>
    /// Attempts to remove an obstacle from the specified world position.
    /// </summary>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Vector3d position, ObstacleToken obstacleToken)
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            && grid.TryRemoveObstacle(voxel!, obstacleToken);
    }

    /// <summary>
    /// Attempts to remove an obstacle from the given XZ-plane world position on the default world Y layer.
    /// </summary>
    /// <param name="grid">The grid to mutate.</param>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="obstacleToken">The obstacle token to remove from the resolved voxel.</param>
    /// <returns>True if the obstacle was removed from the resolved voxel; otherwise false.</returns>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Vector2d position, ObstacleToken obstacleToken)
    {
        return grid.TryRemoveObstacle(position, default, obstacleToken);
    }

    /// <summary>
    /// Attempts to remove an obstacle from the given XZ-plane world position on the supplied world Y layer.
    /// </summary>
    /// <param name="grid">The grid to mutate.</param>
    /// <param name="position">The 2D position whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to resolve.</param>
    /// <param name="obstacleToken">The obstacle token to remove from the resolved voxel.</param>
    /// <returns>True if the obstacle was removed from the resolved voxel; otherwise false.</returns>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Vector2d position, Fixed64 layerY, ObstacleToken obstacleToken)
    {
        return grid.TryRemoveObstacle(GridPlane2d.ToWorld(position, layerY), obstacleToken);
    }

    /// <summary>
    /// Removes an obstacle from a given voxel.
    /// </summary>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Voxel targetVoxel, ObstacleToken obstacleToken)
    {
        if (!obstacleToken.IsValid)
            return false;

        if (targetVoxel.ObstacleCount == 0)
        {
            GridForgeLogger.Channel.Warn($"No obstacle to remove on voxel ({targetVoxel.WorldIndex})!");
            return false;
        }

        byte obstacleCount;
        uint gridVersion;

        lock (grid.ObstacleSyncRoot)
        {
            if (!targetVoxel.ObstacleTracker!.Remove(obstacleToken))
                return false;

            if (--targetVoxel.ObstacleCount <= 0)
            {
                SwiftHashSetPool<ObstacleToken>.Shared.Release(targetVoxel.ObstacleTracker);
                targetVoxel.ObstacleTracker = null;
                targetVoxel.ObstacleCount = 0;
            }

            grid.ObstacleCount--;
            gridVersion = grid.IncrementVersion();
            obstacleCount = targetVoxel.ObstacleCount;
        }

        NotifyObstacleRemoved(grid, targetVoxel, obstacleToken, obstacleCount, gridVersion);

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

        byte clearedObstacleCount;
        uint gridVersion;

        lock (grid.ObstacleSyncRoot)
        {
            clearedObstacleCount = targetVoxel.ObstacleCount;
            if (targetVoxel.ObstacleTracker != null)
            {
                SwiftHashSetPool<ObstacleToken>.Shared.Release(targetVoxel.ObstacleTracker);
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
        ObstacleToken obstacleToken,
        byte obstacleCount,
        uint gridVersion)
    {
        ObstacleEventInfo eventInfo = new(targetVoxel.WorldIndex, obstacleToken, obstacleCount, gridVersion);
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
                    GridForgeLogger.Channel.Error($"[Voxel {targetVoxel.WorldIndex}] Obstacle add error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyObstacleAdded(eventInfo);

        targetVoxel.CachedGridVersion = gridVersion;
        grid.World!.NotifyActiveGridChange(grid);
    }

    /// <summary>
    /// Notifies listeners that an obstacle was removed.
    /// </summary>
    private static void NotifyObstacleRemoved(
        VoxelGrid grid,
        Voxel targetVoxel,
        ObstacleToken obstacleToken,
        byte obstacleCount,
        uint gridVersion)
    {
        ObstacleEventInfo eventInfo = new(targetVoxel.WorldIndex, obstacleToken, obstacleCount, gridVersion);
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
                    GridForgeLogger.Channel.Error($"[Voxel {targetVoxel.WorldIndex}] Obstacle remove error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyObstacleRemoved(eventInfo);

        targetVoxel.CachedGridVersion = gridVersion;
        grid.World!.NotifyActiveGridChange(grid);
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
        ObstacleClearEventInfo eventInfo = new(targetVoxel.WorldIndex, clearedObstacleCount, gridVersion);
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
                    GridForgeLogger.Channel.Error($"[Voxel {targetVoxel.WorldIndex}] Obstacle clear error: {ex.Message}");
                }
            }
        }

        targetVoxel.NotifyObstaclesCleared(eventInfo);

        targetVoxel.CachedGridVersion = gridVersion;
        grid.World!.NotifyActiveGridChange(grid);
    }

    #endregion
}
