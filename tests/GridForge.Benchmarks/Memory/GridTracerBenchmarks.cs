using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Utility;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridTracerBenchmarks
{
    private Vector3d _coverageMin;
    private Vector3d _coverageMax;
    private Vector3d _lineStart;
    private Vector3d _lineEnd;

    [IterationSetup(Target = nameof(GetCoveredVoxels_ColdPools))]
    public void SetupCoverageColdIteration()
    {
        InitializeScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(GetCoveredVoxels_WarmPools))]
    public void SetupCoverageWarmIteration()
    {
        InitializeScenario(clearAllPools: false);
    }

    [IterationSetup(Target = nameof(TraceLine_ColdPools))]
    public void SetupTraceColdIteration()
    {
        InitializeScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(TraceLine_WarmPools))]
    public void SetupTraceWarmIteration()
    {
        InitializeScenario(clearAllPools: false);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Description = "GetCoveredVoxels with cold pools")]
    [BenchmarkCategory("Memory", "Pooling", "GridTracerCoverage")]
    public int GetCoveredVoxels_ColdPools()
    {
        return ExecuteCoveredVoxelTrace();
    }

    [Benchmark(Description = "GetCoveredVoxels with warm pools")]
    [BenchmarkCategory("Memory", "Pooling", "GridTracerCoverage")]
    public int GetCoveredVoxels_WarmPools()
    {
        return ExecuteCoveredVoxelTrace();
    }

    [Benchmark(Description = "TraceLine with cold pools")]
    [BenchmarkCategory("Memory", "Pooling", "GridTracerLine")]
    public int TraceLine_ColdPools()
    {
        return ExecuteLineTrace();
    }

    [Benchmark(Description = "TraceLine with warm pools")]
    [BenchmarkCategory("Memory", "Pooling", "GridTracerLine")]
    public int TraceLine_WarmPools()
    {
        return ExecuteLineTrace();
    }

    private void InitializeScenario(bool clearAllPools)
    {
        BenchmarkEnvironment.PrepareWorld(clearAllPools);

        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            tilesX: 2,
            tilesZ: 2,
            extent: 63,
            scanCellSize: 8);

        for (int i = 0; i < configurations.Length; i++)
        {
            if (!GlobalGridManager.TryAddGrid(configurations[i], out _))
                throw new InvalidOperationException($"Unable to allocate tracer benchmark grid {i}.");
        }

        _coverageMin = new Vector3d(24, 0, 24);
        _coverageMax = new Vector3d(104, 0, 104);
        _lineStart = new Vector3d(8, 0, 8);
        _lineEnd = new Vector3d(120, 0, 120);
    }

    private int ExecuteCoveredVoxelTrace()
    {
        int voxelCount = 0;

        foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(_coverageMin, _coverageMax))
            voxelCount += covered.Voxels.Count;

        return voxelCount;
    }

    private int ExecuteLineTrace()
    {
        int voxelCount = 0;

        foreach (GridVoxelSet traced in GridTracer.TraceLine(_lineStart, _lineEnd, includeEnd: true))
            voxelCount += traced.Voxels.Count;

        return voxelCount;
    }
}
