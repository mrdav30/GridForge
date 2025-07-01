using FixedMathSharp;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GridForge.Grids
{
    /// <summary>
    /// Provides efficient querying methods for retrieving occupants within a grid.
    /// Handles spatial lookups for voxels, filtering by occupant type, and fetching occupants using unique tickets.
    /// </summary>
    public static class ScanManager
    {
        /// <summary>
        /// Attempts to register the occupant with the current voxel it is on.
        /// </summary>
        public static bool TryRegister(IVoxelOccupant occupant)
        {
            if(!GlobalGridManager.TryGetGridAndVoxel(occupant.Position, out VoxelGrid grid, out Voxel voxel))
                return false;

            return grid.TryAddVoxelOccupant(voxel, occupant);
        }

        /// <summary>
        /// Attempts to de-register the occupant from the voxel it was on.
        /// </summary>
        public static bool TryDeregister(IVoxelOccupant occupant)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(occupant.Position, out VoxelGrid grid, out Voxel voxel))
                return false;

            return grid.TryRemoveVoxelOccupant(voxel, occupant);
        }


        /// <summary>
        /// Retrieves all occupants at a given world position within the grid.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position to check for occupants.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetVoxelOccupants(this VoxelGrid grid, Vector3d position)
        {
            return grid.TryGetVoxel(position, out Voxel targetVoxel)
                ? GetVoxelOccupants(grid, targetVoxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given voxel coordinate within the grid.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="voxelIndex">The local coordinates of the voxel.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetVoxelOccupants(this VoxelGrid grid, VoxelIndex voxelIndex)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                ? GetVoxelOccupants(grid, targetVoxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given voxel.
        /// </summary>
        /// <param name="grid">The grid containing the voxel.</param>
        /// <param name="targetVoxel">The target voxel to retrieve occupants from.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetVoxelOccupants(this VoxelGrid grid, Voxel targetVoxel)
        {
            return targetVoxel.IsOccupied
                && grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetOccupantsFor(targetVoxel.GlobalIndex)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given world position.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position of the voxel.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Vector3d position) where T : IVoxelOccupant
        {
            return grid.TryGetVoxel(position, out Voxel targetVoxel)
                ? GetVoxelOccupantsByType<T>(grid, targetVoxel)
                : Enumerable.Empty<T>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given voxel coordinate.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid to query.</param>
        /// <param name="voxelIndex">The local voxel coordinates.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, VoxelIndex voxelIndex) where T : IVoxelOccupant
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                ? GetVoxelOccupantsByType<T>(grid, targetVoxel)
                : Enumerable.Empty<T>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given voxel.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid containing the voxel.</param>
        /// <param name="targetVoxel">The target voxel.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Voxel targetVoxel) where T : IVoxelOccupant
        {
            return targetVoxel == null
                ? Enumerable.Empty<T>()
                : GetVoxelOccupants(grid, targetVoxel).OfType<T>();
        }

        /// <summary>
        /// Retrieves a specific occupant at a given world position using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position of the voxel.</param>
        /// <param name="occupantTicket">The unique identifier of the occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            this VoxelGrid grid,
            Vector3d position,
            int occupantTicket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return grid.TryGetVoxel(position, out Voxel voxel)
                && TryGetVoxelOccupant(grid, voxel, occupantTicket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant at a given voxel coordinate using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="voxelIndex">The local voxel coordinates.</param>
        /// <param name="occupantTicket">The unique identifier of the occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            this VoxelGrid grid,
            VoxelIndex voxelIndex,
            int occupantTicket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                && TryGetVoxelOccupant(grid, targetVoxel, occupantTicket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant from a given voxel using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid containing the voxel.</param>
        /// <param name="targetVoxel">The target voxel.</param>
        /// <param name="occupantTicket">The unique identifier of the occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            this VoxelGrid grid,
            Voxel targetVoxel,
            int occupantTicket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return targetVoxel.IsOccupied
                && grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                && scanCell.TryGetOccupantAt(targetVoxel.GlobalIndex, occupantTicket, out occupant);
        }

        /// <summary>
        /// Retrieves all occupants at a given world position within the grid.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Vector3d position)
        {
            return grid.TryGetVoxel(position, out Voxel targetVoxel)
                ? GetOccupants(grid, targetVoxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given coordinate within the grid.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, VoxelIndex voxelIndex)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                ? GetOccupants(grid, targetVoxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given voxel.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Voxel targetVoxel)
        {
            return targetVoxel.IsOccupied
                && grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetOccupants()
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            this VoxelGrid grid,
            Vector3d position,
            Func<byte, bool> groupConditional)
        {
            return grid.TryGetVoxel(position, out Voxel targetVoxel)
                ? GetConditionalOccupants(grid, targetVoxel, groupConditional)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            this VoxelGrid grid,
            VoxelIndex voxelIndex,
            Func<byte, bool> groupConditional)
        {
            return grid.TryGetVoxel(voxelIndex, out Voxel targetVoxel)
                ? GetConditionalOccupants(grid, targetVoxel, groupConditional)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants at a given voxel that match a specified group condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            this VoxelGrid grid,
            Voxel targetVoxel,
            Func<byte, bool> groupConditional)
        {
            return groupConditional != null
                && targetVoxel.IsOccupied
                && grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetConditionalOccupants(groupConditional)
                : Enumerable.Empty<IVoxelOccupant>();
        }


        /// <summary>
        /// Scans for occupants within a given radius from a specified position.
        /// </summary>
        /// <param name="position">The center position to scan from.</param>
        /// <param name="radius">The search radius.</param>
        /// <param name="groupConditional">Optional filter for occupant groups.</param>
        /// <returns>An enumerable of all occupants found within the radius.</returns>
        public static IEnumerable<IVoxelOccupant> ScanRadius(
            Vector3d position,
            Fixed64 radius,
            Func<byte, bool> groupConditional = null)
        {
            Fixed64 squaredRadius = radius * radius;
            SwiftList<IVoxelOccupant> results = SwiftListPool<IVoxelOccupant>.Shared.Rent();

            // Get bounds for the search area
            Vector3d boundsMin = position - radius;
            Vector3d boundsMax = position + radius;

            foreach (ScanCell scanCell in GridTracer.GetCoveredScanCells(boundsMin, boundsMax))
            {
                if (!scanCell.IsOccupied)
                    continue;

                IEnumerable<IVoxelOccupant> occupants = groupConditional == null
                    ? scanCell.GetOccupants()
                    : scanCell.GetConditionalOccupants(groupConditional);

                foreach (IVoxelOccupant occupant in occupants)
                {
                    if ((occupant.Position - position).SqrMagnitude <= squaredRadius)
                        results.Add(occupant);
                }
            }

            foreach (IVoxelOccupant result in results)
                yield return result;

            SwiftListPool<IVoxelOccupant>.Shared.Release(results);
        }

        /// <summary>
        /// Scans for occupants of a specific type within a given radius.
        /// </summary>
        public static IEnumerable<T> ScanRadius<T>(Vector3d position, Fixed64 radius) where T : IVoxelOccupant
        {
            return ScanRadius(position, radius).OfType<T>();
        }
    }
}
