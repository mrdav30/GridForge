using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob(RuntimeMoniker.Net80)]
public class VoxelGridMemoryBenchmarks
{
    private GridConfiguration[] _configurations;
    private ushort[] _allocatedIndices;

    public int GridCount { get; set; } = 4;

    public int GridExtent { get; set; } = 31;

    [IterationSetup(Target = nameof(CreateAndRemoveGrids_ColdPools))]
    public void SetupColdIteration()
    {
        InitializeScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(CreateAndRemoveGrids_WarmPools))]
    public void SetupWarmIteration()
    {
        InitializeScenario(clearAllPools: false);
    }

    [IterationCleanup]
    public static void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "Grid lifecycle with cold object pools")]
    [BenchmarkCategory("Memory", "Pooling", "VoxelGrid")]
    public int CreateAndRemoveGrids_ColdPools()
    {
        return ExecuteLifecycle();
    }

    [Benchmark(Description = "Grid lifecycle with warm object pools")]
    [BenchmarkCategory("Memory", "Pooling", "VoxelGrid")]
    public int CreateAndRemoveGrids_WarmPools()
    {
        return ExecuteLifecycle();
    }

    private void InitializeScenario(bool clearAllPools)
    {
        BenchmarkEnvironment.PrepareWorld(clearAllPools);

        _configurations = new GridConfiguration[GridCount];
        _allocatedIndices = new ushort[GridCount];

        int spacing = GridExtent + 4;
        for (int i = 0; i < GridCount; i++)
        {
            Vector3d min = new Vector3d(i * spacing, 0, 0);
            Vector3d max = new Vector3d(i * spacing + GridExtent, GridExtent, GridExtent);
            _configurations[i] = new GridConfiguration(min, max, scanCellSize: 8);
        }
    }

    private int ExecuteLifecycle()
    {
        int totalVoxelCount = 0;

        for (int i = 0; i < _configurations.Length; i++)
        {
            if (!GlobalGridManager.TryAddGrid(_configurations[i], out ushort gridIndex))
                throw new InvalidOperationException($"Unable to allocate benchmark grid {i}.");

            _allocatedIndices[i] = gridIndex;
            totalVoxelCount += GlobalGridManager.ActiveGrids[gridIndex].Size;
        }

        for (int i = _allocatedIndices.Length - 1; i >= 0; i--)
        {
            if (!GlobalGridManager.TryRemoveGrid(_allocatedIndices[i]))
                throw new InvalidOperationException($"Unable to release benchmark grid {_allocatedIndices[i]}.");
        }

        return totalVoxelCount;
    }
}
