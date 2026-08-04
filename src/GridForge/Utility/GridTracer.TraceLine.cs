//=======================================================================
// GridTracer.TraceLine.cs
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

/// <content>
/// Provides line-tracing functionality for identifying voxels intersected
/// along a segment between two points across one or more grids.
/// </content>
public static partial class GridTracer
{
    private static IEnumerable<GridVoxelSet> TraceLineIterator(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd)
    {
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping = new();
        SwiftHashSet<Voxel> voxelRedundancyCheck = SwiftHashSetPool<Voxel>.Shared.Rent();
        SwiftList<ushort> candidateGrids = SwiftListPool<ushort>.Shared.Rent();

        try
        {
            AddTraceLineVoxelsToMapping(
                world,
                start,
                end,
                padding,
                includeEnd,
                gridVoxelMapping,
                voxelRedundancyCheck,
                candidateGrids);

            foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
                yield return new GridVoxelSet(kvp.Key, kvp.Value);
        }
        finally
        {
            ReleaseGridVoxelMapping(gridVoxelMapping);
            SwiftHashSetPool<Voxel>.Shared.Release(voxelRedundancyCheck);
            SwiftListPool<ushort>.Shared.Release(candidateGrids);
        }
    }

    private static void AddTraceLineVoxelsToMapping(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftList<ushort> candidateGrids)
    {
        (Vector3d queryMin, Vector3d queryMax) = CreatePaddedOrderedBounds(start, end, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);

        world.CollectGridCandidates(candidateMin, candidateMax, candidateGrids);
        foreach (ushort gridIndex in candidateGrids)
        {
            AddTraceLineVoxelsForGrid(
                world.ActiveGrids[gridIndex],
                start,
                end,
                padding,
                includeEnd,
                gridVoxelMapping,
                voxelRedundancyCheck);
        }
    }

    private static void AddTraceLineVoxelsTo(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd,
        SwiftList<Voxel> voxels,
        SwiftHashSet<Voxel> voxelRedundancyCheck,
        SwiftList<ushort> candidateGrids)
    {
        (Vector3d queryMin, Vector3d queryMax) = CreatePaddedOrderedBounds(start, end, padding);
        (Vector3d candidateMin, Vector3d candidateMax) =
            ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);

        world.CollectGridCandidates(candidateMin, candidateMax, candidateGrids);
        foreach (ushort gridIndex in candidateGrids)
        {
            AddTraceLineVoxelsForGrid(
                world.ActiveGrids[gridIndex],
                start,
                end,
                padding,
                includeEnd,
                voxels,
                voxelRedundancyCheck);
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
        return true;
    }

    private static void AddTraceLineVoxelsForGrid(
        VoxelGrid currentGrid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd,
        SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (!TryClipTraceSegmentToGrid(
            currentGrid,
            start,
            end,
            padding,
            out Vector3d traceStart,
            out Vector3d traceEnd,
            out bool segmentEndsBeforeGlobalEnd))
        {
            return;
        }

        SwiftList<Voxel> voxelList = SwiftListPool<Voxel>.Shared.Rent();
        AddTraceLineGridVoxels(
            currentGrid,
            traceStart,
            traceEnd,
            padding,
            includeEnd || segmentEndsBeforeGlobalEnd,
            voxelList,
            voxelRedundancyCheck);

        if (voxelList.Count > 0)
            gridVoxelMapping.Add(currentGrid, voxelList);
        else
            SwiftListPool<Voxel>.Shared.Release(voxelList);
    }

    private static void AddTraceLineVoxelsForGrid(
        VoxelGrid currentGrid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd,
        SwiftList<Voxel> voxels,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (!TryClipTraceSegmentToGrid(
            currentGrid,
            start,
            end,
            padding,
            out Vector3d traceStart,
            out Vector3d traceEnd,
            out bool segmentEndsBeforeGlobalEnd))
        {
            return;
        }

        AddTraceLineGridVoxels(
            currentGrid,
            traceStart,
            traceEnd,
            padding,
            includeEnd || segmentEndsBeforeGlobalEnd,
            voxels,
            voxelRedundancyCheck);
    }

    private static void AddTraceLineGridVoxels(
        VoxelGrid currentGrid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd,
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
                includeEnd,
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

        if (includeEnd)
            AddTraceVoxelByPosition(currentGrid, end, voxelList, voxelRedundancyCheck);
    }

    private static void AddHexTraceLineGridVoxels(
        VoxelGrid currentGrid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        bool includeEnd,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        CreateHexTraceEndpoints(
            currentGrid,
            start,
            end,
            padding,
            out VoxelIndex startIndex,
            out VoxelIndex endIndex);

        int steps = CalculateHexTraceSteps(startIndex, endIndex);
        if (steps == 0)
        {
            AddTraceVoxelByIndex(currentGrid, startIndex, voxelList, voxelRedundancyCheck);
            return;
        }

        bool includeEndIndex = ShouldIncludeHexTraceEndIndex(currentGrid, end, endIndex, includeEnd);
        int finalStep = includeEndIndex ? steps : steps - 1;
        Fixed64 stepCount = new Fixed64(steps);
        for (int i = 0; i <= finalStep; i++)
        {
            Fixed64 t = new Fixed64(i) / stepCount;
            VoxelIndex traceIndex = InterpolateHexTraceIndex(startIndex, endIndex, t);
            AddTraceVoxelByIndex(currentGrid, traceIndex, voxelList, voxelRedundancyCheck);
        }
    }

