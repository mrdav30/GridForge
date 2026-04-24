using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class BlockerMemoryBenchmarks
{
    private BoundingArea[] _areas;
    private BoundsBlocker[] _blockers;

    public int BlockerCount { get; set; } = 64;

    public int BlockSpan { get; set; } = 6;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _areas = BuildAreas();
    }

    [IterationSetup(Target = nameof(ApplyAndRemoveBlockers_NoCoverageCache))]
    public void SetupUncachedIteration()
    {
        InitializeScenario(cacheCoveredVoxels: false);
    }

    [IterationSetup(Target = nameof(ApplyAndRemoveBlockers_CoverageCacheEnabled))]
    public void SetupCachedIteration()
    {
        InitializeScenario(cacheCoveredVoxels: true);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        if (_blockers != null)
        {
            foreach (BoundsBlocker blocker in _blockers)
                blocker.Reset();
        }

        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "Blocker apply/remove without cached coverage")]
    [BenchmarkCategory("Memory", "Caching", "Blocker")]
    public int ApplyAndRemoveBlockers_NoCoverageCache()
    {
        return ExecuteBlockerWave();
    }

    [Benchmark(Description = "Blocker apply/remove with cached coverage")]
    [BenchmarkCategory("Memory", "Caching", "Blocker")]
    public int ApplyAndRemoveBlockers_CoverageCacheEnabled()
    {
        return ExecuteBlockerWave();
    }

    private void InitializeScenario(bool cacheCoveredVoxels)
    {
        BenchmarkEnvironment.PrepareWorld();

        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(191, 0, 191),
            scanCellSize: 8);

        if (!GlobalGridManager.TryAddGrid(configuration, out _))
            throw new InvalidOperationException("Unable to allocate blocker benchmark grid.");

        _blockers = new BoundsBlocker[_areas.Length];
        for (int i = 0; i < _areas.Length; i++)
            _blockers[i] = new BoundsBlocker(_areas[i], cacheCoveredVoxels: cacheCoveredVoxels);
    }

    private BoundingArea[] BuildAreas()
    {
        BoundingArea[] areas = new BoundingArea[BlockerCount];

        const int columns = 8;
        const int stride = 20;
        for (int i = 0; i < areas.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            int x = 4 + column * stride + (row & 1);
            int z = 4 + row * stride + (column & 1);

            Vector3d min = new(x, 0, z);
            Vector3d max = new(x + BlockSpan, 0, z + BlockSpan);
            areas[i] = new BoundingArea(min, max);
        }

        return areas;
    }

    private int ExecuteBlockerWave()
    {
        int appliedCount = 0;

        for (int wave = 0; wave < 8; wave++)
        {
            for (int i = 0; i < _blockers.Length; i++)
            {
                _blockers[i].ApplyBlockage();
                if (_blockers[i].IsBlocking)
                    appliedCount++;
            }

            for (int i = 0; i < _blockers.Length; i++)
                _blockers[i].RemoveBlockage();
        }

        return appliedCount;
    }
}
