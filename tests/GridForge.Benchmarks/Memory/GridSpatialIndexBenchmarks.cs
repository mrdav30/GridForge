using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using System;

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

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridSpatialIndexMixedBenchmarks
{
    private GridWorld _world;
    private VoxelGrid _boundsQueryGrid;
    private Vector3d _queryPosition;

    [GlobalSetup]
    public void Setup()
    {
        _world = new GridWorld(GridSpatialIndexBenchmarkData.HashCellSize);

        for (int z = 0; z < 16; z++)
        {
            for (int x = 0; x < 16; x++)
            {
                GridSpatialIndexBenchmarkData.AddGrid(
                    _world,
                    GridSpatialIndexBenchmarkData.CreateOrdinaryConfiguration(x * 2, z * 2));
            }
        }

        Vector3d firstLargeOrigin = default;
        for (int i = 0; i < 8; i++)
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

    [Benchmark(Description = "Point lookup in a mixed-scale grid world")]
    [BenchmarkCategory("Memory", "Lookup", "GridWorld", "SpatialIndex")]
    public ushort PointLookup()
    {
        if (!_world.TryGetGrid(_queryPosition, out VoxelGrid grid))
            throw new InvalidOperationException("Unable to resolve the mixed-scale benchmark grid.");

        return grid.GridIndex;
    }

    [Benchmark(Description = "Bounds lookup in a mixed-scale grid world")]
    [BenchmarkCategory("Memory", "Lookup", "GridWorld", "SpatialIndex")]
    public int BoundsLookup() => GridSpatialIndexBenchmarkData.CountOverlaps(_world, _boundsQueryGrid);
}

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridSpatialIndexLargeBenchmarks
{
    private GridWorld _world;
    private VoxelGrid _boundsQueryGrid;
    private Vector3d _queryPosition;

    [Params(8, 64, 256)]
    public int GridCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = new GridWorld(GridSpatialIndexBenchmarkData.HashCellSize);

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
    public int BoundsLookup() => GridSpatialIndexBenchmarkData.CountOverlaps(_world, _boundsQueryGrid);
}

internal static class GridSpatialIndexBenchmarkData
{
    public const int HashCellSize = 50;

    public static GridConfiguration CreateLargeConfiguration(Vector3d origin, int cellsPerAxis)
    {
        Fixed64 extent = (Fixed64)((cellsPerAxis - 1) * HashCellSize);
        return new GridConfiguration(
            origin,
            origin + new Vector3d(extent, extent, extent),
            topologyMetrics: GridTopologyMetrics.Rectangular((Fixed64)HashCellSize),
            storageKind: GridStorageKind.Sparse);
    }

    public static GridConfiguration CreateOrdinaryConfiguration(int x, int z) =>
        new(
            new Vector3d(x, 0, z),
            new Vector3d(x + 1, 0, z + 1),
            storageKind: GridStorageKind.Sparse);

    public static GridConfiguration CreatePointConfiguration(Vector3d position) =>
        new(position, position, storageKind: GridStorageKind.Sparse);

    public static Vector3d GetLargeGridOrigin(int index) =>
        new(10_000 + index * 1_600, 10_000, 10_000);

    public static VoxelGrid AddGrid(GridWorld world, GridConfiguration configuration)
    {
        if (!world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException($"Unable to register benchmark grid {configuration.BoundsMin} -> {configuration.BoundsMax}.");

        return world.ActiveGrids[gridIndex];
    }

    public static int CountOverlaps(GridWorld world, VoxelGrid target)
    {
        int count = 0;
        foreach (VoxelGrid _ in world.FindOverlappingGrids(target))
            count++;

        return count;
    }
}
