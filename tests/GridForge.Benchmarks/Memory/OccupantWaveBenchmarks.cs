using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class OccupantWaveBenchmarks
{
    private BenchmarkOccupant[] _occupants;
    private VoxelGrid _grid;
    private WorldVoxelIndex[] _indices;
    private IVoxelOccupant _resolvedOccupant;
    private OccupantTicket[] _tickets;
    private GridWorld _world;

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

    [IterationSetup(Target = nameof(LookupCurrentOccupantTickets))]
    public void SetupLookupIteration()
    {
        InitializeScenario(clearAllPools: false);
        _indices = new WorldVoxelIndex[_occupants.Length];
        _tickets = new OccupantTicket[_occupants.Length];

        for (int i = 0; i < _occupants.Length; i++)
        {
            BenchmarkOccupant occupant = _occupants[i];
            if (!_grid.TryAddVoxelOccupant(occupant)
                || !_grid.TryGetVoxel(occupant.Position, out Voxel voxel)
                || !GridOccupantManager.TryGetOccupancyTicket(
                    _world,
                    occupant,
                    voxel.WorldIndex,
                    out _tickets[i]))
            {
                throw new InvalidOperationException($"Unable to prepare benchmark occupant {i}.");
            }

            _indices[i] = voxel.WorldIndex;
        }
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

    [Benchmark(Description = "Current occupant ticket lookup")]
    [BenchmarkCategory("Memory", "Occupants", "Lookup")]
    public int LookupCurrentOccupantTickets()
    {
        int resolved = 0;
        for (int i = 0; i < _occupants.Length; i++)
        {
            if (!GridScanManager.TryGetVoxelOccupant(
                    _world,
                    _indices[i],
                    _tickets[i],
                    out _resolvedOccupant)
                || !ReferenceEquals(_occupants[i], _resolvedOccupant))
            {
                throw new InvalidOperationException($"Unable to resolve benchmark occupant {i}.");
            }

            resolved++;
        }

        return resolved;
    }

    private void InitializeScenario(bool clearAllPools)
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools);

        GridConfiguration configuration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(127, 0, 127),
            scanCellSize: 8);

        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate occupant wave benchmark grid.");

        _grid = _world.ActiveGrids[gridIndex];
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
