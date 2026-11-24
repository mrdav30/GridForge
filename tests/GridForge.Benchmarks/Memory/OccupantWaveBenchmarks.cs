using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;

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
    private Voxel[] _bucketVoxels;
    private BenchmarkOccupant[] _bucketExpectedOccupants;
    private BenchmarkOccupant[] _bucketRemovedOccupants;
    private SwiftList<IVoxelOccupant> _bucketQueryResults;
    private GridScanScratch _bucketScratch;
    private IVoxelOccupant[] _bucketRadius3DOrder;
    private IVoxelOccupant[] _bucketRadius2DOrder;
    private int _bucketLiveOccupants;

    public int BucketQueryRounds { get; set; } = 8;

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

    [IterationSetup(Target = nameof(QueryCompactOccupantBuckets))]
    public void SetupCompactBucketIteration() => InitializeBucketScenario(4, 4);

    [IterationSetup(Targets = new[] { nameof(QueryChurnedOccupantBuckets), nameof(ChurnAndQueryOccupantBuckets) })]
    public void SetupChurnedBucketIteration() => InitializeBucketScenario(32, 4);

    [IterationSetup(Target = nameof(QueryFullOccupantBuckets))]
    public void SetupFullBucketIteration() => InitializeBucketScenario(32, 32);

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

    [Benchmark(Description = "Query 256 compact buckets: 4 live / 4 peak")]
    [BenchmarkCategory("Occupants", "BucketChurn", "Lookup", "ScanQuery")]
    public int QueryCompactOccupantBuckets() => ExecuteBucketQueries(BucketQueryRounds);

    [Benchmark(Description = "Query 256 churned buckets: 4 live / 32 peak")]
    [BenchmarkCategory("Occupants", "BucketChurn", "Lookup", "ScanQuery")]
    public int QueryChurnedOccupantBuckets() => ExecuteBucketQueries(BucketQueryRounds);

    [Benchmark(Description = "Query 256 full buckets: 32 live / 32 peak")]
    [BenchmarkCategory("Occupants", "BucketChurn", "Lookup", "ScanQuery")]
    public int QueryFullOccupantBuckets() => ExecuteBucketQueries(BucketQueryRounds);

    [Benchmark(Description = "Refill/remove 7168 registrations, then query retained buckets")]
    [BenchmarkCategory("Occupants", "BucketChurn", "ScanQuery")]
    public int ChurnAndQueryOccupantBuckets()
    {
        foreach (BenchmarkOccupant occupant in _bucketRemovedOccupants)
        {
            if (!_grid.TryAddVoxelOccupant(occupant))
                throw new InvalidOperationException("Unable to refill an occupant bucket.");
        }
        foreach (BenchmarkOccupant occupant in _bucketRemovedOccupants)
        {
            if (!_grid.TryRemoveVoxelOccupant(occupant))
                throw new InvalidOperationException("Unable to remove a churned registration.");
        }
        return ExecuteBucketQueries(BucketQueryRounds);
    }

    private void InitializeBucketScenario(int peakOccupants, int liveOccupants)
    {
        const int bucketCount = 256;
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: true);
        GridConfiguration configuration = new GridConfiguration(
            Vector3d.Zero, new Vector3d(15, 0, 15), scanCellSize: 8);
        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate occupant bucket grid.");
        _grid = _world.ActiveGrids[gridIndex];
        _bucketLiveOccupants = liveOccupants;
        _bucketVoxels = new Voxel[bucketCount];
        _bucketExpectedOccupants = new BenchmarkOccupant[bucketCount * liveOccupants];
        _tickets = new OccupantTicket[_bucketExpectedOccupants.Length];
        _bucketRemovedOccupants = new BenchmarkOccupant[bucketCount * (peakOccupants - liveOccupants)];
        int removed = 0;
        for (int bucket = 0; bucket < bucketCount; bucket++)
        {
            Vector3d position = new Vector3d(bucket % 16, 0, bucket / 16);
            if (!_grid.TryGetVoxel(position, out Voxel voxel))
                throw new InvalidOperationException("Unable to resolve occupant bucket voxel.");
            _bucketVoxels[bucket] = voxel;
            for (int i = 0; i < peakOccupants; i++)
            {
                BenchmarkOccupant occupant = new BenchmarkOccupant(position, (byte)(i % 8));
                if (!_grid.TryAddVoxelOccupant(occupant))
                    throw new InvalidOperationException("Unable to fill an occupant bucket.");
                if (i < peakOccupants - liveOccupants)
                    _bucketRemovedOccupants[removed++] = occupant;
                else
                {
                    int at = bucket * liveOccupants + i - (peakOccupants - liveOccupants);
                    _bucketExpectedOccupants[at] = occupant;
                    if (!GridOccupantManager.TryGetOccupancyTicket(_world, occupant, voxel.WorldIndex, out _tickets[at]))
                        throw new InvalidOperationException("Unable to obtain an occupant bucket ticket.");
                }
            }
            // The public ticket slots define the expected order independently of dense layout.
            int start = bucket * liveOccupants;
            for (int i = start + 1; i < start + liveOccupants; i++)
            {
                for (int j = i; j > start && _tickets[j].Slot < _tickets[j - 1].Slot; j--)
                {
                    (_tickets[j], _tickets[j - 1]) = (_tickets[j - 1], _tickets[j]);
                    (_bucketExpectedOccupants[j], _bucketExpectedOccupants[j - 1]) =
                        (_bucketExpectedOccupants[j - 1], _bucketExpectedOccupants[j]);
                }
            }
        }
        foreach (BenchmarkOccupant occupant in _bucketRemovedOccupants)
        {
            if (!_grid.TryRemoveVoxelOccupant(occupant))
                throw new InvalidOperationException("Unable to create an occupant bucket hole.");
        }
        _bucketQueryResults = new SwiftList<IVoxelOccupant>(_bucketExpectedOccupants.Length);
        _bucketScratch = new GridScanScratch();
        _bucketRadius3DOrder = CaptureBucketRadiusOrder(planar: false);
        _bucketRadius2DOrder = CaptureBucketRadiusOrder(planar: true);
        ExecuteBucketQueries(1);
    }

    private IVoxelOccupant[] CaptureBucketRadiusOrder(bool planar)
    {
        ScanBucketRadius(planar);
        SwiftHashSet<IVoxelOccupant> expected = new SwiftHashSet<IVoxelOccupant>();
        foreach (BenchmarkOccupant occupant in _bucketExpectedOccupants)
            expected.Add(occupant);
        foreach (IVoxelOccupant occupant in _bucketQueryResults)
        {
            if (!expected.Remove(occupant))
                throw new InvalidOperationException("Radius scan returned a duplicate or foreign registration.");
        }
        if (expected.Count != 0)
            throw new InvalidOperationException("Radius scan omitted a live registration.");
        return _bucketQueryResults.ToArray();
    }

    private int ExecuteBucketQueries(int rounds)
    {
        int hits = 0;
        for (int round = 0; round < rounds; round++)
        {
            for (int bucket = 0; bucket < _bucketVoxels.Length; bucket++)
            {
                Voxel voxel = _bucketVoxels[bucket];
                int start = bucket * _bucketLiveOccupants;
                for (int i = start; i < start + _bucketLiveOccupants; i++)
                {
                    if (!_grid.TryGetVoxelOccupant(voxel, _tickets[i], out IVoxelOccupant occupant)
                        || !ReferenceEquals(occupant, _bucketExpectedOccupants[i]))
                        throw new InvalidOperationException("Exact occupant ticket resolved incorrectly.");
                    hits++;
                }
                int count = 0;
                foreach (IVoxelOccupant occupant in _grid.GetOccupants(voxel))
                {
                    if (count >= _bucketLiveOccupants
                        || !ReferenceEquals(occupant, _bucketExpectedOccupants[start + count]))
                        throw new InvalidOperationException("Voxel enumeration changed membership or slot order.");
                    count++;
                }
                if (count != _bucketLiveOccupants)
                    throw new InvalidOperationException("Voxel enumeration omitted a registration.");
                hits += count;
            }
            for (int dimension = 0; dimension < 2; dimension++)
            {
                ScanBucketRadius(planar: dimension == 1);
                IVoxelOccupant[] expected = dimension == 1 ? _bucketRadius2DOrder : _bucketRadius3DOrder;
                if (_bucketQueryResults.Count != expected.Length)
                    throw new InvalidOperationException("Radius scan changed registration count.");
                for (int i = 0; i < expected.Length; i++)
                {
                    if (!ReferenceEquals(_bucketQueryResults[i], expected[i]))
                        throw new InvalidOperationException("Radius scan changed registration membership or order.");
                }
                hits += _bucketQueryResults.Count;
            }
        }
        return hits;
    }

    private void ScanBucketRadius(bool planar)
    {
        if (planar)
            GridScanManager.ScanRadiusInto(_world, new Vector2d(7, 7), (Fixed64)32,
                _bucketQueryResults, _bucketScratch, layerY: Fixed64.Zero);
        else
            GridScanManager.ScanRadiusInto(_world, new Vector3d(7, 0, 7), (Fixed64)32,
                _bucketQueryResults, _bucketScratch);
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
