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
        /// Maximum number of obstacles that can exist on a single node.
        /// </summary>
        public const byte MaxObstacleCount = byte.MaxValue;

        /// <summary>
        /// Event triggered when an obstacle is added or removed.
        /// </summary>
        public static Action<GridChange, CoordinatesGlobal> OnObstacleChange;

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
        public static bool TryAddObstacle(this Grid grid, Vector3d position, int obstacleSpawnToken)
        {
            return grid.TryGetNodeCoordinates(position, out CoordinatesLocal targetCoordinates)
                && TryAddObstacle(grid, targetCoordinates, obstacleSpawnToken);
        }

        /// <summary>
        /// Adds an obstacle to a given node within the grid.
        /// </summary>
        public static bool TryAddObstacle(this Grid grid, CoordinatesLocal coordinatesLocal, int obstacleSpawnToken)
        {
            return grid.TryGetNode(coordinatesLocal, out Node targetNode)
                && TryAddObstacle(grid, targetNode, obstacleSpawnToken);
        }

        /// <summary>
        /// Adds an obstacle to this node.
        /// </summary>
        /// <param name="grid"></param>
        /// <param name="targetNode"></param>
        /// <param name="obstacleSpawnToken"></param>
        /// <exception cref="Exception"></exception>
        public static bool TryAddObstacle(this Grid grid, Node targetNode, int obstacleSpawnToken)
        {
            if (!targetNode.IsBlockable)
                return false;

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                targetNode.ObstacleTracker ??= new SwiftHashSet<int>();
                if (!targetNode.ObstacleTracker.Add(obstacleSpawnToken))
                    return false;
                targetNode.ObstacleCount++;

                grid.ObstacleCount++;
                grid.Version++;
            }

            NotifyObstacleChange(GridChange.Add, targetNode, grid.Version);

            return true;
        }

        /// <summary>
        /// Attempts to remove an obstacle from the specified world position.
        /// </summary>
        public static bool TryRemoveObstacle(this Grid grid, Vector3d position, int obstacleSpawnToken)
        {
            return grid.TryGetNodeCoordinates(position, out CoordinatesLocal targetCoordinates)
                && TryRemoveObstacle(grid, targetCoordinates, obstacleSpawnToken);
        }

        /// <summary>
        /// Attempts to remove an obstacle at the specified node coordinates.
        /// </summary>
        public static bool TryRemoveObstacle(this Grid grid, CoordinatesLocal coordinatesLocal, int obstacleSpawnToken)
        {
            return grid.TryGetNode(coordinatesLocal, out Node targetNode)
                && TryRemoveObstacle(grid, targetNode, obstacleSpawnToken);
        }

        /// <summary>
        /// Removes an obstacle from a given node.
        /// </summary>
        public static bool TryRemoveObstacle(this Grid grid, Node targetNode, int obstacleSpawnToken)
        {
            if (targetNode.ObstacleCount == 0)
            {
                GridForgeLogger.Warn($"No obstacle to remove on node ({targetNode.GlobalCoordinates})!");
                return false;
            }

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                if (!targetNode.ObstacleTracker.Remove(obstacleSpawnToken))
                    return false;

                if (--targetNode.ObstacleCount <= 0)
                {
                    targetNode.ObstacleTracker = null;
                    targetNode.ObstacleCount = 0;
                }

                grid.ObstacleCount--;
                grid.Version++;
            }

            NotifyObstacleChange(GridChange.Remove, targetNode, grid.Version);

            return true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Notifies listeners of an obstacle state change.
        /// </summary>
        private static void NotifyObstacleChange(GridChange change, Node targetNode, uint gridVersion)
        {
            try
            {
                OnObstacleChange?.Invoke(change, targetNode.GlobalCoordinates);
                targetNode.CachedGridVersion = gridVersion;
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error(
                    $"[Node {targetNode.GlobalCoordinates}] Obstacle change error: {ex.Message} | Change: {change}");
            }
        }

        #endregion
    }
}
