//=======================================================================
// GridTopologyMetricUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

/// <summary>
/// Provides deterministic cell-edge measurements for grid topology-aware traversal and padding.
/// </summary>
public static class GridTopologyMetricUtility
{
    /// <summary>
    /// Gets the largest 3D cell edge for the supplied grid.
    /// </summary>
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
    public static Fixed64 GetRepresentativeCellEdge(GridWorld world)
    {
        foreach (VoxelGrid grid in world.ActiveGrids)
            if (grid.IsActive)
                return GetMaxCellEdge(grid);

        return GridWorld.DefaultRectangularCellSize;
    }
}
