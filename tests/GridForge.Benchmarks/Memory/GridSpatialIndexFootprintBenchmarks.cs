using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridSpatialIndexFootprintBenchmarks
{
    private GridConfiguration _configuration;
    private GridWorld _world;

    [Params(1, 4, 8, 16, 24)]
    public int CellsPerAxis { get; set; }

    [GlobalSetup]
    public void SetupScenario()
    {
        _configuration = GridSpatialIndexBenchmarkData.CreateLargeConfiguration(
            Vector3d.Zero,
            CellsPerAxis);
    }

    [IterationSetup]
    public void SetupIteration()
    {
        _world = new GridWorld(GridSpatialIndexBenchmarkData.HashCellSize);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _world.Dispose();
        _world = null;
    }

    [Benchmark(Description = "Register one grid by spatial-hash footprint")]
    [BenchmarkCategory("Memory", "Registration", "GridWorld", "SpatialIndex")]
    public ushort RegisterGrid()
    {
        if (!_world.TryAddGrid(_configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to register the footprint benchmark grid.");

        return gridIndex;
    }
}
