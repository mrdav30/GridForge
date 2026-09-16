//=======================================================================
// GridTracer.RectangularSlabs.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;

public static partial class GridTracer
{
    /// <summary>Attempts a bounded trace observed after each complete rectangular X slab.</summary>
    /// <remarks>
    /// Returns false only before chargeable discovery when the original segment or expected single-layer
    /// rectangular generation is unsupported. Results are then empty and the observer is not invoked.
    /// All qualified terminal outcomes return true, including ceilings. The observer runs under the world
    /// read lock and must not reenter world/grid operations or mutate results or scratch. Reading world
    /// identity/change-stamp properties for freshness fences is allowed. Scratch is not thread-safe.
    /// Returning false from the observer retains emission-order intervals, not a canonical ray prefix;
    /// ties and coverage remain unassigned. Continuation sorts canonically and source ordinals follow every
    /// move. The ordinal span must fit outputLimit. Failure or exception clears results and used ordinals;
    /// exceptions propagate after cleanup. World stamps are captured before qualification, not refreshed by
    /// callbacks; concurrent obstacle changes remain possible and consumers own their final freshness fence.
    /// </remarks>
    public static bool TryTraceRectangularSlabsInto(
        GridWorld world, Vector3d start, Vector3d end,
        in GridCoveredAddressGeneration expectedGeneration,
        SwiftList<GridTraceInterval> results, GridTraceIntervalScratch scratch,
        Span<int> intervalSourceOrdinals, Func<GridTraceSlab, bool> continueTrace,
        int gridCandidateLimit, int addressCandidateLimit, int outputLimit,
        long candidateWorkLimit, out GridTraceIntervalReport report)
    {
        ValidateTraceArguments(results, scratch, gridCandidateLimit, addressCandidateLimit, outputLimit, candidateWorkLimit);
        SwiftThrowHelper.ThrowIfNull(continueTrace, nameof(continueTrace));
        SwiftThrowHelper.ThrowIfArgument(intervalSourceOrdinals.Length < outputLimit,
            nameof(intervalSourceOrdinals), "Source ordinals must fit the output limit.");

        report = default;
        scratch.Enter();
        int usedTags = 0;
        bool retainOutput = false;
        try
        {
            results.Clear();
            if (world == null || !world.IsActive)
                return false;

            world.EnterReadLock();
            try
            {
                // Capture before checking the expected generation: the read lock protects grid lifetime,
                // but concurrent obstacle changes can still commit under ChangeSyncRoot.
                long worldSpawnToken = world.SpawnToken;
                ulong worldChangeSequence = world.ChangeSequence;
                (Vector3d queryMin, Vector3d queryMax) = CreatePaddedOrderedBounds(start, end, padding: null);
                if (!TryQualifyRectangularSlabs(world, start, end, queryMin, queryMax, expectedGeneration,
                        out VoxelGrid? grid, out VoxelIndex minIndex, out VoxelIndex maxIndex, out Fixed64 deltaX))
                    return false;

                (Vector3d candidateMin, Vector3d candidateMax) =
                    ExpandOrderedBounds(queryMin, queryMax, world.MaxTopologyCellEdge);
                bool gridWorkIsTighter = candidateWorkLimit < gridCandidateLimit;
                int effectiveGridLimit = gridWorkIsTighter ? (int)candidateWorkLimit : gridCandidateLimit;
                if (!world.CollectGridCandidates(candidateMin, candidateMax, scratch.CandidateGrids, effectiveGridLimit))
                {
                    report = FailTrace(results, gridWorkIsTighter
                        ? GridTraceIntervalStatus.CandidateWorkLimitExceeded : GridTraceIntervalStatus.GridCandidateLimitExceeded,
                        scratch.CandidateGrids.Count, 0);
                    return true;
                }

                // Snapped topology ranges can reach a grid outside the world's discovery bounds.
                // Match the eager tracer: no discovered grid means no admitted address or slab.
                if (scratch.CandidateGrids.Count == 0)
                {
                    report = CreateTraceReport(GridTraceIntervalStatus.Complete, 0, 0, results);
                    retainOutput = true;
                    return true;
                }

                long remainingWork = candidateWorkLimit - scratch.CandidateGrids.Count;
                bool addressWorkIsTighter = remainingWork < addressCandidateLimit;
                int effectiveAddressLimit = addressWorkIsTighter ? (int)remainingWork : addressCandidateLimit;
                int addressCount = 0;
                if (!TryAdmitTraceRange(minIndex, maxIndex, effectiveAddressLimit, ref addressCount))
                {
                    report = FailTrace(results, addressWorkIsTighter
                        ? GridTraceIntervalStatus.CandidateWorkLimitExceeded : GridTraceIntervalStatus.AddressCandidateLimitExceeded,
                        scratch.CandidateGrids.Count, addressCount);
                    return true;
                }

                for (int x = minIndex.x; x <= maxIndex.x; x++)
                {
                    scratch.AddressCandidates.Clear();
                    CollectRectangularSegmentSlab(grid!, start, end, deltaX, minIndex, maxIndex, x, scratch);
                    if (grid!.StorageKind == GridStorageKind.Sparse)
                        SnapshotSparsePresence(world, scratch.AddressCandidates);

                    int intervalStart = results.Count;
                    GridTraceIntervalStatus status = AppendTraceIntervals(world, start, end, results, scratch,
                        outputLimit, intervalSourceOrdinals, ref usedTags);
                    if (status != GridTraceIntervalStatus.Complete)
                    {
                        report = FailTrace(results, status, scratch.CandidateGrids.Count, addressCount);
                        return true;
                    }

                    GridTraceSlab slab = new GridTraceSlab(x, intervalStart, results.Count - intervalStart,
                        RectangularSlabSeparatesEndpoints(grid, x, start.X, end.X),
                        worldSpawnToken, worldChangeSequence, expectedGeneration);
                    if (!continueTrace(slab))
                    {
                        report = new GridTraceIntervalReport(GridTraceIntervalStatus.StoppedAfterCompleteSlab,
                            scratch.CandidateGrids.Count, addressCount, results.Count, 0, false, false);
                        retainOutput = true;
                        return true;
                    }
                }

                SortIntervals(results, intervalSourceOrdinals);
                report = CreateTraceReport(GridTraceIntervalStatus.Complete, scratch.CandidateGrids.Count, addressCount, results);
                retainOutput = true;
                return true;
            }
            finally
            {
                world.ExitReadLock();
            }
        }
        finally
        {
            if (!retainOutput)
            {
                results.Clear();
                intervalSourceOrdinals.Slice(0, usedTags).Clear();
            }
            scratch.Exit();
        }
    }

