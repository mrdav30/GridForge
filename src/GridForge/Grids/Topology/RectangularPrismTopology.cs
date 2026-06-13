//=======================================================================
// RectangularPrismTopology.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using System.Runtime.CompilerServices;

namespace GridForge.Grids.Topology;

internal sealed class RectangularPrismTopology : IGridTopology
{
    public GridTopologyKind Kind => GridTopologyKind.RectangularPrism;

    public GridTopologyMetrics Metrics { get; }

    public Fixed64 OverlapTolerance => Metrics.SmallestRectangularEdge * Fixed64.Half;

    public Fixed64 MaxCellEdge => Metrics.LargestRectangularEdge;

    public int NeighborSlotCount => RectangularDirectionUtility.Offsets.Length;

    public RectangularPrismTopology(GridTopologyMetrics metrics)
    {
        Metrics = GridTopologyMetrics.Normalize(GridTopologyKind.RectangularPrism, metrics);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public GridDimensions CalculateDimensions(Vector3d boundsMin, Vector3d boundsMax) =>
        new(((boundsMax.X - boundsMin.X) / Metrics.CellWidth).FloorToInt() + 1,
            ((boundsMax.Y - boundsMin.Y) / Metrics.LayerHeight).FloorToInt() + 1,
            ((boundsMax.Z - boundsMin.Z) / Metrics.CellLength).FloorToInt() + 1);

    public (Vector3d min, Vector3d max) NormalizeBounds(Vector3d min, Vector3d max, Fixed64? padding = null)
    {
        Fixed64 fixedPadding = padding.HasValue && padding.Value > Fixed64.Zero
            ? padding.Value
            : Fixed64.Zero;

        min -= fixedPadding;
        max += fixedPadding;

        Vector3d snapMin = FloorToCellOrigin(min);
        Vector3d snapMax = CeilToCellOrigin(max);

        (snapMin.X, snapMax.X) = snapMin.X > snapMax.X ? (snapMax.X, snapMin.X) : (snapMin.X, snapMax.X);
        (snapMin.Y, snapMax.Y) = snapMin.Y > snapMax.Y ? (snapMax.Y, snapMin.Y) : (snapMin.Y, snapMax.Y);
        (snapMin.Z, snapMax.Z) = snapMin.Z > snapMax.Z ? (snapMax.Z, snapMin.Z) : (snapMin.Z, snapMax.Z);

        return (snapMin, snapMax);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsInBounds(Vector3d boundsMin, Vector3d boundsMax, Vector3d position) =>
        boundsMin.X <= position.X && position.X <= boundsMax.X
        && boundsMin.Y <= position.Y && position.Y <= boundsMax.Y
        && boundsMin.Z <= position.Z && position.Z <= boundsMax.Z;

    public bool TryGetVoxelIndex(Vector3d boundsMin, Vector3d boundsMax, Vector3d position, out VoxelIndex result)
    {
        result = default;
        if (!IsInBounds(boundsMin, boundsMax, position))
            return false;

        result = new VoxelIndex(
            ((position.X - boundsMin.X) / Metrics.CellWidth).FloorToInt(),
            ((position.Y - boundsMin.Y) / Metrics.LayerHeight).FloorToInt(),
            ((position.Z - boundsMin.Z) / Metrics.CellLength).FloorToInt());
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d GetWorldPosition(Vector3d boundsMin, VoxelIndex index) =>
         new(boundsMin.X + index.x * Metrics.CellWidth,
            boundsMin.Y + index.y * Metrics.LayerHeight,
            boundsMin.Z + index.z * Metrics.CellLength);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector3d GetWorldOffset((int x, int y, int z) offset) =>
         new(offset.x * Metrics.CellWidth,
            offset.y * Metrics.LayerHeight,
            offset.z * Metrics.CellLength);

    public VoxelIndex GetNeighborOffset(int slot)
    {
        (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[slot];
        return new VoxelIndex(offset.x, offset.y, offset.z);
    }

    public bool TryGetNeighborSlotFromWorldDelta(Vector3d worldDelta, out int slot)
    {
        RectangularDirection direction = RectangularDirectionUtility.GetDirectionFromOffset((
            worldDelta.X.Sign(),
            worldDelta.Y.Sign(),
            worldDelta.Z.Sign()));
        slot = (int)direction;
        return direction != RectangularDirection.None;
    }

    public bool IsFacingBoundary(VoxelIndex voxelIndex, int slot, int width, int height, int length)
    {
        (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[slot];
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
        (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[slot];
        (xStart, xEnd) = RectangularDirectionUtility.GetBoundaryRange(offset.x, width);
        (yStart, yEnd) = RectangularDirectionUtility.GetBoundaryRange(offset.y, height);
        (zStart, zEnd) = RectangularDirectionUtility.GetBoundaryRange(offset.z, length);
    }

    public Vector3d FloorToGrid(Vector3d boundsMin, Vector3d boundsMax, Vector3d position)
    {
        return new Vector3d(
            FixedMath.Clamp(((position.X - boundsMin.X) / Metrics.CellWidth).FloorToInt() * Metrics.CellWidth + boundsMin.X, boundsMin.X, boundsMax.X),
            FixedMath.Clamp(((position.Y - boundsMin.Y) / Metrics.LayerHeight).FloorToInt() * Metrics.LayerHeight + boundsMin.Y, boundsMin.Y, boundsMax.Y),
            FixedMath.Clamp(((position.Z - boundsMin.Z) / Metrics.CellLength).FloorToInt() * Metrics.CellLength + boundsMin.Z, boundsMin.Z, boundsMax.Z));
    }

    public Vector3d CeilToGrid(Vector3d boundsMin, Vector3d boundsMax, Vector3d position)
    {
        return new Vector3d(
            FixedMath.Clamp(((position.X - boundsMin.X) / Metrics.CellWidth).CeilToInt() * Metrics.CellWidth + boundsMin.X, boundsMin.X, boundsMax.X),
            FixedMath.Clamp(((position.Y - boundsMin.Y) / Metrics.LayerHeight).CeilToInt() * Metrics.LayerHeight + boundsMin.Y, boundsMin.Y, boundsMax.Y),
            FixedMath.Clamp(((position.Z - boundsMin.Z) / Metrics.CellLength).CeilToInt() * Metrics.CellLength + boundsMin.Z, boundsMin.Z, boundsMax.Z));
    }

    public (int x, int y, int z) SnapToScanCell(Vector3d boundsMin, Vector3d position, int scanCellSize)
    {
        return (((position.X - boundsMin.X) / Metrics.CellWidth).FloorToInt() / scanCellSize,
            ((position.Y - boundsMin.Y) / Metrics.LayerHeight).FloorToInt() / scanCellSize,
            ((position.Z - boundsMin.Z) / Metrics.CellLength).FloorToInt() / scanCellSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3d FloorToCellOrigin(Vector3d position) =>
        new(FloorToCellOrigin(position.X, Metrics.CellWidth),
            FloorToCellOrigin(position.Y, Metrics.LayerHeight),
            FloorToCellOrigin(position.Z, Metrics.CellLength));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3d CeilToCellOrigin(Vector3d position) =>
        new(CeilToCellOrigin(position.X, Metrics.CellWidth),
            CeilToCellOrigin(position.Y, Metrics.LayerHeight),
            CeilToCellOrigin(position.Z, Metrics.CellLength));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 FloorToCellOrigin(Fixed64 coordinate, Fixed64 cellSize) =>
        (coordinate.Abs() / cellSize).FloorToInt() * cellSize * coordinate.Sign();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Fixed64 CeilToCellOrigin(Fixed64 coordinate, Fixed64 cellSize) =>
        (coordinate.Abs() / cellSize).CeilToInt() * cellSize * coordinate.Sign();

}