    private static void CreateHexTraceEndpoints(
        VoxelGrid grid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        out VoxelIndex startIndex,
        out VoxelIndex endIndex)
    {
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

        grid.TryGetVoxelIndex(traceStart, out startIndex);
        grid.TryGetVoxelIndex(traceEnd, out endIndex);
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

    private static bool TryClipTraceSegmentToGrid(
        VoxelGrid grid,
        Vector3d start,
        Vector3d end,
        Fixed64? padding,
        out Vector3d clippedStart,
        out Vector3d clippedEnd,
        out bool segmentEndsBeforeGlobalEnd)
    {
        clippedStart = default;
        clippedEnd = default;
        segmentEndsBeforeGlobalEnd = false;

        Fixed64 fixedPadding = padding.HasValue && padding.Value > Fixed64.Zero
            ? padding.Value
            : Fixed64.Zero;
        Vector3d boundsMin = grid.BoundsMin - fixedPadding;
        Vector3d boundsMax = grid.BoundsMax + fixedPadding;
        Fixed64 tMin = Fixed64.Zero;
        Fixed64 tMax = Fixed64.One;

        if (!(ClipTraceSegmentAxis(start.X, end.X, boundsMin.X, boundsMax.X, ref tMin, ref tMax)
            && ClipTraceSegmentAxis(start.Y, end.Y, boundsMin.Y, boundsMax.Y, ref tMin, ref tMax)
            && ClipTraceSegmentAxis(start.Z, end.Z, boundsMin.Z, boundsMax.Z, ref tMin, ref tMax)))
        {
            return false;
        }

        clippedStart = InterpolateTraceSegment(start, end, boundsMin, boundsMax, tMin);
        clippedEnd = InterpolateTraceSegment(start, end, boundsMin, boundsMax, tMax);
        segmentEndsBeforeGlobalEnd = tMax < Fixed64.One;
        return true;
    }

    private static bool ClipTraceSegmentAxis(
        Fixed64 start,
        Fixed64 end,
        Fixed64 boundsMin,
        Fixed64 boundsMax,
        ref Fixed64 tMin,
        ref Fixed64 tMax)
    {
        Fixed64 delta = end - start;
        if (delta == Fixed64.Zero)
            return start >= boundsMin && start <= boundsMax;

        Fixed64 axisMin = (boundsMin - start) / delta;
        Fixed64 axisMax = (boundsMax - start) / delta;
        if (axisMin > axisMax)
            (axisMin, axisMax) = (axisMax, axisMin);

        if (axisMin > tMin)
            tMin = axisMin;
        if (axisMax < tMax)
            tMax = axisMax;

        return tMin <= tMax;
    }

    private static bool ShouldIncludeHexTraceEndIndex(
        VoxelGrid grid,
        Vector3d end,
        VoxelIndex endIndex,
        bool includeEnd)
    {
        if (includeEnd)
            return true;

        return !grid.TryGetVoxelIndex(end, out VoxelIndex actualEndIndex)
            || actualEndIndex != endIndex;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3d InterpolateTraceSegment(
        Vector3d start,
        Vector3d end,
        Vector3d boundsMin,
        Vector3d boundsMax,
        Fixed64 t) =>
        new(
            InterpolateTraceAxis(start.X, end.X, boundsMin.X, boundsMax.X, t),
            InterpolateTraceAxis(start.Y, end.Y, boundsMin.Y, boundsMax.Y, t),
            InterpolateTraceAxis(start.Z, end.Z, boundsMin.Z, boundsMax.Z, t));

    private static Fixed64 InterpolateTraceAxis(
        Fixed64 start,
        Fixed64 end,
        Fixed64 boundsMin,
        Fixed64 boundsMax,
        Fixed64 t)
    {
        Fixed64 delta = end - start;
        if (delta == Fixed64.Zero)
            return start;

        if ((boundsMin - start) / delta == t)
            return boundsMin;
        if ((boundsMax - start) / delta == t)
            return boundsMax;

        return start + delta * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddTraceVoxelByPosition(
        VoxelGrid grid,
        Vector3d position,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (grid.TryGetVoxel(grid.FloorToGrid(position), out Voxel? voxel)
            && voxelRedundancyCheck.Add(voxel!))
        {
            voxelList.Add(voxel!);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddTraceVoxelByIndex(
        VoxelGrid grid,
        VoxelIndex index,
        SwiftList<Voxel> voxelList,
        SwiftHashSet<Voxel> voxelRedundancyCheck)
    {
        if (grid.TryGetVoxel(index, out Voxel? voxel)
            && voxelRedundancyCheck.Add(voxel!))
        {
            voxelList.Add(voxel!);
        }
    }

    private static void ReleaseGridVoxelMapping(SwiftDictionary<VoxelGrid, SwiftList<Voxel>> gridVoxelMapping)
    {
        foreach (KeyValuePair<VoxelGrid, SwiftList<Voxel>> kvp in gridVoxelMapping)
            SwiftListPool<Voxel>.Shared.Release(kvp.Value);
    }
}
