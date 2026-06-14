//=======================================================================
// TopologyVoxelRangeUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

internal static class TopologyVoxelRangeUtility
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryGetCandidateRange(
        VoxelGrid grid,
        TopologyVoxelAabb bounds,
        out VoxelIndex minIndex,
        out VoxelIndex maxIndex) =>
        TryGetCandidateRange(grid, bounds.Min, bounds.Max, out minIndex, out maxIndex);

    internal static bool TryGetCandidateRange(
        VoxelGrid grid,
        Vector3d queryMin,
        Vector3d queryMax,
        out VoxelIndex minIndex,
        out VoxelIndex maxIndex)
    {
        if (!grid.IsActive)
        {
            minIndex = default;
            maxIndex = default;
            return false;
        }

        return grid.Topology.Kind == GridTopologyKind.HexPrism
            ? TryGetHexCandidateRange(grid, queryMin, queryMax, out minIndex, out maxIndex)
            : TryGetRectangularCandidateRange(grid, queryMin, queryMax, out minIndex, out maxIndex);
    }

    private static bool TryGetRectangularCandidateRange(
        VoxelGrid grid,
        Vector3d queryMin,
        Vector3d queryMax,
        out VoxelIndex minIndex,
        out VoxelIndex maxIndex)
    {
        minIndex = default;
        maxIndex = default;

        (Vector3d snappedMin, Vector3d snappedMax) = grid.NormalizeBounds(queryMin, queryMax);
        if (!TryClipBoundsToGrid(grid, snappedMin, snappedMax, out Vector3d clippedMin, out Vector3d clippedMax)
            || !grid.TryGetVoxelIndex(clippedMin, out minIndex)
            || !grid.TryGetVoxelIndex(clippedMax, out maxIndex))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetHexCandidateRange(
        VoxelGrid grid,
        Vector3d queryMin,
        Vector3d queryMax,
        out VoxelIndex minIndex,
        out VoxelIndex maxIndex)
    {
        minIndex = default;
        maxIndex = default;

        GridTopologyMetrics metrics = grid.Topology.Metrics;
        Fixed64 horizontalExpansion = metrics.CellRadius;
        Fixed64 layerHeight = metrics.LayerHeight;
        Vector3d candidateMin = new(
            queryMin.X - horizontalExpansion,
            queryMin.Y,
            queryMin.Z - horizontalExpansion);
        Vector3d candidateMax = new(
            queryMax.X + horizontalExpansion,
            queryMax.Y,
            queryMax.Z + horizontalExpansion);

        if (!TryClipBoundsToGrid(grid, candidateMin, candidateMax, out Vector3d clippedMin, out Vector3d clippedMax))
            return false;

        Fixed64 qMin = Fixed64.MaxValue;
        Fixed64 qMax = Fixed64.MinValue;
        Fixed64 rMin = Fixed64.MaxValue;
        Fixed64 rMax = Fixed64.MinValue;

        IncludeHexAxialCorner(grid.BoundsMin, metrics, clippedMin.X, clippedMin.Z, ref qMin, ref qMax, ref rMin, ref rMax);
        IncludeHexAxialCorner(grid.BoundsMin, metrics, clippedMin.X, clippedMax.Z, ref qMin, ref qMax, ref rMin, ref rMax);
        IncludeHexAxialCorner(grid.BoundsMin, metrics, clippedMax.X, clippedMin.Z, ref qMin, ref qMax, ref rMin, ref rMax);
        IncludeHexAxialCorner(grid.BoundsMin, metrics, clippedMax.X, clippedMax.Z, ref qMin, ref qMax, ref rMin, ref rMax);

        int xMin = System.Math.Max(0, qMin.FloorToInt() - 1);
        int xMax = System.Math.Min(grid.Width - 1, qMax.CeilToInt() + 1);
        int zMin = System.Math.Max(0, rMin.FloorToInt() - 1);
        int zMax = System.Math.Min(grid.Length - 1, rMax.CeilToInt() + 1);
        int yMin = System.Math.Max(
            0,
            ((clippedMin.Y - grid.BoundsMin.Y) / layerHeight).FloorToInt());
        int yMax = System.Math.Min(
            grid.Height - 1,
            ((clippedMax.Y - grid.BoundsMin.Y) / layerHeight).CeilToInt());

        minIndex = new VoxelIndex(xMin, yMin, zMin);
        maxIndex = new VoxelIndex(xMax, yMax, zMax);
        return true;
    }

    internal static bool TryClipBoundsToGrid(
        VoxelGrid grid,
        Vector3d min,
        Vector3d max,
        out Vector3d clippedMin,
        out Vector3d clippedMax)
    {
        Fixed64 xMin = FixedMath.Max(min.X, grid.BoundsMin.X);
        Fixed64 yMin = FixedMath.Max(min.Y, grid.BoundsMin.Y);
        Fixed64 zMin = FixedMath.Max(min.Z, grid.BoundsMin.Z);
        Fixed64 xMax = FixedMath.Min(max.X, grid.BoundsMax.X);
        Fixed64 yMax = FixedMath.Min(max.Y, grid.BoundsMax.Y);
        Fixed64 zMax = FixedMath.Min(max.Z, grid.BoundsMax.Z);

        if (xMin > xMax || yMin > yMax || zMin > zMax)
        {
            clippedMin = default;
            clippedMax = default;
            return false;
        }

        clippedMin = new Vector3d(xMin, yMin, zMin);
        clippedMax = new Vector3d(xMax, yMax, zMax);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IncludeHexAxialCorner(
        Vector3d gridBoundsMin,
        GridTopologyMetrics metrics,
        Fixed64 x,
        Fixed64 z,
        ref Fixed64 qMin,
        ref Fixed64 qMax,
        ref Fixed64 rMin,
        ref Fixed64 rMax)
    {
        HexCoordinateUtility.WorldOffsetToAxial(
            x - gridBoundsMin.X,
            z - gridBoundsMin.Z,
            metrics,
            out Fixed64 q,
            out Fixed64 r);

        qMin = FixedMath.Min(qMin, q);
        qMax = FixedMath.Max(qMax, q);
        rMin = FixedMath.Min(rMin, r);
        rMax = FixedMath.Max(rMax, r);
    }
}
