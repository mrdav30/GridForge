using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Diagnostics;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class GridDiagnosticsBenchmarks
{
    private const int RectangularSide = 64;
    private const int RectangularExtent = RectangularSide - 1;
    private const int HexSide = 32;
    private const int HexExtent = HexSide - 1;
    private const int QueryRounds = 256;

    private readonly GridTopologyMetrics _hexMetrics = GridTopologyMetrics.Hex(
        new Fixed64(2),
        Fixed64.One,
        HexOrientation.PointyTop);

    private VoxelIndex[] _sparseRectangularVoxels;
    private VoxelIndex[] _sparseHexVoxels;
    private GridWorld _world;
    private VoxelGrid _denseRectangularGrid;
    private VoxelGrid _sparseRectangularGrid;
    private VoxelGrid _denseHexGrid;
    private VoxelGrid _sparseHexGrid;
    private GridDiagnosticQuery _denseRectangularQuery;
    private GridDiagnosticQuery _sparseRectangularQuery;
    private GridDiagnosticQuery _denseHexQuery;
    private GridDiagnosticQuery _sparseHexQuery;
    private GridDiagnosticQuery _boundedSparseMissingQuery;
    private SwiftList<GridDiagnosticCell> _results;
    private GridDiagnosticScratch _scratch;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _sparseRectangularVoxels = CreateRectangularStridePattern(stride: 4);
        _sparseHexVoxels = CreateHexStridePattern(stride: 2);
    }

    [IterationSetup]
    public void SetupIteration()
    {
        InitializeScenario();
        _results = new SwiftList<GridDiagnosticCell>(GridDiagnosticQuery.DefaultMaxCells);
        _scratch = new GridDiagnosticScratch();

        WarmQuery(_denseRectangularQuery);
        WarmQuery(_sparseRectangularQuery);
        WarmQuery(_denseHexQuery);
        WarmQuery(_sparseHexQuery);
        WarmQuery(_boundedSparseMissingQuery);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _results?.Clear();
        _scratch?.Clear();
        BenchmarkEnvironment.ResetWorld();
        _results = null;
        _scratch = null;
    }

    [Benchmark(Baseline = true, Description = "Dense rectangular physical cells into caller-owned list")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Rectangular", "GetCellsInto")]
    public int DenseRectangularPhysical_GetCellsInto() => ExecuteGetCellsInto(_denseRectangularQuery);

    [Benchmark(Description = "Sparse rectangular physical cells into caller-owned list")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Rectangular", "Sparse", "GetCellsInto")]
    public int SparseRectangularPhysical_GetCellsInto() => ExecuteGetCellsInto(_sparseRectangularQuery);

    [Benchmark(Description = "Dense hex physical cells into caller-owned list")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Hex", "GetCellsInto")]
    public int DenseHexPhysical_GetCellsInto() => ExecuteGetCellsInto(_denseHexQuery);

    [Benchmark(Description = "Sparse hex physical cells into caller-owned list")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Hex", "Sparse", "GetCellsInto")]
    public int SparseHexPhysical_GetCellsInto() => ExecuteGetCellsInto(_sparseHexQuery);

    [Benchmark(Description = "Bounded sparse missing address cells into caller-owned list")]
    [BenchmarkCategory("GridDiagnostics", "MissingSparseAddress", "Sparse", "GetCellsInto")]
    public int BoundedSparseMissing_GetCellsInto() => ExecuteGetCellsInto(_boundedSparseMissingQuery);

    [Benchmark(Description = "Dense rectangular physical cells through visitor")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Rectangular", "VisitCells")]
    public int DenseRectangularPhysical_VisitCells() => ExecuteVisitCells(_denseRectangularQuery);

    [Benchmark(Description = "Sparse rectangular physical cells through visitor")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Rectangular", "Sparse", "VisitCells")]
    public int SparseRectangularPhysical_VisitCells() => ExecuteVisitCells(_sparseRectangularQuery);

    [Benchmark(Description = "Dense hex physical cells through visitor")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Hex", "VisitCells")]
    public int DenseHexPhysical_VisitCells() => ExecuteVisitCells(_denseHexQuery);

    [Benchmark(Description = "Sparse hex physical cells through visitor")]
    [BenchmarkCategory("GridDiagnostics", "Physical", "Hex", "Sparse", "VisitCells")]
    public int SparseHexPhysical_VisitCells() => ExecuteVisitCells(_sparseHexQuery);

    [Benchmark(Description = "Bounded sparse missing address cells through visitor")]
    [BenchmarkCategory("GridDiagnostics", "MissingSparseAddress", "Sparse", "VisitCells")]
    public int BoundedSparseMissing_VisitCells() => ExecuteVisitCells(_boundedSparseMissingQuery);

    private void InitializeScenario()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 128);

        _denseRectangularGrid = AddGrid(new GridConfiguration(
            new Vector3d(0, 0, 0),
            new Vector3d(RectangularExtent, 0, RectangularExtent),
            scanCellSize: 8));

        _sparseRectangularGrid = AddGrid(
            new GridConfiguration(
                new Vector3d(128, 0, 0),
                new Vector3d(128 + RectangularExtent, 0, RectangularExtent),
                scanCellSize: 8,
                storageKind: GridStorageKind.Sparse),
            _sparseRectangularVoxels);

        _denseHexGrid = AddGrid(CreateHexConfiguration(
            new Vector3d(320, 0, 0),
            storageKind: GridStorageKind.Dense));

        _sparseHexGrid = AddGrid(
            CreateHexConfiguration(
                new Vector3d(560, 0, 0),
                storageKind: GridStorageKind.Sparse),
            _sparseHexVoxels);

        _denseRectangularQuery = GridDiagnosticQuery.ForGrid(_denseRectangularGrid.GridIndex);
        _sparseRectangularQuery = GridDiagnosticQuery.ForGrid(_sparseRectangularGrid.GridIndex);
        _denseHexQuery = GridDiagnosticQuery.ForGrid(_denseHexGrid.GridIndex);
        _sparseHexQuery = GridDiagnosticQuery.ForGrid(_sparseHexGrid.GridIndex);
        _boundedSparseMissingQuery = CreateBoundedMissingQuery(_sparseRectangularGrid);
    }

    private VoxelGrid AddGrid(GridConfiguration configuration)
    {
        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate diagnostic benchmark grid.");

        return _world.ActiveGrids[gridIndex];
    }

    private VoxelGrid AddGrid(
        GridConfiguration configuration,
        VoxelIndex[] configuredVoxels)
    {
        if (!_world.TryAddGrid(configuration, configuredVoxels, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate sparse diagnostic benchmark grid.");

        return _world.ActiveGrids[gridIndex];
    }

    private void WarmQuery(in GridDiagnosticQuery query)
    {
        GridDiagnostics.GetCellsInto(_world, query, _results, _scratch);
        _results.Clear();
        _scratch.Clear();
    }

    private int ExecuteGetCellsInto(in GridDiagnosticQuery query)
    {
        int checksum = 0;
        for (int round = 0; round < QueryRounds; round++)
        {
            GridDiagnosticQueryResult result = GridDiagnostics.GetCellsInto(_world, query, _results, _scratch);
            checksum += result.CellCount + result.SkippedCellCount;

            for (int i = 0; i < _results.Count; i++)
                checksum += GetCellChecksum(_results[i]);

            _results.Clear();
            _scratch.Clear();
        }

        return checksum;
    }

    private int ExecuteVisitCells(in GridDiagnosticQuery query)
    {
        int checksum = 0;
        for (int round = 0; round < QueryRounds; round++)
        {
            ChecksumVisitor visitor = new();
            GridDiagnosticQueryResult result = GridDiagnostics.VisitCells(_world, query, ref visitor, _scratch);
            checksum += visitor.Checksum + result.CellCount + result.SkippedCellCount;
            _scratch.Clear();
        }

        return checksum;
    }

    private GridDiagnosticQuery CreateBoundedMissingQuery(VoxelGrid grid)
    {
        Vector3d boundsMin = grid.GetWorldPosition(new VoxelIndex(8, 0, 8));
        Vector3d boundsMax = grid.GetWorldPosition(new VoxelIndex(23, 0, 23));
        return new GridDiagnosticQuery(
            gridIndex: grid.GridIndex,
            addressMode: GridDiagnosticAddressMode.MissingOnly,
            boundsMin: boundsMin,
            boundsMax: boundsMax);
    }

    private GridConfiguration CreateHexConfiguration(
        Vector3d boundsMin,
        GridStorageKind storageKind)
    {
        Vector3d boundsMax = boundsMin + HexCoordinateUtility.AxialToWorldOffset(
            new VoxelIndex(HexExtent, 0, HexExtent),
            _hexMetrics);

        return new GridConfiguration(
            boundsMin,
            boundsMax,
            scanCellSize: 8,
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: _hexMetrics,
            storageKind: storageKind);
    }

    private static VoxelIndex[] CreateRectangularStridePattern(int stride)
    {
        int countPerAxis = (RectangularSide + stride - 1) / stride;
        VoxelIndex[] voxels = new VoxelIndex[countPerAxis * countPerAxis];
        int index = 0;

        for (int x = 0; x < RectangularSide; x += stride)
        {
            for (int z = 0; z < RectangularSide; z += stride)
                voxels[index++] = new VoxelIndex(x, 0, z);
        }

        return voxels;
    }

    private static VoxelIndex[] CreateHexStridePattern(int stride)
    {
        int countPerAxis = (HexSide + stride - 1) / stride;
        VoxelIndex[] voxels = new VoxelIndex[countPerAxis * countPerAxis];
        int index = 0;

        for (int q = 0; q < HexSide; q += stride)
        {
            for (int r = 0; r < HexSide; r += stride)
                voxels[index++] = new VoxelIndex(q, 0, r);
        }

        return voxels;
    }

    private static int GetCellChecksum(in GridDiagnosticCell cell) =>
        cell.Index.x
        + cell.Index.y
        + cell.Index.z
        + cell.WorldPosition.X.FloorToInt()
        + cell.WorldPosition.Z.FloorToInt()
        + (int)cell.Kind
        + (int)cell.State;

    private struct ChecksumVisitor : IGridDiagnosticCellVisitor
    {
        public int Checksum;

        public bool Visit(in GridDiagnosticCell cell)
        {
            Checksum += GetCellChecksum(in cell);
            return true;
        }
    }
}
