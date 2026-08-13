using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using SwiftCollections;

namespace GridForge.Benchmarks.Memory;

[MemoryDiagnoser]
public class ExactBoundaryContactBenchmarks
{
    private GridWorld _world;
    private VoxelGrid _source;
    private VoxelGrid _target;
    private SwiftList<VoxelContactManifold> _results;
    private GridContactQueryScratch _scratch;
    private VoxelGrid _millionCellSource;
    private VoxelGrid _singleCellTarget;
    private SwiftList<VoxelContactManifold> _millionCellResults;
    private GridContactQueryScratch _millionCellScratch;

    [GlobalSetup]
    public void Setup()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 32);
        GridConfiguration sourceConfiguration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(0, 3, 31),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        GridTopologyMetrics targetMetrics = GridTopologyMetrics.Rectangular(
            new Fixed64(2),
            Fixed64.One,
            new Fixed64(2));
        GridConfiguration targetConfiguration = new GridConfiguration(
            new Vector3d(new Fixed64(3) * Fixed64.Half, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(new Fixed64(3) * Fixed64.Half, new Fixed64(3), new Fixed64(31)),
            topologyMetrics: targetMetrics);

        if (!_world.TryAddGrid(sourceConfiguration, out ushort sourceIndex)
            || !_world.TryAddGrid(targetConfiguration, out ushort targetIndex))
        {
            throw new InvalidOperationException("Unable to initialize exact-contact benchmark grids.");
        }

        _source = _world.ActiveGrids[sourceIndex];
        _target = _world.ActiveGrids[targetIndex];
        _results = new SwiftList<VoxelContactManifold>(256);
        _scratch = new GridContactQueryScratch(256, 32);
        GridCellGeometry.GetExactBoundaryContactsInto(_source, _target, _results, _scratch);

        Vector3d millionCellMin = new Vector3d(1000, 0, 0);
        GridConfiguration millionCellConfiguration = new GridConfiguration(
            millionCellMin,
            millionCellMin + new Vector3d(99, 99, 99));
        GridConfiguration singleCellConfiguration = new GridConfiguration(
            new Vector3d(1050, 50, 50),
            new Vector3d(1050, 50, 50));
        if (!_world.TryAddGrid(millionCellConfiguration, out ushort millionCellSourceIndex)
            || !_world.TryAddGrid(singleCellConfiguration, out ushort singleCellTargetIndex))
        {
            throw new InvalidOperationException("Unable to initialize large-volume boundary benchmark grids.");
        }

        _millionCellSource = _world.ActiveGrids[millionCellSourceIndex];
        _singleCellTarget = _world.ActiveGrids[singleCellTargetIndex];
        _millionCellResults = new SwiftList<VoxelContactManifold>(32);
        _millionCellScratch = new GridContactQueryScratch(128, 8);
        GridCellGeometry.GetExactBoundaryContactsInto(
            _millionCellSource,
            _singleCellTarget,
            _millionCellResults,
            _millionCellScratch);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkEnvironment.ResetWorld();
        _world = null;
        _source = null;
        _target = null;
        _results = null;
        _scratch = null;
        _millionCellSource = null;
        _singleCellTarget = null;
        _millionCellResults = null;
        _millionCellScratch = null;
    }

    [Benchmark(Description = "Broad-phase plus exact rectangular boundary contacts")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact", "Exact")]
    public int RectangularDifferingMetricContacts() =>
        GridCellGeometry.GetExactBoundaryContactsInto(_source, _target, _results, _scratch);

    [Benchmark(Description = "One-million-cell dense grid with embedded one-cell contact patch")]
    [BenchmarkCategory("Memory", "Neighbors", "Contact", "Exact", "Scale")]
    public int MillionCellDenseSmallContactPatch() =>
        GridCellGeometry.GetExactBoundaryContactsInto(
            _millionCellSource,
            _singleCellTarget,
            _millionCellResults,
            _millionCellScratch);
}
