using GridForge.Spatial;
using SwiftCollections.Pool;
using System;
using System.Collections.Concurrent;
namespace GridForge.Grids
{
    /// <summary>
    /// Provides utility methods for managing voxel occupants within a grid.
    /// Supports adding, removing, and retrieving occupants with thread-safe operations.
    /// </summary>
    public static class GridOccupantManager
    {
        #region Constants & Events

        /// <summary>
        /// Maximum number of occupants allowed per voxel.
        /// </summary>
        public const byte MaxOccupantCount = byte.MaxValue;

        /// <summary>
        /// Event triggered when an occupant is added or removed.
        /// </summary>
        public static Action<GridChange, GlobalVoxelIndex> OnOccupantChange;

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
        public static bool TryAddVoxelOccupant(this VoxelGrid grid, IVoxelOccupant occupant)
        {
            return grid.TryGetVoxel(occupant.Position, out Voxel voxel)
                && TryAddVoxelOccupant(grid, voxel, occupant);
        }

        /// <summary>
        /// Attempts to add an occupant at the specified voxel index.
        /// </summary>
        public static bool TryAddVoxelOccupant(this VoxelGrid grid, VoxelIndex voxelIndex, IVoxelOccupant occupant)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                && TryAddVoxelOccupant(grid, targetVoxel, occupant);
        }

        /// <summary>
        /// Adds an occupant to the grid.
        /// </summary>
        public static bool TryAddVoxelOccupant(this VoxelGrid grid, Voxel targetVoxel, IVoxelOccupant occupant)
        {
            if (occupant == null)
                return false;

            if (!occupant.OccupyingIndex.Equals(default))
            {
                GridForgeLogger.Error($"Occupant {nameof(occupant)} is already an occupant of another voxel.");
                return false;
            }

            if (!targetVoxel.HasVacancy || !grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell))
                return false;

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                scanCell.AddOccupant(targetVoxel.SpawnToken, occupant, out int occupantTicket);
                grid.ActiveScanCells ??= SwiftHashSetPool<int>.Shared.Rent();
                if (!grid.ActiveScanCells.Contains(targetVoxel.ScanCellKey))
                    grid.ActiveScanCells.Add(targetVoxel.ScanCellKey);

                targetVoxel.OccupantCount++;

                occupant.OccupantTicket = occupantTicket;
                occupant.OccupyingIndex = targetVoxel.GlobalIndex;
            }

            NotifyOccupantChange(GridChange.Add, targetVoxel);

            return true;
        }

        /// <summary>
        /// Attempts to remove an occupant from the given world position.
        /// </summary>
        public static bool TryRemoveVoxelOccupant(this VoxelGrid grid, IVoxelOccupant occupant)
        {
            return grid.TryGetVoxel(occupant.Position, out Voxel voxel)
                && TryRemoveVoxelOccupant(grid, voxel, occupant);
        }

        /// <summary>
        /// Attempts to remove an occupant at the specified voxel coordinates.
        /// </summary>
        public static bool TryRemoveVoxelOccupant(this VoxelGrid grid, VoxelIndex voxelIndex, IVoxelOccupant occupant)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                && TryRemoveVoxelOccupant(grid, targetVoxel, occupant);
        }

        /// <summary>
        /// Removes an occupant from this grid.
        /// </summary>
        public static bool TryRemoveVoxelOccupant(this VoxelGrid grid, Voxel targetVoxel, IVoxelOccupant occupant)
        {
            if (occupant == null)
                return false;

            if (occupant.OccupyingIndex.Equals(default))
            {
                GridForgeLogger.Error($"Occupant {nameof(occupant)} is not currently an occupant of any voxel.");
                return false;
            }

            if (!targetVoxel.IsOccupied || !grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell))
                return false;

            bool success = false;

            object gridLock = _gridLocks.GetOrAdd(grid.GlobalIndex, _ => new object());

            lock (gridLock)
            {
                success = scanCell.TryRemoveOccupant(targetVoxel.SpawnToken, occupant);
                if (success)
                {
                    if (!scanCell.IsOccupied)
                    {
                        grid.ActiveScanCells.Remove(targetVoxel.ScanCellKey);
                        if (!grid.IsOccupied)
                        {
                            GridForgeLogger.Info($"Releasing unused active scan cells collection.");
                            SwiftHashSetPool<int>.Shared.Release(grid.ActiveScanCells);
                            grid.ActiveScanCells = null;
                        }
                    }

                    targetVoxel.OccupantCount--;
                }

                // Reset occupant data regardless of success
                occupant.OccupantTicket = -1;
                occupant.OccupyingIndex = default;
            }

            NotifyOccupantChange(GridChange.Remove, targetVoxel);

            return success;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Notifies listeners of an occupant state change.
        /// </summary>
        private static void NotifyOccupantChange(GridChange change, Voxel targetVoxel)
        {
            try
            {
                OnOccupantChange?.Invoke(change, targetVoxel.GlobalIndex);
                targetVoxel.OnOccupantChange?.Invoke(change, targetVoxel);
            }
            catch (Exception ex)
            {
                GridForgeLogger.Error(
                    $"[Voxel {targetVoxel.GlobalIndex}] Occupant change error: {ex.Message} | Change: {change}");
            }
        }

        #endregion
    }
}
