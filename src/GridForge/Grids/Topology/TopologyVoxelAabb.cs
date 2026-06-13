//=======================================================================
// TopologyVoxelAabb.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

internal readonly struct TopologyVoxelAabb
{
    public readonly Vector3d Min;

    public readonly Vector3d Max;

    public TopologyVoxelAabb(Vector3d min, Vector3d max)
    {
        Min = min;
        Max = max;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TopologyVoxelAabb FromVoxel(VoxelGrid grid, Voxel voxel) =>
        FromIndex(grid, voxel.Index);

    public static TopologyVoxelAabb FromIndex(VoxelGrid grid, VoxelIndex index)
    {
        Vector3d center = grid.GetWorldPosition(index);
        Vector3d halfExtents = GetHalfExtents(grid.Topology);
        return new TopologyVoxelAabb(center - halfExtents, center + halfExtents);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TopologyVoxelAabb Expand(Fixed64 tolerance)
    {
        if (tolerance <= Fixed64.Zero)
            return this;

        Vector3d expansion = new(tolerance, tolerance, tolerance);
        return new TopologyVoxelAabb(Min - expansion, Max + expansion);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Overlaps(TopologyVoxelAabb other, Fixed64 tolerance) =>
        AxisOverlaps(Min.X, Max.X, other.Min.X, other.Max.X, tolerance)
        && AxisOverlaps(Min.Y, Max.Y, other.Min.Y, other.Max.Y, tolerance)
        && AxisOverlaps(Min.Z, Max.Z, other.Min.Z, other.Max.Z, tolerance);

    private static Vector3d GetHalfExtents(IGridTopology topology)
    {
        GridTopologyMetrics metrics = topology.Metrics;
        Fixed64 halfY = metrics.LayerHeight * Fixed64.Half;

        if (topology.Kind == GridTopologyKind.HexPrism)
        {
            Fixed64 halfHexWidth = HexCoordinateUtility.Sqrt3 * metrics.CellRadius * Fixed64.Half;
            return metrics.HexOrientation == HexOrientation.FlatTop
                ? new Vector3d(metrics.CellRadius, halfY, halfHexWidth)
                : new Vector3d(halfHexWidth, halfY, metrics.CellRadius);
        }

        return new Vector3d(
            metrics.CellWidth * Fixed64.Half,
            halfY,
            metrics.CellLength * Fixed64.Half);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AxisOverlaps(
        Fixed64 firstMin,
        Fixed64 firstMax,
        Fixed64 secondMin,
        Fixed64 secondMax,
        Fixed64 tolerance)
    {
        Fixed64 resolvedTolerance = tolerance > Fixed64.Zero ? tolerance : Fixed64.Zero;
        return firstMax >= secondMin - resolvedTolerance
            && firstMin <= secondMax + resolvedTolerance;
    }
}
