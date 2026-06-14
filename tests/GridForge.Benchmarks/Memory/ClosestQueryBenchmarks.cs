using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class ClosestQueryBenchmarks
{
    private const int GridSide = 64;
    private const int GridExtent = GridSide - 1;
    private const int QueryCount = 1 << 15;
    private const int SparseOriginX = 128;
    private const int LargeSparseSide = 256;
    private const int LargeSparseExtent = LargeSparseSide - 1;
    private const int LargeSparseOriginX = 512;
    private const int TileOriginX = 256;
    private const int TileColumns = 8;
    private const int TileRows = 8;
    private const int TileExtent = 15;
    private const int TileStride = TileExtent + 1;

    private GridWorld _world;
    private GridWorld _largeSparseWorld;
    private VoxelGrid _denseGrid;
    private VoxelGrid _sparseGrid;
    private VoxelGrid _largeSparseGrid;
    private Vector3d[] _denseQueries;
    private Vector3d[] _sparseQueries;
    private Vector3d[] _largeSparseQueries;
    private Vector3d[] _worldQueries;
    private VoxelIndex[] _sparseVoxels;
    private VoxelIndex[] _largeSparseVoxels;
    private Voxel[] _largeSparseLinearScanVoxels;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _world = BenchmarkEnvironment.PrepareWorld(spatialGridCellSize: 32);
        _largeSparseWorld = new GridWorld(spatialGridCellSize: 32);
        _sparseVoxels = CreateStridePattern(stride: 4);
        _largeSparseVoxels = CreateStridePattern(LargeSparseSide, stride: 4);

        InitializeDenseGrid();
        InitializeSparseGrid();
        InitializeLargeSparseGrid();
        InitializeTiledWorldGrids();

        _denseQueries = CreateQueryPositions(0, 0, GridSide, GridSide);
        _sparseQueries = CreateQueryPositions(SparseOriginX, 0, GridSide, GridSide);
        _largeSparseQueries = CreateQueryPositions(LargeSparseOriginX, 0, LargeSparseSide, LargeSparseSide);
        _worldQueries = CreateQueryPositions(
            TileOriginX,
            0,
            TileColumns * TileStride,
            TileRows * TileStride);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _largeSparseWorld.Dispose();
        BenchmarkEnvironment.ResetWorld();
        _world = null;
        _largeSparseWorld = null;
        _denseGrid = null;
        _sparseGrid = null;
        _largeSparseGrid = null;
        _denseQueries = null;
        _sparseQueries = null;
        _largeSparseQueries = null;
        _worldQueries = null;
        _sparseVoxels = null;
        _largeSparseVoxels = null;
        _largeSparseLinearScanVoxels = null;
    }

    [Benchmark(Description = "Dense closest voxel queries", OperationsPerInvoke = QueryCount)]
    [BenchmarkCategory("ClosestQuery", "Dense")]
    public int DenseClosestVoxel()
    {
        int checksum = 0;

        for (int i = 0; i < _denseQueries.Length; i++)
        {
            if (_denseGrid.TryGetClosestVoxel(_denseQueries[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Sparse closest configured voxel queries", OperationsPerInvoke = QueryCount)]
    [BenchmarkCategory("ClosestQuery", "Sparse")]
    public int SparseClosestVoxel()
    {
        int checksum = 0;

        for (int i = 0; i < _sparseQueries.Length; i++)
        {
            if (_sparseGrid.TryGetClosestVoxel(_sparseQueries[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Large sparse closest configured voxel queries", OperationsPerInvoke = QueryCount)]
    [BenchmarkCategory("ClosestQuery", "Sparse")]
    public int LargeSparseClosestVoxel()
    {
        int checksum = 0;

        for (int i = 0; i < _largeSparseQueries.Length; i++)
        {
            if (_largeSparseGrid.TryGetClosestVoxel(_largeSparseQueries[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Large sparse linear scan baseline", OperationsPerInvoke = QueryCount)]
    [BenchmarkCategory("ClosestQuery", "Sparse", "Baseline")]
    public int LargeSparseClosestVoxelLinearScanBaseline()
    {
        int checksum = 0;

        for (int i = 0; i < _largeSparseQueries.Length; i++)
        {
            Voxel voxel = GetClosestVoxelByLinearScan(_largeSparseLinearScanVoxels, _largeSparseQueries[i]);
            checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "World closest grid bounds queries", OperationsPerInvoke = QueryCount)]
    [BenchmarkCategory("ClosestQuery", "World")]
    public int WorldClosestGrid()
    {
        int checksum = 0;

        for (int i = 0; i < _worldQueries.Length; i++)
        {
            if (_world.TryGetClosestGrid(_worldQueries[i], out VoxelGrid grid))
                checksum += grid.GridIndex;
        }

        return checksum;
    }

    [Benchmark(Description = "World closest grid and voxel queries", OperationsPerInvoke = QueryCount)]
    [BenchmarkCategory("ClosestQuery", "World")]
    public int WorldClosestGridAndVoxel()
    {
        int checksum = 0;

        for (int i = 0; i < _worldQueries.Length; i++)
        {
            if (_world.TryGetClosestGridAndVoxel(_worldQueries[i], out VoxelGrid grid, out Voxel voxel))
                checksum += grid.GridIndex + voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    private void InitializeDenseGrid()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(GridExtent, 0, GridExtent),
            scanCellSize: 8);

        if (!_world.TryAddGrid(configuration, out ushort gridIndex))
            throw new InvalidOperationException("Failed to create dense closest-query benchmark grid.");

        _denseGrid = _world.ActiveGrids[gridIndex];
    }

    private void InitializeSparseGrid()
    {
        GridConfiguration configuration = new(
            new Vector3d(SparseOriginX, 0, 0),
            new Vector3d(SparseOriginX + GridExtent, 0, GridExtent),
            scanCellSize: 8,
            storageKind: GridStorageKind.Sparse);

        if (!_world.TryAddGrid(configuration, _sparseVoxels, out ushort gridIndex))
            throw new InvalidOperationException("Failed to create sparse closest-query benchmark grid.");

        _sparseGrid = _world.ActiveGrids[gridIndex];
    }

    private void InitializeLargeSparseGrid()
    {
        GridConfiguration configuration = new(
            new Vector3d(LargeSparseOriginX, 0, 0),
            new Vector3d(LargeSparseOriginX + LargeSparseExtent, 0, LargeSparseExtent),
            scanCellSize: 8,
            storageKind: GridStorageKind.Sparse);

        if (!_largeSparseWorld.TryAddGrid(configuration, _largeSparseVoxels, out ushort gridIndex))
            throw new InvalidOperationException("Failed to create large sparse closest-query benchmark grid.");

        _largeSparseGrid = _largeSparseWorld.ActiveGrids[gridIndex];
        _largeSparseLinearScanVoxels = CaptureVoxels(_largeSparseGrid);
    }

    private void InitializeTiledWorldGrids()
    {
        GridConfiguration[] configurations = BenchmarkScenarioFactory.CreateTiledFlatGridConfigurations(
            TileColumns,
            TileRows,
            TileExtent,
            scanCellSize: 8,
            originX: TileOriginX,
            originZ: 0);

        for (int i = 0; i < configurations.Length; i++)
        {
            if (!_world.TryAddGrid(configurations[i], out _))
                throw new InvalidOperationException("Failed to create tiled closest-query benchmark grid.");
        }
    }

    private static VoxelIndex[] CreateStridePattern(int stride)
    {
        return CreateStridePattern(GridSide, stride);
    }

    private static VoxelIndex[] CreateStridePattern(int side, int stride)
    {
        int sideCount = (side + stride - 1) / stride;
        VoxelIndex[] voxels = new VoxelIndex[sideCount * sideCount];
        int index = 0;

        for (int x = 0; x < side; x += stride)
        {
            for (int z = 0; z < side; z += stride)
                voxels[index++] = new VoxelIndex(x, 0, z);
        }

        return voxels;
    }

    private static Vector3d[] CreateQueryPositions(
        int originX,
        int originZ,
        int width,
        int length)
    {
        Vector3d[] queries = new Vector3d[QueryCount];

        for (int i = 0; i < queries.Length; i++)
        {
            int x = originX + (i * 17) % width;
            int z = originZ + (i * 31) % length;
            queries[i] = Vector3d.FromDouble(x + 0.75, 0, z + 0.25);
        }

        return queries;
    }

    private static Voxel[] CaptureVoxels(VoxelGrid grid)
    {
        Voxel[] voxels = new Voxel[grid.ConfiguredVoxelCount];
        int index = 0;
        foreach (Voxel voxel in grid.EnumerateVoxels())
            voxels[index++] = voxel;

        return voxels;
    }

    private static Voxel GetClosestVoxelByLinearScan(Voxel[] voxels, Vector3d position)
    {
        Voxel closest = voxels[0];
        Fixed64 closestDistanceSquared = (closest.WorldPosition - position).MagnitudeSquared;

        for (int i = 1; i < voxels.Length; i++)
        {
            Voxel candidate = voxels[i];
            Fixed64 candidateDistanceSquared = (candidate.WorldPosition - position).MagnitudeSquared;
            if (candidateDistanceSquared < closestDistanceSquared
                || (candidateDistanceSquared == closestDistanceSquared
                    && CompareVoxelIndices(candidate.Index, closest.Index) < 0))
            {
                closest = candidate;
                closestDistanceSquared = candidateDistanceSquared;
            }
        }

        return closest;
    }

    private static int CompareVoxelIndices(VoxelIndex left, VoxelIndex right)
    {
        int result = left.x.CompareTo(right.x);
        if (result != 0)
            return result;

        result = left.y.CompareTo(right.y);
        return result != 0 ? result : left.z.CompareTo(right.z);
    }
}
