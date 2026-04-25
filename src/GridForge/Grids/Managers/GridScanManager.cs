using FixedMathSharp;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GridForge.Grids;

/// <summary>
/// Provides efficient querying methods for retrieving occupants within a grid.
/// Handles spatial lookups for voxels, filtering by occupant type, and fetching occupants using unique tickets.
/// </summary>
public static class GridScanManager
{
    #region Scan Methods

    /// <summary>
    /// Scans for occupants within a given radius from a specified position in the supplied world.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> ScanRadius(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        if (world == null || !world.IsActive)
            return Enumerable.Empty<IVoxelOccupant>();

        return ScanRadiusIterator(world, position, radius, occupantCondition, groupCondition);
    }

    /// <summary>
    /// Scans for occupants of a specific type within a given radius in the supplied world.
    /// </summary>
    public static IEnumerable<T> ScanRadius<T>(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null) where T : IVoxelOccupant
    {
        return ScanRadius(world, position, radius, occupantCondition, groupCondition).OfType<T>();
    }

    #endregion

    #region Occupant Registration & Retrieval

    /// <summary>
    /// Retrieves all occupants of a specific type at a given world-scoped voxel identity in the supplied world.
    /// </summary>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(GridWorld world, WorldVoxelIndex index) where T : IVoxelOccupant
    {
        return world != null && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            ? grid!.GetVoxelOccupantsByType<T>(voxel!)
            : Enumerable.Empty<T>();
    }

    /// <summary>
    /// Retrieves all occupants of a specific type at a given world position.
    /// </summary>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Vector3d position) where T : IVoxelOccupant
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            ? grid.GetVoxelOccupantsByType<T>(voxel!)
            : Enumerable.Empty<T>();
    }

    /// <summary>
    /// Retrieves all occupants of a specific type at a given voxel coordinate.
    /// </summary>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, VoxelIndex index) where T : IVoxelOccupant
    {
        return grid.TryGetVoxel(index, out Voxel? voxel)
            ? grid.GetVoxelOccupantsByType<T>(voxel!)
            : Enumerable.Empty<T>();
    }

    /// <summary>
    /// Retrieves all occupants of a specific type at a given voxel.
    /// </summary>
    public static IEnumerable<T> GetVoxelOccupantsByType<T>(this VoxelGrid grid, Voxel voxel) where T : IVoxelOccupant
    {
        return voxel == null
            ? Enumerable.Empty<T>()
            : grid.GetOccupants(voxel).OfType<T>();
    }

    /// <summary>
    /// Retrieves a specific occupant at a given world-scoped voxel identity using an occupant ticket in the supplied world.
    /// </summary>
    public static bool TryGetVoxelOccupant(
        GridWorld world,
        WorldVoxelIndex index,
        int ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return world != null
            && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            && grid!.TryGetVoxelOccupant(voxel!, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves a specific occupant at a given world position using an occupant ticket.
    /// </summary>
    public static bool TryGetVoxelOccupant(
        this VoxelGrid grid,
        Vector3d position,
        int ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return grid.TryGetVoxel(position, out Voxel? voxel)
            && grid.TryGetVoxelOccupant(voxel!, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves a specific occupant at a given voxel coordinate using an occupant ticket.
    /// </summary>
    public static bool TryGetVoxelOccupant(
        this VoxelGrid grid,
        VoxelIndex index,
        int ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return grid.TryGetVoxel(index, out Voxel? voxel)
            && grid.TryGetVoxelOccupant(voxel!, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves a specific occupant from a given voxel using an occupant ticket.
    /// </summary>
    public static bool TryGetVoxelOccupant(
        this VoxelGrid grid,
        Voxel voxel,
        int ticket,
        out IVoxelOccupant? occupant)
    {
        occupant = null;
        return voxel.IsOccupied
            && grid.TryGetScanCell(voxel.ScanCellKey, out ScanCell? scanCell)
            && scanCell!.IsOccupied
            && scanCell.TryGetOccupantAt(voxel.WorldIndex, ticket, out occupant);
    }

    /// <summary>
    /// Retrieves all occupants at a given world-scoped voxel identity in the supplied world.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetOccupants(GridWorld world, WorldVoxelIndex index)
    {
        return world != null && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            ? grid!.GetOccupants(voxel!)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves all occupants at a given world position within the grid.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Vector3d position)
    {
        return grid.TryGetVoxel(position, out Voxel? targetVoxel)
            ? grid.GetOccupants(targetVoxel!)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves all occupants at a given voxel coordinate within the grid.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, VoxelIndex index)
    {
        return grid.TryGetVoxel(index, out Voxel? targetVoxel)
            ? grid.GetOccupants(targetVoxel!)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves all occupants at a given voxel.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetOccupants(this VoxelGrid grid, Voxel voxel)
    {
        return voxel.IsOccupied
            && grid.TryGetScanCell(voxel.ScanCellKey, out ScanCell? scanCell)
            && scanCell!.IsOccupied
            ? scanCell.GetOccupants()
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves occupants whose group Ids match a given condition at a world-scoped voxel identity in the supplied world.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        GridWorld world,
        WorldVoxelIndex index,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        return world != null && world.TryGetGridAndVoxel(index, out VoxelGrid? grid, out Voxel? voxel)
            ? grid!.GetConditionalOccupants(voxel!, occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves occupants whose group Ids match a given condition.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        this VoxelGrid grid,
        Vector3d position,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        return grid.TryGetVoxel(position, out Voxel? voxel)
            ? grid.GetConditionalOccupants(voxel!, occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves occupants whose group Ids match a given condition.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        this VoxelGrid grid,
        VoxelIndex index,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        return grid.TryGetVoxel(index, out Voxel? voxel)
            ? grid.GetConditionalOccupants(voxel!, occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    /// <summary>
    /// Retrieves occupants at a given voxel that match a specified group condition.
    /// </summary>
    public static IEnumerable<IVoxelOccupant> GetConditionalOccupants(
        this VoxelGrid grid,
        Voxel targetVoxel,
        Func<IVoxelOccupant, bool>? occupantCondition = null,
        Func<byte, bool>? groupCondition = null)
    {
        return targetVoxel != null
            && targetVoxel.IsOccupied
            && grid.TryGetScanCell(targetVoxel.ScanCellKey, out ScanCell? scanCell)
            && scanCell!.IsOccupied
            ? scanCell.GetConditionalOccupants(occupantCondition, groupCondition)
            : Enumerable.Empty<IVoxelOccupant>();
    }

    #endregion

    #region Private Methods

    private static IEnumerable<IVoxelOccupant> ScanRadiusIterator(
        GridWorld world,
        Vector3d position,
        Fixed64 radius,
        Func<IVoxelOccupant, bool>? occupantCondition,
        Func<byte, bool>? groupCondition)
    {
        Fixed64 squaredRadius = radius * radius;
        SwiftList<IVoxelOccupant> results = SwiftListPool<IVoxelOccupant>.Shared.Rent();

        Vector3d boundsMin = position - radius;
        Vector3d boundsMax = position + radius;

        foreach (ScanCell scanCell in GridTracer.GetCoveredScanCells(world, boundsMin, boundsMax))
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

    #endregion
}
