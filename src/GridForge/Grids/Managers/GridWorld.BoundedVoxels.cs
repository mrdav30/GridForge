//=======================================================================
// GridWorld.BoundedVoxels.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using FixedMathSharp;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Query;

namespace GridForge.Grids;

public sealed partial class GridWorld
{
    private static readonly IComparer<VoxelGrid> BoundedGridOrder = Comparer<VoxelGrid>.Create(static (left, right) =>
        GridCoveredAddressGeneration.CompareConfigurationKeys(left.Configuration.ToGridKey(), right.Configuration.ToGridKey()));
    private readonly SwiftSortedList<VoxelGrid> _boundedGrids = new(BoundedGridOrder);

    /// <summary>Collects covered physical voxels under a work and output limit.</summary>
    /// <remarks>
    /// Visits dense and sparse rectangular and hexagonal grids in normalized configuration order.
    /// One unit pays for inspecting each live grid, including fixed topology range setup, or each
    /// voxel address, including its storage-block lookup. Sparse block lookups reserve additional
    /// units for the maximum comparisons over the block's live voxels. A separate unit reserves
    /// clearing each output reference.
    /// Planar coverage also applies the X/Z center filter using each grid's planar cell edge.
    /// Set both Y coordinates to the selected layer for an X/Z plane.
    /// On any incomplete result, discard and clear the written prefix. Voxel references are
    /// borrowed within the caller's serialized world phase. Never use them across world mutation.
    /// </remarks>
    public GridBoundedVoxelStatus CollectCoveredVoxelsBounded(
        Vector3d boundsMin,
        Vector3d boundsMax,
        bool planar,
        ulong discoveryLimit,
        Span<Voxel?> results,
        out ulong discoveryWork,
        out int voxelCount,
        out ulong changeSequence)
    {
        discoveryWork = 0;
        voxelCount = 0;
        changeSequence = 0;
        if (!TryEnterBoundedVoxelSnapshot())
            return GridBoundedVoxelStatus.WorldNotReady;

        try
        {
            changeSequence = _changeSequence;
            return CollectBoundedVoxelSnapshot(
                boundsMin, boundsMax, planar, discoveryLimit, results, out discoveryWork, out voxelCount);
        }
        finally
        {
            ExitBoundedVoxelSnapshot();
        }
    }

    /// <summary>Checks a sequence returned by this world without waiting for publication.</summary>
    public GridBoundedVoxelStatus CheckBoundedVoxelSnapshot(ulong changeSequence)
    {
        if (!TryEnterBoundedVoxelSnapshot())
            return GridBoundedVoxelStatus.WorldNotReady;

        try
        {
            return _changeSequence == changeSequence
                ? GridBoundedVoxelStatus.Completed
                : GridBoundedVoxelStatus.Stale;
        }
        finally
        {
            ExitBoundedVoxelSnapshot();
        }
    }

    private GridBoundedVoxelStatus CollectBoundedVoxelSnapshot(
        Vector3d boundsMin, Vector3d boundsMax, bool planar,
        ulong discoveryLimit, Span<Voxel?> results, out ulong discoveryWork, out int voxelCount)
    {
        discoveryWork = 0;
        voxelCount = 0;
        Vector3d broadMargin = Vector3d.One * MaxTopologyCellEdge;
        Vector3d queryMin = Vector3d.Min(boundsMin, boundsMax);
        Vector3d queryMax = Vector3d.Max(boundsMin, boundsMax);
        if (!Vector3d.TrySubtract(queryMin, broadMargin, out Vector3d broadMin)
            || !Vector3d.TryAdd(queryMax, broadMargin, out Vector3d broadMax))
        {
            return GridBoundedVoxelStatus.Unrepresentable;
        }

        FixedBoundVolume broadBounds = new(broadMin, broadMax);
        for (int i = 0; i < _boundedGrids.Count; i++)
        {
            if (discoveryWork == discoveryLimit)
                return GridBoundedVoxelStatus.WorkBudgetExceeded;
            discoveryWork++;
            VoxelGrid grid = _boundedGrids[i];
            if (!broadBounds.Intersects(new FixedBoundVolume(grid.BoundsMin, grid.BoundsMax)))
                continue;

            if (!TopologyVoxelRangeUtility.TryGetCandidateRange(grid, queryMin, queryMax, out var minimum, out var maximum))
                continue;

            GridBoundedVoxelStatus status = AddBoundedGridVoxels(
                grid, minimum, maximum, planar, queryMin, queryMax,
                discoveryLimit, results, ref discoveryWork, ref voxelCount);
            if (status != GridBoundedVoxelStatus.Completed)
                return status;
        }

        return GridBoundedVoxelStatus.Completed;
    }

    private static GridBoundedVoxelStatus AddBoundedGridVoxels(
        VoxelGrid grid, VoxelIndex minimum, VoxelIndex maximum,
        bool planar, Vector3d queryMin, Vector3d queryMax,
        ulong discoveryLimit, Span<Voxel?> results, ref ulong discoveryWork, ref int voxelCount)
    {
        bool filterCenter = planar || grid.Topology.Kind == GridTopologyKind.HexPrism;
        Vector2d centerMin = default;
        Vector2d centerMax = default;
        if (filterCenter)
        {
            Fixed64 padding = planar ? GridTopologyMetricUtility.GetPlanarMaxCellEdge(grid) * Fixed64.Half
                : grid.Topology.Metrics.CellRadius;
            TopologyVoxelRangeUtility.GetHorizontalCoverageBounds(queryMin, queryMax, padding, out centerMin, out centerMax);
        }

        VoxelIndex address = minimum;
        do
        {
            if (discoveryWork == discoveryLimit)
                return GridBoundedVoxelStatus.WorkBudgetExceeded;
            discoveryWork++;
            if (!grid.TryInspectVoxel(address, discoveryLimit, ref discoveryWork, out Voxel? voxel))
                return GridBoundedVoxelStatus.WorkBudgetExceeded;

            if (voxel == null
                || (filterCenter && !TopologyVoxelRangeUtility.ContainsHorizontalCenter(centerMin, centerMax, voxel.WorldPosition)))
                continue;

            if (voxelCount == results.Length)
                return GridBoundedVoxelStatus.CapacityExceeded;
            // Reserve clearing the retained reference before publishing it to the caller.
            if (discoveryWork == discoveryLimit)
                return GridBoundedVoxelStatus.WorkBudgetExceeded;
            discoveryWork++;
            results[voxelCount++] = voxel;
        }
        while (AdvanceCoveredAddress(ref address, minimum, maximum));

        return GridBoundedVoxelStatus.Completed;
    }

    private bool TryEnterBoundedVoxelSnapshot()
    {
        if (!IsActive || _gridLock.IsReadLockHeld || _gridLock.IsWriteLockHeld
            || !_gridLock.TryEnterReadLock(0))
        {
            return false;
        }

        if (Monitor.TryEnter(ChangeSyncRoot))
        {
            if (IsActive && _publishedChangeSequence == _changeSequence)
                return true;

            Monitor.Exit(ChangeSyncRoot);
        }

        _gridLock.ExitReadLock();
        return false;
    }

    private void ExitBoundedVoxelSnapshot()
    {
        Monitor.Exit(ChangeSyncRoot);
        _gridLock.ExitReadLock();
    }
}
