using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridSpatialIndexTuningBenchmarks
{
    private GridConfiguration _configuration;
    private GridWorld _world;

    [Params(25, 50, 100, 200)]
    public int SpatialGridCellSize { get; set; }

    [GlobalSetup]
    public void SetupScenario()
    {
        _configuration = GridSpatialIndexBenchmarkData.CreateLargeConfiguration(
            Vector3d.Zero,
            cellsPerAxis: 17);
    }

    [IterationSetup]
    public void SetupIteration()
    {
        _world = new GridWorld(SpatialGridCellSize);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _world.Dispose();
        _world = null;
    }

    [Benchmark(Description = "Register a fixed 800-unit grid by hash cell size")]
    [BenchmarkCategory("Memory", "Registration", "GridWorld", "SpatialIndex")]
    public ushort RegisterFixedSpanGrid()
    {
        if (!_world.TryAddGrid(_configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to register the cell-size benchmark grid.");

        return gridIndex;
    }
}
