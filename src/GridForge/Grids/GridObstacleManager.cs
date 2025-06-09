using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace GridForge.Grids
{
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
        public static Action<GridChange, GlobalVoxelIndex> OnObstacleChange;

        #endregion

        #region Private Fields

        /// <summary>
        /// Per-grid locks for ensuring thread-safe obstacle operations.
        /// </summary>
        private static readonly ConcurrentDictionary<ushort, object> _gridLocks = new ConcurrentDictionary<ushort, object>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Attempts to add an obstacle at the given world position.
        /// </summary>
        public static bool TryAddObstacle(this VoxelGrid grid, Vector3d position, int obstacleSpawnToken)
        {
            return grid.TryGetVoxelIndex(position, out VoxelIndex voxelIndex)
                && TryAddObstacle(grid, voxelIndex, obstacleSpawnToken);
        }

        /// <summary>
        /// Adds an obstacle to a given voxel within the grid.
        /// </summary>
        public static bool TryAddObstacle(this VoxelGrid grid, VoxelIndex voxelIndex, int obstacleSpawnToken)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                && TryAddObstacle(grid, targetVoxel, obstacleSpawnToken);
        }

        /// <summary>
        /// Adds an obstacle to this voxel.
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="targetVoxel"></param>
        /// <param name="obstacleSpawnToken"></param>
        /// <exception cref="Exception"></exception>
        public static bool TryAddObstacle(this VoxelGrid grid, Voxel targetVoxel, int obstacleSpawnToken)
        {
            if (!targetVoxel.IsBlockable)
                return false;

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                targetVoxel.ObstacleTracker ??= new SwiftHashSet<int>();
                if (!targetVoxel.ObstacleTracker.Add(obstacleSpawnToken))
                    return false;
                targetVoxel.ObstacleCount++;

                grid.ObstacleCount++;
                grid.Version++;
            }

            NotifyObstacleChange(GridChange.Add, targetVoxel, grid.Version);

            return true;
        }

        /// <summary>
        /// Attempts to remove an obstacle from the specified world position.
        /// </summary>
        public static bool TryRemoveObstacle(this VoxelGrid grid, Vector3d position, int obstacleSpawnToken)
        {
            return grid.TryGetVoxelIndex(position, out VoxelIndex voxelIndex)
                && TryRemoveObstacle(grid, voxelIndex, obstacleSpawnToken);
        }

        /// <summary>
        /// Attempts to remove an obstacle at the specified voxel index.
        /// </summary>
        public static bool TryRemoveObstacle(this VoxelGrid grid, VoxelIndex voxelIndex, int obstacleSpawnToken)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                && TryRemoveObstacle(grid, targetVoxel, obstacleSpawnToken);
        }

        /// <summary>
        /// Removes an obstacle from a given voxel.
        /// </summary>
        public static bool TryRemoveObstacle(this VoxelGrid grid, Voxel targetVoxel, int obstacleSpawnToken)
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
                    targetVoxel.ObstacleTracker = null;
                    targetVoxel.ObstacleCount = 0;
                }

                grid.ObstacleCount--;
                grid.Version++;
            }

            NotifyObstacleChange(GridChange.Remove, targetVoxel, grid.Version);

            return true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Notifies listeners of an obstacle state change.
        /// </summary>
        private static void NotifyObstacleChange(GridChange change, Voxel targetVoxel, uint gridVersion)
        {
            try
            {
                OnObstacleChange?.Invoke(change, targetVoxel.GlobalIndex);
                targetVoxel.OnObstacleChange?.Invoke(change, targetVoxel);
                targetVoxel.CachedGridVersion = gridVersion;
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error(
                    $"[Voxel {targetVoxel.GlobalIndex}] Obstacle change error: {ex.Message} | Change: {change}");
            }
        }

        #endregion
    }
}
