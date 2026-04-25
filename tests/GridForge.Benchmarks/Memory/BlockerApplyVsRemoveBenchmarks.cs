using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class BlockerApplyVsRemoveBenchmarks
{
    private BoundingArea[] _areas;
    private BoundsBlocker[] _blockers;
    private GridWorld _world;

    public int BlockerCount { get; set; } = 64;

    public int BlockSpan { get; set; } = 6;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _areas = BenchmarkScenarioFactory.CreateBlockerAreas(
            BlockerCount,
            BlockSpan,
            columns: 8,
            stride: 20);
    }

    [IterationSetup(Target = nameof(ApplyBlockers_NoCoverageCache))]
    public void SetupApplyUncachedIteration()
    {
        InitializeScenario(cacheCoveredVoxels: false, applyInSetup: false);
    }

    [IterationSetup(Target = nameof(ApplyBlockers_CoverageCacheEnabled))]
    public void SetupApplyCachedIteration()
    {
        InitializeScenario(cacheCoveredVoxels: true, applyInSetup: false);
    }

    [IterationSetup(Target = nameof(RemoveBlockers_NoCoverageCache))]
    public void SetupRemoveUncachedIteration()
    {
        InitializeScenario(cacheCoveredVoxels: false, applyInSetup: true);
    }

    [IterationSetup(Target = nameof(RemoveBlockers_CoverageCacheEnabled))]
    public void SetupRemoveCachedIteration()
    {
        InitializeScenario(cacheCoveredVoxels: true, applyInSetup: true);
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

    [Benchmark(Description = "Apply blockers without cached coverage")]
    [BenchmarkCategory("Memory", "Caching", "BlockerApply")]
    public int ApplyBlockers_NoCoverageCache()
    {
        return ExecuteApply();
    }

    [Benchmark(Description = "Apply blockers with cached coverage")]
    [BenchmarkCategory("Memory", "Caching", "BlockerApply")]
    public int ApplyBlockers_CoverageCacheEnabled()
    {
        return ExecuteApply();
    }

    [Benchmark(Description = "Remove blockers without cached coverage")]
    [BenchmarkCategory("Memory", "Caching", "BlockerRemove")]
    public int RemoveBlockers_NoCoverageCache()
    {
        return ExecuteRemove();
    }

    [Benchmark(Description = "Remove blockers with cached coverage")]
    [BenchmarkCategory("Memory", "Caching", "BlockerRemove")]
    public int RemoveBlockers_CoverageCacheEnabled()
    {
        return ExecuteRemove();
    }

    private void InitializeScenario(bool cacheCoveredVoxels, bool applyInSetup)
    {
        _world = BenchmarkEnvironment.PrepareWorld();

        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            tilesX: 2,
            tilesZ: 2,
            extent: 95,
            scanCellSize: 8);

        for (int i = 0; i < configurations.Length; i++)
        {
            if (!_world.TryAddGrid(configurations[i], out _))
                throw new InvalidOperationException($"Unable to allocate blocker benchmark grid {i}.");
        }

        _blockers = new BoundsBlocker[_areas.Length];
        for (int i = 0; i < _areas.Length; i++)
            _blockers[i] = new BoundsBlocker(_world, _areas[i], cacheCoveredVoxels: cacheCoveredVoxels);

        if (applyInSetup)
        {
            for (int i = 0; i < _blockers.Length; i++)
                _blockers[i].ApplyBlockage();
        }
    }

    private int ExecuteApply()
    {
        int appliedCount = 0;

        for (int i = 0; i < _blockers.Length; i++)
        {
            _blockers[i].ApplyBlockage();
            if (_blockers[i].IsBlocking)
                appliedCount++;
        }

        return appliedCount;
    }

    private int ExecuteRemove()
    {
        int removedCount = 0;

        for (int i = 0; i < _blockers.Length; i++)
        {
            _blockers[i].RemoveBlockage();
            if (!_blockers[i].IsBlocking)
                removedCount++;
        }

        return removedCount;
    }
}
