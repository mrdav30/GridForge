//=======================================================================
// TopologyVoxelRangeUtility.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Spatial;

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

    internal static bool TryGetPrismCandidateRange(
        VoxelGrid grid,
        Vector3d queryMin,
        Vector3d queryMax,
        out VoxelIndex minIndex,
        out VoxelIndex maxIndex)
    {
        GridTopologyMetrics metrics = grid.Topology.Metrics;
        Vector3d rangeMin;
        Vector3d rangeMax;
        if (grid.Topology.Kind == GridTopologyKind.RectangularPrism)
        {
            Vector3d halfExtents = new(
                metrics.CellWidth * Fixed64.Half,
                metrics.LayerHeight * Fixed64.Half,
                metrics.CellLength * Fixed64.Half);
            rangeMin = queryMin - halfExtents;
            rangeMax = queryMax + halfExtents;
        }
        else
        {
            Fixed64 halfHeight = metrics.LayerHeight * Fixed64.Half;
            rangeMin = new Vector3d(queryMin.X, queryMin.Y - halfHeight, queryMin.Z);
            rangeMax = new Vector3d(queryMax.X, queryMax.Y + halfHeight, queryMax.Z);
        }

        return TryGetCandidateRange(grid, rangeMin, rangeMax, out minIndex, out maxIndex);
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
        if (!TryClipBoundsToGrid(grid, snappedMin, snappedMax, out Vector3d clippedMin, out Vector3d clippedMax))
            return false;

        bool minResolved = grid.TryGetVoxelIndex(clippedMin, out minIndex);
        bool maxResolved = grid.TryGetVoxelIndex(clippedMax, out maxIndex);
        Debug.Assert(minResolved && maxResolved);
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
        Fixed64 layerHeight = metrics.LayerHeight;
        GetHorizontalCoverageBounds(queryMin, queryMax, metrics.CellRadius, out Vector2d horizontalMin, out Vector2d horizontalMax);
        Vector3d candidateMin = GridPlane2d.ToWorld(horizontalMin, queryMin.Y);
        Vector3d candidateMax = GridPlane2d.ToWorld(horizontalMax, queryMax.Y);

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

        // Axial values come from the quantized forward basis. Keep one address
        // of padding for rounding, widening before the integer addition.
        int xMin = (int)System.Math.Max(0L, (long)qMin.FloorToInt() - 1);
        int xMax = (int)System.Math.Min(grid.Width - 1L, (long)qMax.CeilToInt() + 1);
        int zMin = (int)System.Math.Max(0L, (long)rMin.FloorToInt() - 1);
        int zMax = (int)System.Math.Min(grid.Length - 1L, (long)rMax.CeilToInt() + 1);
        if (xMin > xMax || zMin > zMax)
            return false;

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void GetHorizontalCoverageBounds(
        Vector3d queryMin, Vector3d queryMax, Fixed64 padding,
        out Vector2d minimum, out Vector2d maximum)
    {
        minimum = new Vector2d(queryMin.X - padding, queryMin.Z - padding);
        maximum = new Vector2d(queryMax.X + padding, queryMax.Z + padding);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ContainsHorizontalCenter(Vector2d minimum, Vector2d maximum, Vector3d position) =>
        position.X >= minimum.X && position.X <= maximum.X
        && position.Z >= minimum.Y && position.Z <= maximum.Y;

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
