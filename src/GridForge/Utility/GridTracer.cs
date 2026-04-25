using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using SwiftCollections.Pool;
using System.Collections.Generic;

namespace GridForge.Utility;

/// <summary>
/// Provides utilities for tracing lines or bounding areas in a grid, aligning them to grid voxels.
/// Uses fixed-point calculations to ensure deterministic and accurate grid traversal.
/// </summary>
public static class GridTracer
{
    /// <summary>
    /// Traces a 3D line between two points in the supplied world.
    /// The traced points are returned as grid voxels.
    /// </summary>
    /// <remarks>
    /// Uses a fractional step algorithm inspired by Bresenham’s line algorithm.
    /// This implementation leverages fixed-point math to maintain precision across a deterministic grid.
    /// </remarks>
    /// <param name="world">The world whose grids should be traced.</param>
    /// <param name="start">Starting position in world space.</param>
    /// <param name="end">Ending position in world space.</param>
    /// <param name="padding">Value applied to the start/end positions before snapping.</param>
    /// <param name="includeEnd">Whether to include the end voxel in the traced line.</param>
    /// <returns>A collection of <see cref="GridVoxelSet"/> objects representing the traced path.</returns>
    public static IEnumerable<GridVoxelSet> TraceLine(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding = null,
        bool includeEnd = true)
    {
        if (world == null || !world.IsActive)
            return System.Array.Empty<GridVoxelSet>();

        return TraceLineIterator(world, start, end, padding, includeEnd);
    }

    /// <summary>
    /// Traces a 2D line between two points in the supplied world, snapping them to grid coordinates.
    /// </summary>
    /// <remarks>
    /// This method projects the 2D line onto the X-Y plane and follows the closest grid-aligned path.
    /// </remarks>
    public static IEnumerable<GridVoxelSet> TraceLine(
        GridWorld world,
        Vector2d start,
        Vector2d end,
        Fixed64? padding = null,
        bool includeEnd = true)
    {
        Vector3d start3D = start.ToVector3d(Fixed64.Zero);
        Vector3d end3D = end.ToVector3d(Fixed64.Zero);

        return TraceLine(world, start3D, end3D, padding, includeEnd);
    }

    /// <summary>
    /// Retrieves all grid voxels covered by the given bounding area in the supplied world.
    /// </summary>
    public static IEnumerable<GridVoxelSet> GetCoveredVoxels(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding = null)
    {
        if (world == null || !world.IsActive)
            return System.Array.Empty<GridVoxelSet>();

        return GetCoveredVoxelsIterator(world, boundsMin, boundsMax, padding);
    }

    /// <summary>
    /// Retrieves all scan cells within the given bounding area across relevant grids in the supplied world.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The minimum corner of the bounding area.</param>
    /// <param name="boundsMax">The maximum corner of the bounding area.</param>
    /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
    /// <returns>An enumerable of covered scan cells grouped by grid.</returns>
    public static IEnumerable<ScanCell> GetCoveredScanCells(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding = null)
    {
        if (world == null || !world.IsActive)
            return System.Array.Empty<ScanCell>();

        return GetCoveredScanCellsIterator(world, boundsMin, boundsMax, padding);
    }

    private static IEnumerable<GridVoxelSet> TraceLineIterator(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd)
    {
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping = new();
        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();

        (Vector3d snappedMin, Vector3d snappedMax) =
            world.SnapBoundsToVoxelSize(start, end, padding);

        // Preserve the caller's trace direction while still using snapped bounds for coverage lookup.
        Vector3d traceStart = new(
            start.x <= end.x ? snappedMin.x : snappedMax.x,
            start.y <= end.y ? snappedMin.y : snappedMax.y,
            start.z <= end.z ? snappedMin.z : snappedMax.z);
        Vector3d traceEnd = new(
            start.x <= end.x ? snappedMax.x : snappedMin.x,
            start.y <= end.y ? snappedMax.y : snappedMin.y,
            start.z <= end.z ? snappedMax.z : snappedMin.z);

        Vector3d diff = traceEnd - traceStart;
        Vector3d delta = Vector3d.Abs(diff);

        Fixed64 steps = FixedMath.Ceiling(FixedMath.Max(FixedMath.Max(delta.x, delta.y), delta.z));

        Fixed64 stepX = diff.x / (steps + Fixed64.One);
        Fixed64 stepY = diff.y / (steps + Fixed64.One);
        Fixed64 stepZ = diff.z / (steps + Fixed64.One);

        foreach (int cellIndex in world.GetSpatialGridCells(snappedMin, snappedMax))
        {
            if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                continue;

            foreach (ushort gridIndex in gridList)
            {
                if (!world.ActiveGrids.IsAllocated(gridIndex))
                    continue;

                VoxelGrid currentGrid = world.ActiveGrids[gridIndex];
                if (gridVoxelMapping.ContainsKey(currentGrid))
                    continue;

                SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
                gridVoxelMapping.Add(currentGrid, voxelList);

                for (Fixed64 i = Fixed64.Zero; i <= steps; i += Fixed64.One)
                {
                    Vector3d tracePos = world.FloorToVoxelSize(
                        new Vector3d(
                            traceStart.x + stepX * i,
                            traceStart.y + stepY * i,
                            traceStart.z + stepZ * i));

                    if (!currentGrid.TryGetVoxel(tracePos, out Voxel? voxel) || voxelRedundancyCheck.Add(voxel!) != true)
                        continue;

                    voxelList.Add(voxel!);
                }
            }
        }

        if (includeEnd
            && world.TryGetGridAndVoxel(end, out VoxelGrid? endGrid, out Voxel? endVoxel)
            && gridVoxelMapping.TryGetValue(endGrid!, out SwiftList<Voxel> endVoxelList)
            && voxelRedundancyCheck.Add(endVoxel!))
        {
            endVoxelList.Add(endVoxel!);
        }

        foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
        {
            yield return new GridVoxelSet(kvp.Key, kvp.Value);
            SwiftListPool<Voxel>.Shared.Release(kvp.Value);
        }

        SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
    }

