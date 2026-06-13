//=======================================================================
// GridTracer.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Utility;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Utility;

/// <summary>
/// Provides utilities for tracing lines or bounding areas in a grid, aligning them to grid voxels.
/// Uses fixed-point calculations to ensure deterministic and accurate grid traversal.
/// </summary>
public static class GridTracer
{
    private readonly struct TraceLinePlan
    {
        public readonly Vector3d TraceStart;
        public readonly Fixed64 Steps;
        public readonly Fixed64 StepX;
        public readonly Fixed64 StepY;
        public readonly Fixed64 StepZ;

        public TraceLinePlan(
            Vector3d traceStart,
            Fixed64 steps,
            Fixed64 stepX,
            Fixed64 stepY,
            Fixed64 stepZ)
        {
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
        (Vector3d queryMin, Vector3d queryMax) =
            CreatePaddedOrderedBounds(boundsMin, boundsMax, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);
        (int cellXMin, int cellYMin, int cellZMin, int cellXMax, int cellYMax, int cellZMax) =
            world.GetSpatialGridCellBounds(candidateMin, candidateMax);

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
                        queryMin,
                        queryMax,
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
        Vector3d queryMin,
        Vector3d queryMax,
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
                queryMin,
                queryMax,
                scanCells,
                voxelRedundancyCheck);
        }
    }

    private static void AddCoveredScanCellsForGrid(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> voxelRedundancyCheck)
    {
        if (currentGrid.Topology.Kind == GridTopologyKind.HexPrism)
        {
            AddCoveredHexScanCellsForGrid(
                currentGrid,
                queryMin,
                queryMax,
                scanCells,
                voxelRedundancyCheck);
            return;
        }

        if (!TryGetCoveredScanCellRange(
            currentGrid,
            queryMin,
            queryMax,
            out int xMin,
            out int yMin,
            out int zMin,
            out int xMax,
            out int yMax,
            out int zMax))
        {
            return;
        }

        currentGrid.AddScanCellsInRange(
            xMin,
            yMin,
            zMin,
            xMax,
            yMax,
            zMax,
            scanCells,
            voxelRedundancyCheck);
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
        SwiftHashSet<ushort> processedGrids = SwiftHashSetPool<ushort>.Shared.Rent();

        try
        {
            AddTraceLineVoxelsToMapping(
                world,
                start,
                end,
                padding,
                gridVoxelMapping,
                voxelRedundancyCheck,
                processedGrids);

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
            SwiftHashSetPool<ushort>.Shared.Release(processedGrids);
        }
    }

    private static void AddTraceLineVoxelsToMapping(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftHashSet<ushort> processedGrids)
    {
        (Vector3d queryMin, Vector3d queryMax) = CreatePaddedOrderedBounds(start, end, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);

        (int cellXMin, int cellYMin, int cellZMin, int cellXMax, int cellYMax, int cellZMax) =
            world.GetSpatialGridCellBounds(candidateMin, candidateMax);

        for (int cellZ = cellZMin; cellZ <= cellZMax; cellZ++)
        {
            for (int cellY = cellYMin; cellY <= cellYMax; cellY++)
            {
                for (int cellX = cellXMin; cellX <= cellXMax; cellX++)
                {
                    int cellIndex = SwiftHashTools.CombineHashCodes(cellX, cellY, cellZ);
                    if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                        continue;

                    AddTraceLineVoxelsForCell(
                        world,
                        gridList,
                        start,
                        end,
                        padding,
                        gridVoxelMapping,
                        voxelRedundancyCheck,
                        processedGrids);
                }
            }
        }
    }

    private static TraceLinePlan CreateTraceLinePlan(
        VoxelGrid grid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding)
    {
        (Vector3d snappedMin, Vector3d snappedMax) =
            grid.NormalizeBounds(start, end, padding);

        Vector3d traceStart = CreateTraceEndpoint(start, end, snappedMin, snappedMax, useMinWhenIncreasing: true);
        Vector3d traceEnd = CreateTraceEndpoint(start, end, snappedMin, snappedMax, useMinWhenIncreasing: false);

        Vector3d diff = traceEnd - traceStart;
        Fixed64 steps = CalculateTraceSteps(grid, diff);

        return new TraceLinePlan(
            traceStart,
            steps,
            diff.X / (steps + Fixed64.One),
            diff.Y / (steps + Fixed64.One),
            diff.Z / (steps + Fixed64.One));
    }

    private static Fixed64 CalculateTraceSteps(VoxelGrid grid, Vector3d diff)
    {
        Vector3d delta = Vector3d.Abs(diff);
        Fixed64 stepX = delta.X / grid.Topology.Metrics.CellWidth;
        Fixed64 stepY = delta.Y / grid.Topology.Metrics.LayerHeight;
        Fixed64 stepZ = delta.Z / grid.Topology.Metrics.CellLength;
        return FixedMath.Ceil(FixedMath.Max(FixedMath.Max(stepX, stepY), stepZ));
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

    private static (Vector3d min, Vector3d max) CreatePaddedOrderedBounds(
        Vector3d min,
        Vector3d max,
        Fixed64? padding)
    {
        Fixed64 fixedPadding = padding.HasValue && padding.Value > Fixed64.Zero
            ? padding.Value
            : Fixed64.Zero;

        min -= fixedPadding;
        max += fixedPadding;

        (min.X, max.X) = min.X > max.X ? (max.X, min.X) : (min.X, max.X);
        (min.Y, max.Y) = min.Y > max.Y ? (max.Y, min.Y) : (min.Y, max.Y);
        (min.Z, max.Z) = min.Z > max.Z ? (max.Z, min.Z) : (min.Z, max.Z);

        return (min, max);
    }

    private static (Vector3d min, Vector3d max) ExpandOrderedBounds(
        Vector3d min,
        Vector3d max,
        Fixed64 expansion)
    {
        if (expansion <= Fixed64.Zero)
            return (min, max);

        return (
            new Vector3d(min.X - expansion, min.Y - expansion, min.Z - expansion),
            new Vector3d(max.X + expansion, max.Y + expansion, max.Z + expansion));
    }

    private static bool TryGetCoveredScanCellRange(
        VoxelGrid grid,
        Vector3d queryMin,
        Vector3d queryMax,
        out int xMin,
        out int yMin,
        out int zMin,
        out int xMax,
        out int yMax,
        out int zMax)
    {
        xMin = 0;
        yMin = 0;
        zMin = 0;
        xMax = 0;
        yMax = 0;
        zMax = 0;

        (Vector3d snappedMin, Vector3d snappedMax) = grid.NormalizeBounds(queryMin, queryMax);
        if (!TopologyVoxelRangeUtility.TryClipBoundsToGrid(grid, snappedMin, snappedMax, out Vector3d clippedMin, out Vector3d clippedMax))
            return false;

        (xMin, yMin, zMin) = grid.SnapToScanCell(clippedMin);
        (xMax, yMax, zMax) = grid.SnapToScanCell(clippedMax);
        OrderScanCellRange(ref xMin, ref yMin, ref zMin, ref xMax, ref yMax, ref zMax);
        return true;
    }

    private static void OrderScanCellRange(
        ref int xMin,
        ref int yMin,
        ref int zMin,
        ref int xMax,
        ref int yMax,
        ref int zMax)
    {
        if (xMin > xMax)
            (xMin, xMax) = (xMax, xMin);
        if (yMin > yMax)
            (yMin, yMax) = (yMax, yMin);
        if (zMin > zMax)
            (zMin, zMax) = (zMax, zMin);
    }

    private static void AddTraceLineVoxelsForCell(
        GridWorld world,
        SwiftHashSet<ushort> gridList,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftHashSet<ushort> processedGrids)
    {
        foreach (ushort gridIndex in gridList)
        {
            if (!world.ActiveGrids.IsAllocated(gridIndex) || !processedGrids.Add(gridIndex))
                continue;

            VoxelGrid currentGrid = world.ActiveGrids[gridIndex];
            SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();

            AddTraceLineGridVoxels(
                currentGrid,
                start,
                end,
                padding,
                voxelList,
                voxelRedundancyCheck);

            if (voxelList.Count > 0)
                gridVoxelMapping.Add(currentGrid, voxelList);
            else
                SwiftListPool<Voxel>.Shared.Release(voxelList);
        }
    }

    private static void AddTraceLineGridVoxels(
        VoxelGrid currentGrid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (currentGrid.Topology.Kind == GridTopologyKind.HexPrism)
        {
            AddHexTraceLineGridVoxels(
                currentGrid,
                start,
                end,
                padding,
                voxelList,
                voxelRedundancyCheck);
            return;
        }

        TraceLinePlan plan = CreateTraceLinePlan(currentGrid, start, end, padding);

        for (Fixed64 i = Fixed64.Zero; i <= plan.Steps; i += Fixed64.One)
        {
            Vector3d tracePos = currentGrid.FloorToGrid(
                new Vector3d(
                    plan.TraceStart.X + plan.StepX * i,
                    plan.TraceStart.Y + plan.StepY * i,
                    plan.TraceStart.Z + plan.StepZ * i));

            if (!currentGrid.TryGetVoxel(tracePos, out Voxel? voxel) || voxelRedundancyCheck.Add(voxel!) != true)
                continue;

            voxelList.Add(voxel!);
        }
    }

    private static void AddHexTraceLineGridVoxels(
        VoxelGrid currentGrid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (!TryCreateHexTraceEndpoints(
            currentGrid,
            start,
            end,
            padding,
            out VoxelIndex startIndex,
            out VoxelIndex endIndex))
        {
            return;
        }

        int steps = CalculateHexTraceSteps(startIndex, endIndex);
        if (steps == 0)
        {
            AddTraceVoxelByIndex(currentGrid, startIndex, voxelList, voxelRedundancyCheck);
            return;
        }

        Fixed64 stepCount = new Fixed64(steps);
        for (int i = 0; i < steps; i++)
        {
            Fixed64 t = new Fixed64(i) / stepCount;
            VoxelIndex traceIndex = InterpolateHexTraceIndex(startIndex, endIndex, t);
            AddTraceVoxelByIndex(currentGrid, traceIndex, voxelList, voxelRedundancyCheck);
        }
    }

    private static bool TryCreateHexTraceEndpoints(
        VoxelGrid grid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        out VoxelIndex startIndex,
        out VoxelIndex endIndex)
    {
        startIndex = default;
        endIndex = default;

        (Vector3d snappedMin, Vector3d snappedMax) = grid.NormalizeBounds(start, end, padding);
        Vector3d traceStart = grid.FloorToGrid(CreateTraceEndpoint(
            start,
            end,
            snappedMin,
            snappedMax,
            useMinWhenIncreasing: true));
        Vector3d traceEnd = grid.FloorToGrid(CreateTraceEndpoint(
            start,
            end,
            snappedMin,
            snappedMax,
            useMinWhenIncreasing: false));

        return grid.TryGetVoxelIndex(traceStart, out startIndex)
            && grid.TryGetVoxelIndex(traceEnd, out endIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CalculateHexTraceSteps(VoxelIndex start, VoxelIndex end)
    {
        int qDelta = System.Math.Abs(end.x - start.x);
        int rDelta = System.Math.Abs(end.z - start.z);
        int sDelta = System.Math.Abs((-end.x - end.z) - (-start.x - start.z));
        int planarSteps = System.Math.Max(qDelta, System.Math.Max(rDelta, sDelta));
        int verticalSteps = System.Math.Abs(end.y - start.y);
        return System.Math.Max(planarSteps, verticalSteps);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VoxelIndex InterpolateHexTraceIndex(VoxelIndex start, VoxelIndex end, Fixed64 t)
    {
        Fixed64 q = Interpolate(new Fixed64(start.x), new Fixed64(end.x), t);
        Fixed64 y = Interpolate(new Fixed64(start.y), new Fixed64(end.y), t);
        Fixed64 r = Interpolate(new Fixed64(start.z), new Fixed64(end.z), t);
        return HexCoordinateUtility.RoundAxial(q, y, r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 Interpolate(Fixed64 start, Fixed64 end, Fixed64 t) =>
        start + (end - start) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddTraceVoxelByIndex(
        VoxelGrid grid,
        VoxelIndex index,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (grid.TryGetVoxel(index, out Voxel? voxel)
            && voxel != null
            && voxelRedundancyCheck.Add(voxel))
        {
            voxelList.Add(voxel);
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
            || !voxelRedundancyCheck.Add(endVoxel!))
        {
            return;
        }

        if (!gridVoxelMapping.TryGetValue(endGrid!, out SwiftList<Voxel> endVoxelList))
        {
            endVoxelList = SwiftListPool<Voxel>.Shared.Rent();
            gridVoxelMapping.Add(endGrid!, endVoxelList);
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
        SwiftHashSet<ushort> processedGrids = SwiftHashSetPool<ushort>.Shared.Rent();

        try
        {
            AddCoveredVoxelsToMapping(
                world,
                boundsMin,
                boundsMax,
                padding,
                gridVoxelMapping,
                voxelRedundancyCheck,
                processedGrids);

            foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
                yield return new GridVoxelSet(kvp.Key, kvp.Value);
        }
        finally
        {
            ReleaseGridVoxelMapping(gridVoxelMapping);
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
            SwiftHashSetPool<ushort>.Shared.Release(processedGrids);
        }
    }

    private static void AddCoveredVoxelsToMapping(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftHashSet<ushort> processedGrids)
    {
        (Vector3d queryMin, Vector3d queryMax) =
            CreatePaddedOrderedBounds(boundsMin, boundsMax, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);

        (int cellXMin, int cellYMin, int cellZMin, int cellXMax, int cellYMax, int cellZMax) =
            world.GetSpatialGridCellBounds(candidateMin, candidateMax);

        for (int cellZ = cellZMin; cellZ <= cellZMax; cellZ++)
        {
            for (int cellY = cellYMin; cellY <= cellYMax; cellY++)
            {
                for (int cellX = cellXMin; cellX <= cellXMax; cellX++)
                {
                    int cellIndex = SwiftHashTools.CombineHashCodes(cellX, cellY, cellZ);
                    if (world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridList))
                    {
                        AddCoveredVoxelsForCell(
                            world,
                            gridList,
                            queryMin,
                            queryMax,
                            gridVoxelMapping,
                            voxelRedundancyCheck,
                            processedGrids);
                    }
                }
            }
        }
    }

    private static void AddCoveredVoxelsForCell(
        GridWorld world,
        SwiftHashSet<ushort> gridList,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftHashSet<ushort> processedGrids)
    {
        foreach (ushort gridIndex in gridList)
        {
            if (!world.ActiveGrids.IsAllocated(gridIndex) || !processedGrids.Add(gridIndex))
                continue;

            VoxelGrid currentGrid = world.ActiveGrids[gridIndex];
            SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
            AddCoveredGridVoxels(currentGrid, queryMin, queryMax, voxelList, voxelRedundancyCheck);

            if (voxelList.Count > 0)
                gridVoxelMapping.Add(currentGrid, voxelList);
            else
                SwiftListPool<Voxel>.Shared.Release(voxelList);
        }
    }

    private static void AddCoveredGridVoxels(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (currentGrid.Topology.Kind == GridTopologyKind.HexPrism)
        {
            AddCoveredHexGridVoxels(
                currentGrid,
                queryMin,
                queryMax,
                voxelList,
                voxelRedundancyCheck);
            return;
        }

        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
            currentGrid,
            queryMin,
            queryMax,
            out VoxelIndex minIndex,
            out VoxelIndex maxIndex))
        {
            return;
        }

        currentGrid.AddVoxelsInIndexRange(minIndex, maxIndex, voxelList, voxelRedundancyCheck);
    }

    private static void AddCoveredHexGridVoxels(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
            currentGrid,
            queryMin,
            queryMax,
            out VoxelIndex minIndex,
            out VoxelIndex maxIndex))
        {
            return;
        }

        Fixed64 horizontalExpansion = currentGrid.Topology.Metrics.CellRadius;
        Fixed64 coverageMinX = queryMin.X - horizontalExpansion;
        Fixed64 coverageMaxX = queryMax.X + horizontalExpansion;
        Fixed64 coverageMinZ = queryMin.Z - horizontalExpansion;
        Fixed64 coverageMaxZ = queryMax.Z + horizontalExpansion;

        for (int x = minIndex.x; x <= maxIndex.x; x++)
        {
            for (int y = minIndex.y; y <= maxIndex.y; y++)
            {
                for (int z = minIndex.z; z <= maxIndex.z; z++)
                {
                    if (currentGrid.TryGetVoxel(x, y, z, out Voxel? voxel)
                        && voxel != null
                        && IsHexVoxelCenterInHorizontalCoverage(
                            voxel,
                            coverageMinX,
                            coverageMaxX,
                            coverageMinZ,
                            coverageMaxZ)
                        && voxelRedundancyCheck.Add(voxel))
                    {
                        voxelList.Add(voxel);
                    }
                }
            }
        }
    }

    private static void AddCoveredHexScanCellsForGrid(
        VoxelGrid currentGrid,
        Vector3d queryMin,
        Vector3d queryMax,
        SwiftList<ScanCell> scanCells,
        SwiftHashSet<ScanCell> scanCellRedundancyCheck)
    {
        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
            currentGrid,
            queryMin,
            queryMax,
            out VoxelIndex minIndex,
            out VoxelIndex maxIndex))
        {
            return;
        }

        currentGrid.AddScanCellsInRange(
            minIndex.x / currentGrid.ScanCellSize,
            minIndex.y / currentGrid.ScanCellSize,
            minIndex.z / currentGrid.ScanCellSize,
            maxIndex.x / currentGrid.ScanCellSize,
            maxIndex.y / currentGrid.ScanCellSize,
            maxIndex.z / currentGrid.ScanCellSize,
            scanCells,
            scanCellRedundancyCheck);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsHexVoxelCenterInHorizontalCoverage(
        Voxel voxel,
        Fixed64 coverageMinX,
        Fixed64 coverageMaxX,
        Fixed64 coverageMinZ,
        Fixed64 coverageMaxZ)
    {
        Vector3d position = voxel.WorldPosition;
        return position.X >= coverageMinX
            && position.X <= coverageMaxX
            && position.Z >= coverageMinZ
            && position.Z <= coverageMaxZ;
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
