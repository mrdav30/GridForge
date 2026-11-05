using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridSpatialIndexLargeBenchmarks
{
    private GridWorld _world;
    private VoxelGrid _boundsQueryGrid;
    private SwiftList<VoxelGrid> _overlaps;
    private Vector3d _queryPosition;

    [Params(8, 64, 256)]
    public int GridCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new GridWorld(GridSpatialIndexBenchmarkData.HashCellSize);
        _overlaps = new SwiftList<VoxelGrid>(1);

        Vector3d firstLargeOrigin = default;
        for (int i = 0; i < GridCount; i++)
        {
            Vector3d origin = GridSpatialIndexBenchmarkData.GetLargeGridOrigin(i);
            GridSpatialIndexBenchmarkData.AddGrid(
                _world,
                GridSpatialIndexBenchmarkData.CreateLargeConfiguration(origin, cellsPerAxis: 17));
            if (i == 0)
            {
                firstLargeOrigin = origin;
                _queryPosition = origin + new Vector3d(400, 400, 400);
            }
        }

        _boundsQueryGrid = GridSpatialIndexBenchmarkData.AddGrid(
            _world,
            GridSpatialIndexBenchmarkData.CreatePointConfiguration(firstLargeOrigin));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world.Dispose();
    }

    [Benchmark(Description = "Point lookup among large grids")]
    [BenchmarkCategory("Memory", "Lookup", "GridWorld", "SpatialIndex")]
    public ushort PointLookup()
    {
        if (!_world.TryGetGrid(_queryPosition, out VoxelGrid grid))
            throw new InvalidOperationException("Unable to resolve the large-grid benchmark target.");

        return grid.GridIndex;
    }

    [Benchmark(Description = "Bounds lookup among large grids")]
    [BenchmarkCategory("Memory", "Lookup", "GridWorld", "SpatialIndex")]
    public int BoundsLookup()
    {
        _world.FindOverlappingGridsInto(_boundsQueryGrid, _overlaps);
        return _overlaps.Count;
    }
}
