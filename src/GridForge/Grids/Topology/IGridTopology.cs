//=======================================================================
// IGridTopology.cs
//=======================================================================
// MIT License, Copyright (c) 2024–present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;

namespace GridForge.Grids.Topology;

internal interface IGridTopology
{
    GridTopologyKind Kind { get; }

    GridTopologyMetrics Metrics { get; }

    Fixed64 OverlapTolerance { get; }

    Fixed64 MaxCellEdge { get; }

    int NeighborSlotCount { get; }

    GridDimensions CalculateDimensions(Vector3d boundsMin, Vector3d boundsMax);

    (Vector3d min, Vector3d max) NormalizeBounds(Vector3d min, Vector3d max, Fixed64? padding = null);

    bool IsInBounds(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position);

    bool TryGetVoxelIndex(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position,
        out VoxelIndex result);

    VoxelIndex GetClosestVoxelIndex(
        Vector3d boundsMin,
        int width,
        int height,
        int length,
        Vector3d position);

    Vector3d GetWorldPosition(Vector3d boundsMin, VoxelIndex index);

    Vector3d GetWorldOffset((int x, int y, int z) offset);

    VoxelIndex GetNeighborOffset(int slot);

    bool TryGetNeighborSlotFromWorldDelta(Vector3d worldDelta, out int slot);

    bool IsFacingBoundary(VoxelIndex voxelIndex, int slot, int width, int height, int length);

    void GetBoundaryRange(
        int slot,
        int width,
        int height,
        int length,
        out int xStart,
        out int xEnd,
        out int yStart,
        out int yEnd,
        out int zStart,
        out int zEnd);

    Vector3d FloorToGrid(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position);

    Vector3d CeilToGrid(
        Vector3d boundsMin,
        Vector3d boundsMax,
        int width,
        int height,
        int length,
        Vector3d position);

    (int x, int y, int z) SnapToScanCell(Vector3d boundsMin, Vector3d position, int scanCellSize);
}
