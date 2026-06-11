using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Utility;
using System.Collections.Generic;

namespace GridForge.Utility;

/// <summary>
/// Provides utilities for tracing lines or bounding areas in a grid, aligning them to grid voxels.
/// Uses fixed-point calculations to ensure deterministic and accurate grid traversal.
/// </summary>
public static class GridTracer
{
    private readonly struct TraceLinePlan
    {
        public readonly Vector3d SnappedMin;
        public readonly Vector3d SnappedMax;
        public readonly Vector3d TraceStart;
        public readonly Fixed64 Steps;
        public readonly Fixed64 StepX;
        public readonly Fixed64 StepY;
        public readonly Fixed64 StepZ;

        public TraceLinePlan(
            Vector3d snappedMin,
            Vector3d snappedMax,
            Vector3d traceStart,
            Fixed64 steps,
            Fixed64 stepX,
            Fixed64 stepY,
            Fixed64 stepZ)
        {
            SnappedMin = snappedMin;
            SnappedMax = snappedMax;
            TraceStart = traceStart;
            Steps = steps;
            StepX = stepX;
            StepY = stepY;
            StepZ = stepZ;
        }
    }

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
    /// Traces a 2D XZ-plane line between two points in the supplied world, snapping them to grid coordinates.
    /// </summary>
    /// <remarks>
    /// This method maps <see cref="Vector2d.X"/> to world X, <see cref="Vector2d.Y"/> to world Z,
    /// and <paramref name="layerY"/> to world Y. The default layer is world Y = 0.
    /// </remarks>
    /// <param name="world">The world whose grids should be traced.</param>
    /// <param name="start">Starting XZ-plane position in world space.</param>
    /// <param name="end">Ending XZ-plane position in world space.</param>
    /// <param name="padding">Value applied to the start/end positions before snapping.</param>
    /// <param name="includeEnd">Whether to include the end voxel in the traced line.</param>
    /// <param name="layerY">The world Y layer to trace. Defaults to zero.</param>
    /// <returns>A collection of <see cref="GridVoxelSet"/> objects representing the traced path.</returns>
    public static IEnumerable<GridVoxelSet> TraceLine(
        GridWorld world,
        Vector2d start,
        Vector2d end,
        Fixed64? padding = null,
        bool includeEnd = true,
        Fixed64 layerY = default)
    {
        Vector3d start3D = GridPlane2d.ToWorld(start, layerY);
        Vector3d end3D = GridPlane2d.ToWorld(end, layerY);

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
    /// Retrieves all grid voxels covered by the given XZ-plane bounding area on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The 2D minimum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="boundsMax">The 2D maximum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
    /// <returns>A collection of <see cref="GridVoxelSet"/> objects representing the covered voxels.</returns>
    public static IEnumerable<GridVoxelSet> GetCoveredVoxels(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        return GetCoveredVoxels(world, min, max, padding);
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

    /// <summary>
    /// Retrieves all scan cells within the given XZ-plane bounding area on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The 2D minimum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="boundsMax">The 2D maximum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
    /// <returns>An enumerable of covered scan cells grouped by grid.</returns>
    public static IEnumerable<ScanCell> GetCoveredScanCells(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        return GetCoveredScanCells(world, min, max, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with scan cells covered by the supplied bounding area.
    /// </summary>
    public static void GetCoveredScanCellsInto(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ScanCell> results,
        Fixed64? padding = null)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddCoveredScanCellsTo(world, boundsMin, boundsMax, results, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with scan cells covered by the supplied XZ-plane bounding area.
    /// </summary>
    public static void GetCoveredScanCellsInto(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        SwiftList<ScanCell> results,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        GetCoveredScanCellsInto(world, min, max, results, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections.
    /// </summary>
    public static void GetCoveredScanCellsInto(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ScanCell> results,
        GridScanScratch scratch,
        Fixed64? padding = null)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));
        SwiftThrowHelper.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddCoveredScanCellsTo(world, boundsMin, boundsMax, results, scratch, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections for an XZ-plane bounding area.
    /// </summary>
    public static void GetCoveredScanCellsInto(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        SwiftList<ScanCell> results,
        GridScanScratch scratch,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        GetCoveredScanCellsInto(world, min, max, results, scratch, padding);
    }

    /// <summary>
    /// Appends covered scan cells without allocating an iterator for hot-path callers.
    /// </summary>
    internal static void AddCoveredScanCellsTo(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ScanCell> scanCells,
        Fixed64? padding = null)
    {
        SwiftHashSet<ushort> processedGrids = SwiftHashSetPool<ushort>.Shared.Rent();
        SwiftHashSet<ScanCell> voxelRedundancyCheck = SwiftHashSetPool<ScanCell>.Shared.Rent();

        try
        {
            AddCoveredScanCellsCore(
                world,
                boundsMin,
                boundsMax,
                scanCells,
                processedGrids,
                voxelRedundancyCheck,
                padding);
        }
        finally
        {
            SwiftHashSetPool<ushort>.Shared.Release(processedGrids);
            SwiftHashSetPool<ScanCell>.Shared.Release(voxelRedundancyCheck);
        }
    }

    /// <summary>
    /// Appends covered scan cells using caller-owned scratch state for allocation-sensitive scans.
    /// </summary>
    internal static void AddCoveredScanCellsTo(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ScanCell> scanCells,
        GridScanScratch scratch,
        Fixed64? padding = null)
    {
        scratch.Clear();
        AddCoveredScanCellsCore(
            world,
            boundsMin,
            boundsMax,
            scanCells,
            scratch.ProcessedGrids,
            scratch.ScanCellRedundancy,
            padding);
    }

    private static void AddCoveredScanCellsCore(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ushort> processedGrids,
        SwiftHashSet<ScanCell> voxelRedundancyCheck,
        Fixed64? padding = null)
    {
        (Vector3d snappedMin, Vector3d snappedMax) =
            world.SnapBoundsToVoxelSize(boundsMin, boundsMax, padding);
        (int cellXMin, int cellYMin, int cellZMin, int cellXMax, int cellYMax, int cellZMax) =
            world.GetSpatialGridCellBounds(snappedMin, snappedMax);

        for (int cellZ = cellZMin; cellZ <= cellZMax; cellZ++)
        {
            for (int cellY = cellYMin; cellY <= cellYMax; cellY++)
            {
                for (int cellX = cellXMin; cellX <= cellXMax; cellX++)
                    AddCoveredScanCellsForSpatialCell(
                        world,
                        cellX,
                        cellY,
                        cellZ,
                        snappedMin,
                        snappedMax,
                        scanCells,
                        processedGrids,
                        voxelRedundancyCheck);
            }
        }
    }

    private static void AddCoveredScanCellsForSpatialCell(
        GridWorld world,
        int cellX,
        int cellY,
        int cellZ,
        Vector3d snappedMin,
        Vector3d snappedMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ushort> processedGrids,
        SwiftHashSet<ScanCell> voxelRedundancyCheck)
    {
        int cellIndex = SwiftHashTools.CombineHashCodes(cellX, cellY, cellZ);
        if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
            return;

        foreach (ushort gridIndex in gridList)
        {
            if (!world.ActiveGrids.IsAllocated(gridIndex) || !processedGrids.Add(gridIndex))
                continue;

            AddCoveredScanCellsForGrid(
                world.ActiveGrids[gridIndex],
                snappedMin,
                snappedMax,
                scanCells,
                voxelRedundancyCheck);
        }
    }

    private static void AddCoveredScanCellsForGrid(
        VoxelGrid currentGrid,
        Vector3d snappedMin,
        Vector3d snappedMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> voxelRedundancyCheck)
    {
        (int xMin, int yMin, int zMin) = currentGrid.SnapToScanCell(snappedMin);
        (int xMax, int yMax, int zMax) = currentGrid.SnapToScanCell(snappedMax);

        AddScanCellsInRange(
            currentGrid,
            xMin,
            yMin,
            zMin,
            xMax,
            yMax,
            zMax,
            scanCells,
            voxelRedundancyCheck);
    }

    private static void AddScanCellsInRange(
        VoxelGrid currentGrid,
        int xMin,
        int yMin,
        int zMin,
        int xMax,
        int yMax,
        int zMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> voxelRedundancyCheck)
    {
        for (int x = xMin; x <= xMax; x++)
        {
            for (int y = yMin; y <= yMax; y++)
            {
                for (int z = zMin; z <= zMax; z++)
                    TryAddScanCell(currentGrid, x, y, z, scanCells, voxelRedundancyCheck);
            }
        }
    }

    private static bool TryAddScanCell(
        VoxelGrid currentGrid,
        int x,
        int y,
        int z,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> voxelRedundancyCheck)
    {
        int scanCellKey = currentGrid.GetScanCellKey(x, y, z);
        if (scanCellKey == -1
            || !currentGrid.TryGetScanCell(scanCellKey, out ScanCell? scanCell)
            || voxelRedundancyCheck.Add(scanCell!) != true)
        {
            return false;
        }

        scanCells.Add(scanCell!);
        return true;
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

        try
        {
            AddTraceLineVoxelsToMapping(
                world,
                start,
                end,
                padding,
                gridVoxelMapping,
                voxelRedundancyCheck);

            AddTraceLineEndVoxel(
                world,
                end,
                includeEnd,
                gridVoxelMapping,
                voxelRedundancyCheck);

            foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
                yield return new GridVoxelSet(kvp.Key, kvp.Value);
        }
        finally
        {
            ReleaseGridVoxelMapping(gridVoxelMapping);
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
        }
    }

    private static void AddTraceLineVoxelsToMapping(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        TraceLinePlan plan = CreateTraceLinePlan(world, start, end, padding);

        foreach (int cellIndex in world.GetSpatialGridCells(plan.SnappedMin, plan.SnappedMax))
        {
            if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                continue;

            AddTraceLineVoxelsForCell(
                world,
                gridList,
                plan,
                gridVoxelMapping,
                voxelRedundancyCheck);
        }
    }

    private static TraceLinePlan CreateTraceLinePlan(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding)
    {
        (Vector3d snappedMin, Vector3d snappedMax) =
            world.SnapBoundsToVoxelSize(start, end, padding);

        Vector3d traceStart = CreateTraceEndpoint(start, end, snappedMin, snappedMax, useMinWhenIncreasing: true);
        Vector3d traceEnd = CreateTraceEndpoint(start, end, snappedMin, snappedMax, useMinWhenIncreasing: false);

        Vector3d diff = traceEnd - traceStart;
        Vector3d delta = Vector3d.Abs(diff);
        Fixed64 steps = FixedMath.Ceil(FixedMath.Max(FixedMath.Max(delta.X, delta.Y), delta.Z));

        return new TraceLinePlan(
            snappedMin,
            snappedMax,
            traceStart,
            steps,
            diff.X / (steps + Fixed64.One),
            diff.Y / (steps + Fixed64.One),
            diff.Z / (steps + Fixed64.One));
    }

    private static Vector3d CreateTraceEndpoint(
        Vector3d start,
        Vector3d end,
        Vector3d snappedMin,
        Vector3d snappedMax,
        bool useMinWhenIncreasing)
    {
        // Preserve the caller's trace direction while still using snapped bounds for coverage lookup.
        return new Vector3d(
            SelectTraceCoordinate(start.X, end.X, snappedMin.X, snappedMax.X, useMinWhenIncreasing),
            SelectTraceCoordinate(start.Y, end.Y, snappedMin.Y, snappedMax.Y, useMinWhenIncreasing),
            SelectTraceCoordinate(start.Z, end.Z, snappedMin.Z, snappedMax.Z, useMinWhenIncreasing));
    }

    private static Fixed64 SelectTraceCoordinate(
        Fixed64 start,
        Fixed64 end,
        Fixed64 snappedMin,
        Fixed64 snappedMax,
        bool useMinWhenIncreasing)
    {
        return (start <= end) == useMinWhenIncreasing ? snappedMin : snappedMax;
    }

    private static void AddTraceLineVoxelsForCell(
        GridWorld world,
        SwiftHashSet<ushort> gridList,
        TraceLinePlan plan,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        foreach (ushort gridIndex in gridList)
        {
            if (!world.ActiveGrids.IsAllocated(gridIndex))
                continue;

            VoxelGrid currentGrid = world.ActiveGrids[gridIndex];
            if (gridVoxelMapping.ContainsKey(currentGrid))
                continue;

            SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
            gridVoxelMapping.Add(currentGrid, voxelList);

            AddTraceLineGridVoxels(
                world,
                currentGrid,
                plan,
                voxelList,
                voxelRedundancyCheck);
        }
    }

    private static void AddTraceLineGridVoxels(
        GridWorld world,
        VoxelGrid currentGrid,
        TraceLinePlan plan,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        for (Fixed64 i = Fixed64.Zero; i <= plan.Steps; i += Fixed64.One)
        {
            Vector3d tracePos = world.FloorToVoxelSize(
                new Vector3d(
                    plan.TraceStart.X + plan.StepX * i,
                    plan.TraceStart.Y + plan.StepY * i,
                    plan.TraceStart.Z + plan.StepZ * i));

            if (!currentGrid.TryGetVoxel(tracePos, out Voxel? voxel) || voxelRedundancyCheck.Add(voxel!) != true)
                continue;

            voxelList.Add(voxel!);
        }
    }

    private static void AddTraceLineEndVoxel(
        GridWorld world,
        Vector3d end,
        bool includeEnd,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (!includeEnd
            || !world.TryGetGridAndVoxel(end, out VoxelGrid? endGrid, out Voxel? endVoxel)
            || !gridVoxelMapping.TryGetValue(endGrid!, out SwiftList<Voxel> endVoxelList)
            || !voxelRedundancyCheck.Add(endVoxel!))
        {
            return;
        }

        endVoxelList.Add(endVoxel!);
    }

    private static void ReleaseGridVoxelMapping(SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping)
    {
        foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
            SwiftListPool<Voxel>.Shared.Release(kvp.Value);
    }

    private static IEnumerable<GridVoxelSet> GetCoveredVoxelsIterator(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding)
    {
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping = new();
        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();

        try
        {
            AddCoveredVoxelsToMapping(
                world,
                boundsMin,
                boundsMax,
                padding,
                gridVoxelMapping,
                voxelRedundancyCheck);

            foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
                yield return new GridVoxelSet(kvp.Key, kvp.Value);
        }
        finally
        {
            ReleaseGridVoxelMapping(gridVoxelMapping);
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
        }
    }

    private static void AddCoveredVoxelsToMapping(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        (Vector3d snappedMin, Vector3d snappedMax) =
            world.SnapBoundsToVoxelSize(boundsMin, boundsMax, padding);

        foreach (int cellIndex in world.GetSpatialGridCells(snappedMin, snappedMax))
        {
            if (world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                AddCoveredVoxelsForCell(world, gridList, snappedMin, snappedMax, gridVoxelMapping, voxelRedundancyCheck);
        }
    }

    private static void AddCoveredVoxelsForCell(
        GridWorld world,
        SwiftHashSet<ushort> gridList,
        Vector3d snappedMin,
        Vector3d snappedMax,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        foreach (ushort gridIndex in gridList)
        {
            if (!world.ActiveGrids.IsAllocated(gridIndex))
                continue;

            VoxelGrid currentGrid = world.ActiveGrids[gridIndex];
            if (gridVoxelMapping.ContainsKey(currentGrid))
                continue;

            SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
            gridVoxelMapping.Add(currentGrid, voxelList);
            AddCoveredGridVoxels(world, currentGrid, snappedMin, snappedMax, voxelList, voxelRedundancyCheck);
        }
    }

    private static void AddCoveredGridVoxels(
        GridWorld world,
        VoxelGrid currentGrid,
        Vector3d snappedMin,
        Vector3d snappedMax,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        Fixed64 resolution = world.VoxelSize;
        for (Fixed64 x = snappedMin.X; x <= snappedMax.X; x += resolution)
        {
            for (Fixed64 y = snappedMin.Y; y <= snappedMax.Y; y += resolution)
            {
                for (Fixed64 z = snappedMin.Z; z <= snappedMax.Z; z += resolution)
                    TryAddCoveredVoxel(currentGrid, new Vector3d(x, y, z), voxelList, voxelRedundancyCheck);
            }
        }
    }

    private static bool TryAddCoveredVoxel(
        VoxelGrid currentGrid,
        Vector3d position,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (!currentGrid.TryGetVoxel(position, out Voxel? voxel)
            || voxelRedundancyCheck.Add(voxel!) != true)
        {
            return false;
        }

        voxelList.Add(voxel!);
        return true;
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

        try
        {
            AddCoveredScanCellsCore(
                world,
                boundsMin,
                boundsMax,
                scanCells,
                processedGrids,
                voxelRedundancyCheck,
                padding);

            foreach (ScanCell scanCell in scanCells)
                yield return scanCell;
        }
        finally
        {
            SwiftListPool<ScanCell>.Shared.Release(scanCells);
            SwiftHashSetPool<ushort>.Shared.Release(processedGrids);
            SwiftHashSetPool<ScanCell>.Shared.Release(voxelRedundancyCheck);
        }
    }
}