    private static IEnumerable<GridVoxelSet> GetCoveredVoxelsIterator(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding)
    {
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping = new();
        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();

        (Vector3d snappedMin, Vector3d snappedMax) =
            world.SnapBoundsToVoxelSize(boundsMin, boundsMax, padding);

        foreach (int cellIndex in world.GetSpatialGridCells(snappedMin, snappedMax))
        {
            if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                continue;

            foreach (ushort gridIndex in gridList)
            {
                if (!world.ActiveGrids.IsAllocated(gridIndex))
                    continue;

                VoxelGrid currentGrid = world.ActiveGrids[gridIndex];
                if (gridVoxelMapping.ContainsKey(currentGrid))
                    continue;

                SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
                gridVoxelMapping.Add(currentGrid, voxelList);

                Fixed64 resolution = world.VoxelSize;
                for (Fixed64 x = snappedMin.x; x <= snappedMax.x; x += resolution)
                {
                    for (Fixed64 y = snappedMin.y; y <= snappedMax.y; y += resolution)
                    {
                        for (Fixed64 z = snappedMin.z; z <= snappedMax.z; z += resolution)
                        {
                            Vector3d position = new(x, y, z);
                            if (!currentGrid.TryGetVoxel(position, out Voxel? voxel) || voxelRedundancyCheck.Add(voxel!) != true)
                                continue;

                            voxelList.Add(voxel!);
                        }
                    }
                }
            }
        }

        foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
        {
            yield return new GridVoxelSet(kvp.Key, kvp.Value);
            SwiftListPool<Voxel>.Shared.Release(kvp.Value);
        }

        SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
    }

    private static IEnumerable<ScanCell> GetCoveredScanCellsIterator(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding)
    {
        SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();
        SwiftHashSet<ushort> processedGrids = SwiftHashSetPool<ushort>.Shared.Rent();
        SwiftHashSet<ScanCell> voxelRedundancyCheck = SwiftHashSetPool<ScanCell>.Shared.Rent();

        (Vector3d snappedMin, Vector3d snappedMax) =
            world.SnapBoundsToVoxelSize(boundsMin, boundsMax, padding);

        foreach (int cellIndex in world.GetSpatialGridCells(snappedMin, snappedMax))
        {
            if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                continue;

            foreach (ushort gridIndex in gridList)
            {
                if (!world.ActiveGrids.IsAllocated(gridIndex) || !processedGrids.Add(gridIndex))
                    continue;

                VoxelGrid currentGrid = world.ActiveGrids[gridIndex];

                (int xMin, int yMin, int zMin) = currentGrid.SnapToScanCell(snappedMin);
                (int xMax, int yMax, int zMax) = currentGrid.SnapToScanCell(snappedMax);

                for (int x = xMin; x <= xMax; x++)
                {
                    for (int y = yMin; y <= yMax; y++)
                    {
                        for (int z = zMin; z <= zMax; z++)
                        {
                            int scanCellKey = currentGrid.GetScanCellKey(x, y, z);
                            if (scanCellKey == -1
                                || !currentGrid.TryGetScanCell(scanCellKey, out ScanCell? scanCell)
                                || voxelRedundancyCheck.Add(scanCell!) != true)
                            {
                                continue;
                            }

                            scanCells.Add(scanCell!);
                        }
                    }
                }
            }
        }

        foreach (ScanCell scanCell in scanCells)
            yield return scanCell;

        SwiftListPool<ScanCell>.Shared.Release(scanCells);
        SwiftHashSetPool<ushort>.Shared.Release(processedGrids);
        SwiftHashSetPool<ScanCell>.Shared.Release(voxelRedundancyCheck);
    }
}
