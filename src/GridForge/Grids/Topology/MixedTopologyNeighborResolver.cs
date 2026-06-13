//=======================================================================
// MixedTopologyNeighborResolver.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Utility;
using System.Collections.Generic;

namespace GridForge.Grids.Topology;

internal static class MixedTopologyNeighborResolver
{
    internal static void AddNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<Voxel> results,
        Fixed64? tolerance = null) =>
        Resolve(source, ownerGrid, results, stopAtFirst: false, tolerance);

    internal static bool HasNeighbor(
        Voxel source,
        VoxelGrid ownerGrid,
        Fixed64? tolerance = null) =>
        Resolve(source, ownerGrid, results: null, stopAtFirst: true, tolerance);

    private static bool Resolve(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<Voxel>? results,
        bool stopAtFirst,
        Fixed64? tolerance)
    {
        GridWorld? world = ownerGrid.World;
        if (world == null || !world.IsActive)
            return false;

        Fixed64 toleranceValue = tolerance.HasValue && tolerance.Value > Fixed64.Zero
            ? tolerance.Value
            : Fixed64.Zero;
        TopologyVoxelAabb sourceBounds = TopologyVoxelAabb.FromVoxel(ownerGrid, source);
        TopologyVoxelAabb queryBounds = sourceBounds.Expand(toleranceValue);
        SwiftList<ushort> candidateGridIds = SwiftListPool<ushort>.Shared.Rent();
        SwiftHashSet<ushort> processedGridIds = SwiftHashSetPool<ushort>.Shared.Rent();
        SwiftList<Voxel> candidateVoxels = SwiftListPool<Voxel>.Shared.Rent();
        SwiftHashSet<Voxel> processedVoxels = SwiftHashSetPool<Voxel>.Shared.Rent();

        try
        {
            CollectCandidateGridIds(world, queryBounds, candidateGridIds, processedGridIds);
            candidateGridIds.Sort();

            for (int i = 0; i < candidateGridIds.Count; i++)
            {
                ushort candidateGridId = candidateGridIds[i];
                if (!world.ActiveGrids.IsAllocated(candidateGridId))
                    continue;

                VoxelGrid candidateGrid = world.ActiveGrids[candidateGridId];
                if (!IsMixedTopologyCandidate(ownerGrid, candidateGrid))
                    continue;

                candidateVoxels.Clear();
                processedVoxels.Clear();
                if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
                        candidateGrid,
                        queryBounds,
                        out VoxelIndex minIndex,
                        out VoxelIndex maxIndex))
                {
                    continue;
                }

                candidateGrid.AddVoxelsInIndexRange(minIndex, maxIndex, candidateVoxels, processedVoxels);
                if (candidateVoxels.Count > 1)
                    candidateVoxels.Sort(VoxelIndexComparer.Instance);

                for (int voxelIndex = 0; voxelIndex < candidateVoxels.Count; voxelIndex++)
                {
                    Voxel candidateVoxel = candidateVoxels[voxelIndex];
                    TopologyVoxelAabb candidateBounds = TopologyVoxelAabb.FromVoxel(candidateGrid, candidateVoxel);
                    if (!sourceBounds.Overlaps(candidateBounds, toleranceValue))
                        continue;

                    if (stopAtFirst)
                        return true;

                    results!.Add(candidateVoxel);
                }
            }

            return results != null && results.Count > 0;
        }
        finally
        {
            SwiftHashSetPool<Voxel>.Shared.Release(processedVoxels);
            SwiftListPool<Voxel>.Shared.Release(candidateVoxels);
            SwiftHashSetPool<ushort>.Shared.Release(processedGridIds);
            SwiftListPool<ushort>.Shared.Release(candidateGridIds);
        }
    }

    private static void CollectCandidateGridIds(
        GridWorld world,
        TopologyVoxelAabb queryBounds,
        SwiftList<ushort> candidateGridIds,
        SwiftHashSet<ushort> processedGridIds)
    {
        TopologyVoxelAabb spatialBounds = queryBounds.Expand(world.MaxTopologyCellEdge);
        (int cellXMin, int cellYMin, int cellZMin, int cellXMax, int cellYMax, int cellZMax) =
            world.GetSpatialGridCellBounds(spatialBounds.Min, spatialBounds.Max);

        for (int cellZ = cellZMin; cellZ <= cellZMax; cellZ++)
        {
            for (int cellY = cellYMin; cellY <= cellYMax; cellY++)
            {
                for (int cellX = cellXMin; cellX <= cellXMax; cellX++)
                {
                    int cellIndex = SwiftHashTools.CombineHashCodes(cellX, cellY, cellZ);
                    if (!world.SpatialGridHash.TryGetValue(cellIndex, out SwiftHashSet<ushort> gridIds))
                        continue;

                    foreach (ushort gridId in gridIds)
                    {
                        if (processedGridIds.Add(gridId))
                            candidateGridIds.Add(gridId);
                    }
                }
            }
        }
    }

    private static bool IsMixedTopologyCandidate(VoxelGrid ownerGrid, VoxelGrid candidateGrid) =>
        candidateGrid.IsActive
        && candidateGrid.GridIndex != ownerGrid.GridIndex
        && candidateGrid.TopologyKind != ownerGrid.TopologyKind;

    private sealed class VoxelIndexComparer : IComparer<Voxel>
    {
        public static readonly VoxelIndexComparer Instance = new();

        public int Compare(Voxel? left, Voxel? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left == null)
                return -1;
            if (right == null)
                return 1;

            VoxelIndex leftIndex = left.Index;
            VoxelIndex rightIndex = right.Index;
            int comparison = leftIndex.x.CompareTo(rightIndex.x);
            if (comparison != 0)
                return comparison;

            comparison = leftIndex.y.CompareTo(rightIndex.y);
            return comparison != 0
                ? comparison
                : leftIndex.z.CompareTo(rightIndex.z);
        }
    }
}
