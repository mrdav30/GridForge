//=======================================================================
// GridTracer.TraceIntervals.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;

/// <content>
/// Provides exact ordered segment intervals over physical and missing grid addresses.
/// </content>
public static partial class GridTracer
{
    /// <summary>
    /// Traces an arbitrary world-space segment into exact, canonically ordered grid-cell intervals.
    /// </summary>
    /// <remarks>
    /// Results are cleared on entry and on any ceiling or representability failure. Candidate grids are
    /// discovered through the world spatial index. Candidate addresses are bounded around the segment,
    /// then exact rectangular or hexagonal prisms reject all broad-phase false positives.
    /// </remarks>
    public static GridTraceIntervalReport TraceIntervalsInto(
        GridWorld world,
        Vector3d start,
        Vector3d end,
        SwiftList<GridTraceInterval> results,
        GridTraceIntervalScratch scratch,
        int gridCandidateLimit,
        int addressCandidateLimit,
        int outputLimit,
        long candidateWorkLimit)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));
        SwiftThrowHelper.ThrowIfNull(scratch, nameof(scratch));
        SwiftThrowHelper.ThrowIfNegative(gridCandidateLimit, nameof(gridCandidateLimit));
        SwiftThrowHelper.ThrowIfNegative(addressCandidateLimit, nameof(addressCandidateLimit));
        SwiftThrowHelper.ThrowIfNegative(outputLimit, nameof(outputLimit));
        if (candidateWorkLimit < 0L)
            throw new ArgumentOutOfRangeException(nameof(candidateWorkLimit));

        results.Clear();
        scratch.Clear();
        if (world == null || !world.IsActive)
            return CreateTraceReport(GridTraceIntervalStatus.Complete, 0, 0, results);

        world.EnterReadLock();
        try
        {
            (Vector3d queryMin, Vector3d queryMax) = CreatePaddedOrderedBounds(start, end, padding: null);
            (Vector3d candidateMin, Vector3d candidateMax) =
                ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);
            bool candidateGridLimitIsTighter = candidateWorkLimit < gridCandidateLimit;
            int effectiveGridLimit = candidateGridLimitIsTighter
                ? (int)candidateWorkLimit
                : gridCandidateLimit;
            if (!world.CollectGridCandidates(
                    candidateMin,
                    candidateMax,
                    scratch.CandidateGrids,
                    effectiveGridLimit))
            {
                return FailTrace(
                    results,
                    candidateGridLimitIsTighter
                        ? GridTraceIntervalStatus.CandidateWorkLimitExceeded
                        : GridTraceIntervalStatus.GridCandidateLimitExceeded,
                    scratch.CandidateGrids.Count,
                    0);
            }

            SortGridIndices(world, scratch.CandidateGrids);

            long remainingCandidateWork = candidateWorkLimit - scratch.CandidateGrids.Count;
            bool candidateAddressLimitIsTighter = remainingCandidateWork < addressCandidateLimit;
            int effectiveAddressLimit = candidateAddressLimitIsTighter
                ? (int)remainingCandidateWork
                : addressCandidateLimit;
            bool hasSparseGrid = false;
            for (int gridCandidateIndex = 0; gridCandidateIndex < scratch.CandidateGrids.Count; gridCandidateIndex++)
            {
                VoxelGrid grid = world.ActiveGrids[scratch.CandidateGrids[gridCandidateIndex]];
                if (!grid.IsActive
                    || !TryCollectSegmentCandidates(
                        grid,
                        queryMin,
                        queryMax,
                        scratch,
                        effectiveAddressLimit))
                {
                    return FailTrace(
                        results,
                        candidateAddressLimitIsTighter
                            ? GridTraceIntervalStatus.CandidateWorkLimitExceeded
                            : GridTraceIntervalStatus.AddressCandidateLimitExceeded,
                        scratch.CandidateGrids.Count,
                        scratch.AddressCandidates.Count);
                }

                hasSparseGrid |= grid.StorageKind == GridStorageKind.Sparse;
            }

            if (hasSparseGrid)
                SnapshotSparsePresence(world, scratch.AddressCandidates);

            for (int addressIndex = 0; addressIndex < scratch.AddressCandidates.Count; addressIndex++)
            {
                GridTraceAddressCandidate candidate = scratch.AddressCandidates[addressIndex];
                VoxelGrid grid = candidate.Grid;
                VoxelIndex index = candidate.Index;
                WorldVoxelIndex cell = new WorldVoxelIndex(
                    world.SpawnToken,
                    grid.GridIndex,
                    grid.SpawnToken,
                    index);
                if (!GridCellGeometry.TryCreatePrism(
                        grid.Configuration.TopologyKind,
                        grid.Configuration.TopologyMetrics,
                        grid.GetWorldPosition(index),
                        cell,
                        out GridCellPrism prism))
                {
                    return FailTrace(
                        results,
                        GridTraceIntervalStatus.UnrepresentableGeometry,
                        scratch.CandidateGrids.Count,
                        scratch.AddressCandidates.Count);
                }

                if (!TryGetPrismInterval(start, end, prism, out Fixed64 tEnter, out Fixed64 tExit))
                    continue;

                if (results.Count >= outputLimit)
                {
                    return FailTrace(
                        results,
                        GridTraceIntervalStatus.OutputLimitExceeded,
                        scratch.CandidateGrids.Count,
                        scratch.AddressCandidates.Count);
                }

                results.Add(new GridTraceInterval(
                    cell,
                    grid.Configuration.ToGridKey(),
                    candidate.IsPhysicallyPresent,
                    grid.LastChangeSequence,
                    tEnter,
                    tExit));
            }

            SortIntervals(results);
            return CreateTraceReport(
                GridTraceIntervalStatus.Complete,
                scratch.CandidateGrids.Count,
                scratch.AddressCandidates.Count,
                results);
        }
        finally
        {
            world.ExitReadLock();
            scratch.Clear();
        }
    }

    private static bool TryCollectSegmentCandidates(
        VoxelGrid grid,
        Vector3d queryMin,
        Vector3d queryMax,
        GridTraceIntervalScratch scratch,
        int addressCandidateLimit)
    {
        if (!TopologyVoxelRangeUtility.TryGetPrismCandidateRange(
                grid,
                queryMin,
                queryMax,
                out VoxelIndex minIndex,
                out VoxelIndex maxIndex))
        {
            return true;
        }

        bool isDense = grid.StorageKind == GridStorageKind.Dense;
        for (int x = minIndex.x; x <= maxIndex.x; x++)
        {
            for (int y = minIndex.y; y <= maxIndex.y; y++)
            {
                for (int z = minIndex.z; z <= maxIndex.z; z++)
                {
                    if (scratch.AddressCandidates.Count >= addressCandidateLimit)
                        return false;

                    scratch.AddressCandidates.Add(new GridTraceAddressCandidate(
                        grid,
                        new VoxelIndex(x, y, z),
                        isDense));
                }
            }
        }

        return true;
    }

    private static void SnapshotSparsePresence(
        GridWorld world,
        SwiftList<GridTraceAddressCandidate> candidates)
    {
        lock (world.ChangeSyncRoot)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                GridTraceAddressCandidate candidate = candidates[i];
                if (candidate.Grid.StorageKind == GridStorageKind.Sparse)
                {
                    candidates[i] = candidate.WithPhysicalPresence(
                        candidate.Grid.TryGetVoxel(candidate.Index, out _));
                }
            }
        }
    }

    internal static bool TryGetPrismInterval(
        Vector3d start,
        Vector3d end,
        in GridCellPrism prism,
        out Fixed64 tEnter,
        out Fixed64 tExit)
    {
        if (!GridCellGeometry.TryGetPlanarSegmentInterval(
                prism,
                new Vector2d(start.X, start.Z),
                new Vector2d(end.X, end.Z),
                out Fixed64 planarEnter,
                out Fixed64 planarExit)
            || !TryGetVerticalInterval(start.Y, end.Y, prism.VerticalMin, prism.VerticalMax,
                out Fixed64 verticalEnter, out Fixed64 verticalExit))
        {
            tEnter = default;
            tExit = default;
            return false;
        }

        tEnter = FixedMath.Max(planarEnter, verticalEnter);
        tExit = FixedMath.Min(planarExit, verticalExit);
        return tEnter <= tExit;
    }

    private static bool TryGetVerticalInterval(
        Fixed64 start,
        Fixed64 end,
        Fixed64 verticalMin,
        Fixed64 verticalMax,
        out Fixed64 tEnter,
        out Fixed64 tExit)
    {
        Fixed64 delta = end - start;
        if (delta == Fixed64.Zero)
        {
            tEnter = Fixed64.Zero;
            tExit = Fixed64.One;
            return start >= verticalMin && start <= verticalMax;
        }

        tEnter = (verticalMin - start) / delta;
        tExit = (verticalMax - start) / delta;
        if (tEnter > tExit)
            (tEnter, tExit) = (tExit, tEnter);

        tEnter = FixedMath.Clamp(tEnter, Fixed64.Zero, Fixed64.One);
        tExit = FixedMath.Clamp(tExit, Fixed64.Zero, Fixed64.One);
        return FixedMath.Min(start, end) <= verticalMax
            && FixedMath.Max(start, end) >= verticalMin;
    }

    private static GridTraceIntervalReport CreateTraceReport(
        GridTraceIntervalStatus status,
        int gridCandidateCount,
        int candidateCount,
        SwiftList<GridTraceInterval> results)
    {
        int tieGroupCount = AssignTieGroups(results);
        return new GridTraceIntervalReport(
            status,
            gridCandidateCount,
            candidateCount,
            results.Count,
            tieGroupCount,
            HasContinuousCoverage(results, requirePhysical: false),
            HasContinuousCoverage(results, requirePhysical: true));
    }

    private static GridTraceIntervalReport FailTrace(
        SwiftList<GridTraceInterval> results,
        GridTraceIntervalStatus status,
        int gridCandidateCount,
        int candidateCount)
    {
        results.Clear();
        return new GridTraceIntervalReport(status, gridCandidateCount, candidateCount, 0, 0, false, false);
    }

    private static int AssignTieGroups(SwiftList<GridTraceInterval> results)
    {
        int groupId = -1;
        int order = 0;
        Fixed64 groupEnter = default;
        Fixed64 groupExit = default;
        for (int i = 0; i < results.Count; i++)
        {
            GridTraceInterval interval = results[i];
            bool pointPeer = i > 0
                && groupEnter == groupExit
                && interval.TEnter == groupEnter
                && interval.TExit == groupExit;
            bool overlapsInterior = i > 0
                && groupExit > groupEnter
                && interval.TExit > interval.TEnter
                && interval.TEnter < groupExit;
            if (i == 0 || (!overlapsInterior && !pointPeer))
            {
                groupId++;
                order = 0;
                groupEnter = interval.TEnter;
                groupExit = interval.TExit;
            }
            else if (interval.TExit > groupExit)
                groupExit = interval.TExit;

            results[i] = interval.WithTie(groupId, order++);
        }

        return groupId + 1;
    }

    private static bool HasContinuousCoverage(
        SwiftList<GridTraceInterval> results,
        bool requirePhysical)
    {
        Fixed64 coveredThrough = Fixed64.Zero;
        bool started = false;
        for (int i = 0; i < results.Count; i++)
        {
            GridTraceInterval interval = results[i];
            if (requirePhysical && !interval.IsPhysicallyPresent)
                continue;
            if (!started)
            {
                if (interval.TEnter > Fixed64.Zero)
                    return false;
                started = true;
            }
            else if (interval.TEnter > coveredThrough)
            {
                return false;
            }

            if (interval.TExit > coveredThrough)
                coveredThrough = interval.TExit;
            if (coveredThrough >= Fixed64.One)
                return true;
        }

        return started && coveredThrough >= Fixed64.One;
    }

    private static void SortGridIndices(GridWorld world, SwiftList<ushort> values) =>
        values.SortInPlace(new GridIndexComparer(world));

    private static void SortIntervals(SwiftList<GridTraceInterval> values)
    {
        GridTraceInterval[] items = values.InnerArray;
        int count = values.Count;
        for (int root = (count >> 1) - 1; root >= 0; root--)
            SiftIntervalsDown(items, root, count);

        for (int end = count - 1; end > 0; end--)
        {
            (items[0], items[end]) = (items[end], items[0]);
            SiftIntervalsDown(items, 0, end);
        }
    }

    private static void SiftIntervalsDown(GridTraceInterval[] items, int root, int count)
    {
        while (true)
        {
            int child = (root << 1) + 1;
            if (child >= count)
                return;
            int right = child + 1;
            if (right < count && CompareIntervals(items[child], items[right]) < 0)
                child = right;
            if (CompareIntervals(items[root], items[child]) >= 0)
                return;

            (items[root], items[child]) = (items[child], items[root]);
            root = child;
        }
    }

    private static int CompareIntervals(GridTraceInterval first, GridTraceInterval second)
    {
        int comparison = first.TEnter.CompareTo(second.TEnter);
        if (comparison != 0)
            return comparison;
        comparison = second.TExit.CompareTo(first.TExit);
        if (comparison != 0)
            return comparison;
        comparison = CompareConfigurationKeys(first.ConfigurationKey, second.ConfigurationKey);
        if (comparison != 0)
            return comparison;
        return first.Cell.VoxelIndex.CompareTo(second.Cell.VoxelIndex);
    }

    private static int CompareGridIdentity(VoxelGrid first, VoxelGrid second)
    {
        return CompareConfigurationKeys(
            first.Configuration.ToGridKey(),
            second.Configuration.ToGridKey());
    }

    private readonly struct GridIndexComparer : IComparer<ushort>
    {
        private readonly GridWorld _world;

        internal GridIndexComparer(GridWorld world) => _world = world;

        public int Compare(ushort first, ushort second) =>
            CompareGridIdentity(_world.ActiveGrids[first], _world.ActiveGrids[second]);
    }

    private static int CompareConfigurationKeys(
        GridConfigurationKey first,
        GridConfigurationKey second)
    {
        int comparison = CompareVectors(first.BoundsMin, second.BoundsMin);
        if (comparison != 0)
            return comparison;
        comparison = CompareVectors(first.BoundsMax, second.BoundsMax);
        if (comparison != 0)
            return comparison;
        comparison = ((int)first.TopologyKind).CompareTo((int)second.TopologyKind);
        if (comparison != 0)
            return comparison;

        GridTopologyMetrics firstMetrics = first.TopologyMetrics;
        GridTopologyMetrics secondMetrics = second.TopologyMetrics;
        comparison = firstMetrics.CellRadius.CompareTo(secondMetrics.CellRadius);
        if (comparison != 0)
            return comparison;
        comparison = firstMetrics.CellWidth.CompareTo(secondMetrics.CellWidth);
        if (comparison != 0)
            return comparison;
        comparison = firstMetrics.LayerHeight.CompareTo(secondMetrics.LayerHeight);
        if (comparison != 0)
            return comparison;
        comparison = firstMetrics.CellLength.CompareTo(secondMetrics.CellLength);
        return comparison != 0
            ? comparison
            : ((int)firstMetrics.HexOrientation).CompareTo((int)secondMetrics.HexOrientation);
    }

    private static int CompareVectors(Vector3d first, Vector3d second)
    {
        int comparison = first.X.CompareTo(second.X);
        if (comparison != 0)
            return comparison;
        comparison = first.Y.CompareTo(second.Y);
        return comparison != 0 ? comparison : first.Z.CompareTo(second.Z);
    }
}
