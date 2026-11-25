using System;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class Vector2dLookupBenchmarks
{
    private GridWorld _world;
    private Vector3d[] _positions3D;
    private Vector2d[] _positions2D;
    private GridConfiguration[] _areaConfigurations;
    private ushort[] _areaGridSlots;
    private ScanCell[] _areaExpectedCells;
    private SwiftList<ScanCell> _areaResults;
    private GridScanScratch _areaScratch;
    private static readonly VoxelIndex[] _areaVoxelIndices = new[] { new VoxelIndex(0, 0, 0) };

    public int QueryCount { get; set; } = 1024;

    public int AreaQueryRounds { get; set; } = 4;

    [IterationSetup(Targets = new[] { nameof(TryGetVoxel_Vector3d), nameof(TryGetVoxel_Vector2d) })]
    public void SetupIteration()
    {
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: false);

        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            tilesX: 2,
            tilesZ: 2,
            extent: 63,
            scanCellSize: 8);

        for (int i = 0; i < configurations.Length; i++)
        {
            if (!_world.TryAddGrid(configurations[i], out _))
                throw new InvalidOperationException($"Unable to allocate lookup benchmark grid {i}.");
        }

        _positions3D = new Vector3d[QueryCount];
        _positions2D = new Vector2d[QueryCount];

        for (int i = 0; i < QueryCount; i++)
        {
            int x = i & 127;
            int z = (i * 17) & 127;
            _positions3D[i] = new Vector3d(x, 0, z);
            _positions2D[i] = new Vector2d(x, z);
        }
    }

    [IterationSetup(Targets = new[] { nameof(QueryFlatPlanarAreas), nameof(ReloadAndQueryFlatPlanarAreas) })]
    public void SetupFlatAreaIteration() => InitializeAreaScenario(1);

    [IterationSetup(Targets = new[] { nameof(QueryStackedPlanarAreas), nameof(ReloadAndQueryStackedPlanarAreas) })]
    public void SetupStackedAreaIteration() => InitializeAreaScenario(8);

    [IterationCleanup]
    public void CleanupIteration()
    {
        BenchmarkEnvironment.ResetWorld();
    }

    [Benchmark(Baseline = true, Description = "TryGetVoxel Vector3d lookups")]
    [BenchmarkCategory("Lookup", "Vector3d")]
    public int TryGetVoxel_Vector3d()
    {
        int checksum = 0;

        for (int i = 0; i < _positions3D.Length; i++)
        {
            if (_world.TryGetVoxel(_positions3D[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "TryGetVoxel Vector2d lookups")]
    [BenchmarkCategory("Lookup", "Vector2d")]
    public int TryGetVoxel_Vector2d()
    {
        int checksum = 0;

        for (int i = 0; i < _positions2D.Length; i++)
        {
            if (_world.TryGetVoxel(_positions2D[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Query planar/3D areas in 64 flat sparse grids")]
    [BenchmarkCategory("Lookup", "PlanarArea", "SpatialIndex")]
    public int QueryFlatPlanarAreas() => ExecuteAreaQueries(AreaQueryRounds);

    [Benchmark(Description = "Query planar/3D areas in 64 footprints x 8 layers")]
    [BenchmarkCategory("Lookup", "PlanarArea", "SpatialIndex")]
    public int QueryStackedPlanarAreas() => ExecuteAreaQueries(AreaQueryRounds);

    [Benchmark(Description = "Reload 64 flat grids, then query planar/3D areas")]
    [BenchmarkCategory("Registration", "PlanarArea", "SpatialIndex")]
    public int ReloadAndQueryFlatPlanarAreas() => ReloadAndQueryAreas();

    [Benchmark(Description = "Reload 512 stacked grids, then query planar/3D areas")]
    [BenchmarkCategory("Registration", "PlanarArea", "SpatialIndex")]
    public int ReloadAndQueryStackedPlanarAreas() => ReloadAndQueryAreas();

    private void InitializeAreaScenario(int layers)
    {
        const int footprints = 64;
        _world = BenchmarkEnvironment.PrepareWorld(clearAllPools: true);
        _areaConfigurations = new GridConfiguration[footprints * layers];
        _areaGridSlots = new ushort[_areaConfigurations.Length];
        _areaExpectedCells = new ScanCell[footprints];
        _areaResults = new SwiftList<ScanCell>(1);
        _areaScratch = new GridScanScratch();
        for (int layer = 0; layer < layers; layer++)
        {
            for (int tile = 0; tile < footprints; tile++)
            {
                Vector3d min = new Vector3d(tile % 8 * 100, layer * 100, tile / 8 * 100);
                _areaConfigurations[layer * footprints + tile] = new GridConfiguration(
                    min, min + new Vector3d(1, 0, 1), storageKind: GridStorageKind.Sparse);
            }
        }
        RegisterAreaGrids();
        ExecuteAreaQueries(1);
    }

    private void RegisterAreaGrids()
    {
        for (int i = 0; i < _areaConfigurations.Length; i++)
        {
            if (!_world.TryAddGrid(_areaConfigurations[i], _areaVoxelIndices, out _areaGridSlots[i]))
                throw new InvalidOperationException($"Unable to register area benchmark grid {i}.");
        }
        // Resolve the physical expected cells independently of tracer output.
        for (int tile = 0; tile < _areaExpectedCells.Length; tile++)
        {
            VoxelGrid grid = _world.ActiveGrids[_areaGridSlots[tile]];
            if (!grid.TryGetVoxel(_areaVoxelIndices[0], out Voxel voxel)
                || !grid.TryGetScanCell(voxel.ScanCellKey, out _areaExpectedCells[tile]))
                throw new InvalidOperationException("Unable to resolve expected area scan cell.");
        }
    }

    private int ReloadAndQueryAreas()
    {
        foreach (ushort slot in _areaGridSlots)
        {
            if (!_world.TryRemoveGrid(slot))
                throw new InvalidOperationException("Unable to remove area benchmark grid.");
        }
        RegisterAreaGrids();
        return ExecuteAreaQueries(AreaQueryRounds);
    }

    private int ExecuteAreaQueries(int rounds)
    {
        Fixed64 quarter = Fixed64.One / (Fixed64)4;
        int hits = 0;
        for (int round = 0; round < rounds; round++)
        {
            for (int tile = 0; tile < _areaExpectedCells.Length; tile++)
            {
                Vector2d center = new Vector2d(tile % 8 * 100, tile / 8 * 100);
                Vector2d min = new Vector2d(center.X - quarter, center.Y - quarter);
                Vector2d max = new Vector2d(center.X + quarter, center.Y + quarter);
                GridTracer.GetCoveredScanCellsInto(_world, min, max, _areaResults, _areaScratch, layerY: Fixed64.Zero);
                ValidateAreaResult(tile);
                hits++;
                GridTracer.GetCoveredScanCellsInto(_world,
                    new Vector3d(min.X, -quarter, min.Y), new Vector3d(max.X, quarter, max.Y),
                    _areaResults, _areaScratch);
                ValidateAreaResult(tile);
                hits++;
            }
        }
        return hits;
    }

    private void ValidateAreaResult(int tile)
    {
        if (_areaResults.Count != 1 || !ReferenceEquals(_areaResults[0], _areaExpectedCells[tile]))
            throw new InvalidOperationException("Area coverage changed physical cell, layer, order or count.");
    }

}
