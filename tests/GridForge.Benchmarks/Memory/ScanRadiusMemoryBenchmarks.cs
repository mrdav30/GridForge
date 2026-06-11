using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class ScanRadiusMemoryBenchmarks
{
    private GridWorld _world;
    private Vector3d _queryCenter;
    private Vector2d _queryCenter2D;
    private Fixed64 _queryRadius;
    private Fixed64 _queryLayerY;
    private SwiftList<IVoxelOccupant> _queryResults;
    private GridScanScratch _queryScratch;

    public int OccupantCount { get; set; } = 16384;
    public int LayerOccupantCount { get; set; } = 8192;

    [IterationSetup(Target = nameof(ScanRadius_ColdPools))]
    public void SetupColdIteration()
    {
        InitializeScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(ScanRadius_WarmPools))]
    public void SetupWarmIteration()
    {
        InitializeScenario(clearAllPools: false);
    }

    [IterationSetup(Target = nameof(ScanRadius2D_ColdPools))]
    public void Setup2DColdIteration()
    {
        InitializeLayeredScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(ScanRadius2D_WarmPools))]
    public void Setup2DWarmIteration()
    {
        InitializeLayeredScenario(clearAllPools: false);
    }

    [IterationSetup(Target = nameof(ScanRadiusInto2D_ColdScratch))]
    public void SetupInto2DColdIteration()
    {
        InitializeLayeredScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(ScanRadiusInto2D_WarmScratch))]
    public void SetupInto2DWarmIteration()
    {
        InitializeLayeredScenario(clearAllPools: false);
        PrimeScanInto2D();
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "ScanRadius with cold temporary pools")]
    [BenchmarkCategory("Memory", "Pooling", "ScanQuery")]
    public int ScanRadius_ColdPools()
    {
        return ExecuteScan();
    }

    [Benchmark(Description = "ScanRadius with warm temporary pools")]
    [BenchmarkCategory("Memory", "Pooling", "ScanQuery")]
    public int ScanRadius_WarmPools()
    {
        return ExecuteScan();
    }

    [Benchmark(Description = "ScanRadius Vector2d with neighboring layers and cold pools")]
    [BenchmarkCategory("Memory", "Pooling", "ScanQuery", "Vector2d")]
    public int ScanRadius2D_ColdPools()
    {
        return ExecuteScan2D();
    }

    [Benchmark(Description = "ScanRadius Vector2d with neighboring layers and warm pools")]
    [BenchmarkCategory("Memory", "Pooling", "ScanQuery", "Vector2d")]
    public int ScanRadius2D_WarmPools()
    {
        return ExecuteScan2D();
    }

    [Benchmark(Description = "ScanRadiusInto Vector2d with caller-owned scratch and cold pools")]
    [BenchmarkCategory("Memory", "Pooling", "ScanQuery", "Vector2d", "Scratch")]
    public int ScanRadiusInto2D_ColdScratch()
    {
        return ExecuteScanInto2D();
    }

    [Benchmark(Description = "ScanRadiusInto Vector2d with caller-owned scratch and warm pools")]
    [BenchmarkCategory("Memory", "Pooling", "ScanQuery", "Vector2d", "Scratch")]
    public int ScanRadiusInto2D_WarmScratch()
    {
        return ExecuteScanInto2D();
    }

    private void InitializeScenario(bool clearAllPools)
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools);

        int gridSize = 63;
        for (int gridX = 0; gridX < 2; gridX++)
        {
            for (int gridZ = 0; gridZ < 2; gridZ++)
            {
                int minX = gridX * (gridSize + 1);
                int minZ = gridZ * (gridSize + 1);
                GridConfiguration configuration = new(
                    new Vector3d(minX, 0, minZ),
                    new Vector3d(minX + gridSize, 0, minZ + gridSize),
                    scanCellSize: 8);

                if (!_world.TryAddGrid(configuration, out _))
                    throw new InvalidOperationException($"Unable to allocate scan benchmark grid ({gridX}, {gridZ}).");
            }
        }

        PopulateOccupants(_world, OccupantCount);

        _queryCenter = new Vector3d(64, 0, 64);
        _queryRadius = (Fixed64)72;
    }

    private void InitializeLayeredScenario(bool clearAllPools)
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools);

        int gridSize = 63;
        for (int gridX = 0; gridX < 2; gridX++)
        {
            for (int gridZ = 0; gridZ < 2; gridZ++)
            {
                int minX = gridX * (gridSize + 1);
                int minZ = gridZ * (gridSize + 1);
                GridConfiguration configuration = new(
                    new Vector3d(minX, 0, minZ),
                    new Vector3d(minX + gridSize, 1, minZ + gridSize),
                    scanCellSize: 8);

                if (!_world.TryAddGrid(configuration, out _))
                    throw new InvalidOperationException($"Unable to allocate layered scan benchmark grid ({gridX}, {gridZ}).");
            }
        }

        PopulateOccupants(_world, LayerOccupantCount, y: 0);
        PopulateOccupants(_world, LayerOccupantCount, y: 1);

        _queryCenter2D = new Vector2d(64, 64);
        _queryLayerY = Fixed64.Zero;
        _queryRadius = (Fixed64)72;
        _queryResults = new SwiftList<IVoxelOccupant>();
        _queryScratch = new GridScanScratch();
    }

    private static void PopulateOccupants(GridWorld world, int occupantCount)
    {
        PopulateOccupants(world, occupantCount, y: 0);
    }

    private static void PopulateOccupants(GridWorld world, int occupantCount, int y)
    {
        int placed = 0;
        int groupId = 0;

        for (int z = 0; z <= 127 && placed < occupantCount; z++)
        {
            for (int x = 0; x <= 127 && placed < occupantCount; x++)
            {
                BenchmarkOccupant occupant = new(
                    new Vector3d(x, y, z),
                    (byte)(groupId++ & 7));

                if (!GridOccupantManager.TryRegister(world, occupant))
                    throw new InvalidOperationException($"Unable to register benchmark occupant at {(x, z)}.");

                placed++;
            }
        }

        if (placed != occupantCount)
            throw new InvalidOperationException($"Only placed {placed} of {occupantCount} benchmark occupants.");
    }

    private int ExecuteScan()
    {
        int occupantHits = 0;

        foreach (IVoxelOccupant occupant in GridScanManager.ScanRadius(_world, _queryCenter, _queryRadius))
            occupantHits++;

        return occupantHits;
    }

    private int ExecuteScan2D()
    {
        int occupantHits = 0;

        foreach (IVoxelOccupant occupant in GridScanManager.ScanRadius(
            _world,
            _queryCenter2D,
            _queryRadius,
            layerY: _queryLayerY))
        {
            occupantHits++;
        }

        return occupantHits;
    }

    private int ExecuteScanInto2D()
    {
        GridScanManager.ScanRadiusInto(
            _world,
            _queryCenter2D,
            _queryRadius,
            _queryResults,
            _queryScratch,
            layerY: _queryLayerY);

        return _queryResults.Count;
    }

    private void PrimeScanInto2D()
    {
        ExecuteScanInto2D();
        _queryResults.Clear();
        _queryScratch.Clear();
    }
}
