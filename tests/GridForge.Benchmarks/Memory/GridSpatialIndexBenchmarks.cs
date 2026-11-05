using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using SwiftCollections.Query;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridSpatialIndexBenchmarks
{
    private const int ScaleCount = 3;
    private const int GridsPerScale = 8;
    private const int GridCount = ScaleCount * GridsPerScale;

    private FixedBoundVolume[] _bounds;
    private ushort[] _gridIndices;
    private Vector3d[] _queryPositions;
    private GridWorld _world;
    private GridSpatialIndex _queryIndex;
    private GridSpatialIndex _lifecycleIndex;
    private SwiftList<ushort> _candidates;

    [Params(64UL, 512UL, 4_096UL)]
    public ulong CellBudget { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _bounds = new FixedBoundVolume[GridCount];
        _gridIndices = new ushort[GridCount];
        _queryPositions = new Vector3d[ScaleCount];
        _world = new GridWorld(GridSpatialIndexBenchmarkData.HashCellSize);
        _queryIndex = new GridSpatialIndex(GridSpatialIndexBenchmarkData.HashCellSize, CellBudget);
        _lifecycleIndex = new GridSpatialIndex(GridSpatialIndexBenchmarkData.HashCellSize, CellBudget);
        _candidates = new SwiftList<ushort>(GridCount);

        int gridOffset = 0;
        for (int scale = 0; scale < ScaleCount; scale++)
        {
            int cellsPerAxis = 4 << scale;
            for (int i = 0; i < GridsPerScale; i++)
            {
                Vector3d origin = new(
                    scale * 1_000_000 + i * 100_000,
                    scale * 1_000_000,
                    0);
                VoxelGrid grid = GridSpatialIndexBenchmarkData.AddGrid(
                    _world,
                    GridSpatialIndexBenchmarkData.CreateLargeConfiguration(origin, cellsPerAxis));
                FixedBoundVolume bounds = new(grid.BoundsMin, grid.BoundsMax);

                _bounds[gridOffset] = bounds;
                _gridIndices[gridOffset] = grid.GridIndex;
                _queryIndex.Insert(grid.GridIndex, bounds);
                if (i == 0)
                    _queryPositions[scale] = bounds.Center;

                gridOffset++;
            }
        }

        QueryMixedScale();
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(OperationsPerInvoke = GridCount, Description = "Register and remove mixed-scale index entries")]
    [BenchmarkCategory("Memory", "Registration", "SpatialIndex")]
    public int RegisterAndRemoveMixedScale()
    {
        for (int i = 0; i < GridCount; i++)
            _lifecycleIndex.Insert(_gridIndices[i], _bounds[i]);

        for (int i = 0; i < GridCount; i++)
            _lifecycleIndex.Remove(_gridIndices[i]);

        return _lifecycleIndex.Count;
    }

    [Benchmark(OperationsPerInvoke = ScaleCount, Description = "Query warmed mixed-scale index entries")]
    [BenchmarkCategory("Memory", "Lookup", "SpatialIndex")]
    public int QueryMixedScale()
    {
        int count = 0;
        for (int i = 0; i < ScaleCount; i++)
        {
            Vector3d position = _queryPositions[i];
            _queryIndex.CollectCandidates(
                new FixedBoundVolume(position, position),
                _world.ActiveGrids,
                _candidates);
            count += _candidates.Count;
        }

        return count;
    }

    [Benchmark(Description = "Create and query one maximum-candidate entry")]
    [BenchmarkCategory("Memory", "Lookup", "SpatialIndex", "Cold")]
    public int CreateAndQueryMaximumCandidate()
    {
        int offset = GridCount - 1;
        var index = new GridSpatialIndex(GridSpatialIndexBenchmarkData.HashCellSize, CellBudget);
        var candidates = new SwiftList<ushort>(1);
        index.Insert(_gridIndices[offset], _bounds[offset]);
        Vector3d position = _bounds[offset].Center;
        index.CollectCandidates(
            new FixedBoundVolume(position, position),
            _world.ActiveGrids,
            candidates);
        return candidates.Count;
    }
}
