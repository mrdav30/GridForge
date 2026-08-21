using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
public class GridNavigationBodyTraceBenchmarks
{
    private GridWorld _world;
    private VoxelGrid _grid;
    private VoxelGrid _closureGrid;
    private SwiftList<GridNavigationBodyTraceCell> _results;
    private GridNavigationBodyTraceScratch _scratch;

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: false);
        if (!_world.TryAddGrid(
                new GridConfiguration(Vector3d.Zero, new Vector3d(2, 0, 2)),
                out ushort gridIndex))
        {
            throw new InvalidOperationException("Unable to allocate navigation-body benchmark grid.");
        }

        _grid = _world.ActiveGrids[gridIndex];
        if (!_world.TryAddGrid(
                new GridConfiguration(new Vector3d(10, 0, 0), new Vector3d(11, 1, 1)),
                out ushort closureGridIndex))
        {
            throw new InvalidOperationException("Unable to allocate closure benchmark grid.");
        }
        _closureGrid = _world.ActiveGrids[closureGridIndex];
        _results = new SwiftList<GridNavigationBodyTraceCell>(9);
        _scratch = new GridNavigationBodyTraceScratch(gridCapacity: 2, addressCapacity: 9);
        ValidateSemanticCounter(
            TraceFourCellClosure(),
            GridNavigationBodyTraceStatus.Complete,
            gridCandidateCount: 1,
            addressCandidateCount: 9,
            cellCount: 4,
            candidateWorkCount: 10L);
        ValidateSemanticCounter(
            TraceEightCellClosure(),
            GridNavigationBodyTraceStatus.Complete,
            gridCandidateCount: 1,
            addressCandidateCount: 8,
            cellCount: 8,
            candidateWorkCount: 9L);
        ValidateSemanticCounter(
            TraceLargeBody(),
            GridNavigationBodyTraceStatus.Complete,
            gridCandidateCount: 1,
            addressCandidateCount: 9,
            cellCount: 9,
            candidateWorkCount: 10L);
    }

    [GlobalCleanup]
    public void Cleanup() => BenchmarkEnvironment.ResetWorld();

    [Benchmark(Description = "TraceNavigationBodyInto rectangular four-cell closure")]
    [BenchmarkCategory("Memory", "GridTracerCoverage", "NavigationBody", "Closure")]
    public long TraceFourCellClosure()
    {
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(new VoxelIndex(0, 0, 0)),
            Address(new VoxelIndex(1, 0, 1)),
            new Vector3d(Fixed64.Zero, Fixed64.FromFraction(-1, 2), Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            _results,
            _scratch,
            addressCandidateLimit: 9,
            outputLimit: 4,
            candidateWorkLimit: 10L);
        return SemanticCounter(report);
    }

    [Benchmark(Description = "TraceNavigationBodyInto rectangular eight-cell closure")]
    [BenchmarkCategory("Memory", "GridTracerCoverage", "NavigationBody", "Closure")]
    public long TraceEightCellClosure()
    {
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            Address(_closureGrid, new VoxelIndex(0, 0, 0)),
            Address(_closureGrid, new VoxelIndex(1, 1, 1)),
            new Vector3d(new Fixed64(10), Fixed64.FromFraction(-1, 2), Fixed64.Zero),
            new Vector3d(new Fixed64(11), Fixed64.Half, Fixed64.One),
            Fixed64.Zero,
            Fixed64.One,
            _results,
            _scratch,
            addressCandidateLimit: 8,
            outputLimit: 8,
            candidateWorkLimit: 10L);
        return SemanticCounter(report);
    }

    [Benchmark(Description = "TraceNavigationBodyInto nine-cell large-body coverage")]
    [BenchmarkCategory("Memory", "GridTracerCoverage", "NavigationBody", "Large")]
    public long TraceLargeBody()
    {
        WorldVoxelIndex center = Address(new VoxelIndex(1, 0, 1));
        Vector3d foot = new(Fixed64.One, Fixed64.FromFraction(-1, 2), Fixed64.One);
        GridNavigationBodyTraceReport report = GridTracer.TraceNavigationBodyInto(
            _world,
            center,
            center,
            foot,
            foot,
            Fixed64.FromFraction(3, 4),
            Fixed64.One,
            _results,
            _scratch,
            addressCandidateLimit: 9,
            outputLimit: 9,
            candidateWorkLimit: 10L);
        return SemanticCounter(report);
    }

    private WorldVoxelIndex Address(VoxelIndex index) =>
        Address(_grid, index);

    private WorldVoxelIndex Address(VoxelGrid grid, VoxelIndex index) =>
        new(_world.SpawnToken, grid.GridIndex, grid.SpawnToken, index);

    private static long SemanticCounter(GridNavigationBodyTraceReport report) =>
        ((long)report.Status << 56)
        | ((long)report.GridCandidateCount << 40)
        | ((long)report.AddressCandidateCount << 24)
        | ((long)report.CellCount << 8)
        | report.CandidateWorkCount;

    private static void ValidateSemanticCounter(
        long actual,
        GridNavigationBodyTraceStatus status,
        int gridCandidateCount,
        int addressCandidateCount,
        int cellCount,
        long candidateWorkCount)
    {
        long expected = ((long)status << 56)
            | ((long)gridCandidateCount << 40)
            | ((long)addressCandidateCount << 24)
            | ((long)cellCount << 8)
            | candidateWorkCount;
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Navigation-body benchmark semantic counter {actual} did not match {expected}.");
        }
    }
}
