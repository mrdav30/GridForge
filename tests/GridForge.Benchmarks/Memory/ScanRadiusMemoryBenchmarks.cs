using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net80)]
public class ScanRadiusMemoryBenchmarks
{
    private Vector3d _queryCenter;
    private Fixed64 _queryRadius;

    public int OccupantCount { get; set; } = 16384;

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

    private void InitializeScenario(bool clearAllPools)
    {
        BenchmarkEnvironment.PrepareWorld(clearAllPools);

        int gridSize = 63;
        for (int gridX = 0; gridX < 2; gridX++)
        {
            for (int gridZ = 0; gridZ < 2; gridZ++)
            {
                int minX = gridX * (gridSize + 1);
                int minZ = gridZ * (gridSize + 1);
                GridConfiguration configuration = new GridConfiguration(
                    new Vector3d(minX, 0, minZ),
                    new Vector3d(minX + gridSize, 0, minZ + gridSize),
                    scanCellSize: 8);

                if (!GlobalGridManager.TryAddGrid(configuration, out _))
                    throw new InvalidOperationException($"Unable to allocate scan benchmark grid ({gridX}, {gridZ}).");
            }
        }

        PopulateOccupants(OccupantCount);

        _queryCenter = new Vector3d(64, 0, 64);
        _queryRadius = (Fixed64)72;
    }

    private static void PopulateOccupants(int occupantCount)
    {
        int placed = 0;
        int groupId = 0;

        for (int z = 0; z <= 127 && placed < occupantCount; z++)
        {
            for (int x = 0; x <= 127 && placed < occupantCount; x++)
            {
                BenchmarkOccupant occupant = new BenchmarkOccupant(
                    new Vector3d(x, 0, z),
                    (byte)(groupId++ & 7));

                if (!GridOccupantManager.TryRegister(occupant))
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

        foreach (IVoxelOccupant occupant in GridScanManager.ScanRadius(_queryCenter, _queryRadius))
            occupantHits++;

        return occupantHits;
    }
}
