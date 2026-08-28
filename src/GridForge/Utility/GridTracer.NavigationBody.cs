//=======================================================================
// GridTracer.NavigationBody.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
using FixedMathSharp.Geometry;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;

/// <content>Provides bounded swept navigation-body coverage.</content>
public static partial class GridTracer
{
    /// <summary>Writes the canonical cells required by one direct upright-body sweep.</summary>
    /// <remarks>
    /// The start and end bodies must have closed-set contact with the declared source and target
    /// prisms respectively; exact endpoint tangency is admitted for that identity check.
    /// A prism is claimed only when its planar and vertical interiors overlap the swept body at
    /// one shared continuous parameter. Boundary-only coincidence and tangency are excluded.
    /// Grid, address, output, and combined candidate-work ceilings are independent.
    /// </remarks>
    public static GridNavigationBodyTraceReport TraceNavigationBodyInto(
        GridWorld world,
        WorldVoxelIndex source,
        WorldVoxelIndex target,
        Vector3d startFoot,
        Vector3d endFoot,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        SwiftList<GridNavigationBodyTraceCell> results,
        GridNavigationBodyTraceScratch scratch,
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
        if (world == null
            || !world.IsActive
            || horizontalRadius < Fixed64.Zero
            || bodyHeight <= Fixed64.Zero)
        {
            return CreateNavigationBodyTraceReport(
                GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                0,
                0,
                results,
                default);
        }

        if (!Fixed64.TryAdd(startFoot.Y, bodyHeight, out Fixed64 startTop)
            || !Fixed64.TryAdd(endFoot.Y, bodyHeight, out Fixed64 endTop)
            || !TryCreateNavigationBodyBounds(
                startFoot,
                endFoot,
                startTop,
                endTop,
                horizontalRadius,
                out Vector3d queryMin,
                out Vector3d queryMax))
        {
            return CreateNavigationBodyTraceReport(
                GridNavigationBodyTraceStatus.ArithmeticOverflow,
                0,
                0,
                results,
                default);
        }

        world.EnterReadLock();
        try
        {
            if (!world.TryGetGrid(source, out VoxelGrid? sourceGridValue)
                || !world.TryGetGrid(target, out VoxelGrid? targetGridValue))
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                    0,
                    0,
                    default);
            }

