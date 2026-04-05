using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using SwiftCollections.Pool;
using System;
using System.Reflection;

namespace GridForge.Benchmarks;

internal static class BenchmarkEnvironment
{
    private static readonly Action _clearGridForgePools = CreateGridForgePoolClearer();
    private static bool _loggingSuppressed;

    public static void PrepareWorld(
        bool clearAllPools = false,
        Fixed64? voxelSize = null,
        int spatialGridCellSize = GlobalGridManager.DefaultSpatialGridCellSize)
    {
        SuppressLogging();
        ResetWorld();

        if (clearAllPools)
            ClearAllPools();

        GlobalGridManager.Setup(voxelSize, spatialGridCellSize);
    }

    public static void ResetWorld()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset(deactivate: true);
    }

    public static void ClearAllPools()
    {
        _clearGridForgePools();

        SwiftHashSetPool<int>.Shared.Clear();
        SwiftHashSetPool<ushort>.Shared.Clear();
        SwiftHashSetPool<ScanCell>.Shared.Clear();
        SwiftHashSetPool<Voxel>.Shared.Clear();

        SwiftListPool<IVoxelOccupant>.Shared.Clear();
        SwiftListPool<ScanCell>.Shared.Clear();
        SwiftListPool<Voxel>.Shared.Clear();
    }

    private static void SuppressLogging()
    {
        if (_loggingSuppressed)
            return;

        GridForgeLogger.MinimumLevel = DiagnosticLevel.None;
        _loggingSuppressed = true;
    }

    private static Action CreateGridForgePoolClearer()
    {
        Type poolsType = typeof(GlobalGridManager).Assembly.GetType("GridForge.Grids.Pools");
        if (poolsType == null)
            throw new InvalidOperationException("Unable to locate GridForge pool manager.");

        MethodInfo clearMethod = poolsType.GetMethod(
            "ClearPools",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (clearMethod == null)
            throw new InvalidOperationException("Unable to locate GridForge pool reset method.");

        return () => clearMethod.Invoke(null, null);
    }
}
