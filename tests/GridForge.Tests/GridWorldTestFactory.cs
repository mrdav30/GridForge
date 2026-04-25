using FixedMathSharp;
using GridForge.Configuration;
using System;

namespace GridForge.Grids.Tests;

internal static class GridWorldTestFactory
{
    public static GridWorld CreateWorld(
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        return new GridWorld(voxelSize, spatialGridCellSize);
    }

    public static VoxelGrid AddGrid(
        GridWorld world,
        Vector3d boundsMin,
        Vector3d boundsMax,
        int scanCellSize = GridConfiguration.DefaultScanCellSize)
    {
        GridConfiguration configuration = new(boundsMin, boundsMax, scanCellSize);
        return AddGrid(world, configuration);
    }

    public static VoxelGrid AddGrid(
        GridWorld world,
        GridConfiguration configuration)
    {
        if (!world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException($"Unable to add grid for bounds {configuration.BoundsMin} -> {configuration.BoundsMax}.");

        return world.ActiveGrids[gridIndex];
    }
}
