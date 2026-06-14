//=======================================================================
// HexPrismTopology.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

internal sealed class HexPrismTopology : IGridTopology
{
    public GridTopologyKind Kind => GridTopologyKind.HexPrism;

    public GridTopologyMetrics Metrics { get; }

    public Fixed64 OverlapTolerance => MaxCellEdge * Fixed64.Half;

    public Fixed64 MaxCellEdge => Metrics.LargestHexEdge;

    public int NeighborSlotCount => HexDirectionUtility.Offsets.Length;

    public HexPrismTopology(GridTopologyMetrics metrics)
    {
        Metrics = GridTopologyMetrics.Normalize(GridTopologyKind.HexPrism, metrics);
    }

    public GridDimensions CalculateDimensions(Vector3d boundsMin, Vector3d boundsMax)
    {
        VoxelIndex maxIndex = GetMaxIndex(boundsMin, boundsMax);
        int height = ((boundsMax.Y - boundsMin.Y) / Metrics.LayerHeight).FloorToInt() + 1;
        return new GridDimensions(maxIndex.x + 1, height, maxIndex.z + 1);
    }

    public (Vector3d min, Vector3d max) NormalizeBounds(Vector3d min, Vector3d max, Fixed64? padding = null)
    {
        Fixed64 fixedPadding = padding.HasValue && padding.Value > Fixed64.Zero
            ? padding.Value
            : Fixed64.Zero;

        min -= fixedPadding;
        max += fixedPadding;

        Fixed64 yMin = FloorToLayerOrigin(min.Y);
        Fixed64 yMax = CeilToLayerOrigin(max.Y);
        Vector3d normalizedMin = new(min.X, yMin, min.Z);

        HexCoordinateUtility.WorldOffsetToAxial(
            max.X - normalizedMin.X,
            max.Z - normalizedMin.Z,
            Metrics,
            out Fixed64 qMax,
            out Fixed64 rMax);

        int maxQ = HexCoordinateUtility.CeilToIntWithTolerance(FixedMath.Max(qMax, Fixed64.Zero));
        int maxR = HexCoordinateUtility.CeilToIntWithTolerance(FixedMath.Max(rMax, Fixed64.Zero));
        int maxY = ((yMax - yMin) / Metrics.LayerHeight).FloorToInt();
        Vector3d normalizedMax = normalizedMin + HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(maxQ, maxY, maxR), Metrics);

