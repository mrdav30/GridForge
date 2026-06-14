//=======================================================================
// VoxelNeighborResolver.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

internal static class VoxelNeighborResolver
{
    [ThreadStatic]
    private static NeighborResolverScratch? _contactScratch;

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

        if (IsSourceGridOnly(scope))
            return ResolveSourceGridContactNeighbors(source, ownerGrid, results, stopAtFirst);

        Fixed64 toleranceValue = tolerance.HasValue && tolerance.Value > Fixed64.Zero
            ? tolerance.Value
            : Fixed64.Zero;
        TopologyVoxelAabb sourceBounds = TopologyVoxelAabb.FromVoxel(ownerGrid, source);
        TopologyVoxelAabb queryBounds = sourceBounds.Expand(toleranceValue);
        NeighborResolverScratch scratch = RentContactScratch();
        GridWorld world = ownerGrid.World!;
        SwiftList<ushort> candidateGridIds = scratch.CandidateGridIds;
        SwiftHashSet<ushort> processedGridIds = scratch.ProcessedGridIds;
        SwiftList<Voxel> voxelCandidates = scratch.CandidateVoxels;
        SwiftHashSet<Voxel> processedVoxels = scratch.ProcessedVoxels;

        try
        {
            PopulateCandidateGridIds(world, ownerGrid, queryBounds, scope, candidateGridIds, processedGridIds);

            for (int i = 0; i < candidateGridIds.Count; i++)
            {
                if (!TryGetCandidateGrid(world, ownerGrid, candidateGridIds[i], scope, out VoxelGrid candidateGrid))
                    continue;

                if (!TryCollectCandidateVoxels(
                    source,
                    ownerGrid,
                    candidateGrid,
                    queryBounds,
                    voxelCandidates,
                    processedVoxels))
                {
                    continue;
                }

                if (AddOverlappingCandidateVoxels(
                    source,
                    candidateGrid,
                    sourceBounds,
                    toleranceValue,
                    voxelCandidates,
                    results,
                    stopAtFirst))
                {
                    return true;
                }
            }

            return results != null && results.Count > 0;
        }
        finally
        {
            ReleaseContactScratch(scratch);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSourceGridOnly(VoxelNeighborScope scope) =>
        (scope & ~VoxelNeighborScope.SourceGrid) == VoxelNeighborScope.None;

    private static void PopulateCandidateGridIds(
        GridWorld world,
        VoxelGrid ownerGrid,
        TopologyVoxelAabb queryBounds,
        VoxelNeighborScope scope,
        SwiftList<ushort> candidateGridIds,
        SwiftHashSet<ushort> processedGridIds)
    {
        CollectCandidateGridIds(world, queryBounds, candidateGridIds, processedGridIds);
        if (candidateGridIds.Count > 1)
            candidateGridIds.Sort();
    }

    private static bool TryGetCandidateGrid(
        GridWorld world,
        VoxelGrid ownerGrid,
        ushort candidateGridId,
        VoxelNeighborScope scope,
        out VoxelGrid candidateGrid)
    {
        candidateGrid = null!;
        if (!world.ActiveGrids.IsAllocated(candidateGridId))
            return false;

        VoxelGrid resolvedGrid = world.ActiveGrids[candidateGridId];
        if (!IsCandidateInScope(ownerGrid, resolvedGrid, scope))
            return false;

        candidateGrid = resolvedGrid;
        return true;
    }

    private static bool TryCollectCandidateVoxels(
        Voxel source,
        VoxelGrid ownerGrid,
        VoxelGrid candidateGrid,
        TopologyVoxelAabb queryBounds,
        SwiftList<Voxel> voxelCandidates,
        SwiftHashSet<Voxel> processedVoxels)
    {
        voxelCandidates.Clear();
        if (candidateGrid.GridIndex == ownerGrid.GridIndex)
        {
            AddSourceGridContactNeighbors(source, ownerGrid, voxelCandidates);
            return true;
        }

        if (!TopologyVoxelRangeUtility.TryGetCandidateRange(
            candidateGrid,
            queryBounds,
            out VoxelIndex minIndex,
            out VoxelIndex maxIndex))
        {
            return false;
        }

        processedVoxels.Clear();
        candidateGrid.AddVoxelsInIndexRange(minIndex, maxIndex, voxelCandidates, processedVoxels);
        SortByVoxelIndex(voxelCandidates);
        return true;
    }

    private static bool AddOverlappingCandidateVoxels(
        Voxel source,
        VoxelGrid candidateGrid,
        TopologyVoxelAabb sourceBounds,
        Fixed64 toleranceValue,
        SwiftList<Voxel> voxelCandidates,
        SwiftList<Voxel>? results,
        bool stopAtFirst)
    {
        for (int voxelIndex = 0; voxelIndex < voxelCandidates.Count; voxelIndex++)
        {
            Voxel candidateVoxel = voxelCandidates[voxelIndex];
            TopologyVoxelAabb candidateBounds = TopologyVoxelAabb.FromVoxel(candidateGrid, candidateVoxel);
            if (!sourceBounds.Overlaps(candidateBounds, toleranceValue))
                continue;

            if (stopAtFirst)
                return true;

            results!.Add(candidateVoxel);
        }

        return false;
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

        SwiftList<Voxel> resultList = results!;
        AddSourceGridContactNeighbors(source, ownerGrid, resultList);

        return resultList.Count > 0;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetNeighborFromSlot(
        Voxel source,
        VoxelGrid ownerGrid,
        int slot,
        out Voxel? neighbor)
    {
        return TryResolveNeighborAtOffset(source, ownerGrid, ownerGrid.GetNeighborOffset(slot), out neighbor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetLocalNeighborFromSlot(
        Voxel source,
        VoxelGrid ownerGrid,
        int slot,
        out Voxel? neighbor)
    {
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

        GridWorld world = ownerGrid.World!;
        Vector3d neighborPosition = source.WorldPosition + ownerGrid.GetWorldOffset((offset.x, offset.y, offset.z));
        if (!world.TryGetVoxel(neighborPosition, out neighbor) || neighbor == null)
            return false;

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
        if (candidateGrid.GridIndex == ownerGrid.GridIndex)
            return (scope & VoxelNeighborScope.SourceGrid) != 0;

        return candidateGrid.TopologyKind == ownerGrid.TopologyKind
            ? (scope & VoxelNeighborScope.SameTopologyGrids) != 0
            : (scope & VoxelNeighborScope.MixedTopologyGrids) != 0;
    }

    private static NeighborResolverScratch RentContactScratch()
    {
        NeighborResolverScratch scratch = _contactScratch ??= new NeighborResolverScratch();
        return scratch;
    }

    private static void ReleaseContactScratch(NeighborResolverScratch scratch)
    {
        scratch.Clear();
    }

    private static void SortByVoxelIndex(SwiftList<Voxel> voxels)
    {
        int count = voxels.Count;
        if (count <= 1)
            return;

        Array.Sort(voxels.InnerArray, 0, count, VoxelIndexComparer.Instance);
    }

    private sealed class NeighborResolverScratch
    {
        public readonly SwiftList<ushort> CandidateGridIds = new();
        public readonly SwiftHashSet<ushort> ProcessedGridIds = new();
        public readonly SwiftList<Voxel> CandidateVoxels = new();
        public readonly SwiftHashSet<Voxel> ProcessedVoxels = new();

        public void Clear()
        {
            CandidateGridIds.Clear();
            ProcessedGridIds.Clear();
            CandidateVoxels.Clear();
            ProcessedVoxels.Clear();
        }
    }

    private sealed class VoxelIndexComparer : IComparer<Voxel>
    {
        public static readonly VoxelIndexComparer Instance = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(Voxel? left, Voxel? right) => left!.Index.CompareTo(right!.Index);
    }
}
