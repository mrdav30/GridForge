//=======================================================================
// GridTracer.PlanarNavigationBody.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace GridForge.Utility;

public static partial class GridTracer
{
    /// <summary>Writes bounded native planar upright-capsule coverage for one immediate neighbor leg.</summary>
    /// <remarks>
    /// Eligible generations must be strictly configuration-ordered, with one selected layer per generation.
    /// The positive-radius capsule has an upright Forward axis of <paramref name="axisLength"/>;
    /// total height is axis length plus twice the radius. World Y selects layers, not gameplay height.
    /// Closed endpoint contact admits identity; only strict swept interior overlap claims other cells.
    /// Existing scratch/output capacities are hard ceilings. Candidate work counts admitted generation
    /// and address inspections, not every geometry operation. Per selected grid, candidate range math
    /// uses at most four 31-step forward-lattice bounds. With N retained candidates, union closure
    /// visits at most eight planar neighbors per member; sorting and lookup are O(N log N), with
    /// duplicate-alternative scans bounded by O(N squared). No world-registry scan or storage growth occurs.
    /// The world read lock precedes the committed-change lock for the whole bounded proof.
    /// </remarks>
    /// <param name="world">The explicit world owning every selected generation.</param>
    /// <param name="eligibleLayers">Exact generations and one selected layer each, in strict configuration order.</param>
    /// <param name="embeddingY">World Y contained by every selected layer.</param>
    /// <param name="source">Pinned source identity in an eligible generation/layer.</param>
    /// <param name="target">Pinned target identity on the same footprint or an immediate planar neighbor.</param>
    /// <param name="startCenter">Capsule center at the start of the planar sweep.</param>
    /// <param name="endCenter">Capsule center at the end of the planar sweep.</param>
    /// <param name="axisLength">Nonnegative upright core segment length; zero represents a circle.</param>
    /// <param name="radius">Strictly positive capsule radius.</param>
    /// <param name="results">Pre-sized caller-owned evidence storage, cleared before writing.</param>
    /// <param name="scratch">Pre-sized caller-owned temporary storage, cleared on return.</param>
    /// <param name="gridCandidateLimit">Maximum charged generation inspections.</param>
    /// <param name="addressCandidateLimit">Maximum charged address inspections.</param>
    /// <param name="outputLimit">Maximum complete required and dependency-evidence cells.</param>
    /// <param name="candidateWorkLimit">Maximum combined generation and address inspections.</param>
    /// <returns>The completion status, exact candidate counts, and coherent evidence stamp.</returns>
    public static GridNavigationBodyTraceReport TracePlanarNavigationBodyInto(
        GridWorld world, ReadOnlySpan<GridPlanarLayer> eligibleLayers, Fixed64 embeddingY,
        WorldVoxelIndex source, WorldVoxelIndex target, Vector2d startCenter, Vector2d endCenter,
        Fixed64 axisLength, Fixed64 radius, SwiftList<GridNavigationBodyTraceCell> results,
        GridNavigationBodyTraceScratch scratch, int gridCandidateLimit, int addressCandidateLimit,
        int outputLimit, long candidateWorkLimit)
    {
        SwiftThrowHelper.ThrowIfNull(results, nameof(results));
        SwiftThrowHelper.ThrowIfNull(scratch, nameof(scratch));
        SwiftThrowHelper.ThrowIfNegative(gridCandidateLimit, nameof(gridCandidateLimit));
        SwiftThrowHelper.ThrowIfNegative(addressCandidateLimit, nameof(addressCandidateLimit));
        SwiftThrowHelper.ThrowIfNegative(outputLimit, nameof(outputLimit));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(candidateWorkLimit < 0L,
            actualValue: null, paramName: nameof(candidateWorkLimit));
        results.Clear();
        scratch.Clear();
        GridNavigationBodyTraceStatus invalid = GridNavigationBodyTraceStatus.InvalidOrUnrepresentableGeometry;
        if (world == null || !world.IsActive || axisLength < Fixed64.Zero || radius <= Fixed64.Zero)
            return CreateNavigationBodyTraceReport(invalid, 0, 0, results, default);
        if (!TryCreatePlanarBodyBounds(startCenter, endCenter, axisLength, radius, out Vector2d queryMin, out Vector2d queryMax))
            return CreateNavigationBodyTraceReport(GridNavigationBodyTraceStatus.ArithmeticOverflow, 0, 0, results, default);

        int gridCount = 0;
        int addressCount = 0;
        int gridCeiling = Math.Min(gridCandidateLimit, scratch.CandidateGrids.Capacity);
        world.EnterReadLock();
        try
        {
            // Match sparse edits: world lifetime lock before the publication lock. No grid
            // obstacle/occupant locks or host callbacks occur inside this bounded proof.
            lock (world.ChangeSyncRoot)
            {
                VoxelGrid? sourceGrid = null;
                VoxelGrid? targetGrid = null;
                for (int i = 0; i < eligibleLayers.Length; i++)
                {
                    if (gridCount >= gridCeiling || gridCount >= candidateWorkLimit)
                        return FailNavigationBodyTrace(results, candidateWorkLimit < gridCeiling
                            ? GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded : GridNavigationBodyTraceStatus.GridCandidateLimitExceeded,
                            gridCount, addressCount, default);
                    gridCount++;
                    GridPlanarLayer selected = eligibleLayers[i];
                    GridCoveredAddressGeneration generation = selected.Generation;
                    if ((i > 0 && eligibleLayers[i - 1].Generation.CompareTo(generation) >= 0)
                        || !world.TryGetGrid(generation.GridIndex, out VoxelGrid? grid)
                        || grid!.SpawnToken != generation.GridSpawnToken
                        || grid.LastChangeSequence != generation.GridLastChangeSequence
                        || grid.Configuration.ToGridKey() != generation.ConfigurationKey
                        || selected.LayerIndex >= grid.Height
                        || !GridCellGeometry.TryCreatePrism(grid.Configuration.TopologyKind, grid.Configuration.TopologyMetrics,
                            grid.GetWorldPosition(new VoxelIndex(0, selected.LayerIndex, 0)), default, out GridCellPrism layerPrism)
                        || embeddingY < layerPrism.VerticalMin || embeddingY > layerPrism.VerticalMax)
                        return FailNavigationBodyTrace(results, invalid, gridCount, addressCount, default);

                    scratch.CandidateGrids.Add(grid.GridIndex);
                    if (MatchesPlanarEndpoint(world, grid, selected.LayerIndex, source))
                        sourceGrid = grid;
                    if (MatchesPlanarEndpoint(world, grid, selected.LayerIndex, target))
                        targetGrid = grid;
                }
                if (sourceGrid == null || targetGrid == null
                    || !TryCreatePlanarProofPrism(sourceGrid, sourceGrid.GetWorldPosition(source.VoxelIndex), out GridCellPrism sourcePrism)
                    || !TryCreatePlanarProofPrism(targetGrid, targetGrid.GetWorldPosition(target.VoxelIndex), out GridCellPrism targetPrism)
                    || !HasClosedPlanarBodyContact(sourcePrism, startCenter, axisLength, radius)
                    || !HasClosedPlanarBodyContact(targetPrism, endCenter, axisLength, radius))
                    return FailNavigationBodyTrace(results, invalid, gridCount, addressCount, default);

                Span<GridCellPrism> closure = stackalloc GridCellPrism[4];
                int closureCount = GetPlanarNavigationClosure(sourceGrid, sourcePrism, targetPrism, closure);
                if (closureCount == 0)
                    return FailNavigationBodyTrace(results, invalid, gridCount, addressCount, default);

                for (int i = 0; i < scratch.CandidateGrids.Count; i++)
                {
                    VoxelGrid grid = world.ActiveGrids[scratch.CandidateGrids[i]];
                    if (!TryExpandPlanarCandidateBounds(grid, queryMin, queryMax, out Vector2d candidateMin, out Vector2d candidateMax))
                        return FailNavigationBodyTrace(results, GridNavigationBodyTraceStatus.ArithmeticOverflow, gridCount, addressCount, default);
                    if (!TryGetPlanarCandidateRange(grid, candidateMin, candidateMax, out VoxelIndex minimum, out VoxelIndex maximum))
                        continue;
                    int layer = eligibleLayers[i].LayerIndex;
                    for (int x = minimum.x; x <= maximum.x; x++)
                    {
                        for (int z = minimum.z; z <= maximum.z; z++)
                        {
                            long remaining = candidateWorkLimit - gridCount;
                            if (addressCount >= addressCandidateLimit || addressCount >= remaining)
                                return FailNavigationBodyTrace(results, remaining < addressCandidateLimit
                                    ? GridNavigationBodyTraceStatus.CandidateWorkLimitExceeded : GridNavigationBodyTraceStatus.AddressLimitExceeded,
                                    gridCount, addressCount, default);
                            addressCount++;
                            VoxelIndex index = new VoxelIndex(x, layer, z);
                            if (!TryCreatePlanarProofPrism(grid, grid.GetWorldPosition(index), out GridCellPrism prism))
                                return FailNavigationBodyTrace(results, invalid, gridCount, addressCount, default);
                            bool overlaps = HasStrictPlanarBodyOverlap(prism, startCenter, endCenter, axisLength, radius);
                            bool isClosure = IsNavigationClosurePrism(prism, closure, closureCount);
                            if (!overlaps && !isClosure)
                                continue;
                            if (scratch.AddressCandidates.Count == scratch.AddressCandidates.Capacity)
                                return FailNavigationBodyTrace(results, GridNavigationBodyTraceStatus.AddressLimitExceeded, gridCount, addressCount, default);
                            scratch.AddressCandidates.Add(new GridNavigationBodyTraceCandidate(grid, index, prism, overlaps, isClosure));
                        }
                    }
                }

                GridCoveredAddressRunStamp stamp = SnapshotNavigationBodyCandidates(world, scratch.AddressCandidates);
                scratch.AddressCandidates.SortInPlace(default(GridNavigationBodyTraceCandidateComparer));
                GridNavigationBodyTraceStatus unionStatus = ClosePlanarBodyUnion(sourceGrid, source.VoxelIndex, targetGrid, target.VoxelIndex,
                    startCenter, endCenter, axisLength, radius, scratch);
                if (unionStatus != GridNavigationBodyTraceStatus.Complete)
                    return FailNavigationBodyTrace(results, unionStatus, gridCount, addressCount, default);
                int alternatives = CountMissingNavigationBodyAlternativeEvidence(scratch.AddressCandidates, sourceGrid, source.VoxelIndex, targetGrid, target.VoxelIndex);
                int outputCeiling = Math.Min(outputLimit, results.Capacity);
                if (scratch.UnionMembers.Count > outputCeiling || alternatives > outputCeiling - scratch.UnionMembers.Count)
                    return FailNavigationBodyTrace(results, GridNavigationBodyTraceStatus.OutputLimitExceeded, gridCount, addressCount, default);
                bool missing = false;
                for (int i = 0; i < scratch.UnionMembers.Count; i++)
                {
                    GridNavigationBodyTraceCandidate candidate = scratch.AddressCandidates[scratch.UnionMembers[i]];
                    missing |= !candidate.IsPhysicallyPresent;
                    results.Add(new GridNavigationBodyTraceCell(new WorldVoxelIndex(world.SpawnToken, candidate.Grid.GridIndex, candidate.Grid.SpawnToken, candidate.Index),
                        candidate.Grid.Configuration.ToGridKey(), candidate.IsPhysicallyPresent, candidate.GridLastChangeSequence, GridNavigationBodyTraceCellRole.RequiredCoverage));
                }
                if (missing)
                    AppendMissingNavigationBodyAlternativeEvidence(world, scratch.AddressCandidates, results, sourceGrid, source.VoxelIndex, targetGrid, target.VoxelIndex);
                results.SortInPlace(default(GridNavigationBodyTraceCellComparer));
                return CreateNavigationBodyTraceReport(missing ? GridNavigationBodyTraceStatus.IncompletePhysicalCoverage : GridNavigationBodyTraceStatus.Complete,
                    gridCount, addressCount, results, stamp);
            }
        }
        finally
        {
            scratch.Clear();
            world.ExitReadLock();
        }
    }
}
