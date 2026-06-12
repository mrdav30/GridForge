using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using FixedMathSharp.Bounds;
using GridForge.Blockers;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using System;

namespace GridForge.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(InProcessShortRunConfig))]
public class SparseVoxelGridBenchmarks
{
    private const int GridSide = 64;
    private const int GridExtent = GridSide - 1;
    private const int ConstructionRounds = 64;
    private const int LookupCount = 1 << 20;
    private const int CoverageRounds = 512;
    private const int BlockerRounds = 256;
    private const int RegistrationRounds = 32;
    private const int ScanRounds = 512;
    private const int NeighborRounds = 32768;

    private readonly GridConfiguration _denseConfiguration = new(
        new Vector3d(0, 0, 0),
        new Vector3d(GridExtent, 0, GridExtent),
        scanCellSize: 8);

    private readonly GridConfiguration _sparseConfiguration = new(
        new Vector3d(0, 0, 0),
        new Vector3d(GridExtent, 0, GridExtent),
        scanCellSize: 8,
        storageKind: GridStorageKind.Sparse);

    private VoxelIndex[] _lowDensityVoxels;
    private VoxelIndex[] _mediumDensityVoxels;
    private VoxelIndex[] _highDensityVoxels;
    private VoxelIndex[] _clusteredVoxels;
    private VoxelIndex[] _occupantVoxels;
    private VoxelIndex[] _lookupIndices;
    private BenchmarkOccupant[] _occupants;
    private Voxel[] _neighborVoxels;
    private GridWorld _world;
    private VoxelGrid _grid;
    private BoundsBlocker _blocker;
    private SwiftList<IVoxelOccupant> _scanResults;
    private GridScanScratch _scanScratch;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _lowDensityVoxels = CreateStridePattern(stride: 8);
        _mediumDensityVoxels = CreateStridePattern(stride: 4);
        _highDensityVoxels = CreateCheckerboardPattern();
        _clusteredVoxels = CreateCluster(startX: 8, startZ: 8, width: 32, length: 32);
        _occupantVoxels = CreateCluster(startX: 16, startZ: 16, width: 24, length: 24);
        _lookupIndices = CreateLookupIndices();
        _occupants = CreateOccupants(_occupantVoxels);
    }

    [IterationSetup(Target = nameof(ConstructDenseGrid))]
    public void SetupConstructDenseIteration()
    {
        _world = BenchmarkEnvironment.PrepareWorld();
    }

    [IterationSetup(Target = nameof(ConstructSparseLowDensityGrid))]
    public void SetupConstructSparseLowIteration()
    {
        _world = BenchmarkEnvironment.PrepareWorld();
    }

    [IterationSetup(Target = nameof(ConstructSparseMediumDensityGrid))]
    public void SetupConstructSparseMediumIteration()
    {
        _world = BenchmarkEnvironment.PrepareWorld();
    }

    [IterationSetup(Target = nameof(ConstructSparseHighDensityGrid))]
    public void SetupConstructSparseHighIteration()
    {
        _world = BenchmarkEnvironment.PrepareWorld();
    }

    [IterationSetup(Target = nameof(LookupDenseGridVoxels))]
    public void SetupDenseLookupIteration()
    {
        InitializeDenseGrid();
    }

    [IterationSetup(Target = nameof(LookupSparseConfiguredVoxels))]
    public void SetupSparseConfiguredLookupIteration()
    {
        InitializeSparseGrid(_mediumDensityVoxels);
    }

    [IterationSetup(Target = nameof(LookupSparseMixedVoxels))]
    public void SetupSparseMixedLookupIteration()
    {
        InitializeSparseGrid(_mediumDensityVoxels);
    }

    [IterationSetup(Target = nameof(CoverSparseEmptyRegion))]
    public void SetupSparseEmptyCoverageIteration()
    {
        InitializeSparseGrid(_clusteredVoxels);
    }

    [IterationSetup(Target = nameof(CoverSparseClusteredRegion))]
    public void SetupSparseClusteredCoverageIteration()
    {
        InitializeSparseGrid(_clusteredVoxels);
    }

    [IterationSetup(Target = nameof(ApplyRemoveSparseBlocker))]
    public void SetupSparseBlockerIteration()
    {
        InitializeSparseGrid(_clusteredVoxels);
        FixedBoundArea bounds = new(new Vector3d(12, 0, 12), new Vector3d(36, 0, 36));
        _blocker = new BoundsBlocker(_world, bounds, cacheCoveredVoxels: true);
    }

    [IterationSetup(Target = nameof(RegisterRemoveSparseOccupants))]
    public void SetupSparseOccupantRegistrationIteration()
    {
        InitializeSparseGrid(_occupantVoxels);
    }

    [IterationSetup(Target = nameof(ScanSparseRadiusInto))]
    public void SetupSparseScanIteration()
    {
        InitializeSparseGrid(_occupantVoxels);
        RegisterOccupants();

        _scanResults = new SwiftList<IVoxelOccupant>();
        _scanScratch = new GridScanScratch();

        ExecuteSparseRadiusScanInto();
        _scanResults.Clear();
        _scanScratch.Clear();
    }

    [IterationSetup(Target = nameof(ResolveDenseToSparseNeighbors_Uncached))]
    public void SetupDenseSparseNeighborUncachedIteration()
    {
        InitializeDenseSparseNeighborScenario(primeCache: false);
    }

    [IterationSetup(Target = nameof(ResolveDenseToSparseNeighbors_Cached))]
    public void SetupDenseSparseNeighborCachedIteration()
    {
        InitializeDenseSparseNeighborScenario(primeCache: true);
    }

    [IterationCleanup]
    public void CleanupIteration()
    {
        _blocker?.Reset();
        _scanResults?.Clear();
        _scanScratch?.Clear();
        BenchmarkEnvironment.ResetWorld();
        _blocker = null;
        _scanResults = null;
        _scanScratch = null;
    }

    [Benchmark(Description = "Construct and remove dense 64x64 flat grid")]
    [BenchmarkCategory("Sparse", "Construction", "DenseBaseline")]
    public int ConstructDenseGrid()
    {
        return ConstructGrid(_denseConfiguration, null);
    }

    [Benchmark(Description = "Construct and remove sparse 64x64 grid at low density")]
    [BenchmarkCategory("Sparse", "Construction")]
    public int ConstructSparseLowDensityGrid()
    {
        return ConstructGrid(_sparseConfiguration, _lowDensityVoxels);
    }

    [Benchmark(Description = "Construct and remove sparse 64x64 grid at medium density")]
    [BenchmarkCategory("Sparse", "Construction")]
    public int ConstructSparseMediumDensityGrid()
    {
        return ConstructGrid(_sparseConfiguration, _mediumDensityVoxels);
    }

    [Benchmark(Description = "Construct and remove sparse 64x64 grid at high density")]
    [BenchmarkCategory("Sparse", "Construction")]
    public int ConstructSparseHighDensityGrid()
    {
        return ConstructGrid(_sparseConfiguration, _highDensityVoxels);
    }

    [Benchmark(Description = "Dense voxel index lookups")]
    [BenchmarkCategory("Sparse", "Lookup", "DenseBaseline")]
    public int LookupDenseGridVoxels()
    {
        int checksum = 0;

        for (int i = 0; i < _lookupIndices.Length; i++)
        {
            if (_grid.TryGetVoxel(_lookupIndices[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Sparse configured voxel index lookups")]
    [BenchmarkCategory("Sparse", "Lookup")]
    public int LookupSparseConfiguredVoxels()
    {
        int checksum = 0;

        for (int i = 0; i < _lookupIndices.Length; i++)
        {
            VoxelIndex index = _mediumDensityVoxels[i & (_mediumDensityVoxels.Length - 1)];
            if (_grid.TryGetVoxel(index, out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Sparse mixed configured and missing voxel index lookups")]
    [BenchmarkCategory("Sparse", "Lookup")]
    public int LookupSparseMixedVoxels()
    {
        int checksum = 0;

        for (int i = 0; i < _lookupIndices.Length; i++)
        {
            if (_grid.TryGetVoxel(_lookupIndices[i], out Voxel voxel))
                checksum += voxel.Index.x + voxel.Index.z;
        }

        return checksum;
    }

    [Benchmark(Description = "Sparse coverage over empty configured region")]
    [BenchmarkCategory("Sparse", "Coverage")]
    public int CoverSparseEmptyRegion()
    {
        return CountCoveredVoxels(new Vector3d(48, 0, 48), new Vector3d(62, 0, 62));
    }

    [Benchmark(Description = "Sparse coverage over clustered configured region")]
    [BenchmarkCategory("Sparse", "Coverage")]
    public int CoverSparseClusteredRegion()
    {
        return CountCoveredVoxels(new Vector3d(12, 0, 12), new Vector3d(36, 0, 36));
    }

    [Benchmark(Description = "Apply and remove cached sparse blocker")]
    [BenchmarkCategory("Sparse", "Blocker")]
    public int ApplyRemoveSparseBlocker()
    {
        int blocked = 0;

        for (int i = 0; i < BlockerRounds; i++)
        {
            _blocker.ApplyBlockage();
            if (_blocker.IsBlocking)
                blocked++;

            _blocker.RemoveBlockage();
        }

        return blocked;
    }

    [Benchmark(Description = "Register and remove sparse occupants")]
    [BenchmarkCategory("Sparse", "Occupants")]
    public int RegisterRemoveSparseOccupants()
    {
        int operations = 0;

        for (int round = 0; round < RegistrationRounds; round++)
        {
            for (int i = 0; i < _occupants.Length; i++)
            {
                if (!_grid.TryAddVoxelOccupant(_occupants[i]))
                    throw new InvalidOperationException($"Unable to add sparse occupant {i}.");

                operations++;
            }

            for (int i = 0; i < _occupants.Length; i++)
            {
                if (!_grid.TryRemoveVoxelOccupant(_occupants[i]))
                    throw new InvalidOperationException($"Unable to remove sparse occupant {i}.");

                operations++;
            }
        }

        return operations;
    }

    [Benchmark(Description = "Sparse radius scan into caller-owned scratch")]
    [BenchmarkCategory("Sparse", "ScanQuery", "Scratch")]
    public int ScanSparseRadiusInto()
    {
        return ExecuteSparseRadiusScanInto();
    }

    [Benchmark(Description = "Dense to sparse conjoined neighbor lookups without cache")]
    [BenchmarkCategory("Sparse", "Neighbors")]
    public int ResolveDenseToSparseNeighbors_Uncached()
    {
        return ExecuteDenseToSparseNeighborLookups(useCache: false);
    }

    [Benchmark(Description = "Dense to sparse conjoined neighbor lookups with cache")]
    [BenchmarkCategory("Sparse", "Neighbors")]
    public int ResolveDenseToSparseNeighbors_Cached()
    {
        return ExecuteDenseToSparseNeighborLookups(useCache: true);
    }

    private int ConstructGrid(GridConfiguration configuration, VoxelIndex[] configuredVoxels)
    {
        int configuredVoxelCount = 0;

        for (int i = 0; i < ConstructionRounds; i++)
        {
            bool added = configuredVoxels == null
                ? _world.TryAddGrid(configuration, out ushort gridIndex)
                : _world.TryAddGrid(configuration, configuredVoxels, out gridIndex);

            if (!added)
                throw new InvalidOperationException("Unable to allocate sparse benchmark grid.");

            configuredVoxelCount += _world.ActiveGrids[gridIndex].ConfiguredVoxelCount;

            if (!_world.TryRemoveGrid(gridIndex))
                throw new InvalidOperationException("Unable to release sparse benchmark grid.");
        }

        return configuredVoxelCount;
    }

    private void InitializeDenseGrid()
    {
        _world = BenchmarkEnvironment.PrepareWorld();

        if (!_world.TryAddGrid(_denseConfiguration, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate dense benchmark grid.");

        _grid = _world.ActiveGrids[gridIndex];
    }

    private void InitializeSparseGrid(VoxelIndex[] configuredVoxels)
    {
        _world = BenchmarkEnvironment.PrepareWorld();

        if (!_world.TryAddGrid(_sparseConfiguration, configuredVoxels, out ushort gridIndex))
            throw new InvalidOperationException("Unable to allocate sparse benchmark grid.");

        _grid = _world.ActiveGrids[gridIndex];
    }

    private int CountCoveredVoxels(Vector3d min, Vector3d max)
    {
        int count = 0;

        for (int i = 0; i < CoverageRounds; i++)
        {
            foreach (GridVoxelSet covered in GridTracer.GetCoveredVoxels(_world, min, max))
                count += covered.Voxels.Count;
        }

        return count;
    }

    private void RegisterOccupants()
    {
        for (int i = 0; i < _occupants.Length; i++)
        {
            if (!_grid.TryAddVoxelOccupant(_occupants[i]))
                throw new InvalidOperationException($"Unable to register sparse scan occupant {i}.");
        }
    }

    private int ExecuteSparseRadiusScanInto()
    {
        int count = 0;

        for (int i = 0; i < ScanRounds; i++)
        {
            GridScanManager.ScanRadiusInto(
                _world,
                new Vector3d(28, 0, 28),
                (Fixed64)18,
                _scanResults,
                _scanScratch);

            count += _scanResults.Count;
        }

        return count;
    }

    private void InitializeDenseSparseNeighborScenario(bool primeCache)
    {
        _world = BenchmarkEnvironment.PrepareWorld();

        GridConfiguration denseConfiguration = new(
            new Vector3d(0, 0, 0),
            new Vector3d(31, 0, 31),
            scanCellSize: 8);
        GridConfiguration sparseConfiguration = new(
            new Vector3d(31, 0, 0),
            new Vector3d(62, 0, 31),
            scanCellSize: 8,
            storageKind: GridStorageKind.Sparse);
        VoxelIndex[] sparseBoundaryVoxels = CreateSparseEastNeighborBoundary();

        if (!_world.TryAddGrid(denseConfiguration, out ushort denseGridIndex))
            throw new InvalidOperationException("Unable to allocate dense neighbor benchmark grid.");

        if (!_world.TryAddGrid(sparseConfiguration, sparseBoundaryVoxels, out _))
            throw new InvalidOperationException("Unable to allocate sparse neighbor benchmark grid.");

        _grid = _world.ActiveGrids[denseGridIndex];
        _neighborVoxels = CreateDenseEastBoundaryVoxels(_grid);

        if (primeCache)
            ExecuteDenseToSparseNeighborLookups(useCache: true);
    }

    private int ExecuteDenseToSparseNeighborLookups(bool useCache)
    {
        int hitCount = 0;

        for (int round = 0; round < NeighborRounds; round++)
        {
            for (int i = 0; i < _neighborVoxels.Length; i++)
            {
                if (_neighborVoxels[i].TryGetNeighborFromDirection(_grid, SpatialDirection.East, out _, useCache))
                    hitCount++;
            }
        }

        return hitCount;
    }

    private static VoxelIndex[] CreateStridePattern(int stride)
    {
        int countPerAxis = (GridSide + stride - 1) / stride;
        VoxelIndex[] voxels = new VoxelIndex[countPerAxis * countPerAxis];
        int index = 0;

        for (int x = 0; x < GridSide; x += stride)
        {
            for (int z = 0; z < GridSide; z += stride)
                voxels[index++] = new VoxelIndex(x, 0, z);
        }

        return voxels;
    }

    private static VoxelIndex[] CreateCheckerboardPattern()
    {
        VoxelIndex[] voxels = new VoxelIndex[(GridSide * GridSide) >> 1];
        int index = 0;

        for (int x = 0; x < GridSide; x++)
        {
            for (int z = 0; z < GridSide; z++)
            {
                if (((x + z) & 1) == 0)
                    voxels[index++] = new VoxelIndex(x, 0, z);
            }
        }

        return voxels;
    }

    private static VoxelIndex[] CreateCluster(int startX, int startZ, int width, int length)
    {
        VoxelIndex[] voxels = new VoxelIndex[width * length];
        int index = 0;

        for (int x = startX; x < startX + width; x++)
        {
            for (int z = startZ; z < startZ + length; z++)
                voxels[index++] = new VoxelIndex(x, 0, z);
        }

        return voxels;
    }

    private static VoxelIndex[] CreateLookupIndices()
    {
        VoxelIndex[] indices = new VoxelIndex[LookupCount];

        for (int i = 0; i < indices.Length; i++)
        {
            int x = (i * 37 + 11) & GridExtent;
            int z = (i * 53 + 7) & GridExtent;
            indices[i] = new VoxelIndex(x, 0, z);
        }

        return indices;
    }

    private static BenchmarkOccupant[] CreateOccupants(VoxelIndex[] configuredVoxels)
    {
        BenchmarkOccupant[] occupants = new BenchmarkOccupant[configuredVoxels.Length];

        for (int i = 0; i < configuredVoxels.Length; i++)
        {
            VoxelIndex voxelIndex = configuredVoxels[i];
            occupants[i] = new BenchmarkOccupant(
                new Vector3d(voxelIndex.x, voxelIndex.y, voxelIndex.z),
                (byte)(i & 7));
        }

        return occupants;
    }

    private static VoxelIndex[] CreateSparseEastNeighborBoundary()
    {
        VoxelIndex[] voxels = new VoxelIndex[32];

        for (int z = 0; z < voxels.Length; z++)
            voxels[z] = new VoxelIndex(1, 0, z);

        return voxels;
    }

    private static Voxel[] CreateDenseEastBoundaryVoxels(VoxelGrid denseGrid)
    {
        Voxel[] voxels = new Voxel[32];

        for (int z = 0; z < voxels.Length; z++)
        {
            if (!denseGrid.TryGetVoxel(new VoxelIndex(31, 0, z), out Voxel voxel))
                throw new InvalidOperationException($"Unable to resolve dense boundary voxel {z}.");

            voxels[z] = voxel;
        }

        return voxels;
    }
}
