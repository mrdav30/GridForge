using System;
using System.Reflection;
using FixedMathSharp;
using GridForge.Configuration;

namespace GridForge.Grids.Tests;

internal static class GridWorldTestFactory
{
    public static GridWorld CreateWorld(int spatialGridCellSize = GridWorld.DefaultSpatialGridCellSize)
    {
        return new GridWorld(spatialGridCellSize);
    }

    // Seed a fully published prefix with no pending notifications.
    public static void SeedPublishedSequence(GridWorld world, ulong sequence)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GridWorld).GetField("_changeSequence", fields).SetValue(world, sequence);
        typeof(GridWorld).GetField("_publishedChangeSequence", fields).SetValue(world, sequence);
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