        return (normalizedMin, normalizedMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsInBounds(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position) =>
        TryGetVoxelIndex(boundsMin, boundsMax, width, height, length, position, out _);

    public bool TryGetVoxelIndex(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position,
        out VoxelIndex result)
    {
        result = default;

        if (!IsInsideCoarseBounds(boundsMin, boundsMax, position))
            return false;

        HexCoordinateUtility.WorldOffsetToAxial(
            position.X - boundsMin.X,
            position.Z - boundsMin.Z,
            Metrics,
            out Fixed64 q,
            out Fixed64 r);

        Fixed64 layer = (position.Y - boundsMin.Y) / Metrics.LayerHeight;
        VoxelIndex rounded = HexCoordinateUtility.RoundAxial(q, layer, r);
        if ((uint)rounded.x >= (uint)width
            || (uint)rounded.y >= (uint)height
            || (uint)rounded.z >= (uint)length)
        {
            return false;
        }

        result = rounded;
        return true;
    }

    public VoxelIndex GetClosestVoxelIndex(
        Vector3d boundsMin,
        int width,
        int height,
        int length,
        Vector3d position)
    {
        HexCoordinateUtility.WorldOffsetToAxial(
            position.X - boundsMin.X,
            position.Z - boundsMin.Z,
            Metrics,
            out Fixed64 q,
            out Fixed64 r);
        HexCoordinateUtility.RoundCube(q, r, out int roundedQ, out int roundedR);

        int roundedY = ((position.Y - boundsMin.Y) / Metrics.LayerHeight).RoundToInt();
        return new VoxelIndex(
            FixedMath.Clamp(roundedQ, 0, width - 1),
            FixedMath.Clamp(roundedY, 0, height - 1),
            FixedMath.Clamp(roundedR, 0, length - 1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d GetWorldPosition(Vector3d boundsMin, VoxelIndex index) =>
        boundsMin + HexCoordinateUtility.AxialToWorldOffset(index, Metrics);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d GetWorldOffset((int x, int y, int z) offset) =>
        HexCoordinateUtility.AxialToWorldOffset(new VoxelIndex(offset.x, offset.y, offset.z), Metrics);

    public VoxelIndex GetNeighborOffset(int slot) => HexDirectionUtility.Offsets[slot];

    public bool TryGetNeighborSlotFromWorldDelta(Vector3d worldDelta, out int slot)
    {
        HexCoordinateUtility.WorldOffsetToAxial(
            worldDelta.X,
            worldDelta.Z,
            Metrics,
            out Fixed64 q,
            out Fixed64 r);
        HexCoordinateUtility.RoundCube(q, r, out int roundedQ, out int roundedR);

        int yDirection = worldDelta.Y.Sign();
        if (yDirection != 0)
        {
            if (roundedQ == 0 && roundedR == 0)
                return TryGetNeighborSlot(new VoxelIndex(0, yDirection, 0), out slot);

            VoxelIndex planarOffset = NormalizePlanarOffset(roundedQ, roundedR);
            if (planarOffset == default)
            {
                slot = -1;
                return false;
            }

            return TryGetNeighborSlot(new VoxelIndex(planarOffset.x, yDirection, planarOffset.z), out slot);
        }

        return TryGetNeighborSlot(NormalizePlanarOffset(roundedQ, roundedR), out slot);
    }

    public bool IsFacingBoundary(VoxelIndex voxelIndex, int slot, int width, int height, int length)
    {
        VoxelIndex offset = HexDirectionUtility.Offsets[slot];
        return RectangularDirectionUtility.IsAxisFacingBoundary(voxelIndex.x, offset.x, width)
            && RectangularDirectionUtility.IsAxisFacingBoundary(voxelIndex.y, offset.y, height)
            && RectangularDirectionUtility.IsAxisFacingBoundary(voxelIndex.z, offset.z, length);
    }

    public void GetBoundaryRange(
        int slot,
        int width,
        int height,
        int length,
        out int xStart,
        out int xEnd,
        out int yStart,
        out int yEnd,
        out int zStart,
        out int zEnd)
    {
        VoxelIndex offset = HexDirectionUtility.Offsets[slot];
        (xStart, xEnd) = RectangularDirectionUtility.GetBoundaryRange(offset.x, width);
        (yStart, yEnd) = RectangularDirectionUtility.GetBoundaryRange(offset.y, height);
        (zStart, zEnd) = RectangularDirectionUtility.GetBoundaryRange(offset.z, length);
    }

    public Vector3d FloorToGrid(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position)
    {
        VoxelIndex index = ClampToGrid(boundsMin, width, height, length, position);
        return GetWorldPosition(boundsMin, index);
    }

    public Vector3d CeilToGrid(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position)
    {
        VoxelIndex index = ClampToGrid(boundsMin, width, height, length, position);
        return GetWorldPosition(boundsMin, index);
    }

    public (int x, int y, int z) SnapToScanCell(Vector3d boundsMin, Vector3d position, int scanCellSize)
    {
        HexCoordinateUtility.WorldOffsetToAxial(
            position.X - boundsMin.X,
            position.Z - boundsMin.Z,
            Metrics,
            out Fixed64 q,
            out Fixed64 r);

        Fixed64 layer = (position.Y - boundsMin.Y) / Metrics.LayerHeight;
        VoxelIndex index = HexCoordinateUtility.RoundAxial(q, layer, r);
        return (index.x / scanCellSize, index.y / scanCellSize, index.z / scanCellSize);
    }

    private VoxelIndex ClampToGrid(
        Vector3d boundsMin,
        int width,
        int height,
        int length,
        Vector3d position)
    {
        HexCoordinateUtility.WorldOffsetToAxial(
            position.X - boundsMin.X,
            position.Z - boundsMin.Z,
            Metrics,
            out Fixed64 q,
            out Fixed64 r);

        Fixed64 layer = (position.Y - boundsMin.Y) / Metrics.LayerHeight;
        VoxelIndex rounded = HexCoordinateUtility.RoundAxial(q, layer, r);

        return new VoxelIndex(
            FixedMath.Clamp(rounded.x, 0, width - 1),
            FixedMath.Clamp(rounded.y, 0, height - 1),
            FixedMath.Clamp(rounded.z, 0, length - 1));
    }

    private VoxelIndex GetMaxIndex(Vector3d boundsMin, Vector3d boundsMax)
    {
        HexCoordinateUtility.WorldOffsetToAxial(
            boundsMax.X - boundsMin.X,
            boundsMax.Z - boundsMin.Z,
            Metrics,
            out Fixed64 qMax,
            out Fixed64 rMax);

        int maxQ = HexCoordinateUtility.CeilToIntWithTolerance(qMax);
        int maxR = HexCoordinateUtility.CeilToIntWithTolerance(rMax);

        return new VoxelIndex(
            maxQ < 0 ? 0 : maxQ,
            0,
            maxR < 0 ? 0 : maxR);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsInsideCoarseBounds(Vector3d boundsMin, Vector3d boundsMax, Vector3d position) =>
        boundsMin.X <= position.X && position.X <= boundsMax.X
        && boundsMin.Y <= position.Y && position.Y <= boundsMax.Y
        && boundsMin.Z <= position.Z && position.Z <= boundsMax.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 FloorToLayerOrigin(Fixed64 coordinate) =>
        (coordinate.Abs() / Metrics.LayerHeight).FloorToInt() * Metrics.LayerHeight * coordinate.Sign();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fixed64 CeilToLayerOrigin(Fixed64 coordinate) =>
        (coordinate.Abs() / Metrics.LayerHeight).CeilToInt() * Metrics.LayerHeight * coordinate.Sign();

    private static bool TryGetNeighborSlot(VoxelIndex neighborOffset, out int slot)
    {
        for (int i = 0; i < HexDirectionUtility.Offsets.Length; i++)
        {
            if (HexDirectionUtility.Offsets[i] == neighborOffset)
            {
                slot = i;
                return true;
            }
        }

        slot = -1;
        return false;
    }

    private static VoxelIndex NormalizePlanarOffset(int q, int r)
    {
        if (q > 0 && r == 0)
            return new VoxelIndex(1, 0, 0);
        if (q > 0 && r < 0)
            return new VoxelIndex(1, 0, -1);
        if (q == 0 && r < 0)
            return new VoxelIndex(0, 0, -1);
        if (q < 0 && r == 0)
            return new VoxelIndex(-1, 0, 0);
        if (q < 0 && r > 0)
            return new VoxelIndex(-1, 0, 1);
        if (q == 0 && r > 0)
            return new VoxelIndex(0, 0, 1);

        return default;
    }
}
