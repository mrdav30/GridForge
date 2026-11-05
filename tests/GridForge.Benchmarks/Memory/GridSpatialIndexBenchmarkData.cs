using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;

namespace GridForge.Benchmarks;

internal static class GridSpatialIndexBenchmarkData
{
    public const int HashCellSize = 50;

    public static GridConfiguration CreateLargeConfiguration(Vector3d origin, int cellsPerAxis)
    {
        Fixed64 extent = (Fixed64)((cellsPerAxis - 1) * HashCellSize);
        return new GridConfiguration(
            origin,
            origin + new Vector3d(extent, extent, extent),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)HashCellSize),
            storageKind: GridStorageKind.Sparse);
    }

    public static GridConfiguration CreateOrdinaryConfiguration(int x, int z) =>
        new(
            new Vector3d(x, 0, z),
            new Vector3d(x + 1, 0, z + 1),
            storageKind: GridStorageKind.Sparse);

    public static GridConfiguration CreatePointConfiguration(Vector3d position) =>
        new(position, position, storageKind: GridStorageKind.Sparse);

    public static Vector3d GetLargeGridOrigin(int index) =>
        new(10_000 + index * 1_600, 10_000, 10_000);

    public static VoxelGrid AddGrid(GridWorld world, GridConfiguration configuration)
    {
        if (!world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException($"Unable to register benchmark grid {configuration.BoundsMin} -> {configuration.BoundsMax}.");

        return world.ActiveGrids[gridIndex];
    }

}
