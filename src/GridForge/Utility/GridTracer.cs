//=======================================================================
// GridTracer.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Geometry;
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
public static partial class GridTracer
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
    /// Clears and fills caller-owned storage with voxels traced by a 3D line.
    /// </summary>
    /// <param name="world">The world whose grids should be traced.</param>
    /// <param name="start">Starting position in world space.</param>
    /// <param name="end">Ending position in world space.</param>
    /// <param name="results">Caller-owned storage that receives traced voxels.</param>
    /// <param name="padding">Value applied to the start/end positions before snapping.</param>
    /// <param name="includeEnd">Whether to include the end voxel in the traced line.</param>
    public static void TraceLineInto(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        SwiftList<Voxel> results,
        Fixed64? padding = null,
        bool includeEnd = true)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();
        SwiftList<ushort> candidateGrids = SwiftListPool<ushort>.Shared.Rent();

        try
        {
            AddTraceLineVoxelsTo(
                world,
                start,
                end,
                padding,
                includeEnd,
                results,
                voxelRedundancyCheck,
                candidateGrids);

        }
        finally
        {
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
            SwiftListPool<ushort>.Shared.Release(candidateGrids);
        }
    }

    /// <summary>
    /// Clears and fills caller-owned storage with voxels traced by a 3D line using caller-owned scratch collections.
    /// </summary>
    /// <param name="world">The world whose grids should be traced.</param>
    /// <param name="start">Starting position in world space.</param>
    /// <param name="end">Ending position in world space.</param>
    /// <param name="results">Caller-owned storage that receives traced voxels.</param>
    /// <param name="scratch">Reusable scratch storage for grid candidates and duplicate-voxel guards.</param>
    /// <param name="padding">Value applied to the start/end positions before snapping.</param>
    /// <param name="includeEnd">Whether to include the end voxel in the traced line.</param>
    public static void TraceLineInto(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        SwiftList<Voxel> results,
        GridTraceScratch scratch,
        Fixed64? padding = null,
        bool includeEnd = true)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));
        SwiftThrowHelper.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        scratch.Clear();
        AddTraceLineVoxelsTo(
            world,
            start,
            end,
            padding,
            includeEnd,
            results,
            scratch.VoxelRedundancy,
            scratch.CandidateGrids);

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
    /// Clears and fills caller-owned storage with voxels traced by a 2D XZ-plane line.
    /// </summary>
    /// <param name="world">The world whose grids should be traced.</param>
    /// <param name="start">Starting XZ-plane position in world space.</param>
    /// <param name="end">Ending XZ-plane position in world space.</param>
    /// <param name="results">Caller-owned storage that receives traced voxels.</param>
    /// <param name="padding">Value applied to the start/end positions before snapping.</param>
    /// <param name="includeEnd">Whether to include the end voxel in the traced line.</param>
    /// <param name="layerY">The world Y layer to trace. Defaults to zero.</param>
    public static void TraceLineInto(
        GridWorld world,
        Vector2d start,
        Vector2d end,
        SwiftList<Voxel> results,
        Fixed64? padding = null,
        bool includeEnd = true,
        Fixed64 layerY = default)
    {
        Vector3d start3D = GridPlane2d.ToWorld(start, layerY);
        Vector3d end3D = GridPlane2d.ToWorld(end, layerY);

        TraceLineInto(world, start3D, end3D, results, padding, includeEnd);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with voxels traced by a 2D XZ-plane line using caller-owned scratch collections.
    /// </summary>
    /// <param name="world">The world whose grids should be traced.</param>
    /// <param name="start">Starting XZ-plane position in world space.</param>
    /// <param name="end">Ending XZ-plane position in world space.</param>
    /// <param name="results">Caller-owned storage that receives traced voxels.</param>
    /// <param name="scratch">Reusable scratch storage for grid candidates and duplicate-voxel guards.</param>
    /// <param name="padding">Value applied to the start/end positions before snapping.</param>
    /// <param name="includeEnd">Whether to include the end voxel in the traced line.</param>
    /// <param name="layerY">The world Y layer to trace. Defaults to zero.</param>
    public static void TraceLineInto(
        GridWorld world,
        Vector2d start,
        Vector2d end,
        SwiftList<Voxel> results,
        GridTraceScratch scratch,
        Fixed64? padding = null,
        bool includeEnd = true,
        Fixed64 layerY = default)
    {
        Vector3d start3D = GridPlane2d.ToWorld(start, layerY);
        Vector3d end3D = GridPlane2d.ToWorld(end, layerY);

        TraceLineInto(world, start3D, end3D, results, scratch, padding, includeEnd);
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
    /// Retrieves all grid voxels covered by the given XZ-plane area on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="area">The 2D area whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
    /// <returns>A collection of <see cref="GridVoxelSet"/> objects representing the covered voxels.</returns>
    public static IEnumerable<GridVoxelSet> GetCoveredVoxels(
        GridWorld world,
        FixedBoundArea area,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        return GetCoveredVoxels(world, area.Min, area.Max, layerY, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with voxels covered by the supplied bounding area.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The minimum corner of the bounding area.</param>
    /// <param name="boundsMax">The maximum corner of the bounding area.</param>
    /// <param name="results">Caller-owned storage that receives covered voxels.</param>
    /// <param name="padding">Value applied to the min/max bounds before normalization.</param>
    public static void GetCoveredVoxelsInto(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<Voxel> results,
        Fixed64? padding = null)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddCoveredVoxelsTo(world, boundsMin, boundsMax, results, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with voxels covered by the supplied XZ-plane bounding area.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The 2D minimum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="boundsMax">The 2D maximum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="results">Caller-owned storage that receives covered voxels.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before normalization.</param>
    public static void GetCoveredVoxelsInto(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        SwiftList<Voxel> results,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        GetCoveredVoxelsInto(world, min, max, results, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage with voxels covered by the supplied XZ-plane area.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="area">The 2D area whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="results">Caller-owned storage that receives covered voxels.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before normalization.</param>
    public static void GetCoveredVoxelsInto(
        GridWorld world,
        FixedBoundArea area,
        SwiftList<Voxel> results,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        GetCoveredVoxelsInto(world, area.Min, area.Max, results, layerY, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The minimum corner of the bounding area.</param>
    /// <param name="boundsMax">The maximum corner of the bounding area.</param>
    /// <param name="results">Caller-owned storage that receives covered voxels.</param>
    /// <param name="scratch">Reusable scratch storage for grid candidates and duplicate-voxel guards.</param>
    /// <param name="padding">Value applied to the min/max bounds before normalization.</param>
    public static void GetCoveredVoxelsInto(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<Voxel> results,
        GridTraceScratch scratch,
        Fixed64? padding = null)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));
        SwiftThrowHelper.ThrowIfNull(scratch, nameof(scratch));

        results.Clear();
        if (world == null || !world.IsActive)
            return;

        AddCoveredVoxelsTo(world, boundsMin, boundsMax, results, scratch, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections for an XZ-plane bounding area.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="boundsMin">The 2D minimum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="boundsMax">The 2D maximum corner whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="results">Caller-owned storage that receives covered voxels.</param>
    /// <param name="scratch">Reusable scratch storage for grid candidates and duplicate-voxel guards.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before normalization.</param>
    public static void GetCoveredVoxelsInto(
        GridWorld world,
        Vector2d boundsMin,
        Vector2d boundsMax,
        SwiftList<Voxel> results,
        GridTraceScratch scratch,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        (Vector3d min, Vector3d max) = GridPlane2d.ToWorldBounds(boundsMin, boundsMax, layerY);
        GetCoveredVoxelsInto(world, min, max, results, scratch, padding);
    }

    /// <summary>
    /// Clears and fills caller-owned storage using caller-owned scratch collections for an XZ-plane area.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="area">The 2D area whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="results">Caller-owned storage that receives covered voxels.</param>
    /// <param name="scratch">Reusable scratch storage for grid candidates and duplicate-voxel guards.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before normalization.</param>
    public static void GetCoveredVoxelsInto(
        GridWorld world,
        FixedBoundArea area,
        SwiftList<Voxel> results,
        GridTraceScratch scratch,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        GetCoveredVoxelsInto(world, area.Min, area.Max, results, scratch, layerY, padding);
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
    /// Retrieves all scan cells within the given XZ-plane area on the supplied world Y layer.
    /// </summary>
    /// <param name="world">The world whose grids should be queried.</param>
    /// <param name="area">The 2D area whose X component maps to world X and Y component maps to world Z.</param>
    /// <param name="layerY">The world Y layer to cover. Defaults to zero.</param>
    /// <param name="padding">Value applied to the min/max bounds before snapping.</param>
    /// <returns>An enumerable of covered scan cells grouped by grid.</returns>
    public static IEnumerable<ScanCell> GetCoveredScanCells(
        GridWorld world,
        FixedBoundArea area,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        return GetCoveredScanCells(world, area.Min, area.Max, layerY, padding);
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
    /// Clears and fills caller-owned storage with scan cells covered by the supplied XZ-plane area.
    /// </summary>
    public static void GetCoveredScanCellsInto(
        GridWorld world,
        FixedBoundArea area,
        SwiftList<ScanCell> results,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        GetCoveredScanCellsInto(world, area.Min, area.Max, results, layerY, padding);
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
    /// Clears and fills caller-owned storage using caller-owned scratch collections for an XZ-plane area.
    /// </summary>
    public static void GetCoveredScanCellsInto(
        GridWorld world,
        FixedBoundArea area,
        SwiftList<ScanCell> results,
        GridScanScratch scratch,
        Fixed64 layerY = default,
        Fixed64? padding = null)
    {
        GetCoveredScanCellsInto(world, area.Min, area.Max, results, scratch, layerY, padding);
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
        SwiftHashSet<ScanCell> voxelRedundancyCheck = SwiftHashSetPool<ScanCell>.Shared.Rent();
        SwiftList<ushort> candidateGrids = SwiftListPool<ushort>.Shared.Rent();

        try
        {
            AddCoveredScanCellsCore(
                world,
                boundsMin,
                boundsMax,
                scanCells,
                candidateGrids,
                voxelRedundancyCheck,
                padding);
        }
        finally
        {
            SwiftHashSetPool<ScanCell>.Shared.Release(voxelRedundancyCheck);
            SwiftListPool<ushort>.Shared.Release(candidateGrids);
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
            scratch.CandidateGrids,
            scratch.ScanCellRedundancy,
            padding);
    }

    /// <summary>
    /// Appends covered voxels without allocating an iterator for hot-path callers.
    /// </summary>
    internal static void AddCoveredVoxelsTo(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<Voxel> voxels,
        Fixed64? padding = null)
    {
        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();
        SwiftList<ushort> candidateGrids = SwiftListPool<ushort>.Shared.Rent();

        try
        {
            AddCoveredVoxelsCore(
                world,
                boundsMin,
                boundsMax,
                voxels,
                candidateGrids,
                voxelRedundancyCheck,
                padding);
        }
        finally
        {
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
            SwiftListPool<ushort>.Shared.Release(candidateGrids);
        }
    }

    /// <summary>
    /// Appends covered voxels using caller-owned scratch state for allocation-sensitive coverage scans.
    /// </summary>
    internal static void AddCoveredVoxelsTo(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<Voxel> voxels,
        GridTraceScratch scratch,
        Fixed64? padding = null)
    {
        scratch.Clear();
        AddCoveredVoxelsCore(
            world,
            boundsMin,
            boundsMax,
            voxels,
            scratch.CandidateGrids,
            scratch.VoxelRedundancy,
            padding);
    }

    private static void AddCoveredVoxelsCore(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<Voxel> voxels,
        SwiftList<ushort> candidateGrids,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        Fixed64? padding = null)
    {
        (Vector3d queryMin, Vector3d queryMax) =
            CreatePaddedOrderedBounds(boundsMin, boundsMax, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);

        world.CollectGridCandidates(candidateMin, candidateMax, candidateGrids);
        foreach (ushort gridIndex in candidateGrids)
        {
            AddCoveredGridVoxels(
                world.ActiveGrids[gridIndex],
                queryMin,
                queryMax,
                voxels,
                voxelRedundancyCheck);
        }
    }

    private static void AddCoveredScanCellsCore(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftList<ScanCell> scanCells,
        SwiftList<ushort> candidateGrids,
        SwiftHashSet<ScanCell> voxelRedundancyCheck,
        Fixed64? padding = null)
    {
        (Vector3d queryMin, Vector3d queryMax) =
            CreatePaddedOrderedBounds(boundsMin, boundsMax, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);
        world.CollectGridCandidates(candidateMin, candidateMax, candidateGrids);
        foreach (ushort gridIndex in candidateGrids)
        {
            AddCoveredScanCellsForGrid(
                world.ActiveGrids[gridIndex],
                queryMin,
                queryMax,
                scanCells,
                voxelRedundancyCheck);
        }
    }

    private static IEnumerable<GridVoxelSet> GetCoveredVoxelsIterator(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding)
    {
        SwiftList<GridVoxelSet> gridVoxelSets = SwiftListPool<GridVoxelSet>.Shared.Rent();
        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();
        SwiftList<ushort> candidateGrids = SwiftListPool<ushort>.Shared.Rent();

        try
        {
            AddCoveredVoxelsToMapping(
                world,
                boundsMin,
                boundsMax,
                padding,
                gridVoxelSets,
                voxelRedundancyCheck,
                candidateGrids);

            foreach (GridVoxelSet gridVoxelSet in gridVoxelSets)
                yield return gridVoxelSet;
        }
        finally
        {
            ReleaseGridVoxelSets(gridVoxelSets);
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
            SwiftListPool<ushort>.Shared.Release(candidateGrids);
        }
    }

    private static void AddCoveredVoxelsToMapping(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding,
        SwiftList<GridVoxelSet> gridVoxelSets,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftList<ushort> candidateGrids)
    {
        (Vector3d queryMin, Vector3d queryMax) =
            CreatePaddedOrderedBounds(boundsMin, boundsMax, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);

        world.CollectGridCandidates(candidateMin, candidateMax, candidateGrids);
        foreach (ushort gridIndex in candidateGrids)
        {
            AddCoveredVoxelsForGrid(
                world.ActiveGrids[gridIndex],
                queryMin,
                queryMax,
                gridVoxelSets,
                voxelRedundancyCheck);
        }
    }

    private static IEnumerable<ScanCell> GetCoveredScanCellsIterator(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64? padding)
    {
        SwiftList<ScanCell> scanCells = SwiftListPool<ScanCell>.Shared.Rent();
        SwiftHashSet<ScanCell> voxelRedundancyCheck = SwiftHashSetPool<ScanCell>.Shared.Rent();
        SwiftList<ushort> candidateGrids = SwiftListPool<ushort>.Shared.Rent();

        try
        {
            AddCoveredScanCellsCore(
                world,
                boundsMin,
                boundsMax,
                scanCells,
                candidateGrids,
                voxelRedundancyCheck,
                padding);

            foreach (ScanCell scanCell in scanCells)
                yield return scanCell;
        }
        finally
        {
            SwiftListPool<ScanCell>.Shared.Release(scanCells);
            SwiftHashSetPool<ScanCell>.Shared.Release(voxelRedundancyCheck);
            SwiftListPool<ushort>.Shared.Release(candidateGrids);
        }
    }
}
