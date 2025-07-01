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
        /// Retrieves all occupants of a specific type at a given global voxel index.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="index">The global voxel index to check for occupants.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetVoxelOccupantsByType<T>(GlobalVoxelIndex index) where T : IVoxelOccupant
        {
            return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid grid, out Voxel voxel)
                ? GetVoxelOccupantsByType<T>(grid, voxel)
                : Enumerable.Empty<T>();
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
            return grid.TryGetVoxel(position, out Voxel voxel)
                ? GetVoxelOccupantsByType<T>(grid, voxel)
                : Enumerable.Empty<T>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given voxel coordinate.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid to query.</param>
        /// <param name="index">The local voxel coordinates.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, VoxelIndex index) where T : IVoxelOccupant
        {
            return grid.TryGetVoxel(index, out Voxel voxel)
                ? GetVoxelOccupantsByType<T>(grid, voxel)
                : Enumerable.Empty<T>();
        }

        /// <summary>
        /// Retrieves all occupants of a specific type at a given voxel.
        /// </summary>
        /// <typeparam name="T">The type of occupant to retrieve.</typeparam>
        /// <param name="grid">The grid containing the voxel.</param>
        /// <param name="voxel">The target voxel.</param>
        /// <returns>An enumerable collection of occupants of the specified type.</returns>
        public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Voxel voxel) where T : IVoxelOccupant
        {
            return voxel == null
                ? Enumerable.Empty<T>()
                : GetOccupants(grid, voxel).OfType<T>();
        }

        /// <summary>
        /// Retrieves a specific occupant at a given world position using an occupant ticket.
        /// </summary>
        /// <param name="index">The global voxel index to check for an occupant.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <param name="ticket">The occupant's ticket assigned by the scancell.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            GlobalVoxelIndex index,
            int ticket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid grid, out Voxel voxel)
                && TryGetVoxelOccupant(grid, voxel, ticket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant at a given world position using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position of the voxel.</param>
        /// <param name="ticket">The occupant's ticket assigned by the scancell.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            this VoxelGrid grid,
            Vector3d position,
            int ticket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return grid.TryGetVoxel(position, out Voxel voxel)
                && TryGetVoxelOccupant(grid, voxel, ticket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant at a given voxel coordinate using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="index">The local voxel coordinates.</param>
        /// <param name="ticket">The occupant's ticket assigned by the scancell.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            this VoxelGrid grid,
            VoxelIndex index,
            int ticket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return grid.TryGetVoxel(index, out Voxel voxel)
                && TryGetVoxelOccupant(grid, voxel, ticket, out occupant);
        }

        /// <summary>
        /// Retrieves a specific occupant from a given voxel using an occupant ticket.
        /// </summary>
        /// <param name="grid">The grid containing the voxel.</param>
        /// <param name="voxel">The target voxel.</param>
        /// <param name="ticket">The occupant's ticket assigned by the scancell.</param>
        /// <param name="occupant">The retrieved occupant if found.</param>
        /// <returns>True if the occupant was found, otherwise false.</returns>
        public static bool TryGetVoxelOccupant(
            this VoxelGrid grid,
            Voxel voxel,
            int ticket,
            out IVoxelOccupant occupant)
        {
            occupant = null;
            return voxel.IsOccupied
                && grid.TryGetScanCell(voxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                && scanCell.TryGetOccupantAt(voxel.GlobalIndex, ticket, out occupant);
        }

        /// <summary>
        /// Retrieves all occupants at a given global voxel index.
        /// </summary>
        /// <param name="index">The global voxel index to check for occupants.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetOccupants(GlobalVoxelIndex index)
        {
            return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid grid, out Voxel voxel)
                ? GetOccupants(grid, voxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given world position within the grid.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="position">The world position to check for occupants.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Vector3d position)
        {
            return grid.TryGetVoxel(position, out Voxel targetVoxel)
                ? GetOccupants(grid, targetVoxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given voxel coordinate within the grid.
        /// </summary>
        /// <param name="grid">The grid to query.</param>
        /// <param name="index">The local coordinates of the voxel.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, VoxelIndex index)
        {
            return grid.TryGetVoxel(index, out Voxel targetVoxel)
                ? GetOccupants(grid, targetVoxel)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves all occupants at a given voxel.
        /// </summary>
        /// <param name="grid">The grid containing the voxel.</param>
        /// <param name="voxel">The target voxel to retrieve occupants from.</param>
        /// <returns>An enumerable collection of voxel occupants.</returns>
        public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Voxel voxel)
        {
            return voxel.IsOccupied
                && grid.TryGetScanCell(voxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetOccupants()
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            GlobalVoxelIndex index,
            Func<IVoxelOccupant, bool> occupantCondition = null,
            Func<byte, bool> groupCondition = null)
        {
            return GlobalGridManager.TryGetGridAndVoxel(index, out VoxelGrid grid, out Voxel voxel)
                ? GetConditionalOccupants(grid, voxel, occupantCondition, groupCondition)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            this VoxelGrid grid,
            Vector3d position,
            Func<IVoxelOccupant, bool> occupantCondition = null,
            Func<byte, bool> groupCondition = null)
        {
            return grid.TryGetVoxel(position, out Voxel voxel)
                ? GetConditionalOccupants(grid, voxel, occupantCondition, groupCondition)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants whose group Ids match a given condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            this VoxelGrid grid,
            VoxelIndex index,
            Func<IVoxelOccupant, bool> occupantCondition = null,
            Func<byte, bool> groupCondition = null)
        {
            return grid.TryGetVoxel(index, out Voxel voxel)
                ? GetConditionalOccupants(grid, voxel, occupantCondition, groupCondition)
                : Enumerable.Empty<IVoxelOccupant>();
        }

        /// <summary>
        /// Retrieves occupants at a given voxel that match a specified group condition.
        /// </summary>
        public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
            this VoxelGrid grid,
            Voxel targetVoxel,
            Func<IVoxelOccupant, bool> occupantCondition = null,
            Func<byte, bool> groupCondition = null)
        {
            return groupCondition != null
                && targetVoxel.IsOccupied
                && grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell scanCell)
                && scanCell.IsOccupied
                ? scanCell.GetConditionalOccupants(occupantCondition, groupCondition)
                : Enumerable.Empty<IVoxelOccupant>();
        }


        /// <summary>
        /// Scans for occupants within a given radius from a specified position.
        /// </summary>
        /// <param name="position">The center position to scan from.</param>
        /// <param name="radius">The search radius.</param>
        /// <param name="occupantCondition">Optional filter for occupants.</param>
        /// <param name="groupCondition">Optional filter for occupant groups.</param>
        /// <returns>An enumerable of all occupants found within the radius.</returns>
        public static IEnumerable<IVoxelOccupant> ScanRadius(
            Vector3d position,
            Fixed64 radius,
            Func<IVoxelOccupant, bool> occupantCondition = null,
            Func<byte, bool> groupCondition = null)
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

                IEnumerable<IVoxelOccupant> occupants = occupantCondition == null && groupCondition == null
                    ? scanCell.GetOccupants()
                    : scanCell.GetConditionalOccupants(occupantCondition, groupCondition);

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
        public static IEnumerable<T> ScanRadius<T>(
            Vector3d position, 
            Fixed64 radius,
            Func<IVoxelOccupant, bool> occupantCondition = null,
            Func<byte, bool> groupCondition = null) where T : IVoxelOccupant
        {
            return ScanRadius(position, radius, occupantCondition, groupCondition).OfType<T>();
        }
    }
}
