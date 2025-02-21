using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace GridForge.Grids
{
    /// <summary>
    /// Provides utility methods for managing node occupants within a grid.
    /// Supports adding, removing, and retrieving occupants with thread-safe operations.
    /// </summary>
    public static class GridOccupantManager
    {
        #region Constants & Events

        /// <summary>
        /// Maximum number of occupants allowed per node.
        /// </summary>
        public const byte MaxOccupantCount = byte.MaxValue;

        /// <summary>
        /// Event triggered when an occupant is added or removed.
        /// </summary>
        public static Action<GridChange, CoordinatesGlobal> OnOccupantChange;

        #endregion

        #region Private Fields

        /// <summary>
        /// Per-grid locks to ensure thread safety when modifying occupant data.
        /// </summary>
        private static readonly ConcurrentDictionary<ushort, object> _gridLocks = new ConcurrentDictionary<ushort, object>();

        #endregion

        #region Occupant Management

        /// <summary>
        /// Attempts to add an occupant at the given world position.
        /// </summary>
        public static bool TryAddNodeOccupant(this Grid grid, INodeOccupant occupant)
        {
            return grid.TryGetNodeCoordinates(occupant.WorldPosition, out CoordinatesLocal targetCoordinates)
                && TryAddNodeOccupant(grid, targetCoordinates, occupant);
        }

        /// <summary>
        /// Attempts to add an occupant at the given world position.
        /// </summary>
        public static bool TryAddNodeOccupant(this Grid grid, Vector3d position, INodeOccupant occupant)
        {
            return grid.TryGetNodeCoordinates(position, out CoordinatesLocal targetCoordinates)
                && TryAddNodeOccupant(grid, targetCoordinates, occupant);
        }

        /// <summary>
        /// Attempts to add an occupant at the specified node coordinates.
        /// </summary>
        public static bool TryAddNodeOccupant(this Grid grid, CoordinatesLocal coordinatesLocal, INodeOccupant occupant)
        {
            return grid.TryGetNode(coordinatesLocal, out Node targetNode)
                && TryAddNodeOccupant(grid, targetNode, occupant);
        }

        /// <summary>
        /// Adds an occupant to the grid.
        /// </summary>
        public static bool TryAddNodeOccupant(this Grid grid, Node targetNode, INodeOccupant occupant)
        {
            if (occupant == null)
                return false;

            if (occupant.IsNodeOccupant)
            {
                GridForgeLogger.Error($"Occupant {nameof(occupant)} is already an occupant of another node.");
                return false;
            }

            if (!targetNode.HasVacancy || !grid.TryGetScanCell(targetNode.ScanCellKey, out ScanCell scanCell))
                return false;

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                scanCell.AddOccupant(targetNode.SpawnToken, occupant, out int occupantTicket);
                grid.ActiveScanCells ??= SwiftHashSetPool<int>.Shared.Rent();
                if (!grid.ActiveScanCells.Contains(targetNode.ScanCellKey))
                    grid.ActiveScanCells.Add(targetNode.ScanCellKey);

                targetNode.OccupantCount++;

                occupant.IsNodeOccupant = true;
                occupant.OccupantTicket = occupantTicket;
                occupant.GridCoordinates = targetNode.GlobalCoordinates;
            }

            NotifyOccupantChange(GridChange.Add, targetNode);

            return true;
        }

        /// <summary>
        /// Attempts to remove an occupant from the given world position.
        /// </summary>
        public static bool TryRemoveNodeOccupant(this Grid grid, INodeOccupant occupant)
        {
            return grid.TryGetNodeCoordinates(occupant.WorldPosition, out CoordinatesLocal targetCoordinates)
                && TryRemoveNodeOccupant(grid, targetCoordinates, occupant);
        }

        /// <summary>
        /// Attempts to remove an occupant from the given world position.
        /// </summary>
        public static bool TryRemoveNodeOccupant(this Grid grid, Vector3d position, INodeOccupant occupant)
        {
            return grid.TryGetNodeCoordinates(position, out CoordinatesLocal targetCoordinates)
                && TryRemoveNodeOccupant(grid, targetCoordinates, occupant);
        }

        /// <summary>
        /// Attempts to remove an occupant at the specified node coordinates.
        /// </summary>
        public static bool TryRemoveNodeOccupant(this Grid grid, CoordinatesLocal coordinatesLocal, INodeOccupant occupant)
        {
            return grid.TryGetNode(coordinatesLocal, out Node targetNode)
                && TryRemoveNodeOccupant(grid, targetNode, occupant);
        }

        /// <summary>
        /// Removes an occupant from this grid.
        /// </summary>
        public static bool TryRemoveNodeOccupant(this Grid grid, Node targetNode, INodeOccupant occupant)
        {
            if (occupant == null)
                return false;

            if (!occupant.IsNodeOccupant || occupant.OccupantTicket == -1)
            {
                GridForgeLogger.Error($"Occupant {nameof(occupant)} is not currently an occupant of any node.");
                return false;
            }

            if (!targetNode.IsOccupied || !grid.TryGetScanCell(targetNode.ScanCellKey, out ScanCell scanCell))
                return false;

            bool success = false;

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                success = scanCell.TryRemoveOccupant(targetNode.SpawnToken, occupant);
                if (success)
                {
                    if (!scanCell.IsOccupied)
                    {
                        grid.ActiveScanCells.Remove(targetNode.ScanCellKey);
                        if (!grid.IsOccupied)
                        {
                            GridForgeLogger.Info($"Releasing unused active scan cells collection.");
                            SwiftHashSetPool<int>.Shared.Release(grid.ActiveScanCells);
                            grid.ActiveScanCells = null;
                        }
                    }

                    targetNode.OccupantCount--;
                }

                // Reset occupant data regardless of success
                occupant.IsNodeOccupant = false;
                occupant.OccupantTicket = -1;
                occupant.GridCoordinates = default;
            }

            NotifyOccupantChange(GridChange.Remove, targetNode);

            return success;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Notifies listeners of an occupant state change.
        /// </summary>
        private static void NotifyOccupantChange(GridChange change, Node targetNode)
        {
            try
            {
                OnOccupantChange?.Invoke(change, targetNode.GlobalCoordinates);
                targetNode.OnOccupantChange?.Invoke(change, targetNode);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error(
                    $"[Node {targetNode.GlobalCoordinates}] Occupant change error: {ex.Message} | Change: {change}");
            }
        }

        #endregion
    }
}
