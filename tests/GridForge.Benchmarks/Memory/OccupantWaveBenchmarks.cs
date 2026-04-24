using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class OccupantWaveBenchmarks
{
    private BenchmarkOccupant[] _occupants;
    private VoxelGrid _grid;

    public int OccupantCount { get; set; } = 8192;

    public int WaveCount { get; set; } = 4;

    [IterationSetup(Target = nameof(AddAndRemoveOccupantWave_ColdPools))]
    public void SetupColdIteration()
    {
        InitializeScenario(clearAllPools: true);
    }

    [IterationSetup(Target = nameof(AddAndRemoveOccupantWave_WarmPools))]
    public void SetupWarmIteration()
    {
        InitializeScenario(clearAllPools: false);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "Occupant waves with cold pools")]
    [BenchmarkCategory("Memory", "Pooling", "Occupants")]
    public int AddAndRemoveOccupantWave_ColdPools()
    {
        return ExecuteWaves();
    }

    [Benchmark(Description = "Occupant waves with warm pools")]
    [BenchmarkCategory("Memory", "Pooling", "Occupants")]
    public int AddAndRemoveOccupantWave_WarmPools()
    {
        return ExecuteWaves();
    }

    private void InitializeScenario(bool clearAllPools)
    {
        BenchmarkEnvironment.PrepareWorld(clearAllPools);

        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(127, 0, 127),
            scanCellSize: 8);

        if (!GlobalGridManager.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate occupant wave benchmark grid.");

        _grid = GlobalGridManager.ActiveGrids[gridIndex];
        _occupants = BenchmarkScenarioFactory.CreateOccupants(OccupantCount, 128, 128);
    }

    private int ExecuteWaves()
    {
        int operations = 0;

        for (int wave = 0; wave < WaveCount; wave++)
        {
            for (int i = 0; i < _occupants.Length; i++)
            {
                if (!_grid.TryAddVoxelOccupant(_occupants[i]))
                    throw new InvalidOperationException($"Unable to add benchmark occupant {i} on wave {wave}.");
            }

            operations += _occupants.Length;

            for (int i = 0; i < _occupants.Length; i++)
            {
                if (!_grid.TryRemoveVoxelOccupant(_occupants[i]))
                    throw new InvalidOperationException($"Unable to remove benchmark occupant {i} on wave {wave}.");
            }

            operations += _occupants.Length;
        }

        return operations;
    }
}
