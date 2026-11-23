//=======================================================================
// GridTopologyMetricUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;
using FixedMathSharp;

namespace GridForge.Grids.Topology;

/// <summary>
/// Provides deterministic cell-edge measurements for grid topology-aware traversal and padding.
/// </summary>
public static class GridTopologyMetricUtility
{
    /// <summary>
    /// Gets the largest 3D cell edge for the supplied grid.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <returns>The largest topology cell edge in world units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 GetMaxCellEdge(VoxelGrid grid)
    {
        GridTopologyMetrics metrics = grid.Configuration.TopologyMetrics;
        if (grid.Configuration.TopologyKind == GridTopologyKind.HexPrism)
            return FixedMath.Max(metrics.CellRadius * (Fixed64)2, metrics.LayerHeight);

        Fixed64 max = FixedMath.Max(metrics.CellWidth, metrics.LayerHeight);
        return FixedMath.Max(max, metrics.CellLength);
    }

    /// <summary>
    /// Gets the largest X/Z-plane cell edge for the supplied grid.
    /// </summary>
    /// <param name="grid">The grid to query or mutate. Voxel references supplied with it must belong to its current generation.</param>
    /// <returns>The largest horizontal topology extent in world units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Fixed64 GetPlanarMaxCellEdge(VoxelGrid grid)
    {
        GridTopologyMetrics metrics = grid.Configuration.TopologyMetrics;
        return grid.Configuration.TopologyKind == GridTopologyKind.HexPrism
            ? metrics.CellRadius * (Fixed64)2
            : FixedMath.Max(metrics.CellWidth, metrics.CellLength);
    }

    /// <summary>
    /// Gets a representative cell edge for a world, or the default rectangular cell size when no grid is active.
    /// </summary>
    /// <param name="world">The owning world. World-space queries use only grids registered in this world.</param>
    /// <returns>The maximum cell edge of the first active grid encountered, or one world unit when no grid is active.</returns>
    public static Fixed64 GetRepresentativeCellEdge(GridWorld world)
    {
        foreach (VoxelGrid grid in world.ActiveGrids)
            if (grid.IsActive)
                return GetMaxCellEdge(grid);

        return GridWorld.DefaultRectangularCellSize;
    }
}
