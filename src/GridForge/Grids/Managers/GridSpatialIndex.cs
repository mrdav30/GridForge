//=======================================================================
// GridSpatialIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using SwiftCollections;
using SwiftCollections.Query;

namespace GridForge.Grids;

/// <summary>
/// Owns deterministic top-level grid spatial classification and lookup.
/// </summary>
internal sealed class GridSpatialIndex
{
    private const int InitialCapacity = 16;
    internal const ulong DefaultHashCellBudget = 64UL;

    private readonly ulong _cellBudget;
    private readonly SwiftFixedSpatialHash<ushort> _ordinaryGrids;
    private readonly SwiftFixedBVH<ushort> _oversizedGrids;
    private readonly SwiftHashSet<ushort> _oversizedSlots;
    private readonly SwiftFixedBVH<ushort> _contactEnvelopes;
    private readonly SwiftHashSet<ushort> _contactEnvelopeSlots;

    internal GridSpatialIndex(int cellSize)
        : this(cellSize, DefaultHashCellBudget)
    {
    }

    internal GridSpatialIndex(int cellSize, ulong cellBudget)
    {
        _cellBudget = cellBudget;
        _ordinaryGrids = new SwiftFixedSpatialHash<ushort>(InitialCapacity, (Fixed64)cellSize);
        _oversizedGrids = new SwiftFixedBVH<ushort>(InitialCapacity);
        _oversizedSlots = new SwiftHashSet<ushort>();
        _contactEnvelopes = new SwiftFixedBVH<ushort>(InitialCapacity);
        _contactEnvelopeSlots = new SwiftHashSet<ushort>();
    }

    internal int Count => OrdinaryCount + OversizedCount;

    internal int OrdinaryCount => _ordinaryGrids.Count;

    internal int OversizedCount => _oversizedGrids.Count;

    internal bool Insert(ushort gridIndex, FixedBoundVolume bounds) =>
        Insert(gridIndex, bounds, bounds);

    internal bool Insert(
        ushort gridIndex,
        FixedBoundVolume bounds,
        FixedBoundVolume? contactEnvelope)
    {
        if (_ordinaryGrids.Contains(gridIndex) || _oversizedSlots.Contains(gridIndex))
            return false;

        bool inserted;
        if (FitsHashCellBudget(bounds))
            inserted = _ordinaryGrids.Insert(gridIndex, bounds);
        else
        {
            _oversizedGrids.Insert(gridIndex, bounds);
            inserted = _oversizedSlots.Add(gridIndex);
        }

        if (inserted && contactEnvelope.HasValue)
        {
            _contactEnvelopes.Insert(gridIndex, contactEnvelope.Value);
            _contactEnvelopeSlots.Add(gridIndex);
        }

        return inserted;
    }

    internal bool Remove(ushort gridIndex)
    {
        bool removed;
        if (!_oversizedSlots.Contains(gridIndex))
            removed = _ordinaryGrids.Remove(gridIndex);
        else
        {
            _oversizedGrids.Remove(gridIndex);
            _oversizedSlots.Remove(gridIndex);
            removed = true;
        }

        if (removed && _contactEnvelopeSlots.Remove(gridIndex))
            _contactEnvelopes.Remove(gridIndex);

        return removed;
    }

    internal void Clear()
    {
        _ordinaryGrids.Clear();
        _oversizedGrids.Clear();
        _oversizedSlots.Clear();
        _contactEnvelopes.Clear();
        _contactEnvelopeSlots.Clear();
    }

    internal void CollectCandidates(
        FixedBoundVolume queryBounds,
        SwiftBucket<VoxelGrid> activeGrids,
        SwiftList<ushort> candidates)
    {
        candidates.Clear();
        if (activeGrids.Count == 0)
            return;

        if (ShouldScanActiveGrids(queryBounds, activeGrids.Count))
        {
            foreach (VoxelGrid grid in activeGrids)
            {
                var gridBounds = new FixedBoundVolume(grid.BoundsMin, grid.BoundsMax);
                if (gridBounds.Intersects(queryBounds))
                    candidates.Add(grid.GridIndex);
            }
        }
        else
        {
            if (_ordinaryGrids.Count > 0)
                _ordinaryGrids.Query(queryBounds, candidates);

            if (_oversizedGrids.Count > 0)
                _oversizedGrids.Query(queryBounds, candidates);
        }

        if (candidates.Count > 1)
            candidates.SortInPlace();
    }

    internal void CollectContactCandidates(
        FixedBoundVolume queryBounds,
        SwiftList<ushort> candidates)
    {
        candidates.Clear();
        if (_contactEnvelopes.Count > 0)
            _contactEnvelopes.Query(queryBounds, candidates);

        if (candidates.Count > 1)
            candidates.SortInPlace();
    }

    internal void CollectPointCandidates(
        Vector3d point,
        SwiftList<ushort> candidates)
    {
        candidates.Clear();
        if (_ordinaryGrids.Count > 0)
            _ordinaryGrids.CollectPointCandidates(point, candidates);

        if (_oversizedGrids.Count > 0)
            _oversizedGrids.Query(new FixedBoundVolume(point, point), candidates);

        if (candidates.Count > 1)
            candidates.SortInPlace();
    }

    internal bool FitsHashCellBudget(FixedBoundVolume bounds)
    {
        if (_cellBudget == 0UL)
            return false;

        GetCellRange(bounds, out SwiftSpatialHashCellIndex minCell, out SwiftSpatialHashCellIndex maxCell);
        ulong xCount = GetCellCount(minCell.X, maxCell.X);
        if (xCount > _cellBudget)
            return false;

        ulong yCount = GetCellCount(minCell.Y, maxCell.Y);
        if (yCount > _cellBudget / xCount)
            return false;

        ulong xyCount = xCount * yCount;
        ulong zCount = GetCellCount(minCell.Z, maxCell.Z);
        return zCount <= _cellBudget / xyCount;
    }

    internal void GetCellRange(
        FixedBoundVolume bounds,
        out SwiftSpatialHashCellIndex minCell,
        out SwiftSpatialHashCellIndex maxCell)
    {
        minCell = _ordinaryGrids.GetCellIndex(bounds.Min);
        maxCell = _ordinaryGrids.GetCellIndex(bounds.Max);
    }

    internal bool ShouldScanActiveGrids(
        FixedBoundVolume queryBounds,
        int activeGridCount)
    {
        GetCellRange(queryBounds, out SwiftSpatialHashCellIndex minCell, out SwiftSpatialHashCellIndex maxCell);
        ulong count = (ulong)activeGridCount;
        ulong xCount = GetCellCount(minCell.X, maxCell.X);
        if (xCount > count)
            return true;

        ulong yCount = GetCellCount(minCell.Y, maxCell.Y);
        if (yCount > count / xCount)
            return true;

        ulong xyCount = xCount * yCount;
        ulong zCount = GetCellCount(minCell.Z, maxCell.Z);
        return zCount > count / xyCount;
    }

    private static ulong GetCellCount(int minimum, int maximum) =>
        (ulong)((long)maximum - minimum + 1L);
}
