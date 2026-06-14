//=======================================================================
// VoxelNeighborResolver.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using SwiftCollections.Utility;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

internal static class VoxelNeighborResolver
{
    internal static void AddContactNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<Voxel> results,
        VoxelNeighborScope scope,
        Fixed64? tolerance = null) =>
        ResolveContactNeighbors(source, ownerGrid, results, stopAtFirst: false, scope, tolerance);

    internal static bool HasContactNeighbor(
        Voxel source,
        VoxelGrid ownerGrid,
        VoxelNeighborScope scope,
        Fixed64? tolerance = null) =>
        ResolveContactNeighbors(source, ownerGrid, results: null, stopAtFirst: true, scope, tolerance);

    internal static bool TryGetNeighbor(
        Voxel source,
        VoxelGrid ownerGrid,
        RectangularDirection direction,
        out Voxel? neighbor)
    {
        neighbor = null;
        return ownerGrid.TryGetNeighborSlot(direction, out int slot)
            && TryGetNeighborFromSlot(source, ownerGrid, slot, out neighbor);
    }

    internal static bool TryGetNeighbor(
        Voxel source,
        VoxelGrid ownerGrid,
        HexDirection direction,
        out Voxel? neighbor)
    {
        neighbor = null;
        return ownerGrid.TryGetNeighborSlot(direction, out int slot)
            && TryGetNeighborFromSlot(source, ownerGrid, slot, out neighbor);
    }

    internal static void AddRectangularNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<(RectangularDirection Direction, Voxel Voxel)> results)
    {
        if (ownerGrid.TopologyKind != GridTopologyKind.RectangularPrism)
            return;

        for (int slot = 0; slot < ownerGrid.NeighborSlotCount; slot++)
        {
            if (TryGetNeighborFromSlot(source, ownerGrid, slot, out Voxel? neighbor))
                results.Add(((RectangularDirection)slot, neighbor!));
        }
    }

    internal static void AddHexNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<(HexDirection Direction, Voxel Voxel)> results)
    {
        if (ownerGrid.TopologyKind != GridTopologyKind.HexPrism)
            return;

        for (int slot = 0; slot < ownerGrid.NeighborSlotCount; slot++)
        {
            if (TryGetNeighborFromSlot(source, ownerGrid, slot, out Voxel? neighbor))
                results.Add(((HexDirection)slot, neighbor!));
        }
    }

    private static bool ResolveContactNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<Voxel>? results,
        bool stopAtFirst,
        VoxelNeighborScope scope,
        Fixed64? tolerance)
    {
        if (scope == VoxelNeighborScope.None)
            return false;

        GridWorld? world = ownerGrid.World;
        if (world == null || !world.IsActive)
            return false;

        if ((scope & ~VoxelNeighborScope.SourceGrid) == VoxelNeighborScope.None)
            return ResolveSourceGridContactNeighbors(source, ownerGrid, results, stopAtFirst);

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
            if ((scope & VoxelNeighborScope.SourceGrid) != 0 && processedGridIds.Add(ownerGrid.GridIndex))
                candidateGridIds.Add(ownerGrid.GridIndex);

            candidateGridIds.Sort();

            for (int i = 0; i < candidateGridIds.Count; i++)
            {
                ushort candidateGridId = candidateGridIds[i];
                if (!world.ActiveGrids.IsAllocated(candidateGridId))
                    continue;

                VoxelGrid candidateGrid = world.ActiveGrids[candidateGridId];
                if (!IsCandidateInScope(ownerGrid, candidateGrid, scope))
                    continue;

                candidateVoxels.Clear();
                processedVoxels.Clear();
                if (candidateGridId == ownerGrid.GridIndex)
                {
                    AddSourceGridContactNeighbors(source, ownerGrid, candidateVoxels);
                }
                else if (TopologyVoxelRangeUtility.TryGetCandidateRange(
                             candidateGrid,
                             queryBounds,
                             out VoxelIndex minIndex,
                             out VoxelIndex maxIndex))
                {
                    candidateGrid.AddVoxelsInIndexRange(minIndex, maxIndex, candidateVoxels, processedVoxels);
                }
                else
                {
                    continue;
                }

                if (candidateVoxels.Count > 1)
                    candidateVoxels.Sort(VoxelIndexComparer.Instance);

                for (int voxelIndex = 0; voxelIndex < candidateVoxels.Count; voxelIndex++)
                {
                    Voxel candidateVoxel = candidateVoxels[voxelIndex];
                    if (ReferenceEquals(source, candidateVoxel))
                        continue;

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

    private static bool ResolveSourceGridContactNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<Voxel>? results,
        bool stopAtFirst)
    {
        if (stopAtFirst)
        {
            for (int slot = 0; slot < ownerGrid.NeighborSlotCount; slot++)
            {
                if (TryGetLocalNeighborFromSlot(source, ownerGrid, slot, out _))
                    return true;
            }

            return false;
        }

        AddSourceGridContactNeighbors(source, ownerGrid, results!);
        if (results!.Count > 1)
            results.Sort(VoxelIndexComparer.Instance);

        return results.Count > 0;
    }

    private static void AddSourceGridContactNeighbors(
        Voxel source,
        VoxelGrid ownerGrid,
        SwiftList<Voxel> results)
    {
        for (int slot = 0; slot < ownerGrid.NeighborSlotCount; slot++)
        {
            if (TryGetLocalNeighborFromSlot(source, ownerGrid, slot, out Voxel? neighbor))
                results.Add(neighbor!);
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

    private static bool TryGetNeighborFromSlot(
        Voxel source,
        VoxelGrid ownerGrid,
        int slot,
        out Voxel? neighbor)
    {
        neighbor = null;
        if ((uint)slot >= (uint)ownerGrid.NeighborSlotCount)
            return false;

        return TryResolveNeighborAtOffset(source, ownerGrid, ownerGrid.GetNeighborOffset(slot), out neighbor);
    }

    private static bool TryGetLocalNeighborFromSlot(
        Voxel source,
        VoxelGrid ownerGrid,
        int slot,
        out Voxel? neighbor)
    {
        neighbor = null;
        if ((uint)slot >= (uint)ownerGrid.NeighborSlotCount)
            return false;

        VoxelIndex offset = ownerGrid.GetNeighborOffset(slot);
        VoxelIndex neighborCoords = new(
            source.Index.x + offset.x,
            source.Index.y + offset.y,
            source.Index.z + offset.z);

        return ownerGrid.TryGetVoxel(neighborCoords, out neighbor);
    }

    private static bool TryResolveNeighborAtOffset(
        Voxel source,
        VoxelGrid ownerGrid,
        VoxelIndex offset,
        out Voxel? neighbor)
    {
        neighbor = null;
        VoxelIndex neighborCoords = new(
            source.Index.x + offset.x,
            source.Index.y + offset.y,
            source.Index.z + offset.z);

        if (ownerGrid.TryGetVoxel(neighborCoords, out neighbor))
            return true;

        GridWorld? world = ownerGrid.World;
        if (world == null)
            return false;

        Vector3d neighborPosition = source.WorldPosition + ownerGrid.GetWorldOffset((offset.x, offset.y, offset.z));
        if (!world.TryGetVoxel(neighborPosition, out neighbor) || neighbor == null)
            return false;

        if (!world.ActiveGrids.IsAllocated(neighbor.WorldIndex.GridIndex))
        {
            neighbor = null;
            return false;
        }

        VoxelGrid neighborGrid = world.ActiveGrids[neighbor.WorldIndex.GridIndex];
        if (neighborGrid.TopologyKind == ownerGrid.TopologyKind)
            return true;

        neighbor = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCandidateInScope(
        VoxelGrid ownerGrid,
        VoxelGrid candidateGrid,
        VoxelNeighborScope scope)
    {
        if (!candidateGrid.IsActive)
            return false;

        if (candidateGrid.GridIndex == ownerGrid.GridIndex)
            return (scope & VoxelNeighborScope.SourceGrid) != 0;

        return candidateGrid.TopologyKind == ownerGrid.TopologyKind
            ? (scope & VoxelNeighborScope.SameTopologyGrids) != 0
            : (scope & VoxelNeighborScope.MixedTopologyGrids) != 0;
    }

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
