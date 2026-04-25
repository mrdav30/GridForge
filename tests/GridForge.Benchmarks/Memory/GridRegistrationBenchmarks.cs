using BenchmarkDotNet.Attributes;
using GridForge.Configuration;
using GridForge.Grids;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridRegistrationBenchmarks
{
    private GridConfiguration[] _configurations;
    private ushort[] _allocatedIndices;
    private GridWorld _world;

    public int GridCountPerAxis { get; set; } = 8;

    public int GridExtent { get; set; } = 7;

    [IterationSetup(Target = nameof(RegisterAdjacentGrids))]
    public void SetupRegisterIteration()
    {
        InitializeScenario();
    }

    [IterationSetup(Target = nameof(RemoveAdjacentGrids))]
    public void SetupRemoveIteration()
    {
        InitializeScenario();
        AllocateAllGrids();
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "Register many adjacent grids")]
    [BenchmarkCategory("Memory", "Registration", "GridWorld")]
    public int RegisterAdjacentGrids()
    {
        return ExecuteRegistration();
    }

    [Benchmark(Description = "Remove many adjacent grids")]
    [BenchmarkCategory("Memory", "Registration", "GridWorld")]
    public int RemoveAdjacentGrids()
    {
        return ExecuteRemoval();
    }

    private void InitializeScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: true);

        _configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            GridCountPerAxis,
            GridCountPerAxis,
            GridExtent,
            scanCellSize: 4,
            overlapBoundaries: true);

        _allocatedIndices = new ushort[_configurations.Length];

        WarmPools();
    }

    private void WarmPools()
    {
        AllocateAllGrids();
        BenchmarkEnvironment.ResetWorld();
        _world = BenchmarkEnvironment.PrepareWorld();
    }

    private int ExecuteRegistration()
    {
        AllocateAllGrids();

        int totalNeighborLinks = 0;
        foreach (VoxelGrid grid in _world.ActiveGrids)
            totalNeighborLinks += grid.NeighborCount;

        return totalNeighborLinks;
    }

    private void AllocateAllGrids()
    {
        for (int i = 0; i < _configurations.Length; i++)
        {
            if (!_world.TryAddGrid(_configurations[i], out ushort gridIndex))
                throw new InvalidOperationException($"Unable to allocate registration benchmark grid {i}.");

            _allocatedIndices[i] = gridIndex;
        }
    }

    private int ExecuteRemoval()
    {
        int removedCount = 0;

        for (int i = _allocatedIndices.Length - 1; i >= 0; i--)
        {
            if (!_world.TryRemoveGrid(_allocatedIndices[i]))
                throw new InvalidOperationException($"Unable to remove registration benchmark grid {_allocatedIndices[i]}.");

            removedCount++;
        }

        return removedCount;
    }
}
