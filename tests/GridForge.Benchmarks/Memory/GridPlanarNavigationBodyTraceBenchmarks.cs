//=======================================================================
// GridPlanarNavigationBodyTraceBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
public class GridPlanarNavigationBodyTraceBenchmarks
{
    private GridWorld _world;
    private WorldVoxelIndex _center;
    private GridPlanarLayer[] _layers;
    private SwiftList<GridNavigationBodyTraceCell> _results;
    private GridNavigationBodyTraceScratch _scratch;

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: false);
        if (!_world.TryAddGrid(new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(1, 0, 1)), out ushort index))
            throw new InvalidOperationException("Unable to allocate planar coverage benchmark grid.");
        VoxelGrid grid = _world.ActiveGrids[index];
        _center = new WorldVoxelIndex(_world.SpawnToken, index, grid.SpawnToken, new VoxelIndex(1, 0, 1));
        _layers = new[] { new GridPlanarLayer(new GridCoveredAddressGeneration(grid.Configuration.ToGridKey(), index, grid.SpawnToken, grid.LastChangeSequence), 0) };
        _results = new SwiftList<GridNavigationBodyTraceCell>(16);
        _scratch = new GridNavigationBodyTraceScratch(1, 16);
        GridNavigationBodyTraceReport circle = Trace(Fixed64.Zero, Fixed64.One);
        Validate(circle, expectedCells: 9, expectedAddresses: 9);
        GridNavigationBodyTraceReport capsule = Trace(Fixed64.One, Fixed64.FromFraction(1, 4));
        Validate(capsule, expectedCells: 3, expectedAddresses: 3);
    }

    [GlobalCleanup]
    public void Cleanup() => BenchmarkEnvironment.ResetWorld();

    [Benchmark]
    [BenchmarkCategory("Memory", "GridTracerCoverage", "PlanarNavigationBody")]
    public long NineCellCircle() => SemanticCounter(Trace(Fixed64.Zero, Fixed64.One));

    [Benchmark]
    [BenchmarkCategory("Memory", "GridTracerCoverage", "PlanarNavigationBody")]
    public long ThreeCellCapsule() => SemanticCounter(Trace(Fixed64.One, Fixed64.FromFraction(1, 4)));

    private static long SemanticCounter(GridNavigationBodyTraceReport report) =>
        ((long)report.Status << 56)
        | ((long)report.GridCandidateCount << 40)
        | ((long)report.AddressCandidateCount << 24)
        | ((long)report.CellCount << 8)
        | report.CandidateWorkCount;

    private GridNavigationBodyTraceReport Trace(Fixed64 axis, Fixed64 radius) =>
        GridTracer.TracePlanarNavigationBodyInto(_world, _layers, Fixed64.Zero, _center, _center,
            Vector2d.Zero, Vector2d.Zero, axis, radius, _results, _scratch, 1, 9, 9, 10);

    private void Validate(GridNavigationBodyTraceReport report, int expectedCells, int expectedAddresses)
    {
        if (report.Status != GridNavigationBodyTraceStatus.Complete || report.GridCandidateCount != 1
            || report.AddressCandidateCount != expectedAddresses || report.CellCount != expectedCells
            || report.CandidateWorkCount != 1 + expectedAddresses
            || _results.Count != expectedCells)
            throw new InvalidOperationException("Planar benchmark did not produce the expected complete bounded evidence.");
        for (int i = 0; i < _results.Count; i++)
        {
            if (!_results[i].IsPhysicallyPresent || _results[i].Role != GridNavigationBodyTraceCellRole.RequiredCoverage)
                throw new InvalidOperationException("Planar benchmark physical evidence was not complete.");
        }
    }
}