    private static bool TryQualifyRectangularSlabs(GridWorld world, Vector3d start, Vector3d end,
        Vector3d queryMin, Vector3d queryMax, in GridCoveredAddressGeneration expected,
        out VoxelGrid? grid, out VoxelIndex minIndex, out VoxelIndex maxIndex, out Fixed64 deltaX)
    {
        grid = null;
        minIndex = maxIndex = default;
        deltaX = default;
        if (world.ActiveGrids.Count != 1 || !world.ActiveGrids.IsAllocated(expected.GridIndex))
            return false;
        grid = world.ActiveGrids[expected.GridIndex];
        if (!grid.IsActive || grid.SpawnToken != expected.GridSpawnToken
            || grid.LastChangeSequence != expected.GridLastChangeSequence
            || grid.Configuration.ToGridKey() != expected.ConfigurationKey
            || grid.Topology.Kind != GridTopologyKind.RectangularPrism || grid.Height != 1
            || !Fixed64.TrySubtract(end.X, start.X, out deltaX) || deltaX == Fixed64.Zero
            || start.Z == end.Z
            || !TopologyVoxelRangeUtility.TryGetPrismCandidateRange(grid, queryMin, queryMax, out minIndex, out maxIndex))
            return false;

        return IsRepresentableSlabExtreme(grid, minIndex, start, end)
            && IsRepresentableSlabExtreme(grid, maxIndex, start, end);
    }

    private static bool IsRepresentableSlabExtreme(VoxelGrid grid, VoxelIndex index, Vector3d start, Vector3d end)
    {
        if (!GridCellGeometry.TryCreatePrism(grid.Topology.Kind, grid.Topology.Metrics,
                grid.GetWorldPosition(index), default, out GridCellPrism prism))
            return false;
        Vector2d minimum = prism.GetFootprintVertex(0);
        Vector2d maximum = prism.GetFootprintVertex(2);
        return GridCellGeometry.TryGetPlanarSlab(start.X, end.X, minimum.X, maximum.X, out _, out _, out _)
            && GridCellGeometry.TryGetPlanarSlab(start.Z, end.Z, minimum.Y, maximum.Y, out _, out _, out _);
    }

    private static bool RectangularSlabSeparatesEndpoints(VoxelGrid grid, int x, Fixed64 start, Fixed64 end)
    {
        // Qualification proves actual extreme faces and positive planar differences representable.
        Fixed64 center = grid.BoundsMin.X + x * grid.Topology.Metrics.CellWidth;
        Fixed64 halfWidth = grid.Topology.Metrics.CellWidth * Fixed64.Half;
        Fixed64 left = center - halfWidth;
        Fixed64 right = center + halfWidth;
        Fixed64 first = FixedMath.Min(start, end);
        Fixed64 last = FixedMath.Max(start, end);
        return Fixed64.TrySubtract(last, first, out Fixed64 delta)
            && Fixed64.TrySubtract(left, first, out Fixed64 before)
            && Fixed64.TrySubtract(last, right, out Fixed64 after)
            && before > Fixed64.Zero && after > Fixed64.Zero
            && Fixed64.CompareProducts(before, Fixed64.One, delta, Fixed64.MinIncrement) > 0
            && Fixed64.CompareProducts(after, Fixed64.One, delta, Fixed64.MinIncrement) > 0;
    }
}