            VoxelGrid sourceGrid = sourceGridValue!;
            VoxelGrid targetGrid = targetGridValue!;
            if (!GridCellGeometry.TryCreatePrism(
                    sourceGrid.Configuration.TopologyKind,
                    sourceGrid.Configuration.TopologyMetrics,
                    sourceGrid.GetWorldPosition(source.VoxelIndex),
                    source,
                    out GridCellPrism sourcePrism)
                || !GridCellGeometry.TryCreatePrism(
                    targetGrid.Configuration.TopologyKind,
                    targetGrid.Configuration.TopologyMetrics,
                    targetGrid.GetWorldPosition(target.VoxelIndex),
                    target,
                    out GridCellPrism targetPrism))
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                    0,
                    0,
                    default);
            }

            if (!HasClosedNavigationBodyPrismContact(
                    sourcePrism,
                    startFoot,
                    startTop,
                    horizontalRadius)
                || !HasClosedNavigationBodyPrismContact(
                    targetPrism,
                    endFoot,
                    endTop,
                    horizontalRadius))
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                    0,
                    0,
                    default);
            }

            Span<GridCellPrism> closurePrisms = stackalloc GridCellPrism[8];
            int closureCount = GetNavigationClosure(
                sourceGrid,
                sourcePrism,
                targetPrism,
                closurePrisms);
            if (closureCount == 0)
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                    0,
                    0,
                    default);
            }

            if (!TryExpandNavigationBodyBounds(
                    queryMin,
                    queryMax,
                    world.MaxTopologyCellEdge,
                    out Vector3d candidateMin,
                    out Vector3d candidateMax))
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.ArithmeticOverflow,
                    0,
                    0,
                    default);
            }

            bool gridWorkLimitIsTighter = candidateWorkLimit < gridCandidateLimit;
            int effectiveGridLimit = gridWorkLimitIsTighter
                ? (int)candidateWorkLimit
                : gridCandidateLimit;
            if (!world.CollectGridCandidates(
                    candidateMin,
                    candidateMax,
                    scratch.CandidateGrids,
                    effectiveGridLimit))
            {
                return FailNavigationBodyTrace(
                    results,
                    gridWorkLimitIsTighter
                        ? GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded
                        : GridNavigationBodyTraceStatus.GridCandidateLimitExceeded,
                    scratch.CandidateGrids.Count,
                    0,
                    default);
            }

            SortGridIndices(world, scratch.CandidateGrids);
            long remainingWork = candidateWorkLimit - scratch.CandidateGrids.Count;
            bool workLimitIsTighter = remainingWork < addressCandidateLimit;
            int effectiveAddressLimit = workLimitIsTighter
                ? (int)remainingWork
                : addressCandidateLimit;
            int addressCandidateCount = 0;
            for (int gridOrdinal = 0; gridOrdinal < scratch.CandidateGrids.Count; gridOrdinal++)
            {
                VoxelGrid grid = world.ActiveGrids[scratch.CandidateGrids[gridOrdinal]];
                if (!TopologyVoxelRangeUtility.TryGetPrismCandidateRange(
                        grid,
                        queryMin,
                        queryMax,
                        out VoxelIndex minimum,
                        out VoxelIndex maximum))
                {
                    continue;
                }

                for (int x = minimum.x; x <= maximum.x; x++)
                {
                    for (int y = minimum.y; y <= maximum.y; y++)
                    {
                        for (int z = minimum.z; z <= maximum.z; z++)
                        {
                            if (addressCandidateCount >= effectiveAddressLimit)
                            {
                                return FailNavigationBodyTrace(
                                    results,
                                    workLimitIsTighter
                                        ? GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded
                                        : GridNavigationBodyTraceStatus.AddressLimitExceeded,
                                    scratch.CandidateGrids.Count,
                                    addressCandidateCount,
                                    default);
                            }

                            addressCandidateCount++;
                            VoxelIndex index = new(x, y, z);
                            WorldVoxelIndex cell = new(
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
                                return FailNavigationBodyTrace(
                                    results,
                                    GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                                    scratch.CandidateGrids.Count,
                                    addressCandidateCount,
                                    default);
                            }

                            bool hasPositiveOverlap =
                                GridCellGeometry.HasPositiveNavigationBodyPrismOverlap(
                                    prism,
                                    startFoot,
                                    endFoot,
                                    horizontalRadius,
                                    bodyHeight);
                            bool isClosure = IsNavigationClosurePrism(
                                prism,
                                closurePrisms,
                                closureCount);
                            if (!hasPositiveOverlap && !isClosure)
                                continue;

                            scratch.AddressCandidates.Add(new GridNavigationBodyTraceCandidate(
                                grid,
                                index,
                                prism,
                                hasPositiveOverlap,
                                isClosure));
                        }
                    }
                }
            }

            GridCoveredAddressRunStamp runStamp = SnapshotNavigationBodyCandidates(
                world,
                scratch.AddressCandidates);
            scratch.AddressCandidates.SortInPlace(
                default(GridNavigationBodyTraceCandidateComparer));
            if (!HasNavigationBodyUnionCoverage(
                    sourceGrid,
                    source.VoxelIndex,
                    targetGrid,
                    target.VoxelIndex,
                    startFoot,
                    endFoot,
                    horizontalRadius,
                    bodyHeight,
                    scratch.AddressCandidates,
                    scratch.UnionMembers))
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry,
                    scratch.CandidateGrids.Count,
                    addressCandidateCount,
                    default);
            }
            int alternativeEvidenceCount = CountMissingNavigationBodyAlternativeEvidence(
                scratch.AddressCandidates,
                sourceGrid,
                source.VoxelIndex,
                targetGrid,
                target.VoxelIndex);
            if (scratch.UnionMembers.Count > outputLimit
                || alternativeEvidenceCount > outputLimit - scratch.UnionMembers.Count)
            {
                return FailNavigationBodyTrace(
                    results,
                    GridNavigationBodyTraceStatus.OutputLimitExceeded,
                    scratch.CandidateGrids.Count,
                    addressCandidateCount,
                    default);
            }

            bool hasMissingPhysicalCell = false;
            for (int i = 0; i < scratch.UnionMembers.Count; i++)
            {
                GridNavigationBodyTraceCandidate candidate =
                    scratch.AddressCandidates[scratch.UnionMembers[i]];
                hasMissingPhysicalCell |= !candidate.IsPhysicallyPresent;
                results.Add(new GridNavigationBodyTraceCell(
                    new WorldVoxelIndex(
                        world.SpawnToken,
                        candidate.Grid.GridIndex,
                        candidate.Grid.SpawnToken,
                        candidate.Index),
                    candidate.Grid.Configuration.ToGridKey(),
                    candidate.IsPhysicallyPresent,
                    candidate.GridLastChangeSequence,
                    GridNavigationBodyTraceCellRole.RequiredCoverage));
            }

            if (hasMissingPhysicalCell)
            {
                AppendMissingNavigationBodyAlternativeEvidence(
                    world,
                    scratch.AddressCandidates,
                    results,
                    sourceGrid,
                    source.VoxelIndex,
                    targetGrid,
                    target.VoxelIndex);
            }

            results.SortInPlace(default(GridNavigationBodyTraceCellComparer));
            return CreateNavigationBodyTraceReport(
                hasMissingPhysicalCell
                    ? GridNavigationBodyTraceStatus.IncompletePhysicalCoverage
                    : GridNavigationBodyTraceStatus.Complete,
                scratch.CandidateGrids.Count,
                addressCandidateCount,
                results,
                runStamp);
        }
        finally
        {
            world.ExitReadLock();
            scratch.Clear();
        }
    }

    private static bool HasClosedNavigationBodyPrismContact(
        in GridCellPrism prism,
        Vector3d foot,
        Fixed64 bodyTop,
        Fixed64 horizontalRadius)
    {
        if (foot.Y > prism.VerticalMax || bodyTop < prism.VerticalMin)
            return false;

        Span<Vector2d> offsets = stackalloc Vector2d[6];
        Vector2d planarOrigin = new(prism.Center.X, prism.Center.Z);
        for (int i = 0; i < prism.FootprintVertexCount; i++)
            offsets[i] = prism.GetFootprintVertex(i) - planarOrigin;

        return FixedConvex2dRelations.TryGetCircleContact(
            new Vector2d(foot.X, foot.Z),
            Fixed64.Zero,
            horizontalRadius,
            planarOrigin,
            Fixed64.Zero,
            offsets[..prism.FootprintVertexCount],
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    private static int GetNavigationClosure(
        VoxelGrid sourceGrid,
        in GridCellPrism source,
        in GridCellPrism target,
        Span<GridCellPrism> closure)
    {
        Span<VoxelIndex> offsets = stackalloc VoxelIndex[8];
        VoxelIndex targetOffset = default;
        bool foundTarget = AreSameNavigationBodyPrism(source, target);
        for (int slot = 0; !foundTarget && slot < sourceGrid.Topology.NeighborSlotCount; slot++)
        {
            VoxelIndex offset = sourceGrid.Topology.GetNeighborOffset(slot);
            if (!Vector3d.TryAdd(
                    source.Center,
                    sourceGrid.Topology.GetWorldOffset((offset.x, offset.y, offset.z)),
                    out Vector3d center)
                || !GridCellGeometry.TryCreatePrism(
                    sourceGrid.Configuration.TopologyKind,
                    sourceGrid.Configuration.TopologyMetrics,
                    center,
                    default,
                    out GridCellPrism neighbor))
            {
                return 0;
            }
            if (AreSameNavigationBodyPrism(neighbor, target))
            {
                targetOffset = offset;
                foundTarget = true;
            }
        }
        if (!foundTarget)
            return 0;

        int closureCount;
        if (targetOffset == default)
        {
            offsets[0] = default;
            closureCount = 1;
        }
        else if (sourceGrid.Configuration.TopologyKind == GridTopologyKind.RectangularPrism)
        {
            RectangularDirection direction = RectangularDirectionUtility.GetDirectionFromOffset(
                (targetOffset.x, targetOffset.y, targetOffset.z));
            closureCount = RectangularDirectionUtility.CopyNavigationClosureOffsets(direction, offsets);
        }
        else
        {
            closureCount = HexDirectionUtility.CopyNavigationClosureOffsets(targetOffset, offsets);
        }

        for (int i = 0; i < closureCount; i++)
        {
            VoxelIndex offset = offsets[i];
            bool usesTargetPlanarCoordinates = offset.x != 0 || offset.z != 0;
            Vector3d center = sourceGrid.Configuration.TopologyKind == GridTopologyKind.RectangularPrism
                ? new Vector3d(
                    offset.x == 0 ? source.Center.X : target.Center.X,
                    offset.y == 0 ? source.Center.Y : target.Center.Y,
                    offset.z == 0 ? source.Center.Z : target.Center.Z)
                : new Vector3d(
                    usesTargetPlanarCoordinates ? target.Center.X : source.Center.X,
                    offset.y == 0 ? source.Center.Y : target.Center.Y,
                    usesTargetPlanarCoordinates ? target.Center.Z : source.Center.Z);

            // Every coordinate comes from one of the two already validated endpoint prisms.
            _ = GridCellGeometry.TryCreatePrism(
                sourceGrid.Configuration.TopologyKind,
                sourceGrid.Configuration.TopologyMetrics,
                center,
                default,
                out closure[i]);
        }

        return closureCount;
    }

    private static bool IsNavigationClosurePrism(
        in GridCellPrism prism,
        ReadOnlySpan<GridCellPrism> closure,
        int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (AreSameNavigationBodyPrism(prism, closure[i]))
                return true;
        }

        return false;
    }

    private static GridCoveredAddressRunStamp SnapshotNavigationBodyCandidates(
        GridWorld world,
        SwiftList<GridNavigationBodyTraceCandidate> candidates)
    {
        GridCoveredAddressRunStamp runStamp;
        lock (world.ChangeSyncRoot)
        {
            runStamp = new GridCoveredAddressRunStamp(
                world.SpawnToken,
                world.Version,
                world.ChangeSequence);
            for (int i = 0; i < candidates.Count; i++)
            {
                GridNavigationBodyTraceCandidate candidate = candidates[i];
                bool isPresent = candidate.Grid.StorageKind == GridStorageKind.Dense
                    || candidate.Grid.TryGetVoxel(candidate.Index, out _);
                candidates[i] = candidate.WithPhysicalEvidence(
                    isPresent,
                    candidate.Grid.LastChangeSequence);
            }
        }

        return runStamp;
    }

    private static bool TryCreateNavigationBodyBounds(
        Vector3d startFoot,
        Vector3d endFoot,
        Fixed64 startTop,
        Fixed64 endTop,
        Fixed64 radius,
        out Vector3d minimum,
        out Vector3d maximum)
    {
        minimum = default;
        maximum = default;
        return Fixed64.TrySubtract(FixedMath.Min(startFoot.X, endFoot.X), radius, out Fixed64 minX)
            && Fixed64.TrySubtract(FixedMath.Min(startFoot.Z, endFoot.Z), radius, out Fixed64 minZ)
            && Fixed64.TryAdd(FixedMath.Max(startFoot.X, endFoot.X), radius, out Fixed64 maxX)
            && Fixed64.TryAdd(FixedMath.Max(startFoot.Z, endFoot.Z), radius, out Fixed64 maxZ)
            && AssignNavigationBodyBounds(
                minX,
                FixedMath.Min(startFoot.Y, endFoot.Y),
                minZ,
                maxX,
                FixedMath.Max(startTop, endTop),
                maxZ,
                out minimum,
                out maximum);
    }

    private static bool AssignNavigationBodyBounds(
        Fixed64 minX,
        Fixed64 minY,
        Fixed64 minZ,
        Fixed64 maxX,
        Fixed64 maxY,
        Fixed64 maxZ,
        out Vector3d minimum,
        out Vector3d maximum)
    {
        minimum = new Vector3d(minX, minY, minZ);
        maximum = new Vector3d(maxX, maxY, maxZ);
        return true;
    }

    private static bool TryExpandNavigationBodyBounds(
        Vector3d minimum,
        Vector3d maximum,
        Fixed64 expansion,
        out Vector3d expandedMinimum,
        out Vector3d expandedMaximum)
    {
        if (!Fixed64.TrySubtract(minimum.X, expansion, out Fixed64 minX)
            || !Fixed64.TrySubtract(minimum.Y, expansion, out Fixed64 minY)
            || !Fixed64.TrySubtract(minimum.Z, expansion, out Fixed64 minZ)
            || !Fixed64.TryAdd(maximum.X, expansion, out Fixed64 maxX)
            || !Fixed64.TryAdd(maximum.Y, expansion, out Fixed64 maxY)
            || !Fixed64.TryAdd(maximum.Z, expansion, out Fixed64 maxZ))
        {
            expandedMinimum = default;
            expandedMaximum = default;
            return false;
        }

        expandedMinimum = new Vector3d(minX, minY, minZ);
        expandedMaximum = new Vector3d(maxX, maxY, maxZ);
        return true;
    }

    private static bool HasNavigationBodyUnionCoverage(
        VoxelGrid sourceGrid,
        VoxelIndex source,
        VoxelGrid targetGrid,
        VoxelIndex target,
        Vector3d startFoot,
        Vector3d endFoot,
        Fixed64 horizontalRadius,
        Fixed64 bodyHeight,
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        SwiftList<int> unionMembers)
    {
        int sourceCandidate = FindNavigationBodyCandidate(candidates, sourceGrid, source);
        int targetCandidate = FindNavigationBodyCandidate(candidates, targetGrid, target);
        // A connected body intersects a connected set of interiors in the source topology's
        // exact lattice. Closing every positive-overlap neighbor therefore proves containment;
        // exact coincident prisms let aligned adjacent grids continue the same lattice.
        AddNavigationBodyUnionMember(candidates, unionMembers, sourceCandidate);
        if (targetCandidate != sourceCandidate
            && AreSameNavigationBodyPrism(
                candidates[sourceCandidate].Prism,
                candidates[targetCandidate].Prism))
        {
            AddNavigationBodyUnionMember(candidates, unionMembers, targetCandidate);
        }
        for (int memberOrdinal = 0; memberOrdinal < unionMembers.Count; memberOrdinal++)
        {
            GridCellPrism member = candidates[unionMembers[memberOrdinal]].Prism;
            for (int slot = 0; slot < sourceGrid.Topology.NeighborSlotCount; slot++)
            {
                VoxelIndex offset = sourceGrid.Topology.GetNeighborOffset(slot);
                // Candidate bounds were expanded by the world's maximum topology edge, so every
                // immediate neighbor of a selected member is exactly representable here.
                _ = Vector3d.TryAdd(
                    member.Center,
                    sourceGrid.Topology.GetWorldOffset((offset.x, offset.y, offset.z)),
                    out Vector3d neighborCenter);
                _ = GridCellGeometry.TryCreatePrism(
                    sourceGrid.Configuration.TopologyKind,
                    sourceGrid.Configuration.TopologyMetrics,
                    neighborCenter,
                    default,
                    out GridCellPrism neighbor);

                int matchingCandidate = FindBestMatchingNavigationBodyPrism(
                    candidates,
                    neighbor,
                    sourceCandidate,
                    targetCandidate);
                bool overlapsBody = matchingCandidate >= 0
                    ? candidates[matchingCandidate].HasPositiveOverlap
                    : GridCellGeometry.HasPositiveNavigationBodyPrismOverlap(
                        neighbor,
                        startFoot,
                        endFoot,
                        horizontalRadius,
                        bodyHeight);
                if (overlapsBody && matchingCandidate < 0)
                    return false;
                if (matchingCandidate >= 0
                    && !candidates[matchingCandidate].IsVisited)
                {
                    AddNavigationBodyUnionMember(candidates, unionMembers, matchingCandidate);
                }
            }
        }

        return true;
    }

    private static int FindNavigationBodyCandidate(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        VoxelGrid grid,
        VoxelIndex index)
    {
        for (int i = 0;; i++)
        {
            GridNavigationBodyTraceCandidate candidate = candidates[i];
            if (candidate.Grid == grid && candidate.Index == index)
                return i;
        }
    }

    private static int FindBestMatchingNavigationBodyPrism(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        in GridCellPrism prism,
        int sourceCandidate,
        int targetCandidate)
    {
        if (AreSameNavigationBodyPrism(candidates[sourceCandidate].Prism, prism))
            return sourceCandidate;
        if (AreSameNavigationBodyPrism(candidates[targetCandidate].Prism, prism))
            return targetCandidate;

        if (!FindNavigationBodyPrismRange(candidates, prism, out int start, out int end))
            return -1;

        int best = start;
        for (int i = start + 1; i < end; i++)
        {
            if (IsPreferredNavigationBodyCandidate(candidates[i], candidates[best]))
                best = i;
        }

        return best;
    }

    private static bool IsPreferredNavigationBodyCandidate(
        GridNavigationBodyTraceCandidate candidate,
        GridNavigationBodyTraceCandidate current)
    {
        if (candidate.IsPhysicallyPresent != current.IsPhysicallyPresent)
            return candidate.IsPhysicallyPresent;

        return CompareGridIdentity(candidate.Grid, current.Grid) < 0;
    }

    private static bool AreSameNavigationBodyPrism(
        in GridCellPrism first,
        in GridCellPrism second) => CompareNavigationBodyPrisms(first, second) == 0;

    private static int CompareNavigationBodyPrisms(
        in GridCellPrism first,
        in GridCellPrism second)
    {
        int comparison = (int)first.TopologyKind - (int)second.TopologyKind;
        if (comparison != 0)
            return comparison;
        comparison = first.Center.X.CompareTo(second.Center.X);
        if (comparison != 0)
            return comparison;
        comparison = first.Center.Y.CompareTo(second.Center.Y);
        if (comparison != 0)
            return comparison;
        comparison = first.Center.Z.CompareTo(second.Center.Z);
        if (comparison != 0)
            return comparison;
        comparison = first.VerticalMin.CompareTo(second.VerticalMin);
        if (comparison != 0)
            return comparison;
        comparison = first.PlanarInradius.CompareTo(second.PlanarInradius);
        if (comparison != 0)
            return comparison;

        for (int i = 0; i < first.FootprintVertexCount; i++)
        {
            Vector2d firstVertex = first.GetFootprintVertex(i);
            Vector2d secondVertex = second.GetFootprintVertex(i);
            comparison = firstVertex.X.CompareTo(secondVertex.X);
            if (comparison != 0)
                return comparison;
            comparison = firstVertex.Y.CompareTo(secondVertex.Y);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private static bool FindNavigationBodyPrismRange(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        in GridCellPrism prism,
        out int start,
        out int end)
    {
        int low = 0;
        int high = candidates.Count;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (CompareNavigationBodyPrisms(candidates[middle].Prism, prism) < 0)
                low = middle + 1;
            else
                high = middle;
        }

        start = low;
        if (start >= candidates.Count
            || CompareNavigationBodyPrisms(candidates[start].Prism, prism) != 0)
        {
            end = start;
            return false;
        }

        low = start + 1;
        high = candidates.Count;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (CompareNavigationBodyPrisms(candidates[middle].Prism, prism) <= 0)
                low = middle + 1;
            else
                high = middle;
        }

        end = low;
        return true;
    }

    private static void AddNavigationBodyUnionMember(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        SwiftList<int> unionMembers,
        int candidateIndex)
    {
        candidates[candidateIndex] = candidates[candidateIndex].WithVisited();
        unionMembers.Add(candidateIndex);
    }

    private static void AppendMissingNavigationBodyAlternativeEvidence(
        GridWorld world,
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        SwiftList<GridNavigationBodyTraceCell> results,
        VoxelGrid sourceGrid,
        VoxelIndex source,
        VoxelGrid targetGrid,
        VoxelIndex target)
    {
        for (int start = 0; start < candidates.Count;)
        {
            int end = GetNavigationBodyPrismGroupEnd(candidates, start);
            if (!IsMissingNavigationBodyAlternativeGroup(
                    candidates,
                    start,
                    end,
                    sourceGrid,
                    source,
                    targetGrid,
                    target))
            {
                start = end;
                continue;
            }

            for (int candidateIndex = start; candidateIndex < end; candidateIndex++)
            {
                GridNavigationBodyTraceCandidate candidate = candidates[candidateIndex];
                if (candidate.IsVisited)
                    continue;

                candidates[candidateIndex] = candidate.WithVisited();
                results.Add(new GridNavigationBodyTraceCell(
                    new WorldVoxelIndex(
                        world.SpawnToken,
                        candidate.Grid.GridIndex,
                        candidate.Grid.SpawnToken,
                        candidate.Index),
                    candidate.Grid.Configuration.ToGridKey(),
                    isPhysicallyPresent: false,
                    candidate.GridLastChangeSequence,
                    GridNavigationBodyTraceCellRole.PhysicalAlternativeDependency));
            }

            start = end;
        }
    }

    private static int CountMissingNavigationBodyAlternativeEvidence(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        VoxelGrid sourceGrid,
        VoxelIndex source,
        VoxelGrid targetGrid,
        VoxelIndex target)
    {
        int count = 0;
        for (int start = 0; start < candidates.Count;)
        {
            int end = GetNavigationBodyPrismGroupEnd(candidates, start);
            if (IsMissingNavigationBodyAlternativeGroup(
                    candidates,
                    start,
                    end,
                    sourceGrid,
                    source,
                    targetGrid,
                    target))
            {
                for (int candidateIndex = start; candidateIndex < end; candidateIndex++)
                    count += candidates[candidateIndex].IsVisited ? 0 : 1;
            }

            start = end;
        }

        return count;
    }

    private static int GetNavigationBodyPrismGroupEnd(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        int start)
    {
        GridCellPrism prism = candidates[start].Prism;
        int end = start + 1;
        while (end < candidates.Count
            && CompareNavigationBodyPrisms(candidates[end].Prism, prism) == 0)
        {
            end++;
        }

        return end;
    }

    private static bool IsMissingNavigationBodyAlternativeGroup(
        SwiftList<GridNavigationBodyTraceCandidate> candidates,
        int start,
        int end,
        VoxelGrid sourceGrid,
        VoxelIndex source,
        VoxelGrid targetGrid,
        VoxelIndex target)
    {
        bool hasMissingSelectedNonEndpoint = false;
        for (int candidateIndex = start; candidateIndex < end; candidateIndex++)
        {
            GridNavigationBodyTraceCandidate candidate = candidates[candidateIndex];
            if (candidate.IsPhysicallyPresent)
                return false;
            hasMissingSelectedNonEndpoint |= candidate.IsVisited
                && !IsNavigationBodyEndpoint(
                    candidate,
                    sourceGrid,
                    source,
                    targetGrid,
                    target);
        }

        return hasMissingSelectedNonEndpoint;
    }

    private static bool IsNavigationBodyEndpoint(
        GridNavigationBodyTraceCandidate candidate,
        VoxelGrid sourceGrid,
        VoxelIndex source,
        VoxelGrid targetGrid,
        VoxelIndex target) =>
        (candidate.Grid == sourceGrid && candidate.Index == source)
        || (candidate.Grid == targetGrid && candidate.Index == target);

    private static GridNavigationBodyTraceReport CreateNavigationBodyTraceReport(
        GridNavigationBodyTraceStatus status,
        int gridCandidateCount,
        int addressCandidateCount,
        SwiftList<GridNavigationBodyTraceCell> results,
        GridCoveredAddressRunStamp runStamp) =>
        new(
            status,
            gridCandidateCount,
            addressCandidateCount,
            gridCandidateCount + (long)addressCandidateCount,
            results.Count,
            runStamp);

    private static GridNavigationBodyTraceReport FailNavigationBodyTrace(
        SwiftList<GridNavigationBodyTraceCell> results,
        GridNavigationBodyTraceStatus status,
        int gridCandidateCount,
        int addressCandidateCount,
        GridCoveredAddressRunStamp runStamp)
    {
        results.Clear();
        return CreateNavigationBodyTraceReport(
            status,
            gridCandidateCount,
            addressCandidateCount,
            results,
            runStamp);
    }

    private readonly struct GridNavigationBodyTraceCandidateComparer :
        IComparer<GridNavigationBodyTraceCandidate>
    {
        public int Compare(
            GridNavigationBodyTraceCandidate first,
            GridNavigationBodyTraceCandidate second) =>
            CompareNavigationBodyPrisms(first.Prism, second.Prism);
    }

    private readonly struct GridNavigationBodyTraceCellComparer :
        IComparer<GridNavigationBodyTraceCell>
    {
        public int Compare(
            GridNavigationBodyTraceCell first,
            GridNavigationBodyTraceCell second) =>
            CompareNavigationBodyTraceCells(first, second);
    }

    private static int CompareNavigationBodyTraceCells(
        GridNavigationBodyTraceCell first,
        GridNavigationBodyTraceCell second)
    {
        int comparison = CompareConfigurationKeys(first.ConfigurationKey, second.ConfigurationKey);
        return comparison != 0
            ? comparison
            : first.Cell.VoxelIndex.CompareTo(second.Cell.VoxelIndex);
    }
}
