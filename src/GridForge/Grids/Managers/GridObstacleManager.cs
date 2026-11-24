//=======================================================================
// GridObstacleManager.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections.Pool;

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

    /// <summary>Raised when an obstacle registration is added.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
    public static event Action<ObstacleEventInfo> OnObstacleAdded
    {
        add => _onObstacleAdded += value;
        remove => _onObstacleAdded -= value;
    }

    /// <summary>
    /// Event triggered when an obstacle is removed.
    /// </summary>
    private static Action<ObstacleEventInfo>? _onObstacleRemoved;

    /// <summary>Raised when an obstacle registration is removed.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
    public static event Action<ObstacleEventInfo> OnObstacleRemoved
    {
        add => _onObstacleRemoved += value;
        remove => _onObstacleRemoved -= value;
    }

    /// <summary>
    /// Event triggered when all obstacles on a voxel are cleared at once.
    /// </summary>
    private static Action<ObstacleClearEventInfo>? _onObstaclesCleared;

    /// <summary>Raised when all obstacle registrations on a voxel are cleared.</summary>
    /// <remarks>Handlers run synchronously after the state change. Handler failures are logged and remaining subscribers are still invoked. Static manager/blocker events can contain changes from any world; use the event identity to filter.</remarks>
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
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <param name="obstacleToken">A nondefault obstacle registration token. Duplicate attachment of the same token to one voxel fails.</param>
    /// <returns>True when this token was newly attached and counts/version were updated; false for an invalid token, blocked capacity, occupied voxel, duplicate token, or unresolved location.</returns>
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
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="obstacleToken">A nondefault obstacle registration token. Duplicate attachment of the same token to one voxel fails.</param>
    /// <returns>True when this token was newly attached and counts/version were updated; false for an invalid token, blocked capacity, occupied voxel, duplicate token, or unresolved location.</returns>
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
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="targetVoxel">A current physical voxel belonging to the supplied grid.</param>
    /// <param name="obstacleToken">The process-unique obstacle registration token.</param>
    /// <returns>True when this token was newly attached and counts/version were updated; false for an invalid token, blocked capacity, occupied voxel, duplicate token, or a voxel not currently owned by the grid.</returns>
    /// <remarks>The voxel must be the current physical voxel in the supplied grid and match its world and grid generation. A foreign, unallocated, or mismatched voxel is rejected without changing obstacle state, versions, or publishing events. Do not retain borrowed voxel references across removal, reset, or pooled reuse. For later calls, resolve a saved <see cref="WorldVoxelIndex"/> through its owning world; this validates world/grid generations and identifies a voxel address, not a persistent physical object.</remarks>
    public static bool TryAddObstacle(this VoxelGrid grid, Voxel targetVoxel, ObstacleToken obstacleToken)
    {
        if (!obstacleToken.IsValid)
            return false;

        byte obstacleCount;
        uint gridVersion;
        bool drainCommittedChanges;
        GridWorld? world = grid.World;
        if (world == null)
            return false;

        world.EnterReadLock();
        try
        {
            lock (grid.ObstacleSyncRoot)
            {
                lock (world.ChangeSyncRoot)
                {
                    if (!IsOwnedVoxel(grid, targetVoxel) || !targetVoxel.IsBlockable)
                        return false;

                    targetVoxel.ObstacleTracker ??= SwiftHashSetPool<ObstacleToken>.Shared.Rent();
                    if (!targetVoxel.ObstacleTracker.Add(obstacleToken))
                        return false;
                    targetVoxel.ObstacleCount++;

                    grid.ObstacleCount++;
                    gridVersion = grid.IncrementVersion();
                    obstacleCount = targetVoxel.ObstacleCount;

                    CreateObstacleCommittedChange(
                        world,
                        grid,
                        targetVoxel,
                        GridEventKind.ObstacleAdded,
                        GridExactChangeKind.ObstacleAdded,
                        obstacleToken,
                        obstacleCount,
                        gridVersion,
                        out _,
                        out drainCommittedChanges);
                }
            }
        }
        finally
        {
            world.ExitReadLock();
        }

        if (drainCommittedChanges)
            world.DrainCommittedChanges();

        return true;
    }

    /// <summary>
    /// Attempts to remove an obstacle at the given world-scoped voxel identity in the supplied world.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <param name="index">The exact world/grid generation and topology-local voxel identity to resolve.</param>
    /// <param name="obstacleToken">A nondefault obstacle registration token. Duplicate attachment of the same token to one voxel fails.</param>
    /// <returns>True when this token was present and removed, updating counts/version; otherwise false.</returns>
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
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="position">The fixed-point position in world units.</param>
    /// <param name="obstacleToken">A nondefault obstacle registration token. Duplicate attachment of the same token to one voxel fails.</param>
    /// <returns>True when this token was present and removed, updating counts/version; otherwise false.</returns>
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
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="targetVoxel">A current physical voxel belonging to the supplied grid.</param>
    /// <param name="obstacleToken">A nondefault obstacle registration token. Duplicate attachment of the same token to one voxel fails.</param>
    /// <returns>True when this token was present and removed, updating counts/version; false when absent, invalid, or the voxel is not currently owned by the grid.</returns>
    /// <remarks>The voxel must be the current physical voxel in the supplied grid and match its world and grid generation. A foreign, unallocated, or mismatched voxel is rejected without changing obstacle state, versions, or publishing events. Do not retain borrowed voxel references across removal, reset, or pooled reuse. For later calls, resolve a saved <see cref="WorldVoxelIndex"/> through its owning world; this validates world/grid generations and identifies a voxel address, not a persistent physical object.</remarks>
    public static bool TryRemoveObstacle(this VoxelGrid grid, Voxel targetVoxel, ObstacleToken obstacleToken)
    {
        if (!obstacleToken.IsValid)
            return false;

        byte obstacleCount;
        uint gridVersion;
        bool drainCommittedChanges;
        GridWorld? world = grid.World;
        if (world == null)
            return false;

        world.EnterReadLock();
        try
        {
            lock (grid.ObstacleSyncRoot)
            {
                lock (world.ChangeSyncRoot)
                {
                    if (!IsOwnedVoxel(grid, targetVoxel))
                        return false;

                    if (targetVoxel.ObstacleCount == 0)
                    {
                        GridForgeLogger.Channel.Warn($"No obstacle to remove on voxel ({targetVoxel.WorldIndex})!");
                        return false;
                    }

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

                    CreateObstacleCommittedChange(
                        world,
                        grid,
                        targetVoxel,
                        GridEventKind.ObstacleRemoved,
                        GridExactChangeKind.ObstacleRemoved,
                        obstacleToken,
                        obstacleCount,
                        gridVersion,
                        out _,
                        out drainCommittedChanges);
                }
            }
        }
        finally
        {
            world.ExitReadLock();
        }

        if (drainCommittedChanges)
            world.DrainCommittedChanges();

        return true;
    }

    /// <summary>
    /// Clears all obstacles from a current physical voxel owned by the supplied grid; otherwise does nothing.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <param name="targetVoxel">A current physical voxel belonging to the supplied grid.</param>
    /// <remarks>The voxel must be the current physical voxel in the supplied grid and match its world and grid generation. A foreign, unallocated, or mismatched voxel is rejected without changing obstacle state, versions, or publishing events. Do not retain borrowed voxel references across removal, reset, or pooled reuse. For later calls, resolve a saved <see cref="WorldVoxelIndex"/> through its owning world; this validates world/grid generations and identifies a voxel address, not a persistent physical object.</remarks>
    public static void ClearObstacles(this VoxelGrid grid, Voxel targetVoxel)
    {
        byte clearedObstacleCount;
        uint gridVersion;
        bool drainCommittedChanges;
        GridWorld? world = grid.World;
        if (world == null)
            return;

        bool enteredReadLock = !world.IsWriteLockHeld;
        if (enteredReadLock)
            world.EnterReadLock();
        try
        {
            lock (grid.ObstacleSyncRoot)
            {
                lock (world.ChangeSyncRoot)
                {
                    if (!IsOwnedVoxel(grid, targetVoxel) || targetVoxel.ObstacleCount == 0)
                        return;

                    clearedObstacleCount = targetVoxel.ObstacleCount;
                    if (targetVoxel.ObstacleTracker != null)
                    {
                        SwiftHashSetPool<ObstacleToken>.Shared.Release(targetVoxel.ObstacleTracker);
                        targetVoxel.ObstacleTracker = null;
                    }

                    grid.ObstacleCount -= targetVoxel.ObstacleCount;
                    targetVoxel.ObstacleCount = 0;
                    gridVersion = grid.IncrementVersion();

                    CreateObstacleClearCommittedChange(
                        world,
                        grid,
                        targetVoxel,
                        clearedObstacleCount,
                        gridVersion,
                        out _,
                        out drainCommittedChanges);
                }
            }
        }
        finally
        {
            if (enteredReadLock)
                world.ExitReadLock();
        }

        if (drainCommittedChanges && enteredReadLock)
            world.DrainCommittedChanges();
    }

    #endregion

    #region Private Methods

    // WorldIndex is mutable; matching generations alone do not prove physical ownership.
    private static bool IsOwnedVoxel(VoxelGrid grid, Voxel targetVoxel) =>
        targetVoxel.IsValidOwnerGrid(grid)
        && grid.TryGetVoxel(targetVoxel.Index, out Voxel? currentVoxel)
        && ReferenceEquals(currentVoxel, targetVoxel);

    private static void CreateObstacleCommittedChange(
        GridWorld world,
        VoxelGrid grid,
        Voxel targetVoxel,
        GridEventKind gridEventKind,
        GridExactChangeKind exactChangeKind,
        ObstacleToken obstacleToken,
        byte obstacleCount,
        uint gridVersion,
        out GridCommittedChange committedChange,
        out bool drainCommittedChanges)
    {
        GridChangeStamp changeStamp = world.AllocateChangeStamp();
        ObstacleEventInfo obstacleEvent = new ObstacleEventInfo(
            targetVoxel.WorldIndex,
            obstacleToken,
            obstacleCount,
            gridVersion,
            changeStamp);
        GridEventInfo gridEvent = world.CreateGridEventInfo(
            grid,
            gridEventKind,
            targetVoxel.Index,
            targetVoxel.WorldPosition,
            targetVoxel.WorldPosition,
            changeStamp,
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount);
        committedChange = new GridCommittedChange(
            gridEvent,
            exactChangeKind,
            obstacleEvent,
            targetVoxel);
        targetVoxel.CachedGridVersion = gridVersion;
        drainCommittedChanges = world.EnqueueCommittedChange(committedChange);
    }

    private static void CreateObstacleClearCommittedChange(
        GridWorld world,
        VoxelGrid grid,
        Voxel targetVoxel,
        byte clearedObstacleCount,
        uint gridVersion,
        out GridCommittedChange committedChange,
        out bool drainCommittedChanges)
    {
        GridChangeStamp changeStamp = world.AllocateChangeStamp();
        ObstacleClearEventInfo clearEvent = new ObstacleClearEventInfo(
            targetVoxel.WorldIndex,
            clearedObstacleCount,
            gridVersion,
            changeStamp);
        GridEventInfo gridEvent = world.CreateGridEventInfo(
            grid,
            GridEventKind.ObstaclesCleared,
            targetVoxel.Index,
            targetVoxel.WorldPosition,
            targetVoxel.WorldPosition,
            changeStamp,
            hasVoxelState: true,
            isVoxelPresent: true,
            obstacleCount: 0);
        committedChange = new GridCommittedChange(gridEvent, clearEvent, targetVoxel);
        targetVoxel.CachedGridVersion = gridVersion;
        drainCommittedChanges = world.EnqueueCommittedChange(committedChange);
    }

    /// <summary>
    /// Notifies listeners that an obstacle was added.
    /// </summary>
    private static void NotifyObstacleAdded(ObstacleEventInfo eventInfo)
    {
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
                    GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Obstacle add error: {ex.Message}");
                }
            }
        }

        // Voxel-local delivery is handled by NotifyCommittedExact after exact identity validation.
    }

    /// <summary>
    /// Notifies listeners that an obstacle was removed.
    /// </summary>
    private static void NotifyObstacleRemoved(ObstacleEventInfo eventInfo)
    {
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
                    GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Obstacle remove error: {ex.Message}");
                }
            }
        }

        // Voxel-local delivery is handled by NotifyCommittedExact after exact identity validation.
    }

    /// <summary>
    /// Notifies listeners that all obstacles on a voxel were cleared.
    /// </summary>
    private static void NotifyObstaclesCleared(ObstacleClearEventInfo eventInfo)
    {
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
                    GridForgeLogger.Channel.Error($"[Voxel {eventInfo.VoxelIndex}] Obstacle clear error: {ex.Message}");
                }
            }
        }

        // Voxel-local delivery is handled by NotifyCommittedExact after exact identity validation.
    }

    internal static void NotifyCommittedExact(GridCommittedChange change)
    {
        switch (change.ExactKind)
        {
            case GridExactChangeKind.ObstacleAdded:
                NotifyObstacleAdded(change.ObstacleEvent);
                if (IsSameVoxel(change.TargetVoxel, change.ObstacleEvent.VoxelIndex))
                    change.TargetVoxel!.NotifyObstacleAdded(change.ObstacleEvent);
                break;
            case GridExactChangeKind.ObstacleRemoved:
                NotifyObstacleRemoved(change.ObstacleEvent);
                if (IsSameVoxel(change.TargetVoxel, change.ObstacleEvent.VoxelIndex))
                    change.TargetVoxel!.NotifyObstacleRemoved(change.ObstacleEvent);
                break;
            case GridExactChangeKind.ObstaclesCleared:
                NotifyObstaclesCleared(change.ObstacleClearEvent);
                if (IsSameVoxel(change.TargetVoxel, change.ObstacleClearEvent.VoxelIndex))
                    change.TargetVoxel!.NotifyObstaclesCleared(change.ObstacleClearEvent);
                break;
        }
    }

    private static bool IsSameVoxel(Voxel? voxel, WorldVoxelIndex index) =>
        voxel != null && voxel.IsAllocated && voxel.WorldIndex == index;

    #endregion
}
