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
    [BenchmarkCategory("Memory", "Registration", "GlobalGridManager")]
    public int RegisterAdjacentGrids()
    {
        return ExecuteRegistration();
    }

    [Benchmark(Description = "Remove many adjacent grids")]
    [BenchmarkCategory("Memory", "Registration", "GlobalGridManager")]
    public int RemoveAdjacentGrids()
    {
        return ExecuteRemoval();
    }

    private void InitializeScenario()
    {
        BenchmarkEnvironment.PrepareWorld(clearAllPools: true);

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
        GlobalGridManager.Setup();
    }

    private int ExecuteRegistration()
    {
        AllocateAllGrids();

        int totalNeighborLinks = 0;
        foreach (VoxelGrid grid in GlobalGridManager.ActiveGrids)
            totalNeighborLinks += grid.NeighborCount;

        return totalNeighborLinks;
    }

    private void AllocateAllGrids()
    {
        for (int i = 0; i < _configurations.Length; i++)
        {
            if (!GlobalGridManager.TryAddGrid(_configurations[i], out ushort gridIndex))
                throw new InvalidOperationException($"Unable to allocate registration benchmark grid {i}.");

            _allocatedIndices[i] = gridIndex;
        }
    }

    private int ExecuteRemoval()
    {
        int removedCount = 0;

        for (int i = _allocatedIndices.Length - 1; i >= 0; i--)
        {
            if (!GlobalGridManager.TryRemoveGrid(_allocatedIndices[i]))
                throw new InvalidOperationException($"Unable to remove registration benchmark grid {_allocatedIndices[i]}.");

            removedCount++;
        }

        return removedCount;
    }
}
